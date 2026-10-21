using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Restock key (F10 "Keep in inventory" list): items marked "Fill" are brought up to their amount from nearby
    /// chests. Same chest rules as "Take stack" and the build grab: Ignore and Only-stations chests are never touched,
    /// Only-build/craft chests are used, "1 item stays" applies. The carry weight is not a limit here (the player asked
    /// for the items); a full bag gets one message.
    /// </summary>
    internal static class Restock
    {
        public static void Run()
        {
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value || !Plugin.Settings.StoreEnabled.Value)
                return;
            Player player = Player.m_localPlayer;
            if (player == null)
                return;
            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            List<KeyValuePair<string, int>> targets = ItemLists.RestockTargets();
            if (targets.Count == 0)
                return;

            NearbyIndex.Tick();
            float range = NearbyIndex.AccessRange();
            bool leaveOne = RequirementBridge.LeaveOneInChests(player);
            bool full = false;

            InventoryCountPatches.Skip++;
            TransferService.IgnoreWeight++;
            try
            {
                for (int t = 0; t < targets.Count; t++)
                {
                    string shared = targets[t].Key;
                    int need = targets[t].Value - inv.CountItems(shared, -1, true);
                    if (need <= 0)
                        continue;

                    ItemDrop.ItemData probe = Probe(shared);
                    foreach (Container chest in ChestPicker.FindHolding(player.transform.position, range, shared))
                    {
                        if (need <= 0)
                            break;
                        if (chest == null || ChestNames.BlocksCraft(chest))
                            continue;
                        if (probe != null && !inv.CanAddItem(probe, 1))
                        {
                            full = true;
                            break;
                        }

                        int got = TransferService.WithdrawForGrab(chest, shared, need, player, leaveOne);
                        if (got <= 0)
                            continue;
                        need -= got;
                        ActivityLog.Move(Loc.T("Restock", "Auffüllen"), ActivityLog.Chest(chest), got, shared, ActivityLog.Bag());
                    }

                    if (need > 0 && probe != null && !inv.CanAddItem(probe, 1))
                        full = true;
                }
            }
            finally
            {
                TransferService.IgnoreWeight--;
                InventoryCountPatches.Skip--;
            }

            Refs.NotifyChanged(inv);
            if (full)
                player.Message(MessageHud.MessageType.Center, Loc.T("Inventory full.", "Inventar voll."), 0, null, false);
        }

        /// <summary>One item of that kind, to ask the bag whether it still has room for it.</summary>
        private static ItemDrop.ItemData Probe(string shared)
        {
            GameObject prefab = ItemIds.PrefabFromToken(shared);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData != null ? drop.m_itemData.Clone() : null;
        }
    }
}
