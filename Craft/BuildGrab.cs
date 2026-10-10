using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Hold BuildGrabKey (default C) + place while the hammer is in place mode:
    /// never build — pull the ghost piece's costs from nearby chests into the bag.
    /// Must cancel at TryPlacePiece (not PlacePiece): UpdatePlacement calls ConsumeResources
    /// after a successful TryPlacePiece, which would eat the stacks we just withdrew.
    /// Shift stays free for vanilla no-snap placement.
    /// </summary>
    internal static class BuildGrab
    {
        private static readonly List<Container> Scratch = new List<Container>(64);
        private static float _lastGrabMsg;

        /// <summary>True when the grab modifier is held (config BuildGrabKey, default C).</summary>
        public static bool GrabHeld()
        {
            if (Plugin.Settings == null)
                return false;
            return KeyUtil.Held(Plugin.Settings.BuildGrabKey.Value);
        }

        /// <summary>Hammer place mode only — not hoe/cultivator. Shift stays free for no-snap.</summary>
        public static bool IsHammerPlaceMode(Player player)
        {
            if (player == null || !player.InPlaceMode())
                return false;

            PieceTable table = player.GetBuildTool();
            if (table == null)
                return false;

            string name = table.gameObject != null ? table.gameObject.name : table.name;
            if (string.IsNullOrEmpty(name))
                return false;

            return name.IndexOf("Hammer", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ShouldGrab(Player player)
        {
            return GrabHeld() && IsHammerPlaceMode(player);
        }

        /// <summary>Returns true if place must be cancelled (grab active).</summary>
        public static bool TryInterceptPlace(Player player, Piece piece)
        {
            if (player == null || player != Player.m_localPlayer || piece == null)
                return false;
            if (!ShouldGrab(player))
                return false;
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value || !Plugin.Settings.CraftEnabled.Value)
            {
                MaybeMsg(player, Loc.T("Craft/build from chests is disabled", "Craft/Bauen aus Truhen ist aus"));
                return true; // still block place while grab is held
            }

            GrabIntoInventory(player, piece);
            return true;
        }

        private static void GrabIntoInventory(Player player, Piece piece)
        {
            Piece.Requirement[] reqs = piece.m_resources;
            if (reqs == null || reqs.Length == 0)
            {
                MaybeMsg(player, Loc.T("Nothing to grab for this piece", "Nichts zum Holen für dieses Piece"));
                return;
            }

            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            NearbyIndex.Tick();
            bool leaveOne = Plugin.Settings.LeaveOneItem.Value;
            float range = Plugin.Settings.CraftReach();
            Vector3 origin = player.transform.position;
            Scratch.Clear();
            NearbyIndex.CollectNear(origin, range, Scratch);

            int pulled = 0;
            int missing = 0;

            InventoryCountPatches.Skip++;
            try
            {
                foreach (Piece.Requirement req in reqs)
                {
                    if (req?.m_resItem?.m_itemData?.m_shared == null)
                        continue;
                    int need = req.GetAmount(1);
                    if (need <= 0)
                        continue;

                    string shared = req.m_resItem.m_itemData.m_shared.m_name;
                    // Always pull one full piece-cost set per grab-click, even if the bag
                    // already has enough (so you can stockpile for several builds).
                    int still = need;
                    for (int i = 0; i < Scratch.Count; i++)
                    {
                        Container chest = Scratch[i];
                        if (still <= 0)
                            break;
                        if (chest == null || ChestNames.BlocksCraft(chest))
                            continue;
                        if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                            continue;
                        if (!ContainerFilter.PlayerMayUse(chest, origin))
                            continue;
                        if (ContainerFilter.Distance(origin, chest.transform.position) > range)
                            continue;

                        NearbyIndex.EnsureInventory(chest);
                        int took = TransferService.WithdrawForGrab(chest, shared, still, player, leaveOne);
                        ActivityLog.Move(Loc.T("Build grab", "Bau-Grab"), ActivityLog.Chest(chest), took, shared, ActivityLog.Bag());
                        still -= took;
                        pulled += took;
                    }

                    if (still > 0)
                        missing += still;
                }
            }
            finally
            {
                InventoryCountPatches.Skip--;
            }

            if (pulled > 0)
                Refs.NotifyChanged(inv);

            if (pulled <= 0 && missing > 0)
                MaybeMsg(player, Loc.T("No materials in nearby chests", "Keine Materialien in nahen Truhen"));
            else if (missing > 0)
                MaybeMsg(player, Loc.T("Grabbed some materials (still short)", "Material geholt (noch zu wenig)"));
            else if (pulled > 0)
                MaybeMsg(player, Loc.T("Grabbed build materials from chests", "Baumaterial aus Truhen geholt"));
            else
                MaybeMsg(player, Loc.T("Could not grab materials (bag full?)", "Kein Material geholt (Tasche voll?)"));
        }

        private static void MaybeMsg(Player player, string text)
        {
            if (player == null || string.IsNullOrEmpty(text))
                return;
            if (Time.time - _lastGrabMsg < 0.35f)
                return;
            _lastGrabMsg = Time.time;
            player.Message(MessageHud.MessageType.Center, text, 0, null, false);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    [HarmonyPriority(Priority.First)]
    internal static class TryPlacePieceGrabPatch
    {
        // UpdatePlacement only calls ConsumeResources when TryPlacePiece returns true.
        // Returning false here keeps grabbed mats in the bag (inventory UI can stay closed).
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!BuildGrab.TryInterceptPlace(__instance, piece))
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    [HarmonyPriority(Priority.First)]
    internal static class PlacePieceGrabPatch
    {
        // Backup: if something calls PlacePiece directly while grab is active, still skip spawn.
        private static bool Prefix(Player __instance, Piece piece)
        {
            if (__instance == Player.m_localPlayer && BuildGrab.ShouldGrab(__instance))
                return false;
            return true;
        }
    }
}
