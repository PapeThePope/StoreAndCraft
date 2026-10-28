using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Per cooking station / beehive / smelter: when on, finished product goes into a nearby chest
    /// (fallback: ground drop for cook/hive; smelter skips Spawn→floor). Hover inventory closed + N.
    /// Independent from Auto-fill (B).
    /// </summary>
    internal static class CookingAutoDrop
    {
        public const string ZdoKey = "SAC_autoDrop";
        public static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();

        private static readonly MethodInfo HaveDoneItem =
            AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
        private static readonly MethodInfo GetSlot =
            AccessTools.Method(typeof(CookingStation), "GetSlot");
        private static readonly MethodInfo SetSlot =
            AccessTools.Method(typeof(CookingStation), "SetSlot");
        private static readonly MethodInfo IsItemDone =
            AccessTools.Method(typeof(CookingStation), "IsItemDone");
        private static readonly Type CookStatusType =
            AccessTools.Inner(typeof(CookingStation), "Status")
            ?? AccessTools.TypeByName("CookingStation+Status");
        private static readonly object StatusNotDone =
            CookStatusType != null ? Enum.ToObject(CookStatusType, 0) : null;
        private static readonly MethodInfo BeeGetHoneyLevel =
            AccessTools.Method(typeof(Beehive), "GetHoneyLevel");
        private static readonly MethodInfo BeeExtract =
            AccessTools.Method(typeof(Beehive), "Extract");
        private static readonly MethodInfo BeeResetLevel =
            AccessTools.Method(typeof(Beehive), "ResetLevel");
        private static readonly MethodInfo SmelterGetConversion =
            AccessTools.Method(typeof(Smelter), "GetItemConversion", new[] { typeof(string) });
        private static readonly MethodInfo FermenterGetStatus =
            AccessTools.Method(typeof(Fermenter), "GetStatus");
        private static readonly MethodInfo FermenterGetConversion =
            AccessTools.Method(typeof(Fermenter), "GetItemConversion", new[] { typeof(int) });
        private const int FermenterStatusReady = 3;
        private static readonly System.Collections.Generic.HashSet<Fermenter> FermenterGroundTag =
            new System.Collections.Generic.HashSet<Fermenter>();

        public static bool IsOn(ZNetView nv)
        {
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return false;
            return nv.GetZDO().GetInt(ZdoKeyHash, 0) != 0;
        }

        public static bool IsOn(CookingStation station)
        {
            return station != null && IsOn(station.GetComponent<ZNetView>());
        }

        public static bool IsOn(Beehive hive)
        {
            return hive != null && IsOn(hive.GetComponent<ZNetView>());
        }

        public static bool IsOn(Smelter smelter)
        {
            return smelter != null && IsOn(smelter.GetComponent<ZNetView>());
        }

        public static bool IsOn(SapCollector sap)
        {
            return sap != null && IsOn(sap.GetComponent<ZNetView>());
        }

        public static bool IsOn(Fermenter fermenter)
        {
            return fermenter != null && IsOn(fermenter.GetComponent<ZNetView>());
        }

        public static void AppendHover(ref string text, Fermenter fermenter)
        {
            if (fermenter == null)
                return;
            AppendHoverLine(ref text, fermenter.GetComponent<ZNetView>());
        }

        public static void AppendHover(ref string text, CookingStation station)
        {
            if (station == null)
                return;
            AppendHoverLine(ref text, station.GetComponent<ZNetView>());
        }

        public static void AppendHover(ref string text, Beehive hive)
        {
            if (hive == null)
                return;
            AppendHoverLine(ref text, hive.GetComponent<ZNetView>());
        }

        public static void AppendHover(ref string text, SapCollector sap)
        {
            if (sap == null)
                return;
            AppendHoverLine(ref text, sap.GetComponent<ZNetView>());
        }

        public static void AppendHover(ref string text, Smelter smelter)
        {
            if (smelter == null)
                return;
            AppendHoverLine(ref text, smelter.GetComponent<ZNetView>());
        }

        private static void AppendHoverLine(ref string text, ZNetView nv)
        {
            if (nv == null || !nv.IsValid())
                return;

            string key = KeyUtil.Format(Plugin.Settings.AutoDropKey.Value);
            if (string.IsNullOrEmpty(key))
                key = "N";

            if (string.IsNullOrEmpty(text))
                text = "";

            text += "\n[<color=yellow><b>" + key + "</b></color>] "
                + Loc.T("Auto-store", "Auto-Lagern")
                + " (" + (IsOn(nv) ? Loc.T("on → chest", "an → Kiste") : Loc.T("off", "aus")) + ")";
        }

        public static bool TryToggle()
        {
            if (InventoryGui.IsVisible() || StationFilterMenu.IsOpen || DisplayTypeMenu.IsOpen || DisplayRangeMenu.IsOpen)
                return false;

            Player player = Player.m_localPlayer;
            if (player == null)
                return false;

            CookingStation station = HoveredStation();
            Beehive hive = station == null ? HoveredBeehive() : null;
            Smelter smelter = station == null && hive == null ? StationPullFilter.HoveredSmelter() : null;
            Fermenter fermenter = station == null && hive == null && smelter == null
                ? StationPullFilter.HoveredFermenter() : null;
            SapCollector sap = station == null && hive == null && smelter == null && fermenter == null
                ? HoveredSap() : null;
            Component target = station != null ? station
                : hive != null ? (Component)hive
                : smelter != null ? (Component)smelter
                : fermenter != null ? (Component)fermenter
                : sap;
            ZNetView nv = target != null ? target.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return false;

            Vector3 pos = target.transform.position;
            if (!PrivateArea.CheckAccess(pos, 0f, false, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return true;
            }

            if (!nv.IsOwner())
                nv.ClaimOwnership();

            bool next = !IsOn(nv);
            nv.GetZDO().Set(ZdoKeyHash, next ? 1 : 0);

            player.Message(
                MessageHud.MessageType.Center,
                next
                    ? Loc.T("Auto-store on (chests)", "Auto-Lagern an (Kisten)")
                    : Loc.T("Auto-store off", "Auto-Lagern aus"),
                0, null, false);
            ActivityLog.Note(StationOutput.StationLabel(target),
                next ? Loc.T("Auto-store on", "Auto-Lagern an") : Loc.T("Auto-store off", "Auto-Lagern aus"));
            return true;
        }

        public static void TryPopDone(CookingStation station)
        {
            if (station == null || !IsOn(station))
                return;

            ZNetView nv = station.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return;

            if (HaveDoneItem == null || !FastCall.Bool(HaveDoneItem, station))
                return;

            StationAutoFill.RequestSoon();

            if (TryDepositDoneToChest(station, nv))
                return;

            Vector3 point = DropPoint(station);
            ActivityLog.Note(Loc.T("Auto-store", "Auto-Lagern"), StationOutput.StationLabel(station) + " → "
                + ActivityLog.Ground() + " (" + Loc.T("no matching chest", "keine passende Kiste") + ")");
            StationOutput.QueueIntakeLinkTag(point, StationLink.Get(station), null);
            nv.InvokeRPC("RPC_RemoveDoneItem", point, 1);
        }

        /// <summary>
        /// SetSlot arguments for the running game version: Valheim 1.0.21 added an optional
        /// "long sender" parameter, and MethodInfo.Invoke needs every parameter, so trailing
        /// optional ones are filled with their defaults.
        /// </summary>
        private static object[] SetSlotArgs(params object[] args)
        {
            ParameterInfo[] ps = SetSlot.GetParameters();
            if (ps.Length <= args.Length)
                return args;
            object[] full = new object[ps.Length];
            Array.Copy(args, full, args.Length);
            for (int p = args.Length; p < ps.Length; p++)
                full[p] = ps[p].HasDefaultValue ? ps[p].DefaultValue : Type.Missing;
            return full;
        }

        private static bool TryDepositDoneToChest(CookingStation station, ZNetView nv)
        {
            if (GetSlot == null || SetSlot == null || IsItemDone == null || StatusNotDone == null)
                return false;
            if (station.m_slots == null)
                return false;

            for (int i = 0; i < station.m_slots.Length; i++)
            {
                object[] args = { i, null, 0f, null, false };
                GetSlot.Invoke(station, args);
                string itemName = args[1] as string;
                if (string.IsNullOrEmpty(itemName))
                    continue;
                if (!(bool)IsItemDone.Invoke(station, new object[] { itemName }))
                    continue;

                string label;
                if (!StationOutput.TryDepositNear(station, itemName, 1, out label, out Container outChest))
                {
                    StationChestFull.NoteGrill(station, itemName);
                    return false;
                }

                SetSlot.Invoke(station, SetSlotArgs(i, "", 0f, StatusNotDone, false));
                nv.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", i, "");
                ActivityLog.ToChest(StationOutput.StationLabel(station), 1, label, outChest);
                return true;
            }

            return false;
        }

        // Beehive / sap extractor / fermenter tick every 2-10 s: the notice must outlast that.
        private const float NoticeSlow = 25f;

        internal static void TickHiveNotice(Beehive hive)
        {
            ZNetView nv = hive.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || BeeGetHoneyLevel == null)
                return;
            int honey = 0;
            try
            {
                honey = FastCall.Int(BeeGetHoneyLevel, hive);
            }
            catch
            {
            }
            string prefab = hive.m_honeyItem != null ? hive.m_honeyItem.gameObject.name : "Honey";
            StationChestFull.TickOutput(hive, prefab, honey, NoticeSlow, true);
        }

        internal static void TickSapNotice(SapCollector sap)
        {
            ZNetView nv = sap.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null)
                return;
            int level = nv.GetZDO().GetInt(ZDOVars.s_level, 0);
            string prefab = sap.m_spawnItem != null ? sap.m_spawnItem.gameObject.name : "Sap";
            StationChestFull.TickOutput(sap, prefab, level, NoticeSlow, true);
        }

        internal static void TickFermenterNotice(Fermenter fermenter)
        {
            ZNetView nv = fermenter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null)
                return;
            if (FermenterGetStatus == null || FermenterGetConversion == null)
                return;
            string prefab = null;
            int count = 0;
            try
            {
                if (FastCall.Int(FermenterGetStatus, fermenter) == FermenterStatusReady)
                {
                    int content = nv.GetZDO().GetInt(ZDOVars.s_content);
                    Fermenter.ItemConversion conv =
                        FermenterGetConversion.Invoke(fermenter, new object[] { content }) as Fermenter.ItemConversion;
                    if (conv != null && conv.m_to != null && conv.m_producedItems > 0)
                    {
                        prefab = conv.m_to.gameObject.name;
                        count = conv.m_producedItems * Mathf.Max(1, FermenterBatch.Count(fermenter));
                    }
                }
            }
            catch
            {
                return;
            }
            StationChestFull.TickOutput(fermenter, prefab, count, NoticeSlow, true);
        }

        public static void TryExtractHoney(Beehive hive)
        {
            if (hive == null || !IsOn(hive))
                return;

            ZNetView nv = hive.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return;

            if (BeeGetHoneyLevel == null)
                return;

            int honey;
            try
            {
                honey = FastCall.Int(BeeGetHoneyLevel, hive);
            }
            catch
            {
                return;
            }

            if (honey <= 0)
                return;

            StationAutoFill.RequestSoon();

            string honeyPrefab = hive.m_honeyItem != null
                ? hive.m_honeyItem.gameObject.name
                : "Honey";
            string label;
            if (StationOutput.TryDepositNear(hive, honeyPrefab, honey, out label, out Container outChest))
            {
                if (BeeResetLevel != null)
                    BeeResetLevel.Invoke(hive, null);
                else if (nv.GetZDO() != null)
                    nv.GetZDO().Set(ZDOVars.s_level, 0);
                ActivityLog.ToChest(StationOutput.StationLabel(hive), honey, label, outChest);
                return;
            }

            if (BeeExtract != null)
            {
                try
                {
                    BeeExtract.Invoke(hive, null);
                }
                catch
                {
                }
            }

            ActivityLog.Move(Loc.T("Auto-store", "Auto-Lagern"), StationOutput.StationLabel(hive),
                honey, honeyPrefab, ActivityLog.Ground());
            Vector3 hivePos = hive.transform.position + Vector3.up * 0.5f;
            StationOutput.QueueIntakeLinkTag(hivePos, StationLink.Get(hive), honeyPrefab);
        }

        /// <summary>
        /// Sap extractor: the collected sap goes into a chest. No chest → it stays in the extractor (nothing is
        /// dropped on the ground). The extractor has no input, so there is no auto-fill for it.
        /// </summary>
        public static void TryExtractSap(SapCollector sap)
        {
            if (sap == null || !IsOn(sap))
                return;

            ZNetView nv = sap.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null)
                return;

            int level = nv.GetZDO().GetInt(ZDOVars.s_level, 0);
            if (level <= 0)
                return;

            string prefab = sap.m_spawnItem != null ? sap.m_spawnItem.gameObject.name : "Sap";
            string label;
            if (!StationOutput.TryDepositNear(sap, prefab, level, out label, out Container outChest))
                return;

            nv.GetZDO().Set(ZDOVars.s_level, 0);
            nv.InvokeRPC(ZNetView.Everybody, "RPC_UpdateEffects");
            ActivityLog.ToChest(StationOutput.StationLabel(sap), level, label, outChest);
        }

        /// <summary>Ready barrel: put the mead into a chest; no chest → vanilla tap to the ground.</summary>
        public static void TryTapFermenter(Fermenter fermenter)
        {
            if (fermenter == null || !IsOn(fermenter))
                return;

            ZNetView nv = fermenter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null)
                return;
            if (FermenterGetStatus == null || FermenterGetConversion == null)
                return;

            int status;
            try
            {
                status = FastCall.Int(FermenterGetStatus, fermenter);
            }
            catch
            {
                return;
            }
            if (status != FermenterStatusReady)
                return;

            int content = nv.GetZDO().GetInt(ZDOVars.s_content);
            Fermenter.ItemConversion conv =
                FermenterGetConversion.Invoke(fermenter, new object[] { content }) as Fermenter.ItemConversion;
            if (conv == null || conv.m_to == null || conv.m_producedItems <= 0)
                return;

            string prefab = conv.m_to.gameObject.name;
            // Batch barrels give m_producedItems per base.
            int count = conv.m_producedItems * Mathf.Max(1, FermenterBatch.Count(fermenter));

            StationAutoFill.RequestSoon();

            string label;
            if (StationOutput.TryDepositNear(fermenter, prefab, count, out label, out Container outChest))
            {
                nv.GetZDO().Set(ZDOVars.s_content, 0);
                nv.GetZDO().Set(ZDOVars.s_startTime, 0L);
                nv.GetZDO().Set(ZDOVars.s_cheatedQueued, false);
                FermenterBatch.ClearAfterDeposit(fermenter);
                if (fermenter.m_tapEffects != null)
                    fermenter.m_tapEffects.Create(fermenter.transform.position, fermenter.transform.rotation);
                ActivityLog.ToChest(StationOutput.StationLabel(fermenter), count, label, outChest);
                return;
            }

            ActivityLog.Move(Loc.T("Auto-store", "Auto-Lagern"), StationOutput.StationLabel(fermenter),
                count, prefab, ActivityLog.Ground());
            FermenterGroundTag.Add(fermenter);
            nv.InvokeRPC("RPC_Tap");
        }

        /// <summary>After vanilla DelayedTap spawned the mead: tag it so intake obeys the barrel link.</summary>
        internal static void TagFermenterDrops(Fermenter fermenter)
        {
            if (fermenter == null || !FermenterGroundTag.Remove(fermenter))
                return;
            Transform output = fermenter.m_outputPoint;
            Vector3 pos = output != null ? output.position : fermenter.transform.position + Vector3.up * 0.5f;
            StationOutput.QueueIntakeLinkTag(pos, StationLink.Get(fermenter), null);
        }

        private static Vector3 DropPoint(CookingStation station)
        {
            Transform spawn = station.m_spawnPoint;
            if (spawn != null)
                return spawn.position;
            return station.transform.position + Vector3.up * 0.5f;
        }

        private static CookingStation HoveredStation()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;
            GameObject hover = player.GetHoverObject();
            if (hover == null)
                return null;
            return hover.GetComponentInParent<CookingStation>();
        }

        private static SapCollector HoveredSap()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;
            GameObject hover = player.GetHoverObject();
            if (hover == null)
                return null;
            return hover.GetComponentInParent<SapCollector>();
        }

        private static Beehive HoveredBeehive()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;
            GameObject hover = player.GetHoverObject();
            if (hover == null)
                return null;
            return hover.GetComponentInParent<Beehive>();
        }

        internal static bool TryGetSmelterOutputPrefab(Smelter smelter, string ore, out string prefabName)
        {
            prefabName = null;
            // An empty name is valid: stations without an input item (Frigid Kiln: m_from == null) spawn with ore "".
            // For every other smelter GetItemConversion("") finds nothing, so this stays false there as before.
            if (smelter == null || ore == null || SmelterGetConversion == null)
                return false;
            object conv = SmelterGetConversion.Invoke(smelter, new object[] { ore });
            if (conv == null)
                return false;
            FieldInfo toField = AccessTools.Field(conv.GetType(), "m_to");
            if (toField == null)
                return false;
            ItemDrop to = toField.GetValue(conv) as ItemDrop;
            if (to == null)
                return false;
            prefabName = to.gameObject.name;
            return true;
        }
    }

    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    internal static class CookingAutoDropUpdatePatch
    {
        private static void Postfix(CookingStation __instance)
        {
            if (__instance == null || !CookingAutoDrop.IsOn(__instance))
                return;
            StationChestFull.TickGrill(__instance);
            CookingAutoDrop.TryPopDone(__instance);
        }
    }

    [HarmonyPatch(typeof(Beehive), "UpdateBees")]
    internal static class BeehiveAutoDropUpdatePatch
    {
        private static void Postfix(Beehive __instance)
        {
            if (__instance == null || !CookingAutoDrop.IsOn(__instance))
                return;
            CookingAutoDrop.TickHiveNotice(__instance);
            CookingAutoDrop.TryExtractHoney(__instance);
        }
    }

    [HarmonyPatch(typeof(SapCollector), "UpdateTick")]
    internal static class SapCollectorAutoDropUpdatePatch
    {
        private static void Postfix(SapCollector __instance)
        {
            if (__instance == null || !CookingAutoDrop.IsOn(__instance))
                return;
            CookingAutoDrop.TickSapNotice(__instance);
            CookingAutoDrop.TryExtractSap(__instance);
        }
    }

    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.GetHoverText))]
    internal static class SapCollectorAutoDropHoverPatch
    {
        private static void Postfix(SapCollector __instance, ref string __result)
        {
            CookingAutoDrop.AppendHover(ref __result, __instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "SlowUpdate")]
    internal static class FermenterAutoDropUpdatePatch
    {
        private static void Postfix(Fermenter __instance)
        {
            if (__instance == null || !CookingAutoDrop.IsOn(__instance))
                return;
            CookingAutoDrop.TickFermenterNotice(__instance);
            CookingAutoDrop.TryTapFermenter(__instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "DelayedTap")]
    internal static class FermenterAutoDropTagPatch
    {
        private static void Postfix(Fermenter __instance)
        {
            CookingAutoDrop.TagFermenterDrops(__instance);
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
    internal static class BeehiveAutoDropHoverPatch
    {
        private static void Postfix(Beehive __instance, ref string __result)
        {
            CookingAutoDrop.AppendHover(ref __result, __instance);
        }
    }

    /// <summary>
    /// When Auto-store (N) is on, kiln/smelter finished bars go to a chest instead of the floor.
    /// Independent from Auto-fill (B). Failed deposit → vanilla Spawn, then tag drop so intake
    /// only uses matching / untagged chests (never a different link).
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class SmelterAutoDropDepositPatch
    {
        private static int _tagLink = -1;
        private static Vector3 _tagPos;
        private static string _tagPrefab;

        private static bool Prefix(Smelter __instance, string ore, int stack)
        {
            _tagLink = -1;
            if (__instance == null || stack <= 0 || ore == null)
                return true;
            if (!CookingAutoDrop.IsOn(__instance))
                return true;

            ZNetView nv = __instance.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return true;

            string prefab;
            if (!CookingAutoDrop.TryGetSmelterOutputPrefab(__instance, ore, out prefab))
                return true;

            string label;
            if (!StationOutput.TryDepositNear(__instance, prefab, stack, out label, out Container outChest))
            {
                _tagLink = StationLink.Get(__instance);
                _tagPrefab = prefab;
                Transform output = __instance.m_outputPoint;
                _tagPos = output != null
                    ? output.position
                    : __instance.transform.position + Vector3.up * 0.5f;
                ActivityLog.Move(Loc.T("Auto-store", "Auto-Lagern"), StationOutput.StationLabel(__instance),
                    stack, prefab, ActivityLog.Ground());
                return true;
            }

            if (__instance.m_produceEffects != null)
                __instance.m_produceEffects.Create(__instance.transform.position, __instance.transform.rotation);

            ActivityLog.ToChest(StationOutput.StationLabel(__instance), stack, label, outChest);
            return false;
        }

        private static void Postfix(Smelter __instance, string ore, int stack)
        {
            if (_tagLink < 0)
                return;
            StationOutput.QueueIntakeLinkTag(_tagPos, _tagLink, _tagPrefab);
            _tagLink = -1;
        }
    }
}
