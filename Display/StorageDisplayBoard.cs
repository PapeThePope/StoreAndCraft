using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    internal class StorageDisplayBoard : MonoBehaviour
    {
        public const string ZdoItemKey = "sac_item";
        public static readonly int ZdoItemKeyHash = ZdoItemKey.GetStableHashCode();
        public const string ZdoRangeKey = "sac_display_range";
        public static readonly int ZdoRangeKeyHash = ZdoRangeKey.GetStableHashCode();
        public const string ZdoScaleKey = "sac_ui_scale";
        public static readonly int ZdoScaleKeyHash = ZdoScaleKey.GetStableHashCode();
        public const string ZdoLayoutKey = "sac_ui_layout";
        public static readonly int ZdoLayoutKeyHash = ZdoLayoutKey.GetStableHashCode();
        public const string ZdoShowNameKey = "sac_show_name";
        public static readonly int ZdoShowNameKeyHash = ZdoShowNameKey.GetStableHashCode();
        public const string ZdoShowAmountKey = "sac_show_amount";
        public static readonly int ZdoShowAmountKeyHash = ZdoShowAmountKey.GetStableHashCode();
        public const int ScaleMin = -2;
        public const int ScaleMax = 4;
        public const int LayoutClassic = 0;
        public const int LayoutCompact = 1;

        private static readonly List<StorageDisplayBoard> All = new List<StorageDisplayBoard>();

        private DisplayKind _kind = DisplayKind.Medium;
        internal string VisualBase;
        /// <summary>Vanilla-sign display: definition id for its own layouts (null on carved).</summary>
        internal string SignLayout;
        internal string AppliedVisual;
        /// <summary>Matches DisplayLayouts.Generation after layout apply (0 = not applied yet).</summary>
        internal int AppliedLayoutGen;
        private int _slotCount = 12;
        private int _columns = 4;
        private int _rows = 3;
        private int _baseColumns = 4;
        private int _baseRows = 3;
        private int _headerColumns;
        private int _itemsPerGroup = 1;
        private float _fontFactor = 0.22f;
        private float _bandFontFactor = 0.15f;
        private float _titleFactor = 0.12f;
        private float _baseAmountFont;
        private float _slotFont;
        private float _chipFont;
        private float _boardW = 1f;
        private float _boardH = 1f;
        private bool _columnMajor;
        private bool _columnHeaders;
        private bool _tightSlots;
        private bool _flowSections;
        private float _labelWidth;
        private int _builtBandCount = -1;
        private int _builtScaleStep = int.MinValue;
        private int _builtLayoutMode = int.MinValue;
        private int _builtShowName = int.MinValue;
        private int _builtShowAmount = int.MinValue;
        private TextMeshProUGUI[] _bandLabels;
        private RectTransform _gridRoot;

        private struct SlotPairLayout
        {
            public float IconFrac;
            public float GapFrac;
            public float FontSize;
        }

        private readonly List<Container> _watched = new List<Container>();
        private readonly List<int> _watchIds = new List<int>();
        private static readonly List<Container> _watchScratch = new List<Container>(64);
        private SlotUi[] _slots;
        private TextMeshProUGUI[] _headers;
        private TextMeshProUGUI _signText;
        private float _titleFont;
        private float _born;
        private float _nextScan;
        private float _nextPaint;
        private bool _dirty = true;
        /// <summary>Chest discovery / subscribe list — only when selection, range, or rare discovery.</summary>
        private bool _watchDirty = true;
        private bool _configured;
        private int _page;
        private int _pages = 1;
        private List<StorageDisplayBoard> _cluster;

        private struct SlotUi
        {
            public Image Icon;
            public TextMeshProUGUI Amount;
            public TextMeshProUGUI Name;
            public bool IsChip;
            public string Shared; // shared name of the item painted here (right click: locate its chest)
            // v2 sections only (0 / false elsewhere → unchanged behaviour).
            public float Font;
            public bool HideAmount;
            public Color AmountColor;
        }

        /// <summary>v2 definition section at runtime: slot range + label.</summary>
        private sealed class SectionUi
        {
            public DisplayLayouts.SectionDef Def;
            public int Start;
            public int Count;
            public TextMeshProUGUI Label;
        }

        public const string ZdoLayoutVariantKey = "sac_ui_layout_var";
        public static readonly int ZdoLayoutVariantKeyHash = ZdoLayoutVariantKey.GetStableHashCode();
        private SectionUi[] _sections;
        private int _builtVariant = int.MinValue;
        private bool _buildingSections;

        public DisplayKind Kind
        {
            get { return _kind; }
        }

        public int SlotCount
        {
            get { return _slotCount; }
        }

        public void Configure(DisplayKind kind)
        {
            _kind = kind;
            _configured = true;
            switch (kind)
            {
                case DisplayKind.Small:
                    _slotCount = 1;
                    _columns = 1;
                    _rows = 1;
                    _headerColumns = 0;
                    _itemsPerGroup = 1;
                    // Slightly larger count than before; icon size is handled in CreateSlotCell.
                    _fontFactor = 0.34f;
                    _bandFontFactor = 0.34f;
                    _titleFactor = 0.16f;
                    _columnMajor = false;
                    _columnHeaders = false;
                    _tightSlots = false;
                    _flowSections = false;
                    break;
                case DisplayKind.Large:
                    // Fixed 12-row table (label | items). Do not rebuild when filter count changes.
                    _headerColumns = 0;
                    _itemsPerGroup = 1;
                    // Dense row at scale 0; higher scale → fewer, bigger cells (down to 4).
                    _columns = 13;
                    _rows = DisplayFilters.MaxCategories; // 12
                    _slotCount = _columns * _rows;
                    // Match Medium readability — 0.14 + band/3 made Compact/rebuild text microscopic.
                    _fontFactor = 0.20f;
                    _bandFontFactor = 0.13f;
                    _titleFactor = 0.10f;
                    _columnMajor = false;
                    _columnHeaders = false;
                    _tightSlots = true;
                    _flowSections = true;
                    // Room for WEAPONS / INGREDIENTS; label X nudge is BuildLargeUi offsetMin (-0.3).
                    _labelWidth = 0.16f;
                    break;
                default:
                    // Category title strip + denser item cells; sort grouped by category.
                    _slotCount = 12;
                    _columns = 4;
                    _rows = 3;
                    _headerColumns = 1;
                    _itemsPerGroup = 4;
                    _fontFactor = 0.22f;
                    _bandFontFactor = 0.13f;
                    _titleFactor = 0.10f;
                    _columnMajor = false;
                    _columnHeaders = true;
                    _tightSlots = true;
                    _flowSections = false;
                    _kind = DisplayKind.Medium;
                    break;
            }

            _baseColumns = _columns;
            _baseRows = _rows;
            ApplyScaleToLayout();
        }

        /// <summary>
        /// Carved Unity board, or a vanilla-sign display whose definition has v2 layouts.
        /// No Display Scale and no Classic/Compact: its look comes only from the definition
        /// layouts (Shift+RMB). A sign without such a definition keeps the classic behaviour.
        /// </summary>
        private bool HasOwnLayouts
        {
            get
            {
                return !string.IsNullOrEmpty(VisualBase)
                    || (!string.IsNullOrEmpty(SignLayout) && DisplayLayouts.HasSections(SignLayout));
            }
        }

        /// <summary>Definition id for layouts: carved visual, else the sign layout id.</summary>
        private string LayoutId()
        {
            string id = CurrentVisualId();
            return !string.IsNullOrEmpty(id) ? id : SignLayout;
        }

        public int ContentScaleStep()
        {
            if (HasOwnLayouts)
                return 0;
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return 0;
            return Mathf.Clamp(zdo.GetInt(ZdoScaleKeyHash, 0), ScaleMin, ScaleMax);
        }

        public float ContentScaleMul()
        {
            // −2…+4 → 0.5…2.0 (25% per Shift+LMB click).
            return 1f + ContentScaleStep() * 0.25f;
        }

        public void SetContentScaleStep(int step)
        {
            step = Mathf.Clamp(step, ScaleMin, ScaleMax);
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                nv?.ClaimOwnership();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return;
            if (zdo.GetInt(ZdoScaleKeyHash, 0) == step)
                return;
            zdo.Set(ZdoScaleKeyHash, step);
            // Rebuild + Paint same frame — do not Destroy the grid first (that blanked the board
            // until the throttled Paint ran 0.3–0.5s later).
            RebuildUiNow();
        }

        /// <summary>
        /// Cycle 0 → +1…+4 → −2…−1 → 0 → … (this board only).
        /// </summary>
        public void CycleContentScaleStep()
        {
            if (_kind != DisplayKind.Medium && _kind != DisplayKind.Large)
                return;
            if (HasOwnLayouts)
                return; // carved boards: layouts only, no scale
            int step = ContentScaleStep();
            int next = step >= ScaleMax ? ScaleMin : step + 1;
            SetContentScaleStep(next);
        }

        /// <summary>Hover: only the current step, e.g. <c>Display Scale : +3</c>.</summary>
        public string FormatHoverScaleLine()
        {
            return Loc.T("Display Scale", "Display Scale") + " : "
                + "<color=yellow><b>" + FormatScaleStep(ContentScaleStep()) + "</b></color>";
        }

        public int ContentLayoutMode()
        {
            if (_kind != DisplayKind.Medium && _kind != DisplayKind.Large)
                return LayoutClassic;
            if (HasOwnLayouts)
                return LayoutClassic; // no Compact mesh swap — horizontal is its own piece
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return LayoutClassic;
            return zdo.GetInt(ZdoLayoutKeyHash, LayoutClassic) == LayoutCompact
                ? LayoutCompact
                : LayoutClassic;
        }

        public bool IsCompactLayout()
        {
            return ContentLayoutMode() == LayoutCompact
                && (_kind == DisplayKind.Medium || _kind == DisplayKind.Large);
        }

        /// <summary>Folder under displays/. Medium and Large swap mesh with Classic / Compact.</summary>
        public string CurrentVisualId()
        {
            if (string.IsNullOrEmpty(VisualBase))
                return null;
            if (VisualBase == "medium")
                return IsCompactLayout() ? "medium_horizontal" : "medium_vertical";
            if (VisualBase == "large")
                return IsCompactLayout() ? "large_horizontal" : "large_vertical";
            return VisualBase;
        }

        /// <summary>
        /// Carved / Prefab-Editor boards with itemGrid JSON: own flat icon+count grid.
        /// Not the vanilla Large flow (category bands) or Medium header table.
        /// </summary>
        private bool UsesDefinitionGrid()
        {
            return !string.IsNullOrEmpty(VisualBase)
                && DisplayLayouts.HasItemGridOverride(CurrentVisualId());
        }

        public void SetLayoutMode(int mode)
        {
            if (_kind != DisplayKind.Medium && _kind != DisplayKind.Large)
                return;
            mode = mode == LayoutCompact ? LayoutCompact : LayoutClassic;
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                nv?.ClaimOwnership();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return;
            if (zdo.GetInt(ZdoLayoutKeyHash, LayoutClassic) == mode)
                return;
            zdo.Set(ZdoLayoutKeyHash, mode);
            // Same as scale: replace UI and Paint immediately so Classic↔Compact does not flash empty.
            RebuildUiNow();
        }

        /// <summary>
        /// Shift+RMB: Classic ↔ Compact on this board only. With v2 definition layouts it first
        /// steps through the layouts of the current mesh, then switches Classic/Compact.
        /// </summary>
        public void CycleLayoutMode()
        {
            if (HasOwnLayouts)
            {
                // Carved boards: only step through their own v2 layouts (no Classic/Compact).
                int ownCount = UsesSections() ? DisplayLayouts.LayoutCount(LayoutId()) : 0;
                if (ownCount < 2)
                    return;
                SetLayoutVariant((LayoutVariant() + 1) % ownCount);
                RebuildUiNow();
                return;
            }
            if (_kind != DisplayKind.Medium && _kind != DisplayKind.Large)
                return;
            int count = DisplayLayouts.LayoutCount(CurrentVisualId());
            int variant = LayoutVariant();
            if (count > 1 && variant + 1 < count)
            {
                SetLayoutVariant(variant + 1);
                RebuildUiNow();
                return;
            }
            SetLayoutVariant(0);
            SetLayoutMode(ContentLayoutMode() == LayoutCompact ? LayoutClassic : LayoutCompact);
        }

        /// <summary>Index into the current visual's v2 layouts (0 when none).</summary>
        public int LayoutVariant()
        {
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            return zdo != null ? Mathf.Max(0, zdo.GetInt(ZdoLayoutVariantKeyHash, 0)) : 0;
        }

        private void SetLayoutVariant(int variant)
        {
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                nv?.ClaimOwnership();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo != null && zdo.GetInt(ZdoLayoutVariantKeyHash, 0) != variant)
                zdo.Set(ZdoLayoutVariantKeyHash, variant);
        }

        /// <summary>v2 definition (layouts/sections) drives this board's face.</summary>
        private bool UsesSections()
        {
            // Also carved Small boards: with v2 layouts they show categories like Large.
            // Vanilla-sign displays too when their sign_* definition has layouts.
            string id = LayoutId();
            return !string.IsNullOrEmpty(id) && DisplayLayouts.HasSections(id);
        }

        public string FormatHoverLayoutLine()
        {
            if (HasOwnLayouts)
            {
                DisplayLayouts.LayoutDef own = UsesSections()
                    ? DisplayLayouts.GetLayout(LayoutId(), LayoutVariant())
                    : null;
                string ownName = Loc.T(own != null && !string.IsNullOrEmpty(own.name) ? own.name : "Standard", null);
                return Loc.T("Layout", "Layout") + " : " + "<color=yellow><b>" + ownName + "</b></color>";
            }
            string mode = ContentLayoutMode() == LayoutCompact
                ? Loc.T("Compact", "Compact")
                : Loc.T("Classic", "Classic");
            if (UsesSections() && DisplayLayouts.LayoutCount(LayoutId()) > 1)
            {
                DisplayLayouts.LayoutDef layout = DisplayLayouts.GetLayout(LayoutId(), LayoutVariant());
                if (layout != null && !string.IsNullOrEmpty(layout.name))
                    mode += " · " + Loc.T(layout.name, layout.name);
            }
            return Loc.T("Layout", "Layout") + " : "
                + "<color=yellow><b>" + mode + "</b></color>";
        }

        /// <summary>
        /// Small: All → no name → icon only → All.
        /// </summary>
        public void CycleSmallDisplayMode()
        {
            if (_kind != DisplayKind.Small)
                return;

            bool name = ShowName();
            bool amount = ShowAmount();
            if (name && amount)
                WriteShowFlags(false, true);   // name off
            else if (!name && amount)
                WriteShowFlags(false, false);  // icon only
            else
                WriteShowFlags(true, true);    // all on
        }

        public string FormatHoverSmallModeLine()
        {
            string mode;
            if (ShowName() && ShowAmount())
                mode = Loc.T("Name + Amount", "Name + Anzahl");
            else if (!ShowName() && ShowAmount())
                mode = Loc.T("No name", "Name weg");
            else
                mode = Loc.T("Icon only", "Nur Icon");
            return Loc.T("Display", "Display") + " : "
                + "<color=yellow><b>" + mode + "</b></color>";
        }

        /// <summary>
        /// Shift+LMB scale / Shift+RMB layout: one cycle on button <b>down</b> only.
        /// StartAttack only blocks the swing — it must not cycle (fires many times per click).
        /// </summary>
        private static int _cycleFrame = -1;
        private static int _layoutFrame = -1;

        public static void TickCycleInput()
        {
            if (!IsScaleChordHeld())
                return;

            bool lmb = Input.GetMouseButtonDown(0);
            bool rmb = Input.GetMouseButtonDown(1);
            try
            {
                if (ZInput.GetButtonDown("Attack"))
                    lmb = true;
                if (ZInput.GetButtonDown("SecondaryAttack"))
                    rmb = true;
            }
            catch
            {
            }

            if (rmb)
            {
                if (_layoutFrame == Time.frameCount)
                    return;
                _layoutFrame = Time.frameCount;
                TryCycleLayoutHovered();
                return;
            }

            if (!lmb)
                return;

            // Same frame can see both mouse and Attack down — only once.
            if (_cycleFrame == Time.frameCount)
                return;
            _cycleFrame = Time.frameCount;

            TryCycleHovered();
        }

        // ---- plain right click on an item on the display: camera flies to the chest that holds it

        private static int _locateFrame = -1;

        /// <summary>Slot under the crosshair that shows an item (-1 = none): ray from the camera against the icon rects.</summary>
        private int SlotUnderCrosshair()
        {
            Camera cam = Utils.GetMainCamera();
            if (_slots == null || cam == null)
                return -1;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _slots.Length; i++)
            {
                Image icon = _slots[i].Icon;
                if (icon == null || !icon.enabled || string.IsNullOrEmpty(_slots[i].Shared))
                    continue;
                RectTransform rt = icon.rectTransform;
                var plane = new Plane(rt.forward, rt.position);
                float dist;
                if (!plane.Raycast(ray, out dist) || dist > 12f)
                    continue;
                Vector3 local = rt.InverseTransformPoint(ray.GetPoint(dist));
                Rect r = rt.rect;
                // Whole tile, not only the icon: a little padding around it.
                float dx = Mathf.Abs(local.x - r.center.x) / Mathf.Max(0.0001f, r.width * 0.7f);
                float dy = Mathf.Abs(local.y - r.center.y) / Mathf.Max(0.0001f, r.height * 0.7f);
                if (dx > 1f || dy > 1f)
                    continue;
                float score = dx * dx + dy * dy;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Plain right click on a display item: no weapon swing / block on that click.</summary>
        public static bool ShouldBlockSecondaryForLocate()
        {
            if (IsScaleChordHeld() || !LocateInputAllowed())
                return false;
            StorageDisplayBoard board = HoveredBoard();
            return board != null && board.SlotUnderCrosshair() >= 0;
        }

        private static bool LocateInputAllowed()
        {
            Player player = Player.m_localPlayer;
            if (player == null || LocateCamera.Active || player.InPlaceMode())
                return false;
            if (DisplayTypeMenu.IsOpen || DisplayRangeMenu.IsOpen || DisplaySmallOptions.IsOpen || StationFilterMenu.IsOpen)
                return false;
            return !(InventoryGui.instance != null && InventoryGui.IsVisible()) && !Menu.IsActive() && !Chat.instance.HasFocus();
        }

        public static void TickLocateInput()
        {
            bool rmb = Input.GetMouseButtonDown(1);
            try
            {
                if (ZInput.GetButtonDown("SecondaryAttack"))
                    rmb = true;
            }
            catch
            {
            }
            if (!rmb || IsScaleChordHeld() || !LocateInputAllowed() || _locateFrame == Time.frameCount)
                return;
            StorageDisplayBoard board = HoveredBoard();
            if (board == null)
                return;
            int slot = board.SlotUnderCrosshair();
            if (slot < 0)
                return;
            _locateFrame = Time.frameCount;
            SearchPing.LocateForBoard(board, board._slots[slot].Shared);
        }

        public static bool TryCycleHovered()
        {
            if (!IsScaleChordHeld())
                return false;

            Player player = Player.m_localPlayer;
            if (player == null)
                return false;

            StorageDisplayBoard board = HoveredBoard();
            if (board == null)
                return false;

            if (!PrivateArea.CheckAccess(board.transform.position, 0f, false, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return true;
            }

            // Scale / mode is always per hovered board ZDO only (never the cluster).
            if (board.Kind == DisplayKind.Small)
                board.CycleSmallDisplayMode();
            else if (board.Kind == DisplayKind.Medium || board.Kind == DisplayKind.Large)
                board.CycleContentScaleStep();
            else
                return false;

            return true;
        }

        public static bool TryCycleLayoutHovered()
        {
            if (!IsScaleChordHeld())
                return false;

            Player player = Player.m_localPlayer;
            if (player == null)
                return false;

            StorageDisplayBoard board = HoveredBoard();
            if (board == null)
                return false;
            if (board.Kind != DisplayKind.Medium && board.Kind != DisplayKind.Large
                && !(board.Kind == DisplayKind.Small && board.UsesSections()))
                return false;

            if (!PrivateArea.CheckAccess(board.transform.position, 0f, false, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return true;
            }

            board.CycleLayoutMode();
            return true;
        }

        public static bool IsScaleChordHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>True when Shift is held over a display that uses the cycle chord.</summary>
        public static bool ShouldBlockAttackForCycle()
        {
            if (!IsScaleChordHeld())
                return false;
            StorageDisplayBoard board = HoveredBoard();
            if (board == null)
                return false;
            return board.Kind == DisplayKind.Small
                || board.Kind == DisplayKind.Medium
                || board.Kind == DisplayKind.Large;
        }

        private static StorageDisplayBoard HoveredBoard()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;

            GameObject hover = player.GetHoverObject();
            StorageDisplayBoard fromHover = hover != null
                ? hover.GetComponentInParent<StorageDisplayBoard>()
                : null;
            if (fromHover != null)
                return fromHover;

            Piece piece = player.GetHoveringPiece();
            return piece != null ? piece.GetComponent<StorageDisplayBoard>() : null;
        }

        public bool ShowName()
        {
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return true;
            return zdo.GetInt(ZdoShowNameKeyHash, 1) != 0;
        }

        public bool ShowAmount()
        {
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return true;
            return zdo.GetInt(ZdoShowAmountKeyHash, 1) != 0;
        }

        public void SetShowName(bool on)
        {
            WriteShowFlags(on, ShowAmount());
        }

        public void SetShowAmount(bool on)
        {
            WriteShowFlags(ShowName(), on);
        }

        private void WriteShowFlags(bool name, bool amount)
        {
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                nv?.ClaimOwnership();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return;

            int nameV = name ? 1 : 0;
            int amountV = amount ? 1 : 0;
            bool changed = false;
            if (zdo.GetInt(ZdoShowNameKeyHash, 1) != nameV)
            {
                zdo.Set(ZdoShowNameKeyHash, nameV);
                changed = true;
            }
            if (zdo.GetInt(ZdoShowAmountKeyHash, 1) != amountV)
            {
                zdo.Set(ZdoShowAmountKeyHash, amountV);
                changed = true;
            }
            if (!changed)
                return;

            // Layout (icon position) depends on flags — rebuild, don't only hide labels.
            RebuildUiNow();
        }

        /// <summary>PrefabStudio preview: rebuild every loaded board showing this visual id.</summary>
        internal static void RebuildVisual(string visualId)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                StorageDisplayBoard board = All[i];
                if (board == null)
                    continue;
                if (!string.Equals(board.LayoutId(), visualId, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                board.AppliedLayoutGen = 0; // force DisplayVisual.EnsureLayout → TryApply
                board.RebuildUiNow();
            }
        }

        /// <summary>
        /// Tear down cached slot state, rebuild the grid, and Paint immediately.
        /// Avoids the empty-board flash from Destroy-then-wait-for-_nextPaint.
        /// </summary>
        private void RebuildUiNow()
        {
            InvalidateUi();
            _slots = null;
            TryBuild();
            if (_slots == null)
            {
                MarkDirty();
                return;
            }

            _dirty = false;
            _nextPaint = Time.time + (_kind == DisplayKind.Large ? 0.5f : 0.3f);
            Paint();
            // The paint above used the last chest count. After a new layout (more categories shown) parts stayed
            // empty until a chest changed: count the chests once more and repaint shortly, the same as a chest
            // change. Only on a rebuild (layout / scale / name options), not per frame.
            MarkDirty();
        }

        public void InvalidateUi()
        {
            _slots = null;
            _headers = null;
            _bandLabels = null;
            _builtScaleStep = int.MinValue;
            _builtLayoutMode = int.MinValue;
            _builtShowName = int.MinValue;
            _builtShowAmount = int.MinValue;
            _builtBandCount = -1;
            _builtVariant = int.MinValue;
            _sections = null;
        }

        private void CaptureBoardMetrics(TextMeshProUGUI template)
        {
            if (template == null)
                return;
            _baseAmountFont = template.fontSize * _fontFactor;
            _chipFont = template.fontSize * _bandFontFactor;
            RectTransform board = template.rectTransform;
            if (board == null)
                return;
            Rect r = board.rect;
            float w = Mathf.Abs(r.width);
            float h = Mathf.Abs(r.height);
            // Stretched signs sometimes report 0 until laid out — fall back to sizeDelta / corners.
            if (w < 0.05f || h < 0.05f)
            {
                Vector3[] corners = new Vector3[4];
                board.GetLocalCorners(corners);
                w = Mathf.Abs(corners[2].x - corners[0].x);
                h = Mathf.Abs(corners[2].y - corners[0].y);
            }
            if (w < 0.05f)
                w = Mathf.Max(0.05f, Mathf.Abs(board.sizeDelta.x));
            if (h < 0.05f)
                h = Mathf.Max(0.05f, Mathf.Abs(board.sizeDelta.y));
            if (w > 0.05f)
                _boardW = w;
            if (h > 0.05f)
                _boardH = h;
        }

        /// <summary>Reserve width for a 4-digit count ("9999") at the given font size.</summary>
        private static float AmountReserveWidth(float fontSize)
        {
            // World-space Valheim signs: digit advance ≈ 0.40×font (0.62 over-shrinks to dots).
            return Mathf.Max(0.01f, fontSize * 0.40f * 4f);
        }

        private float ReferenceCellHeightPx()
        {
            if (IsCompactLayout())
            {
                // Denser than Classic category rows so Compact packs more pairs at scale 0.
                int targetRows = _kind == DisplayKind.Large ? 8 : 5;
                return Mathf.Max(0.01f, _boardH / targetRows);
            }
            if (_kind == DisplayKind.Large)
                return Mathf.Max(0.01f, _boardH / Mathf.Max(1, DisplayFilters.MaxCategories));
            if (_kind == DisplayKind.Medium && _columnHeaders)
                return Mathf.Max(0.01f, (_boardH * 0.88f) / Mathf.Max(1, _baseRows));
            int rows = _baseRows > 0 ? _baseRows : 3;
            return Mathf.Max(0.01f, _boardH / Mathf.Max(1, rows));
        }

        private float MinPairWidthPx(float cellH, float baseFont)
        {
            // Pair size comes from cell height only — Display Scale grows cells (cols/rows), not font alone.
            float refH = ReferenceCellHeightPx();
            float s = refH > 0.01f ? cellH / refH : 1f;
            float font = Mathf.Min(baseFont * s, cellH * 0.70f);
            float icon = cellH * 0.82f;
            float gap = Mathf.Max(0.01f, cellH * 0.04f);
            return icon + gap + AmountReserveWidth(font);
        }

        /// <summary>
        /// Icon + amount as one pair sized to the cell. Scale grows cells (fewer cols/rows);
        /// never let font exceed cell height (that caused Large Classic overlap).
        /// </summary>
        private SlotPairLayout LayoutSlotPair(float cellWFrac, float cellHFrac, float baseFont)
        {
            float cellW = Mathf.Max(0.01f, cellWFrac * _boardW);
            float cellH = Mathf.Max(0.01f, cellHFrac * _boardH);
            float refH = ReferenceCellHeightPx();
            float s = refH > 0.01f ? cellH / refH : 1f;
            float font = Mathf.Min(baseFont * s, cellH * 0.70f);
            float icon = cellH * 0.82f;
            float gap = Mathf.Max(0.01f, cellH * 0.04f);
            float amountW = AmountReserveWidth(font);
            float pairW = icon + gap + amountW;
            if (pairW > cellW && pairW > 0.01f)
            {
                float fit = cellW / pairW;
                fit = Mathf.Max(0.55f, fit);
                icon *= fit;
                font *= fit;
                gap *= fit;
                // Keep icon roughly square vs cell height after width fit.
                icon = Mathf.Min(icon, cellH * 0.90f);
                font = Mathf.Min(font, cellH * 0.70f);
            }

            return new SlotPairLayout
            {
                IconFrac = Mathf.Clamp(icon / cellW, 0.22f, 0.55f),
                GapFrac = Mathf.Max(0.004f, gap / cellW),
                FontSize = font
            };
        }

        private void EnforceClassicColumnFloor(float contentWFrac, float contentHFrac)
        {
            // Do not treat boardW≈1 as "unmeasured" — vanilla wood-sign text rects are often ~1 unit,
            // and skipping the floor left Large at 13–18 tiny columns after Scale/Layout rebuilds.
            if (_baseAmountFont <= 0.01f || _boardW < 0.05f || _boardH < 0.05f)
                return;
            float cellH = contentHFrac * _boardH / Mathf.Max(1, _rows);
            float minPair = MinPairWidthPx(cellH, _baseAmountFont);
            float contentW = contentWFrac * _boardW;
            int maxCols = Mathf.Max(2, Mathf.FloorToInt(contentW / Mathf.Max(0.01f, minPair)));
            if (_columns > maxCols)
                _columns = maxCols;
        }

        private void ApplyCompactScale(float mul)
        {
            float refH = ReferenceCellHeightPx();
            float pairH = Mathf.Clamp(refH * mul, _boardH / 18f, _boardH / 2f);
            float baseFont = _baseAmountFont > 0.01f ? _baseAmountFont : 8f;
            float font = baseFont * (pairH / Mathf.Max(0.01f, refH));
            // Keep a readable floor so scale −2 does not become microscopic dots.
            float minFont = baseFont * 0.55f;
            if (font < minFont)
            {
                float grow = minFont / Mathf.Max(0.01f, font);
                font = minFont;
                pairH *= grow;
            }
            float pairW = pairH * 0.88f + Mathf.Max(0.01f, pairH * 0.05f) + AmountReserveWidth(font);
            _columns = Mathf.Clamp(Mathf.FloorToInt(_boardW / Mathf.Max(0.01f, pairW)), 2, 16);
            _rows = Mathf.Clamp(Mathf.FloorToInt(_boardH / Mathf.Max(0.01f, pairH)), 2, 16);
            _slotCount = _columns * _rows;
            _itemsPerGroup = _columns;
            // Do NOT clear _headerColumns — Classic Medium needs it after toggle.
        }

        /// <summary>Put Medium/Large chassis flags back after Compact (Compact must not leave Classic broken).</summary>
        private void RestoreClassicChassisFlags()
        {
            if (_kind == DisplayKind.Medium)
            {
                _headerColumns = 1;
                _columnHeaders = true;
                _flowSections = false;
                _columnMajor = false;
                _tightSlots = true;
            }
            else if (_kind == DisplayKind.Large)
            {
                _headerColumns = 0;
                _columnHeaders = false;
                _flowSections = true;
                _columnMajor = false;
                _tightSlots = true;
            }
        }

        private void ApplyScaleToLayout()
        {
            if (_kind == DisplayKind.Small)
            {
                // Back from a v2 sections chassis (editor preview) → the one-slot Small chassis.
                if (!UsesSections())
                {
                    _slotCount = 1;
                    _columns = 1;
                    _rows = 1;
                }
                return;
            }

            // v2 definition: sections size themselves in BuildSectionsUi.
            if (UsesSections())
            {
                _headerColumns = 0;
                _columnHeaders = false;
                _flowSections = false;
                _columnMajor = false;
                _tightSlots = true;
                _itemsPerGroup = 1;
                _labelWidth = 0f;
                return;
            }

            // Custom mesh + JSON itemGrid: dedicated flat grid (not Large/Medium classic UI).
            if (TryApplyDefinitionGridLayout())
                return;

            float mul = ContentScaleMul();
            if (IsCompactLayout())
            {
                ApplyCompactScale(mul);
                return;
            }

            RestoreClassicChassisFlags();

            if (_kind == DisplayKind.Medium)
            {
                _columns = Mathf.Clamp(Mathf.RoundToInt(_baseColumns / mul), 2, 8);
                // Taller cells at higher scale so icon+count grow together (not font-only).
                _rows = Mathf.Clamp(Mathf.RoundToInt(_baseRows / mul), 1, _baseRows);
                EnforceClassicColumnFloor(1f, _columnHeaders ? 0.88f : 1f);
                _slotCount = _columns * _rows;
                _itemsPerGroup = _columns;
                _headerColumns = 1;
            }
            else if (_kind == DisplayKind.Large)
            {
                // Fewer / taller rows + fewer columns → icons and counts grow inside the cell.
                _columns = Mathf.Clamp(Mathf.RoundToInt(_baseColumns / mul), 4, 18);
                _rows = Mathf.Clamp(
                    Mathf.RoundToInt(DisplayFilters.MaxCategories / mul),
                    3,
                    DisplayFilters.MaxCategories);
                float labelW = Mathf.Clamp(_labelWidth, 0.12f, 0.22f);
                EnforceClassicColumnFloor(1f - labelW, 1f);
                _slotCount = _columns * _rows;
                _itemsPerGroup = 1;
                _headerColumns = 0;
            }
        }

        /// <summary>Prefab-Editor itemGrid: cols×rows of icon+count cells. Scale still densifies.</summary>
        private bool TryApplyDefinitionGridLayout()
        {
            int cols;
            int rows;
            if (!DisplayLayouts.TryGetItemGrid(CurrentVisualId(), out cols, out rows))
                return false;

            _baseColumns = cols;
            _baseRows = rows;
            _headerColumns = 0;
            _columnHeaders = false;
            _flowSections = false;
            _columnMajor = false;
            _tightSlots = true;
            _itemsPerGroup = 1;
            _labelWidth = 0f;

            float mul = ContentScaleMul();
            _columns = Mathf.Clamp(Mathf.RoundToInt(cols / mul), 1, cols);
            _rows = Mathf.Clamp(Mathf.RoundToInt(rows / mul), 1, rows);
            _slotCount = _columns * _rows;
            return true;
        }

        public static string FormatScaleStep(int step)
        {
            step = Mathf.Clamp(step, ScaleMin, ScaleMax);
            if (step > 0)
                return "+" + step;
            return step.ToString();
        }

        private void Awake()
        {
            if (!_configured)
                DetectKindFromName();
            EnsureVisualBaseFromName();
        }

        /// <summary>
        /// Clones lose VisualBase (not serialized). Without it the carved board skipped
        /// DisplayVisual (no canvas align, no ItemGrid definition UI) and drew the items on
        /// the vanilla sign canvas at the foot of the piece.
        /// </summary>
        private void EnsureVisualBaseFromName()
        {
            if (!string.IsNullOrEmpty(VisualBase))
                return;
            VisualBase = DisplayPrefab.VisualBaseForPrefab(gameObject.name);
            if (string.IsNullOrEmpty(VisualBase) && string.IsNullOrEmpty(SignLayout))
                SignLayout = DisplayPrefab.SignLayoutForPrefab(gameObject.name);
        }

        private void DetectKindFromName()
        {
            string n = gameObject.name.Replace("(Clone)", "").Trim();
            if (n.IndexOf("small", System.StringComparison.OrdinalIgnoreCase) >= 0)
                Configure(DisplayKind.Small);
            else if (n.IndexOf("large", System.StringComparison.OrdinalIgnoreCase) >= 0)
                Configure(DisplayKind.Large);
            else
                Configure(DisplayKind.Medium);
        }

        private string BoardTitle()
        {
            switch (_kind)
            {
                case DisplayKind.Small:
                    return Loc.T("Small Storage Display", "Small Storage Display");
                case DisplayKind.Large:
                    return Loc.T("Large Storage Display", "Large Storage Display");
                default:
                    return Loc.T("Medium Storage Display", "Medium Storage Display");
            }
        }

        private void OnEnable()
        {
            if (!All.Contains(this))
                All.Add(this);
            _born = Time.time;
            _dirty = true;
            _watchDirty = true;
            _nextScan = 0f;
            InvalidateClusters();
        }

        private void Start()
        {
            if (!_configured)
                DetectKindFromName();
            EnsureVisualBaseFromName();
            if (!All.Contains(this))
                All.Add(this);
            // A board loaded far from the camera (zone load, spawn) builds its face when the camera comes close
            // (TickBoard), not now: loading a base used to build every board's UI in the first frames.
            Camera cam = Utils.GetMainCamera();
            float edge = FaceLodMeters + FaceLodHysteresis;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude <= edge * edge)
                TryBuild();
            _started = true;
        }

        private void OnDestroy()
        {
            DisplayTypeMenu.CloseIf(this);
            DisplayRangeMenu.CloseIf(this);
            DisplaySmallOptions.CloseIf(this);
            All.Remove(this);
            Unwatch();
            InvalidateClusters();
        }

        private static void InvalidateClusters()
        {
            for (int i = 0; i < All.Count; i++)
            {
                StorageDisplayBoard board = All[i];
                if (board != null)
                    board._cluster = null;
            }
        }

        public string HoverLabel()
        {
            string rangeKey = KeyUtil.Format(Plugin.Settings != null
                ? Plugin.Settings.DisplayRangeKey.Value
                : new BepInEx.Configuration.KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt));
            if (string.IsNullOrEmpty(rangeKey))
                rangeKey = "Alt+R";
            // Chord keys first, then single keys (same rule as stations).
            string rangeLine = "[<color=yellow><b>" + rangeKey + "</b></color>] "
                + Loc.T("Range", "Reichweite")
                + " (" + Mathf.RoundToInt(EffectiveDisplayRange()) + " m)";

            if (_kind == DisplayKind.Small)
            {
                string hotbar = "[<color=yellow><b>1-8</b></color>] "
                    + Loc.T("Set item from hotbar", "Item aus Hotbar setzen");
                string modeLine = "[<color=yellow><b>Shift+LMB</b></color>] " + FormatHoverSmallModeLine();
                string token = ItemToken();
                string head = string.IsNullOrEmpty(token)
                    ? BoardTitle()
                    : BoardTitle() + " (" + ItemLabel(token) + ")";
                if (UsesSections())
                {
                    string sLayout = "[<color=yellow><b>Shift+RMB</b></color>] " + FormatHoverLayoutLine();
                    string sUse = "[<color=yellow><b>E</b></color>] " + Loc.T("Select type", "Typ wählen");
                    // Carved Small with own layouts: types via [E] only (no hotbar assign).
                    if (HasOwnLayouts)
                    {
                        bool layouts = DisplayLayouts.LayoutCount(LayoutId()) > 1;
                        return head + "\n" + (layouts ? sLayout + "\n" : "") + rangeLine + "\n" + sUse;
                    }
                    return head + "\n" + sLayout + "\n" + rangeLine + "\n" + sUse + "\n" + hotbar;
                }
                return head + "\n" + modeLine + "\n" + rangeLine + "\n" + hotbar;
            }

            List<int> filters = FilterIds();
            List<string> items = ItemTokens();
            string name = DisplayFilters.Label(filters, items);
            string use = "[<color=yellow><b>E</b></color>] " + Loc.T("Select type", "Typ wählen");
            string scaleLine = "[<color=yellow><b>Shift+LMB</b></color>] " + FormatHoverScaleLine();
            string layoutLine = "[<color=yellow><b>Shift+RMB</b></color>] " + FormatHoverLayoutLine();

            string title = BoardTitle();
            if (filters.Count > 0 || items.Count > 0)
            {
                title = BoardTitle() + " (" + name + ")";
                RefreshClusterPages();
                if (_pages > 1)
                    title += " (" + (_page + 1) + "/" + _pages + ")";
            }

            if (HasOwnLayouts)
            {
                // Carved: no scale; Shift+RMB only when there is more than one own layout.
                bool layouts = UsesSections() && DisplayLayouts.LayoutCount(LayoutId()) > 1;
                return title + "\n" + (layouts ? layoutLine + "\n" : "") + rangeLine + "\n" + use;
            }
            return title + "\n" + scaleLine + "\n" + layoutLine + "\n" + rangeLine + "\n" + use;
        }

        public string ItemToken()
        {
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return "";
            return zdo.GetString(ZdoItemKeyHash, "") ?? "";
        }

        /// <summary>Per-display override in meters (5–50). 0 = use config DisplayRange.</summary>
        public int DisplayRangeMeters()
        {
            ZNetView nv = GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return 0;
            return Mathf.Clamp(zdo.GetInt(ZdoRangeKeyHash, 0), 0, 50);
        }

        /// <summary>Range is only set per display (Alt+R); a display without its own value uses this.</summary>
        private const float DefaultDisplayRange = 10f;

        public float EffectiveDisplayRange()
        {
            int custom = DisplayRangeMeters();
            return custom > 0 ? custom : DefaultDisplayRange;
        }

        /// <summary>
        /// Anyone with ward access may set this (no admin). Stored on the piece ZDO.
        /// </summary>
        public void SetDisplayRangeMeters(int meters)
        {
            meters = Mathf.Clamp(meters, 5, 50);
            // Snap to 5 m steps.
            meters = Mathf.RoundToInt(meters / 5f) * 5;
            if (meters < 5)
                meters = 5;

            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();

            nv.GetZDO().Set(ZdoRangeKeyHash, meters);
            _dirty = true;
            _watchDirty = true;
            _nextScan = 0f;
            _cluster = null;
            InvalidateClusters();
        }

        public void SetItemFromHotbar(ItemDrop.ItemData item)
        {
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();

            ZDO zdo = nv.GetZDO();
            if (item?.m_shared == null)
            {
                zdo.Set(ZdoItemKeyHash, "");
            }
            else
            {
                // Prefer shared name ($item_…) so chest CountItems matches reliably.
                string token = ItemIds.SharedName(item);
                if (string.IsNullOrEmpty(token))
                    token = ItemIds.PrefabName(item) ?? "";
                // Ensure drop prefab is known for later SampleFromToken / Matches.
                if (item.m_dropPrefab == null)
                    ItemIds.PrefabName(item);
                zdo.Set(ZdoItemKeyHash, token ?? "");
            }

            // Small boards do not use category filters.
            zdo.Set(DisplayFilters.ZdoKeyMultiHash, "");
            zdo.Set(DisplayFilters.ZdoKeyHash, 0);
            _dirty = true;
            _cluster = null;
            InvalidateClusters();
        }

        /// <summary>
        /// While looking at a small display, hotbar 1-8 assigns that item (empty slot clears).
        /// UseHotbarItem passes 1-8 (same as vanilla); inventory columns are 0-7.
        /// </summary>
        public static bool TryAssignFromHotbar(Player player, int hotbarIndex)
        {
            if (player == null || player != Player.m_localPlayer)
                return false;
            if (InventoryGui.IsVisible() || DisplayTypeMenu.IsOpen || StationFilterMenu.IsOpen
                || DisplayRangeMenu.IsOpen || DisplaySmallOptions.IsOpen)
                return false;
            // Vanilla Player.UseHotbarItem(index) uses 1..8, then GetItemAt(index - 1, 0).
            if (hotbarIndex < 1 || hotbarIndex > 8)
                return false;

            StorageDisplayBoard board = HoveredSmall();
            if (board == null)
                return false;
            // Carved Small with own layouts picks types via [E]; hotbar keys stay vanilla.
            if (board.HasOwnLayouts && board.UsesSections())
                return false;
            if (!PrivateArea.CheckAccess(board.transform.position, 0f, false, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return true;
            }

            Inventory inv = player.GetInventory();
            if (inv == null)
                return true;

            ItemDrop.ItemData item = inv.GetItemAt(hotbarIndex - 1, 0);
            board.SetItemFromHotbar(item);
            if (item?.m_shared != null)
            {
                string shown = ItemLabel(ItemIds.SharedName(item));
                player.Message(MessageHud.MessageType.TopLeft,
                    Loc.T("Display set: ", "Anzeige gesetzt: ") + shown, 0, null, false);
            }
            else
            {
                player.Message(MessageHud.MessageType.TopLeft,
                    Loc.T("Display cleared", "Anzeige geleert"), 0, null, false);
            }
            return true;
        }

        private static StorageDisplayBoard HoveredSmall()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;

            GameObject hover = player.GetHoverObject();
            StorageDisplayBoard fromHover = hover != null
                ? hover.GetComponentInParent<StorageDisplayBoard>()
                : null;
            if (fromHover != null && fromHover.Kind == DisplayKind.Small)
                return fromHover;

            Piece piece = player.GetHoveringPiece();
            StorageDisplayBoard fromPiece = piece != null
                ? piece.GetComponent<StorageDisplayBoard>()
                : null;
            if (fromPiece != null && fromPiece.Kind == DisplayKind.Small)
                return fromPiece;
            return null;
        }

        private static string ItemLabel(string token)
        {
            if (string.IsNullOrEmpty(token))
                return Loc.T("Select item", "Item wählen");
            if (Localization.instance != null)
                return Localization.instance.Localize(token);
            return token;
        }

        // Filters / items are read from the board ZDO several times per repaint: parsed once per ZDO change.
        // Callers get their own copy (some add to it).
        private uint _filterRev = uint.MaxValue;
        private List<int> _filterIds;
        private uint _tokenRev = uint.MaxValue;
        private List<string> _itemTokens;

        public List<int> FilterIds()
        {
            uint rev = CurrentRevision();
            if (_filterIds == null || rev != _filterRev || rev == uint.MaxValue - 1)
            {
                ZDO zdo = _view != null && _view.IsValid() ? _view.GetZDO() : null;
                _filterIds = DisplayFilters.ReadIds(zdo);
                _filterRev = rev;
            }
            return new List<int>(_filterIds);
        }

        public int FilterId()
        {
            List<int> ids = FilterIds();
            return ids.Count > 0 ? ids[0] : 0;
        }

        public List<string> ItemTokens()
        {
            uint rev = CurrentRevision();
            if (_itemTokens == null || rev != _tokenRev || rev == uint.MaxValue - 1)
            {
                ZDO zdo = _view != null && _view.IsValid() ? _view.GetZDO() : null;
                _itemTokens = DisplayFilters.ReadItemTokens(zdo);
                _tokenRev = rev;
            }
            return new List<string>(_itemTokens);
        }

        /// <summary>Carved Small board: [E] selection limited to 1 category or 4 items, no hotbar.</summary>
        private bool IsSmallOwnLayouts => _kind == DisplayKind.Small && HasOwnLayouts;
        private const int SmallCarvedMaxItems = 4;

        public void WriteFilters(List<int> ids)
        {
            WriteSelection(ids, ItemTokens());
        }

        public void WriteSelection(List<int> ids, List<string> itemTokens)
        {
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();

            ZDO zdo = nv.GetZDO();
            zdo.Set(DisplayFilters.ZdoKeyMultiHash, DisplayFilters.EncodeIds(ids));
            zdo.Set(DisplayFilters.ZdoKeyItemsHash, DisplayFilters.EncodeItemTokens(itemTokens));
            // Carved Small has no hotbar assign any more — drop an old hotbar item on first [E] edit.
            if (IsSmallOwnLayouts)
                zdo.Set(ZdoItemKeyHash, "");
            int legacy = 0;
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    if (ids[i] > 0)
                    {
                        legacy = ids[i];
                        break;
                    }
                }
            }
            zdo.Set(DisplayFilters.ZdoKeyHash, legacy);
            _dirty = true;
            _watchDirty = true;
            _nextScan = 0f;
            _cluster = null;
            InvalidateClusters();
        }

        public void ToggleFilter(int id)
        {
            if (id <= 0)
            {
                WriteSelection(new List<int>(), new List<string>());
                return;
            }

            List<int> ids = FilterIds();
            List<string> items = ItemTokens();
            if (ids.Contains(id))
            {
                ids.Remove(id);
            }
            else if (IsSmallOwnLayouts)
            {
                // Carved Small: one category at most — picking one replaces the selection.
                ids = new List<int> { id };
                items = new List<string>();
            }
            else
            {
                if (!CanAddCategory(ids, items, id))
                    return;
                ids.Add(id);
                // Whole category selected: drop fine-grained picks under it.
                if (DisplayFilters.IsExpandable(id))
                    items = RemoveCategoryItems(items, id);
                // Epic Loot "All" replaces Dust/Essence/Reagent/Shard bands.
                if (id == DisplayFilters.EpicLootGroupId)
                    RemoveEpicLootSubFilters(ids);
                // A subtype band replaces the Epic Loot "All" parent label.
                if (DisplayFilters.IsEpicLootSubFilter(id))
                    ids.Remove(DisplayFilters.EpicLootGroupId);
            }
            WriteSelection(ids, items);
        }

        private static void RemoveEpicLootSubFilters(List<int> ids)
        {
            if (ids == null)
                return;
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                if (DisplayFilters.IsEpicLootSubFilter(ids[i]))
                    ids.RemoveAt(i);
            }
        }

        public void ToggleItemToken(string shared, int parentFilterId)
        {
            if (string.IsNullOrEmpty(shared))
                return;

            List<int> ids = FilterIds();
            List<string> items = ItemTokens();
            if (items.Contains(shared))
                items.Remove(shared);
            else if (IsSmallOwnLayouts)
            {
                // Carved Small: either one category or up to 4 single items.
                if (items.Count >= SmallCarvedMaxItems)
                {
                    Player player = Player.m_localPlayer;
                    if (player != null)
                        player.Message(MessageHud.MessageType.Center,
                            Loc.T("You reached the maximum of 4 items", "Maximal 4 Items erreicht"),
                            0, null, false);
                    return;
                }
                ids = new List<int>();
                items.Add(shared);
            }
            else
            {
                // Adding a token whose parent is not already represented counts as a new category.
                int parent = parentFilterId > 0 ? parentFilterId : DisplayFilters.ParentFilterIdFromToken(shared);
                if (parent > 0 && !ids.Contains(parent) && !CategoryRepresentedByItems(items, parent))
                {
                    if (!CanAddCategory(ids, items, parent))
                        return;
                }
                items.Add(shared);
                if (parentFilterId > 0)
                    ids.Remove(parentFilterId);
            }
            WriteSelection(ids, items);
        }

        private static bool CategoryRepresentedByItems(List<string> items, int parentId)
        {
            if (items == null || parentId <= 0)
                return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (DisplayFilters.ParentFilterIdFromToken(items[i]) == parentId)
                    return true;
            }
            return false;
        }

        private static bool CanAddCategory(List<int> ids, List<string> items, int newId)
        {
            var probeIds = new List<int>(ids ?? new List<int>());
            if (newId > 0 && !probeIds.Contains(newId))
                probeIds.Add(newId);
            int count = BuildSectionOrder(probeIds, items).Count;
            if (count <= DisplayFilters.MaxCategories)
                return true;

            Player player = Player.m_localPlayer;
            if (player != null)
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    Loc.T(
                        "You reached the maximum of 12 categories",
                        "Maximal 12 Kategorien erreicht"),
                    0, null, false);
            }
            return false;
        }

        private static List<string> RemoveCategoryItems(List<string> items, int filterId)
        {
            if (items == null || items.Count == 0)
                return new List<string>();
            var next = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!DisplayFilters.TokenBelongsToCategory(items[i], filterId))
                    next.Add(items[i]);
            }
            return next;
        }

        public void SetFilter(int id)
        {
            var ids = new List<int>();
            if (id > 0)
                ids.Add(id);
            WriteSelection(ids, new List<string>());
        }

        public bool TryOpenMenu(Humanoid user, bool alt = false)
        {
            Player player = user as Player;
            if (player == null || player != Player.m_localPlayer)
                return false;
            if (!PrivateArea.CheckAccess(transform.position, 0f, false, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return true;
            }

            // Small: Alt+E (alt use) opens Name / Amount toggles. Plain E does nothing special
            // (hotbar 1-8 while looking at it still assigns — see hover text).
            if (_kind == DisplayKind.Small)
            {
                if (alt)
                    DisplaySmallOptions.Open(this);
                else if (UsesSections())
                    DisplayTypeMenu.Open(this); // v2 layouts: pick categories like Large
                return true;
            }

            DisplayTypeMenu.Open(this);
            return true;
        }

        private static int _heavyFrame = -1;
        private static StorageDisplayBoard _heavyBoard;

        /// <summary>
        /// One board at a time does its heavy work (build, chest discovery, paint) in a frame, so many boards that
        /// all need it at once (arriving at a base) spread over several frames instead of one long frame.
        /// The same board may do several steps in its frame (rebuild + paint, no empty flash in between).
        /// </summary>
        private bool ClaimFrame()
        {
            int f = Time.frameCount;
            if (_heavyFrame == f && _heavyBoard != null && _heavyBoard != this)
                return false;
            _heavyFrame = f;
            _heavyBoard = this;
            return true;
        }

        /// <summary>
        /// All boards from one place (Plugin.Update), like Valheim's MonoUpdaters, instead of one Unity Update call per
        /// board: same work, same order of events, without the per-object engine call. Only active, started boards
        /// tick (what Unity did for Update).
        /// </summary>
        internal static void TickAll()
        {
            if (All.Count == 0)
                return;
            // One camera lookup per frame for every board's LOD / visibility check.
            Camera cam = Utils.GetMainCamera();
            _hasCam = cam != null;
            if (_hasCam)
                _camPos = cam.transform.position;
            TickScratch.Clear();
            TickScratch.AddRange(All); // a tick may remove boards from All
            float now = Time.time;
            for (int i = 0; i < TickScratch.Count; i++)
            {
                StorageDisplayBoard board = TickScratch[i];
                if (board == null || !board._started || !board.isActiveAndEnabled)
                    continue;
                // A board beyond the face LOD shows nothing and does no work: it only has to notice the camera
                // coming closer, which a look every FarTickSeconds does (the face appears within that time).
                if (board._faceHidden && now < board._nextFarTick)
                    continue;
                board._nextFarTick = now + FarTickSeconds;
                board.TickBoard();
            }
            TickScratch.Clear();
        }

        private static readonly List<StorageDisplayBoard> TickScratch = new List<StorageDisplayBoard>(64);
        private const float FarTickSeconds = 0.25f;
        private float _nextFarTick;
        private static bool _hasCam;
        private static Vector3 _camPos;
        private bool _started;

        private void TickBoard()
        {
            if (_bornAt < 0f)
                _bornAt = Time.time;
            // Any chassis mismatch must RebuildUiNow — nulling _slots + throttled Paint
            // flashes an empty board for 0.3–0.5s (Scale / Layout feels like flicker).
            // The checks read the board settings from its ZDO: only run them when something they read can have changed.
            bool needsRebuild = false;
            if (LayoutInputsChanged())
            {
                needsRebuild = LayoutOutdated();
                if (!needsRebuild)
                    RememberLayoutInputs();
            }

            // Beyond the face LOD nothing is drawn: do not build (or rebuild) the face until the camera comes closer.
            // After a spawn / zone load this skips building every far board (10-15 ms each).
            if ((needsRebuild || _slots == null) && BeyondFaceLod())
            {
                _faceHidden = true; // ticked FarTickSeconds apart until it is close enough to build
                return;
            }

            if (needsRebuild)
            {
                if (!ClaimFrame())
                    return;
                RebuildUiNow();
                if (_slots == null)
                    return;
            }
            else if (_slots == null)
            {
                if (!ClaimFrame())
                    return;
                TryBuild();
                if (_slots == null)
                    return;
            }

            if (_slots == null)
                return;

            UpdateFaceLod();

            // Cheap: keep vanilla sign text off. Avoid ApplyTitle/FilterIds every frame.
            SilenceSignText();

            // Chest discovery only on demand (enable / selection / range) or rare pickup of new chests.
            // Inventory count changes use m_onChanged → MarkDirty → Paint, not a full rescan.
            if (_watchDirty || _watched.Count == 0)
            {
                if (Time.time >= _nextScan)
                {
                    if (!ClaimFrame())
                        return;
                    // Fast after a selection / range change; a board with no chests in range
                    // only needs a slow retry (it rescanned every 0.35 s forever before).
                    _nextScan = Time.time + (_watchDirty ? 0.35f : 3f);
                    Resubscribe();
                    _watchDirty = false;
                }
            }
            else if (Time.time >= _nextScan)
            {
                if (!ClaimFrame())
                    return;
                // Rare discovery pass for newly placed / newly loaded chests (no ZDO reload when list unchanged).
                _nextScan = Time.time + (Time.time - _bornAt < YoungBoardSeconds ? 5f : 30f);
                Resubscribe();
            }

            // Boards nobody can see (out of the camera view, or farther than SeenNeverBeyond) stay dirty and do not
            // scan or repaint; as soon as one comes into view it repaints. The overdue clock only runs while seen.
            if (_dirty && !IsSeen())
            {
                _dirtySince = -1f;
                return;
            }

            if (_dirty)
                ApplyTitle();

            if (!_dirty)
                _dirtySince = -1f;
            else if (_dirtySince < 0f)
                _dirtySince = Time.time;
            if (!_dirty || Time.time < _nextPaint)
                return;
            // Safety net: never leave a board blank because of the frame sharing / sliced scan.
            bool overdue = _dirtySince >= 0f && Time.time - _dirtySince > OverdueSeconds;
            if (!ClaimFrame() && !overdue)
                return;
            // The chest scan is spread over several frames; the board stays dirty until it is ready.
            if (SliceRanking && !overdue && !PrepareRank())
                return;

            float basePause = _kind == DisplayKind.Large ? 0.5f : 0.3f;
            _nextPaint = Time.time + basePause;
            _dirty = false;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Paint();
            // The longer a repaint took, the longer until the next one (a busy base keeps boards dirty all the time).
            float took = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency);
            took += _scanSeconds;
            _scanSeconds = 0f;
            _nextPaint = Time.time + Mathf.Max(basePause, took * 60f);
            if (_refreshAfterPaint)
            {
                _refreshAfterPaint = false;
                _dirty = true;
            }
        }

        // ---- rebuild check (Update): same conditions as always, evaluated only when an input changed ----

        private uint _inRev = uint.MaxValue;
        private int _inGeneration = -1;
        private object _inSlots;
        private int _inSlotCount = -1;
        private object _inSections;
        private object _inHeaders;
        private object _inBandLabels;
        private int _inRows = -1;
        private int _inColumns = -1;
        private float _inRecheckAt;

        /// <summary>
        /// True when something LayoutOutdated reads may differ from the last check that said "up to date": the board
        /// ZDO changed (scale / layout / variant / name / amount, also when another player set it), the layout
        /// definitions were reloaded, the UI was rebuilt, or one second passed (safety for anything else).
        /// </summary>
        private bool LayoutInputsChanged()
        {
            uint rev = CurrentRevision();
            return rev != _inRev || DisplayLayouts.Generation != _inGeneration
                || !ReferenceEquals(_slots, _inSlots) || _slotCount != _inSlotCount
                || !ReferenceEquals(_sections, _inSections) || !ReferenceEquals(_headers, _inHeaders)
                || !ReferenceEquals(_bandLabels, _inBandLabels) || _rows != _inRows || _columns != _inColumns
                || Time.time >= _inRecheckAt;
        }

        private void RememberLayoutInputs()
        {
            _inRev = CurrentRevision();
            _inGeneration = DisplayLayouts.Generation;
            _inSlots = _slots;
            _inSlotCount = _slotCount;
            _inSections = _sections;
            _inHeaders = _headers;
            _inBandLabels = _bandLabels;
            _inRows = _rows;
            _inColumns = _columns;
            _inRecheckAt = Time.time + 1f;
        }

        private ZNetView _view;

        // ---- visibility: only boards someone can see scan their chests and repaint ----

        /// <summary>Always updated this close to the camera (turning around next to it never shows old numbers).</summary>
        private const float SeenAlwaysWithin = 8f;
        /// <summary>Never updated where the face is not drawn (see FaceLodMeters); it repaints once you come closer.</summary>
        private const float SeenNeverBeyond = FaceLodMeters + FaceLodHysteresis;

        // ---- LOD: the face (icons, numbers, titles) is only drawn within FaceLodMeters of the camera ----

        private const float FaceLodMeters = 20f;
        /// <summary>Hidden beyond 21 m, shown again within 19 m: no flicker when standing right at the edge.</summary>
        private const float FaceLodHysteresis = 1f;
        private Canvas[] _faceCanvases;
        private object _faceSlots;
        private bool _faceHidden;

        private bool BeyondFaceLod()
        {
            if (!_hasCam)
                return false;
            float edge = FaceLodMeters + FaceLodHysteresis;
            return (_camPos - transform.position).sqrMagnitude > edge * edge;
        }

        /// <summary>
        /// Switches the board's canvases off when the camera is far away (Unity then neither draws nor rebuilds them;
        /// the board model stays). Canvases are looked up again after the face was rebuilt.
        /// </summary>
        private void UpdateFaceLod()
        {
            float sq = _hasCam ? (_camPos - transform.position).sqrMagnitude : 0f;
            float edge = _faceHidden ? FaceLodMeters - FaceLodHysteresis : FaceLodMeters + FaceLodHysteresis;
            bool hide = sq > edge * edge;
            bool rebuilt = _faceCanvases == null || !ReferenceEquals(_slots, _faceSlots);
            if (hide == _faceHidden && !rebuilt)
                return;
            if (rebuilt)
            {
                _faceCanvases = GetComponentsInChildren<Canvas>(true);
                _faceSlots = _slots;
            }
            _faceHidden = hide;
            for (int i = 0; i < _faceCanvases.Length; i++)
            {
                Canvas c = _faceCanvases[i];
                if (c != null && c.enabled == hide)
                    c.enabled = !hide;
            }
        }
        private Renderer[] _renderers;
        private float _renderersAt = -100f;

        /// <summary>
        /// In view of the main camera: Unity's own culling result (Renderer.isVisible) of the board model. It is view
        /// culling, not occlusion (a board behind a wall inside the view counts as seen). No camera / no model: seen.
        /// </summary>
        private bool IsSeen()
        {
            if (!_hasCam)
                return true;
            float sq = (_camPos - transform.position).sqrMagnitude;
            if (sq <= SeenAlwaysWithin * SeenAlwaysWithin)
                return true;
            if (sq > SeenNeverBeyond * SeenNeverBeyond)
                return false;
            // The model can be swapped (Classic / Compact): look the renderers up again now and then.
            if (_renderers == null || Time.time - _renderersAt > 5f)
            {
                _renderers = GetComponentsInChildren<Renderer>(false);
                _renderersAt = Time.time;
            }
            if (_renderers.Length == 0)
                return true;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r != null && r.enabled && r.isVisible)
                    return true;
            }
            return false;
        }

        private uint CurrentRevision()
        {
            if (_view == null)
                _view = GetComponent<ZNetView>();
            ZDO zdo = _view != null && _view.IsValid() ? _view.GetZDO() : null;
            return zdo != null ? zdo.DataRevision : uint.MaxValue - 1;
        }

        /// <summary>The chassis no longer matches the settings: the UI has to be rebuilt.</summary>
        private bool LayoutOutdated()
        {
            bool needsRebuild = false;

            // Force UI rebuild when switching to small layout tweaks / slot count changes.
            if (_slots != null && _slots.Length != _slotCount)
                needsRebuild = true;
            // Content scale or Classic/Compact layout changed (Medium / Large).
            if (_slots != null && (_kind == DisplayKind.Medium || _kind == DisplayKind.Large)
                && (ContentScaleStep() != _builtScaleStep || ContentLayoutMode() != _builtLayoutMode
                    || LayoutVariant() != _builtVariant))
                needsRebuild = true;
            // v2 sections have their own chassis (no band labels / single grid).
            bool sections = _slots != null && UsesSections();
            // Small boards with v2 layouts switch layouts too (Shift+RMB).
            if (sections && _kind == DisplayKind.Small && LayoutVariant() != _builtVariant)
                needsRebuild = true;
            // Definition switched between v2 and older chassis (editor preview / file change).
            if (_slots != null && sections != (_sections != null))
                needsRebuild = true;
            // Small name/amount layout changed — rebuild so icon can recenter.
            if (_slots != null && _kind == DisplayKind.Small
                && ((ShowName() ? 1 : 0) != _builtShowName || (ShowAmount() ? 1 : 0) != _builtShowAmount))
                needsRebuild = true;
            // Medium Classic: need category header strip. Only force rebuild when headers are expected.
            if (_slots != null && _kind == DisplayKind.Medium && !IsCompactLayout()
                && _columnHeaders && _headerColumns > 0 && _headers == null)
                needsRebuild = true;
            // Large: migrate leftover 3-col header chassis once (not every frame on flow boards).
            if (_slots != null && _kind == DisplayKind.Large && !IsCompactLayout() && _flowSections
                && (_headers != null || _columnMajor))
                needsRebuild = true;
            // Compact / Large band UIs — skip for Prefab-Editor definition grids (own chassis).
            if (sections)
            {
                // Own chassis; rebuilt via layout/variant/definition changes above.
            }
            else if (_slots != null && !UsesDefinitionGrid())
            {
                if (IsCompactLayout()
                    && (_bandLabels == null || _bandLabels.Length != _rows
                        || _slots.Length != _columns * _rows))
                    needsRebuild = true;
                if (_kind == DisplayKind.Large && !IsCompactLayout() && _flowSections
                    && (_bandLabels == null || _bandLabels.Length != _rows
                        || _slots.Length != _columns * _rows))
                    needsRebuild = true;
            }
            else if (_slots != null && UsesDefinitionGrid()
                && _slots.Length != _columns * _rows)
            {
                needsRebuild = true;
            }
            if (_slots != null && _kind == DisplayKind.Small && _sections == null && _slots.Length == 1
                && (_slots[0].Name == null
                    || (_slots[0].Amount != null && _slots[0].Amount.fontSize < 1f)))
                needsRebuild = true;
            return needsRebuild;
        }

        private void TryBuild()
        {
            ZNetView nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return;
            Sign sign = GetComponent<Sign>();
            TextMeshProUGUI template = sign != null ? sign.m_textWidget : null;
            if (template == null)
                return;
            DisplayVisual.Ensure(this);
            CaptureBoardMetrics(template);
            ApplyScaleToLayout();
            BuildUi();
            // Canvas may have been aligned before SacDisplayGrid existed — snap again.
            DisplayVisual.Ensure(this);
            if (_slots != null)
            {
                _builtScaleStep = ContentScaleStep();
                _builtLayoutMode = ContentLayoutMode();
                _builtVariant = LayoutVariant();
                _builtShowName = ShowName() ? 1 : 0;
                _builtShowAmount = ShowAmount() ? 1 : 0;
                _dirty = true;
            }
        }

        private void SilenceSignText()
        {
            if (_signText == null || (!_signText.enabled && string.IsNullOrEmpty(_signText.text)))
                return;
            _signText.enabled = false;
            _signText.text = "";
        }

        private void ApplyTitle()
        {
            if (_signText == null)
                return;
            if (FilterIds().Count > 0 || ItemTokens().Count > 0)
            {
                SilenceSignText();
                return;
            }

            if (_kind == DisplayKind.Small && !string.IsNullOrEmpty(ItemToken()))
            {
                SilenceSignText();
                return;
            }

            _signText.enabled = true;
            _signText.alignment = TextAlignmentOptions.Center;
            _signText.overflowMode = TextOverflowModes.Overflow;
            _signText.textWrappingMode = TextWrappingModes.Normal;
            _signText.enableAutoSizing = false;
            if (_titleFont > 0f)
                _signText.fontSize = _titleFont;
            string title = _kind == DisplayKind.Small
                ? Loc.T("Press 1-8", "1-8 drücken")
                : Loc.T("Select type", "Typ wählen");
            if (_signText.text != title)
                _signText.text = title;
        }

        private void BuildUi()
        {
            Sign sign = GetComponent<Sign>();
            TextMeshProUGUI template = sign != null ? sign.m_textWidget : null;
            if (template == null)
                return;

            RectTransform board = template.rectTransform;
            // v2 sections behave like the definition grid here: own canvases, sign canvas off.
            bool sectionsUi = UsesSections();
            bool definitionGrid = sectionsUi || UsesDefinitionGrid();
            if (!definitionGrid && template.canvas == null)
            {
                // A definition-grid build switched the sign canvas off (e.g. Classic ↔ Compact
                // onto a visual without an ItemGrid definition). Bring it back for this layout.
                Canvas signCanvas = DisplayVisual.FindCanvas(template.transform);
                if (signCanvas != null)
                    signCanvas.gameObject.SetActive(true);
            }
            if (board == null || (!definitionGrid && template.canvas == null))
                return;

            _signText = template;
            // Board title stays put - only item cells grow via fewer/wider columns.
            _titleFont = template.fontSize * _titleFactor;
            SilenceSignText();

            float font = template.fontSize * _fontFactor;
            float bandFont = template.fontSize * _bandFontFactor;
            _chipFont = bandFont;

            if (sectionsUi)
            {
                BuildSectionsUi(template);
                return;
            }

            // Carved + baked ItemGrid: flat cells on the AlignCanvas-snapped sign face.
            if (definitionGrid)
            {
                BuildDefinitionGridUi(template, font);
                return;
            }

            Transform existing = board.parent.Find("SacDisplayGrid");
            if (existing != null)
            {
                existing.name = "SacDisplayGrid_old";
                existing.gameObject.SetActive(false);
                if (_gridRoot != null && _gridRoot.transform == existing)
                    _gridRoot = null;
                Destroy(existing.gameObject);
            }

            var root = new GameObject("SacDisplayGrid", typeof(RectTransform));
            root.transform.SetParent(board.parent, false);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = board.anchorMin;
            rt.anchorMax = board.anchorMax;
            rt.pivot = board.pivot;
            rt.anchoredPosition = board.anchoredPosition;
            rt.sizeDelta = board.sizeDelta;
            rt.offsetMin = board.offsetMin;
            rt.offsetMax = board.offsetMax;
            rt.localScale = board.localScale;
            rt.localRotation = board.localRotation;

            if (IsCompactLayout())
            {
                BuildCompactUi(root, template, font, bandFont);
                return;
            }

            if (_kind == DisplayKind.Large && _flowSections)
            {
                BuildLargeUi(root, template, font, bandFont);
                return;
            }

            float contentTop = 1f;
            if (_columnHeaders && _headerColumns > 0)
            {
                contentTop = 0.88f;
                _headers = new TextMeshProUGUI[_headerColumns];
                for (int col = 0; col < _headerColumns; col++)
                {
                    GameObject headerGo = Object.Instantiate(template.gameObject, root.transform);
                    headerGo.name = "Header" + col;
                    headerGo.SetActive(true);
                    RectTransform headerRt = headerGo.GetComponent<RectTransform>();
                    float h0 = (col * _itemsPerGroup) / (float)_columns;
                    float h1 = ((col + 1) * _itemsPerGroup) / (float)_columns;
                    headerRt.anchorMin = new Vector2(h0 + 0.01f, contentTop);
                    headerRt.anchorMax = new Vector2(h1 - 0.01f, 1f);
                    headerRt.offsetMin = Vector2.zero;
                    headerRt.offsetMax = Vector2.zero;
                    headerRt.localScale = Vector3.one;
                    headerRt.localRotation = Quaternion.identity;

                    TextMeshProUGUI header = headerGo.GetComponent<TextMeshProUGUI>();
                    var localize = headerGo.GetComponent("Localize") as MonoBehaviour;
                    if (localize != null)
                        Object.Destroy(localize);
                    if (template.font != null)
                        header.font = template.font;
                    header.enabled = true;
                    header.alignment = TextAlignmentOptions.Center;
                    header.textWrappingMode = TextWrappingModes.NoWrap;
                    header.overflowMode = TextOverflowModes.Overflow;
                    header.enableAutoSizing = false;
                    header.fontSize = bandFont;
                    header.color = new Color(1f, 0.95f, 0.75f, 1f);
                    header.faceColor = new Color32(255, 242, 191, 255);
                    header.outlineWidth = 0f;
                    header.raycastTarget = false;
                    header.text = "";
                    _headers[col] = header;
                }
            }
            else
            {
                _headers = null;
            }

            float padX = _tightSlots ? 0.006f : (_kind == DisplayKind.Small ? 0.04f : 0.012f);
            float padY = _tightSlots ? 0.012f : (_kind == DisplayKind.Small ? 0.06f : 0.018f);

            _slots = new SlotUi[_slotCount];
            for (int i = 0; i < _slotCount; i++)
            {
                int col;
                int row;
                SlotCoord(i, out col, out row);
                CreateSlotCell(root.transform, template, font, padX, padY,
                    col / (float)_columns, (col + 1) / (float)_columns,
                    contentTop * (1f - (row + 1) / (float)_rows),
                    contentTop * (1f - row / (float)_rows),
                    i);
            }
            _bandLabels = null;
            _builtBandCount = -1;
            _gridRoot = rt;
        }

        private void BuildCompactUi(GameObject root, TextMeshProUGUI template, float font, float bandFont)
        {
            _headers = null;
            _gridRoot = root.GetComponent<RectTransform>();

            int rows = Mathf.Max(1, _rows);
            int itemCols = Mathf.Max(1, _columns);
            _slotCount = rows * itemCols;
            _builtBandCount = rows;

            // Fixed left gutter — wide enough for full category words (WEAPONS, INGREDIENTS).
            const float labelW = 0.15f;
            float padX = 0.003f;
            float padY = 0.010f;
            // Soft nudge toward the board edge without leaving the sign face.
            const float labelNudge = -0.22f;

            // Category text grows/shrinks with Display Scale; position stays on the left rail.
            float chipSize = bandFont * Mathf.Clamp(ContentScaleMul(), 0.7f, 1.55f);
            _chipFont = chipSize;

            _bandLabels = new TextMeshProUGUI[rows];
            for (int r = 0; r < rows; r++)
            {
                GameObject labelGo = Object.Instantiate(template.gameObject, root.transform);
                labelGo.name = "CompactCat" + r;
                labelGo.SetActive(false);
                RectTransform labelRt = labelGo.GetComponent<RectTransform>();
                float y0 = 1f - (r + 1) / (float)rows;
                float y1 = 1f - r / (float)rows;
                labelRt.anchorMin = new Vector2(0f, y0 + padY);
                labelRt.anchorMax = new Vector2(labelW, y1 - padY);
                labelRt.offsetMin = new Vector2(labelNudge, 0f);
                labelRt.offsetMax = Vector2.zero;
                labelRt.localScale = Vector3.one;
                labelRt.localRotation = Quaternion.identity;

                TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
                var localize = labelGo.GetComponent("Localize") as MonoBehaviour;
                if (localize != null)
                    Object.Destroy(localize);
                if (template.font != null)
                    label.font = template.font;
                label.enabled = true;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.margin = Vector4.zero;
                label.enableAutoSizing = true;
                label.fontSizeMin = chipSize * 0.55f;
                label.fontSizeMax = chipSize;
                label.fontSize = chipSize;
                label.color = new Color(1f, 0.88f, 0.35f, 1f);
                label.faceColor = new Color32(255, 220, 80, 255);
                label.outlineWidth = 0f;
                label.raycastTarget = false;
                label.text = "";
                _bandLabels[r] = label;
            }

            _slots = new SlotUi[_slotCount];
            int slot = 0;
            for (int r = 0; r < rows; r++)
            {
                float y0 = 1f - (r + 1) / (float)rows;
                float y1 = 1f - r / (float)rows;
                for (int col = 0; col < itemCols; col++)
                {
                    float x0 = labelW + (1f - labelW) * (col / (float)itemCols);
                    float x1 = labelW + (1f - labelW) * ((col + 1) / (float)itemCols);
                    CreateSlotCell(root.transform, template, font, padX, padY,
                        x0, x1, y0, y1, slot);
                    slot++;
                }
            }
        }

        /// <summary>
        /// Prefab-Editor boards: own World Space canvas on SacModel at baked ItemGrid pos/size.
        /// Does not use the vanilla sign canvas (that stays at the wood-sign foot).
        /// </summary>
        private void BuildDefinitionGridUi(TextMeshProUGUI template, float font)
        {
            _headers = null;
            _bandLabels = null;
            _builtBandCount = -1;

            DisplayVisual.Ensure(this);

            int cols;
            int rows;
            Vector3 gridPos;
            Vector3 gridEuler;
            Vector2 faceMeters;
            if (!DisplayLayouts.TryGetItemGridFace(
                    CurrentVisualId(), out cols, out rows, out gridPos, out gridEuler, out faceMeters))
            {
                Plugin.Log.LogWarning("Definition grid UI: no ItemGrid face for " + CurrentVisualId());
                return;
            }

            Transform model = transform.Find("SacModel");
            if (model == null)
            {
                Plugin.Log.LogWarning("Definition grid UI: SacModel missing on " + name);
                return;
            }

            // Strip any UI left on the vanilla foot canvas so it cannot show as a tiny strip.
            if (template.canvas != null)
            {
                Transform footGrid = template.canvas.transform.Find("SacDisplayGrid");
                if (footGrid != null)
                    Destroy(footGrid.gameObject);
                template.canvas.gameObject.SetActive(false);
            }
            template.enabled = false;
            template.text = "";

            Transform oldPieceCanvas = transform.Find("SacDisplayCanvas");
            if (oldPieceCanvas != null)
                Destroy(oldPieceCanvas.gameObject);
            Transform oldModelCanvas = model.Find("SacDisplayCanvas");
            if (oldModelCanvas != null)
                Destroy(oldModelCanvas.gameObject);

            // Match vanilla sign UI scale (meters via tiny canvas scale + large sizeDelta).
            float uiScale = 0.01f;
            if (template.canvas != null)
            {
                float s = Mathf.Abs(template.canvas.transform.localScale.x);
                if (s > 0.0001f && s < 0.2f)
                    uiScale = s;
            }

            // Canvas units (like CaptureBoardMetrics), not meters — else LayoutSlotPair caps
            // the count font at ~0.3 units and amounts become invisible.
            _boardW = faceMeters.x / uiScale;
            _boardH = faceMeters.y / uiScale;
            _baseAmountFont = Mathf.Max(0.05f, font);
            _columns = Mathf.Max(1, _columns);
            _rows = Mathf.Max(1, _rows);
            cols = _columns;
            rows = _rows;
            // Sign-template font (×0.20) is tiny against the ItemGrid cells; size counts from
            // the cell height instead (LayoutSlotPair still shrinks it to fit next to the icon).
            font = Mathf.Max(font, _boardH / rows * 0.45f);

            var canvasGo = new GameObject("SacDisplayCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(model, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;

            RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.anchorMin = canvasRt.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRt.pivot = new Vector2(0.5f, 0.5f);
            canvasRt.sizeDelta = new Vector2(faceMeters.x / uiScale, faceMeters.y / uiScale);
            // Position after the rect setup: anchoredPosition = zero reset localPosition x/y,
            // which dropped the grid offsetY (grid centred on the foot, lower half underground).
            canvasGo.transform.localPosition = gridPos - Vector3.forward * 0.02f;
            canvasGo.transform.localRotation = Quaternion.Euler(gridEuler);
            // Negative X: the readable face is the ItemGrid +Z side (AlignCanvas keeps the vanilla
            // sign canvas' mirrored scale there). Positive X showed counts mirrored in-game.
            canvasGo.transform.localScale = new Vector3(-uiScale, uiScale, uiScale);

            var root = new GameObject("SacDisplayGrid", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            _gridRoot = rt;

            _slotCount = rows * cols;
            _slots = new SlotUi[_slotCount];

            Plugin.Log.LogDebug("Definition grid UI face id=" + CurrentVisualId()
                + " pos=" + gridPos
                + " size=" + faceMeters
                + " cells=" + cols + "x" + rows
                + " source=" + DisplayLayouts.SourceLabel);

            float padX = 0.01f;
            float padY = 0.01f;
            int slot = 0;
            for (int r = 0; r < rows; r++)
            {
                float y0 = 1f - (r + 1) / (float)rows;
                float y1 = 1f - r / (float)rows;
                for (int c = 0; c < cols; c++)
                {
                    float x0 = c / (float)cols;
                    float x1 = (c + 1) / (float)cols;
                    CreateSlotCell(root.transform, template, font, padX, padY, x0, x1, y0, y1, slot);
                    slot++;
                }
            }
        }

        /// <summary>
        /// v2 definition: one World Space canvas per section grid and per label under SacModel,
        /// all positioned in model space (meters) exactly as authored in PrefabStudio.
        /// </summary>
        private void BuildSectionsUi(TextMeshProUGUI template)
        {
            _headers = null;
            _bandLabels = null;
            _builtBandCount = -1;

            DisplayVisual.Ensure(this);

            DisplayLayouts.LayoutDef layout = DisplayLayouts.GetLayout(LayoutId(), LayoutVariant());
            Transform model = transform.Find("SacModel");
            // Vanilla-sign display: sections hang on an unscaled face anchor at the sign text.
            if (model == null && !string.IsNullOrEmpty(SignLayout))
                model = EnsureSignFace(template);
            if (layout == null || model == null)
            {
                Plugin.Log.LogWarning("Display sections: " + (layout == null ? "no layout" : "SacModel missing")
                    + " for " + LayoutId());
                return;
            }

            // Sign canvas off (found even while inactive); its scale is the UI unit.
            Canvas signCanvas = DisplayVisual.FindCanvas(template.transform);
            float uiScale = 0.01f;
            if (signCanvas != null)
            {
                float s = Mathf.Abs(signCanvas.transform.localScale.x);
                if (s > 0.0001f && s < 0.2f)
                    uiScale = s;
                Transform footGrid = signCanvas.transform.Find("SacDisplayGrid");
                if (footGrid != null)
                    Destroy(footGrid.gameObject);
                signCanvas.gameObject.SetActive(false);
            }
            template.enabled = false;
            template.text = "";

            DestroyOldCanvas(transform);
            DestroyOldCanvas(model);

            var container = new GameObject("SacDisplayCanvas");
            container.transform.SetParent(model, false);

            DisplayLayouts.SectionDef[] defs = layout.sections ?? new DisplayLayouts.SectionDef[0];
            int total = 0;
            for (int k = 0; k < defs.Length; k++)
            {
                if (defs[k] != null)
                    total += Mathf.Max(1, defs[k].grid.columns) * Mathf.Max(1, defs[k].grid.rows);
            }

            _slotCount = total;
            _slots = new SlotUi[total];
            _sections = new SectionUi[defs.Length];
            _columns = Mathf.Max(1, total);
            _rows = 1;
            _gridRoot = null;

            int idx = 0;
            _buildingSections = true;
            try
            {
                BuildSectionCells(template, defs, container.transform, uiScale, ref idx);
            }
            finally
            {
                _buildingSections = false;
            }

            Plugin.Log.LogDebug("Display sections UI id=" + LayoutId()
                + " layout=" + layout.name + " sections=" + defs.Length + " cells=" + total
                + " source=" + DisplayLayouts.SourceLabel);
        }

        private void BuildSectionCells(
            TextMeshProUGUI template, DisplayLayouts.SectionDef[] defs, Transform container, float uiScale, ref int idx)
        {
            for (int k = 0; k < defs.Length; k++)
            {
                DisplayLayouts.SectionDef def = defs[k];
                if (def == null)
                {
                    _sections[k] = new SectionUi { Def = null, Start = idx, Count = 0 };
                    continue;
                }

                DisplayLayouts.SectionGridDef g = def.grid;
                int cols = Mathf.Max(1, g.columns);
                int rows = Mathf.Max(1, g.rows);
                float wM = cols * g.spacingX;
                float hM = rows * g.spacingY;
                RectTransform gridRt = NewSectionCanvas(container, "Grid_" + k, g.offset, g.euler, wM, hM, uiScale);

                // CreateSlotCell / LayoutSlotPair measure against the current board size.
                _boardW = wM / uiScale;
                _boardH = hM / uiScale;
                float font = _boardH / rows * 0.45f * def.amountScale;
                Color amountColor = ParseHtmlColor(def.amountColor, new Color(1f, 0.95f, 0.75f, 1f));

                int start = idx;
                // fill "bottom": slot order starts in the bottom row (columns fill upward).
                bool fillUp = string.Equals(def.fill, "bottom", System.StringComparison.OrdinalIgnoreCase);
                for (int r = 0; r < rows; r++)
                {
                    int rr = fillUp ? rows - 1 - r : r;
                    float y0 = 1f - (rr + 1) / (float)rows;
                    float y1 = 1f - rr / (float)rows;
                    for (int c = 0; c < cols; c++)
                    {
                        CreateSlotCell(gridRt, template, font, 0.01f, 0.01f,
                            c / (float)cols, (c + 1) / (float)cols, y0, y1, idx);
                        StyleSectionCell(idx, def, amountColor);
                        idx++;
                    }
                }

                TextMeshProUGUI label = def.label != null && def.label.enabled
                    ? CreateSectionLabel(container, template, k, def.label, uiScale)
                    : null;
                _sections[k] = new SectionUi { Def = def, Start = start, Count = idx - start, Label = label };
            }
        }

        /// <summary>
        /// Section parent for vanilla-sign displays: at the sign text canvas centre, same rotation
        /// and handedness, but 1 unit = 1 m (Medium/Large signs are scaled non-uniformly). Same
        /// axes as a carved model: +X = player's left, +Y up, −Z towards the player.
        /// </summary>
        private Transform EnsureSignFace(TextMeshProUGUI template)
        {
            Canvas canvas = DisplayVisual.FindCanvas(template.transform);
            if (canvas == null)
                return null;
            Transform face = transform.Find("SacSignFace");
            if (face == null)
            {
                face = new GameObject("SacSignFace").transform;
                face.SetParent(transform, false);
            }

            RectTransform crt = canvas.transform as RectTransform;
            face.position = crt != null ? crt.TransformPoint(crt.rect.center) : canvas.transform.position;
            face.rotation = canvas.transform.rotation;
            // Section canvases mirror X (−uiScale); flip the face so they end up with the same
            // handedness as the sign canvas (readable text).
            float flip = canvas.transform.lossyScale.x < 0f ? 1f : -1f;
            face.localScale = Vector3.one;
            Vector3 ls = face.lossyScale;
            if (Mathf.Abs(ls.x) > 0.0001f && Mathf.Abs(ls.y) > 0.0001f && Mathf.Abs(ls.z) > 0.0001f)
                face.localScale = new Vector3(flip / ls.x, 1f / ls.y, 1f / ls.z);

            if (crt != null)
            {
                Vector3 cs = canvas.transform.lossyScale;
                LogFaceOnce("Display sign face id=" + SignLayout + " size="
                    + (crt.rect.width * Mathf.Abs(cs.x)).ToString("0.00") + "x"
                    + (crt.rect.height * Mathf.Abs(cs.y)).ToString("0.00") + " m");
            }
            return face;
        }

        private static readonly HashSet<string> FaceLogged = new HashSet<string>();

        private static void LogFaceOnce(string msg)
        {
            if (FaceLogged.Add(msg))
                Plugin.Log.LogDebug(msg);
        }

        private static void DestroyOldCanvas(Transform parent)
        {
            Transform old = parent != null ? parent.Find("SacDisplayCanvas") : null;
            if (old == null)
                return;
            old.name = "SacDisplayCanvas_old";
            old.gameObject.SetActive(false);
            Destroy(old.gameObject);
        }

        private static RectTransform NewSectionCanvas(
            Transform parent, string name, Vector3 offset, Vector3 euler, float wM, float hM, float uiScale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(wM / uiScale, hM / uiScale);
            // Same placement rules as BuildDefinitionGridUi: after the rect setup, readable
            // from the +Z face (negative X scale).
            go.transform.localPosition = offset - Vector3.forward * 0.02f;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = new Vector3(-uiScale, uiScale, uiScale);
            return rt;
        }

        private void StyleSectionCell(int i, DisplayLayouts.SectionDef def, Color amountColor)
        {
            SlotUi slot = _slots[i];
            slot.AmountColor = amountColor;
            slot.HideAmount = !def.showAmount;
            if (slot.Amount != null)
            {
                slot.Font = slot.Amount.fontSize;
                slot.Amount.color = amountColor;
            }
            if (slot.Icon != null)
            {
                RectTransform iconRt = slot.Icon.rectTransform;
                if (!def.showAmount)
                {
                    // Icon only: use the whole cell.
                    iconRt.anchorMin = new Vector2(0.06f, 0.06f);
                    iconRt.anchorMax = new Vector2(0.94f, 0.94f);
                    iconRt.offsetMin = Vector2.zero;
                    iconRt.offsetMax = Vector2.zero;
                }
                iconRt.localScale = Vector3.one * Mathf.Clamp(def.iconScale, 0.2f, 2f);
            }
            _slots[i] = slot;
        }

        private static TextMeshProUGUI CreateSectionLabel(
            Transform parent, TextMeshProUGUI template, int k, DisplayLayouts.SectionLabelDef def, float uiScale)
        {
            RectTransform canvasRt = NewSectionCanvas(parent, "Label_" + k, def.offset, def.euler,
                def.width, def.height, uiScale);
            GameObject textGo = Object.Instantiate(template.gameObject, canvasRt);
            textGo.name = "Text";
            textGo.SetActive(true);
            var localize = textGo.GetComponent("Localize") as MonoBehaviour;
            if (localize != null)
                Object.Destroy(localize);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.enabled = true;
            if (template.font != null)
                text.font = template.font;
            text.fontSize = def.fontSize / uiScale;
            // Shrink only when the name is wider than the label (narrow column labels).
            text.enableAutoSizing = true;
            text.fontSizeMax = def.fontSize / uiScale;
            text.fontSizeMin = def.fontSize / uiScale * 0.5f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = Vector4.zero;
            text.outlineWidth = 0f;
            text.raycastTarget = false;
            string align = (def.align ?? "").ToLowerInvariant();
            text.alignment = align == "center" ? TextAlignmentOptions.Midline
                : align == "right" ? TextAlignmentOptions.MidlineRight
                : TextAlignmentOptions.MidlineLeft;
            Color color = ParseHtmlColor(def.color, new Color(1f, 0.85f, 0.4f, 1f));
            text.color = color;
            text.faceColor = color;
            text.text = "";
            return text;
        }

        private static Color ParseHtmlColor(string html, Color fallback)
        {
            if (string.IsNullOrEmpty(html))
                return fallback;
            Color c;
            return ColorUtility.TryParseHtmlString(html.Trim(), out c) ? c : fallback;
        }

        private void BuildLargeUi(GameObject root, TextMeshProUGUI template, float font, float bandFont)
        {
            _headers = null;
            _gridRoot = root.GetComponent<RectTransform>();
            // Row count comes from ApplyScaleToLayout (fewer rows = taller cells at higher scale).
            int rows = Mathf.Max(1, _rows);
            int itemCols = _columns;
            _slotCount = rows * itemCols;
            _builtBandCount = rows;

            float labelW = Mathf.Clamp(_labelWidth, 0.12f, 0.22f);
            float padX = 0.0015f;
            float padY = 0.008f;
            // Same scale coupling as Compact — old fixed band/3 stayed microscopic after Scale changes.
            float labelSize = bandFont * Mathf.Clamp(ContentScaleMul(), 0.7f, 1.55f);
            _chipFont = labelSize;

            _bandLabels = new TextMeshProUGUI[rows];
            for (int r = 0; r < rows; r++)
            {
                GameObject labelGo = Object.Instantiate(template.gameObject, root.transform);
                labelGo.name = "BandLabel" + r;
                labelGo.SetActive(false);
                RectTransform labelRt = labelGo.GetComponent<RectTransform>();
                float y0 = 1f - (r + 1) / (float)rows;
                float y1 = 1f - r / (float)rows;
                labelRt.anchorMin = new Vector2(0f, y0 + padY);
                labelRt.anchorMax = new Vector2(labelW, y1 - padY);
                // -0.3 canvas units left of the grid edge (soft nudge).
                labelRt.offsetMin = new Vector2(-0.3f, 0f);
                labelRt.offsetMax = Vector2.zero;
                labelRt.localScale = Vector3.one;
                labelRt.localRotation = Quaternion.identity;

                TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
                var localize = labelGo.GetComponent("Localize") as MonoBehaviour;
                if (localize != null)
                    Object.Destroy(localize);
                if (template.font != null)
                    label.font = template.font;
                label.enabled = true;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                // Overflow + autosize: never "Wea…" — full words like WEAPONS stay readable.
                label.overflowMode = TextOverflowModes.Overflow;
                label.margin = Vector4.zero;
                label.enableAutoSizing = true;
                label.fontSizeMin = labelSize * 0.55f;
                label.fontSizeMax = labelSize;
                label.fontSize = labelSize;
                label.color = new Color(1f, 0.92f, 0.55f, 1f);
                label.faceColor = new Color32(255, 235, 140, 255);
                label.outlineWidth = 0f;
                label.raycastTarget = false;
                label.text = "";
                _bandLabels[r] = label;
            }

            _slots = new SlotUi[_slotCount];
            int slot = 0;
            for (int r = 0; r < rows; r++)
            {
                float y0 = 1f - (r + 1) / (float)rows;
                float y1 = 1f - r / (float)rows;
                for (int col = 0; col < itemCols; col++)
                {
                    float x0 = labelW + (1f - labelW) * (col / (float)itemCols);
                    float x1 = labelW + (1f - labelW) * ((col + 1) / (float)itemCols);
                    CreateSlotCell(root.transform, template, font, padX, padY,
                        x0, x1, y0, y1, slot);
                    slot++;
                }
            }
        }

        private void CreateSlotCell(
            Transform parent,
            TextMeshProUGUI template,
            float font,
            float padX,
            float padY,
            float x0,
            float x1,
            float y0,
            float y1,
            int index)
        {
            var cell = new GameObject("Slot" + index, typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            RectTransform cellRt = cell.GetComponent<RectTransform>();
            cellRt.anchorMin = new Vector2(x0 + padX, y0 + padY);
            cellRt.anchorMax = new Vector2(x1 - padX, y1 - padY);
            cellRt.offsetMin = Vector2.zero;
            cellRt.offsetMax = Vector2.zero;
            cellRt.localScale = Vector3.one;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(cell.transform, false);
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            float cellWFrac = Mathf.Max(0.001f, (x1 - x0) - 2f * padX);
            float cellHFrac = Mathf.Max(0.001f, (y1 - y0) - 2f * padY);
            SlotPairLayout pair = default(SlotPairLayout);
            // v2 section cells use the icon+count pair on every board size (also Small).
            bool smallCell = _kind == DisplayKind.Small && !_buildingSections;
            if (!smallCell)
            {
                pair = LayoutSlotPair(cellWFrac, cellHFrac, font);
                _slotFont = pair.FontSize;
                iconRt.anchorMin = new Vector2(0f, 0.08f);
                iconRt.anchorMax = new Vector2(pair.IconFrac, 0.92f);
            }
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            Image icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.color = Color.white;
            icon.raycastTarget = false;
            icon.enabled = false;

            GameObject textGo = Object.Instantiate(template.gameObject, cell.transform);
            textGo.name = "Amount";
            textGo.SetActive(true);
            textGo.transform.SetAsLastSibling();
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            if (smallCell)
            {
                // Filled in by LayoutSmallIconAndLabels after name exists.
            }
            else
            {
                textRt.anchorMin = new Vector2(pair.IconFrac + pair.GapFrac, 0.08f);
                textRt.anchorMax = new Vector2(1.00f, 0.92f);
                textRt.pivot = new Vector2(0f, 0.5f);
            }
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
            textRt.sizeDelta = Vector2.zero;
            textRt.localScale = Vector3.one;
            textRt.localRotation = Quaternion.identity;

            TextMeshProUGUI amount = textGo.GetComponent<TextMeshProUGUI>();
            amount.enabled = true;
            var localizeAmt = textGo.GetComponent("Localize") as MonoBehaviour;
            if (localizeAmt != null)
                Object.Destroy(localizeAmt);
            if (template.font != null)
                amount.font = template.font;
            amount.alignment = TextAlignmentOptions.MidlineLeft;
            amount.textWrappingMode = TextWrappingModes.NoWrap;
            amount.overflowMode = TextOverflowModes.Overflow;
            amount.enableAutoSizing = false;
            amount.fontSize = smallCell
                ? Mathf.Max(font, template.fontSize * 0.30f)
                : pair.FontSize;
            amount.color = new Color(1f, 0.95f, 0.75f, 1f);
            amount.faceColor = new Color32(255, 242, 191, 255);
            amount.outlineWidth = 0f;
            amount.raycastTarget = false;
            amount.text = "";

            TextMeshProUGUI nameLabel = null;
            RectTransform nameRt = null;
            if (smallCell)
            {
                GameObject nameGo = Object.Instantiate(template.gameObject, cell.transform);
                nameGo.name = "ItemName";
                nameGo.SetActive(true);
                nameGo.transform.SetAsLastSibling();
                nameRt = nameGo.GetComponent<RectTransform>();
                nameRt.pivot = new Vector2(0.5f, 0.5f);
                nameRt.offsetMin = Vector2.zero;
                nameRt.offsetMax = Vector2.zero;
                nameRt.anchoredPosition = Vector2.zero;
                nameRt.sizeDelta = Vector2.zero;
                nameRt.localScale = Vector3.one;
                nameRt.localRotation = Quaternion.identity;

                nameLabel = nameGo.GetComponent<TextMeshProUGUI>();
                nameLabel.enabled = true;
                var localizeName = nameGo.GetComponent("Localize") as MonoBehaviour;
                if (localizeName != null)
                    Object.Destroy(localizeName);
                if (template.font != null)
                    nameLabel.font = template.font;
                nameLabel.alignment = TextAlignmentOptions.Center;
                nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
                nameLabel.overflowMode = TextOverflowModes.Ellipsis;
                nameLabel.margin = Vector4.zero;
                nameLabel.enableAutoSizing = false;
                nameLabel.fontSize = Mathf.Max(font * 0.55f, template.fontSize * 0.16f);
                nameLabel.color = new Color(1f, 0.92f, 0.55f, 1f);
                nameLabel.faceColor = new Color32(255, 235, 140, 255);
                nameLabel.outlineWidth = 0f;
                nameLabel.raycastTarget = false;
                nameLabel.text = "";

                LayoutSmallIconAndLabels(iconRt, textRt, nameRt);
            }

            _slots[index].Icon = icon;
            _slots[index].Amount = amount;
            _slots[index].Name = nameLabel;
            _slots[index].IsChip = false;
        }

        /// <summary>
        /// Small layouts: All (icon+amount+name) | No name (icon+amount) | Icon only (centered).
        /// </summary>
        private void LayoutSmallIconAndLabels(RectTransform iconRt, RectTransform amountRt, RectTransform nameRt)
        {
            if (iconRt == null)
                return;

            bool showName = ShowName();
            bool showAmount = ShowAmount();

            if (!showName && !showAmount)
            {
                // Icon only — true center (no ghost slot for the count).
                iconRt.anchorMin = new Vector2(0.22f, 0.18f);
                iconRt.anchorMax = new Vector2(0.78f, 0.82f);
                if (amountRt != null)
                {
                    amountRt.anchorMin = Vector2.zero;
                    amountRt.anchorMax = Vector2.zero;
                    amountRt.gameObject.SetActive(false);
                }
                if (nameRt != null)
                {
                    nameRt.anchorMin = Vector2.zero;
                    nameRt.anchorMax = Vector2.zero;
                    nameRt.gameObject.SetActive(false);
                }
                return;
            }

            if (!showName && showAmount)
            {
                // Name off: icon + count, full height, slightly more centered pair.
                iconRt.anchorMin = new Vector2(0.26f, 0.18f);
                iconRt.anchorMax = new Vector2(0.50f, 0.88f);
                if (amountRt != null)
                {
                    amountRt.gameObject.SetActive(true);
                    amountRt.anchorMin = new Vector2(0.52f, 0.18f);
                    amountRt.anchorMax = new Vector2(0.80f, 0.88f);
                    amountRt.pivot = new Vector2(0f, 0.5f);
                }
                if (nameRt != null)
                {
                    nameRt.anchorMin = Vector2.zero;
                    nameRt.anchorMax = Vector2.zero;
                    nameRt.gameObject.SetActive(false);
                }
                return;
            }

            // All on (or name-only from Alt+E): icon + count top, name bottom.
            iconRt.anchorMin = new Vector2(0.28f, 0.38f);
            iconRt.anchorMax = new Vector2(0.50f, 0.92f);
            if (amountRt != null)
            {
                amountRt.gameObject.SetActive(showAmount);
                if (showAmount)
                {
                    amountRt.anchorMin = new Vector2(0.52f, 0.38f);
                    amountRt.anchorMax = new Vector2(0.78f, 0.92f);
                    amountRt.pivot = new Vector2(0f, 0.5f);
                }
                else
                {
                    // Name on, amount off: center icon above the name.
                    iconRt.anchorMin = new Vector2(0.30f, 0.38f);
                    iconRt.anchorMax = new Vector2(0.70f, 0.92f);
                    amountRt.anchorMin = Vector2.zero;
                    amountRt.anchorMax = Vector2.zero;
                }
            }
            if (nameRt != null)
            {
                nameRt.gameObject.SetActive(showName);
                if (showName)
                {
                    nameRt.anchorMin = new Vector2(0.08f, 0.04f);
                    nameRt.anchorMax = new Vector2(0.92f, 0.34f);
                }
            }
        }

        private void SlotCoord(int index, out int col, out int row)
        {
            if (_columnMajor)
            {
                col = index / _rows;
                row = index % _rows;
            }
            else
            {
                col = index % _columns;
                row = index / _columns;
            }
        }

        private int SlotIndex(int col, int row)
        {
            if (_columnMajor)
                return col * _rows + row;
            return row * _columns + col;
        }

        private void Resubscribe()
        {
            _rankVersion++; // the watched chests may change
            NearbyIndex.Tick();
            float range = EffectiveDisplayRange();
            Vector3 origin = transform.position;
            var next = new List<Container>();
            var ids = new List<int>();

            // Prefer registered chests around the board (not only the player scan cache).
            NearbyIndex.CollectNear(origin, range, _watchScratch);
            for (int i = 0; i < _watchScratch.Count; i++)
            {
                Container container = _watchScratch[i];
                if (container == null)
                    continue;
                if (!ContainerFilter.IsPlayerBuiltStorage(container))
                    continue;
                if (!ContainerFilter.PlayerMayUse(container, origin))
                    continue;
                if (ChestNames.IsFullyIgnored(container))
                    continue;

                // Unopened chests often have null/empty inv until Load — read-only for displays.
                // Never force-Load over a full bag (that was the old wipe race).
                Inventory inv = container.GetInventory();
                if (inv == null)
                {
                    NearbyIndex.EnsureInventory(container);
                    inv = container.GetInventory();
                }
                else if (inv.NrOfItems() <= 0 && ContainerFilter.ZdoHasItemPayload(Refs.View(container)?.GetZDO()))
                {
                    NearbyIndex.EnsureInventory(container);
                    inv = container.GetInventory();
                }
                if (inv == null)
                    continue;

                next.Add(container);
                ids.Add(container.GetInstanceID());
            }

            ids.Sort();
            bool same = ids.Count == _watchIds.Count;
            if (same)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    if (ids[i] != _watchIds[i])
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
            {
                // List unchanged: do not reload inventories or force a paint.
                // Counts stay live via Inventory.m_onChanged → MarkDirty.
                return;
            }

            Unwatch();
            foreach (Container container in next)
            {
                Inventory inv = container.GetInventory();
                if (inv != null)
                    inv.m_onChanged += MarkDirty;
                _watched.Add(container);
            }
            _watchIds.Clear();
            _watchIds.AddRange(ids);
            _dirty = true;
            _cluster = null;
        }

        private void Unwatch()
        {
            foreach (Container container in _watched)
            {
                Inventory inv = container != null ? container.GetInventory() : null;
                if (inv != null)
                    inv.m_onChanged -= MarkDirty;
            }
            _watched.Clear();
            _watchIds.Clear();
        }

        /// <summary>
        /// A chest was saved (Container.Save). SAC's own partial takes / deposits save without Inventory.Changed, so
        /// m_onChanged never told the boards: repaint every board that watches this chest.
        /// </summary>
        internal static void OnChestSaved(Container chest)
        {
            if (chest == null || All.Count == 0)
                return;
            int id = chest.GetInstanceID();
            for (int i = 0; i < All.Count; i++)
            {
                StorageDisplayBoard board = All[i];
                if (board != null && board._watchIds.BinarySearch(id) >= 0)
                    board.MarkDirty();
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            _rankVersion++;
        }

        /// <summary>
        /// The boards whose chests and items this board shows: only itself. Boards with the same selection no longer
        /// join into page sets (there are enough display sizes now), every board shows all of its items. Kept as a list
        /// so the paint / ranking code stays the same (one page, its own chests, its own scan).
        /// </summary>
        private List<StorageDisplayBoard> Cluster()
        {
            if (_cluster == null)
                _cluster = new List<StorageDisplayBoard>(1) { this };
            return _cluster;
        }

        private void RefreshClusterPages()
        {
            List<StorageDisplayBoard> cluster = Cluster();
            _pages = Mathf.Max(1, cluster.Count);
            _page = Mathf.Max(0, cluster.IndexOf(this));
        }

        private void Paint()
        {
            if (_slots == null)
                return;

            ApplyTitle();
            ClearHeaders();

            if (_kind == DisplayKind.Small && _sections == null)
            {
                PaintExactItem();
                return;
            }

            List<int> filters = FilterIds();
            List<string> itemTokens = ItemTokens();
            // Small with v2 sections: the hotbar item (1-8) counts as one more selected item.
            if (_kind == DisplayKind.Small)
            {
                string hotbar = ItemToken();
                if (!string.IsNullOrEmpty(hotbar) && !itemTokens.Contains(hotbar))
                    itemTokens.Add(hotbar);
            }
            if (filters.Count == 0 && itemTokens.Count == 0)
            {
                for (int i = 0; i < _slotCount; i++)
                    ClearSlot(i);
                // v2: sign canvas is off, so the hint goes into the first label.
                ClearSectionLabels(Loc.T("Select type", "Typ wählen"));
                return;
            }

            List<StorageDisplayBoard> cluster = Cluster();
            _pages = Mathf.Max(1, cluster.Count);
            _page = Mathf.Max(0, cluster.IndexOf(this));

            if (_sections != null)
            {
                PaintSections(cluster, filters, itemTokens);
                return;
            }

            // Custom displays: one filtered item + count per ItemGrid cell (no category bands).
            if (UsesDefinitionGrid())
            {
                PaintRanked(cluster, filters, itemTokens);
                return;
            }

            if (IsCompactLayout())
            {
                PaintCompactFlow(cluster, filters, itemTokens);
                return;
            }

            if (_kind == DisplayKind.Large && _flowSections)
            {
                PaintLargeSections(cluster, filters, itemTokens);
                return;
            }

            PaintRanked(cluster, filters, itemTokens);
        }

        private void PaintExactItem()
        {
            string token = ItemToken();
            if (string.IsNullOrEmpty(token))
            {
                for (int i = 0; i < _slotCount; i++)
                    ClearSlot(i);
                return;
            }

            string shared = ItemIds.SharedFromToken(token);
            List<StorageDisplayBoard> cluster = Cluster();
            int total = 0;
            var seenChest = new HashSet<int>();
            ItemDrop.ItemData sample = null;
            foreach (StorageDisplayBoard board in cluster)
            {
                if (board == null)
                    continue;
                foreach (Container container in board._watched)
                {
                    if (container == null || !seenChest.Add(container.GetInstanceID()))
                        continue;
                    Inventory inv = container.GetInventory();
                    if (inv == null)
                    {
                        NearbyIndex.EnsureInventory(container);
                        inv = container.GetInventory();
                    }
                    else if (inv.NrOfItems() <= 0 && ContainerFilter.ZdoHasItemPayload(Refs.View(container)?.GetZDO()))
                    {
                        NearbyIndex.EnsureInventory(container);
                        inv = container.GetInventory();
                    }
                    if (inv == null)
                        continue;

                    int n = 0;
                    if (!string.IsNullOrEmpty(shared))
                        n = inv.CountItems(shared, -1, true);
                    if (n <= 0)
                    {
                        foreach (ItemDrop.ItemData item in inv.GetAllItems())
                        {
                            if (item?.m_shared == null || item.m_stack <= 0)
                                continue;
                            if (!ItemIds.Matches(item, token) && !ItemIds.Matches(item, shared))
                                continue;
                            n += item.m_stack;
                            if (sample == null)
                                sample = item;
                        }
                    }
                    else if (sample == null)
                    {
                        foreach (ItemDrop.ItemData item in inv.GetAllItems())
                        {
                            if (item?.m_shared == null || item.m_stack <= 0)
                                continue;
                            if (ItemIds.Matches(item, token) || ItemIds.Matches(item, shared))
                            {
                                sample = item;
                                break;
                            }
                        }
                    }

                    total += n;
                }
            }

            if (sample == null)
                sample = SampleFromToken(token);

            for (int i = 0; i < _slotCount; i++)
            {
                if (i == 0 && sample != null)
                {
                    _slots[i].Icon.sprite = StackLimits.Icon(sample);
                    _slots[i].Icon.enabled = _slots[i].Icon.sprite != null;
                    _slots[i].Icon.color = Color.white;
                    _slots[i].Shared = sample.m_shared != null ? sample.m_shared.m_name : null;
                    SetAmount(i, FormatCount(total));
                    SetItemName(i, ItemLabel(sample.m_shared != null ? sample.m_shared.m_name : token));
                    MarkBelowMinimum(i, _slots[i].Shared, total); // SAC-MIN
                }
                else
                {
                    ClearSlot(i);
                }
            }
        }

        private void PaintCompactFlow(
            List<StorageDisplayBoard> cluster,
            List<int> filters,
            List<string> itemTokens)
        {
            ClearHeaders();
            if (_bandLabels != null)
            {
                for (int i = 0; i < _bandLabels.Length; i++)
                {
                    if (_bandLabels[i] == null)
                        continue;
                    _bandLabels[i].text = "";
                    _bandLabels[i].gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < _slotCount; i++)
                ClearSlot(i);

            var ranked = RankItems(cluster, filters, itemTokens, groupByCategory: true);
            if (ranked == null || ranked.Count == 0)
                return;

            int cols = Mathf.Max(1, _columns);
            int slot = 0;
            int lastCat = int.MinValue;
            for (int i = 0; i < ranked.Count; i++)
            {
                RankedItem entry = ranked[i];
                if (entry.CategoryId != lastCat)
                {
                    // New category on a new row; label sits on the fixed left rail.
                    if (slot % cols != 0)
                        slot += cols - (slot % cols);

                    if (slot >= _slotCount)
                        break;

                    int row = slot / cols;
                    if (_bandLabels != null && row >= 0 && row < _bandLabels.Length && _bandLabels[row] != null)
                    {
                        _bandLabels[row].gameObject.SetActive(true);
                        if (_chipFont > 0.01f)
                            _bandLabels[row].fontSize = _chipFont;
                        _bandLabels[row].text = ShortCategoryLabel(entry.CategoryId);
                    }

                    lastCat = entry.CategoryId;
                }

                if (slot >= _slotCount)
                    break;

                bool moreRemain = i < ranked.Count - 1;
                if (moreRemain && slot == _slotCount - 1)
                {
                    ClearSlot(slot);
                    SetAmount(slot, "+", new Color(1f, 0.85f, 0.45f, 1f));
                    slot++;
                    break;
                }

                PaintSlot(slot, entry);
                slot++;
            }
        }

        private void PaintChip(int i, string label)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            _slots[i].Shared = null;
            if (_slots[i].Icon != null)
            {
                _slots[i].Icon.enabled = false;
                _slots[i].Icon.sprite = null;
            }
            SetItemName(i, "");
            _slots[i].IsChip = true;
            TextMeshProUGUI amount = _slots[i].Amount;
            if (amount == null)
                return;
            // Left-aligned header cell — reads as a row label, not another item.
            amount.alignment = TextAlignmentOptions.MidlineLeft;
            if (_chipFont > 0.01f)
                amount.fontSize = _chipFont * 1.15f;
            amount.color = new Color(1f, 0.88f, 0.35f, 1f);
            amount.faceColor = new Color32(255, 220, 80, 255);
            amount.text = label ?? "";
            amount.enabled = true;
        }

        private static ItemDrop.ItemData SampleFromToken(string token)
        {
            GameObject prefab = ItemIds.PrefabFromToken(token);
            if (prefab == null)
                return null;
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            return drop != null ? drop.m_itemData : null;
        }

        private void PaintLargeSections(
            List<StorageDisplayBoard> cluster,
            List<int> filters,
            List<string> itemTokens)
        {
            ClearHeaders();
            List<int> sections = BuildSectionOrder(filters, itemTokens);

            for (int i = 0; i < _slotCount; i++)
            {
                if (_slots != null && i < _slots.Length && _slots[i].Icon != null)
                    ClearSlot(i);
            }

            if (_bandLabels != null)
            {
                for (int i = 0; i < _bandLabels.Length; i++)
                {
                    if (_bandLabels[i] == null)
                        continue;
                    _bandLabels[i].text = "";
                    _bandLabels[i].gameObject.SetActive(false);
                }
            }

            if (sections.Count == 0)
                return;

            int rows = Mathf.Max(1, _rows);
            int itemCols = Mathf.Max(1, _columns);

            // One chest scan for the whole board — not once per category.
            var rankedAll = RankItems(cluster, filters, itemTokens, groupByCategory: true);
            var byCat = new Dictionary<int, List<RankedItem>>();
            for (int i = 0; i < rankedAll.Count; i++)
            {
                RankedItem entry = rankedAll[i];
                int id = entry.CategoryId;
                List<RankedItem> list;
                if (!byCat.TryGetValue(id, out list))
                {
                    list = new List<RankedItem>();
                    byCat[id] = list;
                }
                list.Add(entry);
            }

            // Pack categories top→bottom: each uses only the rows it needs, next category
            // starts on the following free row (no overlap / no wasted empty bands).
            int cursor = 0;
            for (int c = 0; c < sections.Count; c++)
            {
                if (cursor >= rows)
                    break;

                int catId = sections[c];
                List<RankedItem> ranked;
                if (!byCat.TryGetValue(catId, out ranked) || ranked == null)
                    ranked = new List<RankedItem>();

                int itemCount = Mathf.Max(1, ranked.Count); // empty cat still shows a "0" row
                int rowsNeeded = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)itemCols));
                int rowsLeft = rows - cursor;
                int rowsForThis = Mathf.Min(rowsNeeded, rowsLeft);
                if (rowsForThis <= 0)
                    break;

                int row0 = cursor;
                if (_bandLabels != null && row0 < _bandLabels.Length && _bandLabels[row0] != null)
                {
                    _bandLabels[row0].gameObject.SetActive(true);
                    _bandLabels[row0].text = ShortCategoryLabel(catId);
                }

                int capacity = rowsForThis * itemCols;
                int baseSlot = row0 * itemCols;

                if (ranked.Count == 0)
                {
                    if (baseSlot < _slotCount)
                        SetAmount(baseSlot, "0", new Color(0.75f, 0.7f, 0.55f, 1f));
                    cursor = row0 + rowsForThis;
                    continue;
                }

                bool truncated = ranked.Count > capacity || (c < sections.Count - 1 && cursor + rowsNeeded > rows);
                int show = truncated && capacity > 0
                    ? Mathf.Min(capacity - 1, ranked.Count)
                    : Mathf.Min(capacity, ranked.Count);
                for (int i = 0; i < show; i++)
                {
                    int slot = baseSlot + i;
                    if (slot >= _slotCount || _slots[slot].Icon == null)
                        break;
                    PaintSlot(slot, ranked[i]);
                }

                if (truncated && capacity > 0)
                {
                    int overflowSlot = baseSlot + capacity - 1;
                    if (overflowSlot < _slotCount)
                    {
                        ClearSlot(overflowSlot);
                        SetAmount(overflowSlot, "+",
                            new Color(1f, 0.85f, 0.45f, 1f));
                    }
                }

                cursor = row0 + rowsForThis;
            }
        }

        /// <summary>
        /// v2 sections: "1".."n" = n-th selected category (display order), "rest" = selected
        /// categories no numbered section took, "all" = every matching item. Each section gets
        /// its own label (category name unless the definition sets text).
        /// </summary>
        private void PaintSections(
            List<StorageDisplayBoard> cluster,
            List<int> filters,
            List<string> itemTokens)
        {
            List<int> order = BuildSectionOrder(filters, itemTokens);
            // One chest scan for the whole board.
            List<RankedItem> rankedAll = RankItems(cluster, filters, itemTokens, groupByCategory: true);
            var byCat = new Dictionary<int, List<RankedItem>>();
            for (int i = 0; i < rankedAll.Count; i++)
            {
                List<RankedItem> list;
                if (!byCat.TryGetValue(rankedAll[i].CategoryId, out list))
                {
                    list = new List<RankedItem>();
                    byCat[rankedAll[i].CategoryId] = list;
                }
                list.Add(rankedAll[i]);
            }

            // Categories claimed by numbered sections (for "rest").
            var claimed = new HashSet<int>();
            for (int s = 0; s < _sections.Length; s++)
            {
                int n;
                if (_sections[s] != null && _sections[s].Def != null
                    && TrySectionIndex(_sections[s].Def.category, out n) && n <= order.Count)
                    claimed.Add(order[n - 1]);
            }

            for (int s = 0; s < _sections.Length; s++)
            {
                SectionUi sec = _sections[s];
                if (sec == null || sec.Def == null)
                    continue;

                string cat = (sec.Def.category ?? "").Trim().ToLowerInvariant();
                List<RankedItem> items;
                string title;
                bool active = true;
                int n;
                if (TrySectionIndex(cat, out n))
                {
                    if (n <= order.Count)
                    {
                        int catId = order[n - 1];
                        if (!byCat.TryGetValue(catId, out items))
                            items = new List<RankedItem>();
                        title = DisplayFilters.Label(catId);
                    }
                    else
                    {
                        // Fewer categories selected than sections — leave this one blank.
                        items = new List<RankedItem>();
                        title = "";
                        active = false;
                    }
                }
                else if (cat == "rest")
                {
                    items = new List<RankedItem>();
                    var names = new List<string>();
                    for (int o = 0; o < order.Count; o++)
                    {
                        if (claimed.Contains(order[o]))
                            continue;
                        names.Add(ShortCategoryLabel(order[o]));
                        List<RankedItem> part;
                        if (byCat.TryGetValue(order[o], out part))
                            items.AddRange(part);
                    }
                    title = string.Join(", ", names.ToArray());
                    active = names.Count > 0;
                }
                else
                {
                    items = rankedAll;
                    title = DisplayFilters.Label(filters, itemTokens);
                }

                SortSectionItems(items, sec.Def.sort);
                SetSectionLabel(sec, active ? title : "");
                PaintSectionCells(sec, items, active);
            }
        }

        private static bool TrySectionIndex(string category, out int n)
        {
            n = 0;
            return !string.IsNullOrEmpty(category)
                && int.TryParse(category.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out n)
                && n >= 1;
        }

        private static void SortSectionItems(List<RankedItem> items, string sort)
        {
            if (items == null || items.Count < 2)
                return;
            if (string.Equals(sort, "name", System.StringComparison.OrdinalIgnoreCase))
            {
                items.Sort((a, b) => string.Compare(LocalizedName(a), LocalizedName(b),
                    System.StringComparison.CurrentCultureIgnoreCase));
                return;
            }
            items.Sort((a, b) =>
            {
                int byCount = b.Count.CompareTo(a.Count);
                return byCount != 0 ? byCount : string.CompareOrdinal(LocalizedName(a), LocalizedName(b));
            });
        }

        private static string LocalizedName(RankedItem entry)
        {
            string raw = entry.Sample?.m_shared != null ? entry.Sample.m_shared.m_name : "";
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        private void SetSectionLabel(SectionUi sec, string title)
        {
            if (sec.Label == null)
                return;
            DisplayLayouts.SectionLabelDef def = sec.Def.label;
            string text = string.IsNullOrEmpty(title) ? ""
                : !string.IsNullOrEmpty(def.text) ? def.text
                : title;
            if (def.uppercase)
                text = text.ToUpperInvariant();
            if (sec.Label.text != text)
                sec.Label.text = text;
        }

        private void PaintSectionCells(SectionUi sec, List<RankedItem> items, bool active)
        {
            int capacity = sec.Count;
            for (int i = 0; i < capacity; i++)
                ClearSlot(sec.Start + i);
            if (!active || capacity <= 0)
                return;

            if (items.Count == 0)
            {
                if (!string.IsNullOrEmpty(sec.Def.emptyText))
                    SetAmount(sec.Start, sec.Def.emptyText, new Color(0.75f, 0.7f, 0.55f, 1f));
                return;
            }

            // Last cell turns into "+N" when the section overflows.
            bool overflow = items.Count > capacity;
            int show = overflow ? capacity - 1 : items.Count;
            for (int i = 0; i < show; i++)
            {
                int slot = sec.Start + i;
                PaintSlot(slot, items[i]);
                if (_slots[slot].HideAmount)
                    SetAmount(slot, "");
            }
            if (overflow)
                SetAmount(sec.Start + capacity - 1, "+" + (items.Count - show), new Color(1f, 0.85f, 0.45f, 1f));
            for (int i = 0; i < capacity; i++)
                CenterSectionPair(sec.Start + i);
        }

        /// <summary>
        /// v2 section cell: icon + amount as one centred group (wide cells left them hugging
        /// the left edge). Measured per paint so short and long counts both stay centred.
        /// </summary>
        private void CenterSectionPair(int i)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            SlotUi slot = _slots[i];
            if (slot.HideAmount || slot.Icon == null || slot.Amount == null)
                return; // icon-only cells already fill the cell centred
            RectTransform iconRt = slot.Icon.rectTransform;
            RectTransform textRt = slot.Amount.rectTransform;
            RectTransform cellRt = iconRt.parent as RectTransform;
            if (cellRt == null)
                return;
            float w = cellRt.rect.width;
            float h = cellRt.rect.height;
            if (w < 0.01f || h < 0.01f)
                return;

            bool hasIcon = slot.Icon.enabled && slot.Icon.sprite != null;
            float iconBox = Mathf.Min(h * 0.84f, w * 0.55f);
            float scale = Mathf.Abs(iconRt.localScale.x) > 0.01f ? Mathf.Abs(iconRt.localScale.x) : 1f;
            float iconW = hasIcon ? iconBox * scale : 0f;
            string text = slot.Amount.text ?? "";
            float textW = string.IsNullOrEmpty(text) ? 0f : slot.Amount.GetPreferredValues(text).x;
            float gap = hasIcon && textW > 0f ? Mathf.Max(0.01f, h * 0.06f) : 0f;
            float left = Mathf.Max(0f, (w - (iconW + gap + textW)) * 0.5f);

            // Icon rect keeps its unscaled box; centre it inside the scaled footprint.
            float iconCenter = left + iconW * 0.5f;
            iconRt.anchorMin = new Vector2(0f, 0.08f);
            iconRt.anchorMax = new Vector2(0f, 0.92f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.offsetMin = new Vector2(iconCenter - iconBox * 0.5f, 0f);
            iconRt.offsetMax = new Vector2(iconCenter + iconBox * 0.5f, 0f);

            float textX = left + iconW + gap;
            textRt.anchorMin = new Vector2(0f, 0.08f);
            textRt.anchorMax = new Vector2(0f, 0.92f);
            textRt.pivot = new Vector2(0f, 0.5f);
            textRt.offsetMin = new Vector2(textX, 0f);
            textRt.offsetMax = new Vector2(textX + textW + 1f, 0f);
        }

        private void ClearSectionLabels(string hint)
        {
            if (_sections == null)
                return;
            bool hinted = false;
            for (int s = 0; s < _sections.Length; s++)
            {
                SectionUi sec = _sections[s];
                if (sec == null || sec.Label == null)
                    continue;
                string text = !hinted ? hint ?? "" : "";
                hinted = true;
                if (sec.Label.text != text)
                    sec.Label.text = text;
            }
        }

        private static string ShortCategoryLabel(int catId)
        {
            string label = DisplayFilters.Label(catId);
            if (string.IsNullOrEmpty(label))
                return "?";
            // Keep readable but avoid eating item space (CROPS & SEEDS → CROPS).
            int amp = label.IndexOf('&');
            if (amp > 0)
                label = label.Substring(0, amp).Trim();
            int space = label.IndexOf(' ');
            if (space > 0 && label.Length > 10)
                label = label.Substring(0, space);
            // Allow full single words like WEAPONS / UTILITY / INGREDIENTS.
            if (label.Length > 14)
                label = label.Substring(0, 14);
            return label.ToUpperInvariant();
        }

        /// <summary>
        /// Ordered category sections from selected filters + parents of item tokens.
        /// Empty selected categories stay in the list so the board shows 0 stock.
        /// </summary>
        private static List<int> BuildSectionOrder(List<int> filters, List<string> itemTokens)
        {
            var sections = new List<int>();
            var seen = new HashSet<int>();

            if (filters != null)
            {
                for (int i = 0; i < filters.Count; i++)
                {
                    int id = filters[i];
                    if (id <= 0 || !seen.Add(id))
                        continue;
                    DisplayFilter unused;
                    if (DisplayFilters.TryGet(id, out unused))
                        sections.Add(id);
                }
            }

            if (itemTokens != null)
            {
                for (int i = 0; i < itemTokens.Count; i++)
                {
                    int parent = DisplayFilters.ParentFilterIdFromToken(itemTokens[i]);
                    if (parent <= 0 || !seen.Add(parent))
                        continue;
                    sections.Add(parent);
                }
            }

            sections.Sort((a, b) =>
                DisplayFilters.CategorySortOrder(a).CompareTo(DisplayFilters.CategorySortOrder(b)));
            return sections;
        }

        private static void SplitSelectionForSection(
            int catId,
            List<int> filters,
            List<string> itemTokens,
            out List<int> oneFilter,
            out List<string> oneTokens)
        {
            oneFilter = null;
            oneTokens = null;
            bool whole = filters != null && filters.Contains(catId);
            if (whole)
            {
                oneFilter = new List<int> { catId };
                return;
            }

            // Only specific items under this category.
            oneTokens = new List<string>();
            if (itemTokens == null)
                return;
            for (int i = 0; i < itemTokens.Count; i++)
            {
                string token = itemTokens[i];
                if (DisplayFilters.ParentFilterIdFromToken(token) == catId)
                    oneTokens.Add(token);
            }
        }

        private void PaintLargeByFilter(List<StorageDisplayBoard> cluster, List<int> filters)
        {
            // Legacy path kept unused; flowing sections replaced fixed 3-column layout.
            PaintLargeSections(cluster, filters, null);
        }

        private void PaintRanked(
            List<StorageDisplayBoard> cluster,
            List<int> filters,
            List<string> itemTokens)
        {
            bool groupByCategory = _kind == DisplayKind.Medium;
            var ranked = RankItems(cluster, filters, itemTokens, groupByCategory);
            int start = _page * _slotCount;
            bool lastBoard = _page >= _pages - 1;
            int remaining = Mathf.Max(0, ranked.Count - start);
            int extraAfterThis = Mathf.Max(0, remaining - _slotCount);
            int shown = remaining;
            if (lastBoard && extraAfterThis > 0)
                shown = _slotCount - 1;
            else
                shown = Mathf.Min(_slotCount, remaining);

            if (_columnHeaders)
            {
                string cats = DisplayFilters.Label(filters, itemTokens);
                if (_headerColumns >= 1)
                    SetHeader(0, cats);
                for (int h = 1; h < _headerColumns; h++)
                    SetHeader(h, "");
            }

            for (int i = 0; i < _slotCount; i++)
            {
                if (lastBoard && extraAfterThis > 0 && i == _slotCount - 1)
                {
                    ClearSlot(i);
                    SetAmount(i, "+" + extraAfterThis);
                    continue;
                }

                int index = start + i;
                if (i >= shown || index >= ranked.Count)
                {
                    ClearSlot(i);
                    continue;
                }

                PaintSlot(i, ranked[index]);
            }
        }

        private struct RankedItem
        {
            public ItemDrop.ItemData Sample;
            public int Count;
            public int CategoryId;
            public int CategoryOrder;
        }

        // ---- ranking (what the board shows): chest scan in time slices + shared per cluster ----
        //
        // The chest scan (all stacks of all watched chests) is the expensive part of a repaint (6-20 ms). It runs as a
        // job that gets about RankBudgetMs per frame, so a repaint no longer lands in one long frame. All boards of a
        // cluster share one scan per change. _rankVersion goes up whenever a watched chest changes (or the watched
        // chests change). SliceRanking = false goes back to one scan in one go (the old behavior).
        private const bool SliceRanking = true;
        private const float RankBudgetMs = 2f;

        private static int _rankVersion;
        private bool _refreshAfterPaint;
        private float _dirtySince = -1f;
        private float _bornAt = -1f;
        /// <summary>Just after a board appears (join, walking up) chests keep loading in: look for new ones more often.</summary>
        private const float YoungBoardSeconds = 90f;
        /// <summary>A board that waited this long for its repaint (other boards busy, a scan that does not finish) paints at once.</summary>
        private const float OverdueSeconds = 5f;
        private float _scanSeconds; // time of the scan that just finished (it counts for the pause before the next repaint)

        private sealed class AggResult
        {
            public readonly Dictionary<string, int> Totals = new Dictionary<string, int>();
            public readonly Dictionary<string, ItemDrop.ItemData> Sample = new Dictionary<string, ItemDrop.ItemData>();
            public int Version;
            public int Frame;
            // Sorted lists built from this scan, once per scan (every board of the cluster uses them).
            public List<RankedItem> RankedByCategory;
            public List<RankedItem> RankedByCount;
        }

        private sealed class AggJob
        {
            public readonly AggResult Result = new AggResult();
            private readonly IEnumerator<bool> _steps;
            public double ElapsedSeconds;

            public AggJob(List<Container> chests, List<int> filters, List<string> itemTokens)
            {
                Result.Version = _rankVersion;
                _steps = AggregateSteps(chests, filters, itemTokens, Result);
            }

            /// <summary>Runs for about the budget; true when the scan is finished.</summary>
            public bool Step(float budgetMs)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                long end = start + (long)(budgetMs / 1000.0 * System.Diagnostics.Stopwatch.Frequency);
                bool done = false;
                do
                {
                    if (!_steps.MoveNext())
                    {
                        done = true;
                        break;
                    }
                }
                while (System.Diagnostics.Stopwatch.GetTimestamp() < end);
                ElapsedSeconds += (System.Diagnostics.Stopwatch.GetTimestamp() - start) / (double)System.Diagnostics.Stopwatch.Frequency;
                return done;
            }
        }

        private static readonly Dictionary<string, AggResult> AggCache = new Dictionary<string, AggResult>();
        private static readonly Dictionary<string, AggJob> AggJobs = new Dictionary<string, AggJob>();

        private static string AggKey(
            List<StorageDisplayBoard> cluster,
            List<int> filters,
            List<string> itemTokens)
        {
            if (cluster == null)
                return null;
            var ids = new List<int>(cluster.Count);
            for (int i = 0; i < cluster.Count; i++)
            {
                if (cluster[i] != null)
                    ids.Add(cluster[i].GetInstanceID());
            }
            ids.Sort();
            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < ids.Count; i++)
                sb.Append(ids[i]).Append(',');
            sb.Append('|');
            if (filters != null)
            {
                for (int i = 0; i < filters.Count; i++)
                    sb.Append(filters[i]).Append(',');
            }
            sb.Append('|');
            if (itemTokens != null)
            {
                for (int i = 0; i < itemTokens.Count; i++)
                    sb.Append(itemTokens[i]).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>The chests of all boards in the cluster, once each (a copy: the boards' lists change over time).</summary>
        private static List<Container> SnapshotChests(List<StorageDisplayBoard> cluster)
        {
            var chests = new List<Container>();
            var seen = new HashSet<int>();
            if (cluster == null)
                return chests;
            foreach (StorageDisplayBoard board in cluster)
            {
                if (board == null)
                    continue;
                foreach (Container container in board._watched)
                {
                    if (container != null && seen.Add(container.GetInstanceID()))
                        chests.Add(container);
                }
            }
            return chests;
        }

        /// <summary>Filter answers of one selection (filters + items), per item type.</summary>
        private sealed class Verdicts
        {
            public float Created;
            public readonly Dictionary<ItemDrop.ItemData.SharedData, bool> Match = new Dictionary<ItemDrop.ItemData.SharedData, bool>();
            public readonly Dictionary<ItemDrop.ItemData.SharedData, GameObject> Prefab = new Dictionary<ItemDrop.ItemData.SharedData, GameObject>();
        }

        /// <summary>Kept this long (s). The filter lists are fixed tables, the answer depends on the item type only.</summary>
        private const float VerdictSeconds = 300f;
        private static readonly Dictionary<string, Verdicts> VerdictCache = new Dictionary<string, Verdicts>();

        private static Verdicts VerdictsFor(List<int> filters, List<string> itemTokens)
        {
            var sb = new System.Text.StringBuilder(64);
            if (filters != null)
            {
                for (int i = 0; i < filters.Count; i++)
                    sb.Append(filters[i]).Append(',');
            }
            sb.Append('|');
            if (itemTokens != null)
            {
                for (int i = 0; i < itemTokens.Count; i++)
                    sb.Append(itemTokens[i]).Append(';');
            }
            string key = sb.ToString();

            Verdicts v;
            if (VerdictCache.TryGetValue(key, out v) && Time.unscaledTime - v.Created < VerdictSeconds)
                return v;
            if (VerdictCache.Count > 64)
                VerdictCache.Clear();
            v = new Verdicts { Created = Time.unscaledTime };
            VerdictCache[key] = v;
            return v;
        }

        /// <summary>Walks the chests, one chest per step: adds up the matching stacks per item type.</summary>
        private static IEnumerator<bool> AggregateSteps(
            List<Container> chests,
            List<int> filters,
            List<string> itemTokens,
            AggResult result)
        {
            // Same item type (shared data + prefab) = same filter answer: ask once per type, not once per stack.
            // The answers are kept per selection across scans (VerdictsFor), not rebuilt for every scan.
            Verdicts verdicts = VerdictsFor(filters, itemTokens);
            Dictionary<ItemDrop.ItemData.SharedData, bool> verdict = verdicts.Match;
            Dictionary<ItemDrop.ItemData.SharedData, GameObject> verdictPrefab = verdicts.Prefab;

            for (int c = 0; c < chests.Count; c++)
            {
                Container container = chests[c];
                if (container == null)
                    continue;

                // Prefer already-loaded inventories; only ZDO-load when the bag is missing.
                // Never Load over a populated bag, and never Load when the ZDO has no payload
                // (that empty Load + Save race wiped iron chests next to Storage Displays).
                Inventory inv = container.GetInventory();
                if (inv == null)
                {
                    NearbyIndex.EnsureInventory(container);
                    inv = container.GetInventory();
                }
                else if (inv.NrOfItems() <= 0 && ContainerFilter.ZdoHasItemPayload(Refs.View(container)?.GetZDO()))
                {
                    NearbyIndex.EnsureInventory(container);
                    inv = container.GetInventory();
                }
                if (inv == null || inv.NrOfItems() <= 0)
                    continue;

                // Per chest the stacks are already added up per item type (ChestSummary, rebuilt when the chest changes).
                List<ChestSummary.Group> groups = ChestSummary.Groups(inv);
                if (groups == null)
                    continue;
                for (int g = 0; g < groups.Count; g++)
                {
                    ChestSummary.Group group = groups[g];
                    ItemDrop.ItemData item = group.Sample;
                    bool match;
                    GameObject seenPrefab;
                    if (verdict.TryGetValue(group.Shared, out match)
                        && verdictPrefab.TryGetValue(group.Shared, out seenPrefab)
                        && seenPrefab == group.Prefab)
                    {
                        // known type
                    }
                    else
                    {
                        match = DisplayFilters.MatchesSelection(item, filters, itemTokens);
                        verdict[group.Shared] = match;
                        verdictPrefab[group.Shared] = group.Prefab;
                    }
                    if (!match)
                        continue;
                    string key = group.Shared.m_name;
                    int n;
                    result.Totals.TryGetValue(key, out n);
                    result.Totals[key] = n + group.Count;
                    if (!result.Sample.ContainsKey(key))
                        result.Sample[key] = item;
                }

                yield return true;
            }
        }

        /// <summary>
        /// Before a repaint: moves the chest scan along (about RankBudgetMs). False = not finished, try again next
        /// frame (the board stays dirty). True = the scan is ready, the paint can use it right now.
        /// </summary>
        private bool PrepareRank()
        {
            // Same selection the paint will ask for (see Paint).
            if (_kind == DisplayKind.Small && _sections == null)
                return true;
            List<int> filters = FilterIds();
            List<string> itemTokens = ItemTokens();
            if (_kind == DisplayKind.Small)
            {
                string hotbar = ItemToken();
                if (!string.IsNullOrEmpty(hotbar) && !itemTokens.Contains(hotbar))
                    itemTokens.Add(hotbar);
            }
            if (filters.Count == 0 && itemTokens.Count == 0)
                return true;

            List<StorageDisplayBoard> cluster = Cluster();
            string key = AggKey(cluster, filters, itemTokens);
            if (key == null)
                return true;

            AggResult cached;
            if (AggCache.TryGetValue(key, out cached) && cached.Version == _rankVersion)
                return true;

            AggJob job;
            if (!AggJobs.TryGetValue(key, out job))
            {
                if (AggJobs.Count > 8)
                    AggJobs.Clear(); // jobs of selections nobody asks for any more
                job = new AggJob(SnapshotChests(cluster), filters, itemTokens);
                AggJobs[key] = job;
            }

            if (!job.Step(RankBudgetMs))
                return false;

            AggJobs.Remove(key);
            _scanSeconds = (float)job.ElapsedSeconds;
            job.Result.Frame = Time.frameCount;
            // One cached scan per board (boards no longer share one per page set): room for big bases.
            if (AggCache.Count > 256)
                AggCache.Clear();
            AggCache[key] = job.Result;
            // A chest changed while the scan was running: paint with it now, and again right after.
            if (job.Result.Version != _rankVersion)
                _refreshAfterPaint = true;
            return true;
        }

        private List<RankedItem> RankItems(
            List<StorageDisplayBoard> cluster,
            List<int> filters,
            List<string> itemTokens,
            bool groupByCategory = false)
        {
            bool hasFilters = filters != null && filters.Count > 0;
            bool hasItems = itemTokens != null && itemTokens.Count > 0;
            if (!hasFilters && !hasItems)
                return new List<RankedItem>();

            string key = AggKey(cluster, filters, itemTokens);
            AggResult agg;
            // Valid: nothing changed since the scan, or the scan finished this very frame (PrepareRank).
            if (key == null || !AggCache.TryGetValue(key, out agg)
                || (agg.Version != _rankVersion && agg.Frame != Time.frameCount))
            {
                // Nobody prepared it (or slicing is off): scan in one go.
                var job = new AggJob(SnapshotChests(cluster), filters, itemTokens);
                job.Step(float.MaxValue);
                agg = job.Result;
                agg.Frame = Time.frameCount;
                if (key != null)
                {
                    if (AggCache.Count > 256)
                        AggCache.Clear();
                    AggCache[key] = agg;
                }
            }

            return BuildRanked(agg, filters, itemTokens, groupByCategory);
        }

        /// <summary>Sorted list for the paint (callers sort and change it, so each call builds its own).</summary>
        private static List<RankedItem> BuildRanked(
            AggResult agg,
            List<int> filters,
            List<string> itemTokens,
            bool groupByCategory)
        {
            // Callers sort and change their list: hand out copies.
            List<RankedItem> known = groupByCategory ? agg.RankedByCategory : agg.RankedByCount;
            if (known != null)
                return new List<RankedItem>(known);

            var ranked = new List<RankedItem>(agg.Totals.Count);
            foreach (KeyValuePair<string, int> pair in agg.Totals)
            {
                ItemDrop.ItemData item;
                agg.Sample.TryGetValue(pair.Key, out item);
                int catId = CategoryIdForItem(item, filters, itemTokens);
                ranked.Add(new RankedItem
                {
                    Sample = item,
                    Count = pair.Value,
                    CategoryId = catId,
                    CategoryOrder = DisplayFilters.CategorySortOrder(catId)
                });
            }

            if (groupByCategory)
            {
                ranked.Sort((x, y) =>
                {
                    int byCat = x.CategoryOrder.CompareTo(y.CategoryOrder);
                    if (byCat != 0)
                        return byCat;
                    int byCount = y.Count.CompareTo(x.Count);
                    if (byCount != 0)
                        return byCount;
                    string nx = x.Sample?.m_shared != null ? x.Sample.m_shared.m_name : "";
                    string ny = y.Sample?.m_shared != null ? y.Sample.m_shared.m_name : "";
                    return string.CompareOrdinal(nx, ny);
                });
            }
            else
            {
                ranked.Sort((x, y) =>
                {
                    int byCount = y.Count.CompareTo(x.Count);
                    if (byCount != 0)
                        return byCount;
                    string nx = x.Sample?.m_shared != null ? x.Sample.m_shared.m_name : "";
                    string ny = y.Sample?.m_shared != null ? y.Sample.m_shared.m_name : "";
                    return string.CompareOrdinal(nx, ny);
                });
            }
            if (groupByCategory)
                agg.RankedByCategory = new List<RankedItem>(ranked);
            else
                agg.RankedByCount = new List<RankedItem>(ranked);
            return ranked;
        }

        private static int CategoryIdForItem(
            ItemDrop.ItemData item,
            List<int> filters,
            List<string> itemTokens)
        {
            if (item == null)
                return 0;

            // Prefer a selected filter that matches this item (Choices order among selected).
            if (filters != null && filters.Count > 0)
            {
                int best = 0;
                int bestOrder = int.MaxValue;
                for (int i = 0; i < filters.Count; i++)
                {
                    int id = filters[i];
                    if (!DisplayFilters.Matches(item, id))
                        continue;
                    int order = DisplayFilters.CategorySortOrder(id);
                    if (order < bestOrder)
                    {
                        bestOrder = order;
                        best = id;
                    }
                }
                if (best > 0)
                    return best;
            }

            return DisplayFilters.ParentFilterId(item);
        }

        private void PaintSlot(int i, RankedItem entry)
        {
            ResetSlotStyle(i);
            _slots[i].Icon.sprite = StackLimits.Icon(entry.Sample);
            _slots[i].Icon.enabled = _slots[i].Icon.sprite != null;
            _slots[i].Icon.color = Color.white;
            _slots[i].Shared = entry.Sample != null && entry.Sample.m_shared != null ? entry.Sample.m_shared.m_name : null;
            SetAmount(i, FormatCount(entry.Count));
            MarkBelowMinimum(i, _slots[i].Shared, entry.Count); // SAC-MIN
        }

        // SAC-MIN (whole method): below the minimum set in the filter menu → red count (no count shown: red icon).
        // The next paint resets the colors (PaintSlot / ClearSlot), so nothing stays red.
        private void MarkBelowMinimum(int i, string shared, int count)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            if (_view == null)
                _view = GetComponent<ZNetView>();
            if (!DisplayMinimums.Below(_view, shared, count))
                return;
            TextMeshProUGUI amount = _slots[i].Amount;
            bool countShown = !_slots[i].HideAmount && amount != null && amount.enabled && !string.IsNullOrEmpty(amount.text);
            DisplayMinimums.MarkRed(amount, _slots[i].Icon, countShown);
        }

        // SAC-MIN (whole method): a minimum changed in the filter menu: repaint with the current count.
        internal void RepaintSoon()
        {
            _dirty = true;
        }

        private void ResetSlotStyle(int i)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            if (!_slots[i].IsChip && (_slots[i].Amount == null || !_slots[i].Amount.enabled))
            {
                // Still restore font when coming from overflow "+" only.
            }
            _slots[i].IsChip = false;
            TextMeshProUGUI amount = _slots[i].Amount;
            if (amount == null)
                return;
            amount.alignment = TextAlignmentOptions.MidlineLeft;
            if (_slots[i].Font > 0.01f)
            {
                // v2 section cell: its own size / colour.
                amount.fontSize = _slots[i].Font;
                amount.color = _slots[i].AmountColor;
                amount.faceColor = _slots[i].AmountColor;
                return;
            }
            if (_slotFont > 0.01f)
                amount.fontSize = _slotFont;
            amount.color = new Color(1f, 0.95f, 0.75f, 1f);
            amount.faceColor = new Color32(255, 242, 191, 255);
        }

        private void ClearHeaders()
        {
            if (_headers == null)
                return;
            for (int i = 0; i < _headers.Length; i++)
                SetHeader(i, "");
        }

        private void SetHeader(int col, string text)
        {
            if (_headers == null || col < 0 || col >= _headers.Length)
                return;
            TextMeshProUGUI header = _headers[col];
            if (header == null)
                return;
            string next = text ?? "";
            if (header.text == next)
                return;
            header.enabled = true;
            header.text = next;
        }

        private void ClearSlot(int i)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            ResetSlotStyle(i);
            _slots[i].Shared = null;
            if (_slots[i].Icon != null)
            {
                _slots[i].Icon.enabled = false;
                _slots[i].Icon.sprite = null;
            }
            SetAmount(i, "");
            SetItemName(i, "");
        }

        private void SetItemName(int i, string text)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            TextMeshProUGUI label = _slots[i].Name;
            if (label == null)
                return;
            if (!ShowName())
            {
                if (label.enabled || !string.IsNullOrEmpty(label.text))
                {
                    label.enabled = false;
                    label.text = "";
                }
                return;
            }
            string next = text ?? "";
            if (label.text == next && label.enabled)
                return;
            label.enabled = true;
            label.text = next;
        }

        private void SetAmount(int i, string text)
        {
            if (_slots != null && i >= 0 && i < _slots.Length && _slots[i].Font > 0.01f)
            {
                SetAmount(i, text, _slots[i].AmountColor);
                return;
            }
            SetAmount(i, text, new Color(1f, 0.95f, 0.75f, 1f));
        }

        private void SetAmount(int i, string text, Color color)
        {
            if (_slots == null || i < 0 || i >= _slots.Length)
                return;
            TextMeshProUGUI amount = _slots[i].Amount;
            if (amount == null)
                return;
            if (!ShowAmount())
            {
                if (amount.enabled || !string.IsNullOrEmpty(amount.text))
                {
                    amount.enabled = false;
                    amount.text = "";
                }
                return;
            }
            string next = text ?? "";
            amount.enabled = true;
            amount.color = color;
            amount.faceColor = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255));
            if (amount.text != next)
                amount.text = next;
        }

        private static string FormatCount(int count)
        {
            if (count < 10000)
                return count.ToString();
            if (count < 100000)
                return (count / 1000f).ToString("0.#") + "k";
            return (count / 1000).ToString() + "k";
        }
    }
}
