using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// SAC-CATCHUP: smelter-type stations with auto-fill (B) + auto-store (N) catch up the time
    /// their zone was unloaded. Vanilla already processes what was inside (max 1 h); the rest is
    /// stored as idle credit on the station ZDO. When a player with access is in auto-fill range,
    /// the credit is turned into products in packages of one output stack: fuel and ore come out
    /// of chests via StationFeed, products go in via StationOutput (ground drop via vanilla Spawn
    /// if no chest takes them). Gated by ModConfig.CatchUpEnabled; off = no effect at all.
    /// </summary>
    internal static class SmelterCatchUp
    {
        public const string CreditZdoKey = "SAC_catchUp";

        private const double MinGapSeconds = 60.0;
        private const int MaxPackagesPerPulse = 2;
        private const float MinWindPower = 0.05f;
        private const float HandoverSeconds = 6f;

        private static readonly AccessTools.FieldRef<Smelter, bool> HaveRoof =
            AccessTools.FieldRefAccess<Smelter, bool>("m_haveRoof");
        private static readonly AccessTools.FieldRef<Smelter, bool> BlockedSmoke =
            AccessTools.FieldRefAccess<Smelter, bool>("m_blockedSmoke");
        private static readonly MethodInfo SpawnMethod =
            AccessTools.Method(typeof(Smelter), "Spawn", new[] { typeof(string), typeof(int) });

        private static readonly List<Container> NearScratch = new List<Container>(32);
        private static readonly Dictionary<int, float> WaitUntil = new Dictionary<int, float>();
        private static float _nextServerTick;

        // Prefix -> Postfix hand-off for one UpdateSmelter call (main thread, not reentrant).
        private static bool _armed;
        private static double _gap;
        private static double _creditableGap;
        private static int _queue0;
        private static float _bake0;

        public static bool Enabled()
        {
            return Plugin.Settings != null && Plugin.Settings.ModEnabled.Value
                && Plugin.Settings.CraftEnabled.Value && Plugin.Settings.CatchUpEnabled.Value;
        }

        private static float MaxCreditSeconds()
        {
            return Mathf.Clamp(Plugin.Settings.CatchUpMaxHours.Value, 0.5f, 48f) * 3600f;
        }

        private static bool BothOn(Smelter smelter)
        {
            return StationAutoFill.IsOn(smelter) && CookingAutoDrop.IsOn(smelter);
        }

        /// <summary>Host / dedicated: keep CatchUpSince in step with the switch (also after manual cfg edits).</summary>
        internal static void ServerTick()
        {
            if (Plugin.Settings == null || ZNet.instance == null || !AdminUtil.IsServer())
                return;
            if (Time.unscaledTime < _nextServerTick)
                return;
            _nextServerTick = Time.unscaledTime + 5f;

            double now = ZNet.instance.GetTimeSeconds();
            if (now <= 0.0)
                return;

            bool on = Plugin.Settings.CatchUpEnabled.Value;
            double since = Plugin.Settings.CatchUpSince.Value;
            if (on && since <= 0.0)
                Plugin.Settings.CatchUpSince.Value = now;
            else if (!on && since > 0.0)
                Plugin.Settings.CatchUpSince.Value = 0.0;
            else
                return;

            ConfigCommands.SaveConfigFile();
        }

        /// <summary>Called by the catchup command when the switch changes (host and admin client).</summary>
        internal static void OnSwitched(bool on)
        {
            if (Plugin.Settings == null)
                return;
            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;
            Plugin.Settings.CatchUpSince.Value = on && now > 0.0 ? now : 0.0;
        }

        // ---------------- Record idle credit (UpdateSmelter prefix / postfix) ----------------

        internal static void BeforeUpdate(Smelter smelter)
        {
            _armed = false;
            if (smelter == null || !Enabled())
                return;
            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || ZNet.instance == null)
                return;
            if (!BothOn(smelter))
                return;

            ZDO zdo = nv.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            long start = zdo.GetLong(ZDOVars.s_startTime, now);
            double gap = (now - start) / 10000000.0;
            if (gap < MinGapSeconds)
                return;

            double since = Plugin.Settings.CatchUpSince.Value;
            if (since <= 0.0)
                return;
            double creditable = ZNet.instance.GetTimeSeconds() - since;
            if (creditable <= 0.0)
                return;

            _gap = gap;
            _creditableGap = System.Math.Min(gap, creditable);
            _queue0 = zdo.GetInt(ZDOVars.s_queued);
            _bake0 = zdo.GetFloat(ZDOVars.s_bakeTimer);
            _armed = true;
        }

        internal static void AfterUpdate(Smelter smelter)
        {
            if (!_armed)
                return;
            _armed = false;
            if (smelter == null || smelter.m_secPerProduct <= 0f)
                return;
            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return;

            ZDO zdo = nv.GetZDO();
            int queue1 = zdo.GetInt(ZDOVars.s_queued);
            float bake1 = zdo.GetFloat(ZDOVars.s_bakeTimer);
            int made = Mathf.Max(0, _queue0 - queue1);
            double work = made * (double)smelter.m_secPerProduct + bake1 - _bake0;
            float power = Power(smelter);
            // Vanilla ran the whole gap with this power; without power it did nothing and
            // spending would not work either, so no credit.
            if (power < MinWindPower)
                return;

            double busy = System.Math.Max(0.0, work / power);
            double idle = System.Math.Min(_gap - busy, _creditableGap);
            if (idle < MinGapSeconds)
                return;

            float credit = zdo.GetFloat(CreditZdoKey, 0f);
            float next = Mathf.Min(MaxCreditSeconds(), credit + (float)idle);
            if (next <= credit)
                return;
            zdo.Set(CreditZdoKey, next);
        }

        // ---------------- Spend credit (inside the auto-fill pulse) ----------------

        /// <summary>Any smelter in range with catch-up credit left (lets the pulse skip idle ticks).</summary>
        internal static bool HasWorkInRange(List<Smelter> smelters, Vector3 playerPos, float rangeSq)
        {
            if (!Enabled() || smelters == null)
                return false;
            for (int i = 0; i < smelters.Count; i++)
            {
                Smelter smelter = smelters[i];
                if (smelter == null)
                    continue;
                if (ContainerFilter.SqrDistance(playerPos, smelter.transform.position) > rangeSq)
                    continue;
                ZNetView nv = smelter.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid())
                    continue;
                if (nv.GetZDO().GetFloat(CreditZdoKey, 0f) > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Runs inside StationAutoFill.Tick (pulse state: chest reach override already set).
        /// </summary>
        internal static void RunPulse(List<Smelter> smelters, Vector3 playerPos, float rangeSq, Player player)
        {
            if (!Enabled() || smelters == null || player == null)
                return;

            int packages = 0;
            for (int i = 0; i < smelters.Count && packages < MaxPackagesPerPulse; i++)
            {
                Smelter smelter = smelters[i];
                if (smelter == null)
                    continue;
                ZNetView nv = smelter.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid())
                    continue;
                float credit = nv.GetZDO().GetFloat(CreditZdoKey, 0f);
                if (credit <= 0f)
                    continue;
                if (ContainerFilter.SqrDistance(playerPos, smelter.transform.position) > rangeSq)
                    continue;
                if (!PrivateArea.CheckAccess(smelter.transform.position, 0f, false, true))
                    continue;
                if (!MayRunHere(smelter, nv))
                    continue;

                if (!nv.IsOwner())
                    nv.ClaimOwnership();

                if (!BothOn(smelter))
                {
                    Clear(nv);
                    continue;
                }

                StationLink.PushStation(smelter);
                StationFeed.PullOriginOverride = smelter.transform.position;
                try
                {
                    if (RunPackage(smelter, nv, player, credit))
                        packages++;
                }
                finally
                {
                    StationFeed.PullOriginOverride = null;
                    StationLink.Pop();
                }
            }

            if (packages > 0)
                StationAutoFill.RequestSoon();
        }

        /// <summary>Owner goes first; another client takes over after a short wait (same idea as auto-fill).</summary>
        private static bool MayRunHere(Smelter smelter, ZNetView nv)
        {
            int id = smelter.GetInstanceID();
            if (nv.IsOwner())
            {
                WaitUntil.Remove(id);
                return true;
            }
            float now = Time.unscaledTime;
            float until;
            if (!WaitUntil.TryGetValue(id, out until))
            {
                if (WaitUntil.Count > 256)
                    WaitUntil.Clear();
                WaitUntil[id] = now + HandoverSeconds + Random.Range(0f, 4f);
                return false;
            }
            if (now < until)
                return false;
            WaitUntil.Remove(id);
            return true;
        }

        private static bool RunPackage(Smelter smelter, ZNetView nv, Player player, float credit)
        {
            string label = StationOutput.StationLabel(smelter);
            ZDO zdo = nv.GetZDO();

            if (smelter.m_secPerProduct <= 0f || zdo.GetBool(ZDOVars.s_cheated))
                return Stop(nv, label, null);
            if (smelter.m_requiresRoof && !HaveRoof(smelter))
                return Stop(nv, label, Loc.T("no roof", "kein Dach"));
            if (BlockedSmoke(smelter))
                return Stop(nv, label, Loc.T("smoke blocked", "Rauch blockiert"));

            float power = Power(smelter);
            if (power < MinWindPower)
                return false;

            int byTime = Mathf.FloorToInt(credit * power / smelter.m_secPerProduct);
            if (byTime < 1)
                return Stop(nv, label, null);

            List<string> allowed = StationPullFilter.AllowedOreNames(smelter);
            if (allowed == null || allowed.Count == 0)
                return Stop(nv, label, Loc.T("nothing allowed", "nichts erlaubt"));

            int linkId = StationLink.Get(smelter);
            Smelter.ItemConversion conv = null;
            string oreShared = null;
            int oreHave = 0;
            for (int i = 0; i < allowed.Count && conv == null; i++)
            {
                Smelter.ItemConversion c = ConversionFor(smelter, allowed[i]);
                if (c == null)
                    continue;
                int have = RequirementBridge.CountNearby(player, allowed[i], linkId, -1);
                if (have <= 0)
                    continue;
                conv = c;
                oreShared = allowed[i];
                oreHave = have;
            }
            if (conv == null)
                return Stop(nv, label, Loc.T("no ore in chests", "kein Erz in Kisten"));

            int outStack = Mathf.Max(1, conv.m_to.m_itemData.m_shared.m_maxStackSize);
            int n = Mathf.Min(byTime, Mathf.Min(oreHave, outStack));

            string fuelShared = null;
            int fuelPer = 0;
            if (smelter.m_maxFuel > 0 && smelter.m_fuelPerProduct > 0 && smelter.m_fuelItem != null)
            {
                fuelShared = StationFeed.SharedFrom(smelter.m_fuelItem);
                fuelPer = smelter.m_fuelPerProduct;
                int fuelHave = string.IsNullOrEmpty(fuelShared)
                    ? 0
                    : RequirementBridge.CountNearby(player, fuelShared, linkId, -1);
                n = Mathf.Min(n, fuelHave / fuelPer);
                if (n < 1)
                    return Stop(nv, label, Loc.T("no fuel in chests", "kein Brennstoff in Kisten"));
            }

            string outPrefab = conv.m_to.gameObject.name;
            if (!FitsNear(smelter, conv.m_to, n))
                return Stop(nv, label, Loc.T("output chest full", "Ausgabe-Kiste voll"));

            // Fuel first, then only as much ore as that fuel covers: never take ore that cannot be processed.
            int fuelGot = 0;
            if (fuelPer > 0)
            {
                fuelGot = StationFeed.ConsumeFromChests(player, fuelShared, n * fuelPer, linkId);
                n = Mathf.Min(n, fuelGot / fuelPer);
                if (n < 1)
                {
                    GiveBack(smelter, smelter.m_fuelItem, fuelGot);
                    return Stop(nv, label, Loc.T("no fuel in chests", "kein Brennstoff in Kisten"));
                }
            }

            int oreGot = StationFeed.ConsumeFromChests(player, oreShared, n, linkId);
            if (fuelPer > 0)
                GiveBack(smelter, smelter.m_fuelItem, fuelGot - oreGot * fuelPer);
            if (oreGot < 1)
                return Stop(nv, label, Loc.T("no ore in chests", "kein Erz in Kisten"));

            string chestLabel;
            bool inChest = StationOutput.TryDepositNear(smelter, outPrefab, oreGot, out chestLabel);
            if (!inChest)
                SpawnVanilla(smelter, conv.m_from.gameObject.name, oreGot);

            float left = Mathf.Max(0f, credit - oreGot * smelter.m_secPerProduct / power);
            if (left < 1f || !inChest)
                zdo.Set(CreditZdoKey, 0f);
            else
                zdo.Set(CreditZdoKey, left);

            string oreLabel = DisplayFilters.ItemLabel(oreShared);
            string fuelLabel = fuelPer > 0 ? DisplayFilters.ItemLabel(fuelShared) : null;
            string outLabel = DisplayFilters.ItemLabel(conv.m_to.m_itemData.m_shared.m_name);
            string prefix = Loc.T("Catch-up", "Nachholen") + " " + label + ": ";
            string used = oreGot + "x " + oreLabel + (fuelPer > 0 ? " + " + (oreGot * fuelPer) + "x " + fuelLabel : "");
            string made = oreGot + "x " + outLabel + " -> " + (inChest ? chestLabel : ActivityLog.Ground());
            ActivityLog.Add(prefix + Loc.T("used", "verbraucht") + " " + used);
            ActivityLog.Add(prefix + Loc.T("made", "hergestellt") + " " + made);
            Plugin.Log.LogDebug("[CatchUp] " + prefix + "used " + used + ", made " + made
                + " | credit left " + Mathf.RoundToInt(left) + " s");

            Totals sum = TotalsFor(nv, label);
            sum.Seconds += oreGot * smelter.m_secPerProduct / power;
            sum.Add(sum.Used, oreLabel, oreGot);
            if (fuelPer > 0)
                sum.Add(sum.Used, fuelLabel, oreGot * fuelPer);
            sum.Add(sum.Made, outLabel, oreGot);
            if (!inChest)
                Finish(nv, Loc.T("output chest full, rest dropped at station", "Ausgabe-Kiste voll, Rest liegt an der Station"));
            else if (left < 1f)
                Finish(nv, null);
            return true;
        }

        private sealed class Totals
        {
            public string Label;
            public float Seconds;
            public readonly List<KeyValuePair<string, int>> Used = new List<KeyValuePair<string, int>>();
            public readonly List<KeyValuePair<string, int>> Made = new List<KeyValuePair<string, int>>();

            public void Add(List<KeyValuePair<string, int>> list, string item, int amount)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Key == item)
                    {
                        list[i] = new KeyValuePair<string, int>(item, list[i].Value + amount);
                        return;
                    }
                }
                list.Add(new KeyValuePair<string, int>(item, amount));
            }

            public static string Join(List<KeyValuePair<string, int>> list)
            {
                var parts = new List<string>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    parts.Add(list[i].Value + "x " + list[i].Key);
                return string.Join(" + ", parts.ToArray());
            }
        }

        private static readonly Dictionary<int, Totals> Sums = new Dictionary<int, Totals>();

        private static Totals TotalsFor(ZNetView nv, string label)
        {
            int id = nv.GetInstanceID();
            Totals sum;
            if (!Sums.TryGetValue(id, out sum))
            {
                if (Sums.Count > 64)
                    Sums.Clear();
                sum = new Totals { Label = label };
                Sums[id] = sum;
            }
            return sum;
        }

        /// <summary>One summary line per station when its catch-up ends (done or stopped).</summary>
        private static void Finish(ZNetView nv, string reason)
        {
            Totals sum;
            int id = nv.GetInstanceID();
            if (!Sums.TryGetValue(id, out sum))
                return;
            Sums.Remove(id);
            if (sum.Made.Count == 0)
                return;

            int minutes = Mathf.RoundToInt(sum.Seconds / 60f);
            string line = Loc.T("Catch-up", "Nachholen") + " " + sum.Label + " "
                + (string.IsNullOrEmpty(reason) ? Loc.T("done", "fertig") : Loc.T("stopped", "gestoppt") + " (" + reason + ")")
                + ", " + (minutes / 60) + "h " + (minutes % 60) + "m: "
                + Loc.T("used", "verbraucht") + " " + Totals.Join(sum.Used)
                + ", " + Loc.T("made", "hergestellt") + " " + Totals.Join(sum.Made);
            ActivityLog.Add(line);
            Plugin.Log.LogDebug("[CatchUp] " + line);
        }

        private static bool Stop(ZNetView nv, string label, string reason)
        {
            if (nv != null && nv.IsValid() && nv.GetZDO().GetFloat(CreditZdoKey, 0f) > 0f)
            {
                nv.GetZDO().Set(CreditZdoKey, 0f);
                bool hadPackages = Sums.ContainsKey(nv.GetInstanceID());
                Finish(nv, reason);
                if (!hadPackages && !string.IsNullOrEmpty(reason))
                    ActivityLog.Add(Loc.T("Catch-up", "Nachholen") + " " + label + ": "
                        + Loc.T("stopped", "gestoppt") + " (" + reason + ")");
            }
            return false;
        }

        private static void Clear(ZNetView nv)
        {
            if (nv != null && nv.IsValid() && nv.IsOwner())
                nv.GetZDO().Set(CreditZdoKey, 0f);
        }

        private static float Power(Smelter smelter)
        {
            if (smelter.m_windmill == null)
                return 1f;
            if (EnvMan.instance == null)
                return 0f;
            return smelter.m_windmill.GetPowerOutput();
        }

        private static Smelter.ItemConversion ConversionFor(Smelter smelter, string oreShared)
        {
            if (smelter.m_conversion == null)
                return null;
            for (int i = 0; i < smelter.m_conversion.Count; i++)
            {
                Smelter.ItemConversion c = smelter.m_conversion[i];
                if (c == null || c.m_from == null || c.m_to == null)
                    continue;
                if (c.m_from.m_itemData?.m_shared != null && c.m_from.m_itemData.m_shared.m_name == oreShared
                    && c.m_to.m_itemData?.m_shared != null)
                    return c;
            }
            return null;
        }

        /// <summary>Read-only: same chest choice as StationOutput.TryDepositNear, plus a CanAddItem probe.</summary>
        private static bool FitsNear(Smelter smelter, ItemDrop output, int amount)
        {
            if (output?.m_itemData?.m_shared == null || amount <= 0)
                return false;
            string shared = output.m_itemData.m_shared.m_name;
            int linkId = StationLink.Get(smelter);
            bool mustHave = Plugin.Settings == null || Plugin.Settings.MustHaveExisting.Value;

            NearScratch.Clear();
            NearbyIndex.CollectNear(smelter.transform.position, StationOutput.DepositRange(), NearScratch);
            for (int i = 0; i < NearScratch.Count; i++)
            {
                Container chest = NearScratch[i];
                if (chest == null || ChestNames.BlocksDeposit(chest) || !ContainerFilter.IsPlayerBuiltStorage(chest))
                    continue;
                int chestLink = StationLink.ChestLinkId(chest);
                if (linkId >= 1 ? (chestLink != linkId && chestLink != 0) : chestLink != 0)
                    continue;
                NearbyIndex.EnsureInventory(chest, force: true);
                Inventory inv = chest.GetInventory();
                if (inv == null)
                    continue;
                if (mustHave && !inv.HaveItem(shared, true))
                    continue;
                ItemDrop.ItemData probe = output.m_itemData.Clone();
                probe.m_stack = amount;
                if (inv.CanAddItem(probe, amount))
                {
                    NearScratch.Clear();
                    return true;
                }
            }
            NearScratch.Clear();
            return false;
        }

        /// <summary>Leftover fuel from a short package: back into a chest, else on the ground at the station.</summary>
        private static void GiveBack(Smelter smelter, ItemDrop item, int amount)
        {
            if (item == null || amount <= 0)
                return;
            string ignored;
            if (StationOutput.TryDepositNear(smelter, item.gameObject.name, amount, out ignored))
                return;
            DropOnGround(smelter, item, amount);
        }

        private static void DropOnGround(Smelter smelter, ItemDrop item, int amount)
        {
            Transform point = smelter.m_outputPoint != null ? smelter.m_outputPoint : smelter.transform;
            int maxStack = Mathf.Max(1, item.m_itemData.m_shared.m_maxStackSize);
            // Linked smelter: the ground stack may only go into matching [lN] chests.
            int linkId = StationLink.Get(smelter);
            while (amount > 0)
            {
                int stack = Mathf.Min(amount, maxStack);
                amount -= stack;
                ItemDrop drop = Object.Instantiate(item.gameObject, point.position + Vector3.up * 0.5f, point.rotation)
                    .GetComponent<ItemDrop>();
                if (drop == null)
                    continue;
                drop.m_itemData.m_stack = stack;
                ItemDrop.OnCreateNew(drop);
                if (linkId >= 1)
                    StationOutput.SetIntakeLink(drop, linkId);
            }
        }

        /// <summary>No chest took the products: vanilla Spawn (auto-store patch retries, then ground + intake tag).</summary>
        private static void SpawnVanilla(Smelter smelter, string orePrefab, int amount)
        {
            if (SpawnMethod != null)
            {
                try
                {
                    SpawnMethod.Invoke(smelter, new object[] { orePrefab, amount });
                    return;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("[CatchUp] Spawn failed: " + ex.Message);
                }
            }
            Smelter.ItemConversion conv = null;
            for (int i = 0; smelter.m_conversion != null && i < smelter.m_conversion.Count; i++)
            {
                Smelter.ItemConversion c = smelter.m_conversion[i];
                if (c != null && c.m_from != null && c.m_from.gameObject.name == orePrefab)
                    conv = c;
            }
            if (conv != null && conv.m_to != null)
                DropOnGround(smelter, conv.m_to, amount);
        }

        internal static void AppendHover(ref string text, Smelter smelter)
        {
            if (smelter == null || !Enabled())
                return;
            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return;
            float credit = nv.GetZDO().GetFloat(CreditZdoKey, 0f);
            if (credit < 60f)
                return;
            int minutes = Mathf.RoundToInt(credit / 60f);
            text += "\n<color=#9fd8ff>" + Loc.T("Catching up", "Holt nach") + ": "
                + (minutes / 60) + "h " + (minutes % 60) + "m</color>";
        }
    }

    // SAC-CATCHUP: measure idle time before / after vanilla's own offline catch-up.
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterCatchUpRecordPatch
    {
        private static void Prefix(Smelter __instance)
        {
            SmelterCatchUp.BeforeUpdate(__instance);
        }

        private static void Postfix(Smelter __instance)
        {
            SmelterCatchUp.AfterUpdate(__instance);
        }
    }
}
