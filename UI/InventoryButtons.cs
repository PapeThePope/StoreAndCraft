using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Sort + Stack buttons cloned from vanilla "Take all" (look, hover, click sound match Valheim).
    /// Chest: narrow, next to Take all in the header row (Take all shrinks only if the title needs it).
    /// Bag: top right, on the upper edge of the inventory (the trash can mods sit beside the armor), sibling-first so they sit under the inventory panel like a Photoshop layer.
    /// The clones lose UIGamePad and Localize so vanilla cannot fire or rename them.
    /// Laid out every frame from the live vanilla rects, so chest names / resolutions do not matter.
    /// </summary>
    internal static class InventoryButtons
    {
        private const float Gap = 4f;
        private const float ChestButtonAspect = 1.6f;
        /// <summary>How far the bag buttons reach down behind the inventory's upper edge, and their distance from its right edge.</summary>
        private const float BagOverlap = 6f;
        private const float BagRightMargin = 24f;

        private static InventoryGui _builtFor;
        private static RectTransform _take;
        private static Vector2 _takeSize0;
        private static Vector3 _takeLocal0;
        private static float _takeWidth0;
        private static bool _takeMeasured;
        // What we left Take all at last frame. If it differs now, vanilla or another mod (bigger
        // inventories / chests) moved or resized it, and that new spot becomes the base layout.
        private static Vector2 _takeSizeSeen;
        private static Vector3 _takeLocalSeen;
        private static RectTransform _chestSort;
        private static RectTransform _chestStack;
        private static RectTransform _bagStack;
        private static RectTransform _bagSort;
        private static readonly Vector3[] Corners = new Vector3[4];

        internal static void EnsureBuilt(InventoryGui gui)
        {
            if (gui == null || _builtFor == gui)
                return;
            Button template = gui.m_takeAllButton;
            if (template == null || gui.m_container == null || gui.m_player == null || gui.m_weight == null || gui.m_armor == null)
                return;
            _builtFor = gui;

            _take = (RectTransform)template.transform;
            // Take all is measured later, the first time the chest panel is really visible
            // (opening the bag first left it unlaid-out: 0 width → whole button row shifted).
            _takeMeasured = false;

            _chestSort = Make(template, _take.parent, "SAC_ChestSort", Loc.T("Sort", "Sortieren"), InventorySort.SortChest);
            _chestStack = Make(template, _take.parent, "SAC_ChestStack", Loc.T("Stack", "Stapeln"), InventorySort.StackChest);
            // First siblings = drawn under the inventory wood (Photoshop-layer style).
            _bagStack = Make(template, gui.m_player, "SAC_BagStack", Loc.T("Stack", "Stapeln"), InventorySort.StackBag);
            _bagSort = Make(template, gui.m_player, "SAC_BagSort", Loc.T("Sort", "Sortieren"), InventorySort.SortBag);
            _bagStack.SetAsFirstSibling();
            _bagSort.SetAsFirstSibling();
        }

        internal static void Layout(InventoryGui gui)
        {
            if (gui == null || gui != _builtFor || _take == null || !InventoryGui.IsVisible())
                return;
            ModConfig cfg = Plugin.Settings;
            bool showChest = cfg == null || cfg.ShowChestSortButtons.Value;
            bool showBag = cfg == null || cfg.ShowBagSortButtons.Value;
            SetShown(_chestSort, showChest);
            SetShown(_chestStack, showChest);
            SetShown(_bagSort, showBag);
            SetShown(_bagStack, showBag);

            if (showChest && gui.m_container.gameObject.activeInHierarchy)
                LayoutChest(gui);
            else if (!showChest && _takeMeasured)
            {
                // Buttons off: Take all gets its vanilla size and place back.
                _take.sizeDelta = _takeSize0;
                _take.localPosition = _takeLocal0;
                _takeSizeSeen = _take.sizeDelta;
                _takeLocalSeen = _take.localPosition;
            }
            if (showBag)
                LayoutBag(gui);
        }

        private static void SetShown(RectTransform rt, bool on)
        {
            if (rt != null && rt.gameObject.activeSelf != on)
                rt.gameObject.SetActive(on);
        }

        private static void LayoutChest(InventoryGui gui)
        {
            RectTransform parent = _take.parent as RectTransform;
            if (parent == null || _chestSort == null || _chestStack == null)
                return;

            // Measure when Take all is active and laid out (real width), before we ever resize it,
            // and again whenever something else moved / resized it since we last set it.
            bool moved = _takeMeasured
                && ((_take.sizeDelta - _takeSizeSeen).sqrMagnitude > 0.0001f
                    || (_take.localPosition - _takeLocalSeen).sqrMagnitude > 0.0001f);
            if (!_takeMeasured || moved)
            {
                if (!_take.gameObject.activeInHierarchy || _take.rect.width < 1f)
                    return;
                _takeSize0 = _take.sizeDelta;
                _takeLocal0 = _take.localPosition;
                _takeWidth0 = _take.rect.width;
                _takeMeasured = true;
            }

            _take.sizeDelta = _takeSize0;
            _take.localPosition = _takeLocal0;
            float h = _take.rect.height;
            float takeLeft = _takeLocal0.x - _take.pivot.x * _takeWidth0;
            float takeRight = takeLeft + _takeWidth0;
            float centerY = _takeLocal0.y + (0.5f - _take.pivot.y) * h;

            float titleLeft = TitleLeft(gui.m_containerName, parent, takeRight + 2f * (h * ChestButtonAspect + Gap) + Gap);
            float w = h * ChestButtonAspect;
            float room = titleLeft - Gap - takeRight;
            float need = 2f * (w + Gap);
            float takeWidth = _takeWidth0;
            if (room < need)
            {
                takeWidth = Mathf.Max(h * 2f, _takeWidth0 - (need - room));
                float left = titleLeft - Gap - (takeLeft + takeWidth);
                if (left < need)
                    w = Mathf.Max(h, left * 0.5f - Gap);
                _take.sizeDelta = new Vector2(_takeSize0.x + (takeWidth - _takeWidth0), _takeSize0.y);
                _take.localPosition = new Vector3(takeLeft + _take.pivot.x * takeWidth, _takeLocal0.y, _takeLocal0.z);
            }

            float x = takeLeft + takeWidth + Gap + w * 0.5f;
            Set(_chestSort, new Vector2(x, centerY), new Vector2(w, h));
            Set(_chestStack, new Vector2(x + w + Gap, centerY), new Vector2(w, h));

            _takeSizeSeen = _take.sizeDelta;
            _takeLocalSeen = _take.localPosition;
        }

        private static void LayoutBag(InventoryGui gui)
        {
            if (_bagStack == null || _bagSort == null || gui.m_player == null)
                return;

            // Drawn under the inventory wood (Photoshop-layer style). Chest buttons are untouched.
            _bagSort.SetAsFirstSibling();
            _bagStack.SetAsFirstSibling();

            // Top right, sitting on the upper edge of the inventory: the right side under the armor is where
            // trash can mods put theirs. Laid out from the live panel rect, so other inventory sizes work too.
            Rect panel = gui.m_player.rect;
            float h = _take.rect.height;
            float w = h * ChestButtonAspect;
            float cy = panel.yMax + h * 0.5f - BagOverlap;
            float sortX = panel.xMax - BagRightMargin - w * 0.5f;
            float stackX = sortX - w - Gap;

            Set(_bagStack, new Vector2(stackX, cy), new Vector2(w, h));
            Set(_bagSort, new Vector2(sortX, cy), new Vector2(w, h));
        }

        private static float TitleLeft(TMP_Text title, RectTransform parent, float fallback)
        {
            if (title == null || string.IsNullOrEmpty(title.text))
                return fallback;
            Bounds b = title.textBounds;
            if (b.size.x <= 0.01f)
                return fallback;
            Vector3 world = title.transform.TransformPoint(new Vector3(b.min.x, b.center.y, 0f));
            return parent.InverseTransformPoint(world).x;
        }

        private static void Set(RectTransform rt, Vector2 localCenter, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
        }

        private static RectTransform Make(Button template, Transform parent, string name, string label, UnityAction onClick)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(true);

            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true))
            {
                if (pad.m_hint != null)
                    Object.DestroyImmediate(pad.m_hint);
                Object.DestroyImmediate(pad);
            }
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                Component localize = t.GetComponent("Localize");
                if (localize != null)
                    Object.DestroyImmediate(localize);
            }

            foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = label;
                text.enableAutoSizing = true;
                text.fontSizeMin = 8f;
                text.fontSizeMax = Mathf.Max(8f, text.fontSize);
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
            foreach (Text text in go.GetComponentsInChildren<Text>(true))
                text.text = label;

            Button button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            button.interactable = true;
            return (RectTransform)go.transform;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class InventoryButtonsShowPatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            try
            {
                InventoryButtons.EnsureBuilt(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[InventoryButtons] build failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryButtonsLayoutPatch
    {
        private static bool _warned;

        private static void Postfix(InventoryGui __instance)
        {
            try
            {
                InventoryButtons.Layout(__instance);
            }
            catch (System.Exception ex)
            {
                if (!_warned)
                    Plugin.Log.LogWarning("[InventoryButtons] layout failed: " + ex.Message);
                _warned = true;
            }
        }
    }
}
