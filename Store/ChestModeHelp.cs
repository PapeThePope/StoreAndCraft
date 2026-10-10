using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Small "?" circle next to a chest mode in the settings dialog. Hovering it shows what that
    /// mode blocks and allows (tooltip under the dialog, only for the hovered mode).
    /// </summary>
    internal static class ChestModeHelp
    {
        internal enum Mode { CraftOnly, NoDump, Ignore }

        internal const float Size = 14f;
        private static readonly Color Gold = new Color(1f, 0.85f, 0.4f, 1f);
        private static Sprite _circle;
        private static GameObject _tip;
        private static TextMeshProUGUI _tipText;
        private static RectTransform _tipRt;

        /// <summary>"?" button at pos (top-left based, inside parent). Tooltip is built on first use.</summary>
        internal static GameObject Create(RectTransform parent, RectTransform panel, Mode mode, Vector2 pos)
        {
            EnsureTip(panel);

            var go = new GameObject("SAC_Help", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(Size, Size);
            Image img = go.GetComponent<Image>();
            img.sprite = Circle();
            img.color = Gold;
            img.raycastTarget = true;

            var q = new GameObject("Q", typeof(RectTransform));
            q.transform.SetParent(go.transform, false);
            RectTransform qrt = q.transform as RectTransform;
            qrt.anchorMin = Vector2.zero;
            qrt.anchorMax = Vector2.one;
            qrt.offsetMin = Vector2.zero;
            qrt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = UiFonts.CreateLabel(q, 11f);
            t.text = "?";
            t.color = Gold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;

            go.AddComponent<ModeHelpHover>().Mode = mode;
            return go;
        }

        /// <summary>
        /// Destroy the tooltip right now. Unity destroys normally only at the end of the frame, so a
        /// dialog reopened in the same frame would otherwise reuse the old, doomed tooltip.
        /// </summary>
        internal static void Reset()
        {
            if (_tip != null)
                Object.DestroyImmediate(_tip);
            _tip = null;
            _tipText = null;
            _tipRt = null;
        }

        /// <summary>The tooltip object (child of the dialog panel). Caller owns / destroys it.</summary>
        internal static GameObject EnsureTip(RectTransform panel)
        {
            if (_tip != null)
                return _tip;

            _tip = new GameObject("SAC_ModeTip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _tip.transform.SetParent(panel, false);
            _tipRt = _tip.transform as RectTransform;
            _tipRt.anchorMin = new Vector2(0.5f, 0f);
            _tipRt.anchorMax = new Vector2(0.5f, 0f);
            _tipRt.pivot = new Vector2(0.5f, 1f);
            _tipRt.anchoredPosition = new Vector2(0f, -6f);
            Image bg = _tip.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.03f, 0.02f, 0.92f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_tip.transform, false);
            RectTransform trt = textGo.transform as RectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 8f);
            trt.offsetMax = new Vector2(-12f, -8f);
            _tipText = UiFonts.CreateLabel(textGo, 13f);
            _tipText.color = new Color(0.92f, 0.9f, 0.85f, 1f);
            _tipText.alignment = TextAlignmentOptions.TopLeft;
            _tipText.textWrappingMode = TextWrappingModes.NoWrap;
            _tipText.overflowMode = TextOverflowModes.Overflow;
            _tipText.richText = true;
            _tipText.raycastTarget = false;

            _tip.SetActive(false);
            return _tip;
        }

        internal static void ShowTip(Mode mode)
        {
            if (_tip == null || _tipText == null)
                return;
            string text = Build(mode, out int lines);
            _tipText.text = text;
            RectTransform panel = _tip.transform.parent as RectTransform;
            float w = panel != null ? Mathf.Max(360f, panel.rect.width) : 520f;
            _tipRt.sizeDelta = new Vector2(w, lines * 17f + 20f);
            _tip.SetActive(true);
        }

        internal static void HideTip()
        {
            if (_tip != null)
                _tip.SetActive(false);
        }

        private static string Value(bool allowed, string extra = null)
        {
            string word = allowed ? Loc.T("allowed", "erlaubt") : Loc.T("blocked", "gesperrt");
            string color = allowed ? "#5fd36b" : "#e74c3c";
            return "<color=" + color + ">" + word + "</color>" + (extra != null ? " " + extra : "");
        }

        private static string Row(string label, string value)
        {
            return label + "<pos=62%>" + value + "\n";
        }

        private static string Build(Mode mode, out int lines)
        {
            string title;
            string what;
            string dump = Loc.T("Dump key / store one item", "Dump-Taste / Einzel-Einlagern");
            string ground = Loc.T("Items from the ground", "Items vom Boden");
            string output = Loc.T("Station output (auto-store)", "Ausgabe von Stationen");
            string take = Loc.T("Stations / crafting take from it", "Stationen / Craften holen daraus");
            string display = Loc.T("Counted on Storage Displays", "Auf Storage Displays gezählt");
            string rows;
            int extraLine = 0;

            switch (mode)
            {
                case Mode.CraftOnly:
                    title = Loc.T("Only build/craft", "Nur Bauen/Craften");
                    what = Loc.T("You fill it (by hand or dump), only crafting and building take from it.",
                        "Du befüllst sie (per Hand oder Dump), nur Craften und Bauen holen daraus.");
                    rows = Row(dump, Value(true)) + Row(ground, Value(false)) + Row(output, Value(false))
                        + Row(Loc.T("Stations take from it", "Stationen holen daraus"), Value(false))
                        + Row(Loc.T("Crafting / building take from it", "Craften / Bauen holen daraus"), Value(true))
                        + Row(display, Value(true));
                    extraLine = 1;
                    break;
                case Mode.NoDump:
                    title = Loc.T("Only stations", "Nur Stationen");
                    what = Loc.T("Only stations and links fill it (farms, kiln / smelter output).",
                        "Nur Stationen und Links füllen sie (Farmen, Ausgabe von Köhler / Schmelzer).");
                    rows = Row(dump, Value(false)) + Row(ground, Value(false, Loc.T("(without link)", "(ohne Link)")))
                        + Row(output, Value(true))
                        + Row(Loc.T("Stations take from it", "Stationen holen daraus"), Value(true))
                        + Row(Loc.T("Crafting / building take from it", "Craften / Bauen holen daraus"), Value(false))
                        + Row(display, Value(true));
                    extraLine = 1;
                    break;
                default:
                    title = Loc.T("Ignore", "Ignorieren");
                    what = Loc.T("StoreAndCraft does not touch this chest at all.",
                        "StoreAndCraft fasst diese Kiste gar nicht an.");
                    rows = Row(dump, Value(false)) + Row(ground, Value(false)) + Row(output, Value(false))
                        + Row(take, Value(false))
                        + Row(display, Value(false, Loc.T("(allowed with Show on display)", "(erlaubt mit Auf Display zeigen)")));
                    break;
            }

            lines = 7 + extraLine;
            return "<color=#FFD966>" + title + "</color>\n" + what + "\n" + rows;
        }

        private static Sprite Circle()
        {
            if (_circle != null)
                return _circle;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float edge = Mathf.Clamp01(n * 0.5f - d);               // soft outer edge
                    float ring = Mathf.Clamp01(d - (n * 0.5f - 3.5f));      // ring ~3 px wide
                    float a = edge * Mathf.Max(ring, 0.12f);                // faint fill inside
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }
    }

    /// <summary>
    /// Hover check by polling the pointer position (same source Valheim uses for its own hover:
    /// ZInput.pointerPosition). Pointer-enter events did not reach the "?" inside the text dialog.
    /// </summary>
    internal class ModeHelpHover : MonoBehaviour
    {
        public ChestModeHelp.Mode Mode;
        private bool _over;

        private void Update()
        {
            bool over = RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)transform, ZInput.pointerPosition, null);
            if (over == _over)
                return;
            _over = over;
            if (over)
                ChestModeHelp.ShowTip(Mode);
            else
                ChestModeHelp.HideTip();
        }

        private void OnDisable()
        {
            if (_over)
                ChestModeHelp.HideTip();
            _over = false;
        }
    }
}
