using UnityEngine;

namespace StoreAndCraft
{
    internal static class StagingPull
    {
        /// <summary>
        /// True while <c>InventoryGui.DoCrafting</c> runs. onlyOne recipes pay the
        /// chosen ingredient via <c>Inventory.RemoveItem(name,…)</c> here — not via
        /// <c>Player.ConsumeResources</c> (that path is skipped when singleReqItem is set).
        /// </summary>
        private static int _doCraftingDepth;

        public static bool Active
        {
            get
            {
                return Plugin.Settings != null
                    && Plugin.Settings.ModEnabled.Value
                    && Plugin.Settings.CraftEnabled.Value;
            }
        }

        public static void BeginDoCrafting()
        {
            _doCraftingDepth++;
        }

        public static void EndDoCrafting()
        {
            if (_doCraftingDepth > 0)
                _doCraftingDepth--;
        }

        private static bool InDoCrafting => _doCraftingDepth > 0;

        /// <summary>
        /// Pay craft/build costs from nearby chests without moving stacks into the backpack.
        /// Staging into inventory filled free slots and made the craft result fail to add
        /// (classic "mats appear, craft fizzles" — arrows still worked onto an existing stack).
        ///
        /// onlyOne recipes are NOT paid here — see <see cref="PayOnlyOneRemoveFromChests"/>.
        /// </summary>
        public static void ConsumeRequirements(
            Player player,
            Piece.Requirement[] requirements,
            int qualityLevel,
            int itemQuality,
            int multiplier)
        {
            if (player == null || requirements == null || !Active)
                return;

            // onlyOne: DoCrafting removes via Inventory.RemoveItem(string), not ConsumeResources.
            Recipe craftRecipe = Refs.CraftRecipe(InventoryGui.instance);
            if (craftRecipe != null && craftRecipe.m_requireOnlyOneIngredient)
                return;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            NearbyIndex.Tick();
            float cfgCraft = Plugin.Settings.CraftReach();
            Vector3 origin = player.transform.position;
            CraftingStation station = player.GetCurrentCraftingStation();
            bool leaveOne = RequirementBridge.LeaveOneInChests(player);

            InventoryCountPatches.Skip++;
            try
            {
                foreach (Piece.Requirement req in requirements)
                {
                    if (req == null || req.m_resItem == null)
                        continue;

                    if (station != null && station.m_upgrader != req.m_upgraderResource)
                        continue;
                    if (station == null && req.m_upgraderResource)
                        continue;

                    int need = req.GetAmount(qualityLevel) * Mathf.Max(1, multiplier);
                    if (need <= 0)
                        continue;

                    string shared = req.m_resItem.m_itemData != null
                        ? req.m_resItem.m_itemData.m_shared.m_name
                        : null;
                    if (string.IsNullOrEmpty(shared))
                        continue;

                    int have = inv.CountItems(shared, itemQuality, true);
                    int deficit = need - have;
                    if (deficit <= 0)
                        continue;

                    ConsumeDeficitFromChests(
                        shared, deficit, itemQuality, leaveOne, origin, cfgCraft);
                }
            }
            finally
            {
                InventoryCountPatches.Skip--;
            }
        }

        /// <summary>
        /// During DoCrafting for onlyOne recipes, vanilla calls
        /// <c>inventory.RemoveItem(sharedName, amount, quality, …)</c> on the bag.
        /// If the chosen fish (etc.) lives only in a chest, that remove is a no-op and
        /// craft is free. Pay the bag deficit from chests first; then let vanilla
        /// remove whatever remains in the bag.
        /// </summary>
        public static void PayOnlyOneRemoveFromChests(
            Inventory inventory,
            string sharedName,
            int amount,
            int itemQuality)
        {
            if (!Active || !InDoCrafting || inventory == null || amount <= 0)
                return;
            if (string.IsNullOrEmpty(sharedName))
                return;

            Player player = Player.m_localPlayer;
            if (player == null || inventory != player.GetInventory())
                return;

            Recipe recipe = Refs.CraftRecipe(InventoryGui.instance);
            if (recipe == null || !recipe.m_requireOnlyOneIngredient)
                return;

            InventoryCountPatches.Skip++;
            try
            {
                int have = inventory.CountItems(sharedName, itemQuality, true);
                int deficit = amount - have;
                if (deficit <= 0)
                    return;

                NearbyIndex.Tick();
                float cfgCraft = Plugin.Settings.CraftReach();
                Vector3 origin = player.transform.position;
                // Exact ingredient for onlyOne — do not reserve LeaveOne.
                ConsumeDeficitFromChests(
                    sharedName, deficit, itemQuality, leaveOne: false, origin, cfgCraft);
            }
            finally
            {
                InventoryCountPatches.Skip--;
            }
        }

        /// <summary>
        /// Mirror <c>Player.GetFirstRequiredItem</c> quality walk, but scan chests.
        /// Returns a <b>clone</b> so <c>Recipe.GetAmount</c> / DoCrafting get quality +
        /// non-null singleReqItem without withdrawing into the bag.
        /// </summary>
        public static ItemDrop.ItemData FindFirstRequiredInChests(
            Player player,
            Recipe recipe,
            int qualityLevel,
            int craftMultiplier,
            out int amount,
            out int extraAmount)
        {
            amount = 0;
            extraAmount = 0;

            if (player == null || recipe?.m_resources == null || !Active)
                return null;

            NearbyIndex.Tick();
            float cfgCraft = Plugin.Settings.CraftReach();
            Vector3 origin = player.transform.position;
            CraftingStation station = player.GetCurrentCraftingStation();
            int mult = Mathf.Max(1, craftMultiplier);

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
                int maxQ = Mathf.Max(0, req.m_resItem.m_itemData.m_shared.m_maxQuality);

                // Same order as vanilla: lowest quality first.
                for (int q = 0; q <= maxQ; q++)
                {
                    if (CountChestAvail(shared, q, leaveOne: false, origin, cfgCraft) < need)
                        continue;

                    ItemDrop.ItemData sample = SampleChestItem(shared, q, origin, cfgCraft);
                    if (sample == null)
                        continue;

                    amount = need;
                    extraAmount = req.m_extraAmountOnlyOneIngredient;

                    ItemDrop.ItemData clone = sample.Clone();
                    clone.m_stack = need;
                    return clone;
                }
            }

            return null;
        }

        private static ItemDrop.ItemData SampleChestItem(
            string shared,
            int quality,
            Vector3 origin,
            float cfgCraft)
        {
            foreach (Container chest in NearbyIndex.Current)
            {
                if (chest == null || ChestNames.BlocksCraft(chest))
                    continue;
                if (ContainerFilter.Distance(origin, chest.transform.position) > cfgCraft)
                    continue;

                NearbyIndex.EnsureInventory(chest);
                Inventory chestInv = chest.GetInventory();
                if (chestInv == null)
                    continue;

                ItemDrop.ItemData item = chestInv.GetItem(shared, quality, false);
                if (item != null && item.m_stack > 0)
                    return item;
            }

            return null;
        }

        private static int CountChestAvail(
            string shared,
            int itemQuality,
            bool leaveOne,
            Vector3 origin,
            float cfgCraft)
        {
            int total = 0;
            foreach (Container chest in NearbyIndex.Current)
            {
                if (chest == null || ChestNames.BlocksCraft(chest))
                    continue;
                if (ContainerFilter.Distance(origin, chest.transform.position) > cfgCraft)
                    continue;

                NearbyIndex.EnsureInventory(chest);
                Inventory chestInv = chest.GetInventory();
                if (chestInv == null)
                    continue;

                int avail = chestInv.CountItems(shared, itemQuality, true);
                avail -= PendingChestDebit.Of(chest, shared);
                if (ItemIds.ShouldLeaveOne(leaveOne, shared) && avail > 0)
                    avail -= 1;
                if (avail > 0)
                    total += avail;
            }

            return total;
        }

        private static void ConsumeDeficitFromChests(
            string shared,
            int deficit,
            int itemQuality,
            bool leaveOne,
            Vector3 origin,
            float cfgCraft)
        {
            foreach (Container chest in NearbyIndex.Current)
            {
                if (deficit <= 0)
                    break;

                if (chest == null || ChestNames.BlocksCraft(chest))
                    continue;

                if (ContainerFilter.Distance(origin, chest.transform.position) > cfgCraft)
                    continue;

                NearbyIndex.EnsureInventory(chest);
                Inventory chestInv = chest.GetInventory();
                if (chestInv == null)
                    continue;

                int avail = chestInv.CountItems(shared, itemQuality, true);
                avail -= PendingChestDebit.Of(chest, shared);
                if (ItemIds.ShouldLeaveOne(leaveOne, shared) && avail > 0)
                    avail -= 1;
                if (avail <= 0)
                    continue;

                int ask = Mathf.Min(deficit, avail);
                int took = TransferService.Consume(chest, shared, ask, leaveOne, itemQuality);
                ActivityLog.Move(Loc.T("Craft/Build", "Craften/Bauen"), ActivityLog.Chest(chest), took, shared, ActivityLog.Used());
                deficit -= took;
            }
        }
    }
}
