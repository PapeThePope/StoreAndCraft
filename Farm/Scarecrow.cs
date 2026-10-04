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
    /// the local player in auto-fill range works it; on/off = the station auto-fill flag ([B]).
    /// </summary>
    internal class Scarecrow : MonoBehaviour, Hoverable, Interactable
    {
        internal const string PrefabName = "sac_scarecrow";
        private const string KeyCols = "SAC_farmCols";
        private const string KeyRows = "SAC_farmRows";
        private const string KeyCrop = "SAC_farmCrop";
        private const string KeyHarvest = "SAC_farmHarvest";
        private const string KeyPlant = "SAC_farmPlant";
        private const string KeyPlantAt = "SAC_farmPlantAt";     // world time (ms) when the next planting starts, 0 = none
        private const string KeyPlantDone = "SAC_farmPlantDone"; // 1 = planted, waits for the next harvest
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
        private GameObject _preview;
        private int _previewHash;

        internal bool MissingSeeds => _missingSeeds;

        // ---- settings (ZDO)

        private ZDO Zdo => _nv != null && _nv.IsValid() ? _nv.GetZDO() : null;
        internal int Cols => Odd(Zdo != null ? Zdo.GetInt(KeyCols, 5) : 5);
        internal int Rows => Odd(Zdo != null ? Zdo.GetInt(KeyRows, 5) : 5);

        /// <summary>Seeds for the whole grid: every point but the middle one, where the scarecrow stands.</summary>
        internal int SeedsNeeded => Mathf.Max(0, Cols * Rows - 1);

        /// <summary>Odd size 1..MaxSize so there is a real centre cell (even values round up).</summary>
        internal static int Odd(int v)
        {
            v = Mathf.Clamp(v, 1, MaxSize);
            return v % 2 == 0 ? Mathf.Min(v + 1, MaxSize) : v;
        }
        internal string CropSeed => Zdo != null ? Zdo.GetString(KeyCrop, "") : "";
        internal bool Harvest => Zdo == null || Zdo.GetBool(KeyHarvest, true);
        internal bool PlantOn => Zdo == null || Zdo.GetBool(KeyPlant, true);

        /// <summary>Fixed spacing (1.1 m), more only when the crop needs it (else it would not grow).</summary>
        internal float Spacing
        {
            get { return Mathf.Max(DefaultSpacing, MinSpacing()); }
        }

        private const float DefaultSpacing = 1.1f;

        internal float MinSpacing()
        {
            CropMap.Crop crop = CropMap.ForSeed(CropSeed);
            float r = crop != null ? crop.GrowRadius : 0.5f;
            return Mathf.Max(0.5f, Mathf.Ceil(r * 2f * 10f) / 10f + 0.05f);
        }

        internal void SetInt(string key, int v) => Write(z => z.Set(key, v));
        internal void SetCols(int v) { SetInt(KeyCols, Odd(v)); ArmPlant(0f); }
        internal void SetRows(int v) { SetInt(KeyRows, Odd(v)); ArmPlant(0f); }
        internal void SetCrop(string seed) { Write(z => z.Set(KeyCrop, seed ?? "")); ArmPlant(0f); }
        internal void SetHarvest(bool on) => Write(z => z.Set(KeyHarvest, on));
        internal void SetPlant(bool on)
        {
            Write(z => z.Set(KeyPlant, on));
            if (on)
                ArmPlant(0f);
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
            z.Set(KeyPlantAt, (long)((NowSeconds() + delay) * 1000.0));
            z.Set(KeyPlantDone, 0);
            MarkDirty();
        }

        /// <summary>Seconds until the planting starts, or -1 when none is waiting.</summary>
        internal float PlantInSeconds()
        {
            ZDO z = Zdo;
            if (z == null || !PlantOn)
                return -1f;
            long at = z.GetLong(KeyPlantAt, 0L);
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
            if (_nv != null && _nv.IsValid() && _nv.IsOwner() && _nv.GetZDO().GetInt(StationAutoFill.ZdoKey, -1) < 0)
                _nv.GetZDO().Set(StationAutoFill.ZdoKey, 1);
            MarkDirty();
        }

        private void Update()
        {
            bool ghost = _nv == null || _nv.GetZDO() == null;
            bool hovered = Time.frameCount - _hoverFrame <= 2;
            UpdatePreview(ghost || hovered || StationFilterMenu.IsEditing(this));
            if (ghost)
                return;
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
            float range = Plugin.Settings.AutoFillRange.Value;
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

        internal bool Contains(Vector3 pos)
        {
            List<Vector3> pts = Points();
            float tol = Spacing * 0.5f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= tol * tol)
                    return true;
            }
            return false;
        }

        // ---- work

        private enum Spot { Empty, Growing, Ripe, Blocked }

        private void Work()
        {
            CropMap.Crop crop = CropMap.ForSeed(CropSeed);
            if (crop == null)
                return;
            Player player = Player.m_localPlayer;
            List<Vector3> pts = Points();
            var state = new Spot[pts.Count];
            var ripe = new Pickable[pts.Count];
            Scan(pts, crop, state, ripe);
            state[CenterIndex] = Spot.Blocked; // the scarecrow stands here
            ripe[CenterIndex] = null;

            int growing = 0;
            int ripeCount = 0;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] == Spot.Growing) growing++;
                else if (state[i] == Spot.Ripe) ripeCount++;
            }

            int link = StationLink.Get(this);
            // A new scarecrow plants its field once, right away.
            if (Zdo != null && Zdo.GetInt(KeyPlantDone, -1) < 0 && Zdo.GetLong(KeyPlantAt, 0L) == 0L)
                ArmPlant(0f);
            // Whole field ripe → harvest everything at once.
            if (Harvest && ripeCount > 0 && growing == 0)
            {
                // The drops of these pickables go straight into chests (see ScarecrowHarvestDropPatch).
                HarvestUntil = Time.time + 3f;
                for (int i = 0; i < ripe.Length; i++)
                {
                    if (ripe[i] == null)
                        continue;
                    ZNetView pnv = ripe[i].GetComponent<ZNetView>();
                    if (pnv == null || !pnv.IsValid())
                        continue;
                    if (!pnv.IsOwner())
                        pnv.ClaimOwnership();
                    Vector3 at = ripe[i].transform.position;
                    pnv.InvokeRPC("RPC_Pick", 0);
                    state[i] = Spot.Empty;
                }
                ActivityLog.Note(Label(), Loc.T("Harvested ", "Geerntet: ") + ripeCount + " " + DisplayFilters.ItemLabel(crop.Seed));
                ArmPlant(PlantDelaySeconds); // the field rests first; the hover text counts down
            }

            float inSeconds = PlantInSeconds();
            if (inSeconds == 0f) // armed and due (-1 = nothing waiting, > 0 = still counting down)
            {
                PlantEmpty(player, crop, pts, state, link);
                if (_missingSeeds)
                {
                    ArmPlant(PlantDelaySeconds); // not enough seeds: try again later, visible in the hover text
                }
                else
                {
                    Zdo.Set(KeyPlantAt, 0L); // planted: nothing more until the next harvest
                    Zdo.Set(KeyPlantDone, 1);
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
                    float growTime = (float)PlantGrowTime.Invoke(plant, null);
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

        private void Scan(List<Vector3> pts, CropMap.Crop crop, Spot[] state, Pickable[] ripe)
        {
            float sp = Spacing;
            float tol = sp * 0.45f;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
                center += pts[i];
            center /= Mathf.Max(1, pts.Count);
            Vector3 half = new Vector3(Cols * sp * 0.5f + 0.5f, 3f, Rows * sp * 0.5f + 0.5f);
            Collider[] hits = Physics.OverlapBox(center, half, Quaternion.LookRotation(Flat(transform.forward)));
            var seen = new HashSet<int>();
            for (int h = 0; h < hits.Length; h++)
            {
                Plant plant = hits[h].GetComponentInParent<Plant>();
                Pickable pick = plant == null ? hits[h].GetComponentInParent<Pickable>() : null;
                Component c = (Component)plant ?? pick;
                if (c == null || !seen.Add(c.GetInstanceID()))
                    continue;
                int idx = Nearest(pts, c.transform.position, tol);
                if (idx < 0)
                    continue;
                if (plant != null)
                {
                    // The ripe crop is already there (the grown plant is only removed at the end of the frame).
                    if (state[idx] == Spot.Ripe)
                        continue;
                    // A plant that "needs more room" never grows: it must not hold the harvest back for ever.
                    state[idx] = plant.GetStatus() == Plant.Status.NoSpace ? Spot.Blocked : Spot.Growing;
                }
                // Any ripe field crop counts, not only the selected one: switching the crop while the old one is still
                // in the ground must not leave it unharvested (it would block the field for ever).
                else if (!pick.GetPicked() && CropMap.ForRipe(pick.gameObject) != null)
                {
                    if (state[idx] == Spot.Empty || state[idx] == Spot.Growing)
                    {
                        state[idx] = Spot.Ripe;
                        ripe[idx] = pick;
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
                if (picked != null && picked.GetPicked())
                    continue;
                Plant plant = c.GetComponent<Plant>();
                if (plant == null || plant.GetStatus() == Plant.Status.Healthy)
                    return false;
            }
            return true;
        }

        private void PlantEmpty(Player player, CropMap.Crop crop, List<Vector3> pts, Spot[] state, int link)
        {
            var targets = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (state[i] != Spot.Empty)
                    continue;
                if (crop.NeedCultivated)
                {
                    Heightmap hm = Heightmap.FindHeightmap(pts[i]);
                    if (hm == null || !hm.IsCultivated(pts[i]))
                        continue;
                }
                // Same room check as the plant itself: no seed is spent where it could never grow
                // (rock, tree, building piece or an older plant too close).
                if (!HasGrowSpace(pts[i], crop.GrowRadius))
                {
                    state[i] = Spot.Blocked;
                    continue;
                }
                targets.Add(i);
            }
            if (targets.Count == 0)
            {
                SetMissing(false, crop);
                return;
            }

            int got;
            StationFeed.PullOriginOverride = transform.position;
            StationFeed.PullRangeOverride = Plugin.Settings.AutoFillChestReach();
            try
            {
                got = StationFeed.ConsumeFromChests(player, crop.Seed, targets.Count, link);
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
                ActivityLog.FromChest(Label(), got, DisplayFilters.ItemLabel(crop.Seed));
            SetMissing(got < targets.Count, crop);
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

        private void SetMissing(bool missing, CropMap.Crop crop)
        {
            _missingSeeds = missing;
            if (missing)
                StationMissingLabel.SetMissing(this, "seed", DisplayFilters.ItemLabel(crop.Seed));
            else
                StationMissingLabel.ClearMissing(this, "seed");
        }

        private static int Nearest(List<Vector3> pts, Vector3 pos, float tol)
        {
            int best = -1;
            float bestSq = tol * tol;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        // ---- terrain (buttons in the settings panel, never automatic)

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
            // Small cross on every planting spot (not on the scarecrow's centre cell).
            float s = 0.12f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == CenterIndex)
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
            string crop = string.IsNullOrEmpty(CropSeed)
                ? Loc.T("no crop selected", "keine Pflanze gewählt")
                : CropMap.DisplayName(CropSeed);
            _hoverFrame = Time.frameCount; // shows the grid while the crosshair is on it
            string text = Label() + " (" + crop + ", " + Cols + " × " + Rows + ")";
            if (!string.IsNullOrEmpty(CropSeed))
                text += "\n" + Loc.T("Needed for the grid: ", "Benötigt für das Raster: ") + SeedsNeeded;
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
