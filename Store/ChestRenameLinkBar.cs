using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Rename-chest extras: Ignore / Only build/craft / Only stations / Show-on-display toggles + l1–l9 link grid.
    /// The toggles hold their state here and are saved as chest flags (ChestNames.FlagsKey) when the
    /// dialog is confirmed; the name field only keeps the name and the [lN] link tag.
    /// </summary>
    internal static class ChestRenameLinkBar
    {
        private static readonly List<GameObject> Owned = new List<GameObject>();
        private static readonly FieldInfo InputField =
            AccessTools.Field(typeof(TextInput), "m_inputField");
        private static readonly FieldInfo PanelField =
            AccessTools.Field(typeof(TextInput), "m_panel");

        private static GameObject _ignoreToggle;
        private static GameObject _showToggle;
        private static GameObject _craftOnlyToggle;
        private static GameObject _noDumpToggle;
        private static int _flags;
        private static GameObject _linkGrid;
        private static TMP_FontAsset _font;
        private static Container _chest;

        public static void Show(Container chest = null)
        {
            Hide();
            _chest = chest;
            TextInput ti = TextInput.instance;
            if (ti == null)
                return;

            GameObject panel = PanelField?.GetValue(ti) as GameObject;
            if (panel == null)
                return;

            RectTransform panelRt = panel.transform as RectTransform;
            if (panelRt == null)
                return;

            _font = UiFonts.ThinNorse() ?? ResolveFont(ti);

            string current = ReadFieldText();
            _flags = chest != null ? ChestNames.FlagsOf(chest) : ChestNames.FlagsFromName(current);
            bool ignore = (_flags & ChestNames.FlagIgnore) != 0;
            bool showOnDisplay = (_flags & ChestNames.FlagShow) != 0;
            int linkId = StationLink.ParseFromName(current);
            bool showOk = ignore;
            bool craftOnly = (_flags & ChestNames.FlagCraftOnly) != 0 && !ignore;
            bool noDump = (_flags & ChestNames.FlagNoDump) != 0;

            // Left: Only stations, Only build/craft. Right: Ignore next to Show on display. Each mode has a "?"
            // that explains it on hover.
            RectTransform panelRect = panelRt;
            RectTransform noDumpRt = AddModeBlock(panelRect, "SAC_NoDumpBlock", Loc.T("Only stations", "Nur Stationen"),
                noDump, OnNoDumpClicked, ChestModeHelp.Mode.NoDump, false, out _noDumpToggle);
            noDumpRt.anchoredPosition = new Vector2(10f, -6f);
            RectTransform craftOnlyRt = AddModeBlock(panelRect, "SAC_CraftOnlyBlock", Loc.T("Only build/craft", "Nur Bauen/Craften"),
                craftOnly, OnCraftOnlyClicked, ChestModeHelp.Mode.CraftOnly, false, out _craftOnlyToggle);
            craftOnlyRt.anchoredPosition = new Vector2(10f + noDumpRt.sizeDelta.x + 16f, -6f);

            // Show on display — far right, only when Ignore is on
            var showBlock = new GameObject("SAC_ShowBlock", typeof(RectTransform));
            showBlock.transform.SetParent(panel.transform, false);
            RectTransform showRt = showBlock.transform as RectTransform;
            showRt.anchorMin = new Vector2(1f, 1f);
            showRt.anchorMax = new Vector2(1f, 1f);
            showRt.pivot = new Vector2(1f, 1f);
            showRt.anchoredPosition = new Vector2(-10f, -6f);
            showRt.sizeDelta = new Vector2(UiToggle.CompactWidth + 8f, UiToggle.CompactHeight + 20f);
            Owned.Add(showBlock);
            TextMeshProUGUI showLabel = AddLabel(showRt, Loc.T("Show on display", "Auf Display zeigen"), new Vector2(0f, -1f), true);
            _showToggle = UiToggle.Create(
                showRt,
                "SAC_ShowToggle",
                showOk && showOnDisplay,
                showOk,
                OnShowClicked,
                _font,
                UiToggle.CompactWidth,
                UiToggle.CompactHeight);
            PlaceToggle(_showToggle, new Vector2(0f, -16f), true);

            // Ignore — left of Show on display (right-anchored, label + "?" + checkbox left-aligned inside)
            RectTransform ignoreRt = AddModeBlock(panelRect, "SAC_IgnoreBlock", Loc.T("Ignore", "Ignorieren"),
                ignore, OnIgnoreClicked, ChestModeHelp.Mode.Ignore, true, out _ignoreToggle);
            float showW = Mathf.Max(TextWidth(showLabel), 30f);
            ignoreRt.anchoredPosition = new Vector2(-(10f + showW + 16f), -6f);

            Owned.Add(ChestModeHelp.EnsureTip(panelRect));

            // Link grid — bottom-left (dimmed while Ignore is on, still clickable)
            _linkGrid = UiLinkGrid.Build(panel.transform, "SAC_ChestLinks", linkId, OnLinkClicked);
            RectTransform gridRt = _linkGrid.transform as RectTransform;
            gridRt.anchorMin = new Vector2(0f, 0f);
            gridRt.anchorMax = new Vector2(0f, 0f);
            gridRt.pivot = new Vector2(0f, 0f);
            gridRt.anchoredPosition = new Vector2(10f, 16f);
            Owned.Add(_linkGrid);
            UiLinkGrid.SetDimmed(_linkGrid, ignore);
        }

        public static void Hide()
        {
            ChestModeHelp.Reset();
            _ignoreToggle = null;
            _showToggle = null;
            _craftOnlyToggle = null;
            _noDumpToggle = null;
            _flags = 0;
            _linkGrid = null;
            _chest = null;
            for (int i = 0; i < Owned.Count; i++)
            {
                if (Owned[i] != null)
                    Object.Destroy(Owned[i]);
            }
            Owned.Clear();
        }

        private static void PlaceToggle(GameObject toggle, Vector2 anchored, bool fromRight = false)
        {
            RectTransform rt = toggle.transform as RectTransform;
            if (fromRight)
            {
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
            }
            rt.anchoredPosition = anchored;
            // Pivot moved the drawn checkbox to the edge — keep the yellow tick on it.
            UiToggle.RefreshMarker(toggle);
        }

        private static TextMeshProUGUI AddLabel(RectTransform parent, string text, Vector2 pos, bool fromRight = false)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.transform as RectTransform;
            if (fromRight)
            {
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
            }
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(160f, 18f);

            TextMeshProUGUI label = UiFonts.CreateLabel(go, 13f);
            label.text = text;
            label.color = new Color(1f, 0.85f, 0.4f, 1f);
            label.alignment = fromRight ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static float TextWidth(TextMeshProUGUI label)
        {
            float w = label != null ? label.preferredWidth : 0f;
            return w > 1f ? w : (label != null ? label.text.Length * 7f : 0f);
        }

        /// <summary>
        /// One mode: label, "?" help circle right after it, checkbox below. Returns the block (its
        /// width fits label + "?"); the caller sets anchoredPosition. rightAnchored = anchored to the
        /// panel's top-right corner (pivot right), content stays left-aligned inside.
        /// </summary>
        private static RectTransform AddModeBlock(RectTransform panel, string name, string text, bool on,
            UnityEngine.Events.UnityAction click, ChestModeHelp.Mode mode, bool rightAnchored, out GameObject toggle)
        {
            var block = new GameObject(name, typeof(RectTransform));
            block.transform.SetParent(panel, false);
            RectTransform rt = block.transform as RectTransform;
            float ax = rightAnchored ? 1f : 0f;
            rt.anchorMin = new Vector2(ax, 1f);
            rt.anchorMax = new Vector2(ax, 1f);
            rt.pivot = new Vector2(ax, 1f);
            Owned.Add(block);

            TextMeshProUGUI label = AddLabel(rt, text, new Vector2(0f, -1f));
            float textW = TextWidth(label);
            rt.sizeDelta = new Vector2(
                Mathf.Max(textW + 4f + ChestModeHelp.Size, 30f),
                UiToggle.CompactHeight + 20f);
            Owned.Add(ChestModeHelp.Create(rt, panel, mode, new Vector2(textW + 4f, -1f)));

            toggle = UiToggle.Create(rt, name.Replace("Block", "Toggle"), on, true, click, _font,
                UiToggle.CompactWidth, UiToggle.CompactHeight);
            PlaceToggle(toggle, new Vector2(0f, -16f));
            return rt;
        }

        private static void OnIgnoreClicked()
        {
            bool ignore = (_flags & ChestNames.FlagIgnore) == 0;
            // Turning Ignore on while a link is set: drop the link automatically.
            string current = ReadFieldText();
            int link = StationLink.ParseFromName(current);
            if (ignore && link > 0)
                WriteFieldText(StationLink.ApplyToName(current, link));
            // Ignore, Only build/craft and Only stations are exclusive modes; turning Ignore off also drops Show.
            _flags = ignore
                ? (_flags | ChestNames.FlagIgnore) & ~(ChestNames.FlagCraftOnly | ChestNames.FlagNoDump)
                : _flags & ~(ChestNames.FlagIgnore | ChestNames.FlagShow);
            RefreshToggles();
            RefreshLinks();
        }

        private static void OnShowClicked()
        {
            if ((_flags & ChestNames.FlagIgnore) == 0)
                return;
            _flags ^= ChestNames.FlagShow;
            RefreshToggles();
            RefreshLinks();
        }

        /// <summary>Only build/craft on clears Ignore / Show / Only stations (exclusive modes). The link stays.</summary>
        private static void OnCraftOnlyClicked()
        {
            if ((_flags & ChestNames.FlagCraftOnly) != 0)
                _flags &= ~ChestNames.FlagCraftOnly;
            else
                _flags = (_flags | ChestNames.FlagCraftOnly)
                    & ~(ChestNames.FlagIgnore | ChestNames.FlagShow | ChestNames.FlagNoDump);
            RefreshToggles();
            RefreshLinks();
        }

        /// <summary>No dump only makes sense alone (Ignore / Only build/craft are other modes). Works with a link.</summary>
        private static void OnNoDumpClicked()
        {
            if ((_flags & ChestNames.FlagNoDump) != 0)
                _flags &= ~ChestNames.FlagNoDump;
            else
                _flags = (_flags | ChestNames.FlagNoDump)
                    & ~(ChestNames.FlagIgnore | ChestNames.FlagShow | ChestNames.FlagCraftOnly);
            RefreshToggles();
        }

        private static void OnLinkClicked(int linkId)
        {
            string current = ReadFieldText();
            string next = StationLink.ApplyToName(current, linkId);
            // Link and Ignore are mutually exclusive — picking a link clears Ignore / Show.
            // Only build/craft and Only stations stay.
            if (StationLink.ParseFromName(next) > 0)
                _flags &= ~(ChestNames.FlagIgnore | ChestNames.FlagShow);
            WriteFieldText(next);
            RefreshToggles();
            RefreshLinks();
        }

        /// <summary>Dialog confirmed: store the toggle state as the chest's flags.</summary>
        public static void CommitFlags(Container container)
        {
            if (container == null || container != _chest)
                return;
            ChestNames.SetFlags(container, _flags);
        }

        private static void RefreshToggles()
        {
            bool ignore = (_flags & ChestNames.FlagIgnore) != 0;
            UiToggle.SetState(_ignoreToggle, ignore, true);
            UiToggle.SetState(_showToggle, (_flags & ChestNames.FlagShow) != 0, ignore);
            UiToggle.SetState(_craftOnlyToggle, (_flags & ChestNames.FlagCraftOnly) != 0, true);
            UiToggle.SetState(_noDumpToggle, (_flags & ChestNames.FlagNoDump) != 0, true);
        }

        private static void RefreshLinks()
        {
            string current = ReadFieldText();
            int active = StationLink.ParseFromName(current);
            UiLinkGrid.RefreshSelection(_linkGrid, active);
            UiLinkGrid.SetDimmed(_linkGrid, (_flags & ChestNames.FlagIgnore) != 0);
        }

        private static string ReadFieldText()
        {
            TextInput ti = TextInput.instance;
            if (ti == null)
                return "";
            object field = InputField?.GetValue(ti);
            return field != null ? GetFieldText(field) : "";
        }

        private static void WriteFieldText(string next)
        {
            TextInput ti = TextInput.instance;
            if (ti == null)
                return;
            object field = InputField?.GetValue(ti);
            if (field == null)
                return;
            if (next != null && next.Length > ChestNames.MaxLength)
                next = next.Substring(0, ChestNames.MaxLength);
            SetFieldText(field, next ?? "");
        }

        private static TMP_FontAsset ResolveFont(TextInput ti)
        {
            return UiFonts.ThinNorse()
                ?? (ti != null ? ti.GetComponentInChildren<TMP_Text>(true)?.font : null);
        }

        private static string GetFieldText(object field)
        {
            PropertyInfo prop = field.GetType().GetProperty("text");
            if (prop != null)
                return prop.GetValue(field, null) as string ?? "";
            FieldInfo f = AccessTools.Field(field.GetType(), "m_text");
            if (f != null)
                return f.GetValue(field) as string ?? "";
            return "";
        }

        private static void SetFieldText(object field, string value)
        {
            PropertyInfo prop = field.GetType().GetProperty("text");
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(field, value, null);
                return;
            }

            MethodInfo set = AccessTools.Method(field.GetType(), "SetText", new[] { typeof(string) });
            if (set != null)
                set.Invoke(field, new object[] { value });
        }
    }

    [HarmonyPatch(typeof(TextInput), nameof(TextInput.Hide))]
    internal static class TextInputHideChestLinksPatch
    {
        private static void Prefix()
        {
            ChestRenameLinkBar.Hide();
        }
    }
}
