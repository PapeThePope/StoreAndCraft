using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Compact scarecrow settings (Alt+E): small panel top-left under the hotbar so the field stays
    /// visible. Crop button (opens a small crop list beside the panel: pick several, each gets a colour), the
    /// pattern editor (draw which crop grows where on the grid), Harvest / Plant switches and the l1–l9 link
    /// buttons. The grid size is changed in the world (Shift / Ctrl + wheel).
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
        private static GameObject _pattern;
        private static GameObject _linkGrid;
        private static int _brush; // palette index painted with, -1 = erase

        private const float PatternInner = 350f;
        private const float PatternW = PatternInner + 36f;

        /// <summary>Colour of the crops in the list and in the pattern, by palette index.</summary>
        private static readonly Color[] CropColors =
        {
            new Color(0.36f, 0.75f, 0.35f), new Color(0.95f, 0.55f, 0.15f), new Color(0.35f, 0.55f, 0.95f),
            new Color(0.70f, 0.40f, 0.85f), new Color(0.90f, 0.30f, 0.30f), new Color(0.95f, 0.85f, 0.25f),
            new Color(0.30f, 0.85f, 0.85f), new Color(0.95f, 0.50f, 0.70f)
        };
        private static readonly Color EmptyColor = new Color(0.22f, 0.17f, 0.12f, 1f);
        private static Color PaletteColor(int index)
        {
            return index >= 0 && index < CropColors.Length ? CropColors[index] : EmptyColor;
        }
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
            _pattern = null;
            _linkGrid = null;
            _listOpen = false;
        }

        private static void Build()
        {
            if (_panel != null)
                Object.Destroy(_panel);
            if (_list != null)
                Object.Destroy(_list);
            if (_pattern != null)
                Object.Destroy(_pattern);
            _panel = null;
            _list = null;
            _pattern = null;
            _linkGrid = null;

            Scarecrow s = _scarecrow;
            List<string> palette = s.Palette();
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

            // Crops: icon + name (or the number), opens the crop list where several can be picked.
            string seed = s.CropSeed;
            string cropName = palette.Count == 0
                ? Loc.T("choose...", "wählen...")
                : palette.Count == 1 ? CropMap.DisplayName(seed)
                : palette.Count + " " + Loc.T("selected", "gewählt");
            RectTransform cropBtn = Button(p, "Crop", new Vector2(Pad, -y1), new Vector2(PanelW - 2f * Pad, RowH),
                (palette.Count > 1 ? Loc.T("Crops: ", "Pflanzen: ") : Loc.T("Crop: ", "Pflanze: ")) + cropName,
                () => { _listOpen = !_listOpen; Build(); }, _listOpen);
            Sprite icon = string.IsNullOrEmpty(seed) ? null : StationFilterMenu.ItemIcon(CropMap.ItemOf(seed));
            if (icon != null)
                IconImage(cropBtn, icon, new Vector2(8f, -6f), 28f);

            // How many seeds the whole grid takes (the middle point is the scarecrow's).
            Label(p, Loc.T("Needed for the whole grid: ", "Benötigt für das ganze Raster: ") + "<color=#ECC44B>" + s.NeededText() + "</color>",
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

            if (palette.Count > 0)
                BuildPattern(s, palette);
            if (_listOpen)
                BuildCropList(panelH, palette.Count > 0 ? PatternW + 8f : 0f);
        }

        // ---- pattern: which crop grows on which grid point

        private static void BuildPattern(Scarecrow s, List<string> palette)
        {
            int cols = s.Cols, rows = s.Rows;
            if (_brush >= palette.Count)
                _brush = palette.Count - 1;
            if (_brush < -1)
                _brush = -1;

            // Brushes: the eraser and every picked crop in its colour.
            const float chip = 40f, chipGap = 4f;
            int chips = palette.Count + 1;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((PatternInner + chipGap) / (chip + chipGap)));
            int chipRows = (chips + perRow - 1) / perRow;
            float chipsTop = 66f;
            float gridTop = chipsTop + chipRows * (chip + chipGap) + 8f;
            float cell = Mathf.Clamp(Mathf.Floor(PatternInner / Mathf.Max(cols, rows)), 8f, 26f);
            float gridW = cell * cols, gridH = cell * rows;
            float noteTop = gridTop + gridH + 6f;
            float buttonsTop = noteTop + 24f;
            float h = buttonsTop + RowH + 18f;

            _pattern = NewFrame("SAC_ScarecrowPattern", new Vector2(30f + PanelW + 8f, -150f), new Vector2(PatternW, h));
            RectTransform fp = (RectTransform)_pattern.transform;
            Label(fp, Loc.T("Pattern", "Muster"), new Vector2(18f, -16f), new Vector2(PatternW - 36f, 24f), 20f, Gold, TextAlignmentOptions.TopLeft);
            Label(fp, Loc.T("Pick a crop, then click or drag on the grid. Right click erases.",
                    "Pflanze wählen, dann im Raster klicken oder ziehen. Rechtsklick löscht."),
                new Vector2(18f, -40f), new Vector2(PatternW - 36f, 22f), 13f, Muted, TextAlignmentOptions.TopLeft);

            for (int i = 0; i < chips; i++)
            {
                int brush = i - 1; // chip 0 = eraser
                float x = 18f + (i % perRow) * (chip + chipGap);
                float y = chipsTop + (i / perRow) * (chip + chipGap);
                bool on = brush == _brush;
                RectTransform btn = Button(fp, "Brush" + i, new Vector2(x, -y), new Vector2(chip, chip), "",
                    () => { _brush = brush; Build(); }, on);
                Swatch(btn, PaletteColor(brush), new Vector2(5f, -5f), chip - 10f);
                if (brush < 0)
                {
                    Label(btn, "—", new Vector2(0f, -8f), new Vector2(chip, chip - 16f), 20f, Muted, TextAlignmentOptions.Center);
                }
                else
                {
                    Sprite icon = StationFilterMenu.ItemIcon(CropMap.ItemOf(palette[brush]));
                    if (icon != null)
                        IconImage(btn, icon, new Vector2(9f, -9f), chip - 18f);
                }
            }

            // The grid: front row (the way the scarecrow looks) on top, like seen from behind it.
            float gx = 18f + (PatternInner - gridW) * 0.5f;
            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gridGo.transform.SetParent(fp, false);
            RectTransform gr = (RectTransform)gridGo.transform;
            gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(0f, 1f);
            gr.anchoredPosition = new Vector2(gx, -gridTop);
            gr.sizeDelta = new Vector2(gridW, gridH);
            Image back = gridGo.GetComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.4f);
            back.raycastTarget = true;

            int[] idx = (int[])s.CellIndices().Clone(); // painted on here, written back when the stroke ends
            var imgs = new Image[idx.Length];
            int center = s.CenterIndex;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    int visualRow = rows - 1 - r;
                    imgs[i] = Swatch(gr, i == center ? EmptyColor : PaletteColor(idx[i]),
                        new Vector2(c * cell + 0.5f, -(visualRow * cell + 0.5f)), cell - 1f);
                    if (i == center)
                        Label((RectTransform)imgs[i].transform, "S", Vector2.zero, new Vector2(cell - 1f, cell - 1f),
                            Mathf.Min(14f, cell - 4f), Gold, TextAlignmentOptions.Center);
                }
            }
            PatternPaint paint = gridGo.AddComponent<PatternPaint>();
            paint.Cols = cols;
            paint.Rows = rows;
            paint.Cell = cell;
            paint.Center = center;
            paint.Idx = idx;
            paint.Imgs = imgs;
            paint.Brush = () => _brush;
            paint.Commit = () => { s.SetCells(idx); Build(); };

            Label(fp, Loc.T("Top = in front of the scarecrow. S = the scarecrow.", "Oben = vor der Vogelscheuche. S = die Vogelscheuche."),
                new Vector2(18f, -noteTop), new Vector2(PatternW - 36f, 20f), 13f, Muted, TextAlignmentOptions.TopLeft);

            float half = (PatternInner - RowGap) * 0.5f;
            Button(fp, "FillAll", new Vector2(18f, -buttonsTop), new Vector2(half, RowH),
                Loc.T("Fill with brush", "Alles füllen"), () =>
                {
                    int v = _brush;
                    for (int i = 0; i < idx.Length; i++)
                        idx[i] = i == center ? idx[i] : v;
                    s.SetCells(idx);
                    Build();
                }, false);
            Button(fp, "ClearAll", new Vector2(18f + half + RowGap, -buttonsTop), new Vector2(half, RowH),
                Loc.T("Clear", "Leeren"), () =>
                {
                    for (int i = 0; i < idx.Length; i++)
                        idx[i] = -1;
                    s.SetCells(idx);
                    Build();
                }, false);
        }

        private static Image Swatch(RectTransform parent, Color color, Vector2 pos, float size)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Paints on the pattern grid: click or drag with the left button, the right button erases.</summary>
        private sealed class PatternPaint : MonoBehaviour,
            UnityEngine.EventSystems.IPointerDownHandler,
            UnityEngine.EventSystems.IDragHandler,
            UnityEngine.EventSystems.IPointerUpHandler
        {
            public int Cols, Rows, Center;
            public float Cell;
            public int[] Idx;
            public Image[] Imgs;
            public System.Func<int> Brush;
            public System.Action Commit;
            private bool _changed;

            public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
            {
                _changed = false;
                Paint(e);
            }

            public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
            {
                Paint(e);
            }

            public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e)
            {
                if (_changed && Commit != null)
                    Commit();
                _changed = false;
            }

            private void Paint(UnityEngine.EventSystems.PointerEventData e)
            {
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out local))
                    return;
                int c = Mathf.FloorToInt(local.x / Cell);
                int visualRow = Mathf.FloorToInt(-local.y / Cell);
                if (c < 0 || c >= Cols || visualRow < 0 || visualRow >= Rows)
                    return;
                int i = (Rows - 1 - visualRow) * Cols + c;
                if (i == Center)
                    return;
                int v = e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right ? -1 : Brush();
                if (Idx[i] == v)
                    return;
                Idx[i] = v;
                Imgs[i].color = PaletteColor(v);
                _changed = true;
            }
        }

        private static void BuildCropList(float panelH, float shift)
        {
            Scarecrow s = _scarecrow;
            List<string> palette = s.Palette();
            List<string> all = CropMap.SeedChoices();
            var shown = new List<string>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (DisplayFilters.IsKnownToPlayer(CropMap.ItemOf(all[i])) || palette.Contains(all[i]))
                    shown.Add(all[i]);
            }

            const int cols = 4;
            int rows = Mathf.Max(1, (shown.Count + cols - 1) / cols);
            float w = 2f * 18f + cols * CellW + (cols - 1) * 6f;
            float h = 2f * 18f + rows * CellH + (rows - 1) * 6f;
            _list = NewFrame("SAC_ScarecrowCrops", new Vector2(30f + PanelW + 8f + shift, -150f), new Vector2(w, h));
            RectTransform lp = (RectTransform)_list.transform;
            if (shown.Count == 0)
                Label(lp, Loc.T("No crop seeds discovered yet.", "Noch keine Samen entdeckt."),
                    new Vector2(18f, -18f), new Vector2(w - 36f, 40f), 15f, Muted, TextAlignmentOptions.TopLeft);

            for (int i = 0; i < shown.Count; i++)
            {
                string shared = shown[i];
                int col = i % cols;
                int row = i / cols;
                int inBed = palette.IndexOf(shared);
                bool on = inBed >= 0;
                RectTransform cell = Button(lp, "Crop" + i,
                    new Vector2(18f + col * (CellW + 6f), -(18f + row * (CellH + 6f))),
                    new Vector2(CellW, CellH), "", () => PickCrop(shared), on);
                if (on)
                    Swatch(cell, PaletteColor(inBed), new Vector2(CellW - 22f, -8f), 14f);
                Sprite icon = StationFilterMenu.ItemIcon(CropMap.ItemOf(shared));
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
            _scarecrow.ToggleCrop(shared); // several can be picked, the list stays open
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
