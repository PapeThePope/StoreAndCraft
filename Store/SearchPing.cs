using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class SearchPing
    {
        private static Container _blinkChest;
        private static int _blinkLeft;
        private static float _nextBlinkAt;

        public static void PingItem(ItemDrop.ItemData item)
        {
            if (item == null)
                return;
            Search(ItemIds.SharedName(item), ItemIds.PrefabName(item));
        }

        public static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            string query = args != null && args.Length > 1 ? args[1] : null;
            if (string.IsNullOrEmpty(query))
            {
                ItemDrop.ItemData hovered = HoverStore.GetHoveredPlayerItem();
                if (hovered != null)
                    PingItem(hovered);
                else
                    Tell(Loc.T("Usage: storesearch <item>", "Nutzung: storesearch <item>"));
                return;
            }

            Search(query, query);
        }

        public static void Tick()
        {
            if (_blinkLeft <= 0 || _blinkChest == null)
                return;
            if (Time.time < _nextBlinkAt)
                return;

            TransferService.Flash(_blinkChest);
            _blinkLeft--;
            _nextBlinkAt = Time.time + 0.35f;
            if (_blinkLeft <= 0)
                _blinkChest = null;
        }

        public static void Search(string sharedOrToken, string prefabHint)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            string shared = ItemIds.SharedFromToken(sharedOrToken);
            float range = Plugin.Settings != null ? Plugin.Settings.StorageReach() : NearbyIndex.ScanRange();
            NearbyIndex.Tick();
            List<Container> holding = ChestPicker.FindHolding(player.transform.position, range, shared);

            if (holding.Count == 0 && !string.IsNullOrEmpty(prefabHint))
            {
                string alt = ItemIds.SharedFromToken(prefabHint);
                if (alt != shared)
                    holding = ChestPicker.FindHolding(player.transform.position, range, alt);
            }

            if (holding.Count == 0)
            {
                Tell(Loc.T("No nearby chest has that item.", "Keine nahe Truhe hat dieses Item."));
                return;
            }

            Container closest = holding[0];
            int total = 0;
            foreach (Container c in holding)
            {
                Inventory inv = c.GetInventory();
                if (inv != null)
                    total += inv.CountItems(shared, -1, true);
            }

            StartBlink(closest, 3);
            if (Chat.instance != null)
                Chat.instance.SendPing(closest.transform.position);

            string label = prefabHint;
            if (global::Localization.instance != null && !string.IsNullOrEmpty(shared))
                label = global::Localization.instance.Localize(shared);

            Tell(Loc.T(
                label + ": " + total + " in " + holding.Count + " chest(s).",
                label + ": " + total + " in " + holding.Count + " Truhe(n)."));
        }

        private static void StartBlink(Container chest, int times, float delay = 0f)
        {
            _blinkChest = chest;
            _blinkLeft = Mathf.Max(1, times);
            _nextBlinkAt = Time.time + delay;
        }

        // ---- display menu: right click on an item → camera flies to a chest of this display that holds it

        private static string _locateShared;
        private static int _locateIndex;
        private static float _locateAt;

        /// <summary>
        /// Chests this display counts (same filters as the display itself) that hold the item. The camera flies to
        /// the nearest one and it blinks; clicking the same item again goes on to the next chest.
        /// </summary>
        public static void LocateForBoard(StorageDisplayBoard board, string token)
        {
            Player player = Player.m_localPlayer;
            if (board == null || player == null || string.IsNullOrEmpty(token) || LocateCamera.Active)
                return;

            string shared = ItemIds.SharedFromToken(token);
            Vector3 origin = board.transform.position;
            NearbyIndex.Tick();
            var near = new List<Container>();
            NearbyIndex.CollectNear(origin, board.EffectiveDisplayRange(), near);

            var holding = new List<Container>();
            foreach (Container chest in near)
            {
                if (chest == null || !ContainerFilter.IsPlayerBuiltStorage(chest)
                    || !ContainerFilter.PlayerMayUse(chest, origin) || ChestNames.IsFullyIgnored(chest))
                    continue;
                NearbyIndex.EnsureInventory(chest);
                if (ChestPicker.CountShared(chest.GetInventory(), shared) > 0)
                    holding.Add(chest);
            }

            if (holding.Count == 0)
            {
                Tell(Loc.T("No chest in range of this display has that item.", "Keine Truhe in Reichweite dieser Anzeige hat dieses Item."));
                return;
            }

            holding.Sort((a, b) => ContainerFilter.Distance(origin, a.transform.position)
                .CompareTo(ContainerFilter.Distance(origin, b.transform.position)));

            bool again = shared == _locateShared && Time.unscaledTime - _locateAt < 60f;
            _locateIndex = again ? (_locateIndex + 1) % holding.Count : 0;
            _locateShared = shared;
            _locateAt = Time.unscaledTime;

            Container target = holding[_locateIndex];
            LocateCamera.Fly(target.transform.position);
            StartBlink(target, 5, LocateCamera.ArriveDelay);
            Tell(Loc.T("Chest ", "Truhe ") + (_locateIndex + 1) + " / " + holding.Count);
        }

        private static void Tell(string msg)
        {
            Player player = Player.m_localPlayer;
            if (player != null)
                player.Message(MessageHud.MessageType.Center, msg, 0, null, false);
        }
    }
}
