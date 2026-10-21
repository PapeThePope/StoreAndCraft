using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Select Types — standalone overlay matching the 1920×1080 Photoshop mockup.
    /// Left: categories. Right: item grid. Uses Content/UI PS assets + UiToggle.
    /// </summary>
    internal static class DisplayTypeMenu
    {
        public static bool IsOpen { get; private set; }

        // Mockup reference layout (1920×1080) — clean panels + separate insets
        private const float RefW = 1920f;
        private const float RefH = 1080f;
        private const float LeftX = 111f;
        private const float LeftY = 90f;
        private const float LeftW = 552f;
        private const float LeftH = 887f;
        private const float RightX = 669f;
        private const float RightY = 88f;
        private const float RightW = 1053f;
        private const float RightH = 882f;

        // Dark inset SPRITE — stretch to white OUTER so the well is fully dark (no light panel peeking)
        private const float CatInsetL = 40f;
        private const float CatInsetT = 74f;
        private const float CatInsetR = 32f;
        private const float CatInsetB = 93f;

        // Clip + scrollbar = white INNER hole of panel_*_inset_rim (ignore gray)
        private const float CatClipL = 48f;
        private const float CatClipT = 86f;
        private const float CatClipR = 40f;
        private const float CatClipB = 105f;

        private const float CatRowW = 400f;
        private const float CatRowH = 54f;
        private const float CatRowGap = 8f;
        private const float CatContentPadL = 16f;

        private const float ItemInsetL = 50f;
        private const float ItemInsetT = 77f;
        private const float ItemInsetR = 41f;
        private const float ItemInsetB = 32f;

        private const float ItemClipL = 67f;
        private const float ItemClipT = 90f;
        private const float ItemClipR = 58f;
        private const float ItemClipB = 45f;

        private const float ItemCellW = 148f;
        private const float ItemCellH = 108f;
        private const float ItemGapX = 16f;
        private const float ItemGapY = 16f;
        private const float ItemContentPadL = 28f;
        private const float ItemContentPadT = 8f;
        private const int ItemColumns = 5;

        // Small gap so rows/cells don't sit on the white rim line
        private const float ContentPad = 12f;

        private const float ToggleW = 36f;
        private const float ToggleH = 18f;
        private const float ApplyW = 140f;
        private const float ApplyH = 38f;
        private const float LetterSpace = 14f;
        private const float CatFont = 24f;
        private const float ItemFont = 16f;
        private const float ApplyFont = 20f;
        private const float HeaderFont = 22f;

        private static StorageDisplayBoard _board;
        // F10 item lists (Store/ItemLists.cs): the same menu without a display, tiles edit the list instead.
        private static ItemListKind _list;
        private static System.Action _onListClosed;
        private static float _openedAt;
        private static int _suppressMenuFrame;
        private static bool _closing;
        private static int _focusId;
        private static bool _pendingItems;

        private static GameObject _root;
        private static RectTransform _catContent;
        private static RectTransform _itemContent;
        private static ScrollRect _catScroll;
        private static ScrollRect _itemScroll;
        // Search box (right panel header): non-empty = items of every category matching the text.
        private static TMP_InputField _searchField;
        private static string _search = "";

        // ---- Look: Vanilla (Valheim sprites); SAC artwork (UiAssets) only as fallback.
        // Names verified against Jotunn GUIManager: woodpanel_settings, button, button_highlight,
        // button_small, checkbox, checkbox_marker, text_field. Missing sprite → Classic fallback.
        private static bool _vanillaStyle;

        private static Sprite V(string name)
        {
            return _vanillaStyle ? SettingsPanel.Vanilla(name) : null;
        }

        private static Sprite PanelSprite(bool left)
        {
            return V("woodpanel_settings") ?? (left ? UiAssets.PanelLeft : UiAssets.PanelRight);
        }

        private static Sprite InsetSprite(bool left)
        {
            // Vanilla: no SAC inset art, a dark wash like the crafting list instead.
            if (_vanillaStyle)
                return null;
            return left ? UiAssets.PanelLeftInset : UiAssets.PanelRightInset;
        }

        private static void PlaceVanillaInset(RectTransform panel, float padL, float padT, float padR, float padB)
        {
            if (!_vanillaStyle)
                return;
            var go = new GameObject("InsetDark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panel, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padL, padB);
            rt.offsetMax = new Vector2(-padR, -padT);
            Image img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.38f);
            img.raycastTarget = false;
        }

        /// <summary>Image with sprite; sliced when the sprite has a border.</summary>
        private static void SetStyled(Image img, Sprite sprite, Color fallback)
        {
            if (img == null)
                return;
            if (sprite == null)
            {
                img.sprite = null;
                img.color = fallback;
                return;
            }
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            img.color = Color.white;
            UiStyle.Lit(img);
        }

        /// <summary>Classic: SAC pill toggle. Vanilla: Valheim checkbox with yellow tick.</summary>
        private static GameObject CreateToggle(RectTransform parent, bool on, UnityAction click)
        {
            Sprite box = V("checkbox");
            if (box == null)
                return UiToggle.Create(parent, "Toggle", on, true, click, UiFonts.ThinNorse(), ToggleW, ToggleH);

            var go = new GameObject("Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image bg = go.GetComponent<Image>();
            SetStyled(bg, box, Color.black);
            bg.raycastTarget = true;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            if (click != null)
                btn.onClick.AddListener(click);

            var mark = new GameObject("Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            mark.transform.SetParent(go.transform, false);
            RectTransform markRt = mark.transform as RectTransform;
            Stretch(markRt);
            markRt.offsetMin = new Vector2(7f, 7f);
            markRt.offsetMax = new Vector2(-7f, -7f);
            Image markImg = mark.GetComponent<Image>();
            // checkbox_marker sits off-centre in its sprite → centred smaller yellow checkbox knob.
            markImg.sprite = V("checkbox");
            markImg.preserveAspect = true;
            markImg.color = new Color(1f, 0.86f, 0.1f, 1f);
            markImg.raycastTarget = false;
            mark.SetActive(on);
            return go;
        }

        /// <summary>Checkbox is square; the Classic pill is wide.</summary>
        private static Vector2 ToggleSize()
        {
            return V("checkbox") != null ? new Vector2(26f, 26f) : new Vector2(ToggleW, ToggleH);
        }

        /// <summary>Search box has keyboard focus (first Escape only leaves the box).</summary>
        public static bool IsTyping => IsOpen && _searchField != null && _searchField.isFocused;

        private static readonly Dictionary<string, Sprite> _iconCache =
            new Dictionary<string, Sprite>(System.StringComparer.Ordinal);

        private static readonly Color Gold = new Color(0.925f, 0.77f, 0.29f, 1f);
        private static readonly Color Muted = new Color(0.70f, 0.64f, 0.52f, 1f);

        public static void Open(StorageDisplayBoard board)
        {
            if (board == null || Player.m_localPlayer == null)
                return;

            if (IsOpen)
                Close();
            if (DisplayRangeMenu.IsOpen)
                DisplayRangeMenu.Close();
            if (DisplaySmallOptions.IsOpen)
                DisplaySmallOptions.Close();
            if (StationFilterMenu.IsOpen)
                StationFilterMenu.Close();

            if (InventoryGui.instance != null && InventoryGui.IsVisible())
                InventoryGui.instance.Hide();

            _board = board;
            _list = ItemListKind.None;
            _onListClosed = null;
            _openedAt = Time.unscaledTime;
            _focusId = 0;
            _search = "";

            UiFonts.ThinNorse();
            ItemIds.PrefabFromToken("$item_wood");

            // Valheim look is the only look now; SAC artwork stays as fallback for a missing sprite.
            _vanillaStyle = true;

            EnsureRoot();
            if (_root == null)
                return;
            _root.SetActive(true);
            RebuildUi(buildItems: false);
            _pendingItems = true;
            IsOpen = true;
        }

        /// <summary>
        /// Opens the menu for one of the F10 item lists (no display): ground ignore = tiles switch on/off, keep in
        /// inventory = tiles take an amount. <paramref name="onClosed"/> runs when the menu closes (F10 comes back).
        /// </summary>
        public static void OpenList(ItemListKind list, System.Action onClosed)
        {
            if (list == ItemListKind.None || Player.m_localPlayer == null)
                return;
            if (IsOpen)
                Close();
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
                InventoryGui.instance.Hide();

            _board = null;
            _list = list;
            _onListClosed = onClosed;
            _openedAt = Time.unscaledTime;
            _focusId = 0;
            _search = "";
            UiFonts.ThinNorse();
            ItemIds.PrefabFromToken("$item_wood");
            _vanillaStyle = true;

            EnsureRoot();
            if (_root == null)
            {
                _list = ItemListKind.None;
                _onListClosed = null;
                if (onClosed != null)
                    onClosed();
                return;
            }
            _root.SetActive(true);
            RebuildUi(buildItems: false);
            _pendingItems = true;
            IsOpen = true;
        }

        private static string ListTitle()
        {
            if (_list == ItemListKind.GroundIgnore)
                return Loc.T("Leave on the ground", "Am Boden lassen");
            if (_list == ItemListKind.KeepInBag)
                return Loc.T("Keep in inventory", "Im Inventar behalten");
            return null;
        }

        public static void Close()
        {
            if (_closing)
                return;
            _closing = true;
            System.Action onListClosed = _onListClosed;
            try
            {
                IsOpen = false;
                _board = null;
                _list = ItemListKind.None;
                _onListClosed = null;
                _focusId = 0;
                _pendingItems = false;
                _suppressMenuFrame = Time.frameCount;
                DestroyRoot();
            }
            finally
            {
                _closing = false;
            }
            if (onListClosed != null)
                onListClosed();
        }

        public static void CloseIf(StorageDisplayBoard board)
        {
            if (IsOpen && _board == board)
                Close();
        }

        public static bool ShouldBlockPause()
        {
            return IsOpen || Time.frameCount == _suppressMenuFrame;
        }

        internal static void Tick()
        {
            if (!IsOpen)
                return;
            if ((_board == null && _list == ItemListKind.None) || Player.m_localPlayer == null)
            {
                Close();
                return;
            }

            if (_pendingItems)
            {
                _pendingItems = false;
                RebuildUi(buildItems: true, preserveCatScroll: false, resetItemScroll: true);
            }

            if (Time.unscaledTime < _openedAt + 0.15f)
                return;
            if (ZInput.GetKeyDown(KeyCode.Escape, true) || ZInput.GetButtonDown("JoyButtonB"))
            {
                if (TileAmountField.CloseEditor()) // Escape ends a "Min" / keep edit first, the menu stays open
                    return;
                // First Escape only leaves the search box.
                if (IsTyping)
                {
                    _searchField.DeactivateInputField();
                    return;
                }
                Close();
            }
        }

        private static void OnSearchChanged(string text)
        {
            string next = (text ?? "").Trim();
            if (next == _search)
                return;
            _search = next;
            _pendingItems = false;
            RebuildUi(buildItems: true, preserveCatScroll: true, resetItemScroll: true);
        }

        /// <summary>Picking a category leaves search mode (the grid shows that category again).</summary>
        private static void ClearSearch()
        {
            if (string.IsNullOrEmpty(_search))
                return;
            _search = "";
            if (_searchField != null)
                _searchField.SetTextWithoutNotify("");
        }

        private static void ToggleCategory(int id)
        {
            if (_list != ItemListKind.None)
            {
                FocusCategory(id); // the lists hold single items only
                return;
            }
            if (_board == null || id <= 0)
                return;
            ClearSearch();
            _board.ToggleFilter(id);
            bool sameFocus = _focusId == id;
            _focusId = id;
            _pendingItems = false;
            RebuildUi(buildItems: true, preserveCatScroll: true, resetItemScroll: !sameFocus);
        }

        private static void FocusCategory(int id)
        {
            if (id <= 0)
                return;
            ClearSearch();
            bool changed = _focusId != id;
            _focusId = id;
            _pendingItems = false;
            RebuildUi(buildItems: true, preserveCatScroll: true, resetItemScroll: changed);
        }

        private static void PickItem(string shared, int parentFilterId)
        {
            if (_board == null || string.IsNullOrEmpty(shared))
                return;
            _board.ToggleItemToken(shared, parentFilterId);
            _pendingItems = false;
            RebuildUi(buildItems: true, preserveCatScroll: true, resetItemScroll: false);
        }

        private static void PickEpicLootSub(int subFilterId)
        {
            if (_board == null || subFilterId <= 0)
                return;
            _board.ToggleFilter(subFilterId);
            _pendingItems = false;
            RebuildUi(buildItems: true, preserveCatScroll: true, resetItemScroll: false);
        }

        private static void EnsureRoot()
        {
            if (_root != null)
                return;

            _root = new GameObject("SAC_SelectTypes", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            // Dim
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            dim.transform.SetParent(_root.transform, false);
            Stretch(dim.transform as RectTransform);
            Image dimImg = dim.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimImg.raycastTarget = true;
            dim.GetComponent<Button>().transition = Selectable.Transition.None;
            dim.GetComponent<Button>().onClick.AddListener(Close);

            // 1920×1080 reference host (centered)
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(_root.transform, false);
            RectTransform hostRt = host.transform as RectTransform;
            hostRt.anchorMin = hostRt.anchorMax = new Vector2(0.5f, 0.5f);
            hostRt.pivot = new Vector2(0.5f, 0.5f);
            hostRt.sizeDelta = new Vector2(RefW, RefH);
            hostRt.anchoredPosition = Vector2.zero;

            if (_list == ItemListKind.None)
                BuildLeftPanel(hostRt); // the F10 item lists show every item on the right, no categories
            BuildRightPanel(hostRt);
        }

        private static void DestroyRoot()
        {
            _catContent = null;
            _itemContent = null;
            _catScroll = null;
            _itemScroll = null;
            _searchField = null;
            _search = "";
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }

        private static void BuildLeftPanel(RectTransform host)
        {
            var panel = new GameObject("PanelLeft", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(host, false);
            RectTransform rt = panel.transform as RectTransform;
            PlaceTopLeft(rt, LeftX, LeftY, LeftW, LeftH);
            ApplySprite(panel.GetComponent<Image>(), PanelSprite(true), Color.white);

            // Layer: panel → inset → content (clip to white rim, not gray halo)
            PlaceInsetBg(rt, InsetSprite(true), CatInsetL, CatInsetT, CatInsetR, CatInsetB);
            PlaceVanillaInset(rt, CatInsetL, CatInsetT, CatInsetR, CatInsetB);
            PlacePanelHeader(rt, Loc.T("Select type", "Select type"), CatInsetT);

            var scrollGo = new GameObject("CatScroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(rt, false);
            RectTransform scrollRt = scrollGo.transform as RectTransform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(CatClipL + ContentPad, CatClipB + ContentPad);
            scrollRt.offsetMax = new Vector2(-(CatClipR + ContentPad), -(CatClipT + ContentPad));
            Image scrollBg = scrollGo.GetComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0f);
            scrollBg.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollRt, false);
            RectTransform vpRt = viewport.transform as RectTransform;
            Stretch(vpRt);
            Image vpImg = viewport.GetComponent<Image>();
            // Must stay fully transparent — even 1% white washes the dark inset lighter.
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vpRt, false);
            _catContent = content.transform as RectTransform;
            _catContent.anchorMin = new Vector2(0f, 1f);
            _catContent.anchorMax = new Vector2(1f, 1f);
            _catContent.pivot = new Vector2(0.5f, 1f);
            _catContent.anchoredPosition = Vector2.zero;
            _catContent.sizeDelta = Vector2.zero;

            _catScroll = scrollGo.GetComponent<ScrollRect>();
            _catScroll.viewport = vpRt;
            _catScroll.content = _catContent;
            _catScroll.horizontal = false;
            _catScroll.vertical = true;
            _catScroll.movementType = ScrollRect.MovementType.Clamped;
            _catScroll.scrollSensitivity = 360f;
            _catScroll.inertia = true;
            _catScroll.verticalScrollbar = null;

            // Apply button (mockup: below dark inset)
            var apply = new GameObject("Apply", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            apply.transform.SetParent(rt, false);
            RectTransform applyRt = apply.transform as RectTransform;
            applyRt.anchorMin = applyRt.anchorMax = new Vector2(0.5f, 0f);
            applyRt.pivot = new Vector2(0.5f, 0f);
            applyRt.anchoredPosition = new Vector2(0f, 44f);
            applyRt.sizeDelta = new Vector2(ApplyW, ApplyH);
            ApplySprite(apply.GetComponent<Image>(), V("button") ?? UiAssets.BtnApply, Color.white);
            Button applyBtn = apply.GetComponent<Button>();
            applyBtn.transition = Selectable.Transition.None;
            applyBtn.onClick.AddListener(Close);

            var applyTextGo = new GameObject("Text", typeof(RectTransform));
            applyTextGo.transform.SetParent(apply.transform, false);
            Stretch(applyTextGo.transform as RectTransform);
            TextMeshProUGUI applyTmp = UiFonts.CreateLabel(applyTextGo, ApplyFont);
            StyleLabel(applyTmp, ApplyFont, LetterSpace);
            applyTmp.alignment = TextAlignmentOptions.Center;
            applyTmp.color = Gold;
            applyTmp.text = Loc.T("Apply", "Apply");
        }

        private static void BuildRightPanel(RectTransform host)
        {
            var panel = new GameObject("PanelRight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(host, false);
            RectTransform rt = panel.transform as RectTransform;
            bool list = _list != ItemListKind.None;
            PlaceTopLeft(rt, list ? (RefW - RightW) * 0.5f : RightX, RightY, RightW, RightH);
            ApplySprite(panel.GetComponent<Image>(), PanelSprite(false), Color.white);

            PlaceInsetBg(rt, InsetSprite(false), ItemInsetL, ItemInsetT, ItemInsetR, ItemInsetB);
            PlaceVanillaInset(rt, ItemInsetL, ItemInsetT, ItemInsetR, ItemInsetB);
            PlacePanelHeader(rt, list ? ListTitle() : Loc.T("Filter", "Filter"), ItemInsetT);
            BuildSearchField(rt);
            if (list)
                BuildListApplyButton(rt);

            var scrollGo = new GameObject("ItemScroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(rt, false);
            RectTransform scrollRt = scrollGo.transform as RectTransform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(ItemClipL + ContentPad, ItemClipB + ContentPad);
            scrollRt.offsetMax = new Vector2(-(ItemClipR + ContentPad), -(ItemClipT + ContentPad));
            Image scrollBg = scrollGo.GetComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0f);
            scrollBg.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollRt, false);
            RectTransform vpRt = viewport.transform as RectTransform;
            Stretch(vpRt);
            Image vpImg = viewport.GetComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vpRt, false);
            _itemContent = content.transform as RectTransform;
            _itemContent.anchorMin = new Vector2(0f, 1f);
            _itemContent.anchorMax = new Vector2(1f, 1f);
            _itemContent.pivot = new Vector2(0.5f, 1f);
            _itemContent.anchoredPosition = Vector2.zero;
            _itemContent.sizeDelta = Vector2.zero;

            _itemScroll = scrollGo.GetComponent<ScrollRect>();
            _itemScroll.viewport = vpRt;
            _itemScroll.content = _itemContent;
            _itemScroll.horizontal = false;
            _itemScroll.vertical = true;
            _itemScroll.movementType = ScrollRect.MovementType.Clamped;
            _itemScroll.scrollSensitivity = 540f;
            _itemScroll.inertia = true;
            _itemScroll.verticalScrollbar = null;
        }

        /// <summary>Search box in the header band, left of "FILTER".</summary>
        private static void BuildSearchField(RectTransform panel)
        {
            // Built inactive: TMP_InputField must be fully wired before OnEnable (see SettingsPanel).
            var go = new GameObject("Search", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            go.SetActive(false);
            go.transform.SetParent(panel, false);
            RectTransform rt = go.transform as RectTransform;
            PlaceTopLeft(rt, ItemInsetL + 20f, 22f, 330f, 40f);
            Image bg = go.GetComponent<Image>();
            SetStyled(bg, V("text_field"), new Color(0.08f, 0.05f, 0.02f, 0.55f));
            bg.raycastTarget = true;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(rt, false);
            RectTransform areaRt = area.transform as RectTransform;
            Stretch(areaRt);
            areaRt.offsetMin = new Vector2(12f, 2f);
            areaRt.offsetMax = new Vector2(-12f, -2f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(areaRt, false);
            Stretch(textGo.transform as RectTransform);
            TextMeshProUGUI text = UiFonts.CreateLabel(textGo, ItemFont + 4f);
            text.color = Gold;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(areaRt, false);
            Stretch(phGo.transform as RectTransform);
            TextMeshProUGUI placeholder = UiFonts.CreateLabel(phGo, ItemFont + 4f);
            placeholder.color = new Color(Muted.r, Muted.g, Muted.b, 0.75f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.raycastTarget = false;
            placeholder.text = Loc.T("Search item...", "Item suchen...");

            TMP_InputField input = go.GetComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = bg;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 40;
            input.SetTextWithoutNotify(_search ?? "");
            input.onValueChanged.AddListener(OnSearchChanged);
            _searchField = input;
            go.SetActive(true);
        }

        /// <summary>Items of every category whose name contains the search text.</summary>
        private static List<ItemCellData> SearchCells(string query, HashSet<int> selectedSet, HashSet<string> selectedItemSet)
        {
            var found = new List<KeyValuePair<string, ItemCellData>>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            string q = query.ToLowerInvariant();
            bool elAll = selectedSet.Contains(DisplayFilters.EpicLootGroupId);
            for (int i = 0; i < DisplayFilters.Choices.Length; i++)
            {
                int catId = DisplayFilters.Choices[i].Id;
                if (DisplayFilters.IsEpicLootSubFilter(catId))
                    continue;
                List<string> items = DisplayFilters.SubItems(catId);
                if (items == null)
                    continue;
                for (int s = 0; s < items.Count; s++)
                {
                    string shared = items[s];
                    if (string.IsNullOrEmpty(shared) || !seen.Add(shared))
                        continue;
                    // Undiscovered items stay hidden (unless already selected on this board).
                    if (!DisplayFilters.IsKnownToPlayer(shared) && !selectedItemSet.Contains(shared))
                        continue;
                    string label = DisplayFilters.ItemLabel(shared) ?? "";
                    if (label.ToLowerInvariant().IndexOf(q, System.StringComparison.Ordinal) < 0
                        && shared.ToLowerInvariant().IndexOf(q, System.StringComparison.Ordinal) < 0)
                        continue;
                    int parent = catId;
                    if (catId == DisplayFilters.EpicLootGroupId)
                    {
                        int p = DisplayFilters.ParentFilterIdFromToken(shared);
                        if (p > 0)
                            parent = p;
                    }
                    string captured = shared;
                    int capturedParent = parent;
                    found.Add(new KeyValuePair<string, ItemCellData>(label, new ItemCellData
                    {
                        Label = label,
                        On = selectedSet.Contains(parent) || selectedItemSet.Contains(shared)
                            || (catId == DisplayFilters.EpicLootGroupId && elAll),
                        Icon = ItemIcon(shared),
                        Shared = captured,
                        Click = () => PickItem(captured, capturedParent)
                    }));
                }
            }
            found.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.CurrentCultureIgnoreCase));
            var cells = new List<ItemCellData>(found.Count);
            for (int i = 0; i < found.Count; i++)
                cells.Add(found[i].Value);
            return cells;
        }

        private static void PlaceInsetBg(RectTransform panel, Sprite sprite, float padL, float padT, float padR, float padB)
        {
            if (sprite == null)
                return;
            var go = new GameObject("Inset", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panel, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padL, padB);
            rt.offsetMax = new Vector2(-padR, -padT);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = false;
        }

        private static void PlacePanelHeader(RectTransform panel, string text, float insetTop)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Slightly below band center so it sits closer to the inset.
            float bandMid = insetTop * 0.5f + 10f;
            rt.anchoredPosition = new Vector2(0f, -bandMid);
            rt.sizeDelta = new Vector2(-80f, insetTop - 8f);
            TextMeshProUGUI tmp = UiFonts.CreateBoldLabel(go, HeaderFont);
            tmp.enableAutoSizing = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.maxVisibleLines = 1;
            tmp.raycastTarget = false;
            tmp.characterSpacing = LetterSpace;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Gold;
            tmp.text = text;
        }

        private static void ClearContent(RectTransform content)
        {
            if (content == null)
                return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Object.Destroy(content.GetChild(i).gameObject);
        }

        private static void RebuildUi(bool buildItems, bool preserveCatScroll = false, bool resetItemScroll = true)
        {
            EnsureRoot();
            if ((_catContent == null && _list == ItemListKind.None) || _itemContent == null)
                return;

            float catScrollPos = _catScroll != null ? _catScroll.verticalNormalizedPosition : 1f;
            float itemScrollPos = _itemScroll != null ? _itemScroll.verticalNormalizedPosition : 1f;

            ClearContent(_catContent);
            if (buildItems)
                ClearContent(_itemContent);

            List<int> selected = _board != null ? _board.FilterIds() : new List<int>();
            List<string> selectedItems = _board != null ? _board.ItemTokens() : new List<string>();
            var selectedSet = new HashSet<int>(selected);
            var selectedItemSet = new HashSet<string>(selectedItems, System.StringComparer.Ordinal);
            var partialParents = new HashSet<int>();
            foreach (string token in selectedItemSet)
            {
                int parent = DisplayFilters.ParentFilterIdFromToken(token);
                if (parent > 0)
                    partialParents.Add(parent);
            }
            EnsureFocus(selected, selectedItems);

            int catN = 0;
            for (int i = 0; i < DisplayFilters.Choices.Length && _catContent != null; i++)
            {
                DisplayFilter choice = DisplayFilters.Choices[i];
                int id = choice.Id;
                if (DisplayFilters.IsEpicLootSubFilter(id))
                    continue;
                if (DisplayFilters.IsPovFilter(id) && !DisplayFilters.PovInstalled())
                    continue; // Path of Valheiman categories only with that mod
                bool on = CategoryIsOn(id, selectedSet, partialParents);
                bool focused = id == _focusId;
                int captured = id;
                AddCategoryRow(catN++, choice.Label(), on, focused,
                    () => FocusCategory(captured),
                    () => ToggleCategory(captured));
            }
            if (_catContent != null)
                _catContent.sizeDelta = new Vector2(0f, catN * (CatRowH + CatRowGap) + 4f);

            if (_catScroll != null)
                _catScroll.verticalNormalizedPosition = preserveCatScroll ? catScrollPos : 1f;

            if (!buildItems)
                return;

            var cells = new List<ItemCellData>();
            if (!string.IsNullOrEmpty(_search) || _list != ItemListKind.None)
            {
                cells = SearchCells(_search ?? "", selectedSet, selectedItemSet);
            }
            else if (_focusId == DisplayFilters.EpicLootGroupId && _list == ItemListKind.None)
            {
                bool allOn = selectedSet.Contains(DisplayFilters.EpicLootGroupId);
                cells.Add(new ItemCellData
                {
                    Label = Loc.T("All", "All") + " Epic Loot",
                    On = allOn,
                    Icon = ItemIcon(DisplayFilters.RepresentativeShared(DisplayFilters.ElDustFilterId)),
                    Click = () => ToggleCategory(DisplayFilters.EpicLootGroupId)
                });
                for (int s = 0; s < DisplayFilters.EpicLootSubFilterIds.Length; s++)
                {
                    int subId = DisplayFilters.EpicLootSubFilterIds[s];
                    DisplayFilter sub;
                    if (!DisplayFilters.TryGet(subId, out sub))
                        continue;
                    int capturedSub = subId;
                    cells.Add(new ItemCellData
                    {
                        Label = sub.Label(),
                        On = selectedSet.Contains(subId),
                        Icon = ItemIcon(DisplayFilters.RepresentativeShared(subId)),
                        Click = () => PickEpicLootSub(capturedSub)
                    });
                }

                // Individual craft mats under the group (with sprites).
                List<string> elItems = DisplayFilters.SubItems(DisplayFilters.EpicLootGroupId);
                if (elItems != null)
                {
                    for (int s = 0; s < elItems.Count; s++)
                    {
                        string shared = elItems[s];
                        if (!DisplayFilters.IsKnownToPlayer(shared) && !selectedItemSet.Contains(shared))
                            continue;
                        string captured = shared;
                        int parent = DisplayFilters.ParentFilterIdFromToken(shared);
                        if (parent <= 0)
                            parent = DisplayFilters.EpicLootGroupId;
                        int capturedParent = parent;
                        cells.Add(new ItemCellData
                        {
                            Label = DisplayFilters.ItemLabel(shared),
                            On = allOn
                                || selectedSet.Contains(parent)
                                || selectedItemSet.Contains(shared),
                            Icon = ItemIcon(shared),
                            Shared = captured,
                            Click = () => PickItem(captured, capturedParent)
                        });
                    }
                }
            }
            else
            {
                List<string> items = DisplayFilters.SubItems(_focusId);
                if (items != null)
                {
                    bool categoryOn = selectedSet.Contains(_focusId);
                    for (int s = 0; s < items.Count; s++)
                    {
                        string shared = items[s];
                        if (!DisplayFilters.IsKnownToPlayer(shared) && !selectedItemSet.Contains(shared))
                            continue;
                        string captured = shared;
                        int parent = _focusId;
                        cells.Add(new ItemCellData
                        {
                            Label = DisplayFilters.ItemLabel(shared),
                            On = categoryOn || selectedItemSet.Contains(shared),
                            Icon = ItemIcon(shared),
                            Shared = captured,
                            Click = () => PickItem(captured, parent)
                        });
                    }
                }
            }

            if (_list != ItemListKind.None)
                cells = ListCells(cells);
            BuildItemGrid(cells);
            if (_itemScroll != null)
                _itemScroll.verticalNormalizedPosition = resetItemScroll ? 1f : itemScrollPos;
        }

        /// <summary>F10 item lists: single items only, "on" = on the list (keep: the tile click edits the amount).</summary>
        private static List<ItemCellData> ListCells(List<ItemCellData> cells)
        {
            var result = new List<ItemCellData>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                ItemCellData cell = cells[i];
                if (string.IsNullOrEmpty(cell.Shared))
                    continue;
                cell.On = ItemLists.Has(_list, cell.Shared);
                cell.Click = null; // AddItemCell: the tile switches / edits in place (no rebuild, nothing jumps)
                result.Add(cell);
            }
            // Items on the list first (each part keeps its A-Z order): easy to find to switch them off again.
            var sorted = new List<ItemCellData>(result.Count);
            for (int i = 0; i < result.Count; i++)
                if (result[i].On)
                    sorted.Add(result[i]);
            for (int i = 0; i < result.Count; i++)
                if (!result[i].On)
                    sorted.Add(result[i]);
            return sorted;
        }

        private struct ItemCellData
        {
            public string Label;
            public bool On;
            public Sprite Icon;
            public UnityAction Click;
            public string Shared; // item token: right click flies the camera to a chest that holds it
        }

        /// <summary>Right click on an item tile.</summary>
        private sealed class ItemCellRightClick : MonoBehaviour, IPointerClickHandler
        {
            public string Shared;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Right && _board != null)
                    SearchPing.LocateForBoard(_board, Shared);
            }
        }

        /// <summary>Hides / shows the menu while the camera flies to a chest (the menu stays "open").</summary>
        internal static void SetHidden(bool hidden)
        {
            if (_root == null)
                return;
            Canvas canvas = _root.GetComponent<Canvas>();
            if (canvas != null)
                canvas.enabled = !hidden;
        }

        private static void AddCategoryRow(
            int index,
            string label,
            bool on,
            bool focused,
            UnityAction onFocus,
            UnityAction onToggle)
        {
            var row = new GameObject("Cat" + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            row.transform.SetParent(_catContent, false);
            RectTransform rt = row.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(CatRowW, CatRowH);
            rt.anchoredPosition = new Vector2(CatContentPadL, -index * (CatRowH + CatRowGap));

            Sprite rowSp = focused ? UiAssets.RawCategoryFocus : UiAssets.RawCategory;
            Image rowImg = row.GetComponent<Image>();
            Sprite vRow = V(focused ? "button_highlight" : "button");
            if (vRow != null)
            {
                SetStyled(rowImg, vRow, Color.white);
            }
            else if (rowSp != null)
            {
                rowImg.sprite = rowSp;
                rowImg.type = Image.Type.Simple;
                rowImg.preserveAspect = false;
                rowImg.color = Color.white;
            }
            else
            {
                rowImg.color = new Color(0.2f, 0.15f, 0.1f, 0.9f);
            }
            rowImg.raycastTarget = true;

            // Focus click (left part)
            var hit = new GameObject("Focus", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            hit.transform.SetParent(rt, false);
            RectTransform hitRt = hit.transform as RectTransform;
            hitRt.anchorMin = Vector2.zero;
            hitRt.anchorMax = Vector2.one;
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = new Vector2(-(ToggleW + 12f), 0f);
            Image hitImg = hit.GetComponent<Image>();
            hitImg.color = new Color(1f, 1f, 1f, 0.001f);
            hitImg.raycastTarget = true;
            Button hb = hit.GetComponent<Button>();
            hb.transition = Selectable.Transition.None;
            if (onFocus != null)
                hb.onClick.AddListener(onFocus);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(rt, false);
            RectTransform labelRt = labelGo.transform as RectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(36f, 2f);
            labelRt.offsetMax = new Vector2(-(ToggleW + 14f), -2f);
            TextMeshProUGUI tmp = UiFonts.CreateLabel(labelGo, CatFont);
            StyleLabel(tmp, CatFont, LetterSpace);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = focused || on ? Gold : Muted;
            tmp.text = label;

            GameObject toggle = CreateToggle(rt, on, onToggle);
            RectTransform togRt = toggle.transform as RectTransform;
            togRt.anchorMin = new Vector2(1f, 0.5f);
            togRt.anchorMax = new Vector2(1f, 0.5f);
            togRt.pivot = new Vector2(1f, 0.5f);
            togRt.anchoredPosition = new Vector2(-28f, 0f);
            togRt.sizeDelta = ToggleSize();
            Image togImg = toggle.GetComponent<Image>();
            if (togImg != null)
                togImg.preserveAspect = true;
            togRt.SetAsLastSibling();
        }

        private static void BuildItemGrid(List<ItemCellData> cells)
        {
            int n = cells.Count;
            int cols = ItemColumns;
            int rows = Mathf.Max(1, (n + cols - 1) / cols);

            for (int i = 0; i < n; i++)
            {
                int col = i % cols;
                int row = i / cols;
                AddItemCell(col, row, cells[i]);
            }

            _itemContent.sizeDelta = new Vector2(0f, ItemContentPadT + rows * (ItemCellH + ItemGapY) + 8f);
            _itemContent.anchoredPosition = Vector2.zero;
        }

        /// <summary>Leave on the ground: a click on the tile switches the item on / off the list.</summary>
        private static void AddGroundToggle(RectTransform tile, string token)
        {
            Button btn = tile.GetComponent<Button>();
            if (btn == null)
                return;
            btn.onClick.AddListener(() =>
            {
                ItemLists.ToggleGround(token);
                SetCellOn(tile, ItemLists.Has(ItemListKind.GroundIgnore, token));
            });
        }

        /// <summary>The F10 item lists have no left panel: Apply sits top right in the item panel.</summary>
        private static void BuildListApplyButton(RectTransform panel)
        {
            var apply = new GameObject("Apply", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            apply.transform.SetParent(panel, false);
            RectTransform applyRt = apply.transform as RectTransform;
            applyRt.anchorMin = applyRt.anchorMax = new Vector2(1f, 1f);
            applyRt.pivot = new Vector2(1f, 1f);
            applyRt.anchoredPosition = new Vector2(-(ItemInsetR + 20f), -22f);
            applyRt.sizeDelta = new Vector2(ApplyW, ApplyH);
            ApplySprite(apply.GetComponent<Image>(), V("button") ?? UiAssets.BtnApply, Color.white);
            Button applyBtn = apply.GetComponent<Button>();
            applyBtn.transition = Selectable.Transition.None;
            applyBtn.onClick.AddListener(Close);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(apply.transform, false);
            Stretch(textGo.transform as RectTransform);
            TextMeshProUGUI tmp = UiFonts.CreateLabel(textGo, ApplyFont);
            StyleLabel(tmp, ApplyFont, LetterSpace);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Gold;
            tmp.text = Loc.T("Apply", "Apply");
        }

        /// <summary>Keep in inventory: the amount box; a click on the tile edits it too.</summary>
        private static void AddKeepField(RectTransform tile, string token)
        {
            TileAmountField.Field field = TileAmountField.Add(tile, Loc.T("Keep", "Rest"),
                () => ItemLists.GetKeep(token), amount => ItemLists.SetKeep(token, amount),
                amount => SetCellOn(tile, amount > 0));
            Button btn = tile.GetComponent<Button>();
            if (field != null && btn != null)
                btn.onClick.AddListener(field.BeginEdit);
            AddRestockToggle(tile, token);
        }

        /// <summary>Keep list: the small Fill box (top left). On = the restock key brings the item up to its amount.</summary>
        private static void AddRestockToggle(RectTransform tile, string token)
        {
            const string name = "SacRestockToggle";
            Transform old = tile.Find(name);
            if (old != null)
                Object.Destroy(old.gameObject);

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(tile, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(42f, 22f);
            rt.anchoredPosition = new Vector2(6f, -6f);
            Image bg = go.GetComponent<Image>();
            TileAmountField.StyleField(bg);
            bg.raycastTarget = true;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;

            var labelGo = new GameObject("Value", typeof(RectTransform));
            labelGo.transform.SetParent(rt, false);
            Stretch(labelGo.transform as RectTransform);
            TextMeshProUGUI label = UiFonts.CreateLabel(labelGo, 13f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.text = Loc.T("Fill", "Auf");

            System.Action refresh = () =>
            {
                bool on = ItemLists.IsRestock(token);
                label.color = on ? Gold : Muted;
                label.fontStyle = on ? FontStyles.Normal : FontStyles.Italic;
                bg.color = on ? new Color(1f, 0.9f, 0.55f, 1f) : Color.white;
            };
            refresh();
            btn.onClick.AddListener(() =>
            {
                ItemLists.ToggleRestock(token);
                refresh();
            });
        }

        /// <summary>Tile look after its amount changed (no grid rebuild while the next box may be edited).</summary>
        private static void SetCellOn(RectTransform tile, bool on)
        {
            if (tile == null)
                return;
            Image img = tile.GetComponent<Image>();
            Sprite sprite = V(on ? "button_highlight" : "button_small");
            if (img != null && sprite != null)
            {
                SetStyled(img, sprite, Color.white);
                img.color = new Color(1f, 1f, 1f, on ? 0.75f : 0.45f);
            }
            Transform label = tile.Find("Label");
            TextMeshProUGUI tmp = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
            if (tmp != null)
                tmp.color = on ? Gold : Muted;
        }

        private static void AddItemCell(int col, int row, ItemCellData data)
        {
            var go = new GameObject("Item" + row + "_" + col, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_itemContent, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(ItemCellW, ItemCellH);
            rt.anchoredPosition = new Vector2(
                ItemContentPadL + col * (ItemCellW + ItemGapX),
                -(ItemContentPadT + row * (ItemCellH + ItemGapY)));
            if (!string.IsNullOrEmpty(data.Shared))
                go.AddComponent<ItemCellRightClick>().Shared = data.Shared;
            Sprite vCell = V(data.On ? "button_highlight" : "button_small");
            Image cellImg = go.GetComponent<Image>();
            bool wholeCellClick = vCell != null;
            if (vCell != null)
            {
                SetStyled(cellImg, vCell, Color.white);
                // Lighter tile so the wood shows through; selected stays a bit stronger.
                cellImg.color = new Color(1f, 1f, 1f, data.On ? 0.75f : 0.45f);
                cellImg.raycastTarget = true;

                // Vanilla look: the whole tile is the switch (no checkbox on the edge).
                Button cellBtn = go.AddComponent<Button>();
                cellBtn.targetGraphic = cellImg;
                cellBtn.transition = Selectable.Transition.ColorTint;
                ColorBlock colors = cellBtn.colors;
                colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                colors.highlightedColor = Color.white;
                colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                colors.selectedColor = colors.normalColor;
                colors.fadeDuration = 0.08f;
                cellBtn.colors = colors;
                if (data.Click != null)
                    cellBtn.onClick.AddListener(data.Click);
            }
            else
            {
                ApplySprite(cellImg, UiAssets.ItemCell, Color.white);
                if (cellImg != null && cellImg.sprite != null)
                {
                    cellImg.type = Image.Type.Simple;
                    cellImg.preserveAspect = false;
                }
            }

            // Icon top-center
            if (data.Icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(rt, false);
                RectTransform iconRt = iconGo.transform as RectTransform;
                iconRt.anchorMin = new Vector2(0.5f, 1f);
                iconRt.anchorMax = new Vector2(0.5f, 1f);
                iconRt.pivot = new Vector2(0.5f, 1f);
                iconRt.anchoredPosition = new Vector2(0f, -10f);
                iconRt.sizeDelta = new Vector2(52f, 52f);
                Image iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = data.Icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.color = Color.white;
                if (_vanillaStyle)
                {
                    // Soft drop shadow so icons lift off the lighter tile.
                    Shadow shadow = iconGo.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
                    shadow.effectDistance = new Vector2(3f, -3f);
                }
            }

            // Name under icon
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(rt, false);
            RectTransform labelRt = labelGo.transform as RectTransform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            // No edge checkbox in the Vanilla look → the name may use the bottom strip too.
            labelRt.offsetMin = new Vector2(4f, wholeCellClick ? 6f : ToggleH + 6f);
            labelRt.offsetMax = new Vector2(-4f, -50f);
            TextMeshProUGUI tmp = UiFonts.CreateLabel(labelGo, ItemFont);
            StyleLabel(tmp, ItemFont, LetterSpace * 0.7f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.maxVisibleLines = 2;
            tmp.color = data.On ? Gold : Muted;
            tmp.text = data.Label;
            DisplayMinimums.AddTileField(rt, _board, data.Shared); // SAC-MIN: "Min" field in the tile
            if (_list == ItemListKind.KeepInBag && !string.IsNullOrEmpty(data.Shared))
                AddKeepField(rt, data.Shared);
            if (_list == ItemListKind.GroundIgnore && !string.IsNullOrEmpty(data.Shared))
                AddGroundToggle(rt, data.Shared);

            if (wholeCellClick)
                return;

            // Toggle bottom-right (mockup)
            GameObject toggle = CreateToggle(rt, data.On, data.Click);
            RectTransform togRt = toggle.transform as RectTransform;
            togRt.anchorMin = new Vector2(1f, 0f);
            togRt.anchorMax = new Vector2(1f, 0f);
            togRt.pivot = new Vector2(1f, 0f);
            togRt.anchoredPosition = new Vector2(-6f, 6f);
            togRt.sizeDelta = ToggleSize();
            Image togImg = toggle.GetComponent<Image>();
            if (togImg != null)
                togImg.preserveAspect = true;
            togRt.SetAsLastSibling();
        }

        private static Sprite ItemIcon(string shared)
        {
            if (string.IsNullOrEmpty(shared))
                return null;
            Sprite cached;
            if (_iconCache.TryGetValue(shared, out cached))
                return cached;
            GameObject prefab = ItemIds.PrefabFromToken(shared);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            Sprite icon = drop?.m_itemData != null ? StackLimits.Icon(drop.m_itemData) : null;
            _iconCache[shared] = icon;
            return icon;
        }

        private static void StyleLabel(TMP_Text tmp, float size, float letterSpace = 0f)
        {
            UiFonts.StyleThinLabel(tmp, size);
            tmp.enableAutoSizing = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.maxVisibleLines = 1;
            tmp.raycastTarget = false;
            tmp.characterSpacing = letterSpace;
        }

        private static void ApplySprite(Image img, Sprite sprite, Color fallback)
        {
            if (img == null)
                return;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                UiStyle.Lit(img);
            }
            else
            {
                img.sprite = null;
                img.color = fallback;
            }
            img.raycastTarget = true;
        }

        private static void PlaceTopLeft(RectTransform rt, float x, float yFromTop, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void EnsureFocus(List<int> selected, List<string> selectedItems)
        {
            if (_focusId > 0 && !DisplayFilters.IsEpicLootSubFilter(_focusId))
            {
                DisplayFilter unused;
                if (DisplayFilters.TryGet(_focusId, out unused))
                    return;
            }
            for (int i = 0; i < selected.Count; i++)
            {
                int id = selected[i];
                if (DisplayFilters.IsEpicLootSubFilter(id))
                {
                    _focusId = DisplayFilters.EpicLootGroupId;
                    return;
                }
                if (id > 0)
                {
                    _focusId = id;
                    return;
                }
            }
            for (int i = 0; i < selectedItems.Count; i++)
            {
                int parent = DisplayFilters.ParentFilterIdFromToken(selectedItems[i]);
                if (parent > 0)
                {
                    _focusId = parent;
                    return;
                }
            }
            for (int i = 0; i < DisplayFilters.Choices.Length; i++)
            {
                int id = DisplayFilters.Choices[i].Id;
                if (DisplayFilters.IsEpicLootSubFilter(id))
                    continue;
                _focusId = id;
                return;
            }
            _focusId = 0;
        }

        private static bool CategoryIsOn(int id, HashSet<int> selected, HashSet<int> partialParents)
        {
            if (selected.Contains(id))
                return true;
            if (id == DisplayFilters.EpicLootGroupId)
            {
                for (int s = 0; s < DisplayFilters.EpicLootSubFilterIds.Length; s++)
                {
                    if (selected.Contains(DisplayFilters.EpicLootSubFilterIds[s]))
                        return true;
                }
                return false;
            }
            return partialParents != null && partialParents.Contains(id);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.IsVisible))]
    internal static class DisplayTypeMenuInventoryVisiblePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (DisplayTypeMenu.IsOpen)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class DisplayTypeMenuInventoryShowPatch
    {
        private static bool Prefix()
        {
            if (!DisplayTypeMenu.IsOpen)
                return true;
            DisplayTypeMenu.Close();
            return true;
        }
    }

    [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
    internal static class DisplayTypeMenuPausePatch
    {
        private static bool Prefix()
        {
            if (!DisplayTypeMenu.ShouldBlockPause())
                return true;
            DisplayTypeMenu.Close();
            return false;
        }
    }
}
