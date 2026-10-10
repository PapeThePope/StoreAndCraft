using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Deposit finished station products into nearby player chests (Auto-store).
    /// Uses NearbyIndex + TransferService — not a ground drop + intake round-trip.
    /// </summary>
    internal static class StationOutput
    {
        /// <summary>
        /// On a ground ItemDrop: restrict AutoIntake to <see cref="StationLink.ChestAllowedForOutput"/>.
        /// Missing / -1 = normal unrestricted intake.
        /// </summary>
        public const string IntakeLinkZdoKey = "SAC_intakeLink";
        public static readonly int IntakeLinkZdoKeyHash = IntakeLinkZdoKey.GetStableHashCode();

        private static readonly List<Container> NearScratch = new List<Container>(32);
        private static readonly List<Container> TargetScratch = new List<Container>(1);
        private static readonly List<ItemDrop> DropScratch = new List<ItemDrop>(32);

        private static Vector3 _pendingTagPos;
        private static int _pendingTagLink = -1;
        private static string _pendingTagPrefab;
        private static float _pendingTagUntil;

        public static float DepositRange()
        {
            if (Plugin.Settings == null)
                return 10f;
            return Mathf.Max(5f, Plugin.Settings.AutoFillChestReach());
        }

        /// <summary>The scarecrow has its own station → chest reach; every other station uses the common one.</summary>
        public static float DepositRange(Component station)
        {
            if (Plugin.Settings == null || !(station is Scarecrow))
                return DepositRange();
            return Mathf.Max(5f, Plugin.Settings.ScarecrowChestReach());
        }

        /// <summary>
        /// Put <paramref name="amount"/> of prefab/token into a chest near <paramref name="station"/>.
        /// Linked station: only matching [lN] chests — never untagged or a different link.
        /// Unlinked station: untagged chests only.
        /// MustHaveExisting: only chests that already hold that item (empty never).
        /// No match → false (caller ground-drops + tags intake link). Honors [I].
        /// </summary>
        public static bool TryDepositNear(
            Component station,
            string prefabOrToken,
            int amount,
            out string label)
        {
            Container unused;
            return TryDepositNear(station, prefabOrToken, amount, out label, out unused);
        }

        /// <summary>Same, and tells which chest took it (for the activity log, null when nothing was stored).</summary>
        public static bool TryDepositNear(
            Component station,
            string prefabOrToken,
            int amount,
            out string label,
            out Container chest)
        {
            label = null;
            chest = null;
            if (station == null || amount <= 0 || string.IsNullOrEmpty(prefabOrToken))
                return false;

            GameObject prefab = ItemIds.PrefabFromToken(prefabOrToken);
            if (prefab == null && ObjectDB.instance != null)
                prefab = ObjectDB.instance.GetItemPrefab(prefabOrToken);
            if (prefab == null)
                return false;

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                return false;

            string shared = drop.m_itemData.m_shared.m_name;
            label = DisplayFilters.ItemLabel(shared);
            if (string.IsNullOrEmpty(label))
                label = prefab.name;

            int linkId = StationLink.Get(station);
            float range = DepositRange(station);
            bool mustHave = Plugin.Settings == null || Plugin.Settings.MustHaveExisting.Value;
            // Linked station: matching [lN] only. No room there → false, the caller drops it on
            // the ground tagged with the link (AutoIntake then also only uses [lN]).
            // Unlinked station: untagged only.
            int exactChestLink = StationLink.Clamp(linkId >= 1 ? linkId : 0);
            Container used = null;

            // Whole loaded base: the chest that took this item last time first, no search.
            bool remember = TargetChests.Active;
            if (remember)
            {
                Container target = TargetChests.Get(shared, exactChestLink);
                if (target != null && NearbyIndex.Usable(target, station.transform.position, range))
                {
                    TargetScratch.Clear();
                    TargetScratch.Add(target);
                    bool ok = TryDepositPass(TargetScratch, prefabOrToken, shared, amount, exactChestLink, mustHave, out used);
                    TargetScratch.Clear();
                    if (ok)
                    {
                        chest = used;
                        return true;
                    }
                }
                if (target != null)
                    TargetChests.Forget(shared, exactChestLink);
                // A search just found nothing: do not search the whole base again right away.
                if (TargetChests.RecentlyNone(shared, exactChestLink))
                    return false;
            }

            NearScratch.Clear();
            NearbyIndex.CollectNear(station.transform.position, range, NearScratch);
            bool stored = NearScratch.Count > 0
                && TryDepositPass(NearScratch, prefabOrToken, shared, amount, exactChestLink, mustHave, out used);
            if (remember)
            {
                if (stored)
                    TargetChests.Remember(shared, exactChestLink, used);
                else
                    TargetChests.MarkNone(shared, exactChestLink);
            }
            if (stored)
                chest = used;
            return stored;
        }

        /// <summary>
        /// Does the station have a chest to hand things to at all (no product needed)? NoLinkedChest: it has a link but
        /// no chest with that link is in reach. NoChestNearby: unlinked and no chest of its kind in reach. Otherwise Ok.
        /// </summary>
        internal static OutputState ReachStateOf(Component station)
        {
            if (station == null)
                return OutputState.Ok;
            int linkId = StationLink.Get(station);
            int exactChestLink = StationLink.Clamp(linkId >= 1 ? linkId : 0);

            NearScratch.Clear();
            NearbyIndex.CollectNear(station.transform.position, DepositRange(station), NearScratch);
            for (int i = 0; i < NearScratch.Count; i++)
            {
                Container chest = NearScratch[i];
                if (chest == null || ChestNames.BlocksDeposit(chest))
                    continue;
                if (StationLink.ChestLinkId(chest) != exactChestLink)
                    continue;
                if (ContainerFilter.IsPlayerBuiltStorage(chest))
                    return OutputState.Ok;
            }
            return linkId >= 1 ? OutputState.NoLinkedChest : OutputState.NoChestNearby;
        }

        /// <summary>Why a station cannot hand its product to a chest right now.</summary>
        internal enum OutputState { Ok, ChestFull, NoLinkedChest, NoChestNearby }

        /// <summary>
        /// ChestFull: matching chests are in reach (same link / existing-item rules as <see cref="TryDepositNear"/>)
        /// but none has room for <paramref name="amount"/>. NoLinkedChest: the station has a link but no chest with
        /// that link is in reach. NoChestNearby: an unlinked station has no chest of its kind in reach (notice only,
        /// the product drops as before). Ok: a chest has room. Only reads, nothing is moved.
        /// </summary>
        internal static OutputState OutputStateOf(Component station, string prefabOrToken, int amount)
        {
            if (station == null || amount <= 0 || string.IsNullOrEmpty(prefabOrToken))
                return OutputState.Ok;

            GameObject prefab = ItemIds.PrefabFromToken(prefabOrToken);
            if (prefab == null && ObjectDB.instance != null)
                prefab = ObjectDB.instance.GetItemPrefab(prefabOrToken);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                return OutputState.Ok;

            string shared = drop.m_itemData.m_shared.m_name;
            amount = Mathf.Min(amount, Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize));
            int linkId = StationLink.Get(station);
            int exactChestLink = StationLink.Clamp(linkId >= 1 ? linkId : 0);
            bool mustHave = Plugin.Settings == null || Plugin.Settings.MustHaveExisting.Value;

            ItemDrop.ItemData probe = drop.m_itemData.Clone();
            probe.m_stack = amount;

            // Whole loaded base: the remembered target chest has room → Ok without searching the base.
            if (TargetChests.Active)
            {
                Container target = TargetChests.Get(shared, exactChestLink);
                if (target != null && NearbyIndex.Usable(target, station.transform.position, DepositRange(station))
                    && !ChestNames.BlocksDeposit(target) && StationLink.ChestLinkId(target) == exactChestLink
                    && ContainerFilter.IsPlayerBuiltStorage(target))
                {
                    Inventory tinv = target.GetInventory();
                    if (tinv != null && (!mustHave || tinv.HaveItem(shared, true)) && tinv.CanAddItem(probe, amount))
                        return OutputState.Ok;
                }
            }

            NearScratch.Clear();
            NearbyIndex.CollectNear(station.transform.position, DepositRange(station), NearScratch);
            bool anyLinked = false;
            bool anyChest = false;
            for (int i = 0; i < NearScratch.Count; i++)
            {
                Container chest = NearScratch[i];
                if (chest == null || ChestNames.BlocksDeposit(chest))
                    continue;
                if (StationLink.ChestLinkId(chest) != exactChestLink)
                    continue;
                if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                    continue;
                anyLinked = true;
                // Must hold the item: a loaded chest without it is neither a candidate nor a room check (item index).
                if (mustHave && !ItemIndex.MightHold(chest, shared))
                    continue;

                // First look at the inventory as it is (no ZDO load). Only a chest that does not look usable gets
                // a fresh look (the load is throttled per chest): keeps the periodic status check cheap.
                bool candidate = false;
                bool room = false;
                for (int attempt = 0; attempt < 2 && !room; attempt++)
                {
                    if (attempt == 1)
                        NearbyIndex.EnsureInventory(chest);
                    Inventory inv = chest.GetInventory();
                    if (inv == null)
                        continue;
                    if (mustHave && !inv.HaveItem(shared, true))
                        continue;
                    candidate = true;
                    room = inv.CanAddItem(probe, amount);
                }
                if (room)
                    return OutputState.Ok;
                if (candidate)
                    anyChest = true;
            }
            if (!anyLinked)
                return linkId >= 1 ? OutputState.NoLinkedChest : OutputState.NoChestNearby;
            return anyChest ? OutputState.ChestFull : OutputState.Ok;
        }

        /// <param name="exactChestLink">0 = untagged only; 1–9 = that [lN] only.</param>
        private static bool TryDepositPass(
            List<Container> chests,
            string prefabOrToken,
            string shared,
            int amount,
            int exactChestLink,
            bool requireExisting,
            out Container used)
        {
            used = null;
            exactChestLink = StationLink.Clamp(exactChestLink);
            for (int i = 0; i < chests.Count; i++)
            {
                Container chest = chests[i];
                if (chest == null || ChestNames.BlocksDeposit(chest))
                    continue;
                if (StationLink.ChestLinkId(chest) != exactChestLink)
                    continue;
                if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                    continue;
                // Must hold the item: skip chests the item index knows have none, before the forced load.
                if (requireExisting && !ItemIndex.MightHold(chest, shared))
                    continue;

                NearbyIndex.EnsureInventory(chest, force: true);
                Inventory inv = chest.GetInventory();
                if (inv == null)
                    continue;

                if (requireExisting && !inv.HaveItem(shared, true))
                    continue;

                if (TransferService.TryDepositCreated(chest, prefabOrToken, amount))
                {
                    used = chest;
                    return true;
                }
            }
            return false;
        }

        /// <summary>-1 = no intake link restriction.</summary>
        public static int GetIntakeLink(ItemDrop drop)
        {
            if (drop == null)
                return -1;
            ZNetView nv = Refs.View(drop);
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return -1;
            return zdo.GetInt(IntakeLinkZdoKeyHash, -1);
        }

        public static void SetIntakeLink(ItemDrop drop, int linkId)
        {
            if (drop == null)
                return;
            ZNetView nv = Refs.View(drop);
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            nv.GetZDO().Set(IntakeLinkZdoKeyHash, StationLink.Clamp(linkId));
        }

        /// <summary>
        /// After Auto-store fails and vanilla leaves a ground drop: keep intake from other links.
        /// </summary>
        public static void QueueIntakeLinkTag(Vector3 pos, int stationLinkId, string prefabNameOrNull)
        {
            _pendingTagPos = pos;
            _pendingTagLink = StationLink.Clamp(stationLinkId);
            _pendingTagPrefab = prefabNameOrNull;
            _pendingTagUntil = Time.time + 2.5f;
            TagNearbyDrops(pos, prefabNameOrNull, _pendingTagLink);
        }

        public static void TickPendingIntakeTags()
        {
            if (_pendingTagLink < 0)
                return;
            if (TagNearbyDrops(_pendingTagPos, _pendingTagPrefab, _pendingTagLink)
                || Time.time >= _pendingTagUntil)
            {
                _pendingTagLink = -1;
                _pendingTagPrefab = null;
            }
        }

        public static bool TagNearbyDrops(Vector3 pos, string prefabNameOrNull, int linkId, float radius = 2.5f)
        {
            List<ItemDrop> live = Refs.Drops();
            if (live == null || live.Count == 0)
                return false;

            DropScratch.Clear();
            DropScratch.AddRange(live);
            float r2 = radius * radius;
            bool any = false;
            for (int i = 0; i < DropScratch.Count; i++)
            {
                ItemDrop drop = DropScratch[i];
                if (drop == null || drop.m_itemData == null)
                    continue;
                if ((drop.transform.position - pos).sqrMagnitude > r2)
                    continue;
                if (!string.IsNullOrEmpty(prefabNameOrNull))
                {
                    string n = drop.gameObject != null ? drop.gameObject.name : null;
                    if (string.IsNullOrEmpty(n))
                        continue;
                    if (!n.StartsWith(prefabNameOrNull))
                        continue;
                }

                if (GetIntakeLink(drop) >= 0)
                {
                    any = true;
                    continue;
                }

                SetIntakeLink(drop, linkId);
                any = true;
            }

            return any;
        }

        public static string StationLabel(Component station)
        {
            if (station == null)
                return "?";
            string name = null;
            Smelter sm = station as Smelter;
            if (sm != null)
                name = sm.m_name;
            else
            {
                CookingStation cook = station as CookingStation;
                if (cook != null)
                    name = cook.m_name;
                else
                {
                    Beehive bee = station as Beehive;
                    if (bee != null)
                        name = bee.m_name;
                    else
                    {
                        Fireplace fire = station as Fireplace;
                        if (fire != null)
                            name = fire.m_name;
                        else
                        {
                            Fermenter fermenter = station as Fermenter;
                            if (fermenter != null)
                                name = fermenter.m_name;
                            else if (station is SapCollector sapCollector)
                                name = sapCollector.m_name;
                        }
                    }
                }
            }

            // Feed trough (Container) and other container "stations": their piece name.
            if (string.IsNullOrEmpty(name) && station is Container box)
                name = box.m_name;
            if (string.IsNullOrEmpty(name))
                return station.gameObject.name;
            return Localization.instance != null
                ? Localization.instance.Localize(name)
                : name;
        }
    }
}
