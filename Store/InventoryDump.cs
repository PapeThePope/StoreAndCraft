using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class InventoryDump
    {
        public static void DumpNearby()
        {
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value || !Plugin.Settings.StoreEnabled.Value)
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            NearbyIndex.Tick();
            // Valheim reloads every loaded chest within a second of any change (Container.CheckForChanges), so only
            // a chest whose view is still empty (raid / unloaded area: never loaded) needs a forced load here.
            // The chest that takes an item is refreshed again right before the write (TryReadyForWrite).
            float range = Plugin.Settings.DumpReach();
            LoadUnknownViews(player.transform.position, range);

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            int stored = 0;
            var leftovers = new List<ItemDrop.ItemData>();

            foreach (ItemDrop.ItemData item in items)
            {
                if (!ShouldDump(item, inv))
                    continue;

                Container chest = ChestPicker.FindStoreTarget(
                    player.transform.position,
                    item,
                    Plugin.Settings.MustHaveExisting.Value);

                if (chest == null)
                {
                    leftovers.Add(item);
                    continue;
                }

                int fit = ChestPicker.AmountThatFits(chest.GetInventory(), item);
                if (fit <= 0)
                {
                    leftovers.Add(item);
                    continue;
                }

                int stackBefore = item.m_stack;
                string shared = item.m_shared != null ? item.m_shared.m_name : null;
                if (TransferService.StoreItem(chest, inv, item, fit)
                    && (!inv.ContainsItem(item) || item.m_stack < stackBefore))
                {
                    stored++;
                    int moved = stackBefore - (inv.ContainsItem(item) ? item.m_stack : 0);
                    ActivityLog.Move(Loc.T("Dump", "Dump"), ActivityLog.Bag(), moved, shared, ActivityLog.Chest(chest));
                }
                else
                    leftovers.Add(item);
            }


            player.Message(
                MessageHud.MessageType.TopLeft,
                stored > 0
                    ? Loc.T("Stored " + stored + " stacks.", stored + " Stapel eingelagert.")
                    : Loc.T("Nothing to store.", "Nichts einzulagern."),
                0, null, false);
        }

        public static bool StoreOne(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (player == null || item == null || Plugin.Settings == null)
                return false;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return false;

            if (Favorites.IsFavorite(item))
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    Loc.T("Favorite — protected from store. Press F to unfavorite.", "Favorit — vor Einlagern geschützt. F zum Entfernen."),
                    0, null, false);
                return false;
            }

            NearbyIndex.Tick();
            float range = Plugin.Settings.DumpReach();
            LoadUnknownViews(player.transform.position, range);

            Container chest = ChestPicker.FindStoreTarget(
                player.transform.position,
                item,
                Plugin.Settings.MustHaveExisting.Value);

            if (chest == null)
            {
                player.Message(MessageHud.MessageType.Center, Loc.T("No matching chest.", "Keine passende Truhe."), 0, null, false);
                return false;
            }

            int fit = ChestPicker.AmountThatFits(chest.GetInventory(), item);
            if (fit <= 0)
            {
                player.Message(MessageHud.MessageType.Center, Loc.T("No matching chest.", "Keine passende Truhe."), 0, null, false);
                return false;
            }

            int before = item.m_stack;
            string shared = item.m_shared != null ? item.m_shared.m_name : null;
            bool ok = TransferService.StoreItem(chest, inv, item, fit);
            if (ok)
            {
                int moved = before - (inv.ContainsItem(item) ? item.m_stack : 0);
                ActivityLog.Move(Loc.T("Store", "Einlagern"), ActivityLog.Bag(), moved, shared, ActivityLog.Chest(chest));
            }
            return ok;
        }

        private static void LoadUnknownViews(Vector3 origin, float range)
        {
            foreach (Container nearby in NearbyIndex.Within(origin, range))
            {
                Inventory view = nearby != null ? nearby.GetInventory() : null;
                NearbyIndex.EnsureInventory(nearby, force: view == null || view.NrOfItems() <= 0);
            }
        }

        public static bool ShouldDump(ItemDrop.ItemData item, Inventory inv)
        {
            if (item == null || item.m_stack <= 0)
                return false;
            if (item.m_equipped)
                return false;
            if (Favorites.IsFavorite(item))
                return false;
            if (PlayerBag.IsDumpProtected(item))
                return false;
            return true;
        }
    }
}
