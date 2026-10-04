using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Soft-depend Epic Loot inventory provider so the enchanting table (Sacrifice /
    /// Enchant / Upgrade / …) sees and spends nearby chest items. EL's UI does not
    /// go through vanilla InventoryGui / CountItems, so StationHover patches miss it.
    /// </summary>
    internal static class EpicLootBridge
    {
        private const string EpicLootGuid = "randyknapp.mods.epicloot";
        private static readonly List<ItemDrop.ItemData> ItemScratch = new List<ItemDrop.ItemData>(64);
        private static bool _tried;
        private static bool _registered;

        public static void TryRegister()
        {
            if (_tried)
                return;
            _tried = true;

            if (!Chainloader.PluginInfos.ContainsKey(EpicLootGuid))
                return;

            try
            {
                Type api = FindApiType();
                if (api == null)
                {
                    Plugin.Log.LogDebug("Epic Loot present but API type not found.");
                    return;
                }

                MethodInfo register = api.GetMethod(
                    "RegisterInventoryProvider",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(string),
                        typeof(Func<List<ItemDrop.ItemData>>),
                        typeof(Func<string, int>),
                        typeof(Func<string, int, int>),
                        typeof(Func<ItemDrop.ItemData, int, int>)
                    },
                    null);

                if (register == null)
                {
                    Plugin.Log.LogWarning(
                        "Epic Loot found, but RegisterInventoryProvider is missing. Update Epic Loot.");
                    return;
                }

                object ok = register.Invoke(
                    null,
                    new object[]
                    {
                        Plugin.ModGuid,
                        (Func<List<ItemDrop.ItemData>>)GetNearbyItems,
                        (Func<string, int>)CountNearbyItem,
                        (Func<string, int, int>)RemoveNearbyItem,
                        (Func<ItemDrop.ItemData, int, int>)RemoveNearbyExactItem
                    });

                _registered = ok is bool b && b;
                if (_registered)
                    Plugin.Log.LogDebug("Registered chest inventory provider with Epic Loot.");
                else
                    Plugin.Log.LogDebug("Epic Loot RegisterInventoryProvider returned false.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Epic Loot inventory provider failed: " + ex.Message);
            }
        }

        public static void TryUnregister()
        {
            if (!_registered)
                return;

            try
            {
                Type api = FindApiType();
                MethodInfo unregister = api?.GetMethod(
                    "UnregisterInventoryProvider",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                unregister?.Invoke(null, new object[] { Plugin.ModGuid });
            }
            catch
            {
            }

            _registered = false;
        }

        private static Type FindApiType()
        {
            Type direct = Type.GetType("EpicLoot.API, EpicLoot", throwOnError: false);
            if (direct != null)
                return direct;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;
                string name = assembly.GetName().Name;
                if (name != "EpicLoot" && name != "EpicLootAPI")
                    continue;
                Type type = assembly.GetType("EpicLoot.API");
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool Ready()
        {
            return StagingPull.Active;
        }

        /// <summary>Live ItemData instances from nearby craft-range chests.</summary>
        private static List<ItemDrop.ItemData> GetNearbyItems()
        {
            ItemScratch.Clear();
            if (!Ready())
                return ItemScratch;

            Player player = Player.m_localPlayer;
            if (player == null)
                return ItemScratch;

            float range = StationFeed.ActivePullRange();
            if (range <= 0f)
                return ItemScratch;

            Vector3 origin = player.transform.position;
            foreach (Container chest in NearbyIndex.Current)
            {
                if (chest == null || ChestNames.BlocksCraft(chest))
                    continue;
                if (ContainerFilter.Distance(origin, chest.transform.position) > range)
                    continue;

                // Cooldown inside EnsureInventory — safe while EL UI refreshes.
                NearbyIndex.EnsureInventory(chest);
                Inventory inv = chest.GetInventory();
                if (inv == null)
                    continue;

                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                {
                    if (item?.m_shared == null || item.m_stack <= 0)
                        continue;
                    ItemScratch.Add(item);
                }
            }

            return ItemScratch;
        }

        private static int CountNearbyItem(string sharedName)
        {
            if (!Ready() || string.IsNullOrEmpty(sharedName))
                return 0;

            Player player = Player.m_localPlayer;
            if (player == null)
                return 0;

            // No LeaveOne: a single Runestone Rare per chest would otherwise count as 0.
            // Enchanting is an explicit spend; craft/smelter still use LeaveOne elsewhere.
            return NearbyIndex.CountItem(
                player.transform.position,
                0f,
                sharedName,
                leaveOne: false);
        }

        private static int RemoveNearbyItem(string sharedName, int amount)
        {
            if (!Ready() || amount <= 0 || string.IsNullOrEmpty(sharedName))
                return 0;

            Player player = Player.m_localPlayer;
            if (player == null)
                return 0;

            return ConsumeFromChestsNoLeaveOne(player, sharedName, amount);
        }

        private static int ConsumeFromChestsNoLeaveOne(Player player, string shared, int amount)
        {
            NearbyIndex.Tick();
            float range = StationFeed.ActivePullRange();
            Vector3 origin = player.transform.position;
            int need = amount;
            int took = 0;

            foreach (Container chest in NearbyIndex.Current)
            {
                if (need <= 0)
                    break;
                if (chest == null || ChestNames.BlocksCraft(chest))
                    continue;
                if (ContainerFilter.Distance(origin, chest.transform.position) > range)
                    continue;

                int n = TransferService.Consume(chest, shared, need, leaveOne: false);
                if (n <= 0)
                    continue;
                ActivityLog.Move("Epic Loot", ActivityLog.Chest(chest), n, shared, ActivityLog.Used());
                took += n;
                need -= n;
            }

            return took;
        }

        private static int RemoveNearbyExactItem(ItemDrop.ItemData item, int amount)
        {
            if (!Ready() || item == null || amount <= 0)
                return 0;

            Player player = Player.m_localPlayer;
            if (player == null)
                return 0;

            float range = StationFeed.ActivePullRange();
            Vector3 origin = player.transform.position;

            foreach (Container chest in NearbyIndex.Current)
            {
                if (chest == null || ChestNames.BlocksCraft(chest))
                    continue;
                if (ContainerFilter.Distance(origin, chest.transform.position) > range)
                    continue;

                Inventory inv = chest.GetInventory();
                if (inv == null || !inv.ContainsItem(item))
                    continue;

                int n = TransferService.ConsumeExact(chest, item, amount);
                ActivityLog.Move("Epic Loot", ActivityLog.Chest(chest), n, item.m_shared != null ? item.m_shared.m_name : null, ActivityLog.Used());
                return n;
            }

            return 0;
        }
    }
}
