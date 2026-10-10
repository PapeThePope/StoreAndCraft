using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Armor stand: plain [E] on a slot swaps the armor on the stand with what you wear, instead of
    /// dropping it on the ground. Helmet, chest, legs and cape; every piece the stand holds is swapped
    /// at once. What you wore goes onto the stand (when the stand can show it), the stand's piece is
    /// equipped right away. Weapons / tools on the stand go straight into the bag (no replacing); without room
    /// they drop as in vanilla. Switch: ArmorStandSwap (F10).
    /// Attaching with the hotbar-use key stays vanilla.
    /// </summary>
    internal static class ArmorStandSwap
    {
        private static readonly FieldInfo NviewField = AccessTools.Field(typeof(ArmorStand), "m_nview");

        internal static bool Enabled
        {
            get
            {
                return Plugin.Settings != null && Plugin.Settings.ModEnabled.Value
                    && Plugin.Settings.ArmorStandSwap.Value;
            }
        }

        internal static bool IsArmor(ItemDrop.ItemData.ItemType t)
        {
            return t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder;
        }

        /// <summary>Same rule as vanilla UseItem: legs / chest always, other types need an "attach" child.</summary>
        internal static bool StandCanShow(ItemDrop.ItemData item)
        {
            if (item == null || item.m_dropPrefab == null)
                return false;
            ItemDrop.ItemData.ItemType t = item.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Chest)
                return true;
            Transform root = item.m_dropPrefab.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                string n = root.GetChild(i).gameObject.name;
                if (n == "attach" || n == "attach_skin")
                    return true;
            }
            return false;
        }

        internal static ItemDrop.ItemData StandItem(ArmorStand stand, ZDO zdo, int slot)
        {
            int hash = stand.GetAttachedItem(slot);
            if (hash == 0 || ObjectDB.instance == null)
                return null;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
                return null;
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            ItemDrop.LoadFromZDO(item, zdo, slot);
            item.m_stack = 1;
            item.m_equipped = false;
            return item;
        }

        /// <summary>True when the press was handled (vanilla skipped).</summary>
        internal static bool TrySwap(ArmorStand stand, Switch caller, Humanoid user)
        {
            Player player = user as Player;
            if (!Enabled || stand == null || player == null || player != Player.m_localPlayer)
                return false;
            if (!PrivateArea.CheckAccess(stand.transform.position, 0f, false))
                return false;
            ZNetView nv = NviewField != null ? NviewField.GetValue(stand) as ZNetView : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return false;

            // Only when the pressed slot holds a piece of armor; everything else stays vanilla.
            int pressed = SlotOf(stand, caller);
            if (pressed < 0)
                return false;
            ZDO zdo = nv.GetZDO();
            ItemDrop.ItemData pressedItem = StandItem(stand, zdo, pressed);
            if (pressedItem == null)
                return false;

            if (!nv.IsOwner())
                nv.ClaimOwnership();
            if (!nv.IsOwner())
                return false;

            // One switch serves all slots of the stand, so a single [E] handles everything on it:
            // armor swaps with what you wear, weapons / shield / tools with a preset (Alt+E menu) swap with the
            // preset piece from your bag.
            int swapped = 0;
            bool noRoom = false;
            bool presetHandled = false;
            for (int i = 0; i < stand.m_slots.Count; i++)
            {
                if (!stand.HaveAttachment(i))
                    continue;
                ItemDrop.ItemData standItem = StandItem(stand, zdo, i);
                if (standItem == null || !IsArmor(standItem.m_shared.m_itemType))
                    continue;
                if (SwapSlot(stand, nv, zdo, player, i, standItem))
                    swapped++;
                else
                    noRoom = true;
            }

            for (int i = 0; i < stand.m_slots.Count; i++)
            {
                int presetHash, presetQuality;
                if (!IsPresetSlot(stand, i) || !stand.HaveAttachment(i) || !GetPreset(zdo, i, out presetHash, out presetQuality))
                    continue;
                ItemDrop.ItemData standItem = StandItem(stand, zdo, i);
                if (standItem == null)
                    continue;
                presetHandled = true;
                if (SwapPreset(stand, nv, zdo, player, i, standItem, presetHash, presetQuality))
                    swapped++;
            }

            if (noRoom)
                player.Message(MessageHud.MessageType.Center, "$msg_noroom", 0, null, false);
            if (swapped > 0)
                stand.m_effects.Create(stand.transform.position, Quaternion.identity);
            if (swapped > 0 || noRoom || presetHandled)
                return true;

            // Nothing to swap: a weapon / tool on the stand goes straight into the bag (no armor, no preset).
            return !IsArmor(pressedItem.m_shared.m_itemType) && TakeToBag(stand, nv, zdo, player, pressed, pressedItem);
        }

        // ---- presets: which piece of the bag is swapped with the stand's piece in this slot (Alt+E menu)

        private const string PresetItemKey = "SAC_swapItem";
        private static readonly int PresetItemKeyHash = PresetItemKey.GetStableHashCode();
        private const string PresetQualityKey = "SAC_swapQ";
        private static readonly int PresetQualityKeyHash = PresetQualityKey.GetStableHashCode();

        /// <summary>Weapon / shield / tool slot (nothing that takes armor).</summary>
        internal static bool IsPresetSlot(ArmorStand stand, int slot)
        {
            if (stand == null || slot < 0 || slot >= stand.m_slots.Count)
                return false;
            var types = stand.m_slots[slot].m_supportedTypes;
            if (types == null || types.Count == 0)
                return false;
            for (int i = 0; i < types.Count; i++)
            {
                if (IsArmor(types[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The slot behind a switch: several slots can share one switch, so the one that holds a piece wins
        /// (same rule as vanilla UseItem). -1 when none holds a piece. Falls back to the first slot of the switch.
        /// </summary>
        internal static int SlotOf(ArmorStand stand, Switch sw, bool occupiedOnly = true)
        {
            int first = -1;
            for (int i = 0; i < stand.m_slots.Count; i++)
            {
                if (stand.m_slots[i].m_switch != sw)
                    continue;
                if (first < 0)
                    first = i;
                if (stand.HaveAttachment(i))
                    return i;
            }
            return occupiedOnly ? -1 : first;
        }

        internal static ZNetView ViewOf(ArmorStand stand)
        {
            return stand != null && NviewField != null ? NviewField.GetValue(stand) as ZNetView : null;
        }

        internal static bool GetPreset(ZDO zdo, int slot, out int hash, out int quality)
        {
            hash = zdo != null ? zdo.GetInt(PresetItemKey + slot, 0) : 0;
            quality = zdo != null ? zdo.GetInt(PresetQualityKey + slot, 1) : 1;
            return hash != 0;
        }

        /// <summary>Stores the item as the slot's preset (item == null clears it). Nothing leaves the bag.</summary>
        internal static void SetPreset(ArmorStand stand, int slot, ItemDrop.ItemData item)
        {
            ZNetView nv = ViewOf(stand);
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null || !IsPresetSlot(stand, slot))
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            if (!nv.IsOwner())
                return;
            ZDO zdo = nv.GetZDO();
            zdo.Set(PresetItemKey + slot, item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name.GetStableHashCode() : 0);
            zdo.Set(PresetQualityKey + slot, item != null ? item.m_quality : 1);
        }

        internal static ItemDrop.ItemData PresetAsItem(ZDO zdo, int slot)
        {
            int hash, quality;
            if (!GetPreset(zdo, slot, out hash, out quality) || ObjectDB.instance == null)
                return null;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
                return null;
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_quality = quality;
            return item;
        }

        private static ItemDrop.ItemData FindInBag(Inventory inv, int hash, int quality)
        {
            ItemDrop.ItemData found = null;
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                if (it.m_dropPrefab == null || it.m_quality != quality || it.m_dropPrefab.name.GetStableHashCode() != hash)
                    continue;
                if (it.m_equipped)
                    return it;
                if (found == null)
                    found = it;
            }
            return found;
        }

        /// <summary>
        /// The preset piece from the bag goes onto the stand, the stand's piece comes into the bag (and is
        /// equipped when the preset piece was). The preset then names the piece that was on the stand, so the
        /// next [E] swaps back.
        /// </summary>
        private static bool SwapPreset(ArmorStand stand, ZNetView nv, ZDO zdo, Player player, int slot,
            ItemDrop.ItemData standItem, int presetHash, int presetQuality)
        {
            Inventory inv = player.GetInventory();
            ItemDrop.ItemData mine = FindInBag(inv, presetHash, presetQuality);
            if (mine == null)
            {
                player.Message(MessageHud.MessageType.Center,
                    Loc.T("Preset item is not in your bag", "Vorlagen-Item ist nicht im Inventar"), 0, null, false);
                return false;
            }
            if (!StandCanShow(mine))
            {
                player.Message(MessageHud.MessageType.Center,
                    Loc.T("The stand cannot hold this item", "Der Ständer kann das nicht halten"), 0, null, false);
                return false;
            }

            bool wasEquipped = mine.m_equipped;
            int itemKey = (slot + "_item").GetStableHashCode();
            int variantKey = (slot + "_variant").GetStableHashCode();
            ItemDrop.ItemData store = mine.Clone();
            store.m_stack = 1;
            store.m_equipped = false;
            store.m_dropPrefab = mine.m_dropPrefab;
            int hash = mine.m_dropPrefab.name.GetStableHashCode();
            zdo.Set(itemKey, hash);
            zdo.Set(variantKey, mine.m_variant);
            ItemDrop.SaveToZDO(store, zdo, slot);
            player.UnequipItem(mine, false);
            inv.RemoveOneItem(mine);
            nv.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", slot, hash, mine.m_variant);

            standItem.m_equipped = false;
            inv.AddItem(standItem);
            if (wasEquipped)
                player.EquipItem(standItem, true);

            SetPreset(stand, slot, standItem);
            return true;
        }

        /// <summary>
        /// A weapon / tool on the stand goes into the bag (not equipped, nothing put back). Without room the
        /// vanilla behavior stays: it drops on the ground.
        /// </summary>
        private static bool TakeToBag(ArmorStand stand, ZNetView nv, ZDO zdo, Player player, int slot, ItemDrop.ItemData item)
        {
            Inventory inv = player.GetInventory();
            if (!inv.CanAddItem(item))
                return false;

            zdo.Set((slot + "_item").GetStableHashCode(), 0);
            nv.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", slot, 0, 0);
            item.m_equipped = false;
            inv.AddItem(item);
            player.Message(MessageHud.MessageType.TopLeft, "$msg_added " + item.m_shared.m_name, 1, item.GetIcon());
            stand.m_effects.Create(stand.transform.position, Quaternion.identity);
            return true;
        }

        private static bool SwapSlot(ArmorStand stand, ZNetView nv, ZDO zdo, Player player, int slot, ItemDrop.ItemData standItem)
        {
            Inventory inv = player.GetInventory();
            ItemDrop.ItemData.ItemType type = standItem.m_shared.m_itemType;
            ItemDrop.ItemData worn = inv.GetEquippedItems().FirstOrDefault(x => x.m_shared.m_itemType == type);
            bool put = worn != null && StandCanShow(worn);
            // The worn piece stays in the bag when the stand cannot show it: then the new one needs room.
            if (!put && !inv.CanAddItem(standItem))
                return false;

            int itemKey = (slot + "_item").GetStableHashCode();
            int variantKey = (slot + "_variant").GetStableHashCode();
            if (put)
            {
                ItemDrop.ItemData store = worn.Clone();
                store.m_stack = 1;
                store.m_equipped = false;
                store.m_dropPrefab = worn.m_dropPrefab;
                int hash = worn.m_dropPrefab.name.GetStableHashCode();
                zdo.Set(itemKey, hash);
                zdo.Set(variantKey, worn.m_variant);
                ItemDrop.SaveToZDO(store, zdo, slot);
                player.UnequipItem(worn, false);
                inv.RemoveOneItem(worn);
                nv.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", slot, hash, worn.m_variant);
            }
            else
            {
                zdo.Set(itemKey, 0);
                nv.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", slot, 0, 0);
            }

            standItem.m_equipped = false;
            inv.AddItem(standItem);
            player.EquipItem(standItem, true);
            return true;
        }
    }

    [HarmonyPatch(typeof(ArmorStand), "UseItem")]
    internal static class ArmorStandUseItemSwapPatch
    {
        // item == null is a plain [E]; hotbar-use (attach) passes the item and stays vanilla.
        private static bool Prefix(ArmorStand __instance, Switch caller, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (item != null)
                return true;
            // Settings chord (Alt+E) held: opens the presets menu, no swap / drop on the same press.
            if (ArmorStandSwap.Enabled && ChestRename.BlocksStationUse())
            {
                __result = false;
                return false;
            }
            if (!ArmorStandSwap.TrySwap(__instance, caller, user))
                return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Hover on a stand slot: "Take" becomes "Swap armor" / "Swap with preset" while the swap is on, plus the Alt+E hint.</summary>
    [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
    internal static class ArmorStandSwapHoverPatch
    {
        private static void Postfix(Switch __instance, ref string __result)
        {
            if (!ArmorStandSwap.Enabled || string.IsNullOrEmpty(__result) || Localization.instance == null)
                return;
            ArmorStand stand = __instance.GetComponentInParent<ArmorStand>();
            if (stand == null)
                return;
            int slot = ArmorStandSwap.SlotOf(stand, __instance, false);
            string take = Localization.instance.Localize("$piece_itemstand_take");
            if (!string.IsNullOrEmpty(take) && __result.Contains(take))
            {
                string text = Loc.T("Swap", "Tauschen");
                if (ArmorStandSwap.IsPresetSlot(stand, slot))
                {
                    ZNetView nv = ArmorStandSwap.ViewOf(stand);
                    ItemDrop.ItemData preset = nv != null && nv.IsValid() ? ArmorStandSwap.PresetAsItem(nv.GetZDO(), slot) : null;
                    text = preset != null
                        ? Loc.T("Swap with ", "Tauschen mit ") + Localization.instance.Localize(preset.m_shared.m_name)
                        : take;
                }
                __result = __result.Replace(take, text);
            }
            if (slot >= 0)
                __result += "\n[<color=yellow><b>" + StationPullFilter.PromptLabel() + "</b></color>] "
                    + Loc.T("Settings", "Einstellungen");
        }
    }
}
