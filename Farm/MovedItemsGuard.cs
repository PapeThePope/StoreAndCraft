using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Up to 1.5 StoreAndCraft had its own beehives, wild crops, mushrooms, queen bees and spores; since 1.6.0 they
    /// belong to the mod Forager's Garden. A player who updates without it would lose them: Valheim drops every item
    /// whose prefab it does not know when a bag or chest loads, and the host deletes every built object whose prefab it
    /// does not know. So while Forager's Garden is NOT installed: the items are kept as plain stand-ins with the same
    /// prefab names (no use, the name says which mod they need), the built objects as invisible placeholders. With
    /// Forager's Garden nothing is registered here, it brings the real prefabs and everything is back.
    /// This class can be deleted once nobody updates from 1.5 any more (and its line in PrefabLookupPatch).
    /// </summary>
    internal static class MovedItemsGuard
    {
        private const string ForagersGardenGuid = "com.morda.foragersgarden";

        // Prefab name (as saved in bags and chests), vanilla item it is copied from, name shown.
        private static readonly string[][] Items =
        {
            new[] { "sac_queen_ice", "QueenBee", "Ice Queen Bee" },
            new[] { "sac_queen_jelly", "QueenBee", "Royal Jelly Queen Bee" },
            new[] { "sac_queen_crystal", "QueenBee", "Crystal Queen Bee" },
            new[] { "SacSpores_sac_mushroom", "TurnipSeeds", "Mushroom spores" },
            new[] { "SacSpores_sac_mushroom_yellow", "TurnipSeeds", "Yellow mushroom spores" },
            new[] { "SacSpores_sac_mushroom_blue", "TurnipSeeds", "Blue mushroom spores" },
        };

        // Built objects of Forager's Garden (hives, wild crops, mushrooms and their saplings). Without a prefab the host
        // deletes them (ZNetScene.CreateObjectsSorted: "Destroyed invalid prefab ZDO"). An empty placeholder with a
        // ZNetView keeps each one, and its saved data (growth time, honey ...), invisible until the mod is installed.
        private static readonly string[] WorldObjects =
        {
            "sac_hive_ice", "sac_hive_jelly", "sac_hive_crystal",
            "sac_wild_raspberry", "sac_wild_blueberry", "sac_wild_cloudberry",
            "sac_wild_dandelion", "sac_wild_thistle", "sac_wild_fiddlehead", "sac_wild_smokepuff",
            "sapling_sac_bush_raspberry", "sapling_sac_bush_blueberry", "sapling_sac_bush_cloudberry",
            "sapling_sac_wild_dandelion", "sapling_sac_wild_thistle", "sapling_sac_wild_fiddlehead", "sapling_sac_wild_smokepuff",
            "Pickable_sac_mushroom", "Pickable_sac_mushroom_yellow", "Pickable_sac_mushroom_blue",
            "Pickable_sac_mushroom_spores", "Pickable_sac_mushroom_yellow_spores", "Pickable_sac_mushroom_blue_spores",
            "sapling_sac_mushroom", "sapling_sac_mushroom_yellow", "sapling_sac_mushroom_blue",
            "sapling_seed_sac_mushroom", "sapling_seed_sac_mushroom_yellow", "sapling_seed_sac_mushroom_blue",
        };

        private static readonly Dictionary<int, GameObject> ByHash = new Dictionary<int, GameObject>();
        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
        private static HashSet<int> _worldHashes;
        private static GameObject _placeholder;
        private static GameObject _hide;

        /// <summary>
        /// For ZNetScene.GetPrefab when vanilla knows nothing: a stand-in item (one lying on the ground) or the placeholder
        /// for a built object. Null with Forager's Garden (it answers with the real prefabs).
        /// </summary>
        internal static GameObject PrefabForHash(int hash)
        {
            GameObject go;
            if (ByHash.TryGetValue(hash, out go))
                return go;
            if (_worldHashes == null)
            {
                _worldHashes = new HashSet<int>();
                for (int i = 0; i < WorldObjects.Length; i++)
                    _worldHashes.Add(WorldObjects[i].GetStableHashCode());
            }
            if (!_worldHashes.Contains(hash) || ForagersGardenInstalled)
                return null;
            return Placeholder();
        }

        private static GameObject Placeholder()
        {
            if (_placeholder != null)
                return _placeholder;
            _placeholder = new GameObject("sac_foragers_garden_placeholder");
            _placeholder.transform.SetParent(Hide(), false); // inactive parent: the template itself never wakes up
            _placeholder.AddComponent<ZNetView>();
            return _placeholder;
        }

        private static Transform Hide()
        {
            if (_hide == null)
            {
                _hide = new GameObject("sac_moved_items_hide");
                _hide.SetActive(false);
                Object.DontDestroyOnLoad(_hide);
            }
            return _hide.transform;
        }

        private static bool ForagersGardenInstalled
        {
            get { return Chainloader.PluginInfos.ContainsKey(ForagersGardenGuid); }
        }

        internal static void AddToDb(ObjectDB db)
        {
            if (db == null || db.m_items == null || ForagersGardenInstalled)
                return;
            bool added = false;
            for (int i = 0; i < Items.Length; i++)
            {
                GameObject standIn = StandIn(db, Items[i]);
                if (standIn != null && !db.m_items.Contains(standIn))
                {
                    db.m_items.Add(standIn);
                    added = true;
                }
            }
            if (added && UpdateRegisters != null)
                UpdateRegisters.Invoke(db, null);
        }

        private static GameObject StandIn(ObjectDB db, string[] item)
        {
            int hash = item[0].GetStableHashCode();
            GameObject made;
            if (ByHash.TryGetValue(hash, out made) && made != null)
                return made;
            GameObject template = db.GetItemPrefab(item[1]);
            if (template == null || template.GetComponent<ItemDrop>() == null)
                return null; // ObjectDB not filled yet: the next call tries again
            bool wasActive = template.activeSelf;
            template.SetActive(false);
            GameObject clone = Object.Instantiate(template, Hide());
            template.SetActive(wasActive);
            clone.name = item[0];
            clone.SetActive(true); // the parent is inactive: no Awake / OnEnable runs
            ItemDrop.ItemData.SharedData shared = clone.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = item[2] + " (needs Forager's Garden)";
            shared.m_description = "Since StoreAndCraft 1.6.0 this item belongs to the mod Forager's Garden. "
                + "Install it to use the item again; until then it is kept safe here.";
            ByHash[hash] = clone;
            return clone;
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDbAwakeMovedItemsPatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            MovedItemsGuard.AddToDb(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDbCopyMovedItemsPatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            MovedItemsGuard.AddToDb(__instance);
        }
    }
}
