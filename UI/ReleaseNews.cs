using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// After an update Hugin visits once and tells what is new in this StoreAndCraft version.
    /// Uses the vanilla tutorial path (Tutorial.m_texts + Player.ShowTutorial): the raven comes,
    /// talking to him marks the key as seen on the character, and SAC also stores the version in
    /// its local config so other characters do not get the same news again. Update the text below
    /// with every release (only NEW FEATURES, short).
    /// </summary>
    internal static class ReleaseNews
    {
        private const float DelayAfterSpawn = 8f;
        private static float _showAt;

        // One key per minor series (1.4.x): a bug-fix release does not bring Hugin again with the same news.
        private static string Key
        {
            get
            {
                string[] v = Plugin.ModVersion.Split(new[] { (char)46 });
                return "sac_news_" + (v.Length >= 2 ? v[0] + "." + v[1] + ".0" : Plugin.ModVersion);
            }
        }

        private static string Topic => "StoreAndCraft " + Plugin.ModVersion;

        private static string Text => Loc.T(
            "New in this version:\n"
            + "Displays:\n"
            + "- Carved storage displays with their own layouts and hammer icons\n"
            + "- Sign displays get layouts too, columns can fill bottom-up\n"
            + "- Right click an item: the camera shows the chest that holds it\n"
            + "Stations:\n"
            + "- Feed trough: animal food filter and auto-fill\n"
            + "- Fermenter: mead filter and batches of the same base\n"
            + "- Sap extractor: auto-store puts the sap into chests\n"
            + "- Linked stations show in red what their chests ran out of\n"
            + "- Station capacities in the settings menu (admin)\n"
            + "Farm:\n"
            + "- Scarecrow: plants and harvests a field grid, buttons to level and cultivate the ground\n"
            + "Chests:\n"
            + "- New button Only stations in the chest settings\n"
            + "Other:\n"
            + "- Armor stand: swaps your armor, presets let weapon, shield and tool swap too\n"
            + "- Filters only list items you have already discovered\n"
            + "\n"
            + "New: mod manager built in, press F10",
            "Neu in dieser Version:\n"
            + "Displays:\n"
            + "- Geschnitzte Storage Displays mit eigenen Layouts und Hammer-Icons\n"
            + "- Schild-Displays haben jetzt auch Layouts, Säulen füllen sich von unten\n"
            + "- Rechtsklick auf ein Item: die Kamera zeigt die Kiste mit dem Item\n"
            + "Stationen:\n"
            + "- Futtertrog: Futter-Filter und Auto-Fill\n"
            + "- Fermenter: Met-Filter und Chargen derselben Basis\n"
            + "- Sap-Extraktor: Auto-Lagern legt den Sap in Kisten\n"
            + "- Gelinkte Stationen zeigen rot, was in ihren Kisten fehlt\n"
            + "- Stations-Maxima im Einstellungsmenü (Admin)\n"
            + "Farm:\n"
            + "- Vogelscheuche: bepflanzt und erntet ein Feld-Raster, Knöpfe zum Einebnen und Bestellen\n"
            + "Kisten:\n"
            + "- Neuer Knopf Nur Stationen in den Kisten-Einstellungen\n"
            + "Sonstiges:\n"
            + "- Rüstungsständer: tauscht deine Rüstung, mit Vorlagen tauschen auch Waffe, Schild und Werkzeug\n"
            + "- Filter zeigen nur Items, die du schon entdeckt hast\n"
            + "\n"
            + "Neu: Mod-Manager eingebaut, drücke F10");

        private static bool AlreadySeen()
        {
            ModConfig c = Plugin.Settings;
            return c == null || !c.ShowReleaseNews.Value || c.ReleaseNewsSeen.Value == Plugin.ModVersion;
        }

        /// <summary>Player spawned: schedule the visit a few seconds later (not during the load screen).</summary>
        internal static void OnSpawned(Player player)
        {
            if (player == null || player != Player.m_localPlayer || AlreadySeen())
                return;
            if (player.HaveSeenTutorial(Key))
            {
                MarkSeen();
                return;
            }
            _showAt = Time.time + DelayAfterSpawn;
        }

        /// <summary>Plugin.Update: one float compare while nothing is scheduled.</summary>
        internal static void Tick()
        {
            if (_holdGo != null)
                TickHold();
            if (_showAt <= 0f || Time.time < _showAt)
                return;
            _showAt = 0f;
            Show();
        }

        // ---- the news stay on screen until you press Esc or walk away (vanilla: gone after 10 s)

        private const float NewsScale = 1.8f;
        private static GameObject _holdGo;
        private static int _blockPauseFrame = -10;
        private static readonly System.Reflection.FieldInfo NpcTexts = AccessTools.Field(typeof(Chat), "m_npcTexts");

        /// <summary>Raven.Say postfix: the bubble with our news gets no time limit.</summary>
        internal static void AfterRavenSay(Raven raven, string topic)
        {
            if (raven == null || topic != Topic || Chat.instance == null || NpcTexts == null)
                return;
            var list = NpcTexts.GetValue(Chat.instance) as System.Collections.Generic.List<Chat.NpcText>;
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].m_go == raven.gameObject)
                {
                    list[i].m_timeout = false;
                    // The long text is shrunk to fit the small vanilla bubble: scale the whole bubble up.
                    if (list[i].m_gui != null)
                        list[i].m_gui.transform.localScale = Vector3.one * NewsScale;
                    _holdGo = raven.gameObject;
                    return;
                }
            }
        }

        private static void TickHold()
        {
            // Gone: closed with E on Hugin, walked away (the game clears the bubble 20 m out) or Hugin left.
            if (_holdGo == null || Chat.instance == null || !Chat.instance.IsDialogVisible(_holdGo))
            {
                _holdGo = null;
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape, true))
            {
                Chat.instance.ClearNpcText(_holdGo);
                _holdGo = null;
                _blockPauseFrame = Time.frameCount; // the same Escape must not open the pause menu
            }
        }

        internal static bool ShouldBlockPause()
        {
            return Time.frameCount <= _blockPauseFrame + 1;
        }

        private static void Show()
        {
            Player player = Player.m_localPlayer;
            if (player == null || Tutorial.instance == null || AlreadySeen())
                return;
            string key = Key;
            if (!Tutorial.instance.m_texts.Exists(t => t.m_name == key))
            {
                Tutorial.instance.m_texts.Add(new Tutorial.TutorialText
                {
                    m_name = key,
                    m_topic = Topic,
                    m_text = Text,
                    // Also listed in the compendium, readable later.
                    m_label = Topic
                });
            }
            player.ShowTutorial(key);
        }

        /// <summary>
        /// Console "sacnews": Hugin comes again with the news of this version, also when it was seen before
        /// (forgets the seen mark on the character and in the config, rebuilds the text).
        /// </summary>
        internal static void ShowAgain(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null || Tutorial.instance == null)
                return;
            System.Reflection.FieldInfo shown = AccessTools.Field(typeof(Player), "m_shownTutorials");
            var set = shown != null ? shown.GetValue(player) as System.Collections.Generic.HashSet<string> : null;
            if (set != null)
                set.Remove(Key);
            string key = Key;
            Tutorial.instance.m_texts.RemoveAll(t => t.m_name == key);
            if (Plugin.Settings != null)
            {
                Plugin.Settings.ShowReleaseNews.Value = true;
                Plugin.Settings.ReleaseNewsSeen.Value = "";
            }
            _showAt = Time.time + 1f;
            player.Message(MessageHud.MessageType.Center,
                Loc.T("Hugin is on his way...", "Hugin ist unterwegs..."), 0, null, false);
        }

        /// <summary>Raven.Talk postfix: the player listened → never again for this version.</summary>
        internal static void AfterRavenTalk()
        {
            Player player = Player.m_localPlayer;
            if (player != null && player.HaveSeenTutorial(Key))
                MarkSeen();
        }

        private static void MarkSeen()
        {
            if (Plugin.Settings != null && Plugin.Settings.ReleaseNewsSeen.Value != Plugin.ModVersion)
                Plugin.Settings.ReleaseNewsSeen.Value = Plugin.ModVersion;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class ReleaseNewsSpawnPatch
    {
        private static void Postfix(Player __instance)
        {
            ReleaseNews.OnSpawned(__instance);
        }
    }

    [HarmonyPatch(typeof(Raven), "Say")]
    internal static class ReleaseNewsRavenSayPatch
    {
        private static void Postfix(Raven __instance, string topic)
        {
            ReleaseNews.AfterRavenSay(__instance, topic);
        }
    }

    [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
    internal static class ReleaseNewsPausePatch
    {
        private static bool Prefix()
        {
            return !ReleaseNews.ShouldBlockPause();
        }
    }

    [HarmonyPatch(typeof(Raven), "Talk")]
    internal static class ReleaseNewsRavenTalkPatch
    {
        private static void Postfix()
        {
            ReleaseNews.AfterRavenTalk();
        }
    }
}
