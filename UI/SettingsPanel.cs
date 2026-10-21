using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// F10 panel: activity log checkbox (anyone, local) and gameplay settings with Save
    /// (host / adminlist on servers, anyone offline).
    /// Built with uGUI from Valheim's own UI sprites (woodpanel_settings, button, checkbox,
    /// text_field …) and Norse fonts, so it looks like the vanilla settings menu. Any sprite
    /// the game does not provide falls back to the SAC wood art or a plain color.
    /// </summary>
    internal static class SettingsPanel
    {
        private const float RefW = 1920f;
        private const float RefH = 1080f;
        private const float PanelW = 1040f;
        /// <summary>Right-hand strip of every range row reserved for its hotkey buttons.</summary>
        private const float HotkeyBlock = 192f;
        private const float HotkeyBtnW = 86f;
        private const float PanelH = 960f;
        private const float SliderMax = 200f;

        internal static readonly Color Gold = new Color(1f, 0.79f, 0.34f, 1f);
        internal static readonly Color TextColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        internal static readonly Color Muted = new Color(0.78f, 0.74f, 0.66f, 1f);
        internal static readonly Color Warn = new Color(1f, 0.64f, 0.35f, 1f);
        internal static readonly Color CheckYellow = new Color(1f, 0.86f, 0.1f, 1f);

        private sealed class Row
        {
            public string Key;
            public string En;
            public string De;
            public string HelpEn;
            public string HelpDe;
            public float Value;
            public string Unit = "m";
            public float Min;
            public float SliderMax = SettingsPanel.SliderMax;
            public float Max = ModConfig.MaxRange;
            /// <summary>Whole numbers only (station capacities: no 13.5 ore).</summary>
            public bool Whole;
            /// <summary>Capacity row: config 0 = this vanilla amount (shown as the real number). -1 = none.</summary>
            public float Vanilla = -1f;
            /// <summary>Station capacity row: the help lists all of them as one entry.</summary>
            public bool Cap;
            public Slider Slider;
            public TMP_InputField Input;
            public bool Syncing;
            /// <summary>Shown only when "Advanced" is opened (the old player ranges).</summary>
            public bool Advanced;
            public GameObject Go;
        }

        private static bool _showAdvanced;
        private const string LoadedAreaKey = "automationloadedarea";
        /// <summary>The switch as shown in the open panel (not saved yet).</summary>
        private static bool _loadedAreaOn;
        private static TextMeshProUGUI _advancedHeader;

        // SAC-CATCHUP
        private static bool _catchUp;
        private static bool _torchAutoFill;
        private static bool _torchOverride;

        /// <summary>Synced on/off setting (admin, sent on Save via ConfigCommands.BoolEntryFor key).</summary>
        private sealed class BoolRow
        {
            public string Key;
            public string En;
            public string De;
            public bool Value;
        }

        private static readonly List<BoolRow> BoolRows = new List<BoolRow>();

        public static bool IsOpen { get; private set; }

        // ---- hotkeys (live rebind, saved in the BepInEx config right away)
        private sealed class HotkeyBtn
        {
            public ConfigEntry<KeyboardShortcut> Entry;
            public string Name;
            public TextMeshProUGUI Text;
        }

        private static readonly List<HotkeyBtn> HotkeyBtns = new List<HotkeyBtn>();
        private static HotkeyBtn _capturing;
        private static float _captureStarted;
        private static int _captureEndFrame = -10;
        private static KeyCode[] _captureKeys;

        /// <summary>True while a hotkey is being rebound (and one frame after): other hotkeys stay quiet.</summary>
        internal static bool CapturingKey
        {
            get { return _capturing != null || Time.frameCount <= _captureEndFrame || ModManagerPanel.Capturing; }
        }

        private static bool _canEdit;
        private static int _closeAtFrame = -1;
        private static GameObject _root;
        private static RectTransform _content;
        private static GameObject _infoBody;
        private static TextMeshProUGUI _infoHeader;
        private static TextMeshProUGUI _status;
        private static readonly List<Row> Rows = new List<Row>();
        private static readonly Dictionary<string, Sprite> VanillaSprites = new Dictionary<string, Sprite>();

        public static void Toggle()
        {
            if (IsOpen)
                RequestClose();
            else
                Open();
        }

        private static void Open()
        {
            if (Plugin.Settings == null || Player.m_localPlayer == null)
                return;
            _canEdit = ConfigCommands.CanEditRanges();
            // Ask the server again: its answer follows the current adminlist.txt, the one from joining may be old.
            ConfigSync.RequestConfigFromServer();
            LoadRows();
            _closeAtFrame = -1;
            UiFonts.ThinNorse();
            try
            {
                Build();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("F10 panel build failed: " + ex);
                DestroyRoot();
                return;
            }
            IsOpen = true;
        }

        /// <summary>The mod manager opens the StoreAndCraft page (this panel) from its list.</summary>
        internal static void OpenFromManager()
        {
            if (!IsOpen)
                Open();
        }

        /// <summary>Back arrow: close this panel, the mod manager shows its list again.</summary>
        internal static void BackToManager()
        {
            ModManagerPanel.RequestListAfterSac();
            RequestClose();
        }

        private static void RequestClose()
        {
            // Close one frame later so the same Escape press cannot open the pause menu.
            if (_closeAtFrame < 0)
                _closeAtFrame = Time.frameCount + 1;
        }

        private static void Close()
        {
            IsOpen = false;
            _closeAtFrame = -1;
            DestroyRoot();
        }

        private static void DestroyRoot()
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                Rows[i].Slider = null;
                Rows[i].Input = null;
            }
            HotkeyBtns.Clear();
            _capturing = null;
            _content = null;
            _infoBody = null;
            _infoHeader = null;
            _advancedHeader = null;
            _status = null;
            _groundListText = null;
            _keepListText = null;
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }

        internal static bool BlocksInput
        {
            get { return (IsOpen || ModManagerPanel.IsOpen) && Player.m_localPlayer != null; }
        }

        internal static void Tick()
        {
            if (!IsOpen)
                return;
            if (Player.m_localPlayer == null || Plugin.Settings == null || _root == null)
            {
                Close();
                return;
            }

            if (_closeAtFrame >= 0)
            {
                if (Time.frameCount >= _closeAtFrame)
                    Close();
                return;
            }

            // An item list (OpenItemList) is open above, or closed with this Escape: F10 waits.
            if (DisplayTypeMenu.ShouldBlockPause())
                return;

            // The server's answer to the request in Open() can change the edit permission: rebuild once.
            bool canEditNow = ConfigCommands.CanEditRanges();
            if (canEditNow != _canEdit && _capturing == null)
            {
                _canEdit = canEditNow;
                LoadRows();
                try
                {
                    Build();
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("F10 panel rebuild failed: " + ex);
                    Close();
                }
                return;
            }

            if (_capturing != null)
            {
                HandleCapture();
                return;
            }

            if (ZInput.GetKeyDown(KeyCode.Escape, true) || KeyUtil.Down(Plugin.Settings.ActivityLogKey.Value))
                RequestClose();
        }

        // ---------------------------------------------------------------- hotkeys

        private static List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>> HotkeysFor(string rowKey)
        {
            ModConfig c = Plugin.Settings;
            var l = new List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>>();
            switch (rowKey)
            {
                case "dumprange":
                    l.Add(Pair(Loc.T("Dump", "Einlagern"), c.DumpKey));
                    l.Add(Pair(Loc.T("Click", "Klick"), c.HoverStoreKey));
                    break;
                case "storagerange":
                    l.Add(Pair(Loc.T("Take stack", "Stack holen"), c.TakeStackKey));
                    l.Add(Pair(Loc.T("Search", "Suche"), c.SearchKey));
                    break;
                case "craftrange":
                    l.Add(Pair(Loc.T("Grab mats", "Material holen"), c.BuildGrabKey));
                    break;
                case "autofillrange":
                    l.Add(Pair(Loc.T("Auto-fill", "Auto-Fill"), c.AutoFillKey));
                    break;
                case "autofillchestrange":
                    l.Add(Pair(Loc.T("Auto-store", "Auto-Lagern"), c.AutoDropKey));
                    break;
                case "displayrange":
                    l.Add(Pair(Loc.T("Range", "Reichweite"), c.DisplayRangeKey));
                    break;
            }
            return l;
        }

        private static KeyValuePair<string, ConfigEntry<KeyboardShortcut>> Pair(string name, ConfigEntry<KeyboardShortcut> entry)
        {
            return new KeyValuePair<string, ConfigEntry<KeyboardShortcut>>(name, entry);
        }

        /// <summary>Every SAC hotkey with a readable name (conflict check).</summary>
        private static List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>> AllHotkeys()
        {
            ModConfig c = Plugin.Settings;
            return new List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>>
            {
                Pair(Loc.T("Dump", "Einlagern"), c.DumpKey),
                Pair(Loc.T("Restock", "Auffüllen"), c.RestockKey),
                Pair(Loc.T("Click store", "Klick-Einlagern"), c.HoverStoreKey),
                Pair(Loc.T("Search", "Suche"), c.SearchKey),
                Pair(Loc.T("Settings", "Einstellungen"), c.RenameKey),
                Pair(Loc.T("Take stack", "Stack holen"), c.TakeStackKey),
                Pair(Loc.T("Favorite", "Favorit"), c.FavoriteKey),
                Pair(Loc.T("Auto-fill", "Auto-Fill"), c.AutoFillKey),
                Pair(Loc.T("Auto-store", "Auto-Lagern"), c.AutoDropKey),
                Pair(Loc.T("This panel", "Dieses Fenster"), c.ActivityLogKey),
                Pair(Loc.T("Display range", "Display-Reichweite"), c.DisplayRangeKey),
                Pair(Loc.T("Grab mats", "Material holen"), c.BuildGrabKey),
            };
        }

        private static string KeyText(ConfigEntry<KeyboardShortcut> entry)
        {
            string t = entry != null ? KeyUtil.Format(entry.Value) : "";
            return string.IsNullOrEmpty(t) ? "-" : t;
        }

        /// <summary>Key buttons in the right strip of a range row (bottom line, captions above).</summary>
        private static void AddHotkeyButtons(RectTransform row, List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>> keys, bool captions = true)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                // First entry is the left button.
                float right = 6f + (keys.Count - 1 - i) * (HotkeyBtnW + 6f);
                if (captions)
                {
                    TextMeshProUGUI cap = Label(row, "KeyCaption", 13f, false);
                    cap.text = keys[i].Key;
                    cap.color = Muted;
                    cap.alignment = TextAlignmentOptions.MidlineRight;
                    Place(cap.rectTransform, 1f, 0f, 1f, 0f, -(right + HotkeyBtnW), 38f, -right, 58f);
                }
                MakeKeyButton(row, keys[i], Place1(1f, 0f, -(right + HotkeyBtnW), 4f, -right, 36f), HotkeyBtnW);
            }
        }

        private static Vector4 Place1(float ax, float ay, float x0, float y0, float x1, float y1)
        {
            return new Vector4(x0, y0, x1, y1);
        }

        private static void MakeKeyButton(RectTransform parent, KeyValuePair<string, ConfigEntry<KeyboardShortcut>> key, Vector4 box, float width)
        {
            var hk = new HotkeyBtn { Entry = key.Value, Name = key.Key };
            Button btn = MakeButton(parent, "Key_" + key.Value.Definition.Key, KeyText(key.Value), () => StartCapture(hk));
            Place(btn.transform as RectTransform, 1f, 0f, 1f, 0f, box.x, box.y, box.z, box.w);
            TextMeshProUGUI t = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            t.enableAutoSizing = true;
            t.fontSizeMin = 10f;
            t.fontSizeMax = 16f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            hk.Text = t;
            HotkeyBtns.Add(hk);
        }

        private static void StartCapture(HotkeyBtn hk)
        {
            if (_capturing != null && _capturing.Text != null)
                _capturing.Text.text = KeyText(_capturing.Entry);
            _capturing = hk;
            _captureStarted = Time.unscaledTime;
            if (hk.Text != null)
                hk.Text.text = Loc.T("press key...", "Taste...");
            SetStatus(hk.Name + ": " + Loc.T("press the new key (Esc = cancel, Backspace = unbind).",
                "neue Taste drücken (Esc = abbrechen, Rücktaste = entfernen)."));
        }

        private static void EndCapture()
        {
            if (_capturing != null && _capturing.Text != null)
                _capturing.Text.text = KeyText(_capturing.Entry);
            _capturing = null;
            _captureEndFrame = Time.frameCount;
        }

        private static void SetStatus(string text)
        {
            if (_status != null)
                _status.text = text;
        }

        internal static KeyCode[] CaptureKeys()
        {
            if (_captureKeys != null)
                return _captureKeys;
            var list = new List<KeyCode>();
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
            {
                string n = k.ToString();
                if (k == KeyCode.None || k == KeyCode.Escape || k == KeyCode.Backspace
                    || k == KeyCode.Mouse0 || k == KeyCode.Mouse1
                    || n.StartsWith("Joystick") || n.Contains("Shift") || n.Contains("Control")
                    || n.Contains("Alt") || n.Contains("Command") || n.Contains("Windows") || n.Contains("Apple"))
                    continue;
                list.Add(k);
            }
            _captureKeys = list.ToArray();
            return _captureKeys;
        }

        private static void HandleCapture()
        {
            if (Time.unscaledTime - _captureStarted < 0.15f)
                return; // the click that started the capture
            HotkeyBtn hk = _capturing;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndCapture();
                SetStatus("");
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                // The key that opens this panel must stay: it is the way back in.
                if (hk.Entry == Plugin.Settings.ActivityLogKey)
                {
                    SetStatus(Loc.T("This key opens this panel and cannot be unbound.", "Diese Taste öffnet dieses Fenster und kann nicht entfernt werden."));
                    EndCapture();
                    return;
                }
                hk.Entry.Value = new KeyboardShortcut(KeyCode.None);
                SetStatus(hk.Name + ": " + Loc.T("unbound.", "entfernt."));
                EndCapture();
                return;
            }

            KeyCode[] keys = CaptureKeys();
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
                var next = new KeyboardShortcut(keys[i], mods.ToArray());

                List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>> all = AllHotkeys();
                for (int k = 0; k < all.Count; k++)
                {
                    if (all[k].Value != hk.Entry && all[k].Value.Value.Equals(next))
                    {
                        SetStatus(KeyUtil.Format(next) + " " + Loc.T("is already used by", "wird schon benutzt von") + " " + all[k].Key + ".");
                        EndCapture();
                        return;
                    }
                }

                hk.Entry.Value = next;
                SetStatus(hk.Name + ": " + KeyUtil.Format(next));
                EndCapture();
                return;
            }
        }

        // ---------------------------------------------------------------- data

        private static void LoadRows()
        {
            Rows.Clear();
            AddRow("dumprange", "Dump / middle-click (player to chest)", "Einlagern / Mittelklick (Spieler zur Kiste)",
                "Dump key or middle-click: how far chests may be from you when your bag is sorted into them.",
                "Einlager-Taste oder Mittelklick: wie weit Kisten von dir weg sein dürfen, in die deine Tasche einsortiert wird.");
            AddRow("storerange", "Auto-store ground items (item to chest)", "Auto-Lagern vom Boden (Item zur Kiste)",
                "Items on the ground (drops, harvest) move into a matching chest within this distance of the item.",
                "Items auf dem Boden (Drops, Ernte) wandern in eine passende Kiste, die so nah am Item steht.");
            AddRow("storagerange", "Take stack / search", "Stack holen / Suche",
                "Ctrl + middle-click fills a stack from chests, Y points to the chest holding an item. Measured from you.",
                "Strg + Mittelklick füllt einen Stack aus Kisten, Y zeigt die Kiste mit dem Item. Gemessen von dir.");
            AddRow("craftrange", "Craft / build / station [E] (player to chest)", "Craften / Bauen / Station [E] (Spieler zur Kiste)",
                "Crafting, building and [E] on stations can use materials from chests this close to you.",
                "Craften, Bauen und [E] an Stationen nutzen Material aus Kisten, die so nah bei dir stehen.");
            AddRow("autofillrange", "Auto-fill (player to station)", "Auto-Fill (Spieler zur Station)",
                "Stations with auto-fill (B) are refilled while you are at most this far away from them. Only used while \"whole loaded base\" is off; it is also the station chest range when that one is 0.",
                "Stationen mit Auto-Fill (B) werden befüllt, solange du höchstens so weit von ihnen weg bist. Gilt nur, wenn \"ganze geladene Base\" aus ist; außerdem die Stationen-Kisten-Reichweite, wenn die auf 0 steht.");
            AddRow("autofillchestrange", "Auto-fill + auto-store (station to chest, 0 = same)", "Auto-Fill + Auto-Lagern (Station zur Kiste, 0 = wie Auto-Fill)",
                "Which chests around a station supply it, and where finished items (N) go. 0 uses the auto-fill value (Advanced).",
                "Aus welchen Kisten rund um eine Station Material kommt und wohin fertige Items (N) gehen. 0 = Wert von Auto-Fill.");
            AddRow("scarecrowrange", "Scarecrow (player to scarecrow, 0 = same)", "Vogelscheuche (Spieler zur Vogelscheuche, 0 = wie Auto-Fill)",
                "The scarecrow plants and harvests while you are at most this far away from it. 0 uses the auto-fill value.",
                "Die Vogelscheuche pflanzt und erntet, solange du höchstens so weit von ihr weg bist. 0 = Wert von Auto-Fill.");
            AddRow("scarecrowchestrange", "Scarecrow chests (scarecrow to chest, 0 = same)", "Vogelscheuche-Kisten (Vogelscheuche zur Kiste, 0 = wie Stationen)",
                "Which chests around the scarecrow supply its seeds and take its harvest. 0 uses the station chest value.",
                "Aus welchen Kisten rund um die Vogelscheuche sie Samen holt und wohin die Ernte geht. 0 = Wert der Stationen-Kisten.");
            // Storage Display range: only per display (Alt+R), no slider here.
            // Without effect while "whole loaded base" is on: shown under Advanced.
            MarkAdvanced("dumprange", "storerange", "storagerange", "craftrange", "autofillrange",
                "autofillchestrange", "scarecrowrange", "scarecrowchestrange");
            AddRow("feedtroughrange", "Feed trough (animals)", "Futtertrog (Tiere)",
                "How far hungry tames notice a feed trough and walk to it.",
                "Wie weit hungrige gezähmte Tiere einen Futtertrog bemerken und hinlaufen.");

            AddRow("intakeinterval", "Auto-store: seconds between ground scans", "Auto-Lagern: Sekunden zwischen den Boden-Scans",
                "How often items on the ground are stored (also auto-stack). Lower = items vanish faster, higher = less work for the game.",
                "Wie oft Items vom Boden eingelagert werden (auch Auto-Stapeln). Weniger = Items verschwinden schneller, mehr = weniger Arbeit für das Spiel.");
            Row intake = Rows[Rows.Count - 1];
            intake.Unit = "s";
            intake.Min = 1f;
            intake.SliderMax = 30f;
            intake.Max = 60f;

            // SAC-CATCHUP
            _catchUp = Plugin.Settings.CatchUpEnabled.Value;
            _torchAutoFill = Plugin.Settings.TorchAutoFillDefault.Value;
            _torchOverride = Plugin.Settings.TorchAutoFillOverride.Value;
            AddRow("catchuphours", "Catch up while away: max hours", "Nachholen: maximale Stunden",
                "Most game time one smelter catches up after its zone was unloaded (only with the checkbox on).",
                "So viel Spielzeit holt eine Schmelze höchstens nach, wenn ihre Zone entladen war (nur mit Häkchen).");
            Row hours = Rows[Rows.Count - 1];
            hours.Unit = "h";
            hours.Min = 0.5f;
            hours.SliderMax = 48f;
            hours.Max = 48f;

            // Station capacities: 0 = vanilla, up to 50. Applied live after Save.
            AddCapRow("kilnmax", "Charcoal kiln: max wood (0 = vanilla)", "Köhler: max. Holz (0 = Vanilla)");
            AddCapRow("smeltermaxore", "Smelter: max ore (0 = vanilla)", "Schmelze: max. Erz (0 = Vanilla)");
            AddCapRow("smeltermaxfuel", "Smelter: max coal (0 = vanilla)", "Schmelze: max. Kohle (0 = Vanilla)");
            AddCapRow("blastmaxore", "Blast furnace: max ore (0 = vanilla)", "Hochofen: max. Erz (0 = Vanilla)");
            AddCapRow("blastmaxfuel", "Blast furnace: max coal (0 = vanilla)", "Hochofen: max. Kohle (0 = Vanilla)");
            AddCapRow("eitrmaxore", "Eitr refinery: max soft tissue (0 = vanilla)", "Eitr-Raffinerie: max. Weichgewebe (0 = Vanilla)");
            AddCapRow("eitrmaxfuel", "Eitr refinery: max sap (0 = vanilla)", "Eitr-Raffinerie: max. Saft (0 = Vanilla)");
            AddCapRow("beehivemax", "Beehive: max honey (0 = vanilla)", "Bienenstock: max. Honig (0 = Vanilla)");
            AddCapRow("fermenterbatch", "Fermenter: mead bases per batch", "Fermenter: Met-Basen pro Charge");

            BoolRows.Clear();
            AddBool("storeenabled", "Dump / middle-click store, take stack", "Einlagern / Mittelklick, Stack holen");
            AddBool("autointake", "Auto-store ground items", "Auto-Lagern vom Boden");
            AddBool("automationloadedarea", "Everything works in the whole loaded base (off = ranges in meters, see Advanced)", "Alles funktioniert in der ganzen geladenen Base (aus = Reichweiten in Metern, siehe Erweitert)");
            AddBool("musthaveexisting", "Chests only take items they already hold", "Kisten nehmen nur Items, die schon drin sind");
            AddBool("autostack", "Auto-stack inside chests", "Stapel in Kisten automatisch zusammenlegen");
            AddBool("craftenabled", "Craft / build / stations use chest items", "Craften / Bauen / Stationen nutzen Kisten-Items");
            AddBool("leaveone", "Leave 1 item in chests when taking", "Beim Entnehmen 1 Item in der Kiste lassen");
            AddBool("feedtroughenabled", "Feed trough in the hammer (after restart)", "Futtertrog im Hammer (nach Neustart)");
            AddBool("armorstandswap", "Armor stand: E swaps the armor you wear", "Rüstungsständer: E tauscht die getragene Rüstung");
        }

        private static void AddBool(string key, string en, string de)
        {
            ConfigEntry<bool> entry = ConfigCommands.BoolEntryFor(key);
            if (entry == null)
                return;
            BoolRows.Add(new BoolRow { Key = key, En = en, De = de, Value = entry.Value });
        }

        private static void AddCapRow(string key, string en, string de)
        {
            AddRow(key, en, de,
                "How much fits into this station type. 0 keeps the vanilla amount. Applies to every station at once.",
                "Wie viel in diese Station passt. 0 lässt den Vanilla-Wert. Gilt sofort für alle Stationen dieser Art.");
            if (Rows.Count == 0 || Rows[Rows.Count - 1].Key != key)
                return;
            Row row = Rows[Rows.Count - 1];
            row.Cap = true;
            row.Unit = "";
            row.Whole = true;
            row.SliderMax = ModConfig.MaxStationCap;
            row.Max = ModConfig.MaxStationCap;
            if (key == "fermenterbatch")
            {
                // Plain count, vanilla = 1.
                row.Min = 1f;
                row.Value = Mathf.Clamp(Mathf.Round(row.Value), 1f, row.Max);
                return;
            }
            // Left end = the station's vanilla amount; config 0 is shown as that real number.
            int vanilla = StationCaps.Vanilla(key);
            row.Vanilla = vanilla > 0 ? vanilla : 1f;
            row.Min = row.Vanilla;
            if (row.Vanilla > row.SliderMax)
                row.SliderMax = row.Max = row.Vanilla;
            row.Value = row.Value <= 0f ? row.Vanilla : Mathf.Clamp(Mathf.Round(row.Value), row.Min, row.Max);
        }

        private static void AddRow(string key, string en, string de, string helpEn, string helpDe)
        {
            ConfigEntry<float> entry = ConfigCommands.EntryFor(key);
            if (entry == null)
                return;
            Rows.Add(new Row
            {
                Key = key,
                En = en,
                De = de,
                HelpEn = helpEn,
                HelpDe = helpDe,
                Value = entry.Value
            });
        }

        private static void MarkAdvanced(params string[] keys)
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                if (System.Array.IndexOf(keys, Rows[i].Key) >= 0)
                    Rows[i].Advanced = true;
            }
        }

        private static string Format(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static void Save()
        {
            // No admin: the server values cannot be saved, but the settings for this PC (checkboxes under
            // "Only for me", hotkeys) already apply when clicked. Save writes them to the config file now.
            if (!_canEdit)
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.Config.Save();
                if (_status != null)
                    _status.text = Loc.T(
                        "Your settings for this PC are saved. Server values can only be saved by an admin.",
                        "Deine Einstellungen für diesen PC sind gespeichert. Server-Werte kann nur ein Admin speichern.");
                return;
            }

            var changes = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < Rows.Count; i++)
            {
                ConfigEntry<float> entry = ConfigCommands.EntryFor(Rows[i].Key);
                if (entry == null)
                    continue;
                float wanted = Rows[i].Whole ? Mathf.Round(Rows[i].Value) : Rows[i].Value;
                // Capacity back at vanilla → store 0 (follows future vanilla changes).
                if (Rows[i].Vanilla > 0f && wanted <= Rows[i].Vanilla)
                    wanted = 0f;
                if (Mathf.Abs(entry.Value - wanted) < 0.001f)
                    continue;
                changes.Add(new KeyValuePair<string, float>(Rows[i].Key, wanted));
            }
            if (_catchUp != Plugin.Settings.CatchUpEnabled.Value) // SAC-CATCHUP
                changes.Add(new KeyValuePair<string, float>("catchup", _catchUp ? 1f : 0f));
            if (_torchAutoFill != Plugin.Settings.TorchAutoFillDefault.Value)
                changes.Add(new KeyValuePair<string, float>("torchautofill", _torchAutoFill ? 1f : 0f));
            if (_torchOverride != Plugin.Settings.TorchAutoFillOverride.Value)
                changes.Add(new KeyValuePair<string, float>("torchautofilloverride", _torchOverride ? 1f : 0f));
            for (int i = 0; i < BoolRows.Count; i++)
            {
                ConfigEntry<bool> entry = ConfigCommands.BoolEntryFor(BoolRows[i].Key);
                if (entry != null && entry.Value != BoolRows[i].Value)
                    changes.Add(new KeyValuePair<string, float>(BoolRows[i].Key, BoolRows[i].Value ? 1f : 0f));
            }

            string result = ConfigCommands.ApplyFromPanel(changes);
            // Rebuild so sliders / fields show what is really stored now.
            Close();
            Open();
            if (_status != null)
                _status.text = result ?? "";
        }

        // ---------------------------------------------------------------- UI build

        private static void Build()
        {
            DestroyRoot();

            _root = new GameObject("SAC_SettingsPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            // Build inactive: TMP_InputField / Slider must be fully wired before OnEnable runs.
            _root.SetActive(false);
            Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 1f; // by height: on ultrawide screens (21:9, 32:9) the panel still fits vertically

            // Dim + click blocker only (no click-to-close: unsaved edits would be lost).
            var dim = NewUi("Dim", _root.transform, typeof(Image));
            Stretch(dim);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            // Wood panel like the vanilla settings dialog.
            var panel = NewUi("Panel", _root.transform, typeof(Image));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelW, PanelH);
            panel.anchoredPosition = Vector2.zero;
            SetSprite(panel.GetComponent<Image>(), Vanilla("woodpanel_settings") ?? UiAssets.PanelLeft,
                new Color(0.16f, 0.12f, 0.08f, 0.97f));

            TextMeshProUGUI title = Label(panel, "Title", 36f, true);
            title.text = "StoreAndCraft";
            title.color = Gold;
            title.alignment = TextAlignmentOptions.Center;
            Place(title.rectTransform, 0f, 1f, 1f, 1f, 40f, -86f, -40f, -30f);

            // Scrollable settings list.
            var scroll = NewUi("Scroll", panel, typeof(Image), typeof(ScrollRect));
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(48f, 150f);
            scroll.offsetMax = new Vector2(-48f, -96f);
            scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var viewport = NewUi("Viewport", scroll, typeof(Image), typeof(RectMask2D));
            Stretch(viewport);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            var content = NewUi("Content", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
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
            sr.scrollSensitivity = 360f; // same as StationFilterMenu; 40 barely moved
            AddScrollbar(sr, scroll);

            BuildContent();

            // Bottom: Save / Close + status line.
            Button save = MakeButton(panel, "Save", Loc.T("Save", "Speichern"), Save);
            Place(save.transform as RectTransform, 0.5f, 0f, 0.5f, 0f, -230f, 78f, -20f, 124f);
            save.interactable = true; // also without admin: saves the settings for this PC

            Button close = MakeButton(panel, "Close", Loc.T("Close", "Schließen"), RequestClose);
            Place(close.transform as RectTransform, 0.5f, 0f, 0.5f, 0f, 20f, 78f, 230f, 124f);

            // Back to the list of all mods (F10 mod manager).
            Button back = MakeButton(panel, "Back", "<", BackToManager);
            Place(back.transform as RectTransform, 0f, 1f, 0f, 1f, 40f, -86f, 100f, -30f);

            _status = Label(panel, "Status", 17f, false);
            _status.color = Warn;
            _status.alignment = TextAlignmentOptions.Center;
            _status.text = "";
            Place(_status.rectTransform, 0f, 0f, 1f, 0f, 48f, 44f, -48f, 74f);

            _root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static void BuildContent()
        {
            AddItemListButtons();
            RectTransform logRow = AddToggle(Loc.T("Show activity log", "Aktivitäts-Log anzeigen"), ActivityLog.Visible, true,
                on => ActivityLog.SetVisible(on));
            var panelKey = new List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>>
                { Pair(Loc.T("This panel", "Dieses Fenster"), Plugin.Settings.ActivityLogKey) };
            AddHotkeyButtons(logRow, panelKey, false);

            AddHeader(Loc.T("Ranges (meters, 0-1000)", "Reichweiten (Meter, 0-1000)"));
            if (!_canEdit)
            {
                AddText(Loc.T(
                    "Only admins (adminlist) can change these settings on this server.",
                    "Nur Admins (Adminliste) dürfen diese Einstellungen auf diesem Server ändern."), Warn, 17f);
            }

            // The main switch sits on top of the ranges: on = no meters needed, the sliders are under Advanced.
            // Advanced (the meter ranges) sits right below it and is locked while the switch is on.
            _loadedAreaOn = false;
            for (int i = 0; i < BoolRows.Count; i++)
            {
                BoolRow row = BoolRows[i];
                if (row.Key != LoadedAreaKey)
                    continue;
                _loadedAreaOn = row.Value;
                AddToggle(Loc.T(row.En, row.De), row.Value, _canEdit, on =>
                {
                    row.Value = on;
                    _loadedAreaOn = on;
                    RefreshAdvancedLock();
                });
            }

            bool anyAdvanced = false;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Advanced)
                    anyAdvanced = true;
            }

            // Collapsible: the meter ranges, for anyone who keeps "whole loaded base" off.
            if (anyAdvanced)
            {
                Button advBtn = MakeButton(_content, "AdvancedToggle", "", ToggleAdvanced);
                AddLayout(advBtn.gameObject, 44f);
                _advancedHeader = advBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < Rows.Count; i++)
                {
                    if (!Rows[i].Advanced)
                        continue;
                    AddRangeRow(Rows[i]);
                    if (Rows[i].Go != null)
                        Rows[i].Go.SetActive(_showAdvanced);
                }
                RefreshAdvancedLock();
            }

            for (int i = 0; i < Rows.Count; i++)
            {
                if (!Rows[i].Advanced)
                    AddRangeRow(Rows[i]);
            }

            AddHeader(Loc.T("Stations", "Stationen"));
            AddToggle(Loc.T( // SAC-CATCHUP
                    "Catch up while away (smelters with B + N)",
                    "Nachholen während niemand da ist (Schmelzen mit B + N)"),
                _catchUp, _canEdit, on => _catchUp = on);
            AddToggle(Loc.T(
                    "New torches start with auto-fill on",
                    "Neue Fackeln haben Auto-Fill von Anfang an"),
                _torchAutoFill, _canEdit, on => _torchAutoFill = on);
            AddToggle(Loc.T(
                    "World Override: auto-fill on all torches",
                    "Welt-Override: Auto-Fill auf allen Fackeln"),
                _torchOverride, _canEdit, on => _torchOverride = on);

            // Other synced on/off settings — admin, applied on Save like the ranges.
            AddHeader(Loc.T("Store / craft (on / off)", "Lagern / Craften (an / aus)"));
            for (int i = 0; i < BoolRows.Count; i++)
            {
                BoolRow row = BoolRows[i];
                if (row.Key == LoadedAreaKey)
                    continue; // shown above the ranges
                AddToggle(Loc.T(row.En, row.De), row.Value, _canEdit, on => row.Value = on);
            }

            // Local only (this PC) — applied right away, like the activity log checkbox.
            AddHeader(Loc.T("Only for me (this PC)", "Nur für mich (dieser PC)"));
            AddLocalToggle(Loc.T("Dump skips the hotbar", "Einlagern lässt die Hotbar aus"), Plugin.Settings.IgnoreHotbar);
            AddLocalToggle(Loc.T("Chest flashes when something is stored", "Kiste blinkt beim Einlagern"), Plugin.Settings.HighlightOnStore);
            AddLocalToggle(Loc.T("Map ping on the chest after storing", "Karten-Ping an der Kiste nach dem Einlagern"), Plugin.Settings.PingOnStore);
            AddLocalToggle(Loc.T("Stack / Sort buttons on the inventory", "Stapeln- / Sortieren-Buttons am Inventar"), Plugin.Settings.ShowBagSortButtons);
            AddLocalToggle(Loc.T("Stack / Sort buttons on the chest", "Stapeln- / Sortieren-Buttons an der Kiste"), Plugin.Settings.ShowChestSortButtons);
            AddLocalToggle(Loc.T("Notice above stations (chest full / no chest)", "Hinweis über Stationen (Kiste voll / keine Kiste)"), Plugin.Settings.ShowStationNotices);
            // Hotkeys without a range slider (the others sit next to their feature above).
            AddHeader(Loc.T("More hotkeys (only for me, change by clicking)", "Weitere Hotkeys (nur für mich, zum Ändern anklicken)"));
            AddHotkeyRow(Loc.T("Chest / station / display settings", "Kisten- / Stations- / Display-Einstellungen"),
                Plugin.Settings.RenameKey, Loc.T("Settings", "Einstellungen"));
            AddHotkeyRow(Loc.T("Favorite an item (inventory)", "Item favorisieren (Inventar)"),
                Plugin.Settings.FavoriteKey, Loc.T("Favorite", "Favorit"));
            AddHotkeyRow(Loc.T("Restock the items marked Fill from chests", "Mit Auffüllen markierte Items aus Kisten holen"),
                Plugin.Settings.RestockKey, Loc.T("Restock", "Auffüllen"));

            // Collapsible help.
            Button infoBtn = MakeButton(_content, "InfoToggle", "", ToggleInfo);
            AddLayout(infoBtn.gameObject, 44f);
            _infoHeader = infoBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            _infoBody = BuildInfoBody();
            _infoBody.SetActive(false);
            RefreshInfoHeader();
        }

        /// <summary>
        /// Visible scrollbar on the right of a long list (drag it with the mouse; the wheel still works).
        /// Shown only while the content is taller than the list. Used by this panel and the mod manager.
        /// </summary>
        internal static void AddScrollbar(ScrollRect sr, RectTransform scroll)
        {
            RectTransform bar = NewUi("Scrollbar", scroll, typeof(Image), typeof(Scrollbar));
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            // Outside the dark list, in the wood margin between it and the panel edge (not on top of the controls).
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(20f, -12f);
            bar.anchoredPosition = new Vector2(8f, 0f);
            SetSprite(bar.GetComponent<Image>(), Vanilla("text_field"), new Color(0f, 0f, 0f, 0.55f));

            RectTransform area = NewUi("Sliding Area", bar);
            Place(area, 0f, 0f, 1f, 1f, 2f, 2f, -2f, -2f);
            RectTransform handle = NewUi("Handle", area, typeof(Image));
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = Vector2.zero;
            Image handleImg = handle.GetComponent<Image>();
            SetSprite(handleImg, Vanilla("button_small") ?? Vanilla("button"), Gold);

            Scrollbar sb = bar.GetComponent<Scrollbar>();
            sb.handleRect = handle;
            sb.targetGraphic = handleImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static void ToggleAdvanced()
        {
            _showAdvanced = !_showAdvanced;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Advanced && Rows[i].Go != null)
                    Rows[i].Go.SetActive(_showAdvanced);
            }
            RefreshAdvancedHeader();
            if (_content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private static void RefreshAdvancedHeader()
        {
            if (_advancedHeader == null)
                return;
            _advancedHeader.text = (_showAdvanced ? "[-]  " : "[+]  ") + (_loadedAreaOn
                ? Loc.T("Advanced: ranges in meters (locked while \"whole loaded base\" is on)",
                    "Erweitert: Reichweiten in Metern (gesperrt, solange \"ganze geladene Base\" an ist)")
                : Loc.T("Advanced: ranges in meters", "Erweitert: Reichweiten in Metern"));
        }

        /// <summary>The meter ranges do nothing while "whole loaded base" is on: greyed out and not editable.</summary>
        private static void RefreshAdvancedLock()
        {
            bool editable = _canEdit && !_loadedAreaOn;
            for (int i = 0; i < Rows.Count; i++)
            {
                Row row = Rows[i];
                if (!row.Advanced)
                    continue;
                if (row.Slider != null)
                    row.Slider.interactable = editable;
                if (row.Input != null)
                    row.Input.interactable = editable;
                if (row.Go != null)
                {
                    CanvasGroup group = row.Go.GetComponent<CanvasGroup>();
                    if (group == null)
                        group = row.Go.AddComponent<CanvasGroup>();
                    group.alpha = _loadedAreaOn ? 0.45f : 1f;
                }
            }
            RefreshAdvancedHeader();
        }

        private static void ToggleInfo()
        {
            if (_infoBody == null)
                return;
            _infoBody.SetActive(!_infoBody.activeSelf);
            RefreshInfoHeader();
            if (_content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private static void RefreshInfoHeader()
        {
            if (_infoHeader == null)
                return;
            bool open = _infoBody != null && _infoBody.activeSelf;
            _infoHeader.text = (open ? "[-]  " : "[+]  ") + Loc.T("What do these settings do?", "Was bewirken die Einstellungen?");
        }

        private static GameObject BuildInfoBody()
        {
            var body = NewUi("Info", _content, typeof(VerticalLayoutGroup));
            VerticalLayoutGroup v = body.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 2, 8);
            v.spacing = 2f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            bool anyCap = false;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Cap)
                {
                    anyCap = true;
                    continue;
                }
                AddInfoTitle(body, "• " + Loc.T(Rows[i].En, Rows[i].De));
                AddText(body, Loc.T(Rows[i].HelpEn, Rows[i].HelpDe), InfoYellow, 16f);
            }

            // All station capacities share one explanation (only the station differs).
            if (anyCap)
            {
                AddInfoTitle(body, "• " + Loc.T("Station capacities", "Stations-Kapazitäten"));
                AddText(body, Loc.T(
                    "Changes how much fits into a station: charcoal kiln, smelter, blast furnace, eitr refinery, beehive and the fermenter batch. "
                    + "0 keeps the vanilla amount. Applies to every station of that type at once.",
                    "Ändert, wie viel in eine Station passt: Köhler, Schmelze, Hochofen, Eitr-Raffinerie, Bienenstock und die Fermenter-Charge. "
                    + "0 lässt den Vanilla-Wert. Gilt sofort für alle Stationen dieser Art."),
                    InfoYellow, 16f);
            }

            AddText(body, Loc.T(
                "Valheim only loads about 120 m around you (Simulation distance in the graphics settings). "
                + "Larger values change nothing beyond that. Tip: auto-fill 80-120, station to chest 10-15, all others 10-30.",
                "Valheim lädt nur etwa 120 m um dich herum (Simulationsdistanz in den Grafik-Einstellungen). "
                + "Größere Werte bewirken darüber hinaus nichts. Tipp: Auto-Fill 80-120, Station zur Kiste 10-15, alles andere 10-30."),
                Warn, 16f);

            AddInfoTitle(body, "• " + Loc.T("Catch up while away", "Nachholen während niemand da ist")); // SAC-CATCHUP
            AddText(body, Loc.T(
                "Off by default. When nobody is near a base, Valheim unloads it and smelters stop once they are empty. "
                + "With this on, a kiln / smelter / blast furnace / windmill / spinning wheel / eitr refinery with auto-fill (B) "
                + "and auto-store (N) remembers that idle time. When someone with access comes back, it takes ore and fuel "
                + "from its chests and puts the finished items into a chest, one stack at a time, as if it had kept working. "
                + "It stops when the chests run out or the output chest is full. Nothing is taken if the result does not fit. "
                + "Only time after switching this on counts, capped by the max hours. Every package is in the activity log.",
                "Standardmäßig aus. Ist niemand an der Base, entlädt Valheim sie, und Schmelzen stehen still, sobald sie leer sind. "
                + "Mit Häkchen merkt sich ein Meiler / eine Schmelze / Hochofen / Windmühle / Spinnrad / Eitr-Raffinerie mit Auto-Fill (B) "
                + "und Auto-Lagern (N) diese Leerlaufzeit. Kommt jemand mit Zugriff zurück, nimmt sie Erz und Brennstoff "
                + "aus ihren Kisten und legt die fertigen Items in eine Kiste, einen Stapel nach dem anderen, als hätte sie weitergearbeitet. "
                + "Sie stoppt, wenn die Kisten leer sind oder die Ausgabe-Kiste voll ist. Passt das Ergebnis nicht, wird nichts entnommen. "
                + "Es zählt nur Zeit nach dem Einschalten, begrenzt durch die maximalen Stunden. Jedes Paket steht im Aktivitäts-Log."),
                InfoYellow, 16f);

            AddInfoTitle(body, "• " + Loc.T("New torches start with auto-fill on", "Neue Fackeln mit Auto-Fill von Anfang an"));
            AddText(body, Loc.T(
                "Off by default. When on, every torch (standing, wall, green / blue / mist) you place from now on refills itself "
                + "with fuel from nearby chests. Torches that are already placed keep their own setting. B on a torch switches that torch.",
                "Standardmäßig aus. Mit Häkchen füllt sich jede Fackel (Stand-, Wand-, grüne / blaue / Nebel-Fackel), die du ab jetzt "
                + "platzierst, selbst mit Brennstoff aus nahen Kisten nach. Schon stehende Fackeln behalten ihre Einstellung. B auf einer Fackel schaltet diese Fackel."),
                InfoYellow, 16f);
            AddInfoTitle(body, "• " + Loc.T("World Override: auto-fill on all torches", "Welt-Override: Auto-Fill auf allen Fackeln"));
            AddText(body, Loc.T(
                "Off by default. While on, every torch, placed ones too, refills itself whatever its own setting says. "
                + "The torches' own settings are not changed: switch it off and each torch is back to what it was.",
                "Standardmäßig aus. Solange das Häkchen gesetzt ist, füllt sich jede Fackel, auch schon stehende, selbst nach, egal was "
                + "ihre eigene Einstellung sagt. Die Einstellung der Fackeln bleibt unverändert: Häkchen weg, und jede Fackel ist wieder wie vorher."),
                InfoYellow, 16f);
            return body.gameObject;
        }

        private static void AddHeader(string text)
        {
            var go = NewUi("Header", _content);
            TextMeshProUGUI tmp = UiFonts.CreateBoldLabel(go.gameObject, 22f);
            tmp.text = text;
            tmp.color = Gold;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            AddLayout(go.gameObject, 40f);
        }

        private static readonly Color InfoYellow = new Color(1f, 0.9f, 0.3f, 1f);

        /// <summary>Feature name in the help: bold.</summary>
        private static void AddInfoTitle(RectTransform parent, string text)
        {
            var go = NewUi("Title", parent);
            TextMeshProUGUI tmp = UiFonts.CreateBoldLabel(go.gameObject, 19f);
            tmp.text = text;
            tmp.color = TextColor;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
        }

        private static void AddText(string text, Color color, float size)
        {
            AddText(_content, text, color, size);
        }

        private static void AddText(RectTransform parent, string text, Color color, float size)
        {
            var go = NewUi("Text", parent);
            TextMeshProUGUI tmp = UiFonts.CreateLabel(go.gameObject, size);
            tmp.text = text;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
        }

        private static void AddRangeRow(Row row)
        {
            var go = NewUi("Row_" + row.Key, _content);
            row.Go = go.gameObject;
            AddLayout(go.gameObject, 64f);

            TextMeshProUGUI label = Label(go, "Label", 18f, false);
            label.text = Loc.T(row.En, row.De);
            label.color = TextColor;
            Place(label.rectTransform, 0f, 1f, 1f, 1f, 0f, -28f, -HotkeyBlock, 0f);

            // Slider (vanilla text_field track, amber fill, button_small knob).
            var sliderRt = NewUi("Slider", go, typeof(Slider));
            Place(sliderRt, 0f, 0f, 1f, 0f, 4f, 10f, -126f - HotkeyBlock, 30f);
            var bg = NewUi("Background", sliderRt, typeof(Image));
            Stretch(bg);
            SetSprite(bg.GetComponent<Image>(), Vanilla("text_field"), new Color(0f, 0f, 0f, 0.55f));

            var fillArea = NewUi("Fill Area", sliderRt);
            Place(fillArea, 0f, 0f, 1f, 1f, 4f, 4f, -4f, -4f);
            var fill = NewUi("Fill", fillArea, typeof(Image));
            Stretch(fill);
            Image fillImg = fill.GetComponent<Image>();
            fillImg.color = new Color(0.85f, 0.55f, 0.18f, 0.85f);
            fillImg.raycastTarget = false;

            var handleArea = NewUi("Handle Slide Area", sliderRt);
            Place(handleArea, 0f, 0f, 1f, 1f, 8f, 0f, -8f, 0f);
            var handle = NewUi("Handle", handleArea, typeof(Image));
            handle.sizeDelta = new Vector2(18f, 0f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.offsetMin = new Vector2(-9f, -6f);
            handle.offsetMax = new Vector2(9f, 6f);
            SetSprite(handle.GetComponent<Image>(), Vanilla("button_small") ?? Vanilla("button"), Gold);

            Slider slider = sliderRt.GetComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = row.Min;
            slider.maxValue = row.SliderMax;
            slider.wholeNumbers = row.Whole || row.SliderMax > 50f;
            slider.SetValueWithoutNotify(Mathf.Min(row.Value, row.SliderMax));
            slider.interactable = _canEdit;
            row.Slider = slider;

            // Number field (vanilla text_field).
            var fieldRt = NewUi("Field", go, typeof(Image), typeof(TMP_InputField));
            Place(fieldRt, 1f, 0f, 1f, 0f, -110f - HotkeyBlock, 4f, -30f - HotkeyBlock, 36f);
            SetSprite(fieldRt.GetComponent<Image>(), Vanilla("text_field"), new Color(0f, 0f, 0f, 0.6f));

            var area = NewUi("Text Area", fieldRt, typeof(RectMask2D));
            Place(area, 0f, 0f, 1f, 1f, 8f, 2f, -8f, -2f);
            var textGo = NewUi("Text", area);
            Stretch(textGo);
            TextMeshProUGUI fieldText = UiFonts.CreateLabel(textGo.gameObject, 18f);
            fieldText.color = TextColor;
            fieldText.alignment = TextAlignmentOptions.MidlineRight;
            fieldText.textWrappingMode = TextWrappingModes.NoWrap;

            TMP_InputField input = fieldRt.GetComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = fieldText;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.characterLimit = 6;
            input.targetGraphic = fieldRt.GetComponent<Image>();
            input.SetTextWithoutNotify(Format(row.Value));
            input.interactable = _canEdit;
            row.Input = input;

            TextMeshProUGUI unit = Label(go, "Unit", 18f, false);
            unit.text = row.Unit;
            unit.color = Muted;
            Place(unit.rectTransform, 1f, 0f, 1f, 0f, -24f - HotkeyBlock, 4f, -HotkeyBlock, 36f);

            List<KeyValuePair<string, ConfigEntry<KeyboardShortcut>>> rowKeys = HotkeysFor(row.Key);
            if (rowKeys.Count > 0)
                AddHotkeyButtons(go, rowKeys);

            slider.onValueChanged.AddListener(v =>
            {
                if (row.Syncing)
                    return;
                row.Syncing = true;
                row.Value = Mathf.Max(row.Min, row.Whole || row.SliderMax > 50f ? Mathf.Round(v) : Mathf.Round(v * 2f) * 0.5f);
                if (row.Input != null)
                    row.Input.SetTextWithoutNotify(Format(row.Value));
                row.Syncing = false;
            });

            input.onEndEdit.AddListener(text =>
            {
                if (row.Syncing)
                    return;
                row.Syncing = true;
                float parsed;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                    || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                    row.Value = Mathf.Clamp(row.Whole ? Mathf.Round(parsed) : parsed, row.Min, row.Max);
                if (row.Input != null)
                    row.Input.SetTextWithoutNotify(Format(row.Value));
                if (row.Slider != null)
                    row.Slider.SetValueWithoutNotify(Mathf.Min(row.Value, row.SliderMax));
                row.Syncing = false;
            });
        }

        private static void AddLocalToggle(string text, ConfigEntry<bool> entry)
        {
            if (entry == null)
                return;
            AddToggle(text, entry.Value, true, on => entry.Value = on);
        }

        private static RectTransform AddToggle(string text, bool value, bool interactable, System.Action<bool> changed)
        {
            return AddToggle(_content, text, value, interactable, changed);
        }

        internal static RectTransform AddToggle(RectTransform parent, string text, bool value, bool interactable, System.Action<bool> changed)
        {
            var go = NewUi("Toggle", parent, typeof(Toggle));
            AddLayout(go.gameObject, 38f);

            var box = NewUi("Background", go, typeof(Image));
            box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(28f, 28f);
            box.anchoredPosition = new Vector2(2f, 0f);
            SetSprite(box.GetComponent<Image>(), Vanilla("checkbox") ?? UiAssets.ToggleOff, new Color(0f, 0f, 0f, 0.6f));

            var mark = NewUi("Checkmark", box, typeof(Image));
            // Centered yellow knob: vanilla checkbox_marker sits off-centre in its sprite (showed
            // at the edge / hidden), so the checkbox sprite itself is used, smaller and tinted.
            Place(mark, 0f, 0f, 1f, 1f, 7f, 7f, -7f, -7f);
            Image markImg = mark.GetComponent<Image>();
            SetSprite(markImg, Vanilla("checkbox"), Gold);
            markImg.preserveAspect = true;
            markImg.color = CheckYellow;
            markImg.raycastTarget = false;

            TextMeshProUGUI label = Label(go, "Label", 18f, false);
            label.text = text;
            label.color = interactable ? TextColor : Muted;
            Place(label.rectTransform, 0f, 0f, 1f, 1f, 42f, 0f, 0f, 0f);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            Toggle toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = markImg;
            toggle.SetIsOnWithoutNotify(value);
            toggle.interactable = interactable;
            toggle.onValueChanged.AddListener(on => changed(on));
            return go;
        }

        // The two local item lists (Store/ItemLists.cs): wide buttons side by side at the top, they open the list in
        // the display filter menu. The count in the text is updated when the list closes again.
        private static TextMeshProUGUI _groundListText;
        private static TextMeshProUGUI _keepListText;

        private static void AddItemListButtons()
        {
            var row = NewUi("ItemLists", _content);
            AddLayout(row.gameObject, 44f);
            Button ground = MakeButton(row, "GroundList", "", () => OpenItemList(ItemListKind.GroundIgnore));
            Place(ground.transform as RectTransform, 0f, 0f, 0.5f, 1f, 0f, 0f, -6f, 0f);
            Button keep = MakeButton(row, "KeepList", "", () => OpenItemList(ItemListKind.KeepInBag));
            Place(keep.transform as RectTransform, 0.5f, 0f, 1f, 1f, 6f, 0f, 0f, 0f);
            _groundListText = ground.GetComponentInChildren<TextMeshProUGUI>(true);
            _keepListText = keep.GetComponentInChildren<TextMeshProUGUI>(true);
            RefreshItemListButtons();
        }

        private static void RefreshItemListButtons()
        {
            if (_groundListText != null)
                _groundListText.text = Loc.T("Items left on the ground", "Am Boden lassen")
                    + " (" + ItemLists.Count(ItemListKind.GroundIgnore) + ")";
            if (_keepListText != null)
                _keepListText.text = Loc.T("Items kept in inventory", "Im Inventar behalten")
                    + " (" + ItemLists.Count(ItemListKind.KeepInBag) + ")";
        }

        /// <summary>F10 hides while the list is open and comes back when it closes.</summary>
        private static void OpenItemList(ItemListKind list)
        {
            if (!IsOpen || _root == null)
                return;
            _root.SetActive(false);
            DisplayTypeMenu.OpenList(list, () =>
            {
                if (IsOpen && _root != null)
                {
                    _root.SetActive(true);
                    RefreshItemListButtons();
                }
            });
            if (!DisplayTypeMenu.IsOpen && _root != null)
                _root.SetActive(true);
        }

        /// <summary>Label on the left, one key button on the right (local hotkeys, saved right away).</summary>
        private static void AddHotkeyRow(string text, ConfigEntry<KeyboardShortcut> entry, string name)
        {
            var go = NewUi("Hotkey_" + entry.Definition.Key, _content);
            AddLayout(go.gameObject, 40f);
            TextMeshProUGUI label = Label(go, "Label", 18f, false);
            label.text = text;
            label.color = TextColor;
            Place(label.rectTransform, 0f, 0f, 1f, 1f, 4f, 0f, -150f, 0f);
            MakeKeyButton(go, Pair(name, entry), new Vector4(-146f, 4f, -6f, 36f), 140f);
        }

        internal static Button MakeButton(RectTransform parent, string name, string text, UnityEngine.Events.UnityAction onClick)
        {
            var rt = NewUi(name, parent, typeof(Image), typeof(Button));
            SetSprite(rt.GetComponent<Image>(), Vanilla("button") ?? UiAssets.BtnApply, new Color(0.35f, 0.24f, 0.13f, 1f));
            Button btn = rt.GetComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();
            btn.onClick.AddListener(onClick);

            TextMeshProUGUI label = Label(rt, "Text", 20f, false);
            label.text = text;
            label.color = Gold;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return btn;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Vanilla UI sprite by name (same names Jotunn's GUIManager uses). Scans once and
        /// again only while something is still missing. Null = not found → caller falls back.
        /// Also used by DisplayTypeMenu's Vanilla style.
        /// </summary>
        internal static Sprite Vanilla(string name)
        {
            Sprite sprite;
            if (VanillaSprites.TryGetValue(name, out sprite) && sprite != null)
                return sprite;

            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < all.Length; i++)
            {
                Sprite s = all[i];
                if (s == null || string.IsNullOrEmpty(s.name))
                    continue;
                if (!VanillaSprites.ContainsKey(s.name))
                    VanillaSprites[s.name] = s;
            }
            if (VanillaSprites.TryGetValue(name, out sprite) && sprite != null)
                return sprite;

            Plugin.Log.LogDebug("F10 panel: vanilla sprite '" + name + "' not found, using fallback.");
            return null;
        }

        internal static void SetSprite(Image img, Sprite sprite, Color fallback)
        {
            if (img == null)
                return;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                img.color = Color.white;
                UiStyle.Lit(img);
            }
            else
            {
                img.sprite = null;
                img.color = fallback;
            }
        }

        /// <summary>
        /// Parent first, components after: under the inactive root no Awake / OnEnable runs
        /// before Slider / Toggle / TMP_InputField are wired up.
        /// </summary>
        internal static RectTransform NewUi(string name, Transform parent, params System.Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (components != null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == typeof(Image) && go.GetComponent<CanvasRenderer>() == null)
                        go.AddComponent<CanvasRenderer>();
                    if (go.GetComponent(components[i]) == null)
                        go.AddComponent(components[i]);
                }
            }
            return go.transform as RectTransform;
        }

        internal static TextMeshProUGUI Label(RectTransform parent, string name, float size, bool bold)
        {
            var rt = NewUi(name, parent);
            TextMeshProUGUI tmp = bold
                ? UiFonts.CreateBoldLabel(rt.gameObject, size)
                : UiFonts.CreateLabel(rt.gameObject, size);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return tmp;
        }

        internal static void AddLayout(GameObject go, float height)
        {
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null)
                le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>Anchors (min / max) plus offsets (left, bottom, right, top).</summary>
        internal static void Place(RectTransform rt, float minX, float minY, float maxX, float maxY,
            float left, float bottom, float right, float top)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }
    }

    // Vanilla treats TextInput as open: cursor free, no player input, Escape does not open the menu.
    // Also while the storage-display filter menu is open: PlayerController.TakeInput only honours
    // InventoryGui.IsVisible with a gamepad, so keyboard movement / hotkeys leaked through.
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class SettingsPanelTextInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (SettingsPanel.BlocksInput || DisplayTypeMenu.IsOpen)
                __result = true;
        }
    }

    // TextInput alone does not stop GameCamera zoom; zero the Valheim scroll axis while the panel is open.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class SettingsPanelScrollPatch
    {
        private static void Postfix(ref float __result)
        {
            if (SettingsPanel.BlocksInput || DisplayTypeMenu.IsOpen)
                __result = 0f;
        }
    }
}
