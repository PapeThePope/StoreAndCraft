using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Nearby chests via Awake registration, with OverlapSphere fallback.
    /// Inventory Load is throttled (never every rescan) to avoid base lag spikes.
    /// </summary>
    internal static class NearbyIndex
    {
        private static readonly List<Container> Registered = new List<Container>();
        private static readonly HashSet<int> RegisteredIds = new HashSet<int>();
        private static readonly List<Container> Cached = new List<Container>();
        private static readonly Dictionary<int, float> LastInventoryLoad = new Dictionary<int, float>();
        /// <summary>Skip force-probes on chests that still looked empty after a forced Load.</summary>
        private static readonly Dictionary<int, float> EmptyProbeUntil = new Dictionary<int, float>();
        private static float _nextScan;
        private static float _nextPrune;
        private static Vector3 _lastOrigin;
        private static int _cacheFrame = -1;
        private static int _storeForceBudget;
        /// <summary>
        /// Cache key of one count in this frame. A struct instead of a built string: the crafting list asks for
        /// hundreds of counts per frame, and a string key allocated several strings per call.
        /// Origin rounds to 0.1 m and range to 0.01 m, as the old text key did.
        /// </summary>
        private struct CountKey : System.IEquatable<CountKey>
        {
            public string Name;
            public int Quality;
            public bool LeaveOne;
            public int Range100;
            public int Link;
            public int X10;
            public int Y10;
            public int Z10;

            public bool Equals(CountKey o)
            {
                return Quality == o.Quality && LeaveOne == o.LeaveOne && Range100 == o.Range100 && Link == o.Link
                    && X10 == o.X10 && Y10 == o.Y10 && Z10 == o.Z10 && Name == o.Name;
            }

            public override bool Equals(object obj)
            {
                return obj is CountKey && Equals((CountKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Name != null ? Name.GetHashCode() : 0;
                    h = h * 31 + Quality;
                    h = h * 31 + (LeaveOne ? 1 : 0);
                    h = h * 31 + Range100;
                    h = h * 31 + Link;
                    h = h * 31 + X10;
                    h = h * 31 + Y10;
                    h = h * 31 + Z10;
                    return h;
                }
            }
        }

        private static readonly Dictionary<CountKey, int> CountCache = new Dictionary<CountKey, int>();
        private static readonly Dictionary<CountKey, ChestSummary.Aggregate> AggCache = new Dictionary<CountKey, ChestSummary.Aggregate>();
        private static readonly Dictionary<CountKey, int> AggSeen = new Dictionary<CountKey, int>();
        private static readonly List<Container> CountChests = new List<Container>(64);
        private static readonly List<Container> CountScratch = new List<Container>(32);
        private const float RescanMove = 2.5f;
        private const float IdleRescanSeconds = 5f;
        private const float MoveRescanSeconds = 1.75f;
        private const float InventoryLoadCooldown = 4f;
        private const float EmptyProbeBackoff = 20f;
        private const int MaxForceLoadsPerStorePass = 5;
        /// <summary>
        /// Physics fallback at most this often. An empty Cached skips the TickAt throttle, so
        /// without this OverlapSphere ran every frame far from chests. Chests register via
        /// Container.Awake / BootstrapExisting; the fallback only catches the rest.
        /// </summary>
        private const float OverlapFallbackSeconds = 10f;
        private static float _nextOverlap;

        public static IReadOnlyList<Container> Current
        {
            get { return Cached; }
        }

        /// <summary>Every registered container (loaded), for indexes that cover the whole registry.</summary>
        public static IReadOnlyList<Container> AllRegistered
        {
            get { return Registered; }
        }

        public static void Register(Container container)
        {
            if (container == null)
                return;
            int id = container.GetInstanceID();
            if (!RegisteredIds.Add(id))
                return;
            Registered.Add(container);
            ItemIndex.MarkDirty(container);
        }

        public static void Unregister(Container container)
        {
            if (container == null)
                return;
            int id = container.GetInstanceID();
            if (!RegisteredIds.Remove(id))
                return;
            Registered.Remove(container);
            ItemIndex.Remove(container);
            ChestSummary.Forget(Refs.InventoryField(container));
            LastInventoryLoad.Remove(id);
            EmptyProbeUntil.Remove(id);
        }

        public static void BootstrapExisting()
        {
            // Scene objects only (inactive ones included), not every prefab / asset like FindObjectsOfTypeAll:
            // the loop below drops everything outside a scene anyway. Runs at Game.Start and on every spawn.
            Container[] all = Object.FindObjectsByType<Container>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all == null)
                return;
            foreach (Container c in all)
            {
                if (c == null || !c.gameObject.scene.IsValid())
                    continue;
                Register(c);
            }
        }

        public static void Tick()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            TickAt(player.transform.position);
        }

        /// <summary>
        /// Refresh the nearby-chest cache around an origin. Dedicated auto-store has
        /// no local player, so Tick() must not wipe this cache.
        /// </summary>
        public static void TickAt(Vector3 origin)
        {
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value)
            {
                Cached.Clear();
                return;
            }

            if (Time.unscaledTime >= _nextPrune)
            {
                PruneDead();
                if (Registered.Count == 0)
                    BootstrapExisting();
                _nextPrune = Time.unscaledTime + 5f;
            }

            float range = ScanRange();
            bool moved = Vector3.Distance(origin, _lastOrigin) > RescanMove;
            if (Time.time < _nextScan && !moved && Cached.Count > 0)
                return;

            Rescan(origin, range);
            _lastOrigin = origin;
            _nextScan = Time.time + (moved ? MoveRescanSeconds : IdleRescanSeconds);
        }

        /// <summary>Bypass idle throttle — used by remote-dump buddy ping.</summary>
        public static void ForceRescanAt(Vector3 origin, float range = -1f)
        {
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value)
            {
                Cached.Clear();
                return;
            }

            PruneDead();
            if (Registered.Count == 0)
                BootstrapExisting();

            if (range <= 0f)
                range = ScanRange();
            Rescan(origin, range);
            _lastOrigin = origin;
            _nextScan = Time.time + IdleRescanSeconds;
        }

        public static float AccessRange()
        {
            return ScanRange();
        }

        public static float ScanRange()
        {
            if (Plugin.Settings == null)
                return 20f;
            if (Plugin.Settings.AutomationLoadedArea.Value)
                return ModConfig.LoadedAreaReach;
            return Plugin.Settings.MaxGameplayRange();
        }

        /// <summary>The same checks CollectNear does for one chest (for a remembered chest that skips the search).</summary>
        public static bool Usable(Container container, Vector3 origin, float range)
        {
            if (IsDestroyed(container))
                return false;
            if (ContainerFilter.SqrDistance(origin, container.transform.position) > range * range)
                return false;
            return ReadyAndUsable(container);
        }

        private struct UsableEntry
        {
            public bool Usable;
            public uint Revision;
            public float Until;
        }

        private static readonly Dictionary<int, UsableEntry> UsableCache = new Dictionary<int, UsableEntry>();
        private const float UsableSeconds = 1f;

        /// <summary>
        /// IsReady + ContainerFilter.PlayerMayUse (valid, not fully ignored, ward access), remembered per chest:
        /// it runs for every chest of every chest search. A name / mode change bumps the chest ZDO revision and is
        /// seen at once (also from other players); a ward switched on / off shows within UsableSeconds.
        /// PlayerMayUse does not depend on the origin (the ward check uses the chest position).
        /// </summary>
        private static bool ReadyAndUsable(Container container)
        {
            ZNetView nv = Refs.View(container);
            if (nv == null || !nv.IsValid())
                return false;
            ZDO zdo = nv.GetZDO();
            uint rev = zdo != null ? zdo.DataRevision : 0u;
            int id = container.GetInstanceID();
            float now = Time.unscaledTime;
            UsableEntry e;
            if (UsableCache.TryGetValue(id, out e) && e.Revision == rev && now < e.Until)
                return e.Usable;
            bool usable = ContainerFilter.PlayerMayUse(container, container.transform.position);
            if (UsableCache.Count > 8192)
                UsableCache.Clear();
            UsableCache[id] = new UsableEntry { Usable = usable, Revision = rev, Until = now + UsableSeconds };
            return usable;
        }

        /// <summary>
        /// Load chest inventory from ZDO at most once per cooldown (or force).
        /// Used by craft counts / dump checks — never from the idle rescan loop.
        /// </summary>
        public static void EnsureInventory(Container container, bool force = false)
        {
            if (container == null)
                return;

            int id = container.GetInstanceID();
            float now = Time.unscaledTime;
            float last;
            if (!force && LastInventoryLoad.TryGetValue(id, out last) && now - last < InventoryLoadCooldown)
                return;

            ContainerFilter.RefreshInventory(container, force);
            LastInventoryLoad[id] = now;
            PendingChestDebit.OnInventoryLoaded(container);

            Inventory inv = container.GetInventory();
            if (inv != null && inv.NrOfItems() > 0)
                EmptyProbeUntil.Remove(id);
        }

        /// <summary>
        /// Reset the per-pass force-Load budget used by AutoIntake on store misses.
        /// </summary>
        public static void BeginStorePass()
        {
            _storeForceBudget = MaxForceLoadsPerStorePass;
        }

        /// <summary>
        /// Force-Load when a store pass found no target. Budget + empty backoff keep cost bounded.
        /// </summary>
        public static bool TryForceProbeForStore(Container container)
        {
            if (container == null || _storeForceBudget <= 0)
                return false;

            int id = container.GetInstanceID();
            float until;
            if (EmptyProbeUntil.TryGetValue(id, out until) && Time.unscaledTime < until)
                return false;

            _storeForceBudget--;
            EnsureInventory(container, force: true);
            return true;
        }

        /// <summary>
        /// After a force probe: backoff empty chests so we do not burn budget every intake tick.
        /// </summary>
        public static void NoteStoreProbeResult(Container container, bool inventoryEmpty)
        {
            if (container == null)
                return;

            int id = container.GetInstanceID();
            if (inventoryEmpty)
                EmptyProbeUntil[id] = Time.unscaledTime + EmptyProbeBackoff;
            else
                EmptyProbeUntil.Remove(id);
        }

        /// <summary>Clear empty backoff (e.g. player opened the chest).</summary>
        public static void MarkInventorySeen(Container container)
        {
            if (container == null)
                return;
            EmptyProbeUntil.Remove(container.GetInstanceID());
            EnsureInventory(container, force: true);
        }

        public static void Rescan(Vector3 origin, float range)
        {
            Cached.Clear();
            CountCache.Clear();
            ChestSummary.Version++;
            _cacheFrame = Time.frameCount;
            if (range <= 0f)
                return;

            float rangeSq = range * range;
            var seen = new HashSet<int>();
            for (int i = Registered.Count - 1; i >= 0; i--)
            {
                Container container = Registered[i];
                if (IsDestroyed(container))
                {
                    RemoveAt(i);
                    continue;
                }

                if (ContainerFilter.SqrDistance(origin, container.transform.position) > rangeSq)
                    continue;

                if (!ReadyAndUsable(container))
                    continue;

                int id = container.GetInstanceID();
                if (!seen.Add(id))
                    continue;

                Cached.Add(container);
            }

            if (Cached.Count == 0 && Time.unscaledTime >= _nextOverlap)
            {
                _nextOverlap = Time.unscaledTime + OverlapFallbackSeconds;
                FillFromOverlap(origin, range, rangeSq, seen);
            }
        }

        private static void FillFromOverlap(Vector3 origin, float range, float rangeSq, HashSet<int> seen)
        {
            Collider[] hits = Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null)
                return;

            foreach (Collider hit in hits)
            {
                if (hit == null)
                    continue;

                Container container = hit.GetComponentInParent<Container>();
                if (container == null || IsDestroyed(container))
                    continue;

                Register(container);

                if (ContainerFilter.SqrDistance(origin, container.transform.position) > rangeSq)
                    continue;

                if (!ReadyAndUsable(container))
                    continue;

                int id = container.GetInstanceID();
                if (!seen.Add(id))
                    continue;

                Cached.Add(container);
            }
        }

        public static void CollectNear(Vector3 origin, float range, List<Container> dest)
        {
            if (dest == null)
                return;
            dest.Clear();
            if (range <= 0f)
                return;

            if (Time.unscaledTime >= _nextPrune)
            {
                PruneDead();
                if (Registered.Count == 0)
                    BootstrapExisting();
                _nextPrune = Time.unscaledTime + 5f;
            }

            float rangeSq = range * range;
            for (int i = 0; i < Registered.Count; i++)
            {
                Container container = Registered[i];
                if (IsDestroyed(container))
                    continue;
                if (ContainerFilter.SqrDistance(origin, container.transform.position) > rangeSq)
                    continue;
                if (!ReadyAndUsable(container))
                    continue;
                dest.Add(container);
            }
        }

        /// <summary>
        /// CollectNear for one AutoIntake pass: same result and registry order, but IsReady +
        /// PlayerMayUse are evaluated once per chest per pass (memo). Both only depend on the
        /// chest (PlayerMayUse ignores its origin; ward check uses the chest position), not on
        /// the drop, item or player. Distance is checked first so far chests skip the reflection.
        /// Caller clears the memo at the start of every pass.
        /// </summary>
        public static void CollectNear(Vector3 origin, float range, List<Container> dest, Dictionary<int, bool> usableMemo)
        {
            if (usableMemo == null)
            {
                CollectNear(origin, range, dest);
                return;
            }
            if (dest == null)
                return;
            dest.Clear();
            if (range <= 0f)
                return;

            if (Time.unscaledTime >= _nextPrune)
            {
                PruneDead();
                if (Registered.Count == 0)
                    BootstrapExisting();
                _nextPrune = Time.unscaledTime + 5f;
            }

            float rangeSq = range * range;
            for (int i = 0; i < Registered.Count; i++)
            {
                Container container = Registered[i];
                if (IsDestroyed(container))
                    continue;
                if (ContainerFilter.SqrDistance(origin, container.transform.position) > rangeSq)
                    continue;
                int id = container.GetInstanceID();
                bool usable;
                if (!usableMemo.TryGetValue(id, out usable))
                {
                    usable = ReadyAndUsable(container);
                    usableMemo[id] = usable;
                }
                if (usable)
                    dest.Add(container);
            }
        }

        /// <summary>False when TryForceProbeForStore can no longer probe anything this pass.</summary>
        public static bool HasStoreForceBudget => _storeForceBudget > 0;

        public static List<Container> Within(Vector3 origin, float range)
        {
            var result = new List<Container>();
            float rangeSq = range * range;
            foreach (Container c in Cached)
            {
                if (c == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, c.transform.position) <= rangeSq)
                    result.Add(c);
            }
            return result;
        }

        public static void InvalidateCounts()
        {
            CountCache.Clear();
            ChestSummary.Version++;
        }

        private static readonly Dictionary<string, int> SnapshotScratch = new Dictionary<string, int>();

        /// <summary>
        /// One-pass chest scan for auto-fill: spendable counts by shared name (LeaveOne applied per chest).
        /// Prefer already-loaded inventories; only ZDO-load empty/unknown chests (cooldown still applies).
        /// </summary>
        public static Dictionary<string, int> SnapshotSpendable(
            Vector3 origin,
            float range,
            bool leaveOne)
        {
            var totals = new Dictionary<string, int>();
            if (range <= 0f)
                return totals;

            float rangeSq = range * range;
            foreach (Container c in Cached)
            {
                if (c == null || ChestNames.IsIgnored(c))
                    continue;
                if (ContainerFilter.SqrDistance(origin, c.transform.position) > rangeSq)
                    continue;

                Inventory inv = c.GetInventory();
                if (inv == null || inv.NrOfItems() <= 0)
                {
                    // Force once: idle cooldown + empty skip left cooking autofill blind
                    // after the chest-wipe guard (pulse Quiet'd food for 25s).
                    EnsureInventory(c, force: true);
                    inv = c.GetInventory();
                }
                if (inv == null || inv.NrOfItems() <= 0)
                    continue;

                SnapshotScratch.Clear();
                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                {
                    if (item?.m_shared == null || item.m_stack <= 0)
                        continue;
                    string key = item.m_shared.m_name;
                    int n;
                    SnapshotScratch.TryGetValue(key, out n);
                    SnapshotScratch[key] = n + item.m_stack;
                }

                foreach (KeyValuePair<string, int> pair in SnapshotScratch)
                {
                    int n = pair.Value - PendingChestDebit.Of(c, pair.Key);
                    if (n < 0)
                        n = 0;
                    if (leaveOne && n > 0 && ItemIds.ShouldLeaveOne(true, pair.Key))
                        n -= 1;
                    if (n <= 0)
                        continue;
                    int total;
                    totals.TryGetValue(pair.Key, out total);
                    totals[pair.Key] = total + n;
                }
            }

            SnapshotScratch.Clear();
            return totals;
        }

        /// <summary>
        /// Spendable counts from chests near any of the given origins (station-centered autofill).
        /// </summary>
        public static Dictionary<string, int> SnapshotSpendableAround(
            List<Vector3> origins,
            float range,
            bool leaveOne)
        {
            var totals = new Dictionary<string, int>();
            if (origins == null || origins.Count == 0 || range <= 0f)
                return totals;

            var seen = new HashSet<int>();
            var near = new List<Container>(32);
            for (int o = 0; o < origins.Count; o++)
            {
                near.Clear();
                CollectNear(origins[o], range, near);
                for (int i = 0; i < near.Count; i++)
                {
                    Container c = near[i];
                    if (c == null || ChestNames.IsIgnored(c))
                        continue;
                    int id = c.GetInstanceID();
                    if (!seen.Add(id))
                        continue;

                    Inventory inv = c.GetInventory();
                    if (inv == null || inv.NrOfItems() <= 0)
                    {
                        EnsureInventory(c, force: true);
                        inv = c.GetInventory();
                    }
                    if (inv == null || inv.NrOfItems() <= 0)
                        continue;

                    SnapshotScratch.Clear();
                    foreach (ItemDrop.ItemData item in inv.GetAllItems())
                    {
                        if (item?.m_shared == null || item.m_stack <= 0)
                            continue;
                        string key = item.m_shared.m_name;
                        int n;
                        SnapshotScratch.TryGetValue(key, out n);
                        SnapshotScratch[key] = n + item.m_stack;
                    }

                    foreach (KeyValuePair<string, int> pair in SnapshotScratch)
                    {
                        int n = pair.Value - PendingChestDebit.Of(c, pair.Key);
                        if (n < 0)
                            n = 0;
                        if (leaveOne && n > 0 && ItemIds.ShouldLeaveOne(true, pair.Key))
                            n -= 1;
                        if (n <= 0)
                            continue;
                        int total;
                        totals.TryGetValue(pair.Key, out total);
                        totals[pair.Key] = total + n;
                    }
                }
            }

            SnapshotScratch.Clear();
            return totals;
        }

        public static int CountItem(
            Vector3 origin,
            float range,
            string sharedName,
            bool leaveOne,
            int quality = -1,
            int linkId = int.MinValue)
        {
            if (string.IsNullOrEmpty(sharedName))
                return 0;

            if (_cacheFrame != Time.frameCount)
            {
                CountCache.Clear();
                _cacheFrame = Time.frameCount;
            }

            int useLink = linkId == int.MinValue ? StationLink.ActiveId : linkId;
            float useRange = range > 0f ? range : StationFeed.ActivePullRange();
            // Whole loaded base: every origin sees the same chests (PlayerMayUse ignores the origin), so all
            // stations / the player share one count per link instead of one per position.
            bool wholeBase = useRange >= ModConfig.LoadedAreaReach && Plugin.Settings != null && Plugin.Settings.AutomationLoadedArea.Value;
            var key = new CountKey
            {
                Name = sharedName,
                Quality = quality,
                LeaveOne = leaveOne,
                Range100 = Mathf.RoundToInt(useRange * 100f),
                Link = useLink,
                X10 = wholeBase ? 0 : Mathf.RoundToInt(origin.x * 10f),
                Y10 = wholeBase ? 0 : Mathf.RoundToInt(origin.y * 10f),
                Z10 = wholeBase ? 0 : Mathf.RoundToInt(origin.z * 10f)
            };
            int cached;
            if (CountCache.TryGetValue(key, out cached))
                return cached;

            // Origin standing still since an earlier frame: one pass fills a chest-set total and every
            // ingredient after that is a lookup. A moving origin would rebuild it each frame, so it uses
            // the plain per-name loop instead.
            var aggKey = key;
            aggKey.Name = null;
            aggKey.Quality = StationFeed.PullOriginOverride.HasValue ? 1 : 0;
            ChestSummary.Aggregate agg;
            AggCache.TryGetValue(aggKey, out agg);
            int total;
            if (agg != null && agg.IsCurrent)
            {
                total = agg.Get(sharedName, quality);
            }
            else if (agg != null || SeenBefore(aggKey))
            {
                if (agg == null)
                {
                    if (AggCache.Count >= 32)
                        AggCache.Clear();
                    agg = new ChestSummary.Aggregate();
                    AggCache[aggKey] = agg;
                }
                CollectCountChests(origin, useRange, useLink, CountChests);
                agg.Begin();
                foreach (Container c in CountChests)
                    agg.AddChest(c, c.GetInventory(), leaveOne);
                agg.End();
                CountChests.Clear();
                total = agg.Get(sharedName, quality);
            }
            else
            {
                total = 0;
                CollectCountChests(origin, useRange, useLink, CountChests);
                foreach (Container c in CountChests)
                {
                    int n = c.GetInventory().CountItems(sharedName, quality, true);
                    n -= PendingChestDebit.Of(c, sharedName);
                    if (n < 0)
                        n = 0;
                    if (leaveOne && n > 0 && ItemIds.ShouldLeaveOne(true, sharedName))
                        n -= 1;
                    if (n > 0)
                        total += n;
                }
                CountChests.Clear();
            }

            CountCache[key] = total;
            return total;
        }

        /// <summary>True when this chest-set key was already asked for in an earlier frame.</summary>
        private static bool SeenBefore(CountKey aggKey)
        {
            int frame;
            bool seen = AggSeen.TryGetValue(aggKey, out frame) && frame != Time.frameCount;
            if (!seen && !AggSeen.ContainsKey(aggKey))
            {
                if (AggSeen.Count >= 256)
                    AggSeen.Clear();
                AggSeen[aggKey] = Time.frameCount;
            }
            return seen;
        }

        /// <summary>Chests CountItem counts: allowed by the link, inside range, inventory loaded.</summary>
        private static void CollectCountChests(Vector3 origin, float useRange, int useLink, List<Container> into)
        {
            into.Clear();
            float craftSq = useRange * useRange;
            // Auto-fill counts around the station; the player-centered cache can miss chests
            // behind a station at the edge of range (Consume already uses CollectNear).
            IEnumerable<Container> source = Cached;
            if (StationFeed.PullOriginOverride.HasValue)
            {
                CollectNear(origin, useRange, CountScratch);
                source = CountScratch;
            }
            foreach (Container c in source)
            {
                if (c == null || !StationLink.ChestAllowed(c, useLink))
                    continue;

                if (ContainerFilter.SqrDistance(origin, c.transform.position) > craftSq)
                    continue;

                // Same as SnapshotSpendable: after the empty-load wipe-guard, chests can
                // look empty for the cooldown window. Auto-fill then Quiet's ("no chests")
                // even though the ZDO still has items — force once when local inv is empty.
                Inventory inv = c.GetInventory();
                if (inv == null || inv.NrOfItems() <= 0)
                    EnsureInventory(c, force: true);
                else
                    EnsureInventory(c);

                if (c.GetInventory() != null)
                    into.Add(c);
            }
        }

        private static void PruneDead()
        {
            for (int i = Registered.Count - 1; i >= 0; i--)
            {
                if (IsDestroyed(Registered[i]))
                    RemoveAt(i);
            }
        }

        private static void RemoveAt(int index)
        {
            Container c = Registered[index];
            Registered.RemoveAt(index);
            ItemIndex.Remove(c);
            // The field itself, not GetInventory: other mods' GetInventory patches throw on a destroyed container.
            if ((object)c != null)
                ChestSummary.Forget(Refs.InventoryField(c));
            if (c != null)
            {
                int id = c.GetInstanceID();
                RegisteredIds.Remove(id);
                LastInventoryLoad.Remove(id);
                EmptyProbeUntil.Remove(id);
                UsableCache.Remove(id);
            }
            else if (RegisteredIds.Count != Registered.Count)
            {
                RegisteredIds.Clear();
                LastInventoryLoad.Clear();
                EmptyProbeUntil.Clear();
                foreach (Container x in Registered)
                {
                    if (x != null)
                        RegisteredIds.Add(x.GetInstanceID());
                }
            }
        }

        private static bool IsDestroyed(Container container)
        {
            return container == null;
        }
    }
}
