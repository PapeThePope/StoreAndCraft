using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// SAC-MIN: a minimum per item and per Storage Display, set in the display filter menu (a small "Min" field in
    /// every item tile). When the display shows less than the minimum, the count turns red (icon-only cells: the icon).
    /// Stored on the display's ZDO as "shared=amount;..." (shared with every player, read by nothing else).
    /// The counts come from the display's normal chest count: nothing is counted extra.
    /// To remove the feature: delete this file and every line marked "SAC-MIN" (StorageDisplayBoard, DisplayTypeMenu).
    /// </summary>
    internal static class DisplayMinimums
    {
        public const string ZdoKey = "SAC_disp_min";
        public static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();
        private const int MaxAmount = 999999;
        private static readonly Color AmountRed = new Color(1f, 0.3f, 0.25f, 1f);
        private static readonly Color IconRed = new Color(1f, 0.4f, 0.4f, 1f);
        private static readonly Color Gold = new Color(0.925f, 0.77f, 0.29f, 1f);
        private static readonly Color Muted = new Color(0.70f, 0.64f, 0.52f, 0.8f);
        private const string FieldName = "SacMinField";

        // ---- read (display paint): parsed once per display ZDO revision ----

        private sealed class Cached
        {
            public uint Revision;
            public Dictionary<string, int> Map; // null = no minimum on this display
        }

        private static readonly Dictionary<ZDOID, Cached> Cache = new Dictionary<ZDOID, Cached>();

        private static Dictionary<string, int> MapFor(ZDO zdo)
        {
            if (zdo == null)
                return null;
            Cached c;
            if (Cache.TryGetValue(zdo.m_uid, out c) && c.Revision == zdo.DataRevision)
                return c.Map;
            if (c == null)
            {
                if (Cache.Count > 1024)
                    Cache.Clear();
                c = new Cached();
                Cache[zdo.m_uid] = c;
            }
            c.Revision = zdo.DataRevision;
            c.Map = Parse(zdo.GetString(ZdoKeyHash, ""));
            return c.Map;
        }

        /// <summary>True when this display has a minimum for the item and shows less than it.</summary>
        public static bool Below(ZNetView view, string shared, int count)
        {
            if (view == null || string.IsNullOrEmpty(shared) || !view.IsValid())
                return false;
            Dictionary<string, int> map = MapFor(view.GetZDO());
            int min;
            return map != null && map.TryGetValue(shared, out min) && count < min;
        }

        /// <summary>Colors a painted display cell red (count, or the icon when the cell shows no count).</summary>
        public static void MarkRed(TextMeshProUGUI amount, Image icon, bool countShown)
        {
            if (countShown && amount != null)
            {
                amount.color = AmountRed;
                amount.faceColor = AmountRed;
            }
            else if (icon != null)
            {
                icon.color = IconRed;
            }
        }

        private static Dictionary<string, int> Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            Dictionary<string, int> map = null;
            string[] parts = raw.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].LastIndexOf('=');
                if (eq <= 0)
                    continue;
                int amount;
                if (!int.TryParse(parts[i].Substring(eq + 1), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out amount) || amount <= 0)
                    continue;
                if (map == null)
                    map = new Dictionary<string, int>(System.StringComparer.Ordinal);
                map[parts[i].Substring(0, eq)] = amount;
            }
            return map;
        }

        private static string Encode(Dictionary<string, int> map)
        {
            if (map == null || map.Count == 0)
                return "";
            var keys = new List<string>(map.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(keys[i]).Append('=').Append(map[keys[i]].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Shared item name used as the key (the menu tiles use item tokens, the display shared names).</summary>
        private static string KeyOf(string token)
        {
            string shared = ItemIds.SharedFromToken(token);
            if (string.IsNullOrEmpty(shared) || shared.IndexOf('=') >= 0 || shared.IndexOf(';') >= 0)
                return null;
            return shared;
        }

        public static int Get(StorageDisplayBoard board, string token)
        {
            string key = KeyOf(token);
            ZNetView nv = board != null ? board.GetComponent<ZNetView>() : null;
            if (key == null || nv == null || !nv.IsValid())
                return 0;
            Dictionary<string, int> map = MapFor(nv.GetZDO());
            int min;
            return map != null && map.TryGetValue(key, out min) ? min : 0;
        }

        private static void Set(StorageDisplayBoard board, string token, int amount)
        {
            string key = KeyOf(token);
            ZNetView nv = board != null ? board.GetComponent<ZNetView>() : null;
            if (key == null || nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            amount = Mathf.Clamp(amount, 0, MaxAmount);
            if (amount == Get(board, token))
                return; // unchanged: no ZDO write
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            ZDO zdo = nv.GetZDO();
            Dictionary<string, int> current = MapFor(zdo);
            var map = current != null
                ? new Dictionary<string, int>(current, System.StringComparer.Ordinal)
                : new Dictionary<string, int>(System.StringComparer.Ordinal);
            if (amount > 0)
                map[key] = amount;
            else
                map.Remove(key);
            zdo.Set(ZdoKeyHash, Encode(map));
            board.RepaintSoon();
        }

        // ---- filter menu: a "Min" field in every item tile ----
        //
        // Each tile only gets a light box (Valheim text field look + one label). The real input field is created when
        // the box is clicked and removed when editing ends: the item grid is rebuilt on every selection click, and an
        // input field per tile (60+ tiles) would make that rebuild noticeably slower.

        private const float FieldW = 42f;
        private const float FieldH = 22f;

        /// <summary>Adds the "Min" box to an item tile (top right, beside the icon).</summary>
        public static void AddTileField(RectTransform tile, StorageDisplayBoard board, string token)
        {
            if (tile == null || board == null || KeyOf(token) == null)
                return;
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

            MinField field = go.AddComponent<MinField>();
            field.Board = board;
            field.Token = token;
            field.Label = label;
            field.Refresh();
        }

        private static void StyleField(Image img)
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

        private static MinField _editing;
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

        /// <summary>The "Min" box of one tile: shows the value, a left click edits it in place.</summary>
        private sealed class MinField : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
        {
            public StorageDisplayBoard Board;
            public string Token;
            public TextMeshProUGUI Label;
            private TMP_InputField _input;
            private bool _ending;

            public void Refresh()
            {
                if (Label == null)
                    return;
                int min = Get(Board, Token);
                Label.gameObject.SetActive(true);
                if (min > 0)
                {
                    Label.text = min.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Label.color = Gold;
                    Label.fontStyle = FontStyles.Normal;
                }
                else
                {
                    Label.text = Loc.T("Min", "Min");
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
                if (eventData.button == PointerEventData.InputButton.Left && _input == null)
                    BeginEdit();
            }

            private void BeginEdit()
            {
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
                int min = Get(Board, Token);
                input.SetTextWithoutNotify(min > 0 ? min.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
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
                        Set(Board, Token, amount);
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
