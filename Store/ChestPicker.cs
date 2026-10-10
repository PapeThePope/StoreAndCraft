using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class ChestPicker
    {
        public static Container FindStoreTarget(Vector3 origin, ItemDrop.ItemData item, bool mustExist)
        {
            if (item == null)
                return null;

            float range = Plugin.Settings != null
                ? Plugin.Settings.DumpReach()
                : 8f;
            Container best = null;
            float bestDist = float.MaxValue;
            // Must hold the item already: the item index skips every chest that has none (no load, no scan).
            string shared = mustExist ? ItemIds.SharedName(item) : null;

            foreach (Container chest in NearbyIndex.Current)
            {
                if (chest == null || ChestNames.IsNoDump(chest))
                    continue;
                if (mustExist && !ItemIndex.MightHold(chest, shared))
                    continue;

                float d = ContainerFilter.Distance(origin, chest.transform.position);
                if (d > range)
                    continue;

                if (!CanAccept(chest, item, origin, mustExist, fromPlayer: true))
                    continue;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = chest;
                }
            }

            return best;
        }

        /// <param name="fromPlayer">The player's own dump / store one item (Only build/craft chests take it).</param>
        public static bool CanAccept(Container chest, ItemDrop.ItemData item, Vector3 from, bool mustExist, bool fromPlayer = false)
        {
            if (chest == null || item == null)
                return false;

            // Never dump / auto-store into crypt / house-spawn / other world chests.
            if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                return false;

            // Ignore: never. Only build/craft: nothing is put in automatically, but the player's dump may fill it.
            if (fromPlayer ? ChestNames.BlocksDump(chest) : ChestNames.BlocksDeposit(chest))
                return false;

            NearbyIndex.EnsureInventory(chest);
            Inventory inv = chest.GetInventory();
            if (inv == null)
                return false;

            if (mustExist)
            {
                string shared = ItemIds.SharedName(item);
                if (string.IsNullOrEmpty(shared) || inv.CountItems(shared, -1, true) <= 0)
                    return false;
            }

            return AmountThatFits(inv, item) > 0;
        }

        private static bool[] _cells = new bool[0];

        /// <summary>
        /// What Inventory.AddItem really manages: units that merge into a stack with the same name, quality and
        /// world level, plus a free grid cell for the rest (the rest of one drop always fits one cell).
        /// </summary>
        private static int ExactFit(Inventory inv, ItemDrop.ItemData item)
        {
            int w = inv.GetWidth();
            int h = inv.GetHeight();
            int cells = w * h;
            if (cells <= 0 || item.m_shared == null)
                return item.m_stack;
            if (_cells.Length < cells)
                _cells = new bool[cells];
            System.Array.Clear(_cells, 0, cells);

            int max = Mathf.Max(1, item.m_shared.m_maxStackSize);
            int merge = 0;
            int used = 0;
            List<ItemDrop.ItemData> all = inv.GetAllItems();
            for (int i = 0; i < all.Count; i++)
            {
                ItemDrop.ItemData it = all[i];
                if (it == null || it.m_shared == null)
                    continue;
                Vector2i p = it.m_gridPos;
                if (p.x >= 0 && p.y >= 0 && p.x < w && p.y < h)
                {
                    int idx = p.y * w + p.x;
                    if (!_cells[idx])
                    {
                        _cells[idx] = true;
                        used++;
                    }
                }
                if (max > 1 && it.m_shared.m_name == item.m_shared.m_name && it.m_quality == item.m_quality
                    && it.m_stack < it.m_shared.m_maxStackSize && (float)it.m_worldLevel == (float)item.m_worldLevel)
                    merge += it.m_shared.m_maxStackSize - it.m_stack;
            }

            bool freeCell = cells - used >= 1;
            if (max > 1)
                return merge >= item.m_stack || freeCell ? item.m_stack : merge;
            return freeCell ? item.m_stack : 0;
        }

        /// <summary>
        /// How many of this stack the chest can take (fill existing stacks, then empty slots).
        /// Partial is OK: 28 wood into a 26/50 stack stores 24 and leaves 4.
        /// </summary>
        public static int AmountThatFits(Inventory inv, ItemDrop.ItemData item)
        {
            if (inv == null || item == null || item.m_stack <= 0)
                return 0;

            int want = item.m_stack;

            // Valheim's CanAddItem counts free slots and stack space loosely (any quality, slots outside the grid).
            // AddItem is stricter and logs "Trying to add item to occupied slot" when it fails: never promise more than it can do.
            want = Mathf.Min(want, ExactFit(inv, item));
            if (want <= 0)
                return 0;

            if (inv.CanAddItem(item, want))
                return want;

            int lo = 1;
            int hi = want - 1;
            int best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (inv.CanAddItem(item, mid))
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best;
        }

        public static List<Container> FindHolding(Vector3 origin, float range, string sharedName)
        {
            var list = new List<Container>();
            if (string.IsNullOrEmpty(sharedName))
                return list;

            foreach (Container chest in NearbyIndex.Within(origin, range))
            {
                if (chest == null || ChestNames.IsFullyIgnored(chest))
                    continue;
                if (!ItemIndex.MightHold(chest, sharedName))
                    continue;

                NearbyIndex.EnsureInventory(chest);
                Inventory inv = chest.GetInventory();
                if (inv == null)
                    continue;
                if (CountShared(inv, sharedName) > 0)
                    list.Add(chest);
            }

            list.Sort((a, b) =>
                ContainerFilter.Distance(origin, a.transform.position)
                    .CompareTo(ContainerFilter.Distance(origin, b.transform.position)));
            return list;
        }

        public static int CountShared(Inventory inv, string sharedName)
        {
            if (inv == null || string.IsNullOrEmpty(sharedName))
                return 0;

            int n = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null || item.m_stack <= 0)
                    continue;
                if (item.m_shared.m_name == sharedName)
                    n += item.m_stack;
            }
            return n;
        }
    }
}
