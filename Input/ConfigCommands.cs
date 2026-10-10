using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class ConfigCommands
    {
        public const string RpcAdminCmd = "KAC_AdminCmd";
        public const string RpcAdminResult = "KAC_AdminResult";
        private static bool _rpcRegistered;

        /// <summary>
        /// Session override for ground auto-intake. null = follow AutoIntakeEnabled config.
        /// Anyone can toggle via /store enable|disable without admin.
        /// </summary>
        private static bool? _sessionAutoIntake;

        public static bool IsAutoIntakeActive()
        {
            if (Plugin.Settings == null)
                return false;
            if (_sessionAutoIntake.HasValue)
                return _sessionAutoIntake.Value;
            return Plugin.Settings.AutoIntakeEnabled.Value;
        }

        public static void RegisterRpc()
        {
            if (_rpcRegistered || ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.Register<string>(RpcAdminCmd, RPC_AdminCmd);
            ZRoutedRpc.instance.Register<string>(RpcAdminResult, RPC_AdminResult);
            _rpcRegistered = true;
        }

        public static bool TryHandleChat(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string text = raw.Trim();
            if (!text.StartsWith("/", StringComparison.Ordinal))
                return false;

            if (!IsOurCommand(text))
                return false;

            Run(text);
            return true;
        }

        public static void OnConsole(Terminal.ConsoleEventArgs args)
        {
            if (args == null || args.Length < 1)
            {
                ShowHelp();
                return;
            }

            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < args.Length; i++)
                parts.Add(args[i]);
            Run("/" + string.Join(" ", parts.ToArray()));
        }

        public static void ShowHelp()
        {
            foreach (string line in HelpLines())
                PrintLine(line);

            Player player = Player.m_localPlayer;
            if (player != null)
                player.Message(
                    MessageHud.MessageType.Center,
                    Loc.T("StoreAndCraft help printed in console (F5).", "StoreAndCraft-Hilfe in der Konsole (F5)."),
                    0, null, false);
        }

        private static bool IsOurCommand(string text)
        {
            string lower = text.ToLowerInvariant();
            if (lower == "/help store" || lower.StartsWith("/help store "))
                return true;
            if (lower == "/help storeandcraft" || lower.StartsWith("/help storeandcraft "))
                return true;
            return lower.StartsWith("/storerange")
                || lower.StartsWith("/dumprange")
                || lower.StartsWith("/craftrange")
                || lower.StartsWith("/storagerange")
                || lower.StartsWith("/autofillrange")
                || lower.StartsWith("/autofillchestrange")
                || lower.StartsWith("/scarecrowrange")
                || lower.StartsWith("/scarecrowchestrange")
                || lower.StartsWith("/feedtroughrange")
                || lower.StartsWith("/catchup") // SAC-CATCHUP (also /catchuphours)
                || lower.StartsWith("/torchautofill")
                || lower.StartsWith("/sac")
                || lower == "/store"
                || lower.StartsWith("/store ")
                || lower == "/storehelp"
                || lower.StartsWith("/storehelp ");
        }

        private static void Run(string text)
        {
            string[] parts = Split(text);
            if (parts.Length == 0)
            {
                ShowHelp();
                return;
            }

            string cmd = parts[0].TrimStart('/').ToLowerInvariant();

            // Help — anyone can view
            if (IsHelpRequest(cmd, parts))
            {
                ShowHelp();
                return;
            }

            if (cmd == "sac" && parts.Length >= 2 && parts[1].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Tell(StatusText());
                PrintLine(StatusText());
                return;
            }

            // Ground auto-intake toggle — anyone (local). Host/admin also saves + syncs config.
            if (cmd == "store")
            {
                HandleStoreToggle(parts);
                return;
            }

            if (!CanIssue())
            {
                Tell("Only the host / server admin can change StoreAndCraft config.");
                return;
            }

            if (AdminUtil.IsServer())
            {
                string result;
                bool ok = TryApply(parts, out result);
                if (ok)
                    SaveAndSync();
                Tell(result);
                PrintLine(result);
                return;
            }

            if (ZRoutedRpc.instance == null)
            {
                Tell("Not connected.");
                return;
            }

            // Dump/store ranges are enforced on the CLIENT. Apply locally first so the
            // change is immediate, then let the server save + sync as source of truth.
            string localResult;
            if (TryApply(parts, out localResult))
            {
                Tell(localResult);
                PrintLine(localResult);
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(VersionGate.ServerPeerId(), RpcAdminCmd, text);
        }

        private static void RPC_AdminResult(long sender, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            // Console (F5) + log only: the server answers every saved value with the full range list, which was a
            // long yellow message per change on F10 Save. The F10 panel shows the values; chat commands already got
            // their short message from the local apply.
            PrintLine(message);
        }

        private static void HandleStoreToggle(string[] parts)
        {
            if (Plugin.Settings == null)
            {
                Tell("Settings not loaded.");
                return;
            }

            string sub = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "status";

            if (sub == "status" || sub == "?")
            {
                string msg = AutoIntakeStatusText();
                Tell(msg);
                PrintLine(msg);
                return;
            }

            bool enable;
            if (sub == "enable" || sub == "on" || sub == "1" || sub == "true")
                enable = true;
            else if (sub == "disable" || sub == "off" || sub == "0" || sub == "false")
                enable = false;
            else
            {
                Tell("Usage: /store enable | /store disable | /store status");
                PrintLine("Usage: /store enable | /store disable | /store status");
                return;
            }

            _sessionAutoIntake = enable;

            string result;
            if (CanIssue())
            {
                Plugin.Settings.AutoIntakeEnabled.Value = enable;
                // Clear session so everyone follows the saved config after sync.
                _sessionAutoIntake = null;
                SaveAndSync();
                result = enable
                    ? "Ground auto-store ON (saved + synced). Middle-click / dump unchanged."
                    : "Ground auto-store OFF (saved + synced). Middle-click / dump still work.";
            }
            else
            {
                result = enable
                    ? "Ground auto-store ON for you (session). Middle-click / dump unchanged."
                    : "Ground auto-store OFF for you (session). Drops stay on the ground; middle-click / dump still work.";
            }

            Tell(result);
            PrintLine(result);
        }

        private static string AutoIntakeStatusText()
        {
            bool active = IsAutoIntakeActive();
            bool cfg = Plugin.Settings != null && Plugin.Settings.AutoIntakeEnabled.Value;
            string scope = _sessionAutoIntake.HasValue ? "session" : "config";
            return "Ground auto-store: " + (active ? "ON" : "OFF")
                + " (" + scope + "; config=" + (cfg ? "on" : "off") + ")"
                + " | StoreEnabled=" + (Plugin.Settings != null && Plugin.Settings.StoreEnabled.Value ? "on" : "off");
        }

        private static bool IsHelpRequest(string cmd, string[] parts)
        {
            if (cmd == "storehelp")
                return true;
            if (cmd == "sac" && parts.Length >= 2 && parts[1].Equals("help", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cmd == "help" && parts.Length >= 2
                && (parts[1].Equals("store", StringComparison.OrdinalIgnoreCase)
                    || parts[1].Equals("storeandcraft", StringComparison.OrdinalIgnoreCase)
                    || parts[1].Equals("sac", StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private static void RPC_AdminCmd(long sender, string text)
        {
            if (!AdminUtil.IsServer() || string.IsNullOrWhiteSpace(text))
                return;

            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
            if (!ConfigSync.PeerIsAdmin(peer))
            {
                Plugin.Log.LogWarning("Ignored config command from non-admin peer " + sender);
                return;
            }

            string[] parts = Split(text);
            if (IsHelpRequest(parts[0].TrimStart('/').ToLowerInvariant(), parts))
                return;

            string result;
            if (!TryApply(parts, out result))
            {
                Plugin.Log.LogInfo("Admin cmd failed: " + result);
                if (ZRoutedRpc.instance != null)
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminResult, result);
                return;
            }

            SaveAndSync();
            ConfigSync.SendToPeer(sender);
            if (ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminResult, result + " | " + StatusText());
            Plugin.Log.LogInfo("Admin cmd OK from " + sender + ": " + result);
        }

        internal static bool CanEditRanges()
        {
            return CanIssue();
        }

        /// <summary>
        /// F10 panel Save: same path as the chat commands (host saves + syncs; admin client
        /// applies locally and the server re-checks the adminlist per command).
        /// </summary>
        internal static string ApplyFromPanel(IList<KeyValuePair<string, float>> changes)
        {
            if (changes == null || changes.Count == 0)
                return Loc.T("Nothing changed.", "Nichts geändert.");
            if (!CanIssue())
                return Loc.T("Only the host / server admin can change ranges.", "Nur Host / Server-Admin darf Reichweiten ändern.");

            bool server = AdminUtil.IsServer();
            if (!server && ZRoutedRpc.instance == null)
                return Loc.T("Not connected.", "Nicht verbunden.");

            int ok = 0;
            string error = null;
            for (int i = 0; i < changes.Count; i++)
            {
                string text = changes[i].Key + " "
                    + changes[i].Value.ToString("0.##", CultureInfo.InvariantCulture);
                string result;
                if (!TryApply(Split(text), out result))
                {
                    error = result;
                    continue;
                }
                ok++;
                if (!server)
                    ZRoutedRpc.instance.InvokeRoutedRPC(VersionGate.ServerPeerId(), RpcAdminCmd, text);
            }

            if (server && ok > 0)
                SaveAndSync();

            string msg = server
                ? Loc.T("Saved", "Gespeichert") + " (" + ok + ")"
                : Loc.T("Sent to server", "An Server gesendet") + " (" + ok + ")";
            return error != null ? msg + " | " + error : msg;
        }

        private static bool CanIssue()
        {
            ZNet znet = ZNet.instance;
            if (znet == null)
                return true;
            if (znet.IsServer())
                return true;
            if (ConfigSync.ServerGrantedEdit)
                return true;
            try
            {
                return znet.LocalPlayerIsAdminOrHost() || LocalIsInAdminList(znet);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valheim's client check compares "Steam_<id>" with the list, so an adminlist.txt entry that is just the
        /// plain id (what the server accepts) is not found. Look for both spellings in the list the server sent.
        /// PlatformUserID lives in Splatform.dll (not referenced here) → reflection.
        /// </summary>
        private static bool LocalIsInAdminList(ZNet znet)
        {
            try
            {
                System.Collections.Generic.List<string> list = znet.GetAdminList();
                if (list == null || list.Count == 0)
                    return false;
                System.Reflection.MethodInfo getUser = typeof(UserInfo).GetMethod("GetLocalUser");
                object user = getUser != null ? getUser.Invoke(null, null) : null;
                System.Reflection.FieldInfo idField = user != null ? user.GetType().GetField("UserId") : null;
                object id = idField != null ? idField.GetValue(user) : null;
                if (id == null)
                    return false;
                string full = id.ToString();
                System.Reflection.FieldInfo rawField = id.GetType().GetField("m_userID");
                string raw = rawField != null ? rawField.GetValue(id) as string : null;
                for (int i = 0; i < list.Count; i++)
                {
                    string e = (list[i] ?? "").Trim();
                    if (e.Length == 0)
                        continue;
                    if (string.Equals(e, full, System.StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(raw) && string.Equals(e, raw, System.StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool TryApply(string[] parts, out string message)
        {
            message = null;
            if (Plugin.Settings == null)
            {
                message = "Settings not loaded.";
                return false;
            }

            string key;
            string valueToken;
            if (!ResolveKeyValue(parts, out key, out valueToken))
            {
                message = "Unknown command. Type: /help store";
                return false;
            }

            // SAC-CATCHUP: on/off switch rides the same admin path as the ranges.
            if (key == "catchup" || key == "torchautofill" || key == "torchautofilloverride" || BoolEntryFor(key) != null)
            {
                string t = valueToken.ToLowerInvariant();
                if (t == "on" || t == "true" || t == "enable")
                    valueToken = "1";
                else if (t == "off" || t == "false" || t == "disable")
                    valueToken = "0";
            }

            float value;
            if (!float.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.TryParse(valueToken, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                message = "Invalid number: " + valueToken;
                return false;
            }

            if (key == "catchup")
            {
                bool on = value >= 0.5f;
                if (Plugin.Settings.CatchUpEnabled.Value != on)
                {
                    Plugin.Settings.CatchUpEnabled.Value = on;
                    SmelterCatchUp.OnSwitched(on);
                }
                message = "CatchUpWhileAway = " + (on ? "on" : "off") + " (saved + synced)";
                ActivityLog.Note(Loc.T("Config", "Config"), message);
                return true;
            }

            if (key == "torchautofill")
            {
                bool on = value >= 0.5f;
                Plugin.Settings.TorchAutoFillDefault.Value = on;
                message = "TorchAutoFillDefault = " + (on ? "on" : "off") + " (saved + synced)";
                ActivityLog.Note(Loc.T("Config", "Config"), message);
                return true;
            }

            if (key == "torchautofilloverride")
            {
                bool on = value >= 0.5f;
                Plugin.Settings.TorchAutoFillOverride.Value = on;
                message = "TorchAutoFillOverride = " + (on ? "on" : "off") + " (saved + synced)";
                ActivityLog.Note(Loc.T("Config", "Config"), message);
                return true;
            }

            // F10 on/off switches for the other synced gameplay settings (same admin path).
            ConfigEntry<bool> boolEntry = BoolEntryFor(key);
            if (boolEntry != null)
            {
                bool on = value >= 0.5f;
                boolEntry.Value = on;
                // /store enable|disable is a session override — the saved value must win again.
                if (key == "autointake")
                    _sessionAutoIntake = null;
                message = boolEntry.Definition.Key + " = " + (on ? "on" : "off") + " (saved + synced)";
                ActivityLog.Note(Loc.T("Config", "Config"), message);
                return true;
            }

            if (key == "intakeinterval" && (value < 1f || value > 60f))
            {
                message = "Auto-store interval must be between 1 and 60 seconds.";
                return false;
            }

            if (key == "catchuphours" && (value < 0.5f || value > 48f))
            {
                message = "Catch-up hours must be between 0.5 and 48.";
                return false;
            }

            if (IsStationCapKey(key))
            {
                float min = key == "fermenterbatch" ? 1f : 0f;
                if (value < min || value > ModConfig.MaxStationCap)
                {
                    message = "Station capacity must be between " + min.ToString("0", CultureInfo.InvariantCulture)
                        + " and " + ModConfig.MaxStationCap.ToString("0", CultureInfo.InvariantCulture) + ".";
                    return false;
                }
                value = Mathf.Round(value);
            }

            if (value < 0f || value > ModConfig.MaxRange)
            {
                message = "Range must be between 0 and " + ModConfig.MaxRange.ToString("0", CultureInfo.InvariantCulture) + ".";
                return false;
            }

            ConfigEntry<float> entry = EntryFor(key);
            if (entry == null)
            {
                message = "Unknown setting: " + key;
                return false;
            }

            entry.Value = value;
            message = key + " = " + value.ToString("0.##", CultureInfo.InvariantCulture) + " (saved + synced)";
            ActivityLog.Note(Loc.T("Config", "Config"), message);
            return true;
        }

        private static bool ResolveKeyValue(string[] parts, out string key, out string valueToken)
        {
            key = null;
            valueToken = null;
            if (parts == null || parts.Length == 0)
                return false;

            string cmd = parts[0].TrimStart('/').ToLowerInvariant();

            if (cmd == "storerange" || cmd == "dumprange" || cmd == "craftrange" || cmd == "storagerange" || cmd == "autofillrange"
                || cmd == "autofillchestrange" || cmd == "scarecrowrange" || cmd == "scarecrowchestrange"
                || cmd == "feedtroughrange" || cmd == "intakeinterval"
                || cmd == "catchup" || cmd == "catchuphours" || cmd == "torchautofill" || cmd == "torchautofilloverride"
                || BoolEntryFor(cmd) != null
                || IsStationCapKey(cmd)) // F10 station capacities + fermenter batch
            {
                if (parts.Length < 2)
                    return false;
                key = cmd;
                valueToken = parts[1];
                return true;
            }

            if (cmd == "sac")
            {
                if (parts.Length < 3)
                    return false;
                string sub = parts[1].ToLowerInvariant();
                if (sub == "store" || sub == "storerange")
                    key = "storerange";
                else if (sub == "dump" || sub == "dumprange")
                    key = "dumprange";
                else if (sub == "craft" || sub == "craftrange")
                    key = "craftrange";
                else if (sub == "storage" || sub == "storagerange")
                    key = "storagerange";
                else if (sub == "autofill" || sub == "autofillrange")
                    key = "autofillrange";
                else if (sub == "autofillchest" || sub == "autofillchestrange")
                    key = "autofillchestrange";
                else if (sub == "scarecrow" || sub == "scarecrowrange")
                    key = "scarecrowrange";
                else if (sub == "scarecrowchest" || sub == "scarecrowchestrange")
                    key = "scarecrowchestrange";
                else if (sub == "feedtrough" || sub == "feedtroughrange")
                    key = "feedtroughrange";
                else if (sub == "intake" || sub == "intakeinterval")
                    key = "intakeinterval";
                else if (sub == "catchup" || sub == "catchuphours" || sub == "torchautofill" || sub == "torchautofilloverride")
                    key = sub;
                else
                    return false;
                valueToken = parts[2];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Synced on/off settings the F10 panel can switch (admin path). ModEnabled / LockConfig
        /// are left out on purpose: ModEnabled off also disables the F10 key (lock-out).
        /// catchup / torchautofill keep their own branches above.
        /// </summary>
        internal static ConfigEntry<bool> BoolEntryFor(string key)
        {
            if (Plugin.Settings == null || string.IsNullOrEmpty(key))
                return null;
            switch (key.ToLowerInvariant())
            {
                case "storeenabled": return Plugin.Settings.StoreEnabled;
                case "autointake": return Plugin.Settings.AutoIntakeEnabled;
                case "musthaveexisting": return Plugin.Settings.MustHaveExisting;
                case "autostack": return Plugin.Settings.AutoStackEnabled;
                case "craftenabled": return Plugin.Settings.CraftEnabled;
                case "leaveone": return Plugin.Settings.LeaveOneItem;
                case "feedtroughenabled": return Plugin.Settings.FeedTroughEnabled;
                case "armorstandswap": return Plugin.Settings.ArmorStandSwap;
                case "automationloadedarea": return Plugin.Settings.AutomationLoadedArea;
                default: return null;
            }
        }

        private static bool IsStationCapKey(string key)
        {
            switch (key)
            {
                case "kilnmax":
                case "smeltermaxore":
                case "smeltermaxfuel":
                case "blastmaxore":
                case "blastmaxfuel":
                case "eitrmaxore":
                case "eitrmaxfuel":
                case "beehivemax":
                case "fermenterbatch":
                    return true;
                default:
                    return false;
            }
        }

        internal static ConfigEntry<float> EntryFor(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "storerange":
                    return Plugin.Settings.StoreRange;
                case "dumprange":
                    return Plugin.Settings.PlayerDumpRange;
                case "craftrange":
                    return Plugin.Settings.CraftRange;
                case "storagerange":
                    return Plugin.Settings.StorageRange;
                case "autofillrange":
                    return Plugin.Settings.AutoFillRange;
                case "autofillchestrange":
                    return Plugin.Settings.AutoFillChestRange;
                case "scarecrowrange":
                    return Plugin.Settings.ScarecrowRange;
                case "scarecrowchestrange":
                    return Plugin.Settings.ScarecrowChestRange;
                case "feedtroughrange":
                    return Plugin.Settings.FeedTroughRange;
                case "catchuphours":
                    return Plugin.Settings.CatchUpMaxHours;
                case "intakeinterval":
                    return Plugin.Settings.IntakeInterval;
                // Station capacities (0 = vanilla) + fermenter batch.
                case "kilnmax":
                    return Plugin.Settings.KilnMaxWood;
                case "smeltermaxore":
                    return Plugin.Settings.SmelterMaxOre;
                case "smeltermaxfuel":
                    return Plugin.Settings.SmelterMaxCoal;
                case "blastmaxore":
                    return Plugin.Settings.BlastFurnaceMaxOre;
                case "blastmaxfuel":
                    return Plugin.Settings.BlastFurnaceMaxCoal;
                case "eitrmaxore":
                    return Plugin.Settings.EitrRefineryMaxInput;
                case "eitrmaxfuel":
                    return Plugin.Settings.EitrRefineryMaxSap;
                case "beehivemax":
                    return Plugin.Settings.BeehiveMaxHoney;
                case "fermenterbatch":
                    return Plugin.Settings.FermenterBatch;
                default:
                    return null;
            }
        }

        // SAC-CATCHUP: server tick keeps CatchUpSince in step and saves (sync runs via SettingChanged).
        internal static void SaveConfigFile()
        {
            ConfigWatch.SuppressReload(2f);
            try
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.Config.Save();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Config save (catch-up): " + ex.Message);
            }
        }

        private static void SaveAndSync()
        {
            ConfigWatch.SuppressReload(2f);
            try
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.Config.Save();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Config save after command: " + ex.Message);
            }

            if (AdminUtil.IsServer())
                ConfigSync.BroadcastConfig();
        }

        private static string[] Split(string text)
        {
            return text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string[] HelpLines()
        {
            return new[]
            {
                "========== StoreAndCraft ==========",
                "Chat (anyone):",
                "  /help store                 — this help",
                "  /store enable|disable       — ground auto-store on/off (middle-click stays)",
                "  /store status               — ground auto-store state",
                "  /sac status                 — show current ranges",
                "Chat (host/admin):",
                "  /dumprange <m>              — dump / middle-click range",
                "  /storerange <m>             — auto-store range (ground → chest)",
                "  /storagerange <m>           — take-stack / search / displays",
                "  /craftrange <m>             — craft / build / station [E] pull",
                "  /autofillrange <m>          - auto-fill: player to station",
                "  /autofillchestrange <m>     - auto-fill: station to chest (0 = same as autofillrange)",
                "  /scarecrowrange <m>         - scarecrow: player to scarecrow (0 = same as autofillrange)",
                "  /scarecrowchestrange <m>    - scarecrow: scarecrow to chest (0 = same as autofillchestrange)",
                "  /feedtroughrange <m>        - feed trough range",
                "  /sac dump|store|storage|craft|autofill|autofillchest|feedtrough <m>",
                "  /catchup on|off             - smelters catch up time while nobody was near (B + N)",
                "  /catchuphours <h>           - most hours one station catches up (0.5-48)",
                "  /torchautofill on|off       - torches placed from now on start with auto-fill on",
                "  /torchautofilloverride on|off - World Override: auto-fill on every torch, placed ones too",
                "F10: panel with activity log checkbox + range sliders (Save).",
                "Console (F5), same ideas:",
                "  help store   |  sac help  |  sac status",
                "  store enable |  store disable |  store status",
                "  dumprange 50 |  storerange 50 |  craftrange 50 |  storagerange 50 |  autofillrange 40",
                "Host /store enable|disable also saves AutoIntakeEnabled and syncs.",
                "==================================="
            };
        }

        private static string StatusText()
        {
            if (Plugin.Settings == null)
                return "No settings.";
            return "Dump=" + Plugin.Settings.PlayerDumpRange.Value
                + " Store=" + Plugin.Settings.StoreRange.Value
                + " Storage=" + Plugin.Settings.StorageRange.Value
                + " Craft=" + Plugin.Settings.CraftRange.Value
                + " AutoFill=" + Plugin.Settings.AutoFillRange.Value
                + " AutoFillChest=" + Plugin.Settings.AutoFillChestReach()
                + " Scarecrow=" + Plugin.Settings.ScarecrowReach()
                + " ScarecrowChest=" + Plugin.Settings.ScarecrowChestReach()
                + " AutoIntake=" + (IsAutoIntakeActive() ? "on" : "off")
                + " CatchUp=" + (Plugin.Settings.CatchUpEnabled.Value ? "on " + Plugin.Settings.CatchUpMaxHours.Value + "h" : "off")
                + " Lock=" + Plugin.Settings.LockConfig.Value;
        }

        private static void Tell(string msg)
        {
            Player player = Player.m_localPlayer;
            if (player != null)
                player.Message(MessageHud.MessageType.Center, msg, 0, null, false);
            else
                Plugin.Log.LogInfo(msg);
        }

        private static void PrintLine(string line)
        {
            if (Console.instance != null)
                Console.instance.Print(line);
            Plugin.Log.LogInfo(line);
        }
    }

    [HarmonyPatch(typeof(Chat), "InputText")]
    internal static class ChatConfigCommandPatch
    {
        private static readonly System.Reflection.FieldInfo InputField =
            AccessTools.Field(typeof(Chat), "m_input");

        private static bool Prefix(Chat __instance)
        {
            if (__instance == null)
                return true;

            string text = ReadInputText(__instance);
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (!ConfigCommands.TryHandleChat(text))
                return true;

            ClearInputText(__instance);
            return false;
        }

        private static string ReadInputText(Chat chat)
        {
            object input = InputField != null ? InputField.GetValue(chat) : null;
            if (input == null)
                return null;
            var prop = AccessTools.Property(input.GetType(), "text");
            if (prop != null)
                return prop.GetValue(input, null) as string;
            var field = AccessTools.Field(input.GetType(), "text");
            return field != null ? field.GetValue(input) as string : null;
        }

        private static void ClearInputText(Chat chat)
        {
            object input = InputField != null ? InputField.GetValue(chat) : null;
            if (input == null)
                return;
            var prop = AccessTools.Property(input.GetType(), "text");
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(input, "", null);
                return;
            }
            var field = AccessTools.Field(input.GetType(), "text");
            if (field != null)
                field.SetValue(input, "");
        }
    }
}
