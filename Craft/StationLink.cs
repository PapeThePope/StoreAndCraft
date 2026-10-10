using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Optional chest↔station channels [l1]…[l9] (lowercase L).
    /// Station: toggle in Alt+E filter (3×3). Chest: tag in rename label.
    /// Legacy [link1]…[link9] still parsed.
    /// </summary>
    internal static class StationLink
    {
        public const string ZdoKey = "SAC_stationLink";
        public static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();
        public const int MaxId = 9;

        /// <summary>
        /// Pull context: -1 = no link filter (craft / EL / default).
        /// 0 = station with no link (untagged chests only).
        /// 1–9 = only matching [lN] chests.
        /// </summary>
        public static int ActiveId { get; private set; } = -1;

        /// <summary>
        /// Nested Push/Pop (autofill → OnAddFuel Prefix, Shift+fill, etc.) must restore
        /// the outer link. A bare ActiveId=-1 Pop let kiln fuel ConsumeFromChests use
        /// every nearby chest after the first nested EnsureInInventory.
        /// </summary>
        private static readonly List<int> IdStack = new List<int>(4);

        // Bright colors — no red / orange (those are [I] / [H]).
        private static readonly string[] ColorHex =
        {
            null,
            "#5CDBFF", // 1 cyan
            "#7CFF6B", // 2 green
            "#FFE66D", // 3 yellow
            "#C77DFF", // 4 purple
            "#FF6BCB", // 5 pink
            "#6BFFD1", // 6 mint
            "#6B9FFF", // 7 blue
            "#B8FF6B", // 8 lime
            "#E8B8FF"  // 9 lilac
        };

        private static readonly Color[] ColorRgb =
        {
            Color.white,
            Hex(0x5CDBFF),
            Hex(0x7CFF6B),
            Hex(0xFFE66D),
            Hex(0xC77DFF),
            Hex(0xFF6BCB),
            Hex(0x6BFFD1),
            Hex(0x6B9FFF),
            Hex(0xB8FF6B),
            Hex(0xE8B8FF)
        };

        public static void ResetFrame()
        {
            IdStack.Clear();
            ActiveId = -1;
        }

        public static void Push(int linkId)
        {
            IdStack.Add(ActiveId);
            ActiveId = Clamp(linkId);
        }

        public static void PushStation(Component station)
        {
            // Link ≥ 1 → only [lN] chests. Link 0 → only untagged chests (linked chests stay exclusive).
            IdStack.Add(ActiveId);
            ActiveId = Get(station);
        }

        public static void Pop()
        {
            int n = IdStack.Count;
            if (n <= 0)
            {
                ActiveId = -1;
                return;
            }

            ActiveId = IdStack[n - 1];
            IdStack.RemoveAt(n - 1);
        }

        public static int Clamp(int id)
        {
            if (id < 0)
                return 0;
            if (id > MaxId)
                return MaxId;
            return id;
        }

        public static int Get(Component station)
        {
            // Fires and torches take a single fuel type, so they never use links.
            if (station is Fireplace)
                return 0;
            return Get(View(station));
        }

        public static int Get(ZNetView nv)
        {
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return 0;
            return Clamp(zdo.GetInt(ZdoKeyHash, 0));
        }

        public static void Set(Component station, int linkId)
        {
            Set(View(station), linkId);
        }

        public static void Set(ZNetView nv, int linkId)
        {
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            nv.GetZDO().Set(ZdoKeyHash, Clamp(linkId));
        }

        /// <summary>Toggle: same id again clears to none.</summary>
        public static void Toggle(Component station, int linkId)
        {
            linkId = Clamp(linkId);
            if (linkId <= 0)
            {
                Set(station, 0);
                return;
            }

            Set(station, Get(station) == linkId ? 0 : linkId);
        }

        /// <summary>
        /// First [l1]…[l9] or legacy [link1]…[link9] in the label. 0 if none.
        /// </summary>
        public static int ParseFromName(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return 0;

            string s = stored;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '[')
                    continue;

                // [lN]
                if (i + 3 < s.Length
                    && (s[i + 1] == 'l' || s[i + 1] == 'L')
                    && s[i + 2] >= '1' && s[i + 2] <= '9'
                    && s[i + 3] == ']')
                {
                    return s[i + 2] - '0';
                }

                // Legacy [linkN]
                if (!StartsWithIgnoreCase(s, i + 1, "link"))
                    continue;
                int digitAt = i + 5;
                if (digitAt + 1 >= s.Length)
                    continue;
                char d = s[digitAt];
                if (d < '1' || d > '9' || s[digitAt + 1] != ']')
                    continue;
                return d - '0';
            }

            return 0;
        }

        public static int ChestLinkId(Container chest)
        {
            return ParseFromName(ChestNames.Get(chest));
        }

        /// <summary>
        /// Set (try/finally) only while a feed trough is being auto-filled: troughs are then no
        /// pull source, so a trough never refills from itself or another trough.
        /// </summary>
        internal static bool ExcludeTroughs;

        public static bool ChestAllowed(Container chest, int stationLinkId)
        {
            if (chest == null || ChestNames.IsIgnored(chest))
                return false;
            if (ExcludeTroughs && FeedTrough.IsTrough(chest))
                return false;

            // Link -1 = craft / build (no station): chests set to Only stations are for station in / output only.
            if (stationLinkId < 0 && ChestNames.IsNoDump(chest))
                return false;

            // Chests set to Only build/craft are for crafting and building only, no station takes from them.
            if (stationLinkId >= 0 && ChestNames.IsCraftOnly(chest))
                return false;

            if (stationLinkId < 0)
                return true;

            int chestLink = ChestLinkId(chest);
            stationLinkId = Clamp(stationLinkId);

            if (stationLinkId == 0)
                return chestLink == 0;
            return chestLink == stationLinkId;
        }

        /// <summary>
        /// Auto-store output (N): matching [lN] or untagged. Never a different link.
        /// Fill still uses <see cref="ChestAllowed"/> (linked → matching only).
        /// </summary>
        public static bool ChestAllowedForOutput(Container chest, int stationLinkId)
        {
            if (chest == null || ChestNames.BlocksDeposit(chest))
                return false;

            if (stationLinkId < 0)
                return true;

            int chestLink = ChestLinkId(chest);
            stationLinkId = Clamp(stationLinkId);

            if (stationLinkId == 0)
                return chestLink == 0;
            return chestLink == 0 || chestLink == stationLinkId;
        }

        public static bool ChestAllowedForActive(Container chest)
        {
            return ChestAllowed(chest, ActiveId);
        }

        /// <summary>Short token without brackets, e.g. l3.</summary>
        public static string ShortToken(int linkId)
        {
            linkId = Clamp(linkId);
            if (linkId <= 0)
                return Loc.T("none", "keiner");
            return "l" + linkId;
        }

        /// <summary>Bracket tag for names / hover, e.g. [l3].</summary>
        public static string Tag(int linkId)
        {
            linkId = Clamp(linkId);
            if (linkId <= 0)
                return "";
            return "[l" + linkId + "]";
        }

        /// <summary>Put colored link line above the rest of the hover text.</summary>
        public static void PrependHover(ref string text, int linkId, bool chest)
        {
            linkId = Clamp(linkId);
            if (linkId <= 0 || string.IsNullOrEmpty(text))
                return;

            string line = Loc.T("Link: ", "Link: ") + linkId;
            text = Colorize(linkId, line) + "\n" + text;
        }

        /// <summary>Remove [lN] / legacy [linkN] tags from a chest label.</summary>
        public static string StripTags(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return string.Empty;

            var sb = new System.Text.StringBuilder(stored.Length);
            for (int i = 0; i < stored.Length; i++)
            {
                if (stored[i] != '[')
                {
                    sb.Append(stored[i]);
                    continue;
                }

                // [lN]
                if (i + 3 < stored.Length
                    && (stored[i + 1] == 'l' || stored[i + 1] == 'L')
                    && stored[i + 2] >= '1' && stored[i + 2] <= '9'
                    && stored[i + 3] == ']')
                {
                    i += 3;
                    continue;
                }

                // [linkN]
                if (StartsWithIgnoreCase(stored, i + 1, "link")
                    && i + 6 < stored.Length
                    && stored[i + 5] >= '1' && stored[i + 5] <= '9'
                    && stored[i + 6] == ']')
                {
                    i += 6;
                    continue;
                }

                sb.Append(stored[i]);
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Set or clear link tag in a chest name. Same id again clears. Keeps [I]/[H]/[M] and the rest.
        /// </summary>
        public static string ApplyToName(string stored, int linkId)
        {
            linkId = Clamp(linkId);
            int current = ParseFromName(stored);
            if (linkId > 0 && current == linkId)
                linkId = 0;

            string prefix = "";
            string work;
            string trimmed = (stored ?? "").TrimStart();
            if (trimmed.StartsWith("[I]", System.StringComparison.OrdinalIgnoreCase))
            {
                prefix = "[I] ";
                work = StripTags(trimmed.Substring(3)).TrimStart();
            }
            else if (trimmed.StartsWith("[H]", System.StringComparison.OrdinalIgnoreCase))
            {
                prefix = "[H] ";
                work = StripTags(trimmed.Substring(3)).TrimStart();
            }
            else if (trimmed.StartsWith("[M]", System.StringComparison.OrdinalIgnoreCase))
            {
                prefix = "[M] ";
                work = StripTags(trimmed.Substring(3)).TrimStart();
            }
            else
            {
                work = StripTags(stored);
            }

            if (linkId <= 0)
                return (prefix + work).Trim();

            return (prefix + Tag(linkId) + (string.IsNullOrEmpty(work) ? "" : " " + work)).Trim();
        }

        public static string HexColor(int linkId)
        {
            linkId = Clamp(linkId);
            if (linkId <= 0 || linkId >= ColorHex.Length)
                return "#FFFFFF";
            return ColorHex[linkId];
        }

        public static Color UiColor(int linkId)
        {
            linkId = Clamp(linkId);
            if (linkId <= 0 || linkId >= ColorRgb.Length)
                return new Color(0.75f, 0.75f, 0.8f, 1f);
            return ColorRgb[linkId];
        }

        public static string Colorize(int linkId, string inner)
        {
            if (string.IsNullOrEmpty(inner) || linkId <= 0)
                return inner;
            return "<color=" + HexColor(linkId) + ">" + inner + "</color>";
        }

        public static string Label(int linkId)
        {
            linkId = Clamp(linkId);
            if (linkId == 0)
                return Loc.T("l: none", "l: keiner");
            return ShortToken(linkId);
        }

        private static Color Hex(int rgb)
        {
            float r = ((rgb >> 16) & 0xFF) / 255f;
            float g = ((rgb >> 8) & 0xFF) / 255f;
            float b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }

        private static bool StartsWithIgnoreCase(string s, int index, string token)
        {
            if (index + token.Length > s.Length)
                return false;
            for (int i = 0; i < token.Length; i++)
            {
                char a = s[index + i];
                char b = token[i];
                if (a >= 'A' && a <= 'Z')
                    a = (char)(a + 32);
                if (a != b)
                    return false;
            }
            return true;
        }

        private static ZNetView View(Component c)
        {
            return c != null ? c.GetComponent<ZNetView>() : null;
        }
    }
}
