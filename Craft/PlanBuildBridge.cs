using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace StoreAndCraft
{
    /// <summary>
    /// Soft compat with PlanBuild (marcopogo.PlanBuild): plan pieces take their materials through
    /// PlanPiece.StandardInventory (CountItems / HaveItem / RemoveItem) on the player's bag. For the bag only (never
    /// the Plan Totem's own inventory) the counts include the chests, and before the bag is charged the part it lacks
    /// is paid from chests: the same rules as building with the hammer (StagingPull, RequirementBridge).
    /// No reference to PlanBuild: everything is found at runtime, without PlanBuild nothing is patched.
    /// </summary>
    internal static class PlanBuildBridge
    {
        private const string Guid = "marcopogo.PlanBuild";
        private static bool _looked;
        private static System.Type _inventoryType;
        private static FieldInfo _inventoryField;

        internal static MethodInfo Method(string name)
        {
            if (!_looked)
            {
                _looked = true;
                PluginInfo info;
                if (Chainloader.PluginInfos.TryGetValue(Guid, out info) && info != null && info.Instance != null)
                    _inventoryType = info.Instance.GetType().Assembly.GetType("PlanBuild.Plans.PlanPiece+StandardInventory", false);
                _inventoryField = _inventoryType != null ? AccessTools.Field(_inventoryType, "inventory") : null;
                if (_inventoryField == null)
                    _inventoryType = null;
                if (_inventoryType != null)
                    Plugin.Log.LogInfo("PlanBuild found: plan pieces take their materials from chests too.");
            }
            return _inventoryType != null ? AccessTools.Method(_inventoryType, name) : null;
        }

        /// <summary>The local player when this PlanBuild inventory is the player's bag (not the Plan Totem).</summary>
        internal static Player BagOwner(object standardInventory)
        {
            Player player = Player.m_localPlayer;
            if (player == null || standardInventory == null || _inventoryField == null || !StagingPull.Active)
                return null;
            return _inventoryField.GetValue(standardInventory) as Inventory == player.GetInventory() ? player : null;
        }

        /// <summary>Chest stock to add to a bag count (0 when chests are already counted in this scope).</summary>
        internal static int ChestCount(Player player, string sharedName)
        {
            if (player == null || string.IsNullOrEmpty(sharedName) || InventoryCountPatches.IncludeChests > 0)
                return 0;
            return RequirementBridge.CountNearby(player, sharedName, -1);
        }
    }

    [HarmonyPatch]
    internal static class PlanBuildCountItemsPatch
    {
        private static bool Prepare()
        {
            return PlanBuildBridge.Method("CountItems") != null;
        }

        private static MethodBase TargetMethod()
        {
            return PlanBuildBridge.Method("CountItems");
        }

        private static void Postfix(object __instance, string resourceName, ref int __result)
        {
            Player player = PlanBuildBridge.BagOwner(__instance);
            if (player != null)
                __result += PlanBuildBridge.ChestCount(player, resourceName);
        }
    }

    [HarmonyPatch]
    internal static class PlanBuildHaveItemPatch
    {
        private static bool Prepare()
        {
            return PlanBuildBridge.Method("HaveItem") != null;
        }

        private static MethodBase TargetMethod()
        {
            return PlanBuildBridge.Method("HaveItem");
        }

        private static void Postfix(object __instance, string resourceName, ref bool __result)
        {
            if (__result)
                return;
            Player player = PlanBuildBridge.BagOwner(__instance);
            if (player != null)
                __result = PlanBuildBridge.ChestCount(player, resourceName) > 0;
        }
    }

    [HarmonyPatch]
    internal static class PlanBuildRemoveItemPatch
    {
        private static bool Prepare()
        {
            return PlanBuildBridge.Method("RemoveItem") != null;
        }

        private static MethodBase TargetMethod()
        {
            return PlanBuildBridge.Method("RemoveItem");
        }

        /// <summary>Pay the bag's deficit from chests; PlanBuild then removes the rest from the bag.</summary>
        private static void Prefix(object __instance, string resourceName, int amount)
        {
            Player player = PlanBuildBridge.BagOwner(__instance);
            if (player != null && player.IsOwner())
                StagingPull.PayBagDeficitFromChests(player, resourceName, amount);
        }
    }
}
