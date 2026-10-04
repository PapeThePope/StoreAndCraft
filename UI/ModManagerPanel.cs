using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// F10 mod manager. F10 first shows every loaded BepInEx mod that has settings; a click opens that mod.
    /// StoreAndCraft opens its own panel (SettingsPanel, with the back arrow); every other mod gets a page built
    /// from its ConfigFile (switch, slider, number, list, hotkey, text), changed values are written on Save.
    /// Everyone may change the config files of their own PC (client settings); what a server owns stays the
    /// server's. Same look as the F10 panel (its helpers are reused).
    /// </summary>
    internal static class ModManagerPanel
    {
        private const float RefW = 1920f;
        private const float RefH = 1080f;
        private const float PanelW = 1040f;
        private const float PanelH = 960f;
        private const float WidgetW = 380f;

        private enum State { Closed, List, Page, Sac }

        private sealed class ModInfo
        {
            public string Name;
            public string Version;
            public string Guid;
            public ConfigFile Config;
            public readonly List<ConfigEntryBase> Entries = new List<ConfigEntryBase>();
            public bool IsSac;
            /// <summary>Morda's own mods (GUID com.morda.* or installed from a Morda- Thunderstore folder): listed first.</summary>
            public bool IsMine;
        }

        private static State _state = State.Closed;
        private static GameObject _root;
        private static RectTransform _content;
        private static TextMeshProUGUI _status;
        private static Button _save;
        private static List<ModInfo> _mods;
        private static ModInfo _page;
        private static int _closeAtFrame = -1;
        private static bool _listAfterSac;

        // pending edits of the open page (written on Save)
        private static readonly Dictionary<ConfigEntryBase, object> Pending = new Dictionary<ConfigEntryBase, object>();

        // hotkey capture
        private static ConfigEntryBase _capEntry;
        private static TextMeshProUGUI _capText;
        private static float _capStarted;

        internal static bool IsOpen
        {
            get { return _state != State.Closed; }
        }

        internal static bool Capturing
        {
            get { return _capEntry != null; }
        }

        // ---------------------------------------------------------------- open / close

        internal static void Toggle()
        {
            if (SettingsPanel.IsOpen)
            {
                SettingsPanel.Toggle(); // F10 inside the StoreAndCraft page closes everything, like before
                return;
            }
            if (_state != State.Closed)
            {
                RequestClose();
                return;
            }
            OpenList();
        }

        /// <summary>The back arrow of the StoreAndCraft page: show the list when that page is closed.</summary>
        internal static void RequestListAfterSac()
        {
            _listAfterSac = true;
        }

        private static void OpenList()
        {
            if (Plugin.Settings == null || Player.m_localPlayer == null)
                return;
            _mods = LoadMods();
            _state = State.List;
            _page = null;
            Pending.Clear();
            _closeAtFrame = -1;
            UiFonts.ThinNorse();
            Rebuild();
        }

        private static void RequestClose()
        {
            // One frame later, so the same Escape press cannot open the pause menu.
            if (_closeAtFrame < 0)
                _closeAtFrame = Time.frameCount + 1;
        }

        private static void Close()
        {
            _state = State.Closed;
            _closeAtFrame = -1;
            _listAfterSac = false;
            _page = null;
            Pending.Clear();
            EndCapture();
            DestroyRoot();
        }

        private static void DestroyRoot()
        {
            _content = null;
            _status = null;
            _save = null;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        internal static void Tick()
        {
            if (_state == State.Closed)
                return;
            if (Player.m_localPlayer == null || Plugin.Settings == null)
            {
                Close();
                return;
            }

            if (_state == State.Sac)
            {
                // The StoreAndCraft page is open (or has just closed): back arrow -> list, anything else -> done.
                if (!SettingsPanel.IsOpen)
                {
                    if (_listAfterSac)
                    {
                        _listAfterSac = false;
                        OpenList();
                    }
                    else
                    {
                        Close();
                    }
                }
                return;
            }

            if (_closeAtFrame >= 0)
            {
                if (Time.frameCount >= _closeAtFrame)
                    Close();
                return;
            }

            if (_capEntry != null)
            {
                HandleCapture();
                return;
            }

            if (ZInput.GetKeyDown(KeyCode.Escape, true) || KeyUtil.Down(Plugin.Settings.ActivityLogKey.Value))
                RequestClose();
        }

        // ---------------------------------------------------------------- data

        private static List<ModInfo> LoadMods()
        {
            var list = new List<ModInfo>();
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                BaseUnityPlugin plugin = info != null ? info.Instance : null;
                if (plugin == null)
                    continue;
                ConfigFile cfg;
                try
                {
                    cfg = plugin.Config;
                }
                catch
                {
                    continue;
                }
                if (cfg == null)
                    continue;

                var mod = new ModInfo
                {
                    Name = info.Metadata.Name,
                    Version = info.Metadata.Version != null ? info.Metadata.Version.ToString() : "",
                    Guid = info.Metadata.GUID,
                    Config = cfg,
                    IsSac = info.Metadata.GUID == Plugin.ModGuid,
                    IsMine = IsMordaMod(info)
                };
                try
                {
                    foreach (ConfigDefinition def in cfg.Keys)
                    {
                        ConfigEntryBase entry = cfg[def];
                        if (entry != null && TagBool(entry, "Browsable", true))
                            mod.Entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Mod manager: cannot read the settings of " + mod.Name + ": " + ex.Message);
                    continue;
                }
                if (mod.Entries.Count > 0 || mod.IsSac)
                    list.Add(mod);
            }
            list.Sort((a, b) =>
            {
                if (a.IsSac != b.IsSac)
                    return a.IsSac ? -1 : 1; // StoreAndCraft first
                if (a.IsMine != b.IsMine)
                    return a.IsMine ? -1 : 1; // then the other Morda mods
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static bool IsMordaMod(PluginInfo info)
        {
            string guid = info.Metadata.GUID ?? "";
            if (guid.StartsWith("com.morda.", StringComparison.OrdinalIgnoreCase))
                return true;
            // Thunderstore installs into BepInEx/plugins/<Author>-<Mod>/
            string path = info.Location ?? "";
            string[] parts = path.Split(new[] { '/', (char)92 }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("Morda-", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>ConfigurationManagerAttributes tag (a class the mods copy into their own namespace): read by name.</summary>
        private static object Tag(ConfigEntryBase entry, string field)
        {
            if (entry.Description == null || entry.Description.Tags == null)
                return null;
            foreach (object tag in entry.Description.Tags)
            {
                if (tag == null)
                    continue;
                System.Reflection.FieldInfo f = tag.GetType().GetField(field);
                if (f != null)
                    return f.GetValue(tag);
            }
            return null;
        }

        private static bool TagBool(ConfigEntryBase entry, string field, bool fallback)
        {
            object v = Tag(entry, field);
            return v is bool ? (bool)v : fallback;
        }

        // ---------------------------------------------------------------- UI

        private static void Rebuild()
        {
            DestroyRoot();
            _root = new GameObject("SAC_ModManager", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.SetActive(false); // wire sliders / inputs fully before OnEnable
            UnityEngine.Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform dim = SettingsPanel.NewUi("Dim", _root.transform, typeof(Image));
            SettingsPanel.Stretch(dim);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            RectTransform panel = SettingsPanel.NewUi("Panel", _root.transform, typeof(Image));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelW, PanelH);
            panel.anchoredPosition = Vector2.zero;
            SettingsPanel.SetSprite(panel.GetComponent<Image>(), SettingsPanel.Vanilla("woodpanel_settings") ?? UiAssets.PanelLeft,
                new Color(0.16f, 0.12f, 0.08f, 0.97f));

            bool page = _state == State.Page && _page != null;
            TextMeshProUGUI title = SettingsPanel.Label(panel, "Title", 36f, true);
            title.text = page ? _page.Name : Loc.T("Mods", "Mods");
            title.color = SettingsPanel.Gold;
            title.alignment = TextAlignmentOptions.Center;
            SettingsPanel.Place(title.rectTransform, 0f, 1f, 1f, 1f, 120f, -86f, -120f, -30f);

            RectTransform scroll = SettingsPanel.NewUi("Scroll", panel, typeof(Image), typeof(ScrollRect));
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(48f, 150f);
            scroll.offsetMax = new Vector2(-48f, -96f);
            scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            RectTransform viewport = SettingsPanel.NewUi("Viewport", scroll, typeof(Image), typeof(RectMask2D));
            SettingsPanel.Stretch(viewport);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            RectTransform content = SettingsPanel.NewUi("Content", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 18);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _content = content;

            ScrollRect sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 360f;
            SettingsPanel.AddScrollbar(sr, scroll);

            if (page)
            {
                BuildPage();
                Button back = SettingsPanel.MakeButton(panel, "Back", "<", () => { Pending.Clear(); EndCapture(); _state = State.List; _page = null; Rebuild(); });
                SettingsPanel.Place(back.transform as RectTransform, 0f, 1f, 0f, 1f, 40f, -86f, 100f, -30f);
                _save = SettingsPanel.MakeButton(panel, "Save", Loc.T("Save", "Speichern"), Save);
                SettingsPanel.Place(_save.transform as RectTransform, 0.5f, 0f, 0.5f, 0f, -230f, 78f, -20f, 124f);
                _save.interactable = false;
            }
            else
            {
                BuildList();
            }

            Button close = SettingsPanel.MakeButton(panel, "Close", Loc.T("Close", "Schließen"), RequestClose);
            if (page)
                SettingsPanel.Place(close.transform as RectTransform, 0.5f, 0f, 0.5f, 0f, 20f, 78f, 230f, 124f);
            else
                SettingsPanel.Place(close.transform as RectTransform, 0.5f, 0f, 0.5f, 0f, -105f, 78f, 105f, 124f);

            _status = SettingsPanel.Label(panel, "Status", 17f, false);
            _status.color = SettingsPanel.Warn;
            _status.alignment = TextAlignmentOptions.Center;
            _status.text = "";
            SettingsPanel.Place(_status.rectTransform, 0f, 0f, 1f, 0f, 48f, 44f, -48f, 74f);

            _root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static void SetStatus(string text)
        {
            if (_status != null)
                _status.text = text;
        }

        private static void BuildList()
        {
            int hidden = 0;
            AddText(Loc.T("Choose a mod to change its settings.", "Wähle eine Mod, um ihre Einstellungen zu ändern."),
                SettingsPanel.Muted, 17f);
            bool anyMine = false;
            bool anyOther = false;
            for (int i = 0; i < _mods.Count; i++)
            {
                if (_mods[i].Entries.Count == 0 && !_mods[i].IsSac)
                    continue;
                if (_mods[i].IsMine || _mods[i].IsSac)
                    anyMine = true;
                else
                    anyOther = true;
            }
            bool headers = anyMine && anyOther;
            bool wroteMine = false;
            bool wroteOther = false;
            for (int i = 0; i < _mods.Count; i++)
            {
                ModInfo mod = _mods[i];
                if (mod.Entries.Count == 0 && !mod.IsSac)
                {
                    hidden++;
                    continue;
                }
                bool mine = mod.IsMine || mod.IsSac;
                if (headers && mine && !wroteMine)
                {
                    wroteMine = true;
                    AddHeader(Loc.T("Mods by Morda", "Mods von Morda"));
                }
                else if (headers && !mine && !wroteOther)
                {
                    wroteOther = true;
                    AddHeader(Loc.T("Other mods", "Andere Mods"));
                }
                AddModRow(mod);
            }
            if (hidden > 0)
                AddText(hidden + " " + Loc.T("mods have no settings.", "Mods haben keine Einstellungen."), SettingsPanel.Muted, 15f);
        }

        private static void AddModRow(ModInfo mod)
        {
            ModInfo captured = mod;
            Button btn = SettingsPanel.MakeButton(_content, "Mod_" + mod.Guid, "", () => OpenMod(captured));
            SettingsPanel.AddLayout(btn.gameObject, 52f);
            TextMeshProUGUI text = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = true;
            text.text = "  " + mod.Name + (mod.IsSac ? "  <size=70%><color=#c9b27a>" + Loc.T("(this mod)", "(diese Mod)") + "</color></size>" : "");
            SettingsPanel.Place(text.rectTransform, 0f, 0f, 1f, 1f, 18f, 0f, -230f, 0f);

            TextMeshProUGUI info = SettingsPanel.Label(btn.transform as RectTransform, "Info", 16f, false);
            info.alignment = TextAlignmentOptions.MidlineRight;
            info.color = SettingsPanel.Muted;
            info.text = (mod.IsSac ? "" : mod.Entries.Count + " " + Loc.T("settings", "Einstellungen") + "   ") + "v" + mod.Version;
            SettingsPanel.Place(info.rectTransform, 1f, 0f, 1f, 1f, -230f, 0f, -16f, 0f);
        }

        private static void OpenMod(ModInfo mod)
        {
            if (mod == null)
                return;
            if (mod.IsSac)
            {
                // The existing panel; its back arrow comes back here.
                _state = State.Sac;
                DestroyRoot();
                SettingsPanel.OpenFromManager();
                if (!SettingsPanel.IsOpen)
                    OpenList();
                return;
            }
            _state = State.Page;
            _page = mod;
            Pending.Clear();
            Rebuild();
        }

        // ---------------------------------------------------------------- page

        private static void BuildPage()
        {
            ModInfo mod = _page;
            AddText(Loc.T(
                "Changes go into this mod's config file after Save. Some mods need a restart. On a server, mods that sync their settings use the server's values.",
                "Änderungen kommen nach Speichern in die Config-Datei dieser Mod. Manche Mods brauchen einen Neustart. Auf einem Server gelten bei Mods mit Synchronisierung die Werte des Servers."),
                SettingsPanel.Muted, 15f);

            string lastSection = null;
            for (int i = 0; i < mod.Entries.Count; i++)
            {
                ConfigEntryBase entry = mod.Entries[i];
                string section = (Tag(entry, "Category") as string) ?? entry.Definition.Section;
                if (section != lastSection)
                {
                    lastSection = section;
                    AddHeader(section);
                }
                try
                {
                    AddEntryRow(entry);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Mod manager: cannot show " + mod.Name + " / " + entry.Definition.Key + ": " + ex.Message);
                }
            }
        }

        private static object Current(ConfigEntryBase entry)
        {
            object v;
            return Pending.TryGetValue(entry, out v) ? v : entry.BoxedValue;
        }

        private static void SetPending(ConfigEntryBase entry, object value)
        {
            if (Equals(value, entry.BoxedValue))
                Pending.Remove(entry);
            else
                Pending[entry] = value;
            if (_save != null)
                _save.interactable = Pending.Count > 0;
            SetStatus(Pending.Count > 0 ? Loc.T("Unsaved changes.", "Ungespeicherte Änderungen.") : "");
        }

        private static void Save()
        {
            if (Pending.Count == 0)
                return;
            var files = new HashSet<ConfigFile>();
            int done = 0;
            foreach (KeyValuePair<ConfigEntryBase, object> kv in new List<KeyValuePair<ConfigEntryBase, object>>(Pending))
            {
                try
                {
                    kv.Key.BoxedValue = kv.Value;
                    if (_page != null)
                        files.Add(_page.Config);
                    done++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Mod manager: cannot set " + kv.Key.Definition.Key + ": " + ex.Message);
                }
            }
            foreach (ConfigFile f in files)
            {
                try
                {
                    f.Save();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Mod manager: cannot save a config file: " + ex.Message);
                }
            }
            Pending.Clear();
            if (_save != null)
                _save.interactable = false;
            SetStatus(Loc.T("Saved", "Gespeichert") + " (" + done + ")");
        }

        private static void AddHeader(string text)
        {
            RectTransform go = SettingsPanel.NewUi("Header", _content);
            TextMeshProUGUI tmp = UiFonts.CreateBoldLabel(go.gameObject, 22f);
            tmp.text = text;
            tmp.color = SettingsPanel.Gold;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            SettingsPanel.AddLayout(go.gameObject, 40f);
        }

        private static void AddText(string text, Color color, float size)
        {
            RectTransform go = SettingsPanel.NewUi("Text", _content);
            TextMeshProUGUI tmp = UiFonts.CreateLabel(go.gameObject, size);
            tmp.text = text;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
        }

        private static bool IsNumeric(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long)
                || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ushort) || t == typeof(sbyte);
        }

        private static bool IsWhole(Type t)
        {
            return t != typeof(float) && t != typeof(double);
        }

        private static bool TryRange(ConfigEntryBase entry, out double min, out double max)
        {
            min = max = 0;
            AcceptableValueBase av = entry.Description != null ? entry.Description.AcceptableValues : null;
            if (av == null)
                return false;
            System.Reflection.PropertyInfo pMin = av.GetType().GetProperty("MinValue");
            System.Reflection.PropertyInfo pMax = av.GetType().GetProperty("MaxValue");
            if (pMin == null || pMax == null)
                return false;
            try
            {
                min = Convert.ToDouble(pMin.GetValue(av, null), CultureInfo.InvariantCulture);
                max = Convert.ToDouble(pMax.GetValue(av, null), CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
            return max > min;
        }

        private static object[] Choices(ConfigEntryBase entry)
        {
            Type t = entry.SettingType;
            AcceptableValueBase av = entry.Description != null ? entry.Description.AcceptableValues : null;
            if (av != null)
            {
                System.Reflection.PropertyInfo p = av.GetType().GetProperty("AcceptableValues");
                Array arr = p != null ? p.GetValue(av, null) as Array : null;
                if (arr != null && arr.Length > 0)
                {
                    var list = new List<object>();
                    foreach (object o in arr)
                        list.Add(o);
                    return list.ToArray();
                }
            }
            if (t.IsEnum && t.GetCustomAttributes(typeof(FlagsAttribute), false).Length == 0)
            {
                var list = new List<object>();
                foreach (object o in Enum.GetValues(t))
                    list.Add(o);
                return list.ToArray();
            }
            return null;
        }

        private static string ValueText(ConfigEntryBase entry, object value)
        {
            if (value == null)
                return "";
            Type t = entry.SettingType;
            if (t == typeof(string))
                return (string)value;
            if (t.IsEnum)
                return value.ToString();
            if (value is IFormattable && IsNumeric(t))
                return ((IFormattable)value).ToString(IsWhole(t) ? "0" : "0.###", CultureInfo.InvariantCulture);
            try
            {
                if (TomlTypeConverter.CanConvert(t))
                    return TomlTypeConverter.ConvertToString(value, t);
            }
            catch
            {
            }
            return value.ToString();
        }

        private static void AddEntryRow(ConfigEntryBase entry)
        {
            Type t = entry.SettingType;
            string name = (Tag(entry, "DispName") as string) ?? entry.Definition.Key;
            string desc = entry.Description != null ? entry.Description.Description : null;
            bool readOnly = TagBool(entry, "ReadOnly", false);
            bool hasDesc = !string.IsNullOrEmpty(desc);
            float h = hasDesc ? 70f : 46f;

            RectTransform row = SettingsPanel.NewUi("Row", _content);
            SettingsPanel.AddLayout(row.gameObject, h);

            TextMeshProUGUI label = SettingsPanel.Label(row, "Label", 18f, false);
            label.text = name;
            label.color = readOnly ? SettingsPanel.Muted : SettingsPanel.TextColor;
            SettingsPanel.Place(label.rectTransform, 0f, 1f, 1f, 1f, 4f, -30f, -WidgetW - 12f, -4f);

            if (hasDesc)
            {
                TextMeshProUGUI d = SettingsPanel.Label(row, "Desc", 14f, false);
                d.textWrappingMode = TextWrappingModes.Normal;
                d.overflowMode = TextOverflowModes.Ellipsis;
                d.alignment = TextAlignmentOptions.TopLeft;
                d.color = SettingsPanel.Muted;
                d.text = desc.Replace(System.Environment.NewLine, " ");
                SettingsPanel.Place(d.rectTransform, 0f, 1f, 1f, 1f, 4f, -h + 4f, -WidgetW - 12f, -30f);
            }

            RectTransform w = SettingsPanel.NewUi("Widget", row);
            SettingsPanel.Place(w, 1f, 1f, 1f, 1f, -WidgetW - 4f, -42f, -4f, -4f);

            double min, max;
            object[] choices = Choices(entry);

            if (t == typeof(bool))
            {
                RectTransform tg = SettingsPanel.AddToggle(w, "", (bool)Current(entry), !readOnly, on => SetPending(entry, on));
                SettingsPanel.Place(tg, 1f, 0.5f, 1f, 0.5f, -40f, -19f, -4f, 19f);
            }
            else if (t == typeof(KeyboardShortcut))
            {
                var ks = (KeyboardShortcut)Current(entry);
                Button b = SettingsPanel.MakeButton(w, "Key", "", () => { });
                SettingsPanel.Stretch(b.transform as RectTransform);
                TextMeshProUGUI text = b.GetComponentInChildren<TextMeshProUGUI>(true);
                text.text = KeyText(ks);
                b.interactable = !readOnly;
                b.onClick.AddListener(() => StartCapture(entry, text));
            }
            else if (choices != null)
            {
                int index = Math.Max(0, IndexOfChoice(choices, Current(entry)));
                Button b = SettingsPanel.MakeButton(w, "Choice", "", () => { });
                SettingsPanel.Stretch(b.transform as RectTransform);
                TextMeshProUGUI text = b.GetComponentInChildren<TextMeshProUGUI>(true);
                text.text = ValueText(entry, choices[index]);
                b.interactable = !readOnly;
                b.onClick.AddListener(() =>
                {
                    index = (index + 1) % choices.Length;
                    text.text = ValueText(entry, choices[index]);
                    SetPending(entry, choices[index]);
                });
            }
            else if (IsNumeric(t))
            {
                bool hasRange = TryRange(entry, out min, out max);
                BuildNumber(w, entry, hasRange, min, max, readOnly);
            }
            else if (t == typeof(string) || TomlTypeConverter.CanConvert(t))
            {
                BuildText(w, entry, readOnly);
            }
            else
            {
                TextMeshProUGUI na = SettingsPanel.Label(w, "NA", 16f, false);
                na.text = Loc.T("(not editable here)", "(hier nicht änderbar)");
                na.color = SettingsPanel.Muted;
                na.alignment = TextAlignmentOptions.MidlineRight;
                SettingsPanel.Stretch(na.rectTransform);
            }
        }

        private static int IndexOfChoice(object[] choices, object value)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (Equals(choices[i], value))
                    return i;
            }
            return -1;
        }

        private static object ConvertNumber(Type t, double v)
        {
            if (IsWhole(t))
                v = Math.Round(v);
            return Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
        }

        private static void BuildNumber(RectTransform w, ConfigEntryBase entry, bool hasRange, double min, double max, bool readOnly)
        {
            Type t = entry.SettingType;
            bool whole = IsWhole(t);
            bool syncing = false;
            double current = Convert.ToDouble(Current(entry), CultureInfo.InvariantCulture);

            Slider slider = null;
            TMP_InputField input = null;
            float inputW = hasRange ? 110f : WidgetW;

            if (hasRange)
            {
                RectTransform sliderRt = SettingsPanel.NewUi("Slider", w, typeof(Slider));
                SettingsPanel.Place(sliderRt, 0f, 0f, 1f, 1f, 0f, 6f, -inputW - 12f, -6f);
                RectTransform bg = SettingsPanel.NewUi("Background", sliderRt, typeof(Image));
                SettingsPanel.Stretch(bg);
                SettingsPanel.SetSprite(bg.GetComponent<Image>(), SettingsPanel.Vanilla("text_field"), new Color(0f, 0f, 0f, 0.55f));
                RectTransform fillArea = SettingsPanel.NewUi("Fill Area", sliderRt);
                SettingsPanel.Place(fillArea, 0f, 0f, 1f, 1f, 4f, 4f, -4f, -4f);
                RectTransform fill = SettingsPanel.NewUi("Fill", fillArea, typeof(Image));
                SettingsPanel.Stretch(fill);
                Image fillImg = fill.GetComponent<Image>();
                fillImg.color = new Color(0.85f, 0.55f, 0.18f, 0.85f);
                fillImg.raycastTarget = false;
                RectTransform handleArea = SettingsPanel.NewUi("Handle Slide Area", sliderRt);
                SettingsPanel.Place(handleArea, 0f, 0f, 1f, 1f, 8f, 0f, -8f, 0f);
                RectTransform handle = SettingsPanel.NewUi("Handle", handleArea, typeof(Image));
                handle.anchorMin = new Vector2(0f, 0f);
                handle.anchorMax = new Vector2(0f, 1f);
                handle.offsetMin = new Vector2(-9f, -4f);
                handle.offsetMax = new Vector2(9f, 4f);
                SettingsPanel.SetSprite(handle.GetComponent<Image>(), SettingsPanel.Vanilla("button_small") ?? SettingsPanel.Vanilla("button"), SettingsPanel.Gold);

                slider = sliderRt.GetComponent<Slider>();
                slider.fillRect = fill;
                slider.handleRect = handle;
                slider.targetGraphic = handle.GetComponent<Image>();
                slider.direction = Slider.Direction.LeftToRight;
                slider.minValue = (float)min;
                slider.maxValue = (float)max;
                slider.wholeNumbers = whole && max - min <= 1000.0;
                slider.SetValueWithoutNotify((float)Math.Max(min, Math.Min(max, current)));
                slider.interactable = !readOnly;
            }

            RectTransform fieldRt = SettingsPanel.NewUi("Field", w, typeof(Image), typeof(TMP_InputField));
            if (hasRange)
                SettingsPanel.Place(fieldRt, 1f, 0f, 1f, 1f, -inputW, 2f, 0f, -2f);
            else
                SettingsPanel.Stretch(fieldRt);
            SettingsPanel.SetSprite(fieldRt.GetComponent<Image>(), SettingsPanel.Vanilla("text_field"), new Color(0f, 0f, 0f, 0.6f));
            RectTransform area = SettingsPanel.NewUi("Text Area", fieldRt, typeof(RectMask2D));
            SettingsPanel.Place(area, 0f, 0f, 1f, 1f, 8f, 2f, -8f, -2f);
            RectTransform textGo = SettingsPanel.NewUi("Text", area);
            SettingsPanel.Stretch(textGo);
            TextMeshProUGUI fieldText = UiFonts.CreateLabel(textGo.gameObject, 18f);
            fieldText.color = SettingsPanel.TextColor;
            fieldText.alignment = TextAlignmentOptions.MidlineRight;
            fieldText.textWrappingMode = TextWrappingModes.NoWrap;

            input = fieldRt.GetComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = fieldText;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 20;
            input.targetGraphic = fieldRt.GetComponent<Image>();
            input.SetTextWithoutNotify(ValueText(entry, Current(entry)));
            input.interactable = !readOnly;

            if (slider != null)
            {
                slider.onValueChanged.AddListener(v =>
                {
                    if (syncing)
                        return;
                    syncing = true;
                    object value = ConvertNumber(t, v);
                    SetPending(entry, value);
                    input.SetTextWithoutNotify(ValueText(entry, value));
                    syncing = false;
                });
            }

            input.onEndEdit.AddListener(text =>
            {
                if (syncing)
                    return;
                syncing = true;
                double parsed;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                {
                    if (hasRange)
                        parsed = Math.Max(min, Math.Min(max, parsed));
                    object value;
                    try
                    {
                        value = ConvertNumber(t, parsed);
                        SetPending(entry, value);
                    }
                    catch
                    {
                        value = Current(entry);
                    }
                    input.SetTextWithoutNotify(ValueText(entry, Current(entry)));
                    if (slider != null)
                        slider.SetValueWithoutNotify((float)Math.Max(min, Math.Min(max, Convert.ToDouble(Current(entry), CultureInfo.InvariantCulture))));
                }
                else
                {
                    input.SetTextWithoutNotify(ValueText(entry, Current(entry)));
                }
                syncing = false;
            });
        }

        private static void BuildText(RectTransform w, ConfigEntryBase entry, bool readOnly)
        {
            Type t = entry.SettingType;
            RectTransform fieldRt = SettingsPanel.NewUi("Field", w, typeof(Image), typeof(TMP_InputField));
            SettingsPanel.Stretch(fieldRt);
            SettingsPanel.SetSprite(fieldRt.GetComponent<Image>(), SettingsPanel.Vanilla("text_field"), new Color(0f, 0f, 0f, 0.6f));
            RectTransform area = SettingsPanel.NewUi("Text Area", fieldRt, typeof(RectMask2D));
            SettingsPanel.Place(area, 0f, 0f, 1f, 1f, 8f, 2f, -8f, -2f);
            RectTransform textGo = SettingsPanel.NewUi("Text", area);
            SettingsPanel.Stretch(textGo);
            TextMeshProUGUI fieldText = UiFonts.CreateLabel(textGo.gameObject, 18f);
            fieldText.color = SettingsPanel.TextColor;
            fieldText.alignment = TextAlignmentOptions.MidlineLeft;
            fieldText.textWrappingMode = TextWrappingModes.NoWrap;

            TMP_InputField input = fieldRt.GetComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = fieldText;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 400;
            input.targetGraphic = fieldRt.GetComponent<Image>();
            input.SetTextWithoutNotify(ValueText(entry, Current(entry)));
            input.interactable = !readOnly;

            input.onEndEdit.AddListener(text =>
            {
                if (t == typeof(string))
                {
                    SetPending(entry, text);
                    return;
                }
                try
                {
                    SetPending(entry, TomlTypeConverter.ConvertToValue(text, t));
                }
                catch
                {
                    SetStatus(Loc.T("This value is not valid.", "Dieser Wert ist nicht gültig."));
                    input.SetTextWithoutNotify(ValueText(entry, Current(entry)));
                }
            });
        }

        // ---------------------------------------------------------------- hotkeys

        private static string KeyText(KeyboardShortcut ks)
        {
            string s = KeyUtil.Format(ks);
            return string.IsNullOrEmpty(s) ? Loc.T("(none)", "(keine)") : s;
        }

        private static void StartCapture(ConfigEntryBase entry, TextMeshProUGUI text)
        {
            EndCapture();
            _capEntry = entry;
            _capText = text;
            _capStarted = Time.unscaledTime;
            text.text = Loc.T("press a key...", "Taste drücken...");
            SetStatus(Loc.T("Press the new key (Esc = cancel, Backspace = unbind).", "Neue Taste drücken (Esc = abbrechen, Rücktaste = entfernen)."));
        }

        private static void EndCapture()
        {
            if (_capEntry != null && _capText != null)
                _capText.text = KeyText((KeyboardShortcut)Current(_capEntry));
            _capEntry = null;
            _capText = null;
        }

        private static void HandleCapture()
        {
            if (Time.unscaledTime - _capStarted < 0.15f)
                return; // the click that started the capture

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndCapture();
                SetStatus("");
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                SetPending(_capEntry, new KeyboardShortcut(KeyCode.None));
                EndCapture();
                return;
            }

            KeyCode[] keys = SettingsPanel.CaptureKeys();
            for (int i = 0; i < keys.Length; i++)
            {
                if (!Input.GetKeyDown(keys[i]))
                    continue;
                var mods = new List<KeyCode>();
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    mods.Add(KeyCode.LeftShift);
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    mods.Add(KeyCode.LeftControl);
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                    mods.Add(KeyCode.LeftAlt);
                SetPending(_capEntry, new KeyboardShortcut(keys[i], mods.ToArray()));
                EndCapture();
                return;
            }
        }
    }
}
