using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class AutoIntake
    {
        private static float _next;
        private static float _nextErrorLog;
        private static readonly List<ItemDrop> DropScratch = new List<ItemDrop>(64);
        private static readonly List<Player> PlayerScratch = new List<Player>(8);
        private static readonly List<Container> ChestScratch = new List<Container>(32);
        private static readonly List<Container> ProbeOrder = new List<Container>(32);
        /// <summary>Chest usable (IsReady + PlayerMayUse) — one pass only, cleared in Run.</summary>
        private static readonly Dictionary<int, bool> UsableMemo = new Dictionary<int, bool>(64);

        public static void Tick()
        {
            Run();
        }

        public static void TickDedicated()
        {
            Run();
        }

        private static bool Ready()
        {
            return Plugin.Settings != null
                && Plugin.Settings.ModEnabled.Value
                && Plugin.Settings.StoreEnabled.Value
                && ConfigCommands.IsAutoIntakeActive();
        }

        // A drop that could not be stored: 20 s pause, and after 5 failed tries only one more try every 5 minutes
        // (a new drop, or a chest that got room, is picked up again at the latest then).
        private const float FailedRetrySeconds = 20f;
        private const int FailedTriesBeforeLongPause = 5;
        private const float LongPauseSeconds = 300f;

        private struct FailInfo
        {
            public int Tries;
            public float Until;
        }

        private static readonly Dictionary<int, FailInfo> Failed = new Dictionary<int, FailInfo>();

        // A pass over the ground items may take several frames: about PassBudgetMs per frame, it goes on next frame
        // with the same snapshot, move count and memo. Same pass and order as one long frame, without the spike.
        private const float PassBudgetMs = 1f;
        private static int _cursor = -1;
        private static int _moved;
        private static float _storeRange;
        private static bool _loadedArea;
        private static int _cap;

        private static void Run()
        {
            if (!Ready())
            {
                _cursor = -1;
                return;
            }
            if (_cursor < 0 && !StartPass())
                return;

            long end = System.Diagnostics.Stopwatch.GetTimestamp()
                + (long)(PassBudgetMs / 1000.0 * System.Diagnostics.Stopwatch.Frequency);
            float storeRange = _storeRange;
            bool loadedArea = _loadedArea;
            int cap = _cap;
            int moved = _moved;
            int i = _cursor;
            try
            {
                for (; i < DropScratch.Count; i++)
                {
                    if (moved >= cap)
                        break;
                    if (i > _cursor && System.Diagnostics.Stopwatch.GetTimestamp() >= end)
                    {
                        // Budget used up: this drop and the rest next frame.
                        _cursor = i;
                        _moved = moved;
                        return;
                    }

                    ItemDrop drop = DropScratch[i];
                    if (drop == null || drop.m_itemData == null)
                        continue;
                    if (FeedTroughBait.IsBait(drop))
                        continue;
                    ZNetView nv = Refs.View(drop);
                    if (nv == null || !nv.IsValid() || drop.IsPiece())
                        continue;

                    // Whoever owns the drop stores it. One player in StoreRange of
                    // the pile is enough (you can walk off, a buddy can stay).
                    // AutomationLoadedArea: a loaded drop is in some player's loaded area, that is enough.
                    if (!nv.IsOwner())
                        continue;
                    if (!loadedArea && !AnyPlayerInRange(drop.transform.position, storeRange))
                        continue;
                    if (IsFish(drop))
                        continue;
                    // F10 "Do not pick up from the ground" list (this PC: the drops this game owns).
                    if (drop.m_itemData.m_shared != null && ItemLists.IgnoredOnGround(drop.m_itemData.m_shared.m_name))
                        continue;

                    // A drop that just failed to go in waits a while instead of being tried again every pass.
                    int dropId = drop.GetInstanceID();
                    FailInfo fail;
                    bool hadFailed = Failed.TryGetValue(dropId, out fail);
                    if (hadFailed && Time.time < fail.Until)
                        continue;

                    Container chest = BestChest(drop, storeRange);
                    if (chest == null)
                        continue;
                    string shared = drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
                    int stack = drop.m_itemData.m_stack;
                    if (TransferService.StoreDrop(chest, drop))
                    {
                        if (hadFailed)
                            Failed.Remove(dropId);
                        moved++;
                        ActivityLog.Move(Loc.T("Auto-store", "Auto-Lagern"), ActivityLog.Ground(), stack, shared, ActivityLog.Chest(chest));
                    }
                    else
                    {
                        if (Failed.Count > 256)
                            Failed.Clear();
                        int tries = hadFailed ? fail.Tries + 1 : 1;
                        float wait = tries >= FailedTriesBeforeLongPause ? LongPauseSeconds : FailedRetrySeconds;
                        Failed[dropId] = new FailInfo { Tries = tries, Until = Time.time + wait };
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (Time.unscaledTime >= _nextErrorLog)
                {
                    _nextErrorLog = Time.unscaledTime + 30f;
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("AutoIntake: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            _cursor = -1; // pass done
        }

        /// <summary>
        /// Fish are never stored from the ground: a live fish is an ItemDrop (with a Fish component), and fish swim at
        /// the surface, so every fish in the loaded sea / a fish pond ended up in a chest. Fish are picked up by hand.
        /// </summary>
        private static bool IsFish(ItemDrop drop)
        {
            return drop.GetComponent<Fish>() != null;
        }

        /// <summary>Begins a pass when it is due (IntakeInterval). False = nothing to do this frame.</summary>
        private static bool StartPass()
        {
            if (Time.time < _next)
                return false;

            _next = Time.time + Mathf.Max(1f, Plugin.Settings.IntakeInterval.Value);
            StationOutput.TickPendingIntakeTags();
            if (!SnapshotDrops())
                return false;

            PlayerScratch.Clear();
            List<Player> live = Player.GetAllPlayers();
            if (live != null && live.Count > 0)
                PlayerScratch.AddRange(live);
            if (PlayerScratch.Count == 0)
                return false;

            _storeRange = Plugin.Settings.StoreReach();
            _loadedArea = Plugin.Settings.AutomationLoadedArea.Value;
            _cap = Plugin.Settings.MaxTransfersPerTick.Value;
            _moved = 0;
            NearbyIndex.BeginStorePass();
            UsableMemo.Clear();
            _cursor = 0;
            return true;
        }

        private static bool SnapshotDrops()
        {
            DropScratch.Clear();
            List<ItemDrop> live = Refs.Drops();
            if (live == null || live.Count == 0)
                return false;

            DropScratch.AddRange(live);
            return DropScratch.Count > 0;
        }

        private static bool AnyPlayerInRange(Vector3 pos, float range)
        {
            for (int i = 0; i < PlayerScratch.Count; i++)
            {
                Player player = PlayerScratch[i];
                if (player == null || player.IsDead() || player.IsTeleporting())
                    continue;
                if (ContainerFilter.Distance(pos, player.transform.position) <= range)
                    return true;
            }
            return false;
        }

        private static readonly List<Container> TargetScratch = new List<Container>(1);

        /// <summary>
        /// Whole loaded base: the chest that took this item type last time (same intake link) first, without a search.
        /// Only when it is gone or cannot take it the base is searched, and the result is remembered.
        /// </summary>
        private static Container BestChest(ItemDrop drop, float storeRange)
        {
            if (!TargetChests.Active || drop.m_itemData.m_shared == null)
                return SearchChest(drop, storeRange);

            string shared = drop.m_itemData.m_shared.m_name;
            Vector3 pos = drop.transform.position;
            int intakeLink = StationOutput.GetIntakeLink(drop);
            Container target = TargetChests.Get(shared, intakeLink);
            if (target != null && NearbyIndex.Usable(target, pos, storeRange)
                && !(intakeLink < 0 && ChestNames.IsNoDump(target)))
            {
                TargetScratch.Clear();
                TargetScratch.Add(target);
                Container hit = PickAccepting(TargetScratch, drop.m_itemData, pos, Plugin.Settings.MustHaveExisting.Value,
                    exactChestLink: intakeLink >= 0 ? intakeLink : -1);
                TargetScratch.Clear();
                if (hit != null)
                    return hit;
            }
            if (target != null)
                TargetChests.Forget(shared, intakeLink);
            if (TargetChests.RecentlyNone(shared, intakeLink))
                return null;

            Container found = SearchChest(drop, storeRange);
            if (found != null)
                TargetChests.Remember(shared, intakeLink, found);
            else
                TargetChests.MarkNone(shared, intakeLink);
            return found;
        }

        private static Container SearchChest(ItemDrop drop, float storeRange)
        {
            NearbyIndex.CollectNear(drop.transform.position, storeRange, ChestScratch, UsableMemo);
            bool mustExist = Plugin.Settings.MustHaveExisting.Value;
            Vector3 pos = drop.transform.position;
            int intakeLink = StationOutput.GetIntakeLink(drop);

            // "No dump" chests skip plain ground pickup; station output (link tag) still reaches them.
            if (intakeLink < 0)
                ChestScratch.RemoveAll(ChestNames.IsNoDump);

            // Linked station output: matching [lN] only (same as StationOutput.TryDepositNear).
            // No linked chest takes it → it stays on the ground, never an untagged chest.
            if (intakeLink >= 1)
            {
                Container linked = PickAccepting(ChestScratch, drop.m_itemData, pos, mustExist, exactChestLink: intakeLink);
                if (linked != null || !mustExist)
                    return linked;
                return ProbePreferLink(drop, pos, mustExist, exactFirst: intakeLink, exactSecond: -1);
            }

            if (intakeLink == 0)
            {
                Container untagged = PickAccepting(ChestScratch, drop.m_itemData, pos, mustExist, exactChestLink: 0);
                if (untagged != null || !mustExist)
                    return untagged;
                return ProbePreferLink(drop, pos, mustExist, exactFirst: 0, exactSecond: -1);
            }

            Container best = PickAccepting(ChestScratch, drop.m_itemData, pos, mustExist, exactChestLink: -1);
            if (best != null || !mustExist)
                return best;

            return ProbePreferLink(drop, pos, mustExist, exactFirst: -1, exactSecond: -1);
        }

        /// <param name="exactChestLink">-1 = any allowed; 0 = untagged; 1–9 = that link only.</param>
        private static Container PickAccepting(
            List<Container> chests,
            ItemDrop.ItemData item,
            Vector3 pos,
            bool mustExist,
            int exactChestLink)
        {
            Container best = null;
            float bestDist = float.MaxValue;
            string shared = mustExist ? ItemIds.SharedName(item) : null;
            for (int i = 0; i < chests.Count; i++)
            {
                Container candidate = chests[i];
                if (candidate == null)
                    continue;
                if (mustExist && !ItemIndex.MightHold(candidate, shared))
                    continue;
                if (exactChestLink >= 0 && StationLink.ChestLinkId(candidate) != exactChestLink)
                    continue;
                if (!ChestPicker.CanAccept(candidate, item, pos, mustExist))
                    continue;
                float dist = ContainerFilter.Distance(pos, candidate.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            return best;
        }

        private static Container ProbePreferLink(
            ItemDrop drop,
            Vector3 pos,
            bool mustExist,
            int exactFirst,
            int exactSecond)
        {
            Container hit = ProbePass(drop, pos, mustExist, exactFirst);
            if (hit != null)
                return hit;
            if (exactSecond >= 0 && exactSecond != exactFirst)
                return ProbePass(drop, pos, mustExist, exactSecond);
            return null;
        }

        private static Container ProbePass(ItemDrop drop, Vector3 pos, bool mustExist, int exactChestLink)
        {
            // No force budget left: TryForceProbeForStore would return false for every chest
            // (no side effect), so the pass ends with null anyway — skip the sort.
            if (!NearbyIndex.HasStoreForceBudget)
                return null;

            ProbeOrder.Clear();
            ProbeOrder.AddRange(ChestScratch);
            ProbeOrder.Sort((a, b) =>
            {
                float da = a == null ? float.MaxValue : ContainerFilter.Distance(pos, a.transform.position);
                float db = b == null ? float.MaxValue : ContainerFilter.Distance(pos, b.transform.position);
                return da.CompareTo(db);
            });

            for (int i = 0; i < ProbeOrder.Count; i++)
            {
                Container candidate = ProbeOrder[i];
                if (candidate == null)
                    continue;
                if (exactChestLink >= 0 && StationLink.ChestLinkId(candidate) != exactChestLink)
                    continue;
                // A loaded chest without the item is not probed (Valheim keeps loaded views current); only chests
                // that might hold it (or whose view is still empty) get the forced load.
                if (mustExist && !ItemIndex.MightHold(candidate, ItemIds.SharedName(drop.m_itemData)))
                    continue;
                if (!NearbyIndex.TryForceProbeForStore(candidate))
                    continue;

                Inventory inv = candidate.GetInventory();
                bool empty = inv == null || inv.NrOfItems() <= 0;
                NearbyIndex.NoteStoreProbeResult(candidate, empty);
                if (empty)
                    continue;

                if (ChestPicker.CanAccept(candidate, drop.m_itemData, pos, mustExist))
                    return candidate;
            }

            return null;
        }
    }
}
