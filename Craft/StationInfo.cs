using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Console (F5) "sacstation": what the station you look at is made of (components, smelter inputs / outputs /
    /// switches, SAC switches). For support: a station that behaves differently from the others can be understood
    /// from this output without guessing. Read only, changes nothing.
    /// </summary>
    internal static class StationInfo
    {
        public static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            GameObject hover = player.GetHoverObject();
            Smelter smelter = hover != null ? hover.GetComponentInParent<Smelter>() : null;
            if (smelter == null)
                smelter = Nearest(player.transform.position);

            var lines = new List<string>();
            if (hover != null)
            {
                lines.Add("Looking at: " + hover.name + " (root " + Utils.GetPrefabName(hover.transform.root.gameObject) + ")");
                lines.Add("Components: " + Components(hover));
            }
            if (smelter == null)
            {
                lines.Add("No Smelter-type station looked at or within 8 m.");
            }
            else
            {
                Describe(smelter, lines);
            }

            foreach (string line in lines)
            {
                Plugin.Log.LogInfo("[sacstation] " + line);
                if (args != null && args.Context != null)
                    args.Context.AddString(line);
            }
        }

        private static Smelter Nearest(Vector3 pos)
        {
            Smelter best = null;
            float bestSqr = 8f * 8f;
            foreach (Smelter s in Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None))
            {
                if (s == null)
                    continue;
                float d = (s.transform.position - pos).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = s;
                }
            }
            return best;
        }

        private static string Components(GameObject go)
        {
            var names = new List<string>();
            foreach (MonoBehaviour mb in go.transform.root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                string n = mb.GetType().Name;
                if (!names.Contains(n))
                    names.Add(n);
            }
            return string.Join(", ", names.ToArray());
        }

        private static string Item(ItemDrop drop)
        {
            if (drop == null)
                return "-";
            string shared = drop.m_itemData != null && drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : "?";
            return drop.gameObject.name + " (" + shared + ")";
        }

        private static void Describe(Smelter s, List<string> lines)
        {
            lines.Add("Smelter: " + Utils.GetPrefabName(s.gameObject));
            lines.Add("  fuel item: " + Item(s.m_fuelItem) + ", max fuel " + s.m_maxFuel + ", max ore " + s.m_maxOre
                + ", spawn stack " + s.m_spawnStack + ", needs roof " + s.m_requiresRoof);
            lines.Add("  switches: add ore " + (s.m_addOreSwitch != null) + ", add wood/fuel " + (s.m_addWoodSwitch != null)
                + ", empty " + (s.m_emptyOreSwitch != null));
            var sb = new StringBuilder("  conversions: ");
            if (s.m_conversion != null)
            {
                for (int i = 0; i < s.m_conversion.Count; i++)
                {
                    Smelter.ItemConversion c = s.m_conversion[i];
                    if (c == null)
                        continue;
                    if (i > 0)
                        sb.Append("; ");
                    sb.Append(c.m_from != null ? c.m_from.gameObject.name : "(no input)").Append(" -> ")
                        .Append(c.m_to != null ? c.m_to.gameObject.name : "-");
                }
            }
            lines.Add(sb.ToString());
            List<string> allowed = StationPullFilter.AllowedOreNames(s);
            lines.Add("  SAC allowed inputs: " + (allowed != null && allowed.Count > 0 ? string.Join(", ", allowed.ToArray()) : "(none)"));
            lines.Add("  SAC auto-store (N) on: " + CookingAutoDrop.IsOn(s) + ", link " + StationLink.Get(s));
        }
    }
}
