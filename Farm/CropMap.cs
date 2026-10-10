using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Field crops from ZNetScene: seed item ↔ sapling piece (Plant) ↔ ripe Pickable. Built once
    /// when the scene exists (vanilla + modded crops). Trees are left out (their grown prefab has
    /// no Pickable). Used by the scarecrow to plant and harvest its grid.
    /// </summary>
    internal static class CropMap
    {
        internal sealed class Crop
        {
            /// <summary>What the scarecrow stores for this crop (ZDO) and the list selects. The seed item itself,
            /// except for a second crop that needs the same seed (kale seeds → kale seeds): seed|sapling name.</summary>
            public string Key;
            public string Seed;          // shared name of the seed item ($item_carrotseeds, $item_barley…)
            public int Cost = 1;         // how many of the seed item one sapling costs (berry bushes: 5)
            public GameObject Sapling;   // piece with Plant
            public float GrowRadius;     // Plant.m_growRadius (free space it needs)
            public bool NeedCultivated;  // Plant.m_needCultivatedGround
            /// <summary>Plants the vegetable itself to grow seeds (sapling_seedcarrot...): keeps the seed supply going.</summary>
            public bool MakesSeeds;
            public readonly HashSet<string> Ripe = new HashSet<string>(); // Pickable prefab names
        }

        private static List<Crop> _all;
        private static readonly Dictionary<string, Crop> ByKey = new Dictionary<string, Crop>();
        private static readonly Dictionary<string, Crop> ByRipe = new Dictionary<string, Crop>();
        private static readonly Dictionary<string, Crop> BySapling = new Dictionary<string, Crop>();

        /// <summary>
        /// The crop list is built once, from the Cultivator's pieces. Saplings added to that list afterwards (mushrooms,
        /// wild plants: they register when the player spawns) need a rebuild, else they never show up.
        /// </summary>
        internal static void Invalidate()
        {
            _all = null;
        }

        public static List<Crop> All()
        {
            if (_all != null && _all.Count > 0)
                return _all;
            _all = new List<Crop>();
            ByKey.Clear();
            ByRipe.Clear();
            BySapling.Clear();
            if (ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null)
                return _all;

            // Only saplings the Cultivator really offers: a Carrot cannot be planted, its seeds can.
            if (ObjectDB.instance == null)
                return _all;
            HashSet<GameObject> placeable = CultivatorPieces();

            // Two passes: the normal saplings first, then the ones that grow seeds (they take the seed item as key
            // when a normal sapling already has it).
            for (int pass = 0; pass < 2; pass++)
            foreach (GameObject go in ZNetScene.instance.m_prefabs)
            {
                if (go == null)
                    continue;
                if (placeable != null && !placeable.Contains(go))
                    continue;
                // Saplings only: sapling_carrot (seeds → carrots) and sapling_seedcarrot (carrot → seeds).
                if (!go.name.StartsWith("sapling", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (go.name.StartsWith("sapling_seed", System.StringComparison.OrdinalIgnoreCase) != (pass == 1))
                    continue;
                Plant plant = go.GetComponent<Plant>();
                Piece piece = go.GetComponent<Piece>();
                if (plant == null || piece == null || plant.m_grownPrefabs == null || plant.m_grownPrefabs.Length == 0)
                    continue;
                if (piece.m_resources == null || piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null)
                    continue;
                string seed = piece.m_resources[0].m_resItem.m_itemData?.m_shared?.m_name;
                if (string.IsNullOrEmpty(seed))
                    continue;
                // Kale: sapling_Kale and sapling_seedkale both take kale seeds (one grows kale, the other seeds).
                string key = ByKey.ContainsKey(seed) ? seed + "|" + go.name : seed;
                if (ByKey.ContainsKey(key))
                    continue;

                var crop = new Crop
                {
                    Key = key,
                    Seed = seed,
                    Cost = Mathf.Max(1, piece.m_resources[0].m_amount),
                    Sapling = go,
                    GrowRadius = plant.m_growRadius,
                    NeedCultivated = plant.m_needCultivatedGround,
                    MakesSeeds = go.name.StartsWith("sapling_seed", System.StringComparison.OrdinalIgnoreCase)
                };
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    // Own wild plants (WildCrops) may carry the Pickable below the root; everything else only on the root.
                    if (grown != null && (grown.GetComponent<Pickable>() != null
                        || (grown.name.StartsWith("sac_wild_") && grown.GetComponentInChildren<Pickable>(true) != null)))
                        crop.Ripe.Add(grown.name);
                }
                if (crop.Ripe.Count == 0)
                    continue; // tree / non-crop

                _all.Add(crop);
                ByKey[key] = crop;
                BySapling[go.name] = crop;
                foreach (string r in crop.Ripe)
                    ByRipe[r] = crop;
            }

            _all.Sort((a, b) =>
            {
                int byName = string.Compare(DisplayFilters.ItemLabel(a.Seed), DisplayFilters.ItemLabel(b.Seed),
                    System.StringComparison.CurrentCultureIgnoreCase);
                return byName != 0 ? byName : string.CompareOrdinal(a.Key, b.Key);
            });
            Plugin.Log.LogDebug("Scarecrow crops: " + _all.Count);
            return _all;
        }

        /// <summary>The Cultivator's build pieces, or null when it cannot be read (then nothing is filtered).</summary>
        private static HashSet<GameObject> CultivatorPieces()
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Cultivator") : null;
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            PieceTable table = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (table == null || table.m_pieces == null || table.m_pieces.Count == 0)
                return null;
            var set = new HashSet<GameObject>();
            foreach (GameObject p in table.m_pieces)
            {
                Piece piece = p != null ? p.GetComponent<Piece>() : null;
                if (p != null && piece != null && piece.m_enabled)
                    set.Add(p);
            }
            return set.Count > 0 ? set : null;
        }

        /// <summary>What the crop list shows: the item, and "(for seeds)" when the vegetable is planted to grow seeds.</summary>
        public static string DisplayName(string key)
        {
            Crop crop = ForSeed(key);
            string name = DisplayFilters.ItemLabel(crop != null ? crop.Seed : key);
            // WildCrops: berry bushes keep growing berries; named so they are not mistaken for the berries themselves.
            if (crop != null && crop.Sapling != null && crop.Sapling.name.StartsWith("sapling_sac_bush_"))
                return name + " " + Loc.T("(bush)", "(Strauch)");
            if (crop == null || !crop.MakesSeeds)
                return name;
            bool mushroom = crop.Sapling != null && crop.Sapling.name.Contains("mushroom");
            return name + " " + (mushroom ? Loc.T("(for spores)", "(für Sporen)") : Loc.T("(for seeds)", "(für Samen)"));
        }

        /// <summary>The crop for a stored key (see Crop.Key; for most crops that is the seed item).</summary>
        public static Crop ForSeed(string key)
        {
            All();
            Crop c;
            return !string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out c) ? c : null;
        }

        /// <summary>The seed item behind a stored key (icon, "known" check).</summary>
        public static string ItemOf(string key)
        {
            Crop c = ForSeed(key);
            return c != null ? c.Seed : key;
        }

        /// <summary>The crop a planted sapling belongs to (null for anything else, e.g. a tree sapling).</summary>
        public static Crop ForSapling(GameObject go)
        {
            if (go == null)
                return null;
            All();
            Crop c;
            ZNetView root = go.GetComponentInParent<ZNetView>();
            return BySapling.TryGetValue(Utils.GetPrefabName(root != null ? root.gameObject : go), out c) ? c : null;
        }

        public static Crop ForRipe(GameObject go)
        {
            if (go == null)
                return null;
            All();
            Crop c;
            // The Pickable can sit on a child of the plant: the crop is known by the prefab root.
            ZNetView root = go.GetComponentInParent<ZNetView>();
            return ByRipe.TryGetValue(Utils.GetPrefabName(root != null ? root.gameObject : go), out c) ? c : null;
        }

        public static List<string> SeedChoices()
        {
            var list = new List<string>();
            foreach (Crop c in All())
                list.Add(c.Key);
            return list;
        }
    }
}
