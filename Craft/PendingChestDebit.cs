using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Client-side reservation after a consume RPC. Remote chest inventories do not
    /// drop until the owner applies the RPC, so HaveRequirements would stay true and
    /// listen-server clients could craft many items for one pull.
    ///
    /// Once the ZDO sync updates the local chest inventory, the debit must be cleared.
    /// Otherwise counts subtract twice (debit + synced removal) and look like a double
    /// consume that "refunds" when the debit expires — common with a second player nearby
    /// who owns the chests.
    /// </summary>
    internal static class PendingChestDebit
    {
        private const float Lifetime = 8f;
        private static readonly Dictionary<string, int> Amounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> ExpireAt = new Dictionary<string, float>();
        /// <summary>Raw chest count we expect after the owner applies all pending debit.</summary>
        private static readonly Dictionary<string, int> ExpectedMax = new Dictionary<string, int>();
        // Of() runs for every chest of every count: chests that have nothing reserved answer without building a key.
        // May hold ids whose reservation is gone (then the normal lookup answers 0); emptied with Amounts.
        private static readonly HashSet<int> ChestsWithDebit = new HashSet<int>();
        private static readonly List<string> DeadScratch = new List<string>();
        private static int _prunedFrame = -1;

        public static void Add(Container chest, string shared, int amount)
        {
            if (chest == null || amount <= 0 || string.IsNullOrEmpty(shared))
                return;

            string key = Key(chest, shared);
            ChestsWithDebit.Add(chest.GetInstanceID());
            int have;
            Amounts.TryGetValue(key, out have);
            int total = have + amount;
            Amounts[key] = total;
            ExpireAt[key] = Time.unscaledTime + Lifetime;

            Inventory inv = chest.GetInventory();
            int raw = inv != null ? inv.CountItems(shared, -1, true) : 0;
            ExpectedMax[key] = raw - total;

            NearbyIndex.InvalidateCounts();
        }

        public static int Of(Container chest, string shared)
        {
            if (chest == null || string.IsNullOrEmpty(shared) || Amounts.Count == 0)
                return 0;
            if (!ChestsWithDebit.Contains(chest.GetInstanceID()))
                return 0;

            Prune();
            string key = Key(chest, shared);
            int n;
            if (!Amounts.TryGetValue(key, out n) || n <= 0)
                return 0;

            // ZDO sync already removed the items locally — drop the reservation or
            // UI/craft math counts the take twice until Lifetime expires.
            if (SyncApplied(chest, shared, key))
                return 0;

            return n;
        }

        public static void OnInventoryLoaded(Container chest)
        {
            if (chest == null || Amounts.Count == 0)
                return;

            string prefix = chest.GetInstanceID() + "|";
            var keys = new List<string>();
            foreach (string key in Amounts.Keys)
            {
                if (key.StartsWith(prefix))
                    keys.Add(key);
            }

            bool changed = false;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                string shared = key.Substring(prefix.Length);
                if (SyncApplied(chest, shared, key))
                    changed = true;
            }

            if (changed)
                NearbyIndex.InvalidateCounts();
        }

        public static void Tick()
        {
            if (Amounts.Count == 0)
                return;
            Prune();
        }

        private static bool SyncApplied(Container chest, string shared, string key)
        {
            int expectedMax;
            if (!ExpectedMax.TryGetValue(key, out expectedMax))
                return false;

            Inventory inv = chest.GetInventory();
            if (inv == null)
                return false;

            int current = inv.CountItems(shared, -1, true);
            if (current > expectedMax)
                return false;

            Amounts.Remove(key);
            ExpireAt.Remove(key);
            ExpectedMax.Remove(key);
            return true;
        }

        private static void Prune()
        {
            if (ExpireAt.Count == 0)
            {
                ChestsWithDebit.Clear();
                return;
            }
            // Expiry has frame resolution anyway: once per frame is enough (Of runs hundreds of times per frame).
            if (_prunedFrame == Time.frameCount)
                return;
            _prunedFrame = Time.frameCount;

            float now = Time.unscaledTime;
            List<string> dead = DeadScratch;
            dead.Clear();
            foreach (KeyValuePair<string, float> pair in ExpireAt)
            {
                if (now >= pair.Value)
                    dead.Add(pair.Key);
            }

            if (dead.Count == 0)
                return;

            for (int i = 0; i < dead.Count; i++)
            {
                string key = dead[i];
                Amounts.Remove(key);
                ExpireAt.Remove(key);
                ExpectedMax.Remove(key);
            }

            NearbyIndex.InvalidateCounts();
        }

        private static string Key(Container chest, string shared)
        {
            return chest.GetInstanceID() + "|" + shared;
        }
    }
}
