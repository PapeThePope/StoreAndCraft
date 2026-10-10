using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Which registered chests hold an item (by shared name), so a store / take / deposit / consume search can skip
    /// every chest that cannot hold it instead of loading and scanning it. Built from the local chest inventories,
    /// the same views the searches read: a chest is re-indexed when its inventory changes (Container.OnContainerChanged:
    /// adds, removes, loads, other players' edits) and everything again every RefreshSeconds as a safety net.
    /// A prefilter only: callers still run all their normal checks on the chests it lets through.
    /// </summary>
    internal static class ItemIndex
    {
        private const float RefreshSeconds = 5f;

        private static readonly Dictionary<string, HashSet<Container>> Holders = new Dictionary<string, HashSet<Container>>();
        private static readonly Dictionary<Container, List<string>> NamesOf = new Dictionary<Container, List<string>>();
        private static readonly HashSet<Container> Dirty = new HashSet<Container>();
        private static readonly List<Container> DirtyScratch = new List<Container>(64);
        private static readonly HashSet<string> NameScratch = new HashSet<string>();
        // Name lists of chests go back here when a chest is re-indexed (the full refresh re-indexes every chest).
        private static readonly Stack<List<string>> ListPool = new Stack<List<string>>();
        private static float _fullAt = -100f;

        /// <summary>The chest's contents changed (or it was registered): re-index it before the next lookup.</summary>
        public static void MarkDirty(Container chest)
        {
            if (chest != null)
                Dirty.Add(chest);
        }

        /// <summary>The chest left the registry (destroyed / unloaded): forget it.</summary>
        public static void Remove(Container chest)
        {
            if ((object)chest == null)
                return;
            Dirty.Remove(chest);
            Unindex(chest);
        }

        /// <summary>
        /// False only when the chest surely does not hold the item: its loaded inventory has none. A chest whose
        /// local view is still empty while its ZDO has items (not loaded yet) counts as "might hold".
        /// </summary>
        public static bool MightHold(Container chest, string sharedName)
        {
            if (chest == null || string.IsNullOrEmpty(sharedName))
                return true;
            Flush();
            HashSet<Container> set;
            if (Holders.TryGetValue(sharedName, out set) && set.Contains(chest))
                return true;
            Inventory inv = chest.GetInventory();
            if (inv == null || inv.NrOfItems() == 0)
            {
                ZNetView nv = Refs.View(chest);
                return nv != null && nv.IsValid() && ContainerFilter.ZdoHasItemPayload(nv.GetZDO());
            }
            return false;
        }

        private static void Flush()
        {
            if (Time.unscaledTime >= _fullAt)
            {
                _fullAt = Time.unscaledTime + RefreshSeconds;
                foreach (Container c in NearbyIndex.AllRegistered)
                    Dirty.Add(c);
            }
            if (Dirty.Count == 0)
                return;
            DirtyScratch.Clear();
            DirtyScratch.AddRange(Dirty);
            Dirty.Clear();
            for (int i = 0; i < DirtyScratch.Count; i++)
                Reindex(DirtyScratch[i]);
            DirtyScratch.Clear();
        }

        private static void Reindex(Container chest)
        {
            Unindex(chest);
            if (chest == null)
                return;
            Inventory inv = chest.GetInventory();
            if (inv == null || inv.NrOfItems() == 0)
                return;
            NameScratch.Clear();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item != null && item.m_shared != null && item.m_stack > 0)
                    NameScratch.Add(item.m_shared.m_name);
            }
            if (NameScratch.Count == 0)
                return;
            List<string> names = ListPool.Count > 0 ? ListPool.Pop() : new List<string>(8);
            names.AddRange(NameScratch);
            NameScratch.Clear();
            NamesOf[chest] = names;
            for (int i = 0; i < names.Count; i++)
            {
                HashSet<Container> set;
                if (!Holders.TryGetValue(names[i], out set))
                {
                    set = new HashSet<Container>();
                    Holders[names[i]] = set;
                }
                set.Add(chest);
            }
        }

        private static void Unindex(Container chest)
        {
            List<string> names;
            if (!NamesOf.TryGetValue(chest, out names))
                return;
            NamesOf.Remove(chest);
            for (int i = 0; i < names.Count; i++)
            {
                HashSet<Container> set;
                if (Holders.TryGetValue(names[i], out set))
                {
                    set.Remove(chest);
                    if (set.Count == 0)
                        Holders.Remove(names[i]);
                }
            }
            names.Clear();
            if (ListPool.Count < 4096)
                ListPool.Push(names);
        }
    }
}
