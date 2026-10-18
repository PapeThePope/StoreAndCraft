using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class StationHover
    {
        public static void Begin()
        {
            if (!StagingPull.Active)
                return;
            // Do not Tick() here — SetupRequirement / HaveRequirements call Begin many
            // times per frame; Plugin.Update already refreshes the index.
            InventoryCountPatches.IncludeChests++;
        }

        public static void End()
        {
            if (InventoryCountPatches.IncludeChests > 0)
                InventoryCountPatches.IncludeChests--;
        }

        /// <summary>
        /// Safety: never leave IncludeChests stuck across frames (FPS death with inventory open).
        /// </summary>
        public static void ResetFrame()
        {
            InventoryCountPatches.IncludeChests = 0;
        }

        public static Switch HoveredSwitch()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;
            GameObject hover = player.GetHoverObject();
            if (hover == null)
                return null;
            Switch sw = hover.GetComponent<Switch>();
            return sw != null ? sw : hover.GetComponentInParent<Switch>();
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnAddFuel")]
    internal static class SmelterAddFuelPatch
    {
        private static int _fillDepth;

        private static void Prefix(Smelter __instance, Humanoid user, ItemDrop.ItemData item)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            // Hotbar key with another item (hammer, tool...): vanilla says "wrong item", nothing comes from chests.
            if (StationFeed.IsOtherItem(item, StationFeed.SharedFrom(__instance.m_fuelItem)))
                return;
            StationLink.PushStation(__instance);
            try
            {
                StationFeed.EnsureInInventory(
                    player,
                    StationFeed.SharedFrom(__instance.m_fuelItem),
                    1,
                    StationLink.Get(__instance));
            }
            finally
            {
                StationLink.Pop();
            }
        }

        private static void Postfix(Smelter __instance, Humanoid user)
        {
            if (_fillDepth > 0 || !FillToMaxInput.Held())
                return;
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            _fillDepth++;
            try
            {
                StationAutoFill.ManualFillSmelterToMax(__instance, player, fuel: true, ore: false);
            }
            finally
            {
                _fillDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnAddOre")]
    internal static class SmelterAddOrePatch
    {
        private static int _fillDepth;

        private static void Prefix(Smelter __instance, Humanoid user, ref ItemDrop.ItemData item)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            // Hotbar key with an item this station never takes (hammer, tool...): stays vanilla ("won't work", the
            // hammer is equipped). Before, the item was dropped and the station took ore from the bag like [E].
            if (StationFeed.IsOtherItem(item, StationPullFilter.OreChoices(__instance)))
                return;
            StationLink.PushStation(__instance);
            try
            {
                StationFeed.EnsureForUse(player, ref item, StationPullFilter.AllowedOreNames(__instance));
            }
            finally
            {
                StationLink.Pop();
            }
        }

        private static void Postfix(Smelter __instance, Humanoid user)
        {
            if (_fillDepth > 0 || !FillToMaxInput.Held())
                return;
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            _fillDepth++;
            try
            {
                StationAutoFill.ManualFillSmelterToMax(__instance, player, fuel: false, ore: true);
            }
            finally
            {
                _fillDepth--;
            }
        }

        internal static List<string> OreNames(Smelter smelter)
        {
            var names = new List<string>();
            if (smelter?.m_conversion == null)
                return names;
            foreach (Smelter.ItemConversion conv in smelter.m_conversion)
            {
                string shared = StationFeed.SharedFrom(conv != null ? conv.m_from : null);
                if (!string.IsNullOrEmpty(shared) && !names.Contains(shared))
                    names.Add(shared);
            }
            return names;
        }
    }

    /// <summary>Shift held = fill station to max on the next [E] add. Does not steal Shift from build no-snap (hammer mode).</summary>
    internal static class FillToMaxInput
    {
        public static bool Held()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return false;
            // Hammer placement uses Shift for no-snap — never fill-to-max there.
            if (player.InPlaceMode())
                return false;
            try
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.CanUseItems))]
    internal static class SmelterCanUseItemsPatch
    {
        private static void Prefix()
        {
            StationHover.Begin();
        }

        private static void Postfix(Smelter __instance, Player player, ref bool __result)
        {
            StationLink.PushStation(__instance);
            try
            {
                if (__result || !StationFeed.Ready() || player == null || __instance == null)
                    return;

                Switch hovered = StationHover.HoveredSwitch();
                if (hovered != null && hovered == __instance.m_emptyOreSwitch)
                    return;
                if (hovered != null && hovered == __instance.m_addOreSwitch)
                {
                    if (StationFeed.HasOrChestsAny(player, StationPullFilter.AllowedOreNames(__instance)))
                        __result = true;
                    return;
                }
                if (hovered != null && hovered == __instance.m_addWoodSwitch)
                {
                    if (StationFeed.HasOrChests(player, StationFeed.SharedFrom(__instance.m_fuelItem)))
                        __result = true;
                    return;
                }

                if (StationFeed.HasOrChests(player, StationFeed.SharedFrom(__instance.m_fuelItem))
                    || StationFeed.HasOrChestsAny(player, StationPullFilter.AllowedOreNames(__instance)))
                    __result = true;
            }
            finally
            {
                StationLink.Pop();
                StationHover.End();
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverAddOre")]
    internal static class SmelterHoverAddOrePatch
    {
        private static void Postfix(Smelter __instance, ref string __result)
        {
            if (__instance == null || !StationFeed.Ready())
                return;
            StationAutoFill.AppendSmelterHover(ref __result, __instance);
        }
    }

    /// <summary>
    /// Vanilla picks the first cookable item in conversion order (Wood before Core wood).
    /// Respect the per-station allow/deny filter so OFF types are never auto-selected.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "FindCookableItem")]
    internal static class SmelterFindCookablePatch
    {
        private static void Postfix(Smelter __instance, Inventory inventory, ref ItemDrop.ItemData __result)
        {
            if (__instance == null || inventory == null || !StationFeed.Ready())
                return;
            if (!StationPullFilter.CanConfigure(__instance))
                return;

            HashSet<string> denied = StationPullFilter.ReadDenied(__instance);
            if (denied.Count == 0)
                return;

            if (__result?.m_shared != null && !denied.Contains(__result.m_shared.m_name))
                return;

            __result = null;
            if (__instance.m_conversion == null)
                return;

            foreach (Smelter.ItemConversion conv in __instance.m_conversion)
            {
                string shared = StationFeed.SharedFrom(conv != null ? conv.m_from : null);
                if (string.IsNullOrEmpty(shared) || denied.Contains(shared))
                    continue;

                ItemDrop.ItemData found = inventory.GetItem(shared, -1, false);
                if (found != null)
                {
                    __result = found;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
    internal static class FireplaceInteractPatch
    {
        private static int _fillDepth;

        private static bool Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            Player player = StationFeed.LocalPlayer(user);
            // Settings chord (Alt+E) held: no vanilla [E] (add fuel / turn off) on the same press.
            if (player != null && !hold && ChestRename.BlocksStationUse())
            {
                __result = false;
                return false;
            }
            if (player == null || __instance == null || !__instance.m_canRefill)
                return true;

            // Lit + canTurnOff: [E] toggles off — but Shift+[E] must fill to max instead.
            if (__instance.m_canTurnOff && !hold && !alt && FireplaceFuel(__instance) > 0f
                && !FillToMaxInput.Held())
                return true;

            string fuel = StationFeed.SharedFrom(__instance.m_fuelItem);
            StationLink.PushStation(__instance);
            try
            {
                StationFeed.EnsureInInventory(player, fuel, 1, StationLink.Get(__instance));
            }
            finally
            {
                StationLink.Pop();
            }

            // Inventory first: vanilla takes from the bag when the pull landed.
            if (StationFeed.LocalCount(player, fuel) > 0)
                return true;

            // Chest still has fuel (or grant is in flight): skip "$msg_outof".
            if (StationFeed.Ready() && !string.IsNullOrEmpty(fuel)
                && StationFeed.ChestsHave(player, fuel, StationLink.Get(__instance)))
            {
                __result = true;
                return false;
            }

            return true;
        }

        private static void Postfix(Fireplace __instance, Humanoid user, bool hold, bool alt)
        {
            // Shift+E arrives as alt=true in Valheim, so alt must not block fill-to-max.
            if (_fillDepth > 0 || hold || !FillToMaxInput.Held())
                return;
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null || !__instance.m_canRefill)
                return;
            _fillDepth++;
            try
            {
                StationAutoFill.ManualFillFireToMax(__instance, player);
            }
            finally
            {
                _fillDepth--;
            }
        }

        /// <summary>Fireplace has no GetFuel(): the fuel is the ZDO float s_fuel.</summary>
        private static float FireplaceFuel(Fireplace fireplace)
        {
            ZNetView nv = fireplace != null ? fireplace.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return 0f;
            return nv.GetZDO().GetFloat(ZDOVars.s_fuel);
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UseItem))]
    internal static class FireplaceUseItemPatch
    {
        private static int _fillDepth;

        private static void Prefix(Fireplace __instance, Humanoid user)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null || !__instance.m_canRefill)
                return;
            StationLink.PushStation(__instance);
            try
            {
                StationFeed.EnsureInInventory(
                    player,
                    StationFeed.SharedFrom(__instance.m_fuelItem),
                    1,
                    StationLink.Get(__instance));
            }
            finally
            {
                StationLink.Pop();
            }
        }

        private static void Postfix(Fireplace __instance, Humanoid user)
        {
            if (_fillDepth > 0 || !FillToMaxInput.Held())
                return;
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null || !__instance.m_canRefill)
                return;
            _fillDepth++;
            try
            {
                StationAutoFill.ManualFillFireToMax(__instance, player);
            }
            finally
            {
                _fillDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.CanUseItems))]
    internal static class FireplaceCanUseItemsPatch
    {
        private static void Prefix(Fireplace __instance, Player player, ref bool sendErrorMessage)
        {
            StationHover.Begin();
            if (!sendErrorMessage || __instance == null || player == null)
                return;
            if (StationFeed.HasOrChests(player, StationFeed.SharedFrom(__instance.m_fuelItem)))
                sendErrorMessage = false;
        }

        private static void Postfix(Fireplace __instance, Player player, ref bool __result)
        {
            try
            {
                if (__result || !StationFeed.Ready() || player == null || __instance == null)
                    return;
                if (StationFeed.HasOrChests(player, StationFeed.SharedFrom(__instance.m_fuelItem)))
                    __result = true;
            }
            finally
            {
                StationHover.End();
            }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnAddFuelSwitch")]
    internal static class CookingAddFuelPatch
    {
        private static int _fillDepth;

        private static void Prefix(CookingStation __instance, Humanoid user)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            StationLink.PushStation(__instance);
            try
            {
                StationFeed.EnsureInInventory(
                    player,
                    StationFeed.SharedFrom(__instance.m_fuelItem),
                    1,
                    StationLink.Get(__instance));
            }
            finally
            {
                StationLink.Pop();
            }
        }

        private static void Postfix(CookingStation __instance, Humanoid user)
        {
            if (_fillDepth > 0 || !FillToMaxInput.Held())
                return;
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            _fillDepth++;
            try
            {
                StationAutoFill.ManualFillOvenFuelToMax(__instance, player);
            }
            finally
            {
                _fillDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnUseItem")]
    internal static class CookingOnUseItemPatch
    {
        private static void Prefix(CookingStation __instance, Humanoid user, ref ItemDrop.ItemData item)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (__instance == null)
                return;

            // Stamp before any chest pull so hotbar stacks with null m_dropPrefab do not NRE.
            StationFeed.EnsureCookDropPrefab(__instance, item);

            if (player == null)
                return;
            // Hotbar key with an item this station never cooks: stays vanilla (see SmelterAddOrePatch).
            if (StationFeed.IsOtherItem(item, StationPullFilter.FoodChoices(__instance)))
                return;
            StationLink.PushStation(__instance);
            try
            {
                StationFeed.EnsureForUse(player, ref item, CookingOnInteractPatch.FoodNames(__instance));
                // EnsureForUse may replace `item` with a bag stack — stamp again.
                StationFeed.EnsureCookDropPrefab(__instance, item);
            }
            finally
            {
                StationLink.Pop();
            }
        }
    }

    /// <summary>
    /// CookItem does item.m_dropPrefab.name as its first instruction.
    /// Align dropPrefab to this station's conversion when possible; skip vanilla if still null.
    /// </summary>
    [HarmonyPatch(typeof(CookingStation), "CookItem", new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class CookingCookItemDropPrefabPatch
    {
        private static bool Prefix(CookingStation __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (item == null)
            {
                __result = false;
                return false;
            }

            StationFeed.EnsureCookDropPrefab(__instance, item);

            if (item.m_dropPrefab)
                return true;

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Stamp conversion m_from before the name check so chest/hotbar stacks with a wrong
    /// or (Clone) dropPrefab still pass IsItemAllowed on this station.
    /// </summary>
    [HarmonyPatch(typeof(CookingStation), "IsItemAllowed", new[] { typeof(ItemDrop.ItemData) })]
    internal static class CookingIsItemAllowedDropPrefabPatch
    {
        private static void Prefix(CookingStation __instance, ItemDrop.ItemData item)
        {
            if (item != null)
                StationFeed.EnsureCookDropPrefab(__instance, item);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnInteract")]
    internal static class CookingOnInteractPatch
    {
        private static readonly System.Reflection.MethodInfo HaveDoneItem =
            AccessTools.Method(typeof(CookingStation), "HaveDoneItem");

        private static void Prefix(CookingStation __instance, Humanoid user)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;

            // OnInteract first collects finished food. Do not pull raw meat from chests
            // in that case (was giving cooked + raw boar on the first E).
            if (HaveDoneItem != null && FastCall.Bool(HaveDoneItem, __instance))
                return;

            StationLink.PushStation(__instance);
            try
            {
                List<string> foods = FoodNames(__instance);
                StationFeed.EnsureAny(player, foods, 1);
                // CookItem requires m_dropPrefab; stamp from conversion so chest-pulled
                // Ashlands meats (e.g. Vulture) actually land on the spit instead of only the bag.
                StampCookables(player, __instance, foods);
            }
            finally
            {
                StationLink.Pop();
            }
        }

        private static void StampCookables(Player player, CookingStation station, List<string> foods)
        {
            Inventory inv = player != null ? player.GetInventory() : null;
            if (inv == null || foods == null)
                return;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item?.m_shared == null)
                    continue;
                if (!foods.Contains(item.m_shared.m_name))
                    continue;
                StationFeed.EnsureCookDropPrefab(station, item);
            }
        }

        /// <summary>All conversion inputs (menu choices). Not filtered.</summary>
        internal static List<string> AllFoodNames(CookingStation station)
        {
            var names = new List<string>();
            if (station?.m_conversion == null)
                return names;
            foreach (CookingStation.ItemConversion conv in station.m_conversion)
            {
                ItemDrop from = conv != null ? conv.m_from : null;
                string shared = StationFeed.SharedFrom(from);
                if (string.IsNullOrEmpty(shared) && from != null && from.gameObject != null)
                    shared = ItemIds.SharedFromToken(ItemIds.StripClone(from.gameObject.name));
                if (!string.IsNullOrEmpty(shared) && !names.Contains(shared))
                    names.Add(shared);
            }
            return names;
        }

        /// <summary>Inputs allowed by the per-station chest-pull / autofill filter.</summary>
        internal static List<string> FoodNames(CookingStation station)
        {
            return StationPullFilter.AllowedFoodNames(station);
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.CanUseItems))]
    internal static class CookingCanUseItemsPatch
    {
        private static void Prefix()
        {
            StationHover.Begin();
        }

        private static void Postfix(CookingStation __instance, Player player, ref bool __result)
        {
            StationLink.PushStation(__instance);
            try
            {
                if (__result || !StationFeed.Ready() || player == null || __instance == null)
                    return;
                Switch hovered = StationHover.HoveredSwitch();
                if (hovered != null && hovered == __instance.m_addFuelSwitch)
                {
                    if (StationFeed.HasOrChests(player, StationFeed.SharedFrom(__instance.m_fuelItem)))
                        __result = true;
                    return;
                }
                if (StationFeed.HasOrChestsAny(player, CookingOnInteractPatch.FoodNames(__instance))
                    || (hovered == null && StationFeed.HasOrChests(player, StationFeed.SharedFrom(__instance.m_fuelItem))))
                    __result = true;
            }
            finally
            {
                StationLink.Pop();
                StationHover.End();
            }
        }
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Interact))]
    internal static class FermenterInteractPatch
    {
        private static bool Prefix(Fermenter __instance, Humanoid user, bool hold, ref bool __result)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return true;
            // Settings chord (Alt+E) held: open settings only, no vanilla [E] (add / tap) too.
            if (!hold && ChestRename.BlocksStationUse())
            {
                __result = false;
                return false;
            }
            // Running batch (first 60 s): [E] adds one more of the same base (bag, else chests).
            if (!hold && FermenterBatch.CanAddMore(__instance))
            {
                string same = FermenterBatch.ContentShared(__instance);
                if (!string.IsNullOrEmpty(same))
                    StationFeed.EnsureAny(player, new List<string> { same }, 1);
                __result = FermenterBatch.TryAddFrom(__instance, user, null);
                return false;
            }
            // Chest pull on [E]: only mead bases the fermenter filter allows.
            StationFeed.EnsureAny(player, StationPullFilter.AllowedMeadNames(__instance), 1);
            return true;
        }

        internal static List<string> MeadNames(Fermenter fermenter)
        {
            var names = new List<string>();
            if (fermenter?.m_conversion == null)
                return names;
            foreach (Fermenter.ItemConversion conv in fermenter.m_conversion)
            {
                string shared = StationFeed.SharedFrom(conv != null ? conv.m_from : null);
                if (!string.IsNullOrEmpty(shared) && !names.Contains(shared))
                    names.Add(shared);
            }
            return names;
        }
    }

    [HarmonyPatch(typeof(Turret), nameof(Turret.UseItem))]
    internal static class TurretUseItemPatch
    {
        private static void Prefix(Turret __instance, Humanoid user)
        {
            Player player = StationFeed.LocalPlayer(user);
            if (player == null || __instance == null)
                return;
            StationFeed.EnsureAny(player, AmmoNames(__instance), 1);
        }

        internal static List<string> AmmoNames(Turret turret)
        {
            var names = new List<string>();
            if (turret?.m_allowedAmmo == null)
                return names;
            foreach (Turret.AmmoType ammo in turret.m_allowedAmmo)
            {
                string shared = StationFeed.SharedFrom(ammo.m_ammo);
                if (!string.IsNullOrEmpty(shared) && !names.Contains(shared))
                    names.Add(shared);
            }
            return names;
        }
    }

    [HarmonyPatch(typeof(Turret), nameof(Turret.CanUseItems))]
    internal static class TurretCanUseItemsPatch
    {
        private static void Postfix(Turret __instance, Player player, ref bool __result)
        {
            if (__result || !StationFeed.Ready() || player == null || __instance == null)
                return;
            if (StationFeed.HasOrChestsAny(player, TurretUseItemPatch.AmmoNames(__instance)))
                __result = true;
        }
    }
}
