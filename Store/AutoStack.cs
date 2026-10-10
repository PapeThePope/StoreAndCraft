using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class AutoStack
    {
        private static float _next;

        // Only a chest whose contents changed can have something new to merge: Container.OnContainerChanged
        // (every change, also loads and other players' edits) marks it, the pass only looks at marked chests.
        // Everything in range is looked at once after switching on / loading (seed) and when the list overflows.
        private static readonly HashSet<Container> Changed = new HashSet<Container>();
        private static bool _seedAll = true;
        private const int MaxChanged = 4096;

        internal static void MarkChanged(Container chest)
        {
            if (chest == null)
                return;
            if (Changed.Count >= MaxChanged)
            {
                Changed.Clear();
                _seedAll = true;
                return;
            }
            Changed.Add(chest);
        }

        public static void Tick()
        {
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value || !Plugin.Settings.StoreEnabled.Value
                || !Plugin.Settings.AutoStackEnabled.Value)
            {
                // Off: nothing to remember; switched on again, every chest is looked at once.
                Changed.Clear();
                _seedAll = true;
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || player.IsTeleporting())
                return;

            if (Time.time < _next)
                return;
            _next = Time.time + Mathf.Max(1f, Plugin.Settings.IntakeInterval.Value);

            float range = Mathf.Max(Plugin.Settings.StoreReach(), Plugin.Settings.StorageReach());
            NearbyIndex.Tick();
            bool seed = _seedAll;
            _seedAll = false;
            foreach (Container chest in NearbyIndex.Within(player.transform.position, range))
            {
                if (chest == null)
                    continue;
                if (!seed && !Changed.Contains(chest))
                    continue;
                ZNetView nv = Refs.View(chest);
                if (nv == null || !nv.IsOwner())
                {
                    MarkChanged(chest); // tried again when we own it
                    continue;
                }
                // Look at the chest as it is first: no load / save / redraw for a chest with nothing to merge.
                if (!HasMergeable(chest.GetInventory()))
                {
                    Changed.Remove(chest);
                    continue;
                }
                if (!ContainerFilter.TryReadyForWrite(chest))
                {
                    MarkChanged(chest); // not loaded yet: tried again next pass
                    continue;
                }
                Changed.Remove(chest);
                Inventory chestInv = chest.GetInventory();
                if (chestInv == null || chestInv.NrOfItems() <= 0)
                    continue;
                int merged = CompactInventory(chestInv, false);
                if (merged > 0)
                    ActivityLog.Note(Loc.T("Auto-stack", "Auto-Stapeln"),
                        ActivityLog.Chest(chest) + " (" + merged + "x " + Loc.T("merged", "zusammengelegt") + ")");
            }
        }

        private static int CompactInventory(Inventory inv, bool skipHotbarAndEquipped)
        {
            if (inv == null)
                return 0;
            int merged = 0;

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            items.Sort((a, b) => b.m_stack.CompareTo(a.m_stack));

            foreach (ItemDrop.ItemData dest in items)
            {
                if (dest == null || dest.m_shared == null || dest.m_stack <= 0)
                    continue;
                if (StackLimits.MaxStack(dest) <= 1)
                    continue;
                if (skipHotbarAndEquipped && SkipPlayerItem(dest))
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
                    // Only a partial stack gives up its items. Taking from a full stack just swaps
                    // "9/50" and "50/50" with each other every pass (nothing is saved, the chest is rewritten).
                    if (StackLimits.RoomInStack(src) <= 0)
                        continue;
                    if (skipHotbarAndEquipped && SkipPlayerItem(src))
                        continue;

                    int move = Mathf.Min(room, src.m_stack);
                    if (move <= 0)
                        continue;
                    dest.m_stack += move;
                    src.m_stack -= move;
                    room -= move;
                    merged += move;
                }
            }

            int removed = 0;
            var leftover = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (ItemDrop.ItemData item in leftover)
            {
                if (item != null && item.m_stack <= 0)
                {
                    inv.RemoveItem(item);
                    removed++;
                }
            }

            // Only a real change is reported (it saves the chest and redraws displays).
            if (merged > 0 || removed > 0)
                Refs.NotifyChanged(inv);
            return merged;
        }

        private struct StackKind : System.IEquatable<StackKind>
        {
            public string Name;
            public int Quality;
            public int WorldLevel;

            public bool Equals(StackKind o) { return Quality == o.Quality && WorldLevel == o.WorldLevel && string.Equals(Name, o.Name); }
            public override bool Equals(object o) { return o is StackKind && Equals((StackKind)o); }
            public override int GetHashCode() { return ((Name != null ? Name.GetHashCode() : 0) * 31 + Quality) * 31 + WorldLevel; }
        }

        private static readonly HashSet<StackKind> PartialScratch = new HashSet<StackKind>();

        /// <summary>True when two partial stacks of the same kind (name, quality, world level) could be merged.</summary>
        private static bool HasMergeable(Inventory inv)
        {
            if (inv == null || inv.NrOfItems() < 2)
                return false;
            PartialScratch.Clear();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null || item.m_stack <= 0)
                    continue;
                if (StackLimits.MaxStack(item) <= 1 || StackLimits.RoomInStack(item) <= 0)
                    continue;
                if (!PartialScratch.Add(new StackKind { Name = item.m_shared.m_name, Quality = item.m_quality, WorldLevel = item.m_worldLevel }))
                {
                    PartialScratch.Clear();
                    return true;
                }
            }
            PartialScratch.Clear();
            return false;
        }

        private static bool SkipPlayerItem(ItemDrop.ItemData item)
        {
            if (item == null || item.m_equipped)
                return true;
            return PlayerBag.IsDumpProtected(item);
        }

        private static bool SameStackType(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            if (a?.m_shared == null || b?.m_shared == null)
                return false;
            return a.m_shared.m_name == b.m_shared.m_name
                && a.m_quality == b.m_quality
                && a.m_worldLevel == b.m_worldLevel;
        }
    }
}
