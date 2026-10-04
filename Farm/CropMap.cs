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
            public string Seed;          // shared name of the seed item ($item_carrotseeds, $item_barley…)
            public GameObject Sapling;   // piece with Plant
            public float GrowRadius;     // Plant.m_growRadius (free space it needs)
            public bool NeedCultivated;  // Plant.m_needCultivatedGround
            /// <summary>Plants the vegetable itself to grow seeds (sapling_seedcarrot...): keeps the seed supply going.</summary>
            public bool MakesSeeds;
            public readonly HashSet<string> Ripe = new HashSet<string>(); // Pickable prefab names
        }

        private static List<Crop> _all;
        private static readonly Dictionary<string, Crop> BySeed = new Dictionary<string, Crop>();
        private static readonly Dictionary<string, Crop> ByRipe = new Dictionary<string, Crop>();

        public static List<Crop> All()
        {
            if (_all != null && _all.Count > 0)
                return _all;
            _all = new List<Crop>();
            BySeed.Clear();
            ByRipe.Clear();
            if (ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null)
                return _all;

            // Only saplings the Cultivator really offers: a Carrot cannot be planted, its seeds can.
            if (ObjectDB.instance == null)
                return _all;
            HashSet<GameObject> placeable = CultivatorPieces();

            foreach (GameObject go in ZNetScene.instance.m_prefabs)
            {
                if (go == null)
                    continue;
                if (placeable != null && !placeable.Contains(go))
                    continue;
                // Saplings only: sapling_carrot (seeds → carrots) and sapling_seedcarrot (carrot → seeds).
                if (!go.name.StartsWith("sapling", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                Plant plant = go.GetComponent<Plant>();
                Piece piece = go.GetComponent<Piece>();
                if (plant == null || piece == null || plant.m_grownPrefabs == null || plant.m_grownPrefabs.Length == 0)
                    continue;
                if (piece.m_resources == null || piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null)
                    continue;
                string seed = piece.m_resources[0].m_resItem.m_itemData?.m_shared?.m_name;
                if (string.IsNullOrEmpty(seed) || BySeed.ContainsKey(seed))
                    continue;

                var crop = new Crop
                {
                    Seed = seed,
                    Sapling = go,
                    GrowRadius = plant.m_growRadius,
                    NeedCultivated = plant.m_needCultivatedGround,
                    MakesSeeds = go.name.StartsWith("sapling_seed", System.StringComparison.OrdinalIgnoreCase)
                };
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown != null && grown.GetComponent<Pickable>() != null)
                        crop.Ripe.Add(grown.name);
                }
                if (crop.Ripe.Count == 0)
                    continue; // tree / non-crop

                _all.Add(crop);
                BySeed[seed] = crop;
                foreach (string r in crop.Ripe)
                    ByRipe[r] = crop;
            }

            _all.Sort((a, b) => string.Compare(DisplayFilters.ItemLabel(a.Seed), DisplayFilters.ItemLabel(b.Seed),
                System.StringComparison.CurrentCultureIgnoreCase));
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
        public static string DisplayName(string seed)
        {
            string name = DisplayFilters.ItemLabel(seed);
            Crop crop = ForSeed(seed);
            if (crop == null || !crop.MakesSeeds)
                return name;
            bool mushroom = crop.Sapling != null && crop.Sapling.name.Contains("mushroom");
            return name + " " + (mushroom ? Loc.T("(for spores)", "(für Sporen)") : Loc.T("(for seeds)", "(für Samen)"));
        }

        public static Crop ForSeed(string seed)
        {
            All();
            Crop c;
            return !string.IsNullOrEmpty(seed) && BySeed.TryGetValue(seed, out c) ? c : null;
        }

        public static Crop ForRipe(GameObject go)
        {
            if (go == null)
                return null;
            All();
            Crop c;
            return ByRipe.TryGetValue(Utils.GetPrefabName(go), out c) ? c : null;
        }

        public static List<string> SeedChoices()
        {
            var list = new List<string>();
            foreach (Crop c in All())
                list.Add(c.Seed);
            return list;
        }
    }
}
