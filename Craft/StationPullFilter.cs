using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Per-station (ZDO) deny-list for Smelter ore slots and CookingStation food slots.
    /// Empty = allow all. Denied types cannot be chest-pulled, auto-filled, or manually
    /// inserted from the bag on that station.
    /// </summary>
    internal static class StationPullFilter
    {
        public const string ZdoKey = "SAC_stationDeny";
        public static readonly int ZdoKeyHash = ZdoKey.GetStableHashCode();

        public static Smelter HoveredSmelter()
        {
            return HoveredComponent<Smelter>();
        }

        public static CookingStation HoveredCooking()
        {
            return HoveredComponent<CookingStation>();
        }

        public static Fermenter HoveredFermenter()
        {
            return HoveredComponent<Fermenter>();
        }

        public static Fireplace HoveredFireplace()
        {
            return HoveredComponent<Fireplace>();
        }

        /// <summary>SAC scarecrow under the crosshair, or null.</summary>
        public static Scarecrow HoveredScarecrow()
        {
            return HoveredComponent<Scarecrow>();
        }

        /// <summary>Armor stand under the crosshair, or null.</summary>
        public static ArmorStand HoveredArmorStand()
        {
            return HoveredComponent<ArmorStand>();
        }

        /// <summary>SAC feed trough under the crosshair (its Container), or null.</summary>
        public static Container HoveredTrough()
        {
            FeedTrough trough = HoveredComponent<FeedTrough>();
            return trough != null ? trough.GetComponent<Container>() : null;
        }

        // Fermenter: same ZDO deny-list, choices = mead bases this fermenter converts.
        public static List<string> MeadChoices(Fermenter fermenter)
        {
            return FermenterInteractPatch.MeadNames(fermenter);
        }

        public static List<string> AllowedMeadNames(Fermenter fermenter)
        {
            return FilterAllowed(MeadChoices(fermenter), ReadDenied(View(fermenter)));
        }

        public static bool IsAllowed(Fermenter fermenter, string shared)
        {
            return !IsDenied(View(fermenter), shared);
        }

        public static void SetDenied(Fermenter fermenter, string shared, bool denied)
        {
            SetDenied(View(fermenter), shared, denied);
        }

        public static void Clear(Fermenter fermenter)
        {
            Clear(View(fermenter));
        }

        // Feed trough: same ZDO deny-list, choices = foods tameable animals eat.
        public static List<string> TroughFoodChoices()
        {
            return FeedTroughFoods.All();
        }

        public static List<string> AllowedTroughFoods(Container trough)
        {
            return FilterAllowed(TroughFoodChoices(), ReadDenied(View(trough)));
        }

        public static bool IsAllowed(Container trough, string shared)
        {
            return !IsDenied(View(trough), shared);
        }

        public static void SetDenied(Container trough, string shared, bool denied)
        {
            SetDenied(View(trough), shared, denied);
        }

        public static void Clear(Container trough)
        {
            Clear(View(trough));
        }

        private static T HoveredComponent<T>() where T : Component
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;
            GameObject hover = player.GetHoverObject();
            if (hover == null)
                return null;
            return hover.GetComponentInParent<T>();
        }

        public static bool CanConfigure(Smelter smelter)
        {
            return OreChoices(smelter).Count >= 2;
        }

        public static bool CanConfigure(CookingStation cook)
        {
            return FoodChoices(cook).Count >= 2;
        }

        public static List<string> OreChoices(Smelter smelter)
        {
            return SmelterAddOrePatch.OreNames(smelter);
        }

        public static List<string> FoodChoices(CookingStation cook)
        {
            return CookingOnInteractPatch.AllFoodNames(cook);
        }

        public static List<string> AllowedOreNames(Smelter smelter)
        {
            return FilterAllowed(OreChoices(smelter), ReadDenied(View(smelter)));
        }

        public static List<string> AllowedFoodNames(CookingStation cook)
        {
            return FilterAllowed(FoodChoices(cook), ReadDenied(View(cook)));
        }

        public static bool IsDenied(Smelter smelter, string shared)
        {
            return IsDenied(View(smelter), shared);
        }

        public static bool IsDenied(CookingStation cook, string shared)
        {
            return IsDenied(View(cook), shared);
        }

        public static bool IsAllowed(Smelter smelter, string shared)
        {
            return !IsDenied(smelter, shared);
        }

        public static bool IsAllowed(CookingStation cook, string shared)
        {
            return !IsDenied(cook, shared);
        }

        public static void SetDenied(Smelter smelter, string shared, bool denied)
        {
            SetDenied(View(smelter), shared, denied);
        }

        public static void SetDenied(CookingStation cook, string shared, bool denied)
        {
            SetDenied(View(cook), shared, denied);
        }

        public static void Clear(Smelter smelter)
        {
            Clear(View(smelter));
        }

        public static void Clear(CookingStation cook)
        {
            Clear(View(cook));
        }

        public static HashSet<string> ReadDenied(Smelter smelter)
        {
            return ReadDenied(View(smelter));
        }

        public static HashSet<string> ReadDenied(CookingStation cook)
        {
            return ReadDenied(View(cook));
        }

        public static string DisplayName(string shared)
        {
            if (string.IsNullOrEmpty(shared))
                return "?";
            return Localization.instance != null
                ? Localization.instance.Localize(shared)
                : shared;
        }

        public static string PromptLabel()
        {
            return ChestRename.PromptLabel();
        }

        public static void AppendFilterHover(ref string text, Smelter smelter)
        {
            AppendFilterHover(ref text, smelter, prependLink: true);
        }

        public static void AppendFilterHover(ref string text, Smelter smelter, bool prependLink)
        {
            if (smelter == null)
                return;
            AppendFilterLine(ref text);
            if (prependLink)
                StationLink.PrependHover(ref text, StationLink.Get(smelter), chest: false);
        }

        public static void AppendFilterHover(ref string text, CookingStation cook)
        {
            if (cook == null)
                return;
            AppendFilterLine(ref text);
        }

        public static void AppendFilterHover(ref string text, Fermenter fermenter)
        {
            if (fermenter == null)
                return;
            AppendFilterLine(ref text);
        }

        private static void AppendFilterLine(ref string text)
        {
            string key = PromptLabel();
            text += "\n[<color=yellow><b>" + key + "</b></color>] "
                + Loc.T("Settings", "Einstellungen");
        }

        public static bool TryOpen(bool warnIfMissing = true)
        {
            Player player = Player.m_localPlayer;
            if (player == null || !StationFeed.Ready())
                return false;

            Smelter smelter = HoveredSmelter();
            if (smelter != null)
            {
                if (!PrivateArea.CheckAccess(smelter.transform.position, 0f, false, true))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                    return true;
                }
                StationFilterMenu.Open(smelter);
                return true;
            }

            CookingStation cook = HoveredCooking();
            if (cook != null)
            {
                if (!PrivateArea.CheckAccess(cook.transform.position, 0f, false, true))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                    return true;
                }
                StationFilterMenu.Open(cook);
                return true;
            }

            Fermenter fermenter = HoveredFermenter();
            if (fermenter != null)
            {
                if (!PrivateArea.CheckAccess(fermenter.transform.position, 0f, false, true))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                    return true;
                }
                StationFilterMenu.Open(fermenter);
                return true;
            }

            // Scarecrow: crop, grid, link, harvest / plant switches.
            Scarecrow scarecrow = HoveredScarecrow();
            if (scarecrow != null)
            {
                if (!PrivateArea.CheckAccess(scarecrow.transform.position, 0f, false, true))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                    return true;
                }
                StationFilterMenu.Open(scarecrow);
                return true;
            }

            // Armor stand: presets for the weapon / shield / tool slots (only while the swap is on).
            ArmorStand stand = ArmorStandSwap.Enabled ? HoveredArmorStand() : null;
            if (stand != null)
            {
                if (!PrivateArea.CheckAccess(stand.transform.position, 0f, false, true))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                    return true;
                }
                StationFilterMenu.Open(stand);
                return true;
            }

            // Feed trough: food filter + link + auto-fill instead of chest settings.
            Container trough = HoveredTrough();
            if (trough != null)
            {
                if (!PrivateArea.CheckAccess(trough.transform.position, 0f, false, true))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                    return true;
                }
                StationFilterMenu.Open(trough);
                return true;
            }

            // Fires and torches have no settings; swallow the key so it does not fall through to chest rename.
            if (HoveredFireplace() != null)
                return true;

            if (warnIfMissing)
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    Loc.T(
                        "Look at a kiln / smelter / cook / fermenter, then press " + PromptLabel() + ".",
                        "Schau Ofen / Schmelze / Grill / Fass an, dann " + PromptLabel() + "."),
                    0, null, false);
            }
            return false;
        }

        private static ZNetView View(Component c)
        {
            return c != null ? c.GetComponent<ZNetView>() : null;
        }

        private static List<string> FilterAllowed(List<string> all, HashSet<string> denied)
        {
            if (all == null || all.Count == 0)
                return all ?? new List<string>();
            if (denied == null || denied.Count == 0)
                return all;

            var allowed = new List<string>();
            foreach (string shared in all)
            {
                if (!denied.Contains(shared))
                    allowed.Add(shared);
            }
            return allowed;
        }

        private static bool IsDenied(ZNetView nv, string shared)
        {
            if (nv == null || string.IsNullOrEmpty(shared))
                return false;
            return ReadDenied(nv).Contains(shared);
        }

        private static void SetDenied(ZNetView nv, string shared, bool denied)
        {
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null || string.IsNullOrEmpty(shared))
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();

            HashSet<string> set = ReadDenied(nv);
            if (denied)
                set.Add(shared);
            else
                set.Remove(shared);

            nv.GetZDO().Set(ZdoKeyHash, Encode(set));
        }

        private static void Clear(ZNetView nv)
        {
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            nv.GetZDO().Set(ZdoKeyHash, "");
        }

        private static HashSet<string> ReadDenied(ZNetView nv)
        {
            var set = new HashSet<string>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return set;

            string raw = zdo.GetString(ZdoKeyHash, "");
            if (string.IsNullOrEmpty(raw))
                return set;

            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (!string.IsNullOrEmpty(p))
                    set.Add(p);
            }
            return set;
        }

        private static string Encode(HashSet<string> set)
        {
            if (set == null || set.Count == 0)
                return "";
            var sb = new StringBuilder();
            foreach (string s in set)
            {
                if (string.IsNullOrEmpty(s))
                    continue;
                if (sb.Length > 0)
                    sb.Append('|');
                sb.Append(s);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Alt+E (RenameKey) opens the chest-pull filter via Hotkeys, but vanilla still
    /// fires Switch.Interact on E and inserts wood. Block that while the chord modifiers are held.
    /// </summary>
    [HarmonyPatch(typeof(Switch), nameof(Switch.Interact))]
    internal static class SwitchInteractBlockFilterChordPatch
    {
        private static bool Prefix(Switch __instance, Humanoid character, bool hold)
        {
            if (hold || character != Player.m_localPlayer || __instance == null)
                return true;
            if (!ChestRename.BlocksStationUse())
                return true;

            if (__instance.GetComponentInParent<Smelter>() != null)
                return false;
            if (__instance.GetComponentInParent<CookingStation>() != null)
                return false;

            return true;
        }
    }
}
