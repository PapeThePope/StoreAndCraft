using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Sort / Stack buttons on the bag and chest panels (favorites, equipped, hotbar stay put in the bag).
    /// </summary>
    internal static class InventorySort
    {
        private static readonly FieldInfo CurrentContainer =
            AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        /// <summary>Inventory buttons: act on one panel, even when the other one is open too.</summary>
        public static void SortBag()
        {
            Player player = ButtonPlayer();
            if (player != null)
                SortPlayerInventory(player);
        }

        public static void SortChest()
        {
            Player player = ButtonPlayer();
            Container open = player != null ? GetOpenContainer() : null;
            if (open != null)
                SortOpenChest(player, open);
        }

        public static void StackBag()
        {
            Player player = ButtonPlayer();
            Inventory inv = player != null ? player.GetInventory() : null;
            if (inv == null)
                return;
            int merged = StackInventory(inv, lockFavorites: true, lockEquipped: true, lockHotbar: true);
            ActivityLog.Note(Loc.T("Stack", "Stapeln"), ActivityLog.Bag() + " (" + merged + ")");
        }

        public static void StackChest()
        {
            Player player = ButtonPlayer();
            Container chest = player != null ? GetOpenContainer() : null;
            if (!ChestWritable(chest))
                return;
            int merged = StackInventory(chest.GetInventory(), lockFavorites: false, lockEquipped: false, lockHotbar: false);
            ContainerFilter.SaveInventory(chest);
            ActivityLog.Note(Loc.T("Stack", "Stapeln"), ActivityLog.Chest(chest) + " (" + merged + ")");
        }

        private static Player ButtonPlayer()
        {
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value || !InventoryGui.IsVisible())
                return null;
            return Player.m_localPlayer;
        }

        private static bool ChestWritable(Container chest)
        {
            if (chest == null || ChestNames.IsFullyIgnored(chest))
                return false;
            ZNetView nv = Refs.View(chest);
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return false;
            return ContainerFilter.TryReadyForWrite(chest) && chest.GetInventory() != null;
        }

        /// <summary>Merge partial stacks of the same item (same rules as sort), no reordering. Returns stacks freed.</summary>
        private static int StackInventory(Inventory inv, bool lockFavorites, bool lockEquipped, bool lockHotbar)
        {
            if (inv == null)
                return 0;
            var movable = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null || item.m_stack <= 0)
                    continue;
                if (!PlayerBag.IsSortLocked(item, lockFavorites, lockEquipped, lockHotbar, inv))
                    movable.Add(item);
            }

            CompactStacks(movable);

            int freed = 0;
            for (int i = movable.Count - 1; i >= 0; i--)
            {
                if (movable[i] != null && movable[i].m_stack <= 0)
                {
                    inv.RemoveItem(movable[i]);
                    freed++;
                }
            }
            Refs.NotifyChanged(inv);
            return freed;
        }

        private static void SortPlayerInventory(Player player)
        {
            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            SortInventory(inv, lockFavorites: true, lockEquipped: true, lockHotbar: true);
            ActivityLog.Note(Loc.T("Sort", "Sortieren"), ActivityLog.Bag());
        }

        private static void SortOpenChest(Player player, Container chest)
        {
            if (chest == null || ChestNames.IsFullyIgnored(chest))
                return;

            ZNetView nv = Refs.View(chest);
            if (nv == null || !nv.IsValid())
                return;

            if (!nv.IsOwner())
                return;

            // Open chests are owned by the viewer — still guard against stale empty Load.
            if (!ContainerFilter.TryReadyForWrite(chest))
                return;

            Inventory inv = chest.GetInventory();
            if (inv == null)
                return;

            SortInventory(inv, lockFavorites: false, lockEquipped: false, lockHotbar: false);
            ContainerFilter.SaveInventory(chest);
            ActivityLog.Note(Loc.T("Sort", "Sortieren"), ActivityLog.Chest(chest));
        }

        /// <summary>
        /// Compact stacks then pack into free slots. Locked items keep their grid positions.
        /// </summary>
        private static int SortInventory(Inventory inv, bool lockFavorites, bool lockEquipped, bool lockHotbar)
        {
            if (inv == null)
                return 0;

            int width = inv.GetWidth();
            int height = inv.GetHeight();
            if (width <= 0 || height <= 0)
                return 0;

            var all = new List<ItemDrop.ItemData>(inv.GetAllItems());
            var locked = new HashSet<ItemDrop.ItemData>();
            var lockedSlots = new HashSet<Vector2i>();
            var movable = new List<ItemDrop.ItemData>();

            foreach (ItemDrop.ItemData item in all)
            {
                if (item == null || item.m_shared == null || item.m_stack <= 0)
                    continue;

                if (PlayerBag.IsSortLocked(item, lockFavorites, lockEquipped, lockHotbar, inv))
                {
                    locked.Add(item);
                    lockedSlots.Add(item.m_gridPos);
                }
                else
                {
                    movable.Add(item);
                }
            }

            if (movable.Count == 0)
                return 0;

            CompactStacks(movable);

            // Drop emptied stacks from compacting.
            for (int i = movable.Count - 1; i >= 0; i--)
            {
                if (movable[i] == null || movable[i].m_stack <= 0)
                {
                    if (movable[i] != null)
                        inv.RemoveItem(movable[i]);
                    movable.RemoveAt(i);
                }
            }

            if (movable.Count == 0)
            {
                Refs.NotifyChanged(inv);
                return 0;
            }

            movable.Sort(CompareItems);

            var freeSlots = new List<Vector2i>();
            for (int y = 0; y < height; y++)
            {
                // Player hotbar is row 0 - never pack sorted bag items into empty hotbar slots.
                if (lockHotbar && y == 0)
                    continue;
                // Wider/Deeper Pockets rows (y >= 4) are normal bag — include them.
                for (int x = 0; x < width; x++)
                {
                    var slot = new Vector2i(x, y);
                    if (lockedSlots.Contains(slot))
                        continue;
                    freeSlots.Add(slot);
                }
            }

            int n = Mathf.Min(movable.Count, freeSlots.Count);
            int changed = 0;
            for (int i = 0; i < n; i++)
            {
                ItemDrop.ItemData item = movable[i];
                Vector2i target = freeSlots[i];
                if (item.m_gridPos.x != target.x || item.m_gridPos.y != target.y)
                {
                    item.m_gridPos = target;
                    changed++;
                }
            }

            Refs.NotifyChanged(inv);
            return Mathf.Max(changed, movable.Count > 0 ? 1 : 0);
        }

        private static void CompactStacks(List<ItemDrop.ItemData> items)
        {
            items.Sort((a, b) => b.m_stack.CompareTo(a.m_stack));
            foreach (ItemDrop.ItemData dest in items)
            {
                if (dest == null || dest.m_shared == null || dest.m_stack <= 0)
                    continue;
                if (StackLimits.MaxStack(dest) <= 1)
                    continue;

                int room = StackLimits.RoomInStack(dest);
                if (room <= 0)
                    continue;

                foreach (ItemDrop.ItemData src in items)
                {
                    if (room <= 0)
                        break;
                    if (src == null || src == dest || src.m_stack <= 0)
                        continue;
                    if (!SameStackType(dest, src))
                        continue;
                    // Only a partial stack gives up its items (a full one would just swap with this one).
                    if (StackLimits.RoomInStack(src) <= 0)
                        continue;

                    int move = Mathf.Min(room, src.m_stack);
                    if (move <= 0)
                        continue;
                    dest.m_stack += move;
                    src.m_stack -= move;
                    room -= move;
                }
            }
        }

        private static bool SameStackType(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            if (a?.m_shared == null || b?.m_shared == null)
                return false;
            return a.m_shared.m_name == b.m_shared.m_name
                && a.m_quality == b.m_quality
                && a.m_worldLevel == b.m_worldLevel;
        }

        private static int CompareItems(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            string na = DisplayName(a);
            string nb = DisplayName(b);
            int c = string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
                return c;
            c = a.m_quality.CompareTo(b.m_quality);
            if (c != 0)
                return c;
            return b.m_stack.CompareTo(a.m_stack);
        }

        private static string DisplayName(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return "";
            try
            {
                if (Localization.instance != null)
                    return Localization.instance.Localize(item.m_shared.m_name);
            }
            catch
            {
            }
            return item.m_shared.m_name ?? "";
        }

        private static Container GetOpenContainer()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
                return null;

            try
            {
                if (!gui.IsContainerOpen())
                    return null;
            }
            catch
            {
                // Fall through to field check.
            }

            if (CurrentContainer != null)
                return CurrentContainer.GetValue(gui) as Container;

            return null;
        }
    }
}
