using HarmonyLib;

namespace StoreAndCraft
{
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance)
        {
            TransferService.RegisterOn(__instance);
            NearbyIndex.Register(__instance);
            if (__instance.GetComponent<ChestRenameReceiver>() == null)
                __instance.gameObject.AddComponent<ChestRenameReceiver>();
        }
    }

    // Container has no OnDestroy in Valheim — dead entries are pruned in NearbyIndex.

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class NewConnectionPatch
    {
        private static void Postfix(ZNetPeer peer)
        {
            if (peer == null)
                return;
            VersionGate.RegisterOn(peer.m_rpc);
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartPatch
    {
        private static void Postfix(Game __instance)
        {
            VersionGate.ResetClient();
            ConfigSync.Register();
            TransferService.RegisterGrant();
            ConfigCommands.RegisterRpc();
            NearbyIndex.BootstrapExisting();
            StationAutoFill.BootstrapExisting();
            VersionGate.SendHello();

            if (__instance != null && __instance.GetComponent<ConfigSyncRetry>() == null)
                __instance.gameObject.AddComponent<ConfigSyncRetry>();
            if (__instance != null && __instance.GetComponent<VersionGate>() == null)
                __instance.gameObject.AddComponent<VersionGate>();
        }
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class PeerInfoPatch
    {
        private static void Postfix(ZRpc rpc)
        {
            if (rpc == null || ZNet.instance == null)
                return;

            if (ZNet.instance.IsServer())
            {
                ZNetPeer peer = VersionGate.PeerFor(rpc);
                if (peer == null)
                    return;
                VersionGate.OnPeerReady(peer.m_uid);
                ConfigSync.SendToPeer(peer.m_uid);
                return;
            }

            VersionGate.SendHello(rpc);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), typeof(ZNetPeer))]
    internal static class DisconnectPatch
    {
        private static void Postfix(ZNetPeer peer)
        {
            if (peer != null)
                VersionGate.Forget(peer.m_uid);
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerSpawnedPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;

            // Chests load with the world, after Game.Start — re-index here.
            NearbyIndex.BootstrapExisting();

            if (AdminUtil.IsServer())
                return;
            VersionGate.SendHello();
            if (!ConfigSync.HasReceivedConfig)
                ConfigSync.RequestConfigFromServer();
        }
    }

    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class TerminalInitPatch
    {
        private static bool _added;

        private static void Postfix()
        {
            if (_added)
                return;
            _added = true;
            new Terminal.ConsoleCommand(
                "storesearch",
                "Find an item in nearby containers",
                SearchPing.OnCommand,
                false, false, false, false, false, false, null, false, false, false);

            // Admin config — onlyAdmin=true (last bool in this overload)
            new Terminal.ConsoleCommand(
                "sac",
                "StoreAndCraft admin: sac <dump|store|storage|craft|status|help> [meters]",
                ConfigCommands.OnConsole,
                false, false, false, false, false, false, null, false, false, true);

            new Terminal.ConsoleCommand(
                "store",
                "StoreAndCraft: store enable|disable|status — ground auto-store (middle-click stays).",
                ConfigCommands.OnConsole,
                false, false, false, false, false, false, null, false, false, false);

            new Terminal.ConsoleCommand(
                "storerange",
                "StoreAndCraft: set auto-store range (meters). Host/admin.",
                ConfigCommands.OnConsole,
                false, false, false, false, false, false, null, false, false, true);

            new Terminal.ConsoleCommand(
                "dumprange",
                "StoreAndCraft: set dump range (meters). Host/admin.",
                ConfigCommands.OnConsole,
                false, false, false, false, false, false, null, false, false, true);

            new Terminal.ConsoleCommand(
                "craftrange",
                "StoreAndCraft: set craft/build pull range (meters). Host/admin.",
                ConfigCommands.OnConsole,
                false, false, false, false, false, false, null, false, false, true);

            new Terminal.ConsoleCommand(
                "storagerange",
                "StoreAndCraft: set storage/display range (meters). Host/admin.",
                ConfigCommands.OnConsole,
                false, false, false, false, false, false, null, false, false, true);

            new Terminal.ConsoleCommand(
                "sacnews",
                "StoreAndCraft: Hugin brings the news of this version again",
                ReleaseNews.ShowAgain,
                false, false, false, false, false, false, null, false, false, false);

            new Terminal.ConsoleCommand(
                "sacstation",
                "StoreAndCraft: what the station you look at is made of (for support)",
                StationInfo.OnCommand,
                false, false, false, false, false, false, null, false, false, false);

            new Terminal.ConsoleCommand(
                "storehelp",
                "StoreAndCraft: list admin chat/console commands",
                args => ConfigCommands.ShowHelp(),
                false, false, false, false, false, false, null, false, false, false);
        }
    }

    /// <summary>
    /// Console (F5): accept "help store" and slash forms like "/dumprange 50".
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "TryRunCommand")]
    internal static class TerminalHelpStorePatch
    {
        private static bool Prefix(ref string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            string raw = text.Trim();
            string t = raw.ToLowerInvariant();

            if (t == "help store" || t == "help storeandcraft" || t == "help sac"
                || t == "/help store" || t == "/help storeandcraft" || t == "/help sac")
            {
                ConfigCommands.ShowHelp();
                return false;
            }

            // F5 users often type chat-style "/dumprange 50" — strip slash for our commands.
            if (raw.StartsWith("/", System.StringComparison.Ordinal)
                && ConfigCommands.TryHandleChat(raw))
                return false;

            return true;
        }
    }
}
