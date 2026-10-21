using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;

namespace StoreAndCraft
{
    internal struct DisplayFilter
    {
        public int Id;
        public string En;
        public string De;
        public ItemDrop.ItemData.ItemType[] Types;
        public string[] Names;
        public bool ExcludeNamed;

        public string Label()
        {
            return Loc.T(En, De);
        }
    }

    internal static class DisplayFilters
    {
        public const string ZdoKey = "sac_filter";
        public static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();
        public const string ZdoKeyMulti = "sac_filters";
        public static readonly int ZdoKeyMultiHash = ZdoKeyMulti.GetStableHashCode();
        public const string ZdoKeyItems = "sac_filter_items";
        public static readonly int ZdoKeyItemsHash = ZdoKeyItems.GetStableHashCode();

        public const int FoodFilterId = 2;
        public const int IngredientsFilterId = 22;
        public const int OtherMaterialsFilterId = 23;
        public const int BossRareFilterId = 21;
        public const int GemsCoinsFilterId = 20;
        public const int UtilityFilterId = 10;
        public const int MiscFilterId = 5;
        public const int ToolsFilterId = 7;
        public const int WeaponsFilterId = 8;
        public const int ArmorFilterId = 9;
        public const int TrinketsFilterId = 24;
        /// <summary>Menu group only; "All" selects every Epic Loot craft-mat subtype.</summary>
        public const int EpicLootGroupId = 30;
        public const int ElDustFilterId = 31;
        public const int ElEssenceFilterId = 32;
        public const int ElReagentFilterId = 33;
        public const int ElShardFilterId = 34;
        public const int ElRunestoneFilterId = 35;
        public const int MaxCategories = 12;

        // Path of Valheiman (soft: only with that mod). Its items are all prefabs named "PT_...".
        public const int PovRunesId = 40;
        public const int PovEssencesId = 41;
        public const int PovAzuriteId = 42;
        public const int PovJewelsId = 43;
        public const int PovOilsBombsId = 44;

        public static readonly int[] EpicLootSubFilterIds =
        {
            ElDustFilterId, ElEssenceFilterId, ElReagentFilterId, ElShardFilterId, ElRunestoneFilterId
        };

        private static readonly string[] ElRarities =
        {
            "magic", "rare", "epic", "legendary", "mythic"
        };

        public static bool IsExpandable(int filterId)
        {
            if (filterId == EpicLootGroupId)
                return true;
            DisplayFilter filter;
            if (!TryGet(filterId, out filter))
                return false;
            return true;
        }

        /// <summary>Dust/Essence/Reagent/Shard/Runestone — under Epic Loot expand only, not top-level.</summary>
        public static bool IsEpicLootSubFilter(int filterId)
        {
            return filterId == ElDustFilterId
                || filterId == ElEssenceFilterId
                || filterId == ElReagentFilterId
                || filterId == ElShardFilterId
                || filterId == ElRunestoneFilterId;
        }

        // Wood
        private static readonly string[] WoodNames =
        {
            "wood", "finewood", "roundlog", "elderbark", "yggdrasilwood", "ashwood", "blackwood"
        };

        // Ore & scrap (not finished bars)
        private static readonly string[] OreNames =
        {
            "copperore", "tinore", "ironore", "ironscrap", "silverore",
            "flametalore", "flametalorenew", "blackmetalscrap", "copperscrap"
        };

        // Smelted / worked metals
        private static readonly string[] MetalNames =
        {
            "copper", "tin", "bronze", "iron", "silver", "blackmetal", "flametal", "flametalnew", "gold",
            "bronzenails", "ironnails", "chain"
        };

        private static readonly string[] StoneNames =
        {
            "stone", "flint", "obsidian", "crystal", "sharpeningstone", "thunderstone", "grausten"
        };

        private static readonly string[] FuelNames =
        {
            "coal", "resin", "tar", "frozenfuel"
        };

        private static readonly string[] HideNames =
        {
            "deerhide", "leatherscraps", "trollhide", "wolfpelt", "loxpelt",
            "serpentscale", "chitin", "scalehide", "askhide", "asksvinhide", "hare_pelt", "harepelt",
            "bjornhide", "bearhide", "bear_hide", "bearpelt", "bear_pelt"
        };

        private static readonly string[] PartsNames =
        {
            "bonefragments", "witheredbone", "entrails", "bloodbag", "feathers", "guck", "ooze",
            "needle", "greydwarfeye", "freezegland", "hardantler", "wolffang", "root",
            "softtissue", "mandible", "ectoplasm", "bilebag"
        };

        private static readonly string[] CropNames =
        {
            "barley", "barleyflour", "flax", "carrot", "onion", "turnip",
            "kale", "oat", "carrotseeds", "onionseeds", "turnipseeds", "kaleseeds", "oatseeds", "beechseeds", "birchseeds",
            "fircone", "pinecone", "acorn", "oakseeds", "dandelion", "thistle",
            "sap", "royaljelly"
        };

        // Raw meats / fish for roasting (not berries or meal prep).
        private static readonly string[] RawFoodNames =
        {
            "rawmeat", "boar_meat", "deer_meat", "deermeat", "wolf_meat", "wolfmeat",
            "loxmeat", "fish_raw", "fishraw", "serpentmeat", "necktail",
            "chicken_meat", "chickenmeat", "hare_meat", "haremeat", "asksvin_meat", "asksvinmeat"
        };

        // Cooking ingredients: berries, mushrooms, honey, dough / mead bases / uncooked pies.
        private static readonly string[] IngredientNames =
        {
            "blueberry", "raspberry", "cloudberry", "blueberryjam", "bukeperries",
            "honey", "mushroom", "mushroomblue", "mushroomyellow", "mushroomendon",
            "magecap", "jotunpuffs", "smokepuffs",
            "breaddough", "loxpie_uncooked", "loxpieuncooked",
            "meadbasefrostresist", "meadbasehealth_medium", "meadbasehealth",
            "meadbasepoisonresist", "meadbasestamina_medium", "meadbasestamina", "meadbasetasty",
            "meadbaseeitr_minor", "meadbaseeitr_medium", "barleywinebase"
        };

        private static readonly string[] GemNames =
        {
            "coins", "amber", "amberpearl", "ruby", "silvernecklace",
            "foresttoken", "ironbountytoken", "goldbountytoken"
        };

        private static readonly string[] BossNames =
        {
            "surtlingcore", "dragontear", "ancientseed", "ymirremains", "queenbee",
            "yagluthdrop", "vegvisirshard_bonemass", "dvergrkeyfragment", "dvergrextractorkey",
            "mechanicalspring", "blackcore", "seekerchitin", "carapace", "refinedeitr",
            "gemstone_red", "gemstone_blue", "gemstone_green"
        };

        // Drinking vessels are weapon-type items (no category for those): Misc takes them by name.
        private static readonly string[] DrinkVesselNames =
        {
            "tankard", "tankard_dvergr", "tankardanniversary", "tankardodin"
        };

        // Tools that are weapon-type items in Valheim: pickaxes, the scythe and the fishing rod belong under Tools.
        private static bool SoftToolName(string key)
        {
            return !string.IsNullOrEmpty(key)
                && (key.StartsWith("pickaxe", System.StringComparison.Ordinal) || key == "scythe" || key == "fishingrod");
        }

        private static readonly string[] ElUtilityNames =
        {
            "leatherbelt", "silverring", "goldrubyring", "andvaranaut"
        };

        /// <summary>
        /// Menu order (top → bottom): building → hunt → cooking → valuables → gear → mods.
        /// Ids stay stable for saved boards; only display order changes.
        /// </summary>
        public static readonly DisplayFilter[] Choices =
        {
            // --- Building & refining ---
            Named(11, "Wood", "Holz", WoodNames, ItemDrop.ItemData.ItemType.Material),
            Named(14, "Stone", "Stein", StoneNames, ItemDrop.ItemData.ItemType.Material),
            Named(12, "Ore", "Erz", OreNames, ItemDrop.ItemData.ItemType.Material),
            Named(13, "Metals", "Metalle", MetalNames, ItemDrop.ItemData.ItemType.Material),
            Named(15, "Fuel", "Brennstoff", FuelNames, ItemDrop.ItemData.ItemType.Material),

            // --- Hunt / creature materials ---
            Named(16, "Hides", "Häute", HideNames, ItemDrop.ItemData.ItemType.Material),
            Named(17, "Parts", "Teile", PartsNames, ItemDrop.ItemData.ItemType.Material),

            // --- Farming & cooking ---
            Named(18, "Crops & Seeds", "Pflanzen & Samen", CropNames,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Consumable),
            Named(19, "Raw Food", "Rohes Essen", RawFoodNames, ItemDrop.ItemData.ItemType.Material),
            Named(22, "Ingredients", "Zutaten", IngredientNames,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Consumable),
            Typed(2, "Food", "Essen", true,
                ItemDrop.ItemData.ItemType.Consumable),
            Typed(3, "Fish", "Fisch", false,
                ItemDrop.ItemData.ItemType.Fish),

            // --- Valuables & leftovers ---
            Named(20, "Gems & Coins", "Edelsteine & Münzen", GemNames, ItemDrop.ItemData.ItemType.Material),
            Named(21, "Boss / Rare", "Boss / Selten", BossNames, ItemDrop.ItemData.ItemType.Material),
            Other(23, "Other materials", "Andere Materialien"),

            // Torch-type items (Dvergr Lantern, Torch, Mist torch, Sparkler) count as tools: no other category has them.
            Typed(7, "Tools", "Werkzeuge", false,
                ItemDrop.ItemData.ItemType.Tool, ItemDrop.ItemData.ItemType.Torch),
            // Weapons: all weapon types incl. bows, bombs, staves (pickaxes, scythe, fishing rod are Tools by name, tankards are Misc).
            Typed(8, "Weapons", "Waffen", false,
                ItemDrop.ItemData.ItemType.OneHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
                ItemDrop.ItemData.ItemType.Bow,
                ItemDrop.ItemData.ItemType.Attach_Atgeir),
            // Armor: helmet, chest, legs, capes, gloves and shields.
            Typed(9, "Armor", "Rüstung", false,
                ItemDrop.ItemData.ItemType.Helmet,
                ItemDrop.ItemData.ItemType.Chest,
                ItemDrop.ItemData.ItemType.Legs,
                ItemDrop.ItemData.ItemType.Shoulder,
                ItemDrop.ItemData.ItemType.Hands,
                ItemDrop.ItemData.ItemType.Shield),
            Typed(6, "Ammo", "Munition", false,
                ItemDrop.ItemData.ItemType.Ammo,
                ItemDrop.ItemData.ItemType.AmmoNonEquipable),
            Typed(10, "Utility", "Nutzen", false,
                ItemDrop.ItemData.ItemType.Utility),
            Typed(24, "Trinkets", "Schmuckstücke", false,
                ItemDrop.ItemData.ItemType.Trinket),
            Typed(4, "Trophy", "Trophäe", false,
                ItemDrop.ItemData.ItemType.Trophy),
            Typed(5, "Misc", "Sonstiges", false,
                ItemDrop.ItemData.ItemType.Misc),

            // --- Mods (parent expands; subs are real board categories) ---
            Typed(30, "Epic Loot", "Epic Loot", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc),
            Typed(31, "Dust", "Dust", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc),
            Typed(32, "Essence", "Essence", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc),
            Typed(33, "Reagent", "Reagent", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc),
            Typed(34, "Shard", "Shard", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc),
            Typed(35, "Runestone", "Runestone", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc),

            // Path of Valheiman (matched by PovCategoryOf, shown only when that mod is installed)
            Typed(40, "PoV Runes", "PoV Runes", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc, ItemDrop.ItemData.ItemType.Consumable),
            Typed(41, "PoV Essences & Sigils", "PoV Essences & Sigils", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc, ItemDrop.ItemData.ItemType.Consumable),
            Typed(42, "PoV Azurite", "PoV Azurite", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc, ItemDrop.ItemData.ItemType.Consumable),
            Typed(43, "PoV Jewels", "PoV Jewels", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc, ItemDrop.ItemData.ItemType.Consumable),
            Typed(44, "PoV Oils & Bombs", "PoV Oils & Bombs", false,
                ItemDrop.ItemData.ItemType.Material, ItemDrop.ItemData.ItemType.Misc, ItemDrop.ItemData.ItemType.Consumable)
        };

        private static DisplayFilter Named(int id, string en, string de, string[] names, params ItemDrop.ItemData.ItemType[] types)
        {
            return new DisplayFilter
            {
                Id = id,
                En = en,
                De = de,
                Types = types,
                Names = names,
                ExcludeNamed = false
            };
        }

        private static DisplayFilter Typed(int id, string en, string de, bool excludeNamed, params ItemDrop.ItemData.ItemType[] types)
        {
            return new DisplayFilter
            {
                Id = id,
                En = en,
                De = de,
                Types = types,
                Names = null,
                ExcludeNamed = excludeNamed
            };
        }

        private static DisplayFilter Other(int id, string en, string de)
        {
            return new DisplayFilter
            {
                Id = id,
                En = en,
                De = de,
                Types = new[]
                {
                    ItemDrop.ItemData.ItemType.Material,
                    ItemDrop.ItemData.ItemType.Misc
                },
                Names = null,
                ExcludeNamed = false
            };
        }

        public static bool TryGet(int id, out DisplayFilter filter)
        {
            for (int i = 0; i < Choices.Length; i++)
            {
                if (Choices[i].Id == id)
                {
                    filter = Choices[i];
                    return true;
                }
            }
            filter = default(DisplayFilter);
            return false;
        }

        public static string Label(int id)
        {
            DisplayFilter found;
            if (TryGet(id, out found))
                return found.Label();
            return Loc.T("Select type", "Typ wählen");
        }

        public static string Label(IReadOnlyList<int> ids)
        {
            return Label(ids, null);
        }

        public static string Label(IReadOnlyList<int> ids, IReadOnlyList<string> itemTokens)
        {
            List<string> parts = CategoryLabels(ids, itemTokens);
            if (parts.Count == 0)
                return Loc.T("Select type", "Typ wählen");
            if (parts.Count <= 3)
                return string.Join(", ", parts.ToArray());
            return parts[0] + ", " + parts[1] + " +" + (parts.Count - 2);
        }

        /// <summary>
        /// Category names for the selection (filter ids + parent cats of item tokens).
        /// Prefer "Ingredients" over listing every berry when only sub-items are on.
        /// </summary>
        public static List<string> CategoryLabels(IReadOnlyList<int> ids, IReadOnlyList<string> itemTokens)
        {
            var parts = new List<string>();
            var seenIds = new HashSet<int>();

            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    DisplayFilter found;
                    if (id <= 0 || !TryGet(id, out found) || !seenIds.Add(id))
                        continue;
                    parts.Add(found.Label());
                }
            }

            if (itemTokens != null)
            {
                for (int i = 0; i < itemTokens.Count; i++)
                {
                    string token = itemTokens[i];
                    if (string.IsNullOrEmpty(token))
                        continue;
                    int parent = ParentFilterIdFromToken(token);
                    if (parent > 0)
                    {
                        if (!seenIds.Add(parent))
                            continue;
                        DisplayFilter found;
                        if (TryGet(parent, out found))
                            parts.Add(found.Label());
                        continue;
                    }
                    string shown = ItemLabel(token);
                    if (!string.IsNullOrEmpty(shown) && !parts.Contains(shown))
                        parts.Add(shown);
                }
            }

            return parts;
        }

        /// <summary>Which display category owns this item (Choices order; expandable Food/Ingredients first for tokens).</summary>
        public static int ParentFilterId(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return 0;
            // Prefer explicit named lists / expandable before broad Food.
            if (Matches(item, IngredientsFilterId))
                return IngredientsFilterId;
            if (Matches(item, FoodFilterId))
                return FoodFilterId;
            // Epic Loot subtypes before the Epic Loot "All" group.
            for (int i = 0; i < EpicLootSubFilterIds.Length; i++)
            {
                if (Matches(item, EpicLootSubFilterIds[i]))
                    return EpicLootSubFilterIds[i];
            }
            for (int i = 0; i < Choices.Length; i++)
            {
                int id = Choices[i].Id;
                if (id == FoodFilterId || id == IngredientsFilterId || IsEpicLootSubFilter(id))
                    continue;
                if (Matches(item, id))
                    return id;
            }
            return 0;
        }

        public static int ParentFilterIdFromToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return 0;
            GameObject prefab = ItemIds.PrefabFromToken(token);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData != null)
                return ParentFilterId(drop.m_itemData);
            return 0;
        }

        public static int CategorySortOrder(int filterId)
        {
            if (filterId <= 0)
                return 1000;
            for (int i = 0; i < Choices.Length; i++)
            {
                if (Choices[i].Id == filterId)
                    return i;
            }
            return 1000;
        }

        public static List<int> ReadIds(ZDO zdo)
        {
            var ids = new List<int>();
            if (zdo == null)
                return ids;

            string raw = zdo.GetString(ZdoKeyMultiHash, "");
            if (!string.IsNullOrEmpty(raw))
            {
                string[] parts = raw.Split('|');
                for (int i = 0; i < parts.Length; i++)
                {
                    int id;
                    if (!int.TryParse(parts[i], out id) || id <= 0)
                        continue;
                    DisplayFilter unused;
                    if (!TryGet(id, out unused))
                        continue;
                    if (!ids.Contains(id))
                        ids.Add(id);
                }
                ids.Sort();
                return ids;
            }

            // Legacy single-filter boards.
            int legacy = zdo.GetInt(ZdoKeyHash, 0);
            DisplayFilter found;
            if (legacy > 0 && TryGet(legacy, out found))
                ids.Add(legacy);
            return ids;
        }

        public static string EncodeIds(IEnumerable<int> ids)
        {
            if (ids == null)
                return "";
            var list = new List<int>();
            foreach (int id in ids)
            {
                DisplayFilter unused;
                if (id <= 0 || !TryGet(id, out unused) || list.Contains(id))
                    continue;
                list.Add(id);
            }
            list.Sort();
            if (list.Count == 0)
                return "";
            return string.Join("|", list.ConvertAll(i => i.ToString()).ToArray());
        }

        public static bool SameIds(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        public static bool Matches(ItemDrop.ItemData item, int filterId)
        {
            DisplayFilter found;
            if (item?.m_shared == null || filterId <= 0 || !TryGet(filterId, out found))
                return false;

            if (filterId == OtherMaterialsFilterId)
                return MatchesOtherMaterials(item);

            if (filterId == EpicLootGroupId || IsEpicLootSubFilter(filterId))
                return MatchesEpicLootCraftMat(item, filterId);

            string key = Key(item);

            // Path of Valheiman items belong to their own categories only (not Food / Misc / Other materials ...).
            if (key.StartsWith("pt_", System.StringComparison.Ordinal))
            {
                int pov = PovCategoryOf(key, item.m_shared.m_itemType);
                if (IsPovFilter(filterId))
                    return pov == filterId;
                if (pov != 0)
                    return false;
            }
            else if (IsPovFilter(filterId))
                return false;

            // Epic Loot items routed into existing vanilla-style categories.
            // Runestones live under Epic Loot → Runestone (not Boss / Rare).
            if (filterId == GemsCoinsFilterId && SoftElToken(key))
                return true;
            if (filterId == UtilityFilterId && SoftElUtility(key))
                return true;
            if (filterId == MiscFilterId && SoftElUnidentified(key))
                return true;
            if (filterId == MiscFilterId && NameInList(key, DrinkVesselNames))
                return true;
            if (filterId == ToolsFilterId && SoftToolName(key))
                return true;
            if (filterId == WeaponsFilterId && (SoftToolName(key) || NameInList(key, DrinkVesselNames) || key == "playerunarmed"))
                return false;

            if (!TypeAllowed(item.m_shared.m_itemType, found.Types))
                return false;

            if (found.Names != null && found.Names.Length > 0)
            {
                if (NameInList(key, found.Names) || ExtraOreWoodMatch(key, found.Id))
                    return true;
                if (found.Id == 16 && SoftHideMatch(key))
                    return true;
                if (found.Id == 17 && SoftPartsMatch(key))
                    return true;
                if (found.Id == GemsCoinsFilterId && SoftElToken(key))
                    return true;
                return false;
            }

            if (found.ExcludeNamed)
                return !IsClaimedByNamedFilter(key, item.m_shared.m_itemType);

            return true;
        }

        public static bool IsPovFilter(int filterId)
        {
            return filterId >= PovRunesId && filterId <= PovOilsBombsId;
        }

        private static int _povInstalled = -1;

        /// <summary>Path of Valheiman is loaded (its categories show in the filter menu only then).</summary>
        public static bool PovInstalled()
        {
            if (_povInstalled < 0)
            {
                bool found = false;
                foreach (string guid in Chainloader.PluginInfos.Keys)
                {
                    if (guid != null && guid.IndexOf("pathofvalheiman", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = true;
                        break;
                    }
                }
                _povInstalled = found ? 1 : 0;
            }
            return _povInstalled == 1;
        }

        /// <summary>The PoV category of a "pt_..." prefab (runes before essences: "Great Rune of First Essence"), 0 = none.</summary>
        private static int PovCategoryOf(string key, ItemDrop.ItemData.ItemType type)
        {
            if (type != ItemDrop.ItemData.ItemType.Material && type != ItemDrop.ItemData.ItemType.Misc
                && type != ItemDrop.ItemData.ItemType.Consumable)
                return 0;
            string k = key.Substring(3);
            if (k.StartsWith("jewel", System.StringComparison.Ordinal))
                return PovJewelsId;
            if (k.Contains("azurite"))
                return PovAzuriteId;
            if (k.StartsWith("oil", System.StringComparison.Ordinal) || k.StartsWith("bomb", System.StringComparison.Ordinal))
                return PovOilsBombsId;
            if (k.Contains("rune"))
                return PovRunesId;
            if (k.Contains("essence") || k.StartsWith("sigil", System.StringComparison.Ordinal))
                return PovEssencesId;
            return 0;
        }

        /// <summary>Materials/misc not claimed by any other display category (mod reagents etc.).</summary>
        private static bool MatchesOtherMaterials(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return false;
            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            if (type != ItemDrop.ItemData.ItemType.Material && type != ItemDrop.ItemData.ItemType.Misc)
                return false;

            string key = Key(item);
            // Prefer hide/parts / Epic Loot routes over dumping into Other.
            if (SoftHideMatch(key) || SoftPartsMatch(key) || SoftElRoutedElsewhere(key))
                return false;

            for (int i = 0; i < Choices.Length; i++)
            {
                int id = Choices[i].Id;
                if (id == OtherMaterialsFilterId)
                    continue;
                if (id == MiscFilterId || id == FoodFilterId)
                    continue;
                if (id == EpicLootGroupId)
                    continue;
                if (Matches(item, id))
                    return false;
            }

            return true;
        }

        private static bool MatchesEpicLootCraftMat(ItemDrop.ItemData item, int filterId)
        {
            string key = Key(item);
            if (filterId == ElDustFilterId)
                return SoftElTyped(key, "dust");
            if (filterId == ElEssenceFilterId)
                return SoftElTyped(key, "essence");
            if (filterId == ElReagentFilterId)
                return SoftElTyped(key, "reagent");
            if (filterId == ElShardFilterId)
                return SoftElTyped(key, "shard");
            if (filterId == ElRunestoneFilterId)
                return SoftElTyped(key, "runestone") || SoftElTyped(key, "etchedrunestone");
            if (filterId == EpicLootGroupId)
            {
                return SoftElTyped(key, "dust")
                    || SoftElTyped(key, "essence")
                    || SoftElTyped(key, "reagent")
                    || SoftElTyped(key, "shard")
                    || SoftElTyped(key, "runestone")
                    || SoftElTyped(key, "etchedrunestone");
            }
            return false;
        }

        public static bool MatchesAny(ItemDrop.ItemData item, IReadOnlyList<int> filterIds)
        {
            if (item?.m_shared == null || filterIds == null || filterIds.Count == 0)
                return false;
            for (int i = 0; i < filterIds.Count; i++)
            {
                if (Matches(item, filterIds[i]))
                    return true;
            }
            return false;
        }

        public static bool MatchesSelection(
            ItemDrop.ItemData item,
            IReadOnlyList<int> filterIds,
            IReadOnlyList<string> itemTokens)
        {
            if (MatchesAny(item, filterIds))
                return true;
            if (item?.m_shared == null || itemTokens == null || itemTokens.Count == 0)
                return false;
            for (int i = 0; i < itemTokens.Count; i++)
            {
                if (ItemIds.Matches(item, itemTokens[i]))
                    return true;
            }
            return false;
        }

        public static List<string> ReadItemTokens(ZDO zdo)
        {
            var list = new List<string>();
            if (zdo == null)
                return list;
            string raw = zdo.GetString(ZdoKeyItemsHash, "");
            if (string.IsNullOrEmpty(raw))
                return list;
            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p) || list.Contains(p))
                    continue;
                list.Add(p);
            }
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        public static string EncodeItemTokens(IEnumerable<string> tokens)
        {
            if (tokens == null)
                return "";
            var list = new List<string>();
            foreach (string t in tokens)
            {
                if (string.IsNullOrEmpty(t) || list.Contains(t))
                    continue;
                list.Add(t);
            }
            list.Sort(System.StringComparer.Ordinal);
            if (list.Count == 0)
                return "";
            return string.Join("|", list.ToArray());
        }

        public static bool SameItemTokens(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], System.StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        /// <summary>Shared-name tokens for category item rows (cached).</summary>
        public static List<string> SubItems(int filterId)
        {
            if (filterId <= 0)
                return new List<string>();
            if (filterId == IngredientsFilterId)
                return IngredientSubItems();
            if (filterId == FoodFilterId)
                return FoodSubItems();
            DisplayFilter filter;
            if (!TryGet(filterId, out filter))
                return new List<string>();
            if (filter.Names != null && filter.Names.Length > 0)
                return NamedSubItems(filterId, filter.Names);
            // Typed categories (Fish, Ammo, Trophy, Epic Loot mats, …): scan ObjectDB.
            return TypedSubItems(filterId);
        }

        private static List<string> _ingredientSubs;
        private static List<string> _foodSubs;
        private static readonly Dictionary<int, List<string>> _namedSubs = new Dictionary<int, List<string>>();
        private static readonly Dictionary<int, List<string>> _typedSubs = new Dictionary<int, List<string>>();

        public static void InvalidateSubItemCache()
        {
            _ingredientSubs = null;
            _foodSubs = null;
            _namedSubs.Clear();
            _typedSubs.Clear();
        }

        /// <summary>First matching item shared-name for icon previews (Epic Loot subs, etc.).</summary>
        public static string RepresentativeShared(int filterId)
        {
            List<string> items = SubItems(filterId);
            if (items != null && items.Count > 0)
                return items[0];
            return null;
        }

        private static List<string> NamedSubItems(int filterId, string[] names)
        {
            List<string> cached;
            if (_namedSubs.TryGetValue(filterId, out cached) && cached != null)
                return cached;
            var list = new List<string>();
            if (names == null)
            {
                _namedSubs[filterId] = list;
                return list;
            }
            for (int i = 0; i < names.Length; i++)
            {
                string shared = ResolveSharedName(names[i]);
                if (string.IsNullOrEmpty(shared) || list.Contains(shared))
                    continue;
                list.Add(shared);
            }
            // Items that belong to the category by its soft rules (name ends on "hide", mod items, ...) but are not
            // in the fixed name list: they are listed too (and found by the search), like the typed categories do.
            bool scanned = false;
            if (ObjectDB.instance?.m_items != null && ObjectDB.instance.m_items.Count > 0)
            {
                scanned = true;
                foreach (UnityEngine.GameObject go in ObjectDB.instance.m_items)
                {
                    if (go == null)
                        continue;
                    ItemDrop drop = go.GetComponent<ItemDrop>();
                    if (drop?.m_itemData?.m_shared == null || !Matches(drop.m_itemData, filterId))
                        continue;
                    string shared = drop.m_itemData.m_shared.m_name;
                    if (string.IsNullOrEmpty(shared) || list.Contains(shared) || IsBadLabel(ItemLabel(shared)))
                        continue;
                    list.Add(shared);
                }
            }
            list.Sort(CompareLocalized);
            if (scanned)
                _namedSubs[filterId] = list; // not cached before ObjectDB is ready: the next call scans again
            return list;
        }

        private static List<string> TypedSubItems(int filterId)
        {
            List<string> cached;
            if (_typedSubs.TryGetValue(filterId, out cached) && cached != null)
                return cached;

            var list = new List<string>();
            if (ObjectDB.instance?.m_items == null)
                return list;

            // Epic Loot group: every craft-mat subtype item.
            int matchId = filterId == EpicLootGroupId ? EpicLootGroupId : filterId;

            foreach (UnityEngine.GameObject go in ObjectDB.instance.m_items)
            {
                if (go == null)
                    continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop?.m_itemData?.m_shared == null)
                    continue;
                if (!Matches(drop.m_itemData, matchId))
                    continue;
                // Avoid dumping Food-claimed consumables into Misc/Other scans twice.
                if (filterId == MiscFilterId || filterId == OtherMaterialsFilterId)
                {
                    if (Matches(drop.m_itemData, FoodFilterId) || Matches(drop.m_itemData, IngredientsFilterId))
                        continue;
                }
                string shared = drop.m_itemData.m_shared.m_name;
                if (string.IsNullOrEmpty(shared) || list.Contains(shared))
                    continue;
                string label = ItemLabel(shared);
                if (IsBadLabel(label))
                    continue;
                list.Add(shared);
            }

            list.Sort(CompareLocalized);
            if (list.Count > 0)
                _typedSubs[filterId] = list;
            return list;
        }

        private static List<string> IngredientSubItems()
        {
            if (_ingredientSubs != null)
                return _ingredientSubs;
            var list = new List<string>();
            for (int i = 0; i < IngredientNames.Length; i++)
            {
                string shared = ResolveSharedName(IngredientNames[i]);
                if (string.IsNullOrEmpty(shared) || list.Contains(shared))
                    continue;
                list.Add(shared);
            }
            list.Sort(CompareLocalized);
            _ingredientSubs = list;
            return list;
        }

        private static List<string> FoodSubItems()
        {
            if (_foodSubs != null)
                return _foodSubs;
            var list = new List<string>();
            if (ObjectDB.instance?.m_items == null)
                return list;
            foreach (UnityEngine.GameObject go in ObjectDB.instance.m_items)
            {
                if (go == null)
                    continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop?.m_itemData?.m_shared == null)
                    continue;
                // Only cooked-style consumables that localize cleanly (skip obscure mod junk labels).
                if (drop.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
                    continue;
                if (!Matches(drop.m_itemData, FoodFilterId))
                    continue;
                // Skip items already listed under Ingredients / Raw Food expand parents.
                if (Matches(drop.m_itemData, IngredientsFilterId) || Matches(drop.m_itemData, 19))
                    continue;
                string shared = drop.m_itemData.m_shared.m_name;
                if (string.IsNullOrEmpty(shared) || list.Contains(shared))
                    continue;
                string label = ItemLabel(shared);
                if (IsBadLabel(label))
                    continue;
                list.Add(shared);
            }
            list.Sort(CompareLocalized);
            // Only cache once ObjectDB actually returned entries.
            if (list.Count > 0)
                _foodSubs = list;
            return list;
        }

        private static string ResolveSharedName(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            UnityEngine.GameObject prefab = ItemIds.PrefabFromToken(key);
            if (prefab == null && key.IndexOf('_') >= 0)
                prefab = ItemIds.PrefabFromToken(key.Replace("_", ""));
            if (prefab == null && !key.StartsWith("$item_"))
                prefab = ItemIds.PrefabFromToken("$item_" + key);

            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData?.m_shared == null || string.IsNullOrEmpty(drop.m_itemData.m_shared.m_name))
                return null;
            return drop.m_itemData.m_shared.m_name;
        }

        private static int CompareLocalized(string a, string b)
        {
            return string.Compare(ItemLabel(a), ItemLabel(b), System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Filter menus list only items the local player has discovered (vanilla known materials:
        /// picked up / crafted), so new players do not see late-game items. No player or no-cost
        /// cheat (admin creative) → everything.
        /// </summary>
        public static bool IsKnownToPlayer(string shared)
        {
            Player player = Player.m_localPlayer;
            if (player == null || string.IsNullOrEmpty(shared) || player.NoCostCheat())
                return true;
            return player.IsMaterialKnown(shared);
        }

        public static string ItemLabel(string shared)
        {
            if (string.IsNullOrEmpty(shared))
                return "?";

            string shown = Localization.instance != null
                ? Localization.instance.Localize(shared)
                : shared;

            // Missing Valheim keys show as "[item_foo]" — resolve via ObjectDB and retry.
            if (IsBadLabel(shown))
            {
                UnityEngine.GameObject prefab = ItemIds.PrefabFromToken(shared);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string token = drop?.m_itemData?.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
                if (!string.IsNullOrEmpty(token) && Localization.instance != null)
                    shown = Localization.instance.Localize(token);
            }

            if (IsBadLabel(shown))
            {
                // Last resort: strip $item_ / brackets for readability.
                shown = shared;
                if (shown.StartsWith("$item_"))
                    shown = shown.Substring(6);
                if (shown.StartsWith("[") && shown.EndsWith("]"))
                    shown = shown.Substring(1, shown.Length - 2);
                shown = shown.Replace('_', ' ');
            }

            return shown;
        }

        private static bool IsBadLabel(string shown)
        {
            if (string.IsNullOrEmpty(shown))
                return true;
            if (shown.StartsWith("$item_"))
                return true;
            if (shown.StartsWith("[") && shown.EndsWith("]"))
                return true;
            if (shown.StartsWith("[item_", System.StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        public static bool TokenBelongsToCategory(string shared, int filterId)
        {
            if (string.IsNullOrEmpty(shared) || filterId <= 0)
                return false;
            if (filterId == EpicLootGroupId)
                return false;
            // Prefer Matches over SubItems — building full Food/ingredient lists is expensive.
            GameObject prefab = ItemIds.PrefabFromToken(shared);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData == null)
                return false;
            return Matches(drop.m_itemData, filterId);
        }

        private static bool TypeAllowed(ItemDrop.ItemData.ItemType type, ItemDrop.ItemData.ItemType[] types)
        {
            if (types == null || types.Length == 0)
                return true;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == type)
                    return true;
            }
            return false;
        }

        private static bool IsClaimedByNamedFilter(string key, ItemDrop.ItemData.ItemType type)
        {
            if (NameInList(key, CropNames)
                || NameInList(key, RawFoodNames)
                || NameInList(key, IngredientNames))
                return true;
            if (type != ItemDrop.ItemData.ItemType.Material)
                return false;
            return NameInList(key, WoodNames)
                || NameInList(key, OreNames)
                || NameInList(key, MetalNames)
                || NameInList(key, StoneNames)
                || NameInList(key, FuelNames)
                || NameInList(key, HideNames)
                || SoftHideMatch(key)
                || NameInList(key, PartsNames)
                || SoftPartsMatch(key)
                || NameInList(key, GemNames)
                || NameInList(key, BossNames)
                || SoftElRoutedElsewhere(key)
                || ExtraOreWoodMatch(key, 11)
                || ExtraOreWoodMatch(key, 12);
        }

        // Catch future/mod items: *wood, *ore (not leather scraps etc.)
        private static bool ExtraOreWoodMatch(string key, int filterId)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (filterId == 11)
            {
                return key.EndsWith("wood") && !NameInList(key, OreNames) && !NameInList(key, MetalNames);
            }
            if (filterId == 12)
            {
                // "surtlingcore" / "*core" ends with "ore" — those belong in Boss/Rare, not Ore.
                if (NameInList(key, BossNames) || key.EndsWith("core"))
                    return false;
                if (key.EndsWith("ore"))
                    return true;
                if (key.EndsWith("scrap") && !key.Contains("leather"))
                    return NameInList(key, OreNames) || key.Contains("metal") || key.Contains("iron") || key.Contains("copper") || key.Contains("black");
            }
            return false;
        }

        private static bool SoftHideMatch(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (key.Contains("leather") || key.EndsWith("hide") || key.EndsWith("pelt")
                || key.Contains("chitin") || key.EndsWith("scale"))
                return true;
            return false;
        }

        private static bool SoftPartsMatch(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key.Contains("ectoplasm") || key.Contains("bile") || key.Contains("entrails")
                || key.Contains("bloodbag") || key.Contains("bonefragment");
        }

        /// <summary>Epic Loot Dust/Essence/Reagent/Shard prefabs: DustMagic, EssenceRare, …</summary>
        public static bool SoftElTyped(string key, string typePrefix)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(typePrefix))
                return false;
            for (int i = 0; i < ElRarities.Length; i++)
            {
                if (key == typePrefix + ElRarities[i])
                    return true;
            }
            return false;
        }

        public static bool SoftElRunestone(string key)
        {
            return SoftElTyped(key, "runestone") || SoftElTyped(key, "etchedrunestone");
        }

        public static bool SoftElToken(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key == "foresttoken" || key == "ironbountytoken" || key == "goldbountytoken"
                || key.EndsWith("bountytoken");
        }

        public static bool SoftElUtility(string key)
        {
            return NameInList(key, ElUtilityNames);
        }

        public static bool SoftElUnidentified(string key)
        {
            return !string.IsNullOrEmpty(key) && key.Contains("unidentified");
        }

        /// <summary>Any Epic Loot item that belongs in a dedicated route (not Other).</summary>
        public static bool SoftElRoutedElsewhere(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return SoftElTyped(key, "dust")
                || SoftElTyped(key, "essence")
                || SoftElTyped(key, "reagent")
                || SoftElTyped(key, "shard")
                || SoftElRunestone(key)
                || SoftElToken(key)
                || SoftElUtility(key)
                || SoftElUnidentified(key);
        }

        /// <summary>Legacy broad match — prefer SoftElRoutedElsewhere / SoftElTyped.</summary>
        public static bool SoftEpicLootMaterial(string key)
        {
            return SoftElRoutedElsewhere(key);
        }

        private static bool NameInList(string key, string[] names)
        {
            if (string.IsNullOrEmpty(key) || names == null)
                return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (key == names[i])
                    return true;
            }
            return false;
        }

        private static string Key(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return "";

            if (item.m_dropPrefab != null)
            {
                string prefab = Normalize(item.m_dropPrefab.name);
                if (!string.IsNullOrEmpty(prefab))
                    return prefab;
            }

            return Normalize(item.m_shared.m_name);
        }

        // Normalize is a pure text function, but a Storage Display repaint calls it several times per item
        // and filter (thousands of items): remember the result per input text (a few hundred item names).
        private static readonly Dictionary<string, string> NormalizeCache = new Dictionary<string, string>();

        private static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "";
            string cached;
            if (NormalizeCache.TryGetValue(raw, out cached))
                return cached;

            string s = raw.Trim().ToLowerInvariant();
            if (s.StartsWith("$item_"))
                s = s.Substring(6);
            s = s.Replace(" ", "").Replace("-", "");
            if (NormalizeCache.Count > 4096)
                NormalizeCache.Clear();
            NormalizeCache[raw] = s;
            return s;
        }
    }
}
