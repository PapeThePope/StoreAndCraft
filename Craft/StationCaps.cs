using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Admin station capacities (F10 / "6 - Stations"): kiln, smelter, blast furnace, eitr
    /// refinery (Smelter.m_maxOre / m_maxFuel) and beehive (m_maxHoney). 0 = vanilla. Vanilla
    /// values are remembered per prefab before the first change. Applied to hammer prefabs and
    /// every loaded instance only when a setting changes or a new ZNetScene appears — never polled.
    /// </summary>
    internal static class StationCaps
    {
        private struct Pair
        {
            public int Ore;
            public int Fuel;
        }

        private static readonly Dictionary<string, Pair> VanillaSmelter = new Dictionary<string, Pair>();
        private static readonly Dictionary<string, int> VanillaHoney = new Dictionary<string, int>();
        private static bool _dirty = true;
        private static ZNetScene _appliedScene;
        private static bool _logged;

        /// <summary>Any config change (local save or server sync) → apply once next frame.</summary>
        internal static void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>Plugin.Update: cheap unless dirty or the scene changed (world load).</summary>
        internal static void Tick()
        {
            if (Plugin.Settings == null || ZNetScene.instance == null)
                return;
            if (!_dirty && _appliedScene == ZNetScene.instance)
                return;
            _dirty = false;
            _appliedScene = ZNetScene.instance;
            Apply();
        }

        /// <summary>New stations: Awake after Apply already copies the patched prefab values.</summary>
        private static void Apply()
        {
            // Includes prefabs (hammer templates) and live instances.
            Smelter[] smelters = Resources.FindObjectsOfTypeAll<Smelter>();
            for (int i = 0; i < smelters.Length; i++)
            {
                Smelter s = smelters[i];
                if (s == null)
                    continue;
                string name = Utils.GetPrefabName(s.gameObject);
                ConfigEntry<float> ore;
                ConfigEntry<float> fuel;
                if (!Targets(name, out ore, out fuel))
                    continue;

                Pair vanilla;
                if (!VanillaSmelter.TryGetValue(name, out vanilla))
                {
                    vanilla = new Pair { Ore = s.m_maxOre, Fuel = s.m_maxFuel };
                    VanillaSmelter[name] = vanilla;
                }
                s.m_maxOre = Resolve(ore, vanilla.Ore);
                if (fuel != null)
                    s.m_maxFuel = Resolve(fuel, vanilla.Fuel);
            }

            Beehive[] hives = Resources.FindObjectsOfTypeAll<Beehive>();
            for (int i = 0; i < hives.Length; i++)
            {
                Beehive b = hives[i];
                if (b == null)
                    continue;
                string name = Utils.GetPrefabName(b.gameObject);
                // Only the bee hive (piece_birdnest is a Beehive component too).
                if (name != "piece_beehive")
                    continue;
                int vanilla;
                if (!VanillaHoney.TryGetValue(name, out vanilla))
                {
                    vanilla = b.m_maxHoney;
                    VanillaHoney[name] = vanilla;
                }
                b.m_maxHoney = Resolve(Plugin.Settings.BeehiveMaxHoney, vanilla);
            }

            if (!_logged && VanillaSmelter.Count > 0)
            {
                _logged = true;
                var parts = new List<string>();
                foreach (KeyValuePair<string, Pair> kv in VanillaSmelter)
                    parts.Add(kv.Key + " ore=" + kv.Value.Ore + " fuel=" + kv.Value.Fuel);
                foreach (KeyValuePair<string, int> kv in VanillaHoney)
                    parts.Add(kv.Key + " honey=" + kv.Value);
                Plugin.Log.LogDebug("Station caps vanilla: " + string.Join(", ", parts.ToArray()));
            }
        }

        /// <summary>Vanilla prefab names → their two settings (fuel null = no fuel slot).</summary>
        private static bool Targets(string prefab, out ConfigEntry<float> ore, out ConfigEntry<float> fuel)
        {
            ModConfig c = Plugin.Settings;
            ore = null;
            fuel = null;
            switch (prefab)
            {
                case "charcoal_kiln":
                    ore = c.KilnMaxWood;
                    return true;
                case "smelter":
                    ore = c.SmelterMaxOre;
                    fuel = c.SmelterMaxCoal;
                    return true;
                case "blastfurnace":
                    ore = c.BlastFurnaceMaxOre;
                    fuel = c.BlastFurnaceMaxCoal;
                    return true;
                case "eitrrefinery":
                    ore = c.EitrRefineryMaxInput;
                    fuel = c.EitrRefineryMaxSap;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Vanilla amount behind an F10 capacity key (captured on the first apply), or -1 while no
        /// world is loaded yet. F10 shows it as the slider's left end.
        /// </summary>
        internal static int Vanilla(string key)
        {
            Pair p;
            int honey;
            switch (key)
            {
                case "kilnmax":
                    return VanillaSmelter.TryGetValue("charcoal_kiln", out p) ? p.Ore : -1;
                case "smeltermaxore":
                    return VanillaSmelter.TryGetValue("smelter", out p) ? p.Ore : -1;
                case "smeltermaxfuel":
                    return VanillaSmelter.TryGetValue("smelter", out p) ? p.Fuel : -1;
                case "blastmaxore":
                    return VanillaSmelter.TryGetValue("blastfurnace", out p) ? p.Ore : -1;
                case "blastmaxfuel":
                    return VanillaSmelter.TryGetValue("blastfurnace", out p) ? p.Fuel : -1;
                case "eitrmaxore":
                    return VanillaSmelter.TryGetValue("eitrrefinery", out p) ? p.Ore : -1;
                case "eitrmaxfuel":
                    return VanillaSmelter.TryGetValue("eitrrefinery", out p) ? p.Fuel : -1;
                case "beehivemax":
                    return VanillaHoney.TryGetValue("piece_beehive", out honey) ? honey : -1;
                default:
                    return -1;
            }
        }

        private static int Resolve(ConfigEntry<float> entry, int vanilla)
        {
            int v = entry != null ? Mathf.RoundToInt(entry.Value) : 0;
            if (v <= 0)
                return vanilla;
            return Mathf.Clamp(v, 1, Mathf.RoundToInt(ModConfig.MaxStationCap));
        }
    }
}
