using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace StoreAndCraft
{
    /// <summary>
    /// Soft compat with PierSupports (squatch.piersupports): its extra pier segments check their cost with
    /// Inventory.CountItems and pay with Inventory.RemoveItem on the bag only (PierBuilder.CanAfford / Consume), so
    /// building from chests did not cover them. Same rules as a normal build: the check counts bag + chests (the
    /// StationHover scope SAC also puts around Player.HaveRequirements), and before the bag is charged the part the
    /// bag lacks is paid from chests (StagingPull.ConsumeRequirements, as for Player.ConsumeResources).
    /// Without PierSupports nothing is patched.
    /// </summary>
    internal static class PierSupportsBridge
    {
        private const string Guid = "squatch.piersupports";
        private static bool _looked;
        private static System.Type _builder;

        internal static MethodInfo Method(string name)
        {
            if (!_looked)
            {
                _looked = true;
                PluginInfo info;
                if (Chainloader.PluginInfos.TryGetValue(Guid, out info) && info != null && info.Instance != null)
                    _builder = info.Instance.GetType().Assembly.GetType("PierSupports.PierBuilder", false);
                if (_builder != null)
                    Plugin.Log.LogInfo("PierSupports found: pier segments are paid from chests too.");
            }
            return _builder != null ? AccessTools.Method(_builder, name) : null;
        }
    }

    /// <summary>PierBuilder.CanAfford counts bag + chests, like the build check of a normal piece.</summary>
    [HarmonyPatch]
    internal static class PierSupportsCanAffordPatch
    {
        private static bool Prepare()
        {
            return PierSupportsBridge.Method("CanAfford") != null;
        }

        private static MethodBase TargetMethod()
        {
            return PierSupportsBridge.Method("CanAfford");
        }

        private static void Prefix()
        {
            StationHover.Begin();
        }

        private static void Postfix()
        {
            StationHover.End();
        }
    }

    /// <summary>Before PierBuilder.Consume charges the bag: pay what the bag lacks from chests.</summary>
    [HarmonyPatch]
    internal static class PierSupportsConsumePatch
    {
        private static bool Prepare()
        {
            return PierSupportsBridge.Method("Consume") != null;
        }

        private static MethodBase TargetMethod()
        {
            return PierSupportsBridge.Method("Consume");
        }

        private static void Prefix(Player player, Piece piece, int count)
        {
            if (player == null || piece == null || count <= 0 || !player.IsOwner() || !StagingPull.Active)
                return;
            // Vanilla building pays ConsumeResources(piece.m_resources, 0): quality level 0 = m_amount per piece.
            StagingPull.ConsumeRequirements(player, piece.m_resources, 0, -1, count);
        }
    }
}
