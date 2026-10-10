using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Auto-store (N) on a smelter / kiln / blast furnace / refinery / windmill / spinning wheel: the station
    /// pauses when its chests are full (chests are there, none has room for the product) or when it has a
    /// link but no chest with that link is in reach. Nothing is used up, nothing drops on the ground, and a
    /// notice ("Chest full" / "No linked chest") shows above it.
    /// An unlinked station with no chest in reach keeps running (the product drops on the ground) and only shows
    /// "No chest nearby".
    /// Grill: food cannot wait on the grill (it burns), so it still goes to the ground; only the notice shows.
    /// </summary>
    internal static class StationChestFull
    {
        private const float CacheSeconds = 2f;

        /// <summary>Set on the station while it waits for chest room. Synced, so every player sees it switched off.</summary>
        internal const string HeldKey = "SAC_chestFull";
        internal static readonly int HeldKeyHash = HeldKey.GetStableHashCode();

        private static readonly MethodInfo GetQueuedOre = AccessTools.Method(typeof(Smelter), "GetQueuedOre");
        private static readonly MethodInfo UpdateState = AccessTools.Method(typeof(Smelter), "UpdateState");

        private struct Verdict
        {
            public float Until;
            public StationOutput.OutputState State;
        }

        private static readonly Dictionary<int, Verdict> Cache = new Dictionary<int, Verdict>();

        internal static string NoticeText(StationOutput.OutputState state)
        {
            switch (state)
            {
                case StationOutput.OutputState.NoLinkedChest:
                    return Loc.T("No linked chest", "Keine verlinkte Kiste");
                case StationOutput.OutputState.NoChestNearby:
                    return Loc.T("No chest nearby", "Keine Kiste in der Nähe");
                default:
                    return Loc.T("Chest full", "Kiste voll");
            }
        }

        /// <summary>Why this smelter has to wait (Ok = it runs). Checked at most every 2 s.</summary>
        internal static StationOutput.OutputState Check(Smelter smelter)
        {
            if (smelter == null || Plugin.Settings == null || GetQueuedOre == null)
                return StationOutput.OutputState.Ok;
            if (!CookingAutoDrop.IsOn(smelter))
                return StationOutput.OutputState.Ok;
            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return StationOutput.OutputState.Ok;

            int id = smelter.GetInstanceID();
            Verdict v;
            if (Cache.TryGetValue(id, out v) && Time.unscaledTime < v.Until)
                return v.State;

            StationOutput.OutputState state = Evaluate(smelter, nv.GetZDO());
            if (Cache.Count > 512)
                Cache.Clear();
            Cache[id] = new Verdict { Until = Time.unscaledTime + CacheSeconds, State = state };
            return state;
        }

        private static StationOutput.OutputState Evaluate(Smelter smelter, ZDO zdo)
        {
            if (zdo == null)
                return StationOutput.OutputState.Ok;

            // What comes out next: the ore being smelted, or (not running) a stack still waiting to be spawned.
            string ore = FastCall.Object(GetQueuedOre, smelter) as string;
            bool running = !string.IsNullOrEmpty(ore) && (smelter.m_maxFuel <= 0 || zdo.GetFloat(ZDOVars.s_fuel) > 0f);
            int amount = 1;
            if (running)
            {
                // Stack smelters (kiln) hand out the waiting stack in one go: the chest needs room for all of it.
                if (smelter.m_spawnStack)
                    amount = zdo.GetInt(ZDOVars.s_spawnAmount) + 1;
            }
            else
            {
                ore = zdo.GetString(ZDOVars.s_spawnOre);
                amount = Mathf.Max(1, zdo.GetInt(ZDOVars.s_spawnAmount));
            }
            if (string.IsNullOrEmpty(ore))
                return StationOutput.OutputState.Ok;

            string prefab;
            if (!CookingAutoDrop.TryGetSmelterOutputPrefab(smelter, ore, out prefab))
                return StationOutput.OutputState.Ok;
            return StationOutput.OutputStateOf(smelter, prefab, amount);
        }

        /// <summary>
        /// Paused tick: vanilla's own clock stamp is moved to now, so the waiting time is not
        /// credited as free production when the station runs again.
        /// </summary>
        internal static void Hold(Smelter smelter, StationOutput.OutputState state)
        {
            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid() && ZNet.instance != null)
                nv.GetZDO().Set(ZDOVars.s_startTime, ZNet.instance.GetTime().Ticks);
            SetHeld(smelter, true);
            // The skipped UpdateSmelter would have refreshed fire / smoke / animation: show "off" now.
            if (UpdateState != null)
                UpdateState.Invoke(smelter, null);
            StationMissingLabel.SetNotice(smelter, NoticeText(state));
        }

        internal static bool IsHeld(Smelter smelter)
        {
            ZNetView nv = smelter != null ? smelter.GetComponent<ZNetView>() : null;
            return nv != null && nv.IsValid() && nv.GetZDO().GetInt(HeldKeyHash, 0) != 0;
        }

        /// <summary>Owner only, written only when it changes (no extra network traffic while paused).</summary>
        internal static void SetHeld(Smelter smelter, bool held)
        {
            ZNetView nv = smelter != null ? smelter.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return;
            ZDO zdo = nv.GetZDO();
            if ((zdo.GetInt(HeldKeyHash, 0) != 0) != held)
                zdo.Set(HeldKeyHash, held ? 1 : 0);
        }

        /// <summary>
        /// Grill: no chest with its link / no chest at all in reach shows the notice the whole time (food on it or
        /// not), until a chest is linked / placed. Chest full is only noted when an item is really popped
        /// (<see cref="NoteGrill"/>), because only then the finished item is known.
        /// </summary>
        internal static void TickGrill(CookingStation station)
        {
            if (station == null)
                return;
            ZNetView nv = station.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return;
            int id = station.GetInstanceID();
            StationOutput.OutputState state;
            Verdict v;
            if (Cache.TryGetValue(id, out v) && Time.unscaledTime < v.Until)
                state = v.State;
            else
            {
                state = StationOutput.ReachStateOf(station);
                if (Cache.Count > 512)
                    Cache.Clear();
                Cache[id] = new Verdict { Until = Time.unscaledTime + CacheSeconds, State = state };
            }
            if (state == StationOutput.OutputState.NoLinkedChest || state == StationOutput.OutputState.NoChestNearby)
                StationMissingLabel.SetNotice(station, NoticeText(state));
        }

        private static readonly Dictionary<int, float> NoteUntil = new Dictionary<int, float>();

        /// <summary>
        /// Stations that hand their product to a chest (beehive, sap extractor, fermenter, scarecrow): the notice stays
        /// while there is no chest with its link / no chest in reach, and (with a product) while the chests are full.
        /// <paramref name="seconds"/> must be longer than the station's own tick. clearWhenOk: a notice from
        /// <see cref="NoteOutput"/> is not wiped by the next check.
        /// </summary>
        internal static void TickOutput(Component station, string productPrefab, int amount, float seconds, bool clearWhenOk)
        {
            if (station == null)
                return;
            int id = station.GetInstanceID();
            StationOutput.OutputState state;
            Verdict v;
            if (Cache.TryGetValue(id, out v) && Time.unscaledTime < v.Until)
                state = v.State;
            else
            {
                state = StationOutput.ReachStateOf(station);
                if (state == StationOutput.OutputState.Ok && !string.IsNullOrEmpty(productPrefab) && amount > 0)
                    state = StationOutput.OutputStateOf(station, productPrefab, amount);
                if (Cache.Count > 512)
                    Cache.Clear();
                Cache[id] = new Verdict { Until = Time.unscaledTime + CacheSeconds, State = state };
            }
            if (state != StationOutput.OutputState.Ok)
                StationMissingLabel.SetNotice(station, NoticeText(state), seconds);
            else if (clearWhenOk)
                StationMissingLabel.ClearNotice(station);
        }

        /// <summary>A product could not be stored (scarecrow harvest): short notice when the cause is clear. At most every 2 s per station.</summary>
        internal static void NoteOutput(Component station, string prefab, int amount)
        {
            if (station == null)
                return;
            int id = station.GetInstanceID();
            float until;
            if (NoteUntil.TryGetValue(id, out until) && Time.unscaledTime < until)
                return;
            if (NoteUntil.Count > 512)
                NoteUntil.Clear();
            NoteUntil[id] = Time.unscaledTime + CacheSeconds;
            StationOutput.OutputState state = StationOutput.OutputStateOf(station, prefab, amount);
            if (state != StationOutput.OutputState.Ok)
                StationMissingLabel.SetNotice(station, NoticeText(state));
        }

        /// <summary>Grill: a finished item could not go into a chest. Notice only when the chests are full / not linked.</summary>
        internal static void NoteGrill(Component station, string item)
        {
            StationOutput.OutputState state = StationOutput.OutputStateOf(station, item, 1);
            if (state != StationOutput.OutputState.Ok)
                StationMissingLabel.SetNotice(station, NoticeText(state));
        }
    }

    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterChestFullPausePatch
    {
        // First: when paused, the other UpdateSmelter prefixes (catch-up bookkeeping) are skipped too.
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Smelter __instance)
        {
            StationOutput.OutputState state = StationChestFull.Check(__instance);
            if (state == StationOutput.OutputState.Ok)
            {
                StationChestFull.SetHeld(__instance, false);
                StationMissingLabel.ClearNotice(__instance);
                return true;
            }
            if (state == StationOutput.OutputState.NoChestNearby)
            {
                // Unlinked, no chest in reach: the station keeps running (product drops), only the notice shows.
                StationChestFull.SetHeld(__instance, false);
                StationMissingLabel.SetNotice(__instance, StationChestFull.NoticeText(state));
                return true;
            }
            StationChestFull.Hold(__instance, state);
            return false;
        }
    }

    /// <summary>A station that waits for chest room counts as not active: fire, smoke and animation go off for everyone.</summary>
    [HarmonyPatch(typeof(Smelter), nameof(Smelter.IsActive))]
    internal static class SmelterHeldIsActivePatch
    {
        private static void Postfix(Smelter __instance, ref bool __result)
        {
            if (__result && StationChestFull.IsHeld(__instance))
                __result = false;
        }
    }
}
