using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// SAC-MIN: a minimum per item and per Storage Display, set in the display filter menu (a small "Min" field in
    /// every item tile). When the display shows less than the minimum, the count turns red (icon-only cells: the icon).
    /// Stored on the display's ZDO as "shared=amount;..." (shared with every player, read by nothing else).
    /// The counts come from the display's normal chest count: nothing is counted extra.
    /// To remove the feature: delete this file and every line marked "SAC-MIN" (StorageDisplayBoard, DisplayTypeMenu).
    /// </summary>
    internal static class DisplayMinimums
    {
        public const string ZdoKey = "SAC_disp_min";
        public static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();
        private const int MaxAmount = 999999;
        private static readonly Color AmountRed = new Color(1f, 0.3f, 0.25f, 1f);
        private static readonly Color IconRed = new Color(1f, 0.4f, 0.4f, 1f);

        // ---- read (display paint): parsed once per display ZDO revision ----

        private sealed class Cached
        {
            public uint Revision;
            public Dictionary<string, int> Map; // null = no minimum on this display
        }

        private static readonly Dictionary<ZDOID, Cached> Cache = new Dictionary<ZDOID, Cached>();

        private static Dictionary<string, int> MapFor(ZDO zdo)
        {
            if (zdo == null)
                return null;
            Cached c;
            if (Cache.TryGetValue(zdo.m_uid, out c) && c.Revision == zdo.DataRevision)
                return c.Map;
            if (c == null)
            {
                if (Cache.Count > 1024)
                    Cache.Clear();
                c = new Cached();
                Cache[zdo.m_uid] = c;
            }
            c.Revision = zdo.DataRevision;
            c.Map = Parse(zdo.GetString(ZdoKeyHash, ""));
            return c.Map;
        }

        /// <summary>True when this display has a minimum for the item and shows less than it.</summary>
        public static bool Below(ZNetView view, string shared, int count)
        {
            if (view == null || string.IsNullOrEmpty(shared) || !view.IsValid())
                return false;
            Dictionary<string, int> map = MapFor(view.GetZDO());
            int min;
            return map != null && map.TryGetValue(shared, out min) && count < min;
        }

        /// <summary>Colors a painted display cell red (count, or the icon when the cell shows no count).</summary>
        public static void MarkRed(TextMeshProUGUI amount, Image icon, bool countShown)
        {
            if (countShown && amount != null)
            {
                amount.color = AmountRed;
                amount.faceColor = AmountRed;
            }
            else if (icon != null)
            {
                icon.color = IconRed;
            }
        }

        private static Dictionary<string, int> Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            Dictionary<string, int> map = null;
            string[] parts = raw.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].LastIndexOf('=');
                if (eq <= 0)
                    continue;
                int amount;
                if (!int.TryParse(parts[i].Substring(eq + 1), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out amount) || amount <= 0)
                    continue;
                if (map == null)
                    map = new Dictionary<string, int>(System.StringComparer.Ordinal);
                map[parts[i].Substring(0, eq)] = amount;
            }
            return map;
        }

        private static string Encode(Dictionary<string, int> map)
        {
            if (map == null || map.Count == 0)
                return "";
            var keys = new List<string>(map.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(keys[i]).Append('=').Append(map[keys[i]].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Shared item name used as the key (the menu tiles use item tokens, the display shared names).</summary>
        private static string KeyOf(string token)
        {
            string shared = ItemIds.SharedFromToken(token);
            if (string.IsNullOrEmpty(shared) || shared.IndexOf('=') >= 0 || shared.IndexOf(';') >= 0)
                return null;
            return shared;
        }

        public static int Get(StorageDisplayBoard board, string token)
        {
            string key = KeyOf(token);
            ZNetView nv = board != null ? board.GetComponent<ZNetView>() : null;
            if (key == null || nv == null || !nv.IsValid())
                return 0;
            Dictionary<string, int> map = MapFor(nv.GetZDO());
            int min;
            return map != null && map.TryGetValue(key, out min) ? min : 0;
        }

        private static void Set(StorageDisplayBoard board, string token, int amount)
        {
            string key = KeyOf(token);
            ZNetView nv = board != null ? board.GetComponent<ZNetView>() : null;
            if (key == null || nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            amount = Mathf.Clamp(amount, 0, MaxAmount);
            if (amount == Get(board, token))
                return; // unchanged: no ZDO write
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            ZDO zdo = nv.GetZDO();
            Dictionary<string, int> current = MapFor(zdo);
            var map = current != null
                ? new Dictionary<string, int>(current, System.StringComparer.Ordinal)
                : new Dictionary<string, int>(System.StringComparer.Ordinal);
            if (amount > 0)
                map[key] = amount;
            else
                map.Remove(key);
            zdo.Set(ZdoKeyHash, Encode(map));
            board.RepaintSoon();
        }

        // ---- filter menu: a "Min" field in every item tile (the box itself: UI/TileAmountField) ----

        /// <summary>Adds the "Min" box to an item tile (top right, beside the icon).</summary>
        public static void AddTileField(RectTransform tile, StorageDisplayBoard board, string token)
        {
            if (tile == null || board == null || KeyOf(token) == null)
                return;
            TileAmountField.Add(tile, Loc.T("Min", "Min"), () => Get(board, token), amount => Set(board, token, amount));
        }
    }
}
