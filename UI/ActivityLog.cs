using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Optional transfer log (F10 panel checkbox). Up to 10 lines under MessageHud TopLeft
    /// ("Dir ist kalt" / item pickup text), same TMP font. Preference is saved locally.
    /// New lines come in at the bottom. The top line leaves first: once it has been readable for MinSeconds it fades
    /// out, then the next one moves up and does the same (a conveyor). A full log fades its top line out quickly.
    /// </summary>
    internal static class ActivityLog
    {
        private const int MaxLines = 10;
        /// <summary>Every line stays fully visible at least this long.</summary>
        private const float MinSeconds = 3f;
        private const float FadeSeconds = 0.8f;
        /// <summary>Top line of a full log when a new line comes in.</summary>
        private const float FullFadeSeconds = 0.35f;

        private struct Entry
        {
            public string Text;
            public float Born;
            public float FadeStart; // < 0: not fading
            public float FadeLength;
        }

        private static readonly List<Entry> Lines = new List<Entry>(MaxLines);
        private static readonly StringBuilder Sb = new StringBuilder(256);
        private static bool _visible;
        private static bool _booted;
        private static TextMeshProUGUI _tmp;
        private static RectTransform _rt;

        public static bool Visible
        {
            get { return _visible; }
        }

        public static void LoadFromConfig()
        {
            if (Plugin.Settings?.ActivityLogVisible == null)
                return;
            _visible = Plugin.Settings.ActivityLogVisible.Value;
        }

        public static void Toggle()
        {
            SetVisible(!_visible);
            Player player = Player.m_localPlayer;
            if (player != null)
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    _visible
                        ? Loc.T("Activity log on", "Aktivitäts-Log an")
                        : Loc.T("Activity log off", "Aktivitäts-Log aus"),
                    0, null, false);
            }
        }

        public static void SetVisible(bool visible)
        {
            if (_visible != visible)
            {
                _visible = visible;
                Persist();
            }
            EnsureHost();
            RefreshUi();
        }

        private static void Persist()
        {
            if (Plugin.Settings?.ActivityLogVisible == null)
                return;
            if (Plugin.Settings.ActivityLogVisible.Value == _visible)
                return;
            ConfigWatch.SuppressReload(2f);
            Plugin.Settings.ActivityLogVisible.Value = _visible;
        }

        /// <summary>Chest → station, e.g. "Chest 10x Tin → Smelter".</summary>
        public static void FromChest(string stationLabel, int amount, string itemLabel)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemLabel))
                return;
            Add(Format(ChestWord(), amount, itemLabel, StationWord(stationLabel)));
        }

        /// <summary>Station → chest, e.g. "Smelter 1x Tin → Chest[Tin Ore]" (just "Chest" when the chest is unknown).</summary>
        public static void ToChest(string stationLabel, int amount, string itemLabel, Container chest = null)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemLabel))
                return;
            Add(Format(StationWord(stationLabel), amount, itemLabel, chest != null ? Chest(chest) : ChestWord()));
        }

        /// <summary>Tagged move, e.g. "Dump: Bag 20x Wood → Chest Wood".</summary>
        public static void Move(string action, string from, int amount, string itemShared, string to)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemShared) || Headless)
                return;
            // Logging must never abort the transfer loop that called it.
            try
            {
                Add(action + ": " + Format(from, amount, DisplayFilters.ItemLabel(itemShared), to));
            }
            catch
            {
            }
        }

        public static void Note(string action, string detail)
        {
            if (Headless)
                return;
            Add(string.IsNullOrEmpty(detail) ? action : action + ": " + detail);
        }

        /// <summary>Renamed chest: "Chest[Tin Ore]". Not renamed: the vanilla name ("Reinforced chest"), else "Chest".</summary>
        public static string Chest(Container chest)
        {
            if (Headless)
                return string.Empty;
            try
            {
                if (chest == null)
                    return ChestWord();
                string custom = ChestNames.DisplayName(ChestNames.Get(chest));
                if (!string.IsNullOrEmpty(custom))
                    return ChestWord() + "[" + custom + "]";
                string vanilla = ChestNames.LocalizedVanillaName(chest);
                return string.IsNullOrEmpty(vanilla) ? ChestWord() : vanilla;
            }
            catch
            {
                return ChestWord();
            }
        }

        /// <summary>Dedicated server: no HUD, skip all log work.</summary>
        private static bool Headless
        {
            get { return ZNet.instance != null && ZNet.instance.IsDedicated(); }
        }

        public static string Bag()
        {
            return Loc.T("Bag", "Tasche");
        }

        public static string Ground()
        {
            return Loc.T("Ground", "Boden");
        }

        public static string Used()
        {
            return Loc.T("used", "verbraucht");
        }

        public static void Add(string line)
        {
            if (string.IsNullOrEmpty(line) || Headless)
                return;
            EnsureHost();
            float now = Time.unscaledTime;
            Lines.Add(new Entry { Text = line, Born = now, FadeStart = -1f });
            if (Lines.Count > MaxLines)
            {
                // Full: the top line fades out quickly (one extra line while it fades), an older fading one goes now.
                while (Lines.Count > MaxLines + 1)
                    Lines.RemoveAt(0);
                Entry top = Lines[0];
                // Keep how far it already faded (no jump back to full), only finish faster.
                float done = top.FadeStart >= 0f && top.FadeLength > 0f
                    ? Mathf.Clamp01((now - top.FadeStart) / top.FadeLength) : 0f;
                if (top.FadeStart < 0f || (1f - done) * top.FadeLength > FullFadeSeconds)
                {
                    top.FadeLength = FullFadeSeconds;
                    top.FadeStart = now - done * FullFadeSeconds;
                    Lines[0] = top;
                }
            }
            RefreshUi();
        }

        public static void Tick()
        {
            if (!_booted)
                return;
            EnsureHost();
            if (Lines.Count == 0)
            {
                RefreshUi();
                return;
            }

            float now = Time.unscaledTime;
            bool changed = false;
            // The top line starts to fade once it was readable long enough; the next one follows when it is gone.
            Entry top = Lines[0];
            if (top.FadeStart < 0f && now - top.Born >= MinSeconds)
            {
                top.FadeStart = now;
                top.FadeLength = FadeSeconds;
                Lines[0] = top;
            }
            bool fading = false;
            for (int i = Lines.Count - 1; i >= 0; i--)
            {
                Entry e = Lines[i];
                if (e.FadeStart < 0f)
                    continue;
                if (now - e.FadeStart >= e.FadeLength)
                {
                    Lines.RemoveAt(i);
                    changed = true;
                }
                else
                {
                    fading = true;
                }
            }
            if (changed || fading)
                RefreshUi();
            else
                LayoutUnderMessage();
        }

        /// <summary>0..255 opacity of a line (255 = not fading).</summary>
        private static int AlphaOf(Entry e, float now)
        {
            if (e.FadeStart < 0f || e.FadeLength <= 0f)
                return 255;
            float t = Mathf.Clamp01((now - e.FadeStart) / e.FadeLength);
            return Mathf.RoundToInt((1f - t) * 255f);
        }

        private static string Format(string from, int amount, string item, string to)
        {
            return from + " " + amount + "x " + item + " → " + to;
        }

        private static string ChestWord()
        {
            return Loc.T("Chest", "Kiste");
        }

        private static string StationWord(string stationLabel)
        {
            return string.IsNullOrEmpty(stationLabel) ? "?" : stationLabel;
        }

        private static void EnsureHost()
        {
            if (Plugin.Instance == null)
                return;
            if (!_booted)
            {
                _booted = true;
                Plugin.Instance.gameObject.AddComponent<ActivityLogHud>();
            }

            if (_tmp != null)
                return;

            MessageHud hud = MessageHud.instance;
            if (hud == null || hud.m_messageText == null)
                return;

            TMP_Text template = hud.m_messageText;
            GameObject go = new GameObject("SAC_ActivityLog");
            go.transform.SetParent(template.rectTransform.parent, false);
            go.layer = template.gameObject.layer;

            _tmp = go.AddComponent<TextMeshProUGUI>();
            _tmp.font = template.font;
            _tmp.fontSharedMaterial = template.fontSharedMaterial;
            _tmp.fontSize = template.fontSize;
            _tmp.color = template.color;
            _tmp.alignment = TextAlignmentOptions.TopLeft;
            _tmp.raycastTarget = false;
            _tmp.textWrappingMode = TextWrappingModes.NoWrap;
            _tmp.overflowMode = TextOverflowModes.Overflow;
            // Rich text only for the per-line fade (<alpha>); the line text itself stays plain (<noparse>).
            _tmp.richText = true;

            _rt = _tmp.rectTransform;
            LayoutUnderMessage();
            RefreshUi();
        }

        private static void LayoutUnderMessage()
        {
            if (_rt == null || _tmp == null)
                return;
            MessageHud hud = MessageHud.instance;
            if (hud == null || hud.m_messageText == null)
                return;

            RectTransform msg = hud.m_messageText.rectTransform;
            float font = _tmp.fontSize > 0f ? _tmp.fontSize : 18f;
            float gap = font * 1.25f;
            float width = Mathf.Max(360f, msg.rect.width > 10f ? msg.rect.width : 420f);
            float height = MaxLines * (font + 4f);

            _rt.anchorMin = msg.anchorMin;
            _rt.anchorMax = msg.anchorMax;
            _rt.pivot = new Vector2(msg.pivot.x, 1f);
            _rt.sizeDelta = new Vector2(width, height);
            // Grow downward from just under the TopLeft status / pickup line.
            _rt.anchoredPosition = msg.anchoredPosition + new Vector2(0f, -gap);
        }

        private static void RefreshUi()
        {
            if (_tmp == null)
                return;

            bool show = _visible
                && Lines.Count > 0
                && Player.m_localPlayer != null
                && !Hud.IsUserHidden();
            if (Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()))
                show = false;

            _tmp.gameObject.SetActive(show);
            if (!show)
            {
                _tmp.text = string.Empty;
                return;
            }

            LayoutUnderMessage();
            float now = Time.unscaledTime;
            Sb.Length = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (i > 0)
                    Sb.Append('\n');
                int alpha = AlphaOf(Lines[i], now);
                if (alpha < 255)
                    Sb.Append("<alpha=#").Append(alpha.ToString("X2")).Append('>');
                // Chest names are player text: never read as tags.
                Sb.Append("<noparse>").Append(Lines[i].Text.Replace("</noparse>", "")).Append("</noparse>");
            }
            _tmp.text = Sb.ToString();
        }
    }

    internal sealed class ActivityLogHud : MonoBehaviour
    {
        private void Update()
        {
            ActivityLog.Tick();
        }
    }
}
