namespace StoreAndCraft
{
    internal static class ConfigSync
    {
        public const string RpcSyncName = "KAC_SyncConfig";
        public const string RpcRequestName = "KAC_RequestConfig";
        public const string HandshakeName = "KAC_Hello";

        public static bool IsApplyingRemoteConfig { get; private set; }
        public static bool Registered { get; private set; }
        public static bool HasReceivedConfig { get; private set; }
        public static bool ServerGrantedEdit { get; private set; }

        public static void Register()
        {
            if (ZRoutedRpc.instance == null)
                return;

            if (!Registered)
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcSyncName, RPC_ReceiveConfig);
                ZRoutedRpc.instance.Register(RpcRequestName, RPC_RequestConfig);
                ZRoutedRpc.instance.Register<int, int>(HandshakeName, RPC_Hello);
                Registered = true;
                Plugin.Log.LogDebug("StoreAndCraft RPCs registered.");
            }

            if (AdminUtil.IsServer())
            {
                HasReceivedConfig = true;
                ServerGrantedEdit = true;
                BroadcastConfig();
            }
            else if (!HasReceivedConfig)
            {
                RequestConfigFromServer();
            }
        }

        public static void BroadcastConfig()
        {
            if (!AdminUtil.IsServer() || ZNet.instance == null || Plugin.Settings == null)
                return;

            int n = 0;
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !peer.IsReady())
                    continue;
                SendToPeer(peer.m_uid);
                n++;
            }

            if (n > 0)
                Plugin.Log.LogDebug("StoreAndCraft synced config to " + n + " peer(s).");
        }

        public static void SendToPeer(long peerId)
        {
            if (!AdminUtil.IsServer() || ZRoutedRpc.instance == null || Plugin.Settings == null)
                return;

            var peer = ZNet.instance != null ? ZNet.instance.GetPeer(peerId) : null;
            bool canEdit = !Plugin.Settings.LockConfig.Value || PeerIsAdmin(peer);
            var pkg = new ZPackage();
            pkg.Write(ModConfig.ProtocolVersion);
            pkg.Write(canEdit);
            Plugin.Settings.WriteToPackage(pkg);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcSyncName, pkg);
        }

        public static void AcceptLocalFallback(string reason)
        {
            if (HasReceivedConfig)
                return;
            HasReceivedConfig = true;
            Plugin.Log.LogWarning(
                "StoreAndCraft: using local config (" + reason + "). " +
                "Install the same StoreAndCraft version on the server if settings should sync.");
        }

        private static int _requestsSent;

        public static void RequestConfigFromServer()
        {
            if (AdminUtil.IsServer() || ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(VersionGate.ServerPeerId(), RpcRequestName);
            _requestsSent++;
        }

        /// <summary>State of the config exchange for the "no config package" warning (where it got stuck).</summary>
        internal static string Describe()
        {
            ZNetPeer server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            return "RPCs registered=" + Registered + ", requests sent=" + _requestsSent
                + ", server peer=" + (server != null ? server.m_uid.ToString() : "none")
                + ", routed rpc=" + (ZRoutedRpc.instance != null);
        }

        public static bool PeerIsAdmin(ZNetPeer peer)
        {
            if (peer?.m_rpc == null || ZNet.instance == null)
                return false;
            try
            {
                var socket = peer.m_rpc.GetSocket();
                if (socket == null)
                    return false;
                string hostName = socket.GetHostName();
                return !string.IsNullOrEmpty(hostName) && ZNet.instance.IsAdmin(hostName);
            }
            catch
            {
                return false;
            }
        }

        private static void RPC_ReceiveConfig(long sender, ZPackage pkg)
        {
            if (AdminUtil.IsServer() || pkg == null || Plugin.Settings == null)
                return;

            int version = pkg.ReadInt();
            if (version != ModConfig.ProtocolVersion)
            {
                Plugin.Log.LogWarning("StoreAndCraft config protocol mismatch: " + version
                    + " (want " + ModConfig.ProtocolVersion + ")");
                AcceptLocalFallback("protocol mismatch " + version + "/" + ModConfig.ProtocolVersion);
                Player player = Player.m_localPlayer;
                if (player != null)
                {
                    player.Message(
                        MessageHud.MessageType.Center,
                        Loc.T(
                            "StoreAndCraft version mismatch with server. Update the mod.",
                            "StoreAndCraft-Version passt nicht zum Server. Mod updaten."),
                        0, null, false);
                }
                return;
            }

            ServerGrantedEdit = pkg.ReadBool();
            IsApplyingRemoteConfig = true;
            try
            {
                Plugin.Settings.ReadFromPackage(pkg);
                HasReceivedConfig = true;
                ActivityLog.Note(Loc.T("Config", "Config"), Loc.T("server settings received", "Server-Einstellungen erhalten"));
                Plugin.Log.LogDebug("StoreAndCraft received server config. Lock="
                    + Plugin.Settings.LockConfig.Value
                    + " Dump=" + Plugin.Settings.PlayerDumpRange.Value
                    + " Store=" + Plugin.Settings.StoreRange.Value
                    + " Storage=" + Plugin.Settings.StorageRange.Value
                    + " Craft=" + Plugin.Settings.CraftRange.Value
                    + " canEdit=" + ServerGrantedEdit);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning(
                    "StoreAndCraft: failed to apply server config: " + ex.GetType().Name +
                    ": " + ex.Message + " (server/client version mismatch?)");
                // Stop the forever-wait loop; keep local config so the client stays playable.
                HasReceivedConfig = true;
            }
            finally
            {
                IsApplyingRemoteConfig = false;
            }
        }

        private static void RPC_RequestConfig(long sender)
        {
            if (!AdminUtil.IsServer())
                return;
            SendToPeer(sender);
        }

        private static void RPC_Hello(long sender, int protocol, int major)
        {
            if (!AdminUtil.IsServer())
                return;
            VersionGate.Accept(sender, protocol, major);
            SendToPeer(sender);
        }
    }
}
