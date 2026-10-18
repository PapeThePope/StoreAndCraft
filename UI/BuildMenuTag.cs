using System.Reflection;
using HarmonyLib;

namespace StoreAndCraft
{
    /// <summary>
    /// Own "Store and Craft" tag in the hammer build menu (Valheim's tags are Piece.UsageTagFlags; the menu lists
    /// them from ByUsagePieceList.m_usageTags). One more flag bit is added to that list when it is created; Valheim
    /// handles every bit the same way (pieces with it show under the tag). Jotunn numbers its own category tags after
    /// that list, so they come after ours and nothing collides.
    /// When the list cannot be extended (game update), SAC pieces keep the wood chest's tags (Storage) as before.
    /// </summary>
    internal static class BuildMenuTag
    {
        /// <summary>Unused by Valheim (its highest tag is 0x80000).</summary>
        internal const Piece.UsageTagFlags Tag = (Piece.UsageTagFlags)(1 << 30);
        internal const string Name = "Store and Craft";

        internal static bool Available { get; set; }

        /// <summary>The build-menu tags for a SAC piece: our own tag, or the chest's when it is not available.</summary>
        internal static Piece.UsageTagFlags UsageFor(Piece chest)
        {
            if (Available)
                return Tag;
            return chest != null ? chest.m_usage : Piece.UsageTagFlags.Storage;
        }
    }

    [HarmonyPatch(typeof(ByUsagePieceList), MethodType.Constructor, new[] { typeof(string) })]
    internal static class BuildMenuTagPatch
    {
        private static readonly FieldInfo TagsField = AccessTools.Field(typeof(ByUsagePieceList), "m_usageTags");
        private static readonly FieldInfo NamesField = AccessTools.Field(typeof(ByUsagePieceList), "m_usageTagDisplayNames");

        private static bool Prepare()
        {
            BuildMenuTag.Available = TagsField != null && NamesField != null
                && TagsField.FieldType == typeof(Piece.UsageTagFlags[]) && NamesField.FieldType == typeof(string[]);
            return BuildMenuTag.Available;
        }

        private static void Postfix(ByUsagePieceList __instance)
        {
            var tags = TagsField.GetValue(__instance) as Piece.UsageTagFlags[];
            var names = NamesField.GetValue(__instance) as string[];
            if (tags == null || names == null || tags.Length != names.Length
                || System.Array.IndexOf(tags, BuildMenuTag.Tag) >= 0)
                return;
            var newTags = new Piece.UsageTagFlags[tags.Length + 1];
            var newNames = new string[names.Length + 1];
            tags.CopyTo(newTags, 0);
            names.CopyTo(newNames, 0);
            newTags[tags.Length] = BuildMenuTag.Tag;
            newNames[names.Length] = BuildMenuTag.Name;
            TagsField.SetValue(__instance, newTags);
            NamesField.SetValue(__instance, newNames);
        }
    }
}
