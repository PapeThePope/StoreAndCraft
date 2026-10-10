using UnityEngine;

namespace StoreAndCraft
{
    internal class ConfigSyncRetry : MonoBehaviour
    {
        private float _nextRequest;
        private int _attempts;
        private bool _loggedGiveUp;

        private void Update()
        {
            if (AdminUtil.IsServer())
            {
                enabled = false;
                return;
            }

            if (VersionGate.ClientVerified && ConfigSync.HasReceivedConfig)
            {
                enabled = false;
                return;
            }

            if (ZNet.instance == null)
                return;

            if (Time.time < _nextRequest)
                return;

            _attempts++;
            _nextRequest = Time.time + 2f;

            // Handshake already done: only re-request config. Re-sending Hello every
            // tick spammed "handshake acknowledged" while config stayed missing.
            if (!VersionGate.ClientVerified)
                VersionGate.SendHello();
            else if (ConfigSync.Registered && !ConfigSync.HasReceivedConfig)
                ConfigSync.RequestConfigFromServer();

            if (_attempts == 1 || _attempts % 10 == 0)
            {
                Plugin.Log.LogDebug(
                    "StoreAndCraft: waiting for server" +
                    (VersionGate.ClientVerified ? "" : " handshake") +
                    (ConfigSync.HasReceivedConfig ? "" : " config") +
                    " (attempt " + _attempts + ")...");
            }

            if (!_loggedGiveUp && _attempts >= 30 && !ConfigSync.HasReceivedConfig)
            {
                _loggedGiveUp = true;
                if (VersionGate.ClientVerified)
                {
                    ConfigSync.AcceptLocalFallback(
                        "handshake ok but no config package after " + _attempts + " attempts; " + ConfigSync.Describe());
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "StoreAndCraft: still no server config after " + _attempts +
                        " attempts. Install the same StoreAndCraft version on the dedicated" +
                        " server, then" +
                        " reconnect. Handshake can succeed while config sync fails.");
                    ConfigSync.AcceptLocalFallback("no server handshake/config");
                }
                enabled = false;
            }
        }
    }
}
