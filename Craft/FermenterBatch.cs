using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Fermenter batches: more of the SAME mead base can go in during the first AddWindowSeconds
    /// of a fermentation (up to FermenterBatch, F10). All of them finish in the time of one; the
    /// tap gives batch × m_producedItems. Count lives in the fermenter ZDO; only the owner
    /// changes it (SAC_FermAdd RPC), like vanilla RPC_AddItem / RPC_Tap.
    /// </summary>
    internal static class FermenterBatch
    {
        internal const string ZdoKey = "SAC_fermBatch";
        internal static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();
        internal const string RpcAdd = "SAC_FermAdd";
        internal const float AddWindowSeconds = 60f;
        private const int StatusFermenting = 1;
        private const int StatusReady = 3;

        private static readonly MethodInfo GetStatusM = AccessTools.Method(typeof(Fermenter), "GetStatus");
        private static readonly MethodInfo GetTimeM = AccessTools.Method(typeof(Fermenter), "GetFermentationTime");
        private static readonly MethodInfo GetConversionM =
            AccessTools.Method(typeof(Fermenter), "GetItemConversion", new[] { typeof(int) });
        private static readonly FieldInfo DelayedItemF = AccessTools.Field(typeof(Fermenter), "m_delayedTapItem");
        private static readonly FieldInfo DelayedCheatedF = AccessTools.Field(typeof(Fermenter), "m_delayedTapItemCheated");

        /// <summary>Tap in flight: fermenter instance → batch count read before vanilla cleared it.</summary>
        private static readonly Dictionary<int, int> PendingTap = new Dictionary<int, int>();

        internal static int MaxBatch()
        {
            if (Plugin.Settings == null)
                return 1;
            return Mathf.Clamp(Mathf.RoundToInt(Plugin.Settings.FermenterBatch.Value), 1,
                Mathf.RoundToInt(ModConfig.MaxStationCap));
        }

        /// <summary>Bases in the current fermentation (1 when content but no SAC count yet).</summary>
        internal static int Count(Fermenter f)
        {
            ZDO zdo = Zdo(f);
            if (zdo == null || zdo.GetInt(ZDOVars.s_content) == 0)
                return 0;
            return Mathf.Max(1, zdo.GetInt(ZdoKeyHash, 1));
        }

        internal static int ContentHash(Fermenter f)
        {
            ZDO zdo = Zdo(f);
            return zdo != null ? zdo.GetInt(ZDOVars.s_content) : 0;
        }

        /// <summary>Shared name of the base currently fermenting, or null.</summary>
        internal static string ContentShared(Fermenter f)
        {
            int hash = ContentHash(f);
            GameObject prefab = hash != 0 && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData?.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
        }

        /// <summary>Fermenting, still inside the add window and below the batch limit.</summary>
        internal static bool CanAddMore(Fermenter f)
        {
            if (f == null || MaxBatch() <= 1 || GetStatusM == null || GetTimeM == null)
                return false;
            try
            {
                if (FastCall.Int(GetStatusM, f) != StatusFermenting)
                    return false;
                double t = FastCall.Double(GetTimeM, f);
                if (t < 0 || t > AddWindowSeconds)
                    return false;
            }
            catch
            {
                return false;
            }
            return Count(f) < MaxBatch();
        }

        /// <summary>Put one matching base from the user's bag into the running batch.</summary>
        internal static bool TryAddFrom(Fermenter f, Humanoid user, ItemDrop.ItemData item)
        {
            if (f == null || user == null || !CanAddMore(f))
                return false;
            int content = ContentHash(f);
            Inventory inv = user.GetInventory();
            if (inv == null)
                return false;
            if (item == null)
                item = FindMatching(inv, content);
            if (item == null || item.m_dropPrefab == null
                || item.m_dropPrefab.name.GetStableHashCode() != content)
                return false;
            if (!inv.RemoveOneItem(item))
                return false;
            ZNetView nv = f.GetComponent<ZNetView>();
            nv.InvokeRPC(RpcAdd, content);
            return true;
        }

        /// <summary>Auto-fill: n more of the content already in the barrel (items already taken).</summary>
        internal static void AddCounted(Fermenter f, int nameHash, int n)
        {
            ZNetView nv = f != null ? f.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid())
                return;
            for (int i = 0; i < n; i++)
                nv.InvokeRPC(RpcAdd, nameHash);
        }

        private static ItemDrop.ItemData FindMatching(Inventory inv, int content)
        {
            List<ItemDrop.ItemData> items = inv.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name.GetStableHashCode() == content)
                    return it;
            }
            return null;
        }

        private static ZDO Zdo(Fermenter f)
        {
            ZNetView nv = f != null ? f.GetComponent<ZNetView>() : null;
            return nv != null && nv.IsValid() ? nv.GetZDO() : null;
        }

        // ---- owner side

        internal static void Register(Fermenter f)
        {
            ZNetView nv = f != null ? f.GetComponent<ZNetView>() : null;
            if (nv == null || nv.GetZDO() == null)
                return;
            nv.Register<int>(RpcAdd, (sender, hash) => RPC_Add(f, hash));
        }

        private static void RPC_Add(Fermenter f, int nameHash)
        {
            ZNetView nv = f != null ? f.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return;
            // Re-check on the owner: same base, still in the window, room left.
            if (ContentHash(f) != nameHash || !CanAddMore(f))
            {
                ReturnBase(f, nameHash);
                return;
            }
            nv.GetZDO().Set(ZdoKeyHash, Count(f) + 1);
            if (f.m_addedEffects != null)
                f.m_addedEffects.Create(f.transform.position, f.transform.rotation);
        }

        /// <summary>Rejected add (race / window closed): the base drops next to the barrel.</summary>
        private static void ReturnBase(Fermenter f, int nameHash)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(nameHash) : null;
            if (prefab == null)
                return;
            Vector3 pos = f.transform.position + Vector3.up;
            ItemDrop.OnCreateNew(Object.Instantiate(prefab, pos, Quaternion.identity));
        }

        internal static void OnFreshAdd(Fermenter f)
        {
            ZNetView nv = f != null ? f.GetComponent<ZNetView>() : null;
            if (nv != null && nv.IsValid() && nv.IsOwner())
                nv.GetZDO().Set(ZdoKeyHash, 1);
        }

        internal static void OnTap(Fermenter f)
        {
            ZNetView nv = f != null ? f.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || GetStatusM == null)
                return;
            // Vanilla RPC_Tap only taps a ready barrel — keep the count otherwise.
            try
            {
                if (FastCall.Int(GetStatusM, f) != StatusReady)
                    return;
            }
            catch
            {
                return;
            }
            PendingTap[f.GetInstanceID()] = Count(f);
            nv.GetZDO().Set(ZdoKeyHash, 0);
        }

        /// <summary>After vanilla DelayedTap spawned one base worth: spawn the rest of the batch.</summary>
        internal static void AfterDelayedTap(Fermenter f)
        {
            int count;
            if (f == null || !PendingTap.TryGetValue(f.GetInstanceID(), out count))
                return;
            PendingTap.Remove(f.GetInstanceID());
            if (count <= 1 || GetConversionM == null || DelayedItemF == null)
                return;

            int item = (int)DelayedItemF.GetValue(f);
            var conv = GetConversionM.Invoke(f, new object[] { item }) as Fermenter.ItemConversion;
            if (conv == null || conv.m_to == null)
                return;
            ZDO zdo = Zdo(f);
            bool cheated = ((DelayedCheatedF != null && (bool)DelayedCheatedF.GetValue(f))
                || (zdo != null && zdo.GetBool(ZDOVars.s_cheated))) && !PlayerProfile.s_bypassCheatChecks;
            Vector3 basePos = f.m_outputPoint != null ? f.m_outputPoint.position : f.transform.position + Vector3.up;
            int extra = (count - 1) * conv.m_producedItems;
            for (int i = 0; i < extra; i++)
            {
                Vector3 pos = basePos + Vector3.up * 0.3f + Random.insideUnitSphere * 0.15f;
                ItemDrop.OnCreateNew(Object.Instantiate(conv.m_to, pos, Quaternion.identity), cheated);
            }
        }

        /// <summary>Auto-store tap (straight into a chest): clear the batch count too.</summary>
        internal static void ClearAfterDeposit(Fermenter f)
        {
            ZDO zdo = Zdo(f);
            if (zdo != null)
                zdo.Set(ZdoKeyHash, 0);
        }

        internal static void AppendHover(ref string text, Fermenter f)
        {
            if (f == null || MaxBatch() <= 1 || ContentHash(f) == 0)
                return;
            text += "\n" + Loc.T("Batch", "Charge") + ": " + Count(f) + "/" + MaxBatch();
            if (CanAddMore(f))
                text += " — " + Loc.T("add more with [E]", "mit [E] nachlegen");
        }
    }

    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class FermenterBatchAwakePatch
    {
        private static void Postfix(Fermenter __instance)
        {
            FermenterBatch.Register(__instance);
        }
    }

    /// <summary>UseItem (hotbar) path: vanilla AddItem only accepts an empty barrel.</summary>
    [HarmonyPatch(typeof(Fermenter), "AddItem")]
    internal static class FermenterBatchAddItemPatch
    {
        private static bool Prefix(Fermenter __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (!FermenterBatch.CanAddMore(__instance))
                return true;
            __result = FermenterBatch.TryAddFrom(__instance, user, item);
            return false;
        }
    }

    [HarmonyPatch(typeof(Fermenter), "RPC_AddItem")]
    internal static class FermenterBatchFreshAddPatch
    {
        private static void Prefix(Fermenter __instance)
        {
            // Vanilla starts a new fermentation only when empty; count restarts at 1.
            if (FermenterBatch.ContentHash(__instance) == 0)
                FermenterBatch.OnFreshAdd(__instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "RPC_Tap")]
    internal static class FermenterBatchTapPatch
    {
        private static void Prefix(Fermenter __instance)
        {
            if (FermenterBatch.ContentHash(__instance) != 0)
                FermenterBatch.OnTap(__instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "DelayedTap")]
    internal static class FermenterBatchDelayedTapPatch
    {
        private static void Postfix(Fermenter __instance)
        {
            FermenterBatch.AfterDelayedTap(__instance);
        }
    }
}
