using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class Refs
    {
        private static readonly FieldInfo ItemDropNview = AccessTools.Field(typeof(ItemDrop), "m_nview");
        private static readonly FieldInfo ItemDropInstances = AccessTools.Field(typeof(ItemDrop), "s_instances");
        private static readonly FieldInfo ContainerNview = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly FieldInfo ContainerName = AccessTools.Field(typeof(Container), "m_name");
        private static readonly FieldInfo GuiRecipe = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");
        private static readonly FieldInfo GuiMulti = AccessTools.Field(typeof(InventoryGui), "m_multiCrafting");
        private static readonly FieldInfo GuiMultiAmount = AccessTools.Field(typeof(InventoryGui), "m_multiCraftAmount");
        private static readonly FieldInfo GuiUpgrade = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
        private static readonly FieldInfo WearRenderersField = AccessTools.Field(typeof(WearNTear), "m_renderers");
        private static readonly MethodInfo InventoryChanged = FindInventoryChanged();

        // Compiled field access for the hot paths (every chest check, every drop): FieldInfo.GetValue is reflection
        // on each call. Null when the field is missing in this Valheim version: then the FieldInfo / GetComponent path.
        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerNviewRef = RefOrNull<Container, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> ItemDropNviewRef = RefOrNull<ItemDrop, ZNetView>("m_nview");

        private static AccessTools.FieldRef<T, F> RefOrNull<T, F>(string field) where T : class
        {
            try
            {
                return AccessTools.Field(typeof(T), field) != null ? AccessTools.FieldRefAccess<T, F>(field) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Inventory.Changed, whatever its parameters are in this Valheim version (no Harmony warning when it differs).</summary>
        private static MethodInfo FindInventoryChanged()
        {
            MethodInfo any = null;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(Inventory)))
            {
                if (m.Name != "Changed")
                    continue;
                if (m.GetParameters().Length == 0)
                    return m;
                if (any == null)
                    any = m;
            }
            return any;
        }

        public static ZNetView View(Container container)
        {
            if (container == null)
                return null;
            if (ContainerNviewRef != null)
            {
                ZNetView fast = ContainerNviewRef(container);
                if (fast != null)
                    return fast;
            }
            else if (ContainerNview != null)
            {
                var nv = ContainerNview.GetValue(container) as ZNetView;
                if (nv != null)
                    return nv;
            }
            return container.GetComponent<ZNetView>();
        }

        public static string VanillaHoverName(Container container)
        {
            if (container == null || ContainerName == null)
                return null;
            return ContainerName.GetValue(container) as string;
        }

        public static ZNetView View(ItemDrop drop)
        {
            if (drop == null)
                return null;
            if (ItemDropNviewRef != null)
            {
                ZNetView fast = ItemDropNviewRef(drop);
                if (fast != null)
                    return fast;
            }
            else if (ItemDropNview != null)
            {
                var nv = ItemDropNview.GetValue(drop) as ZNetView;
                if (nv != null)
                    return nv;
            }
            return drop.GetComponent<ZNetView>();
        }

        public static List<ItemDrop> Drops()
        {
            if (ItemDropInstances == null)
                return null;
            return ItemDropInstances.GetValue(null) as List<ItemDrop>;
        }

        /// <summary>WearNTear's renderer cache (filled in Awake). Null before Awake / if missing.</summary>
        public static List<Renderer> WearRenderers(WearNTear wnt)
        {
            return wnt != null && WearRenderersField != null ? WearRenderersField.GetValue(wnt) as List<Renderer> : null;
        }

        public static Recipe CraftRecipe(InventoryGui gui)
        {
            return gui != null && GuiRecipe != null ? GuiRecipe.GetValue(gui) as Recipe : null;
        }

        public static ItemDrop.ItemData CraftUpgradeItem(InventoryGui gui)
        {
            return gui != null && GuiUpgrade != null ? GuiUpgrade.GetValue(gui) as ItemDrop.ItemData : null;
        }

        public static bool MultiCrafting(InventoryGui gui)
        {
            if (gui == null || GuiMulti == null)
                return false;
            object v = GuiMulti.GetValue(gui);
            return v is bool && (bool)v;
        }

        public static int MultiCraftAmount(InventoryGui gui)
        {
            if (gui == null || GuiMultiAmount == null)
                return 1;
            object v = GuiMultiAmount.GetValue(gui);
            return v is int ? Mathf.Max(1, (int)v) : 1;
        }

        public static void NotifyChanged(Inventory inventory)
        {
            if (inventory == null || InventoryChanged == null)
                return;
            try
            {
                if (InventoryChanged.GetParameters().Length == 0)
                    InventoryChanged.Invoke(inventory, null);
                else
                    InventoryChanged.Invoke(inventory, new object[] { true, false });
            }
            catch
            {
                InventoryChanged.Invoke(inventory, null);
            }
        }
    }
}
