using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Item totals kept across frames, so NearbyIndex.CountItem does not walk every chest once per ingredient.
    /// Per chest: same numbers as Inventory.CountItems(name, quality, true), rebuilt when that inventory
    /// changes (Inventory.Changed). Per chest set (Aggregate): the sum over chests of
    /// max(0, count - PendingChestDebit) minus one per chest for LeaveOne, exactly what the per-name loop adds
    /// up; dropped when any summarized chest changes, when counts are invalidated, or after MaxAge.
    /// </summary>
    internal static class ChestSummary
    {
        /// <summary>Safety age (s). Shorter than NearbyIndex's 4 s inventory load cooldown.</summary>
        internal const float MaxAge = 0.5f;

        internal struct QKey : System.IEquatable<QKey>
        {
            public string Name;
            public int Quality;

            public bool Equals(QKey o) { return Quality == o.Quality && string.Equals(Name, o.Name); }
            public override bool Equals(object o) { return o is QKey && Equals((QKey)o); }
            public override int GetHashCode() { return (Name != null ? Name.GetHashCode() : 0) * 31 + Quality; }
        }

        /// <summary>Display view of a chest: stacks of one item type (shared data + drop prefab) added up.</summary>
        internal sealed class Group
        {
            public ItemDrop.ItemData.SharedData Shared;
            public GameObject Prefab;
            public ItemDrop.ItemData Sample;
            public int Count;
        }

        private sealed class Entry
        {
            public Inventory Inv;
            public readonly List<Group> Groups = new List<Group>();
            public bool Dirty = true;
            public float BuiltAt;
            public int WorldLevel;
            public long Signature;
            public readonly Dictionary<string, int> Total = new Dictionary<string, int>();
            public readonly Dictionary<QKey, int> ByQuality = new Dictionary<QKey, int>();
        }

        /// <summary>Summed totals of one chest set (one CountItem origin / range / link / LeaveOne).</summary>
        internal sealed class Aggregate
        {
            public float BuiltAt = -1f;
            public int Version = -1;
            public readonly Dictionary<string, int> Total = new Dictionary<string, int>();
            public readonly Dictionary<QKey, int> ByQuality = new Dictionary<QKey, int>();

            public bool IsCurrent
            {
                get { return BuiltAt >= 0f && Version == ChestSummary.Version && Time.unscaledTime - BuiltAt < MaxAge; }
            }

            public void Begin()
            {
                Total.Clear();
                ByQuality.Clear();
            }

            /// <summary>Call after all AddChest: loads during the build bump Version and must not void the result.</summary>
            public void End()
            {
                BuiltAt = Time.unscaledTime;
                Version = ChestSummary.Version;
            }

            public void AddChest(Container chest, Inventory inv, bool leaveOne)
            {
                Entry e = EntryFor(inv);
                if (e == null)
                    return;
                foreach (KeyValuePair<string, int> pair in e.Total)
                    AddTo(Total, pair.Key, Adjust(chest, pair.Key, pair.Value, leaveOne));
                foreach (KeyValuePair<QKey, int> pair in e.ByQuality)
                    AddTo(ByQuality, pair.Key, Adjust(chest, pair.Key.Name, pair.Value, leaveOne));
            }

            public int Get(string sharedName, int quality)
            {
                int n;
                if (quality < 0)
                    return Total.TryGetValue(sharedName, out n) ? n : 0;
                return ByQuality.TryGetValue(new QKey { Name = sharedName, Quality = quality }, out n) ? n : 0;
            }
        }

        private const int MaxEntries = 4096;
        private static readonly Dictionary<Inventory, Entry> Entries = new Dictionary<Inventory, Entry>();
        private static readonly Dictionary<string, bool> LeaveOneByName = new Dictionary<string, bool>();
        private static ObjectDB _leaveOneDb;

        /// <summary>Bumped when a summarized chest changes or counts are invalidated.</summary>
        internal static int Version;

        /// <summary>The chest left the registry (unloaded / destroyed): drop its summary instead of keeping the dead inventory.</summary>
        public static void Forget(Inventory inv)
        {
            if (inv != null)
                Entries.Remove(inv);
        }

        public static void Invalidate(Inventory inv)
        {
            Entry e;
            if (inv == null || !Entries.TryGetValue(inv, out e))
                return;
            e.Dirty = true;
            Version++;
        }

        private static Entry EntryFor(Inventory inv)
        {
            if (inv == null)
                return null;
            Entry e;
            if (!Entries.TryGetValue(inv, out e))
            {
                if (Entries.Count >= MaxEntries)
                    Entries.Clear();
                e = new Entry { Inv = inv };
                Entries[inv] = e;
            }
            // Only a changed chest is read again. Marked by Inventory.Changed and by every Container.Save (SAC's own
            // stack edits, e.g. a partial take in ConsumeLocal, save without Changed). On top of that a cheap signature
            // of the stacks catches any change that reported neither. (An age limit here re-read every chest on every
            // rebuild of a chest-set total, about once a second: the per-chest summary saved nothing.)
            if (e.Dirty || e.WorldLevel != Game.m_worldLevel || e.Signature != SignatureOf(e.Inv))
                Build(e);
            return e;
        }

        /// <summary>Item count and the stacks weighted by item type: changes whenever an amount or a type changes.</summary>
        private static long SignatureOf(Inventory inv)
        {
            long sig = inv.NrOfItems();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null)
                    continue;
                unchecked
                {
                    sig = sig * 31 + (long)item.m_stack * (item.m_shared.m_name.GetHashCode() | 1) + item.m_quality;
                }
            }
            return sig;
        }

        private static void Build(Entry e)
        {
            e.Dirty = false;
            e.Signature = SignatureOf(e.Inv);
            e.BuiltAt = Time.unscaledTime;
            e.WorldLevel = Game.m_worldLevel;
            e.Total.Clear();
            e.ByQuality.Clear();
            e.Groups.Clear();
            int worldLevel = e.WorldLevel;
            foreach (ItemDrop.ItemData item in e.Inv.GetAllItems())
            {
                if (item == null || item.m_shared == null)
                    continue;
                if (item.m_stack > 0)
                    AddGroup(e.Groups, item);
                if (item.m_worldLevel < worldLevel)
                    continue;
                string name = item.m_shared.m_name;
                AddTo(e.Total, name, item.m_stack);
                AddTo(e.ByQuality, new QKey { Name = name, Quality = item.m_quality }, item.m_stack);
            }
        }

        // Groups keep the order of first appearance, so the first sample per name is the same item a plain walk finds.
        private static void AddGroup(List<Group> groups, ItemDrop.ItemData item)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                Group g = groups[i];
                if (g.Shared == item.m_shared && g.Prefab == item.m_dropPrefab)
                {
                    g.Count += item.m_stack;
                    return;
                }
            }
            groups.Add(new Group { Shared = item.m_shared, Prefab = item.m_dropPrefab, Sample = item, Count = item.m_stack });
        }

        /// <summary>
        /// Item types of a chest with their stack totals (all world levels, stacks above zero), for the display scan.
        /// The list belongs to the summary: read it right away, do not keep it.
        /// </summary>
        public static List<Group> Groups(Inventory inv)
        {
            Entry e = EntryFor(inv);
            return e != null ? e.Groups : null;
        }

        private static int Adjust(Container chest, string name, int n, bool leaveOne)
        {
            n -= PendingChestDebit.Of(chest, name);
            if (n <= 0)
                return 0;
            if (leaveOne && ShouldLeaveOne(name))
                n -= 1;
            return n;
        }

        // ItemIds.ShouldLeaveOne looks up the prefab, and a name ObjectDB does not know walks the whole item list
        // every time. The answer (stack size above 1) does not change while a world is loaded: remember it until
        // ObjectDB is a different one (new world / back to the menu).
        private static bool ShouldLeaveOne(string name)
        {
            if (_leaveOneDb != ObjectDB.instance || LeaveOneByName.Count > 4096)
            {
                LeaveOneByName.Clear();
                _leaveOneDb = ObjectDB.instance;
            }
            bool v;
            if (!LeaveOneByName.TryGetValue(name, out v))
            {
                v = ItemIds.ShouldLeaveOne(true, name);
                LeaveOneByName[name] = v;
            }
            return v;
        }

        private static void AddTo<TKey>(Dictionary<TKey, int> d, TKey key, int n)
        {
            if (n <= 0)
                return;
            int cur;
            d.TryGetValue(key, out cur);
            d[key] = cur + n;
        }
    }

    // A chest was saved: SAC's own writes (partial takes, deposits) save without Inventory.Changed. Mark it like a change.
    [HarmonyLib.HarmonyPatch(typeof(Container), "Save")]
    internal static class ChestSummarySavePatch
    {
        private static void Postfix(Container __instance)
        {
            if (__instance == null)
                return;
            ChestSummary.Invalidate(__instance.GetInventory());
            ItemIndex.MarkDirty(__instance);
            StorageDisplayBoard.OnChestSaved(__instance);
        }
    }

    // A chest inventory changed (take / store / load): rebuild its summary and every chest-set total.
    [HarmonyLib.HarmonyPatch(typeof(Inventory), "Changed")]
    internal static class ChestSummaryInvalidatePatch
    {
        private static void Postfix(Inventory __instance)
        {
            ChestSummary.Invalidate(__instance);
        }
    }
}
