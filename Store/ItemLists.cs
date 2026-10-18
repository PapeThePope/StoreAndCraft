using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;

namespace StoreAndCraft
{
    internal enum ItemListKind
    {
        None,
        /// <summary>Auto-store never picks these up from the ground (e.g. eggs).</summary>
        GroundIgnore,
        /// <summary>Dump leaves this many in the bag.</summary>
        KeepInBag
    }

    /// <summary>
    /// The two local item lists of the F10 panel (this PC only, not synced): stored in the config as shared item names
    /// ("$item_egg,$item_wood=10,..."), parsed again only when the config text changes. The F10 buttons edit them in
    /// the display filter menu (<see cref="DisplayTypeMenu.OpenList"/>).
    /// </summary>
    internal static class ItemLists
    {
        private const int MaxAmount = 999999;

        private static string _groundRaw;
        private static readonly HashSet<string> Ground = new HashSet<string>(System.StringComparer.Ordinal);
        private static string _keepRaw;
        private static readonly Dictionary<string, int> Keep = new Dictionary<string, int>(System.StringComparer.Ordinal);

        private static ConfigEntry<string> GroundEntry => Plugin.Settings != null ? Plugin.Settings.GroundIgnoreItems : null;
        private static ConfigEntry<string> KeepEntry => Plugin.Settings != null ? Plugin.Settings.KeepInBagItems : null;

        // ---- read (auto-store / dump)

        public static bool IgnoredOnGround(string shared)
        {
            return !string.IsNullOrEmpty(shared) && GroundSet().Contains(shared);
        }

        /// <summary>How many of this item dump leaves in the bag (0 = none).</summary>
        public static int KeepAmount(string shared)
        {
            int amount;
            return !string.IsNullOrEmpty(shared) && KeepMap().TryGetValue(shared, out amount) ? amount : 0;
        }

        public static bool AnyKeep()
        {
            return KeepMap().Count > 0;
        }

        /// <summary>Number of items on a list (F10 button text).</summary>
        public static int Count(ItemListKind kind)
        {
            if (kind == ItemListKind.GroundIgnore)
                return GroundSet().Count;
            if (kind == ItemListKind.KeepInBag)
                return KeepMap().Count;
            return 0;
        }

        // ---- menu (tiles use item tokens)

        /// <summary>Shared item name for a menu token; null when it cannot be stored in the list text.</summary>
        private static string KeyOf(string token)
        {
            string shared = ItemIds.SharedFromToken(token);
            if (string.IsNullOrEmpty(shared) || shared.IndexOf(',') >= 0 || shared.IndexOf('=') >= 0)
                return null;
            return shared.Trim();
        }

        public static bool Has(ItemListKind kind, string token)
        {
            string key = KeyOf(token);
            if (key == null)
                return false;
            if (kind == ItemListKind.GroundIgnore)
                return GroundSet().Contains(key);
            if (kind == ItemListKind.KeepInBag)
                return KeepMap().ContainsKey(key);
            return false;
        }

        public static void ToggleGround(string token)
        {
            string key = KeyOf(token);
            ConfigEntry<string> entry = GroundEntry;
            if (key == null || entry == null)
                return;
            var set = new HashSet<string>(GroundSet(), System.StringComparer.Ordinal);
            if (!set.Remove(key))
                set.Add(key);
            var keys = new List<string>(set);
            keys.Sort(System.StringComparer.Ordinal);
            entry.Value = string.Join(",", keys.ToArray());
        }

        public static int GetKeep(string token)
        {
            string key = KeyOf(token);
            int amount;
            return key != null && KeepMap().TryGetValue(key, out amount) ? amount : 0;
        }

        public static void SetKeep(string token, int amount)
        {
            string key = KeyOf(token);
            ConfigEntry<string> entry = KeepEntry;
            if (key == null || entry == null)
                return;
            if (amount < 0)
                amount = 0;
            if (amount > MaxAmount)
                amount = MaxAmount;
            if (amount == GetKeep(token))
                return;
            var map = new Dictionary<string, int>(KeepMap(), System.StringComparer.Ordinal);
            if (amount > 0)
                map[key] = amount;
            else
                map.Remove(key);
            var keys = new List<string>(map.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(keys[i]).Append('=').Append(map[keys[i]].ToString(CultureInfo.InvariantCulture));
            }
            entry.Value = sb.ToString();
        }

        // ---- parse (cached by the config text)

        private static HashSet<string> GroundSet()
        {
            ConfigEntry<string> entry = GroundEntry;
            string raw = entry != null ? entry.Value ?? "" : "";
            if (raw == _groundRaw)
                return Ground;
            _groundRaw = raw;
            Ground.Clear();
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i].Trim();
                if (key.Length > 0)
                    Ground.Add(key);
            }
            return Ground;
        }

        private static Dictionary<string, int> KeepMap()
        {
            ConfigEntry<string> entry = KeepEntry;
            string raw = entry != null ? entry.Value ?? "" : "";
            if (raw == _keepRaw)
                return Keep;
            _keepRaw = raw;
            Keep.Clear();
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].LastIndexOf('=');
                if (eq <= 0)
                    continue;
                int amount;
                if (!int.TryParse(parts[i].Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount)
                    || amount <= 0)
                    continue;
                string key = parts[i].Substring(0, eq).Trim();
                if (key.Length > 0)
                    Keep[key] = amount > MaxAmount ? MaxAmount : amount;
            }
            return Keep;
        }
    }
}
