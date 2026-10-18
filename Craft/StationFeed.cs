using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class StationFeed
    {
        public static bool Ready()
        {
            return StagingPull.Active;
        }

        public static Player LocalPlayer(Humanoid user)
        {
            Player player = user as Player;
            if (player == null || player != Player.m_localPlayer)
                return null;
            return player;
        }

        public static string SharedFrom(ItemDrop drop)
        {
            return drop?.m_itemData?.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
        }

        public static int LocalCount(Player player, string shared)
        {
            if (player == null || string.IsNullOrEmpty(shared))
                return 0;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return 0;

            // Extra rows / quick slots are not "bag" — auto-fill and [E] must not steal them.
            return PlayerBag.CountInBag(inv, shared);
        }

        public static bool ChestsHave(Player player, string shared)
        {
            return ChestsHave(player, shared, StationLink.ActiveId);
        }

        /// <param name="linkId">-1 = all chests; 0 = untagged only; 1–9 = matching [lN] only.</param>
        public static bool ChestsHave(Player player, string shared, int linkId)
        {
            if (player == null || string.IsNullOrEmpty(shared))
                return false;

            // Linked / station context: never trust the autofill pulse (built with ActiveId=-1).
            if (linkId >= 0)
                return RequirementBridge.CountNearby(player, shared, linkId, -1) > 0;

            if (_pulseActive && _pulseSpendable != null)
            {
                int n;
                return _pulseSpendable.TryGetValue(shared, out n) && n > 0;
            }

            return RequirementBridge.CountNearby(player, shared, -1, -1) > 0;
        }

        /// <summary>
        /// While &gt; 0, chest count / pull uses this range instead of CraftRange (auto-fill).
        /// </summary>
        public static float PullRangeOverride;

        /// <summary>Auto-fill pulse: one chest snapshot, then O(1) ChestsHave.</summary>
        private static Dictionary<string, int> _pulseSpendable;
        private static bool _pulseActive;
        /// <summary>When set, ConsumeFromChests measures distance from this point (station).</summary>
        public static Vector3? PullOriginOverride;

        public static Vector3 ActivePullOrigin(Player player)
        {
            if (PullOriginOverride.HasValue)
                return PullOriginOverride.Value;
            if (player != null)
                return player.transform.position;
            return Vector3.zero;
        }

        public static void BeginAutoFillPulse(Player player, float range)
        {
            BeginAutoFillPulse(player, range, null);
        }

        /// <summary>
        /// Snapshot spendable mats near the given station positions (combo: player gates which
        /// stations fire; chests are counted around each station).
        /// </summary>
        public static void BeginAutoFillPulse(Player player, float range, List<Vector3> stationOrigins)
        {
            _pulseActive = false;
            _pulseSpendable = null;
            if (player == null || Plugin.Settings == null)
                return;

            NearbyIndex.Tick();
            // No chest snapshot: every auto-fill path passes the station link (0-9) to
            // ChestsHave, which then counts via RequirementBridge and never reads the map.
            // Building it scanned every chest around every station on each pulse for nothing.
            _pulseActive = true;
        }

        public static void EndAutoFillPulse()
        {
            _pulseActive = false;
            _pulseSpendable = null;
            PullOriginOverride = null;
        }

        public static void NotePulseConsumed(string shared, int amount)
        {
            if (!_pulseActive || _pulseSpendable == null || amount <= 0 || string.IsNullOrEmpty(shared))
                return;
            int n;
            if (!_pulseSpendable.TryGetValue(shared, out n))
                return;
            n -= amount;
            if (n <= 0)
                _pulseSpendable.Remove(shared);
            else
                _pulseSpendable[shared] = n;
        }

        public static float ActivePullRange()
        {
            if (PullRangeOverride > 0f)
                return PullRangeOverride;
            return Plugin.Settings != null ? Plugin.Settings.CraftReach() : 20f;
        }

        public static bool HasOrChests(Player player, string shared)
        {
            return LocalCount(player, shared) > 0 || ChestsHave(player, shared);
        }

        public static bool HasOrChestsAny(Player player, List<string> sharedNames)
        {
            if (sharedNames == null)
                return false;
            foreach (string shared in sharedNames)
            {
                if (HasOrChests(player, shared))
                    return true;
            }
            return false;
        }

        public static List<string> NamesFromDrops(IEnumerable<ItemDrop> drops)
        {
            var names = new List<string>();
            if (drops == null)
                return names;
            foreach (ItemDrop drop in drops)
            {
                string shared = SharedFrom(drop);
                if (!string.IsNullOrEmpty(shared) && !names.Contains(shared))
                    names.Add(shared);
            }
            return names;
        }

        public static bool EnsureInInventory(Player player, string shared, int amount)
        {
            return EnsureInInventory(player, shared, amount, StationLink.ActiveId);
        }

        public static bool EnsureInInventory(Player player, string shared, int amount, int linkId)
        {
            if (player == null || amount <= 0 || string.IsNullOrEmpty(shared))
                return false;

            int have = LocalCount(player, shared);
            if (have >= amount)
                return true;
            if (!Ready())
                return false;

            PullIntoInventory(player, shared, amount - have, linkId);
            return LocalCount(player, shared) >= amount;
        }

        public static bool EnsureAny(Player player, List<string> sharedNames, int amount)
        {
            if (player == null || sharedNames == null || amount <= 0)
                return false;

            // Prefer anything already in the inventory before touching chests
            // (e.g. deer meat on you vs boar meat in a nearby chest).
            foreach (string shared in sharedNames)
            {
                if (string.IsNullOrEmpty(shared))
                    continue;
                if (LocalCount(player, shared) >= amount)
                {
                    StampDropPrefabs(player, sharedNames);
                    return true;
                }
            }

            foreach (string shared in sharedNames)
            {
                if (string.IsNullOrEmpty(shared))
                    continue;
                if (EnsureInInventory(player, shared, amount))
                {
                    StampDropPrefabs(player, sharedNames);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// CookingStation.CookItem reads m_dropPrefab.name immediately (NRE if null).
        /// IsItemAllowed compares that prefab GameObject name to conversion m_from.
        /// Only stamps when missing — never replaces a valid existing prefab.
        /// </summary>
        public static void StampDropPrefabs(Player player, List<string> sharedNames)
        {
            Inventory inv = player != null ? player.GetInventory() : null;
            if (inv == null || sharedNames == null || sharedNames.Count == 0)
                return;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item?.m_shared == null || item.m_dropPrefab)
                    continue;
                if (!sharedNames.Contains(item.m_shared.m_name))
                    continue;
                EnsureDropPrefab(item);
            }
        }

        public static void EnsureDropPrefab(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
                return;
            if (item.m_dropPrefab)
                return;

            // SharedData is reference-keyed in ObjectDB — works when m_shared is live prefab data.
            if (ObjectDB.instance != null)
            {
                GameObject go = ObjectDB.instance.GetItemPrefab(item.m_shared);
                if (go != null)
                {
                    item.m_dropPrefab = go;
                    return;
                }
            }

            // By shared token / prefab name (cache no longer stores null failures).
            GameObject fromToken = ItemIds.PrefabFromToken(item.m_shared.m_name);
            if (fromToken != null)
            {
                item.m_dropPrefab = fromToken;
                return;
            }

            ResolveDropPrefabFromObjectDb(item);
        }

        /// <summary>
        /// CookingStation.IsItemAllowed compares m_dropPrefab.name to conversion m_from
        /// with exact string equality — a (Clone) ref or wrong GO → "can't use …".
        /// When this station has a conversion for the item, stamp that m_from GameObject.
        /// Items with no conversion here are left alone (correct vanilla reject).
        /// </summary>
        public static void EnsureCookDropPrefab(CookingStation station, ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return;

            CookingStation.ItemConversion conv = FindCookConversion(station, item);
            if (conv?.m_from != null && conv.m_from.gameObject != null)
            {
                if (item.m_dropPrefab != conv.m_from.gameObject)
                    item.m_dropPrefab = conv.m_from.gameObject;
                return;
            }

            // No conversion on this station (e.g. Volture on wooden spit) — only fill nulls.
            if (!item.m_dropPrefab)
                EnsureDropPrefab(item);
        }

        /// <summary>
        /// Prefab name RPC_AddItem / IsItemAllowed(string) need — exact conversion GameObject name.
        /// </summary>
        public static string CookPrefabName(CookingStation station, string shared)
        {
            if (station?.m_conversion == null || string.IsNullOrEmpty(shared))
                return null;

            for (int i = 0; i < station.m_conversion.Count; i++)
            {
                CookingStation.ItemConversion conv = station.m_conversion[i];
                ItemDrop from = conv != null ? conv.m_from : null;
                if (from?.gameObject == null)
                    continue;

                string fromShared = from.m_itemData?.m_shared != null
                    ? from.m_itemData.m_shared.m_name
                    : null;
                if (!string.IsNullOrEmpty(fromShared)
                    && string.Equals(fromShared, shared, System.StringComparison.OrdinalIgnoreCase))
                    return from.gameObject.name;

                string fromName = ItemIds.StripClone(from.gameObject.name);
                GameObject odb = ItemIds.PrefabFromToken(shared);
                if (odb != null
                    && string.Equals(fromName, ItemIds.StripClone(odb.name), System.StringComparison.OrdinalIgnoreCase))
                    return from.gameObject.name;
            }

            GameObject fallback = ItemIds.PrefabFromToken(shared);
            return fallback != null ? ItemIds.StripClone(fallback.name) : null;
        }

        public static CookingStation.ItemConversion FindCookConversion(
            CookingStation station,
            ItemDrop.ItemData item)
        {
            if (station?.m_conversion == null || item?.m_shared == null)
                return null;

            string shared = item.m_shared.m_name;
            string hint = item.m_dropPrefab ? ItemIds.StripClone(item.m_dropPrefab.name) : null;
            GameObject odb = ItemIds.PrefabFromToken(shared);
            string odbName = odb != null ? ItemIds.StripClone(odb.name) : null;

            for (int i = 0; i < station.m_conversion.Count; i++)
            {
                CookingStation.ItemConversion conv = station.m_conversion[i];
                ItemDrop from = conv != null ? conv.m_from : null;
                if (from?.gameObject == null)
                    continue;

                string fromShared = from.m_itemData?.m_shared != null
                    ? from.m_itemData.m_shared.m_name
                    : null;
                string fromName = ItemIds.StripClone(from.gameObject.name);

                if (!string.IsNullOrEmpty(fromShared)
                    && string.Equals(fromShared, shared, System.StringComparison.OrdinalIgnoreCase))
                    return conv;
                if (!string.IsNullOrEmpty(hint)
                    && string.Equals(fromName, hint, System.StringComparison.OrdinalIgnoreCase))
                    return conv;
                if (!string.IsNullOrEmpty(odbName)
                    && string.Equals(fromName, odbName, System.StringComparison.OrdinalIgnoreCase))
                    return conv;
            }

            return null;
        }

        private static void ResolveDropPrefabFromObjectDb(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null || ObjectDB.instance?.m_items == null)
                return;
            string shared = item.m_shared.m_name;
            for (int i = 0; i < ObjectDB.instance.m_items.Count; i++)
            {
                GameObject go = ObjectDB.instance.m_items[i];
                if (go == null)
                    continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                string dropShared = drop?.m_itemData?.m_shared != null
                    ? drop.m_itemData.m_shared.m_name
                    : null;
                if (string.IsNullOrEmpty(dropShared))
                    continue;
                if (string.Equals(dropShared, shared, System.StringComparison.OrdinalIgnoreCase))
                {
                    item.m_dropPrefab = go;
                    return;
                }
            }
        }

        public static int ConsumeFromChests(Player player, string shared, int amount)
        {
            return ConsumeFromChests(player, shared, amount, StationLink.ActiveId);
        }

        public static int ConsumeFromChests(Player player, string shared, int amount, int linkId)
        {
            if (player == null || amount <= 0 || string.IsNullOrEmpty(shared))
                return 0;
            if (!Ready())
                return 0;

            NearbyIndex.Tick();
            bool leaveOne = Plugin.Settings != null && Plugin.Settings.LeaveOneItem.Value;
            float range = ActivePullRange();
            Vector3 origin = ActivePullOrigin(player);
            int took = ConsumePass(origin, range, shared, amount, linkId, leaveOne, forceLoad: false);
            // Stale empty view: ChestsHave said yes / pulse map had stock, but Consume got 0.
            if (took <= 0)
            {
                took = ConsumePass(origin, range, shared, amount, linkId, leaveOne, forceLoad: true);
                if (took <= 0 && _pulseActive && _pulseSpendable != null
                    && _pulseSpendable.ContainsKey(shared))
                {
                    ActivityLog.Add(Loc.T("Missed", "Verpasst") + ": "
                        + DisplayFilters.ItemLabel(shared)
                        + " (" + Loc.T("chest view stale", "Kisten-View veraltet") + ")");
                }
            }

            return took;
        }

        private static int ConsumePass(
            Vector3 origin,
            float range,
            string shared,
            int amount,
            int linkId,
            bool leaveOne,
            bool forceLoad)
        {
            int need = amount;
            int took = 0;

            if (PullOriginOverride.HasValue)
            {
                var near = new List<Container>(24);
                NearbyIndex.CollectNear(origin, range, near);
                for (int i = 0; i < near.Count; i++)
                {
                    if (need <= 0)
                        break;
                    took += TryConsumeOne(near[i], shared, ref need, linkId, leaveOne, forceLoad);
                }
            }
            else
            {
                foreach (Container chest in NearbyIndex.Current)
                {
                    if (need <= 0)
                        break;
                    took += TryConsumeOne(chest, shared, ref need, linkId, leaveOne, forceLoad);
                }
            }

            return took;
        }

        private static int TryConsumeOne(
            Container chest,
            string shared,
            ref int need,
            int linkId,
            bool leaveOne,
            bool forceLoad)
        {
            if (chest == null || need <= 0 || !StationLink.ChestAllowed(chest, linkId))
                return 0;
            // A chest that surely has none is skipped (no load); the forced second pass still looks at every chest.
            if (!forceLoad && !ItemIndex.MightHold(chest, shared))
                return 0;

            Inventory inv = chest.GetInventory();
            if (forceLoad || inv == null || inv.NrOfItems() <= 0)
                NearbyIndex.EnsureInventory(chest, force: true);

            int n = TransferService.Consume(chest, shared, need, leaveOne);
            if (n <= 0)
                return 0;
            need -= n;
            NotePulseConsumed(shared, n);
            return n;
        }

        public static void PullIntoInventory(Player player, string shared, int amount)
        {
            PullIntoInventory(player, shared, amount, StationLink.ActiveId);
        }

        public static void PullIntoInventory(Player player, string shared, int amount, int linkId)
        {
            if (player == null || amount <= 0 || string.IsNullOrEmpty(shared))
                return;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            NearbyIndex.Tick();
            bool leaveOne = Plugin.Settings != null && Plugin.Settings.LeaveOneItem.Value;
            float cfgCraft = ActivePullRange();
            Vector3 origin = ActivePullOrigin(player);
            int need = amount;

            foreach (Container chest in NearbyIndex.Current)
            {
                if (need <= 0)
                    break;
                if (chest == null || !StationLink.ChestAllowed(chest, linkId))
                    continue;

                if (ContainerFilter.Distance(origin, chest.transform.position) > cfgCraft)
                    continue;

                Inventory chestInv = chest.GetInventory();
                if (chestInv == null || chestInv.NrOfItems() <= 0)
                    NearbyIndex.EnsureInventory(chest, force: true);

                int took = TransferService.Withdraw(chest, shared, need, inv, leaveOne);
                ActivityLog.Move(Loc.T("Station [E]", "Station [E]"), ActivityLog.Chest(chest), took, shared, ActivityLog.Bag());
                need -= took;
            }
        }

        /// <summary>
        /// A manually used item (hotbar key) that is none of the station's inputs, e.g. the hammer. Null item ([E])
        /// or an input type (also one switched off in the station filter) is not "other".
        /// </summary>
        public static bool IsOtherItem(ItemDrop.ItemData item, List<string> inputs)
        {
            string shared = item != null && item.m_shared != null ? item.m_shared.m_name : null;
            return !string.IsNullOrEmpty(shared) && inputs != null && inputs.Count > 0 && !inputs.Contains(shared);
        }

        public static bool IsOtherItem(ItemDrop.ItemData item, string input)
        {
            string shared = item != null && item.m_shared != null ? item.m_shared.m_name : null;
            return !string.IsNullOrEmpty(shared) && !string.IsNullOrEmpty(input) && shared != input;
        }

        public static void EnsureForUse(Player player, ref ItemDrop.ItemData item, List<string> fallbackNames)
        {
            if (player == null)
                return;

            ItemDrop.ItemData requested = item;
            string want = requested != null && requested.m_shared != null ? requested.m_shared.m_name : null;

            if (!string.IsNullOrEmpty(want))
            {
                // Allow-list from StationPullFilter: denied types cannot come from chests OR bag.
                bool allowed = fallbackNames == null
                    || fallbackNames.Count == 0
                    || fallbackNames.Contains(want);
                if (!allowed)
                {
                    item = null;
                    return;
                }

                EnsureInInventory(player, want, 1);
            }
            else
            {
                EnsureAny(player, fallbackNames, 1);
                // Vanilla FindCookableItem runs after us with item still null — pick an allowed stack now.
                if (fallbackNames != null)
                {
                    foreach (string shared in fallbackNames)
                    {
                        ItemDrop.ItemData local = GetLocal(player, shared);
                        if (local == null)
                            continue;
                        if (!local.m_dropPrefab)
                            EnsureDropPrefab(local);
                        item = local;
                        return;
                    }
                }
                return;
            }

            // Prefer bag stack (IgnoreHotbar may hide the hotbar row from GetLocal).
            ItemDrop.ItemData found = GetLocal(player, want);
            if (found != null)
            {
                if (!found.m_dropPrefab)
                    EnsureDropPrefab(found);
                item = found;
                return;
            }

            // Manual use: player already selected this stack (often hotbar). Only if allowed above.
            if (requested != null && requested.m_shared != null
                && string.Equals(requested.m_shared.m_name, want, System.StringComparison.Ordinal)
                && requested.m_stack > 0)
            {
                if (!requested.m_dropPrefab)
                    EnsureDropPrefab(requested);
                item = requested;
                return;
            }

            item = null;
        }

        public static ItemDrop.ItemData SampleNearby(Player player, string shared)
        {
            if (player == null || string.IsNullOrEmpty(shared))
                return null;

            NearbyIndex.Tick();
            foreach (Container chest in NearbyIndex.Current)
            {
                if (chest == null || !StationLink.ChestAllowedForActive(chest))
                    continue;
                Inventory inv = chest.GetInventory();
                if (inv == null)
                    continue;
                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                {
                    if (item?.m_shared == null || item.m_stack <= 0)
                        continue;
                    if (item.m_shared.m_name != shared)
                        continue;
                    return item.Clone();
                }
            }

            GameObject prefab = ItemIds.PrefabFromToken(shared);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop?.m_itemData != null ? drop.m_itemData.Clone() : null;
        }

        private static ItemDrop.ItemData GetLocal(Player player, string shared)
        {
            Inventory inv = player != null ? player.GetInventory() : null;
            if (inv == null || string.IsNullOrEmpty(shared))
                return null;
            return PlayerBag.FindInBag(inv, shared);
        }
    }
}
