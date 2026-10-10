using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ContainerInteractRenamePatch
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold || character != Player.m_localPlayer)
                return true;

            if (!ChestRename.WantsRename(alt))
                return true;

            // Feed trough: food filter / link / auto-fill menu instead of chest settings.
            if (FeedTrough.IsTrough(__instance))
            {
                if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, false, true))
                    return true;
                StationFilterMenu.Open(__instance);
                __result = true;
                return false;
            }

            if (!ChestRename.TryOpen(__instance, false))
                return true;

            __result = true;
            return false;
        }

        private static void Postfix(Container __instance, bool __result)
        {
            if (!__result || __instance == null)
                return;
            NearbyIndex.MarkInventorySeen(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverName))]
    internal static class ContainerHoverNamePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Container __instance, ref string __result)
        {
            string custom = ChestNames.Get(__instance);
            if (string.IsNullOrEmpty(custom))
                return;

            string shown = ChestNames.ResolveDisplayName(__instance, custom);
            if (!string.IsNullOrEmpty(shown))
                __result = shown;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class ContainerHoverTextPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Container __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result))
                return;

            string custom = ChestNames.Get(__instance);
            if (!string.IsNullOrEmpty(custom))
            {
                string shown = ChestNames.ResolveDisplayName(__instance, custom);
                if (!string.IsNullOrEmpty(shown))
                    __result = ReplaceHoverTitle(__instance, __result, shown, custom);
            }

            __result += "\n[<color=yellow><b>" + ChestRename.PromptLabel() + "</b></color>] "
                + Loc.T("Settings", "Einstellungen");

            // Feed trough: [B] auto-fill line + station link (ZDO), not the chest-name link.
            if (FeedTrough.IsTrough(__instance))
            {
                StationAutoFill.AppendHover(ref __result, __instance.GetComponent<ZNetView>(), includeManualFill: false);
                StationLink.PrependHover(ref __result, StationLink.Get(__instance), chest: false);
                return;
            }

            int link = StationLink.ParseFromName(custom);
            if (link > 0)
                StationLink.PrependHover(ref __result, link, chest: true);

            // Status line only (do not tint the whole hover red/orange).
            ChestNames.PrependFullHover(ref __result, __instance);
            ChestNames.PrependStatusHover(ref __result, ChestNames.FlagsOf(__instance));
        }

        /// <summary>
        /// Force the hover title to the custom label. Empty chests often include
        /// "( Empty )" on the same line as the vanilla name — StartsWith alone
        /// fails once other hover mods rewrite the first line when items are inside.
        /// </summary>
        private static string ReplaceHoverTitle(Container container, string hover, string shown, string custom)
        {
            string token = Refs.VanillaHoverName(container);
            string vanilla = token;
            if (!string.IsNullOrEmpty(vanilla) && Localization.instance != null)
                vanilla = Localization.instance.Localize(vanilla);

            if (!string.IsNullOrEmpty(vanilla) && hover.IndexOf(vanilla, System.StringComparison.Ordinal) >= 0)
                hover = hover.Replace(vanilla, shown);
            else if (!string.IsNullOrEmpty(custom) && custom != shown
                && hover.IndexOf(custom, System.StringComparison.Ordinal) >= 0)
                hover = hover.Replace(custom, shown);
            else
            {
                int nl = hover.IndexOf('\n');
                if (nl >= 0)
                    hover = shown + hover.Substring(nl);
                else if (hover.IndexOf(shown, System.StringComparison.Ordinal) < 0)
                    hover = shown + "\n" + hover;
            }

            return hover;
        }
    }

    /// <summary>
    /// Open-chest panel title uses InventoryGui.m_containerName from vanilla m_name
    /// ("Chest"), ignoring GetHoverName — so renamed chests looked wrong once opened
    /// with items inside.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    internal static class InventoryGuiContainerNamePatch
    {
        private static readonly FieldInfo CurrentContainer =
            AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly FieldInfo ContainerNameLabel =
            AccessTools.Field(typeof(InventoryGui), "m_containerName");

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryGui __instance)
        {
            if (__instance == null || CurrentContainer == null || ContainerNameLabel == null)
                return;

            Container container = CurrentContainer.GetValue(__instance) as Container;
            if (container == null)
                return;

            string custom = ChestNames.Get(container);
            if (string.IsNullOrEmpty(custom))
                return;

            string shown = ChestNames.ResolveDisplayName(container, custom);
            if (string.IsNullOrEmpty(shown))
                return;

            object label = ContainerNameLabel.GetValue(__instance);
            if (label == null)
                return;

            PropertyInfo textProp = label.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (textProp == null || !textProp.CanWrite)
                return;

            object current = textProp.GetValue(label, null);
            if (current is string s && s == shown)
                return;

            textProp.SetValue(label, shown, null);
        }
    }
}
