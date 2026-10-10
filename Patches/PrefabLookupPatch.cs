using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// The mod's own pieces / items for ZNetScene.GetPrefab(hash) when vanilla does not know them (one patch for all
    /// of them instead of one per feature: GetPrefab runs for every object that is loaded). Only fills a null result.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(int))]
    internal static class ZNetSceneGetPrefabSacPatch
    {
        private static void Postfix(int hash, ref GameObject __result)
        {
            if (__result != null)
                return;
            GameObject found = DisplayPrefab.PrefabForHash(hash);
            if (found == null)
                found = FeedTroughPrefab.PrefabForHash(hash);
            if (found == null)
                found = ScarecrowPrefab.PrefabForHash(hash);
            if (found == null)
                found = MushroomCrops.PrefabForHash(hash);
            if (found == null)
                found = WildCrops.PrefabForHash(hash);
            if (found == null)
                found = QueenHives.PrefabForHash(hash);
            if (found != null)
                __result = found;
        }
    }
}
