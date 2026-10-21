using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// A small number box in an item tile of the display filter menu (top right, beside the icon): the display "Min"
    /// (SAC-MIN, DisplayMinimums) and the F10 "Keep in inventory" list (ItemLists). Shows the value, a left click edits
    /// it in place.
    ///
    /// Each tile only gets a light box (Valheim text field look + one label). The real input field is created when the
    /// box is clicked and removed when editing ends: the item grid is rebuilt on every selection click, and an input
    /// field per tile (60+ tiles) would make that rebuild noticeably slower.
    /// </summary>
    internal static class TileAmountField
    {
        private static readonly Color Gold = new Color(0.925f, 0.77f, 0.29f, 1f);
        private static readonly Color Muted = new Color(0.70f, 0.64f, 0.52f, 0.8f);
        private const string FieldName = "SacMinField";
        private const float FieldW = 42f;
        private const float FieldH = 22f;

        /// <summary>Adds the box to an item tile. <paramref name="changed"/> runs after a new value was saved.</summary>
        public static Field Add(RectTransform tile, string placeholder, System.Func<int> get, System.Action<int> set,
            System.Action<int> changed = null)
        {
            if (tile == null || get == null || set == null)
                return null;
            Transform old = tile.Find(FieldName);
            if (old != null)
                Object.Destroy(old.gameObject);

            var go = new GameObject(FieldName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(tile, false);
            RectTransform rt = go.transform as RectTransform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(FieldW, FieldH);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            Image bg = go.GetComponent<Image>();
            StyleField(bg);
            bg.raycastTarget = true; // clicks on the box do not (de)select the item

            var labelGo = new GameObject("Value", typeof(RectTransform));
            labelGo.transform.SetParent(rt, false);
            RectTransform lrt = labelGo.transform as RectTransform;
            Stretch(lrt, 3f, 1f);
            TextMeshProUGUI label = UiFonts.CreateLabel(labelGo, 14f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Field field = go.AddComponent<Field>();
            field.Placeholder = placeholder;
            field.Get = get;
            field.Set = set;
            field.Changed = changed;
            field.Label = label;
            field.Refresh();
            return field;
        }

        internal static void StyleField(Image img)
        {
            Sprite sprite = SettingsPanel.Vanilla("text_field");
            if (sprite == null)
            {
                img.sprite = null;
                img.color = new Color(0.08f, 0.05f, 0.02f, 0.75f);
                return;
            }
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            img.color = Color.white;
            UiStyle.Lit(img);
        }

        private static Field _editing;
        private static int _closedFrame = -1;

        /// <summary>
        /// Escape in the filter menu: ends an edit first (the field itself already reverted the text). True when an
        /// edit was open or just ended this frame, so the menu stays open.
        /// </summary>
        public static bool CloseEditor()
        {
            if (_editing != null)
            {
                _editing.EndEdit(apply: false);
                return true;
            }
            return _closedFrame == Time.frameCount;
        }

        /// <summary>The box of one tile: shows the value, a left click edits it in place.</summary>
        internal sealed class Field : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
        {
            public string Placeholder;
            public System.Func<int> Get;
            public System.Action<int> Set;
            public System.Action<int> Changed;
            public TextMeshProUGUI Label;
            private TMP_InputField _input;
            private bool _ending;

            public void Refresh()
            {
                if (Label == null)
                    return;
                int value = Get();
                Label.gameObject.SetActive(true);
                if (value > 0)
                {
                    Label.text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Label.color = Gold;
                    Label.fontStyle = FontStyles.Normal;
                }
                else
                {
                    Label.text = Placeholder;
                    Label.color = Muted;
                    Label.fontStyle = FontStyles.Italic;
                }
            }

            // Pressing the box must not press the tile button below it.
            public void OnPointerDown(PointerEventData eventData)
            {
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                    BeginEdit();
            }

            public void BeginEdit()
            {
                if (_input != null)
                    return;
                if (_editing != null && _editing != this)
                    _editing.EndEdit(apply: true);
                _editing = this;

                // Built inactive: TMP_InputField must be fully wired before OnEnable (see DisplayTypeMenu search).
                var go = new GameObject("Input", typeof(RectTransform), typeof(TMP_InputField));
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                RectTransform rt = go.transform as RectTransform;
                Stretch(rt, 0f, 0f);

                var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
                area.transform.SetParent(rt, false);
                RectTransform art = area.transform as RectTransform;
                Stretch(art, 3f, 1f);
                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(art, false);
                Stretch(textGo.transform as RectTransform, 0f, 0f);
                TextMeshProUGUI text = UiFonts.CreateLabel(textGo, 14f);
                text.color = Gold;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;

                TMP_InputField input = go.GetComponent<TMP_InputField>();
                input.textViewport = art;
                input.textComponent = text;
                input.targetGraphic = GetComponent<Image>();
                input.contentType = TMP_InputField.ContentType.IntegerNumber;
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.characterLimit = 6;
                input.caretColor = Gold;
                int value = Get();
                input.SetTextWithoutNotify(value > 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                // Enter or clicking elsewhere saves; Escape reverts the text first, so saving is a no-op then.
                input.onEndEdit.AddListener(_ => EndEdit(apply: true));
                _input = input;

                if (Label != null)
                    Label.gameObject.SetActive(false);
                go.SetActive(true);
                input.ActivateInputField();
                input.Select();
            }

            public void EndEdit(bool apply)
            {
                if (_ending || _input == null)
                    return;
                _ending = true;
                try
                {
                    if (apply)
                    {
                        int amount;
                        string text = (_input.text ?? "").Trim();
                        if (text.Length == 0 || !int.TryParse(text, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out amount) || amount < 0)
                            amount = 0;
                        int before = Get();
                        Set(amount);
                        int after = Get();
                        if (after != before && Changed != null)
                            Changed(after);
                    }
                    Destroy(_input.gameObject);
                    _input = null;
                    if (_editing == this)
                        _editing = null;
                    _closedFrame = Time.frameCount;
                    Refresh();
                }
                finally
                {
                    _ending = false;
                }
            }

            private void OnDestroy()
            {
                // Menu closed / grid rebuilt while editing: keep what was typed.
                if (_input != null)
                    EndEdit(apply: true);
                if (_editing == this)
                    _editing = null;
            }
        }

        private static void Stretch(RectTransform rt, float padX, float padY)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
