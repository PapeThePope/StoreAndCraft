using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Scarecrow: stands in the centre cell of its planting grid (odd cols × rows, spacing — ZDO). When every
    /// planted spot is ripe it harvests the whole field (drops get its link → auto-intake stores them)
    /// and replants the empty, cultivated spots with seeds from (linked) chests.
    /// Event driven: Plant.Grow postfix, load / placement / setting change. Only a slow 60 s re-check
    /// while something is growing (crops grown on another client's machine). Only the ZDO owner with
    /// the local player in the scarecrow's range (ScarecrowRange, 0 = auto-fill range) works it; on/off = the station auto-fill flag ([B]).
    /// </summary>
    internal class Scarecrow : MonoBehaviour, Hoverable, Interactable
    {
        internal const string PrefabName = "sac_scarecrow";
        private const string KeyCols = "SAC_farmCols";
        private static readonly int KeyColsHash = KeyCols.GetStableHashCode();
        private const string KeyRows = "SAC_farmRows";
        private static readonly int KeyRowsHash = KeyRows.GetStableHashCode();
        private const string KeyCrop = "SAC_farmCrop";
        private static readonly int KeyCropHash = KeyCrop.GetStableHashCode();
        private const string KeyHarvest = "SAC_farmHarvest";
        private static readonly int KeyHarvestHash = KeyHarvest.GetStableHashCode();
        private const string KeyPlant = "SAC_farmPlant";
        private static readonly int KeyPlantHash = KeyPlant.GetStableHashCode();
        private const string KeyPlantAt = "SAC_farmPlantAt";     // world time (ms) when the next planting starts, 0 = none
        private static readonly int KeyPlantAtHash = KeyPlantAt.GetStableHashCode();
        private const string KeyPlantDone = "SAC_farmPlantDone"; // 1 = planted, waits for the next harvest
        private static readonly int KeyPlantDoneHash = KeyPlantDone.GetStableHashCode();
        internal const int MaxSize = 21; // odd: the scarecrow stands in the centre cell
        private const float RecheckGrowingSeconds = 60f;
        private const float RetryMissingSeconds = 30f;
        /// <summary>After a harvest the field rests this long before it is planted again (shown in the hover text).</summary>
        private const float PlantDelaySeconds = 30f;

        internal static readonly List<Scarecrow> Live = new List<Scarecrow>();

        private ZNetView _nv;
        private bool _dirty = true;
        private float _nextCheck;
        private float _notBefore;      // no planting before this time (wheel edits settle first)
        private int _hoverFrame = -10; // last frame the crosshair was on this scarecrow
        private bool _missingSeeds;
        // Grid spots that lost their cultivated ground get it back (paint only, never the height); a spot where that
        // does not work is given up after RecultivateMaxTries, so nothing is tried over and over.
        private const int RecultivateMaxTries = 2;
        private const float RecultivateRetrySeconds = 5f;
        private readonly Dictionary<long, int> _recultivateTries = new Dictionary<long, int>();
        private bool _recultivated;
        private GameObject _preview;
        private int _previewHash;

        internal bool MissingSeeds => _missingSeeds;

        // ---- settings (ZDO)

        private ZDO Zdo => _nv != null && _nv.IsValid() ? _nv.GetZDO() : null;
        internal int Cols => Odd(Zdo != null ? Zdo.GetInt(KeyColsHash, 5) : 5);
        internal int Rows => Odd(Zdo != null ? Zdo.GetInt(KeyRowsHash, 5) : 5);


        /// <summary>Odd size 1..MaxSize so there is a real centre cell (even values round up).</summary>
        internal static int Odd(int v)
        {
            v = Mathf.Clamp(v, 1, MaxSize);
            return v % 2 == 0 ? Mathf.Min(v + 1, MaxSize) : v;
        }
        /// <summary>The first crop of the bed (kept in sync with the palette; old scarecrows only have this one).</summary>
        internal string CropSeed => Zdo != null ? Zdo.GetString(KeyCropHash, "") : "";
        internal bool Harvest => Zdo == null || Zdo.GetBool(KeyHarvestHash, true);
        internal bool PlantOn => Zdo == null || Zdo.GetBool(KeyPlantHash, true);

        // ---- crops of the bed and the pattern (ZDO)
        // The palette is the crops picked for this bed (max MaxCrops, each gets a colour in the settings panel). The
        // pattern says which palette crop grows on which grid point. It is stored as a MaxSize x MaxSize canvas
        // (row-major, 'a' + palette index, '.' = left empty) with the grid in its middle, so changing the grid size
        // keeps the drawing in place. No pattern = the whole bed grows the first crop (every scarecrow before this).

        private const string KeyPalette = "SAC_farmPalette";
        private static readonly int KeyPaletteHash = KeyPalette.GetStableHashCode();
        private const string KeyPattern = "SAC_farmPattern";
        private static readonly int KeyPatternHash = KeyPattern.GetStableHashCode();
        internal const int MaxCrops = 8;
        private static readonly List<string> NoCrops = new List<string>();
        private string _palRaw, _palLegacy;
        private List<string> _palCache = NoCrops;

        /// <summary>Crop keys of the bed in palette order. Do not change the list.</summary>
        internal List<string> Palette()
        {
            ZDO z = Zdo;
            if (z == null)
                return NoCrops;
            string raw = z.GetString(KeyPaletteHash, "");
            string legacy = z.GetString(KeyCropHash, "");
            if (raw == _palRaw && legacy == _palLegacy)
                return _palCache;
            var list = new List<string>();
            if (!string.IsNullOrEmpty(raw))
            {
                string[] parts = raw.Split(';');
                for (int i = 0; i < parts.Length && list.Count < MaxCrops; i++)
                {
                    if (parts[i].Length > 0)
                        list.Add(parts[i]);
                }
            }
            else if (!string.IsNullOrEmpty(legacy))
            {
                list.Add(legacy);
            }
            _palRaw = raw;
            _palLegacy = legacy;
            _palCache = list;
            return list;
        }

        private int[] _cellsCache;
        private string _cellsPattern;
        private List<string> _cellsPalette;
        private int _cellsCols, _cellsRows;

        /// <summary>
        /// Per grid point (row-major): palette index of the crop that grows there, -1 = left empty. The same array
        /// comes back until the pattern, the crops or the size change (the hover text and the preview ask every
        /// frame): copy it before changing it.
        /// </summary>
        internal int[] CellIndices()
        {
            int cols = Cols, rows = Rows;
            List<string> palette = Palette();
            string pattern = Zdo != null ? Zdo.GetString(KeyPatternHash, "") : "";
            if (_cellsCache != null && pattern == _cellsPattern && ReferenceEquals(palette, _cellsPalette)
                && cols == _cellsCols && rows == _cellsRows)
                return _cellsCache;
            var cells = new int[cols * rows];
            int n = palette.Count;
            bool canvas = pattern.Length >= MaxSize * MaxSize;
            int offC = (MaxSize - cols) / 2, offR = (MaxSize - rows) / 2;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = n == 0 ? -1 : 0;
                    if (n > 0 && canvas)
                    {
                        idx = pattern[(r + offR) * MaxSize + c + offC] - 'a';
                        if (idx < 0 || idx >= n)
                            idx = -1;
                    }
                    cells[r * cols + c] = idx;
                }
            }
            _cellsCache = cells;
            _cellsPattern = pattern;
            _cellsPalette = palette;
            _cellsCols = cols;
            _cellsRows = rows;
            return cells;
        }

        /// <summary>Per grid point (row-major): the crop that grows there, null = left empty.</summary>
        internal CropMap.Crop[] CellCrops()
        {
            List<string> pal = Palette();
            int[] idx = CellIndices();
            var byIndex = new CropMap.Crop[pal.Count];
            for (int i = 0; i < pal.Count; i++)
                byIndex[i] = CropMap.ForSeed(pal[i]);
            var crops = new CropMap.Crop[idx.Length];
            for (int i = 0; i < idx.Length; i++)
                crops[i] = idx[i] >= 0 ? byIndex[idx[i]] : null;
            return crops;
        }

        /// <summary>Writes the pattern (row-major, palette index or -1) into the canvas, the rest of it stays.</summary>
        internal void SetCells(int[] cells)
        {
            int cols = Cols, rows = Rows;
            if (cells == null || cells.Length != cols * rows)
                return;
            string old = Zdo != null ? Zdo.GetString(KeyPatternHash, "") : "";
            char[] canvas;
            if (old.Length >= MaxSize * MaxSize)
            {
                canvas = old.Substring(0, MaxSize * MaxSize).ToCharArray();
            }
            else
            {
                canvas = new char[MaxSize * MaxSize];
                for (int i = 0; i < canvas.Length; i++)
                    canvas[i] = 'a';
            }
            int offC = (MaxSize - cols) / 2, offR = (MaxSize - rows) / 2;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = cells[r * cols + c];
                    canvas[(r + offR) * MaxSize + c + offC] = idx < 0 ? '.' : (char)('a' + idx);
                }
            }
            string text = new string(canvas);
            Write(z => z.Set(KeyPatternHash, text));
            ArmPlant(0f);
            _notBefore = Time.time + 1.5f; // plant only once the drawing has settled
        }

        /// <summary>Adds the crop to the bed, or removes it. A removed crop's points are left empty.</summary>
        internal void ToggleCrop(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            var old = new List<string>(Palette());
            int[] cells = CellIndices();
            var next = new List<string>(old);
            if (!next.Remove(key))
            {
                if (next.Count >= MaxCrops)
                {
                    if (Player.m_localPlayer != null)
                        Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
                            Loc.T("A scarecrow takes up to " + MaxCrops + " crops", "Eine Vogelscheuche nimmt höchstens " + MaxCrops + " Pflanzen"));
                    return;
                }
                next.Add(key);
            }
            var mapped = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                if (old.Count == 0)
                    mapped[i] = 0; // the first crop fills the whole bed, like before
                else
                    mapped[i] = cells[i] >= 0 && cells[i] < old.Count ? next.IndexOf(old[cells[i]]) : -1;
            }
            string palette = string.Join(";", next.ToArray());
            string first = next.Count > 0 ? next[0] : "";
            Write(z =>
            {
                z.Set(KeyPaletteHash, palette);
                z.Set(KeyCropHash, first);
            });
            SetCells(mapped);
        }

        private int[] _needCells;
        private string _needText = "0";

        /// <summary>What the whole bed takes: "Carrot seeds 12, Raspberries 60" (the middle point is the scarecrow's).</summary>
        internal string NeededText()
        {
            int[] shape = CellIndices();
            if (ReferenceEquals(shape, _needCells))
                return _needText; // nothing changed since the last time
            _needCells = shape;
            _needText = BuildNeededText();
            return _needText;
        }

        private string BuildNeededText()
        {
            CropMap.Crop[] crops = CellCrops();
            var counts = new Dictionary<CropMap.Crop, int>();
            var order = new List<CropMap.Crop>();
            int center = CenterIndex;
            for (int i = 0; i < crops.Length; i++)
            {
                if (i == center || crops[i] == null)
                    continue;
                int n;
                counts.TryGetValue(crops[i], out n);
                if (n == 0)
                    order.Add(crops[i]);
                counts[crops[i]] = n + Mathf.Max(1, crops[i].Cost);
            }
            if (order.Count == 0)
                return "0";
            var parts = new List<string>(order.Count);
            for (int i = 0; i < order.Count; i++)
                parts.Add(DisplayFilters.ItemLabel(order[i].Seed) + " " + counts[order[i]]);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>Fixed spacing (1.1 m), more only when the crop needs it (else it would not grow).</summary>
        internal float Spacing
        {
            get { return Mathf.Max(DefaultSpacing, MinSpacing()); }
        }

        private const float DefaultSpacing = 1.1f;

        /// <summary>One grid for the whole bed: the biggest plant in it sets the spacing.</summary>
        internal float MinSpacing()
        {
            float r = 0.5f;
            List<string> pal = Palette();
            for (int i = 0; i < pal.Count; i++)
            {
                CropMap.Crop crop = CropMap.ForSeed(pal[i]);
                if (crop != null && crop.GrowRadius > r)
                    r = crop.GrowRadius;
            }
            return Mathf.Max(0.5f, Mathf.Ceil(r * 2f * 10f) / 10f + 0.05f);
        }

        internal void SetInt(string key, int v) => Write(z => z.Set(key, v));
        internal void SetCols(int v) { SetInt(KeyCols, Odd(v)); ArmPlant(0f); }
        internal void SetRows(int v) { SetInt(KeyRows, Odd(v)); ArmPlant(0f); }
        internal void SetHarvest(bool on) => Write(z => z.Set(KeyHarvestHash, on));
        internal void SetPlant(bool on)
        {
            Write(z => z.Set(KeyPlantHash, on));
            if (on)
                ArmPlant(PlantDelaySeconds); // like after a harvest: counts down from 30 s, the hover text shows it
        }

        private static double NowSeconds()
        {
            return ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;
        }

        /// <summary>Plant again in <paramref name="delay"/> seconds, and only once: it is switched off again after a pass.</summary>
        internal void ArmPlant(float delay)
        {
            if (_nv == null || !_nv.IsValid())
                return;
            if (!_nv.IsOwner())
                _nv.ClaimOwnership();
            ZDO z = _nv.GetZDO();
            z.Set(KeyPlantAtHash, (long)((NowSeconds() + delay) * 1000.0));
            z.Set(KeyPlantDoneHash, 0);
            MarkDirty();
        }

        /// <summary>Seconds until the planting starts, or -1 when none is waiting.</summary>
        internal float PlantInSeconds()
        {
            ZDO z = Zdo;
            if (z == null || !PlantOn)
                return -1f;
            long at = z.GetLong(KeyPlantAtHash, 0L);
            return at <= 0L ? -1f : Mathf.Max(0f, (float)(at / 1000.0 - NowSeconds()));
        }

        private void Write(System.Action<ZDO> set)
        {
            if (_nv == null || !_nv.IsValid())
                return;
            if (!_nv.IsOwner())
                _nv.ClaimOwnership();
            set(_nv.GetZDO());
            MarkDirty();
        }

        internal void MarkDirty()
        {
            _dirty = true;
            _nextCheck = 0f;
        }

        /// <summary>
        /// Check again in a moment: a plant that just grew is replaced by the ripe crop, but the old plant object is
        /// only removed at the end of that frame - an immediate scan still counted it as "growing" and then waited
        /// a whole minute for the next check.
        /// </summary>
        internal void MarkDirtyAfter(float seconds)
        {
            _notBefore = Mathf.Max(_notBefore, Time.time + seconds);
            MarkDirty();
        }

        // ---- lifecycle

        private void Awake()
        {
            _nv = GetComponent<ZNetView>();
        }

        private void OnEnable()
        {
            if (!Live.Contains(this))
                Live.Add(this);
        }

        private void OnDisable()
        {
            Live.Remove(this);
        }

        private void Start()
        {
            // New scarecrow: working by default (same flag as [B] auto-fill).
            if (_nv != null && _nv.IsValid() && _nv.IsOwner() && _nv.GetZDO().GetInt(StationAutoFill.ZdoKeyHash, -1) < 0)
                _nv.GetZDO().Set(StationAutoFill.ZdoKeyHash, 1);
            MarkDirty();
        }

        private float _noticeNext;

        private void Update()
        {
            bool ghost = _nv == null || _nv.GetZDO() == null;
            bool hovered = Time.frameCount - _hoverFrame <= 2;
            UpdatePreview(ghost || hovered || StationFilterMenu.IsEditing(this));
            if (ghost)
                return;
            if (Time.time >= _noticeNext)
            {
                // No chest with its link / none in reach (seeds in, harvest out): notice above the scarecrow.
                _noticeNext = Time.time + 2.5f;
                if (_nv.IsValid() && _nv.IsOwner() && StationAutoFill.IsOn(_nv))
                    StationChestFull.TickOutput(this, null, 0, 6f, false);
            }
            if (hovered)
                HandleWheel();
            if (Time.time < _notBefore)
                return;
            if (!_dirty && Time.time < _nextCheck)
                return;
            if (!CanWork())
            {
                _nextCheck = Time.time + 5f;
                return;
            }
            _dirty = false;
            _nextCheck = float.MaxValue;
            Work();
        }

        /// <summary>
        /// Looking at the scarecrow: Shift + wheel = width, Ctrl + wheel = length.
        /// The wheel is taken from the camera zoom while one of those keys is held (see ScarecrowWheelPatch).
        /// </summary>
        private void HandleWheel()
        {
            bool shift = ZInput.GetKey(KeyCode.LeftShift, true) || ZInput.GetKey(KeyCode.RightShift, true);
            bool ctrl = ZInput.GetKey(KeyCode.LeftControl, true) || ZInput.GetKey(KeyCode.RightControl, true);
            if (!shift && !ctrl)
                return;
            if (InventoryGui.IsVisible() || StationFilterMenu.IsOpen || Player.m_localPlayer == null)
                return;
            if (_nv == null || !_nv.IsValid() || !PrivateArea.CheckAccess(transform.position, 0f, false, false))
                return;

            WheelGrabFrame = Time.frameCount;
            ZInput.GetMouseScrollWheel(); // lets the patch record the raw wheel value this frame
            float w = WheelRaw;
            if (Mathf.Abs(w) < 0.01f)
                return;
            int dir = w > 0f ? 1 : -1;
            if (shift)
                SetCols(Cols + 2 * dir);
            else
                SetRows(Rows + 2 * dir);
            _notBefore = Time.time + 1.5f; // plant only once the size has settled
        }

        // Harvest in progress: drops of pickables inside the grid until this time are put into chests.
        internal float HarvestUntil;

        /// <summary>The scarecrow that is harvesting right now and whose grid holds this position, or null.</summary>
        internal static Scarecrow HarvestingAt(Vector3 pos)
        {
            for (int i = 0; i < Live.Count; i++)
            {
                Scarecrow s = Live[i];
                if (s != null && Time.time <= s.HarvestUntil && s.Contains(pos))
                    return s;
            }
            return null;
        }

        // Mouse wheel hand-over (written by ScarecrowWheelPatch).
        internal static float WheelRaw;
        internal static int WheelGrabFrame = -10;

        private bool CanWork()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !_nv.IsValid() || !_nv.IsOwner() || Plugin.Settings == null)
                return false;
            if (!StationAutoFill.IsOn(_nv))
                return false;
            float range = Plugin.Settings.ScarecrowPlayerReach();
            return ContainerFilter.SqrDistance(player.transform.position, transform.position) <= range * range;
        }

        // ---- grid

        /// <summary>Index of the centre cell (the scarecrow's own spot, never planted).</summary>
        internal int CenterIndex => (Rows / 2) * Cols + Cols / 2;

        /// <summary>World positions of the grid points (ground height), row-major, centred on the scarecrow.</summary>
        internal List<Vector3> Points()
        {
            var pts = new List<Vector3>(Cols * Rows);
            float sp = Spacing;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 start = transform.position - right * ((Cols - 1) * sp * 0.5f) - fwd * ((Rows - 1) * sp * 0.5f);
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    Vector3 p = start + right * (c * sp) + fwd * (r * sp);
                    if (ZoneSystem.instance != null)
                        p.y = ZoneSystem.instance.GetGroundHeight(p);
                    pts.Add(p);
                }
            }
            return pts;
        }

        /// <summary>
        /// Is the position within half a spacing of a grid point (height ignored)? Worked out from the position on the
        /// grid's two axes: no point list and no ground heights (plants ask this every health tick).
        /// </summary>
        internal bool Contains(Vector3 pos)
        {
            float sp = Spacing;
            int cols = Cols, rows = Rows;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 d = pos - transform.position;
            d.y = 0f;
            // Same layout as Points(): the first point lies (cols - 1) / 2 spacings to the left and behind the scarecrow.
            float x = Vector3.Dot(d, right) + (cols - 1) * sp * 0.5f;
            float z = Vector3.Dot(d, fwd) + (rows - 1) * sp * 0.5f;
            int c = Mathf.Clamp(Mathf.RoundToInt(x / sp), 0, cols - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt(z / sp), 0, rows - 1);
            float dx = x - c * sp;
            float dz = z - r * sp;
            float tol = sp * 0.5f;
            return dx * dx + dz * dz <= tol * tol;
        }

        // ---- work

        private enum Spot { Empty, Growing, Ripe, Blocked }

        private void Work()
        {
            if (Palette().Count == 0)
                return;
            CropMap.Crop[] cellCrop = CellCrops(); // null = the pattern leaves this point empty
            Player player = Player.m_localPlayer;
            List<Vector3> pts = Points();
            var state = new Spot[pts.Count];
            var ripe = new Pickable[pts.Count];
            var cropAt = new CropMap.Crop[pts.Count]; // the crop of what stands there (sapling / ripe plant)
            Scan(pts, state, ripe, cropAt);
            state[CenterIndex] = Spot.Blocked; // the scarecrow stands here
            ripe[CenterIndex] = null;
            for (int i = 0; i < state.Length; i++)
            {
                // Points the pattern leaves empty are never planted (a plant that stands there still gets harvested).
                if (cellCrop[i] == null && state[i] == Spot.Empty)
                    state[i] = Spot.Blocked;
            }

            // Per kind of plant: it is harvested once every plant of that kind is ripe (a bed of slow carrots does not
            // hold back the berries).
            var growingBy = new Dictionary<string, int>();
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] != Spot.Growing)
                    continue;
                string k = cropAt[i] != null ? cropAt[i].Key : "";
                int n;
                growingBy.TryGetValue(k, out n);
                growingBy[k] = n + 1;
            }

            int link = StationLink.Get(this);
            // A new scarecrow plants its field once, right away.
            if (Zdo != null && Zdo.GetInt(KeyPlantDoneHash, -1) < 0 && Zdo.GetLong(KeyPlantAtHash, 0L) == 0L)
                ArmPlant(0f);
            if (Harvest)
            {
                var harvested = new Dictionary<CropMap.Crop, int>();
                for (int i = 0; i < ripe.Length; i++)
                {
                    if (ripe[i] == null || cropAt[i] == null)
                        continue;
                    int g;
                    growingBy.TryGetValue(cropAt[i].Key, out g);
                    if (g > 0)
                        continue; // this kind is not all ripe yet
                    ZNetView pnv = ripe[i].GetComponent<ZNetView>();
                    if (pnv == null || !pnv.IsValid())
                        continue;
                    if (!pnv.IsOwner())
                        pnv.ClaimOwnership();
                    // The drops of these pickables go straight into chests (see ScarecrowHarvestDropPatch).
                    HarvestUntil = Time.time + 3f;
                    pnv.InvokeRPC("RPC_Pick", 0);
                    // A bush that grows its berries back stays in the ground: nothing to plant, wait for it.
                    state[i] = ripe[i].m_respawnTimeMinutes > 0f ? Spot.Growing : Spot.Empty;
                    int h;
                    harvested.TryGetValue(cropAt[i], out h);
                    harvested[cropAt[i]] = h + 1;
                }
                if (harvested.Count > 0)
                {
                    foreach (KeyValuePair<CropMap.Crop, int> kv in harvested)
                        ActivityLog.Note(Label(), Loc.T("Harvested ", "Geerntet: ") + kv.Value + " " + DisplayFilters.ItemLabel(kv.Key.Seed));
                    ArmPlant(PlantDelaySeconds); // the field rests first; the hover text counts down
                }
            }

            int growing = 0;
            float inSeconds = PlantInSeconds();
            if (inSeconds == 0f) // armed and due (-1 = nothing waiting, > 0 = still counting down)
            {
                PlantEmpty(player, cellCrop, pts, state, link);
                if (_missingSeeds)
                {
                    ArmPlant(PlantDelaySeconds); // not enough seeds: try again later, visible in the hover text
                }
                else if (_recultivated)
                {
                    ArmPlant(RecultivateRetrySeconds); // ground cultivated again: plant those spots in a moment
                }
                else
                {
                    Zdo.Set(KeyPlantAtHash, 0L); // planted: nothing more until the next harvest
                    Zdo.Set(KeyPlantDoneHash, 1);
                }
            }

            growing = 0;
            int empty = 0;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] == Spot.Growing) growing++;
                else if (state[i] == Spot.Empty) empty++;
            }
            // Crops on another client's machine do not call our Grow hook → slow re-check.
            if (growing > 0)
                _nextCheck = Time.time + RecheckGrowingSeconds;
            // Missing seeds, or spots still empty (ground not cultivated yet / plant switched off):
            // look again later instead of waiting for a menu change.
            // Planting is waiting for its timer: look again when it is due.
            float wait = PlantInSeconds();
            if (wait > 0f)
                _nextCheck = Mathf.Min(_nextCheck, Time.time + wait + 0.25f);
        }

        // ---- time until the harvest (hover text)

        private static readonly System.Reflection.MethodInfo PlantGrowTime = AccessTools.Method(typeof(Plant), "GetGrowTime");
        private float _etaAt = -10f;
        private float _eta = -1f;

        /// <summary>
        /// Seconds until the last healthy plant of the grid is grown (the field is harvested then), -1 when nothing
        /// is growing. The plant's own grow time (random per plant) minus the time since it was planted.
        /// Counted at most once a second: the hover text asks every frame.
        /// </summary>
        internal float HarvestInSeconds()
        {
            if (Time.time - _etaAt < 1f)
                return _eta;
            _etaAt = Time.time;
            _eta = -1f;
            if (PlantGrowTime == null || ZNet.instance == null || Zdo == null)
                return _eta;

            float sp = Spacing;
            Vector3 half = new Vector3(Cols * sp * 0.5f + 0.5f, 3f, Rows * sp * 0.5f + 0.5f);
            Collider[] hits = Physics.OverlapBox(transform.position, half, Quaternion.LookRotation(Flat(transform.forward)));
            var seen = new HashSet<int>();
            System.DateTime now = ZNet.instance.GetTime();
            float max = -1f;
            for (int h = 0; h < hits.Length; h++)
            {
                Pickable bush = hits[h].GetComponentInParent<Pickable>();
                if (bush != null && bush.GetPicked() && bush.m_respawnTimeMinutes > 0f && seen.Add(bush.GetInstanceID()))
                {
                    // Berry bush picked earlier: its respawn time minus the time since it was picked.
                    ZNetView bnv = bush.GetComponent<ZNetView>();
                    long picked = bnv != null && bnv.IsValid() ? bnv.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L) : 0L;
                    if (picked > 1L)
                    {
                        float leftBush = bush.m_respawnTimeMinutes * 60f - (float)(now - new System.DateTime(picked)).TotalSeconds;
                        if (leftBush > max)
                            max = leftBush;
                    }
                    continue;
                }
                Plant plant = hits[h].GetComponentInParent<Plant>();
                if (plant == null || !seen.Add(plant.GetInstanceID()) || plant.GetStatus() != Plant.Status.Healthy)
                    continue;
                ZNetView pnv = plant.GetComponent<ZNetView>();
                if (pnv == null || !pnv.IsValid())
                    continue;
                long planted = pnv.GetZDO().GetLong(ZDOVars.s_plantTime, 0L);
                if (planted == 0L)
                    continue;
                try
                {
                    float growTime = FastCall.Float(PlantGrowTime, plant);
                    float left = growTime - (float)(now - new System.DateTime(planted)).TotalSeconds;
                    if (left > max)
                        max = left;
                }
                catch (System.Exception)
                {
                    return _eta;
                }
            }
            _eta = max;
            return _eta;
        }

        private static string DurationText(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            int m = total / 60;
            int s = total % 60;
            return m > 0 ? m + " min " + s + " s" : s + " s";
        }

        private void Scan(List<Vector3> pts, Spot[] state, Pickable[] ripe, CropMap.Crop[] cropAt)
        {
            float sp = Spacing;
            float tol = sp * 0.45f;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
                center += pts[i];
            center /= Mathf.Max(1, pts.Count);
            Vector3 half = new Vector3(Cols * sp * 0.5f + 0.5f, 3f, Rows * sp * 0.5f + 0.5f);
            // Only layers that plants / pickables use: the box no longer returns every wall, floor, beam and rock
            // collider of the base (each cost two GetComponentInParent calls). Buffer reused; a full buffer falls
            // back to the allocating query so nothing is ever missed.
            Quaternion rot = Quaternion.LookRotation(Flat(transform.forward));
            int mask = PlantMask();
            Collider[] hits = ScanHits;
            int count = Physics.OverlapBoxNonAlloc(center, half, hits, rot, mask);
            if (count >= hits.Length)
            {
                hits = Physics.OverlapBox(center, half, rot, mask);
                count = hits.Length;
            }
            HashSet<int> seen = ScanSeen;
            seen.Clear();
            for (int h = 0; h < count; h++)
            {
                Plant plant = hits[h].GetComponentInParent<Plant>();
                Pickable pick = plant == null ? hits[h].GetComponentInParent<Pickable>() : null;
                Component c = (Component)plant ?? pick;
                if (c == null || !seen.Add(c.GetInstanceID()))
                    continue;
                int idx = GridIndex(c.transform.position, tol);
                if (idx < 0)
                    continue;
                if (plant != null)
                {
                    // The ripe crop is already there (the grown plant is only removed at the end of the frame).
                    if (state[idx] == Spot.Ripe)
                        continue;
                    // A plant that "needs more room" never grows: it must not hold the harvest back for ever.
                    state[idx] = plant.GetStatus() == Plant.Status.NoSpace ? Spot.Blocked : Spot.Growing;
                    cropAt[idx] = CropMap.ForSapling(plant.gameObject);
                }
                // Any ripe field crop counts, not only the selected one: switching the crop while the old one is still
                // in the ground must not leave it unharvested (it would block the field for ever).
                else if (!pick.GetPicked() && CropMap.ForRipe(pick.gameObject) != null)
                {
                    if (state[idx] == Spot.Empty || state[idx] == Spot.Growing)
                    {
                        state[idx] = Spot.Ripe;
                        ripe[idx] = pick;
                        cropAt[idx] = CropMap.ForRipe(pick.gameObject);
                    }
                }
                else if (pick.GetPicked() && pick.m_respawnTimeMinutes > 0f && CropMap.ForRipe(pick.gameObject) != null)
                {
                    // Picked bush that grows its berries back: not empty, it is growing again.
                    if (state[idx] == Spot.Empty)
                    {
                        state[idx] = Spot.Growing;
                        cropAt[idx] = CropMap.ForRipe(pick.gameObject);
                    }
                }
                else if (state[idx] == Spot.Empty)
                    state[idx] = Spot.Blocked;
            }
        }

        private static int _spaceMask;
        private static readonly Collider[] SpaceHits = new Collider[24];

        /// <summary>Plant.HaveGrowSpace for a spot: anything but a sick plant within the grow radius blocks.</summary>
        private bool HasGrowSpace(Vector3 pos, float radius)
        {
            if (_spaceMask == 0)
                _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            int n = Physics.OverlapSphereNonAlloc(pos, Mathf.Max(0.05f, radius), SpaceHits, _spaceMask);
            for (int i = 0; i < n; i++)
            {
                Collider c = SpaceHits[i];
                if (c == null || c.transform.IsChildOf(transform))
                    continue; // the scarecrow itself
                // Just harvested: the picked crop is only removed at the end of the frame, its collider is still here.
                Pickable picked = c.GetComponentInParent<Pickable>();
                if (picked != null && picked.GetPicked() && picked.m_respawnTimeMinutes <= 0f)
                    continue; // a bush that regrows stays in the ground and blocks the spot
                Plant plant = c.GetComponent<Plant>();
                if (plant == null || plant.GetStatus() == Plant.Status.Healthy)
                    return false;
            }
            return true;
        }

        /// <summary>Plants every empty point with the crop the pattern gives it.</summary>
        private void PlantEmpty(Player player, CropMap.Crop[] cellCrop, List<Vector3> pts, Spot[] state, int link)
        {
            var groups = new List<CropMap.Crop>();
            var targetsBy = new Dictionary<CropMap.Crop, List<int>>();
            _recultivated = false;
            int recultivated = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                CropMap.Crop crop = cellCrop[i];
                if (state[i] != Spot.Empty || crop == null)
                    continue;
                if (crop.NeedCultivated)
                {
                    Heightmap hm = Heightmap.FindHeightmap(pts[i]);
                    if (hm == null)
                        continue;
                    if (!hm.IsCultivated(pts[i]))
                    {
                        // The heightmap shows new paint only a moment later: this spot is planted in the next pass.
                        if (TryRecultivate(pts[i]))
                            recultivated++;
                        continue;
                    }
                    if (_recultivateTries.Count > 0)
                        _recultivateTries.Remove(SpotKey(pts[i])); // cultivated (again): a later loss may be fixed too
                }
                // Same room check as the plant itself: no seed is spent where it could never grow
                // (rock, tree, building piece or an older plant too close).
                if (!HasGrowSpace(pts[i], crop.GrowRadius))
                {
                    state[i] = Spot.Blocked;
                    continue;
                }
                List<int> list;
                if (!targetsBy.TryGetValue(crop, out list))
                {
                    list = new List<int>();
                    targetsBy[crop] = list;
                    groups.Add(crop);
                }
                list.Add(i);
            }
            if (recultivated > 0)
            {
                _recultivated = true;
                ActivityLog.Note(Label(), Loc.T("Ground cultivated again: ", "Boden neu bestellt: ") + recultivated);
            }
            var missing = new List<CropMap.Crop>();
            for (int g = 0; g < groups.Count; g++)
            {
                if (!PlantGroup(player, groups[g], targetsBy[groups[g]], pts, state, link))
                    missing.Add(groups[g]);
            }
            SetMissing(missing);
        }

        /// <summary>Plants one kind of crop on its empty points. False when the chests did not have seeds for all of them.</summary>
        private bool PlantGroup(Player player, CropMap.Crop crop, List<int> targets, List<Vector3> pts, Spot[] state, int link)
        {
            int got; // saplings that can be planted
            int cost = Mathf.Max(1, crop.Cost);
            StationFeed.PullOriginOverride = transform.position;
            StationFeed.PullRangeOverride = Plugin.Settings.ScarecrowChestReach();
            try
            {
                if (cost == 1)
                {
                    got = StationFeed.ConsumeFromChests(player, crop.Seed, targets.Count, link);
                }
                else
                {
                    // A sapling that costs several (berry bush: 5): only take what whole saplings can be made of.
                    int have = RequirementBridge.CountNearby(player, crop.Seed, link, -1);
                    int afford = Mathf.Min(targets.Count, have / cost);
                    got = afford > 0 ? StationFeed.ConsumeFromChests(player, crop.Seed, afford * cost, link) / cost : 0;
                    NearbyIndex.InvalidateCounts();
                }
            }
            finally
            {
                StationFeed.PullOriginOverride = null;
                StationFeed.PullRangeOverride = 0f;
            }

            for (int k = 0; k < got && k < targets.Count; k++)
            {
                int i = targets[k];
                GameObject go = Instantiate(crop.Sapling, pts[i], Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                Piece piece = go.GetComponent<Piece>();
                if (piece != null)
                {
                    SetCreator(piece, player);
                    piece.m_placeEffect?.Create(pts[i], go.transform.rotation, go.transform);
                }
                state[i] = Spot.Growing;
            }
            if (got > 0)
                ActivityLog.FromChest(Label(), got * cost, DisplayFilters.ItemLabel(crop.Seed));
            return got >= targets.Count;
        }

        private static System.Reflection.MethodInfo _setCreator;
        private static object _platformUser;
        private static bool _creatorResolved;

        /// <summary>
        /// Piece.SetCreator(playerId, PlatformUserID) like Player.PlacePiece. PlatformUserID lives in the
        /// Splatform assembly (not referenced) → reflection: PlatformManager.DistributionPlatform
        /// .LocalUser.PlatformUserID.
        /// </summary>
        private static void SetCreator(Piece piece, Player player)
        {
            try
            {
                if (!_creatorResolved)
                {
                    _creatorResolved = true;
                    System.Type pm = AccessTools.TypeByName("PlatformManager");
                    object platform = pm != null ? AccessTools.Property(pm, "DistributionPlatform")?.GetValue(null, null) : null;
                    object user = platform != null ? AccessTools.Property(platform.GetType(), "LocalUser")?.GetValue(platform, null) : null;
                    _platformUser = user != null ? AccessTools.Property(user.GetType(), "PlatformUserID")?.GetValue(user, null) : null;
                    _setCreator = AccessTools.Method(typeof(Piece), "SetCreator");
                }
                if (_setCreator != null && _platformUser != null)
                    _setCreator.Invoke(piece, new object[] { player.GetPlayerID(), _platformUser });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Scarecrow SetCreator: " + ex.Message);
            }
        }

        private void SetMissing(List<CropMap.Crop> missing)
        {
            _missingSeeds = missing.Count > 0;
            if (missing.Count > 0)
            {
                var names = new List<string>(missing.Count);
                for (int i = 0; i < missing.Count; i++)
                    names.Add(DisplayFilters.ItemLabel(missing[i].Seed));
                StationMissingLabel.SetMissing(this, "seed", string.Join(", ", names.ToArray()));
            }
            else
            {
                StationMissingLabel.ClearMissing(this, "seed");
            }
        }

        private static readonly Collider[] ScanHits = new Collider[512];
        private static readonly HashSet<int> ScanSeen = new HashSet<int>();
        private static int _plantMask;
        private static ZNetScene _plantMaskScene;

        /// <summary>
        /// Every layer a collider of a Plant or Pickable prefab uses (all spawnable objects are ZNetScene prefabs; the
        /// mod's saplings are copies of vanilla saplings). Read once from the prefabs, not guessed; ~0 if none found.
        /// </summary>
        private static int PlantMask()
        {
            ZNetScene scene = ZNetScene.instance;
            if (_plantMask != 0 && _plantMaskScene == scene)
                return _plantMask;
            int mask = 0;
            if (scene != null && scene.m_prefabs != null)
            {
                foreach (GameObject go in scene.m_prefabs)
                {
                    if (go == null || (go.GetComponent<Plant>() == null && go.GetComponent<Pickable>() == null))
                        continue;
                    foreach (Collider col in go.GetComponentsInChildren<Collider>(true))
                        mask |= 1 << col.gameObject.layer;
                }
            }
            foreach (CropMap.Crop crop in CropMap.All())
            {
                if (crop.Sapling == null)
                    continue;
                foreach (Collider col in crop.Sapling.GetComponentsInChildren<Collider>(true))
                    mask |= 1 << col.gameObject.layer;
            }
            _plantMask = mask != 0 ? mask : ~0;
            _plantMaskScene = scene;
            return _plantMask;
        }

        /// <summary>
        /// Grid point within tol of the position (height ignored), -1 if none: the same answer as searching the
        /// point list for the nearest one (points are a spacing apart, tol is under half of it), worked out from the
        /// position on the grid's axes like Contains.
        /// </summary>
        private int GridIndex(Vector3 pos, float tol)
        {
            float sp = Spacing;
            int cols = Cols, rows = Rows;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 d = pos - transform.position;
            d.y = 0f;
            float x = Vector3.Dot(d, right) + (cols - 1) * sp * 0.5f;
            float z = Vector3.Dot(d, fwd) + (rows - 1) * sp * 0.5f;
            int c = Mathf.Clamp(Mathf.RoundToInt(x / sp), 0, cols - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt(z / sp), 0, rows - 1);
            float dx = x - c * sp;
            float dz = z - r * sp;
            return dx * dx + dz * dz <= tol * tol ? r * cols + c : -1;
        }

        // ---- terrain (buttons in the settings panel; by itself only TryRecultivate: paint, never the height)

        internal bool TerrainBusy { get; private set; }

        /// <summary>
        /// Level the grid to the ground height at the scarecrow, or cultivate it like the Cultivator does.
        /// Uses SacTerrainOps (own TerrainOps, registered on every client) so every client applies the same
        /// operation. One pass per button press, spread over some frames.
        /// </summary>
        internal void StartTerrain(bool level)
        {
            if (TerrainBusy)
                return;
            Player player = Player.m_localPlayer;
            if (player == null || _nv == null || !_nv.IsValid())
                return;
            float half = Mathf.Max(Cols, Rows) * Spacing * 0.75f;
            if (!PrivateArea.CheckAccess(transform.position, half, true, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return;
            }
            _recultivateTries.Clear(); // after the buttons every spot may be cultivated again by itself
            StartCoroutine(TerrainRoutine(level));
        }

        private IEnumerator TerrainRoutine(bool level)
        {
            TerrainBusy = true;
            Player player = Player.m_localPlayer;
            GameObject prefab = level ? SacTerrainOps.LevelPrefab : SacTerrainOps.CultivatePrefab;
            SacTerrainOps.Register(ObjectDB.instance); // normally done by the UpdateRegisters patch
            if (prefab == null || ZoneSystem.instance == null)
            {
                Plugin.Log.LogWarning("StoreAndCraft scarecrow: terrain operation not available.");
                TerrainBusy = false;
                yield break;
            }

            // The terrain is a grid of height points exactly 1 m apart. One operation = exactly one height point
            // (level) / one paint point, placed on a whole-metre lattice, so no point is ever touched by two
            // operations at once: an overlapping operation reads the not yet updated height and adds its change
            // twice, which made the spikes. The area is the planted grid plus one point around it.
            const float step = 1f;
            float hw = (Cols - 1) * Spacing * 0.5f + 1f;
            float hl = (Rows - 1) * Spacing * 0.5f + 1f;
            Vector3 c0 = transform.position;
            Vector3 fwd = Flat(transform.forward);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float cx = Mathf.Round(c0.x);
            float cz = Mathf.Round(c0.z);
            int n = Mathf.CeilToInt(Mathf.Max(hw, hl) * 1.5f / step) + 1;

            if (player != null)
                player.Message(MessageHud.MessageType.TopLeft,
                    level ? Loc.T("Levelling the field...", "Feld wird eingeebnet...")
                          : Loc.T("Cultivating the field...", "Feld wird bestellt..."), 0, null, false);

            int done = 0;
            for (int ix = -n; ix <= n; ix++)
            {
                for (int iz = -n; iz <= n; iz++)
                {
                    Vector3 p = new Vector3(cx + ix * step, c0.y, cz + iz * step); // c0.y: the scarecrow's base
                    Vector3 d = new Vector3(p.x - c0.x, 0f, p.z - c0.z);
                    if (Mathf.Abs(Vector3.Dot(d, right)) > hw || Mathf.Abs(Vector3.Dot(d, fwd)) > hl)
                        continue;
                    if (!PrivateArea.CheckAccess(p, 0f, false, false))
                        continue;
                    PlaceTerrainOp(prefab, p);
                    if (++done % 8 == 0)
                        yield return null;
                }
            }

            ActivityLog.Note(Label(), level ? Loc.T("Field levelled", "Feld eingeebnet") : Loc.T("Field cultivated", "Feld bestellt"));
            if (player != null)
                player.Message(MessageHud.MessageType.TopLeft,
                    level ? Loc.T("Field levelled", "Feld eingeebnet") : Loc.T("Field cultivated", "Feld bestellt"), 0, null, false);
            MarkDirty(); // cultivated now: plant right away
            // The heightmap needs a moment to take the new heights: pressing again at once would read old heights.
            yield return new WaitForSeconds(2f);
            TerrainBusy = false;
        }

        /// <summary>One terrain operation: the clone of the inactive template runs its Awake on SetActive and destroys itself.</summary>
        private static void PlaceTerrainOp(GameObject prefab, Vector3 pos)
        {
            GameObject go = Instantiate(prefab, pos, Quaternion.identity);
            go.hideFlags = HideFlags.None;
            go.SetActive(true);
        }

        /// <summary>
        /// A grid spot lost its cultivated ground (it happens now and then, the terrain change of that area is lost):
        /// cultivate it again, paint only, never the height. The four terrain points around the spot, like the
        /// Cultivate button. False when not tried: ward, button busy, or this spot already failed RecultivateMaxTries
        /// times (something there keeps it from being cultivated: given up until the Cultivate button is pressed).
        /// </summary>
        private bool TryRecultivate(Vector3 spot)
        {
            if (TerrainBusy || ZoneSystem.instance == null)
                return false;
            long key = SpotKey(spot);
            int tries;
            _recultivateTries.TryGetValue(key, out tries);
            if (tries >= RecultivateMaxTries)
                return false;
            _recultivateTries[key] = tries + 1;
            if (!PrivateArea.CheckAccess(spot, 0f, false, false))
                return false;
            GameObject prefab = SacTerrainOps.CultivatePrefab;
            if (prefab == null)
                return false;
            SacTerrainOps.Register(ObjectDB.instance);
            float x0 = Mathf.Floor(spot.x), z0 = Mathf.Floor(spot.z);
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 1; dz++)
                    PlaceTerrainOp(prefab, new Vector3(x0 + dx, spot.y, z0 + dz));
            }
            return true;
        }

        private static long SpotKey(Vector3 spot)
        {
            return ((long)Mathf.RoundToInt(spot.x * 4f) << 32) ^ (uint)Mathf.RoundToInt(spot.z * 4f);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.001f ? Vector3.forward : v.normalized;
        }

        private string Label()
        {
            return Loc.T("Scarecrow", "Vogelscheuche");
        }

        // ---- grid preview (ghost + open menu only)

        private void UpdatePreview(bool show)
        {
            if (!show)
            {
                if (_preview != null && _preview.activeSelf)
                    _preview.SetActive(false);
                return;
            }
            int hash = Cols * 7919 ^ Rows * 104729 ^ Spacing.GetHashCode()
                ^ transform.position.GetHashCode() ^ transform.rotation.GetHashCode();
            int[] shape = CellIndices();
            for (int i = 0; i < shape.Length; i++)
                hash = hash * 31 + shape[i];
            if (_preview == null)
            {
                _preview = new GameObject("SacFarmGrid");
                _previewHash = 0;
            }
            if (!_preview.activeSelf)
                _preview.SetActive(true);
            if (hash == _previewHash)
                return;
            _previewHash = hash;
            BuildPreview();
        }

        private void BuildPreview()
        {
            for (int i = _preview.transform.childCount - 1; i >= 0; i--)
                Destroy(_preview.transform.GetChild(i).gameObject);
            List<Vector3> pts = Points();
            int cols = Cols, rows = Rows;
            Material mat = PreviewMaterial();
            // Row lines and column lines through the planting spots, 5 cm above ground.
            for (int r = 0; r < rows; r++)
            {
                var line = new List<Vector3>();
                for (int c = 0; c < cols; c++)
                    line.Add(pts[r * cols + c] + Vector3.up * 0.05f);
                AddLine(line, mat);
            }
            for (int c = 0; c < cols; c++)
            {
                var line = new List<Vector3>();
                for (int r = 0; r < rows; r++)
                    line.Add(pts[r * cols + c] + Vector3.up * 0.05f);
                AddLine(line, mat);
            }
            // Small cross on every planting spot (not on the scarecrow's centre cell, not where the pattern plants nothing).
            float s = 0.12f;
            int[] shape = CellIndices();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == CenterIndex || shape[i] < 0)
                    continue;
                Vector3 p = pts[i];
                AddLine(new List<Vector3> { p + new Vector3(-s, 0.06f, 0f), p + new Vector3(s, 0.06f, 0f) }, mat);
                AddLine(new List<Vector3> { p + new Vector3(0f, 0.06f, -s), p + new Vector3(0f, 0.06f, s) }, mat);
            }
        }

        private void AddLine(List<Vector3> pts, Material mat)
        {
            if (pts.Count < 2)
            {
                if (pts.Count == 1)
                    pts.Add(pts[0] + Vector3.up * 0.3f);
                else
                    return;
            }
            var go = new GameObject("Line");
            go.transform.SetParent(_preview.transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.widthMultiplier = 0.04f;
            lr.material = mat;
            lr.startColor = lr.endColor = new Color(0.55f, 0.95f, 0.35f, 0.85f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private static Material _previewMat;

        private static Material PreviewMaterial()
        {
            if (_previewMat != null)
                return _previewMat;
            Shader sh = Shader.Find("Sprites/Default");
            _previewMat = new Material(sh != null ? sh : Shader.Find("Standard"));
            return _previewMat;
        }

        private void OnDestroy()
        {
            Live.Remove(this);
            if (_preview != null)
                Destroy(_preview);
        }

        // ---- hover / interact

        public string GetHoverName()
        {
            return Label();
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public string GetHoverText()
        {
            int kinds = Palette().Count;
            string crop = kinds == 0
                ? Loc.T("no crop selected", "keine Pflanze gewählt")
                : kinds == 1 ? CropMap.DisplayName(CropSeed) : kinds + " " + Loc.T("crops", "Pflanzen");
            _hoverFrame = Time.frameCount; // shows the grid while the crosshair is on it
            string text = Label() + " (" + crop + ", " + Cols + " × " + Rows + ")";
            if (kinds > 0)
                text += "\n" + Loc.T("Needed for the grid: ", "Benötigt für das Raster: ") + NeededText();
            string use = Localization.instance != null ? Localization.instance.Localize("$KEY_Use") : "E";
            text += "\n[<color=yellow><b>" + use + "</b></color>] " + Loc.T("Settings", "Einstellungen");
            text += "\n[<color=yellow><b>" + Loc.T("Shift", "Umschalt") + "</b></color> / <color=yellow><b>"
                + Loc.T("Ctrl", "Strg") + "</b></color> + "
                + Loc.T("Wheel", "Mausrad") + "] " + Loc.T("Width / Length", "Breite / Länge");
            // Status only (no key prompt): auto-fill, harvest and plant are switched in the settings panel.
            text += "\n" + Loc.T("Auto-fill", "Auto-Fill") + ": " + StateText(StationAutoFill.IsOn(_nv));
            text += "\n" + Loc.T("Harvest", "Ernten") + ": " + StateText(Harvest)
                + "   " + Loc.T("Plant", "Pflanzen") + ": " + StateText(PlantOn);
            float harvestIn = HarvestInSeconds();
            if (harvestIn > 0f)
                text += "\n" + Loc.T("Harvest in: ", "Ernte in: ") + "<color=#ECC44B>~" + DurationText(harvestIn) + "</color>";
            float plantIn = PlantInSeconds();
            if (plantIn > 0f)
                text += "\n" + Loc.T("Auto-plant in: ", "Auto-Pflanzen in: ") + "<color=#ECC44B>" + Mathf.CeilToInt(plantIn) + " s</color>";
            StationLink.PrependHover(ref text, StationLink.Get(this), chest: false);
            return text;
        }

        private static string StateText(bool on)
        {
            return on
                ? "<color=#5fd36b>" + Loc.T("on", "an") + "</color>"
                : "<color=#e74c3c>" + Loc.T("off", "aus") + "</color>";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            // Plain E opens the settings. Alt+E keeps its own path (StationPullFilter.TryOpen), so skip it here.
            if (hold || alt)
                return false;
            if (!PrivateArea.CheckAccess(transform.position, 0f, true, true))
                return true;
            StationFilterMenu.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
    }

    /// <summary>
    /// Plants on a scarecrow's grid grow on any cultivated ground: the biome limit of the crop (flax and barley only in
    /// the Plains, onion and turnip only in the Swamp ...) is lifted for them. Valheim checks it in Plant.UpdateHealth
    /// and a plant in the wrong biome never grows. The rest of that check (cultivated ground, room, roof, cold / heat)
    /// still counts. Works for saplings that were already in the ground when the world loaded too.
    /// </summary>
    [HarmonyPatch(typeof(Plant), nameof(Plant.UpdateHealth))]
    internal static class ScarecrowPlantBiomePatch
    {
        private const float GateRange = 40f; // a grid reaches 21 points at most: farther away it cannot hold the plant

        private static void Prefix(Plant __instance, out Heightmap.Biome __state)
        {
            __state = __instance.m_biome;
            if (Scarecrow.Live.Count == 0)
                return;
            // Cheapest check first: is a scarecrow's grid under this plant at all? Only then the biome lookup.
            Vector3 pos = __instance.transform.position;
            bool onGrid = false;
            for (int i = 0; i < Scarecrow.Live.Count && !onGrid; i++)
            {
                Scarecrow s = Scarecrow.Live[i];
                if (s == null || (s.transform.position - pos).sqrMagnitude > GateRange * GateRange)
                    continue;
                onGrid = s.Contains(pos);
            }
            if (!onGrid)
                return;
            Heightmap hm = Heightmap.FindHeightmap(pos);
            if (hm == null || (hm.GetBiome(pos) & __instance.m_biome) != 0)
                return; // the biome is fine anyway
            __instance.m_biome = Heightmap.Biome.All;
        }

        private static void Postfix(Plant __instance, Heightmap.Biome __state)
        {
            __instance.m_biome = __state;
        }
    }

    /// <summary>A crop finished growing: scarecrows whose grid holds it re-check (no polling).</summary>
    [HarmonyPatch(typeof(Plant), nameof(Plant.Grow))]
    internal static class ScarecrowPlantGrowPatch
    {
        private static void Postfix(GameObject __result)
        {
            if (__result == null || Scarecrow.Live.Count == 0)
                return;
            Vector3 pos = __result.transform.position;
            for (int i = 0; i < Scarecrow.Live.Count; i++)
            {
                Scarecrow s = Scarecrow.Live[i];
                if (s != null && s.Contains(pos))
                    s.MarkDirtyAfter(1.5f);
            }
        }
    }

    /// <summary>
    /// While the crosshair is on a scarecrow and Shift / Ctrl is held, the mouse wheel edits the grid.
    /// The raw value is recorded for it and the camera (and anything else reading the wheel) gets 0.
    /// </summary>
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ScarecrowWheelPatch
    {
        private static void Postfix(ref float __result)
        {
            Scarecrow.WheelRaw = __result;
            if (Scarecrow.WheelGrabFrame >= Time.frameCount - 1)
                __result = 0f;
        }
    }

    /// <summary>
    /// Scarecrow harvest: every crop / seed a pickable drops goes straight into a chest next to the scarecrow
    /// (same chest rules and range as station auto-store: link match, AutoFillChestRange), instead of lying on
    /// the ground and waiting for ground auto-store. That one needs a player within StoreRange of the pile,
    /// the scarecrow works from further away. No chest takes it: normal ground drop, tagged with the link.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), "Drop")]
    internal static class ScarecrowHarvestDropPatch
    {
        private static bool Prefix(Pickable __instance, GameObject prefab, int stack, out bool __state)
        {
            __state = false;
            Scarecrow s = prefab != null ? Scarecrow.HarvestingAt(__instance.transform.position) : null;
            if (s == null)
                return true;
            string label;
            if (StationOutput.TryDepositNear(s, prefab.name, stack, out label))
                return false;
            StationChestFull.NoteOutput(s, prefab.name, stack);
            __state = true; // stays on the ground: Postfix tags it with the link
            return true;
        }

        private static void Postfix(Pickable __instance, GameObject prefab, bool __state)
        {
            if (!__state)
                return;
            Scarecrow s = Scarecrow.HarvestingAt(__instance.transform.position);
            int link = s != null ? StationLink.Get(s) : 0;
            if (link > 0)
                StationOutput.TagNearbyDrops(__instance.transform.position, prefab.name, link);
        }
    }
}
