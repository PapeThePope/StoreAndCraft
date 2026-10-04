using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Compact scarecrow settings (Alt+E): small panel top-left under the hotbar so the field stays
    /// visible. Crop button (opens a small crop list beside the panel), Harvest / Plant switches and the
    /// l1–l9 link buttons. The grid size is changed in the world (Shift / Ctrl + wheel).
    /// Hosted by StationFilterMenu (open / close / Esc handling); this class only builds the UI.
    /// </summary>
    internal static class ScarecrowPanel
    {
        private const float PanelW = 330f;
        private const float Pad = 24f;
        private const float RowH = 40f;
        private const float RowGap = 8f;
        private const float CellW = 92f;
        private const float CellH = 92f;
        private static readonly Color Gold = new Color(0.925f, 0.77f, 0.29f, 1f);
        private static readonly Color Muted = new Color(0.78f, 0.72f, 0.60f, 0.9f);

        private static RectTransform _root;
        private static Scarecrow _scarecrow;
        private static UnityAction _close;
        private static GameObject _panel;
        private static GameObject _list;
        private static GameObject _linkGrid;
        private static bool _listOpen;
        private static float _levelArmedUntil;

        internal static void Create(RectTransform root, Scarecrow scarecrow, UnityAction close)
        {
            _root = root;
            _scarecrow = scarecrow;
            _close = close;
            _listOpen = false;
            Build();
        }

        internal static void Rebuild()
        {
            if (_root == null || _scarecrow == null)
                return;
            Build();
        }

        internal static void Dispose()
        {
            _root = null;
            _scarecrow = null;
            _close = null;
            _panel = null;
            _list = null;
            _linkGrid = null;
            _listOpen = false;
        }

        private static void Build()
        {
            if (_panel != null)
                Object.Destroy(_panel);
            if (_list != null)
                Object.Destroy(_list);
            _panel = null;
            _list = null;
            _linkGrid = null;

            Scarecrow s = _scarecrow;
            float linkBlock = UiLinkGrid.BlockSize(UiLinkGrid.Cell, UiLinkGrid.Gap);
            float top = 56f;
            float y1 = top;
            const float infoH = 24f; // the seed count under the crop button
            float y2 = y1 + RowH + infoH + RowGap;
            float y2b = y2 + RowH + RowGap;
            float y2c = y2b + RowH + RowGap;
            float y3 = y2c + RowH + RowGap;
            float panelH = y3 + linkBlock + Pad;

            _panel = NewFrame("SAC_ScarecrowPanel", new Vector2(30f, -150f), new Vector2(PanelW, panelH));
            RectTransform p = (RectTransform)_panel.transform;

            Label(p, Loc.T("Scarecrow", "Vogelscheuche"), new Vector2(Pad, -Pad + 2f), new Vector2(PanelW - 2f * Pad - 36f, 26f), 20f, Gold,
                TextAlignmentOptions.TopLeft);
            Button(p, "X", new Vector2(PanelW - Pad - 28f, -Pad + 4f), new Vector2(28f, 28f), "X", () => _close?.Invoke(), false);

            // Crop: icon + name, opens the crop list.
            string seed = s.CropSeed;
            string cropName = string.IsNullOrEmpty(seed)
                ? Loc.T("choose...", "wählen...")
                : CropMap.DisplayName(seed);
            RectTransform cropBtn = Button(p, "Crop", new Vector2(Pad, -y1), new Vector2(PanelW - 2f * Pad, RowH),
                Loc.T("Crop: ", "Pflanze: ") + cropName, () => { _listOpen = !_listOpen; Build(); }, _listOpen);
            Sprite icon = string.IsNullOrEmpty(seed) ? null : StationFilterMenu.ItemIcon(seed);
            if (icon != null)
                IconImage(cropBtn, icon, new Vector2(8f, -6f), 28f);

            // How many seeds the whole grid takes (the middle point is the scarecrow's).
            Label(p, Loc.T("Needed for the whole grid: ", "Benötigt für das ganze Raster: ") + "<color=#ECC44B>" + s.SeedsNeeded + "</color>",
                new Vector2(Pad + 4f, -(y1 + RowH + 3f)), new Vector2(PanelW - 2f * Pad - 8f, infoH - 4f), 15f, Muted,
                TextAlignmentOptions.TopLeft);

            float half = (PanelW - 2f * Pad - RowGap) * 0.5f;
            Button(p, "Harvest", new Vector2(Pad, -y2), new Vector2(half, RowH),
                Loc.T("Harvest: ", "Ernten: ") + OnOff(s.Harvest), () => { s.SetHarvest(!s.Harvest); Build(); }, s.Harvest);
            Button(p, "Plant", new Vector2(Pad + half + RowGap, -y2), new Vector2(half, RowH),
                Loc.T("Plant: ", "Pflanzen: ") + OnOff(s.PlantOn), () => { s.SetPlant(!s.PlantOn); Build(); }, s.PlantOn);

            // Auto-fill on/off (same flag as [B]).
            bool autoOn = StationAutoFill.IsOn(s.GetComponent<ZNetView>());
            Button(p, "AutoFill", new Vector2(Pad, -y2b), new Vector2(PanelW - 2f * Pad, RowH),
                Loc.T("Auto-fill: ", "Auto-Fill: ") + OnOff(autoOn), () => { StationAutoFill.SetOn(s, !autoOn); Build(); }, autoOn);

            // Terrain, only on a press: level the grid to the scarecrow's ground, cultivate it like the Cultivator.
            bool armed = Time.unscaledTime < _levelArmedUntil;
            Button(p, "Level", new Vector2(Pad, -y2c), new Vector2(half, RowH),
                s.TerrainBusy ? Loc.T("working...", "arbeitet...")
                    : armed ? Loc.T("Click again!", "Nochmal klicken!") : Loc.T("Level ground", "Einebnen"),
                () =>
                {
                    if (s.TerrainBusy)
                        return;
                    if (Time.unscaledTime < _levelArmedUntil)
                    {
                        _levelArmedUntil = 0f;
                        s.StartTerrain(true);
                    }
                    else
                    {
                        _levelArmedUntil = Time.unscaledTime + 4f; // permanent terrain change: confirm with a second click
                    }
                    Build();
                }, armed);
            Button(p, "Cultivate", new Vector2(Pad + half + RowGap, -y2c), new Vector2(half, RowH),
                Loc.T("Cultivate", "Bestellen"),
                () => { if (!s.TerrainBusy) s.StartTerrain(false); Build(); }, false);

            // Links.
            _linkGrid = UiLinkGrid.Build(p, "SAC_ScarecrowLinks", StationLink.Get(s), SetLink, UiLinkGrid.Cell, UiLinkGrid.Gap);
            RectTransform lg = (RectTransform)_linkGrid.transform;
            lg.anchorMin = lg.anchorMax = lg.pivot = new Vector2(0f, 1f);
            lg.anchoredPosition = new Vector2((PanelW - linkBlock) * 0.5f, -y3);

            if (_listOpen)
                BuildCropList(panelH);
        }

        private static void BuildCropList(float panelH)
        {
            Scarecrow s = _scarecrow;
            List<string> all = CropMap.SeedChoices();
            var shown = new List<string>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (DisplayFilters.IsKnownToPlayer(all[i]) || all[i] == s.CropSeed)
                    shown.Add(all[i]);
            }

            const int cols = 4;
            int rows = Mathf.Max(1, (shown.Count + cols - 1) / cols);
            float w = 2f * 18f + cols * CellW + (cols - 1) * 6f;
            float h = 2f * 18f + rows * CellH + (rows - 1) * 6f;
            _list = NewFrame("SAC_ScarecrowCrops", new Vector2(30f + PanelW + 8f, -150f), new Vector2(w, h));
            RectTransform lp = (RectTransform)_list.transform;
            if (shown.Count == 0)
                Label(lp, Loc.T("No crop seeds discovered yet.", "Noch keine Samen entdeckt."),
                    new Vector2(18f, -18f), new Vector2(w - 36f, 40f), 15f, Muted, TextAlignmentOptions.TopLeft);

            for (int i = 0; i < shown.Count; i++)
            {
                string shared = shown[i];
                int col = i % cols;
                int row = i / cols;
                bool on = shared == s.CropSeed;
                RectTransform cell = Button(lp, "Crop" + i,
                    new Vector2(18f + col * (CellW + 6f), -(18f + row * (CellH + 6f))),
                    new Vector2(CellW, CellH), "", () => PickCrop(shared), on);
                Sprite icon = StationFilterMenu.ItemIcon(shared);
                if (icon != null)
                    IconImage(cell, icon, new Vector2((CellW - 44f) * 0.5f, -8f), 44f);
                Label(cell, CropMap.DisplayName(shared), new Vector2(4f, -54f), new Vector2(CellW - 8f, 34f), 12f,
                    on ? Gold : Muted, TextAlignmentOptions.Top);
            }
        }

        private static void PickCrop(string shared)
        {
            if (_scarecrow == null)
                return;
            _scarecrow.SetCrop(shared);
            _listOpen = false;
            Build();
        }

        private static void SetLink(int id)
        {
            Scarecrow s = _scarecrow;
            if (s == null)
                return;
            StationLink.Toggle(s, id);
            if (_linkGrid != null)
                UiLinkGrid.RefreshSelection(_linkGrid, StationLink.Get(s));
            ActivityLog.Note(StationOutput.StationLabel(s), Loc.T("Link", "Link") + " " + StationLink.ShortToken(StationLink.Get(s)));
            StationAutoFill.WakeStation(s);
        }

        private static string OnOff(bool on)
        {
            return on ? Loc.T("on", "an") : Loc.T("off", "aus");
        }

        // ---- small UI helpers (top-left anchored, y negative downwards)

        private static GameObject NewFrame(string name, Vector2 pos, Vector2 size)
        {
            return NewFrame(_root, name, pos, size);
        }

        internal static GameObject NewFrame(RectTransform root, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(root, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            Sprite frame = UiStyle.Sprite("woodpanel_settings");
            if (frame != null)
            {
                UiStyle.SetFrame(img, frame, Mathf.Min(size.x, size.y));
            }
            else
            {
                img.sprite = null;
                img.color = new Color(0.16f, 0.11f, 0.07f, 0.95f);
            }
            img.raycastTarget = true;
            return go;
        }

        internal static RectTransform Button(RectTransform parent, string name, Vector2 pos, Vector2 size, string label,
            UnityAction onClick, bool on)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = go.GetComponent<Image>();
            Sprite sprite = UiStyle.Sprite(on ? "button_highlight" : "button") ?? UiStyle.Sprite("button");
            if (sprite != null)
            {
                UiStyle.SetFrame(img, sprite, Mathf.Min(size.x, size.y));
                img.color = new Color(1f, 1f, 1f, on ? 1f : 0.85f);
            }
            else
            {
                img.sprite = null;
                img.color = on ? new Color(0.45f, 0.32f, 0.12f, 1f) : new Color(0.2f, 0.15f, 0.1f, 0.95f);
            }
            img.raycastTarget = true;

            UnityEngine.UI.Button btn = go.GetComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            if (!string.IsNullOrEmpty(label))
            {
                bool hasIcon = name == "Crop";
                Label(rt, label, new Vector2(hasIcon ? 42f : 6f, -2f), new Vector2(size.x - (hasIcon ? 48f : 12f), size.y - 4f), 17f,
                    on ? Gold : new Color(0.95f, 0.9f, 0.8f, 1f),
                    hasIcon ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            }
            return rt;
        }

        internal static void Label(RectTransform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color,
            TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            TextMeshProUGUI tmp = UiFonts.CreateLabel(go, fontSize);
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = align;
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
        }

        internal static void IconImage(RectTransform parent, Sprite sprite, Vector2 pos, float size)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
    }
}
