using UnityEngine;

namespace StoreAndCraft
{
    internal static class RequirementBridge
    {
        /// <summary>
        /// Count spendable mats in nearby chests for UI / HaveRequirements / plant.
        /// Matches withdraw: LeaveOneItem reserves one per chest for auto-store routing.
        /// Forge of Potential idols skip LeaveOne — they are unique and a single copy
        /// in a chest must still be usable (same as Epic Loot runestones).
        /// </summary>
        public static int CountNearby(Player player, string sharedName, int quality = -1)
        {
            return CountNearby(player, sharedName, StationLink.ActiveId, quality);
        }

        /// <summary>
        /// Count with an explicit station link (0 = untagged only, 1–9 = [lN] only, -1 = all).
        /// Prefer this for kiln/smelter fill so a leaked ActiveId=-1 cannot open every chest.
        /// </summary>
        public static int CountNearby(Player player, string sharedName, int linkId, int quality)
        {
            if (player == null || Plugin.Settings == null || string.IsNullOrEmpty(sharedName))
                return 0;

            return NearbyIndex.CountItem(
                StationFeed.ActivePullOrigin(player),
                0f,
                sharedName,
                leaveOne: LeaveOneInChests(player),
                quality: quality,
                linkId: linkId);
        }

        /// <summary>
        /// LeaveOne is for stackable craft mats so auto-store can keep routing.
        /// The Potential forge only spends idol items (often 1 per chest).
        /// </summary>
        public static bool LeaveOneInChests(Player player)
        {
            if (Plugin.Settings == null || !Plugin.Settings.LeaveOneItem.Value)
                return false;

            CraftingStation station = player != null ? player.GetCurrentCraftingStation() : null;
            if (station != null && station.m_upgrader)
                return false;

            return true;
        }

        /// <summary>
        /// Same rules as vanilla HaveRequirementItems (forge / workbench / black forge / …),
        /// but counts inventory + chests the way SetupRequirement does (all qualities).
        /// </summary>
        public static bool RecipeHasItems(Player player, Recipe recipe, int qualityLevel, int amount)
        {
            if (player == null || recipe?.m_resources == null)
                return false;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return false;

            // Index is refreshed in Plugin.Update; avoid per-requirement Tick spam.
            CraftingStation station = player.GetCurrentCraftingStation();
            bool onlyOne = recipe.m_requireOnlyOneIngredient;
            int mult = Mathf.Max(1, amount);

            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (req?.m_resItem?.m_itemData?.m_shared == null)
                    continue;

                if (station != null && station.m_upgrader != req.m_upgraderResource)
                    continue;
                if (station == null && req.m_upgraderResource)
                    continue;

                int need = req.GetAmount(qualityLevel) * mult;
                if (need <= 0)
                    continue;

                string shared = req.m_resItem.m_itemData.m_shared.m_name;
                int have = inv.CountItems(shared);
                if (onlyOne)
                {
                    if (have >= need)
                        return true;
                    continue;
                }

                if (have < need)
                    return false;
            }

            return !onlyOne;
        }

        public static bool SenderInRange(long sender, Vector3 target, float range)
        {
            if (ZNet.instance == null)
                return true;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null)
                return true;

            float slack = 6f;
            return Vector3.Distance(peer.m_refPos, target) <= range + slack;
        }

        /// <summary>
        /// True when the target lies in the area loaded around the sender (ZNetScene.InActiveArea around the
        /// peer's reference position). Local calls (no peer) count as inside.
        /// </summary>
        public static bool SenderLoadedArea(long sender, Vector3 target)
        {
            if (ZNet.instance == null)
                return true;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null)
                return true;

            return ZNetScene.InActiveArea(target, peer.m_refPos);
        }
    }
}
