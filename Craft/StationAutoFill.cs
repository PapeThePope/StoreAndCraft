using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Per-station auto-fill for every input station we can feed: kiln, smelter,
    /// blast furnace, eitr, windmill, Frost Foundry, cooking / stone oven, fermenter,
    /// torches / fires, Mistlands ballistas (Turret), and food serving trays (ItemStand).
    /// Hover the station with the inventory closed and press B to toggle.
    /// Inventory-open F stays favorites. Chest pull filters still apply.
    /// </summary>
    internal static class StationAutoFill
    {
        public const string ZdoKey = "SAC_autoFill";
        private const float Interval = 2.5f;
        private const float QuietEmptySeconds = 25f;
        private const int MaxBurstsPerPulse = 4;
        /// <summary>Cap how many needy stations we evaluate per pulse (after range/IsOn filters).</summary>
        private const int MaxChecksPerPulse = 12;

        internal static int Silence;

        private static readonly List<Smelter> Smelters = new List<Smelter>();
        private static readonly HashSet<int> SmelterIds = new HashSet<int>();
        private static readonly List<Fireplace> Fires = new List<Fireplace>();
        private static readonly HashSet<int> FireIds = new HashSet<int>();
        private static readonly List<CookingStation> Ovens = new List<CookingStation>();
        private static readonly HashSet<int> OvenIds = new HashSet<int>();
        private static readonly List<Fermenter> Fermenters = new List<Fermenter>();
        private static readonly HashSet<int> FermenterIds = new HashSet<int>();
        private static readonly List<Turret> Turrets = new List<Turret>();
        private static readonly HashSet<int> TurretIds = new HashSet<int>();
        private static readonly List<ItemStand> FoodTrays = new List<ItemStand>();
        // SAC feed troughs (their Container), refreshed from FeedTrough.Live each pulse.
        private static readonly List<Container> Troughs = new List<Container>();
        private const int TroughTarget = 50;
        private static readonly HashSet<int> FoodTrayIds = new HashSet<int>();
        private static readonly Dictionary<string, float> QuietUntil = new Dictionary<string, float>();
        // Non-owner handover time per station (instance id). The ZDO owner fills first; others only
        // take over when the owner has not refilled in time (owner out of range / no auto-fill).
        private static readonly Dictionary<int, float> NeedyUntil = new Dictionary<int, float>();
        private const float HandoverSeconds = 6f;
        private const float HandoverJitterSeconds = 4f;

        private static readonly MethodInfo SmelterGetFuel = AccessTools.Method(typeof(Smelter), "GetFuel");
        private static readonly MethodInfo SmelterSetFuel = AccessTools.Method(typeof(Smelter), "SetFuel");
        private static readonly MethodInfo SmelterGetQueue = AccessTools.Method(typeof(Smelter), "GetQueueSize");
        private static readonly MethodInfo CookGetFuel = AccessTools.Method(typeof(CookingStation), "GetFuel");
        private static readonly MethodInfo CookIsFull = AccessTools.Method(typeof(CookingStation), "IsStationFull");
        private static readonly MethodInfo FermenterGetStatus = AccessTools.Method(typeof(Fermenter), "GetStatus");
        private static readonly MethodInfo TurretGetAmmo = AccessTools.Method(typeof(Turret), "GetAmmo");
        private static readonly MethodInfo TurretGetAmmoType = AccessTools.Method(typeof(Turret), "GetAmmoType");
        private static readonly MethodInfo ItemStandHaveAttachment = AccessTools.Method(typeof(ItemStand), "HaveAttachment");
        private static readonly int FuelHash = ReadFuelHash();

        private static float _nextPulse;
        private static float _nextPrune;
        private static int _stationCursor;
        private static bool _inPulse;
        private static readonly Dictionary<int, float> ChestWakeUntil = new Dictionary<int, float>();
        private const float ChestWakeCooldown = 1f;
        private static readonly List<Component> AllStations = new List<Component>(64);

        public static void Register(Smelter smelter)
        {
            if (smelter == null)
                return;
            int id = smelter.GetInstanceID();
            if (!SmelterIds.Add(id))
                return;
            Smelters.Add(smelter);
        }

        public static void Register(Fireplace fire)
        {
            if (fire == null)
                return;
            int id = fire.GetInstanceID();
            if (!FireIds.Add(id))
                return;
            Fires.Add(fire);
        }

        public static void Register(CookingStation oven)
        {
            if (oven == null)
                return;
            int id = oven.GetInstanceID();
            if (!OvenIds.Add(id))
                return;
            Ovens.Add(oven);
        }

        public static void Register(Fermenter fermenter)
        {
            if (fermenter == null)
                return;
            int id = fermenter.GetInstanceID();
            if (!FermenterIds.Add(id))
                return;
            Fermenters.Add(fermenter);
        }

        public static void Register(Turret turret)
        {
            if (turret == null)
                return;
            int id = turret.GetInstanceID();
            if (!TurretIds.Add(id))
                return;
            Turrets.Add(turret);
        }

        public static void Register(ItemStand stand)
        {
            if (stand == null || !IsFoodServingTray(stand))
                return;
            int id = stand.GetInstanceID();
            if (!FoodTrayIds.Add(id))
                return;
            FoodTrays.Add(stand);
        }

        internal static IReadOnlyList<Smelter> RegisteredSmelters { get { return Smelters; } }
        internal static IReadOnlyList<Fireplace> RegisteredFires { get { return Fires; } }
        internal static IReadOnlyList<CookingStation> RegisteredOvens { get { return Ovens; } }
        internal static IReadOnlyList<Fermenter> RegisteredFermenters { get { return Fermenters; } }

        /// <summary>Serving trays accept consumable food; weapon stands / boss trophies do not.</summary>
        internal static bool IsFoodServingTray(ItemStand stand)
        {
            if (stand == null)
                return false;
            if (stand.m_guardianPower != null)
                return false;

            if (stand.m_supportedTypes != null)
            {
                for (int i = 0; i < stand.m_supportedTypes.Count; i++)
                {
                    if (stand.m_supportedTypes[i] == ItemDrop.ItemData.ItemType.Consumable)
                        return true;
                }
            }

            if (stand.m_supportedItems != null)
            {
                for (int i = 0; i < stand.m_supportedItems.Count; i++)
                {
                    ItemDrop drop = stand.m_supportedItems[i];
                    if (drop?.m_itemData?.m_shared != null
                        && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                        return true;
                }
            }

            return false;
        }

        public static void BootstrapExisting()
        {
            Smelter[] smelters = Resources.FindObjectsOfTypeAll<Smelter>();
            if (smelters != null)
            {
                foreach (Smelter s in smelters)
                {
                    if (s == null || !s.gameObject.scene.IsValid())
                        continue;
                    Register(s);
                }
            }

            Fireplace[] fires = Resources.FindObjectsOfTypeAll<Fireplace>();
            if (fires != null)
            {
                foreach (Fireplace f in fires)
                {
                    if (f == null || !f.gameObject.scene.IsValid())
                        continue;
                    Register(f);
                }
            }

            CookingStation[] ovens = Resources.FindObjectsOfTypeAll<CookingStation>();
            if (ovens != null)
            {
                foreach (CookingStation oven in ovens)
                {
                    if (oven == null || !oven.gameObject.scene.IsValid())
                        continue;
                    Register(oven);
                }
            }

            Fermenter[] fermenters = Resources.FindObjectsOfTypeAll<Fermenter>();
            if (fermenters != null)
            {
                foreach (Fermenter fermenter in fermenters)
                {
                    if (fermenter == null || !fermenter.gameObject.scene.IsValid())
                        continue;
                    Register(fermenter);
                }
            }

            Turret[] turrets = Resources.FindObjectsOfTypeAll<Turret>();
            if (turrets != null)
            {
                foreach (Turret turret in turrets)
                {
                    if (turret == null || !turret.gameObject.scene.IsValid())
                        continue;
                    Register(turret);
                }
            }

            ItemStand[] stands = Resources.FindObjectsOfTypeAll<ItemStand>();
            if (stands != null)
            {
                foreach (ItemStand stand in stands)
                {
                    if (stand == null || !stand.gameObject.scene.IsValid())
                        continue;
                    Register(stand);
                }
            }
        }

        public static bool IsOn(ZNetView nv)
        {
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return false;
            // World Override (torches only) sits on top of the stored flag and never writes it.
            if (OverrideActive(nv))
                return true;
            return StoredOn(nv);
        }

        /// <summary>The station's own flag (B / settings switch / torch default at placement). -1 = off.</summary>
        internal static bool StoredOn(ZNetView nv)
        {
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return false;
            return nv.GetZDO().GetInt(ZdoKey, -1) > 0;
        }

        /// <summary>World Override: auto-fill is forced on for this torch whatever its own flag says.</summary>
        internal static bool OverrideActive(ZNetView nv)
        {
            return Plugin.Settings != null && Plugin.Settings.TorchAutoFillOverride.Value
                && nv != null && IsTorch(nv);
        }

        /// <summary>New torch: store the TorchAutoFillDefault once (existing torches keep their flag).</summary>
        internal static void ApplyTorchDefault(Piece piece)
        {
            ZNetView nv = piece != null ? piece.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null || !nv.IsOwner())
                return;
            if (Plugin.Settings == null || !IsTorch(nv) || nv.GetZDO().GetInt(ZdoKey, -1) >= 0)
                return;
            nv.GetZDO().Set(ZdoKey, Plugin.Settings.TorchAutoFillDefault.Value ? 1 : 0);
        }

        private static readonly Dictionary<int, bool> TorchCache = new Dictionary<int, bool>();

        /// <summary>
        /// Refillable torch of any kind (standing, wall, green/blue/mist, modded "*torch*").
        /// Campfires, hearths, braziers etc. keep the manual B toggle.
        /// </summary>
        private static bool IsTorch(ZNetView nv)
        {
            int id = nv.GetInstanceID();
            bool torch;
            if (TorchCache.TryGetValue(id, out torch))
                return torch;

            Fireplace fire = nv.GetComponent<Fireplace>();
            torch = fire != null && fire.m_canRefill && !fire.m_infiniteFuel
                && ItemIds.StripClone(fire.gameObject.name)
                    .IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0;
            if (TorchCache.Count > 2048)
                TorchCache.Clear();
            TorchCache[id] = torch;
            return torch;
        }

        public static bool IsOn(Smelter smelter)
        {
            return smelter != null && IsOn(smelter.GetComponent<ZNetView>());
        }

        public static bool IsOn(Fireplace fire)
        {
            return fire != null && IsOn(fire.GetComponent<ZNetView>());
        }

        public static void AppendHover(ref string text, ZNetView nv)
        {
            AppendHover(ref text, nv, includeManualFill: true);
        }

        public static void AppendHover(ref string text, ZNetView nv, bool includeManualFill)
        {
            if (!StationFeed.Ready())
                return;
            if (nv == null || !nv.IsValid())
                return;

            if (string.IsNullOrEmpty(text))
                text = "";

            // Chord keys first, then single keys (same order everywhere).
            if (includeManualFill)
                AppendFillToMaxLine(ref text);
            AppendAutoFillLine(ref text, nv);
        }

        /// <summary>
        /// After vanilla hover: Alt+E filter → Shift+E fill to max → B auto-fill → N auto-store.
        /// Link / remote are status (link stays on top).
        /// </summary>
        public static void AppendSmelterHover(ref string text, Smelter smelter)
        {
            if (smelter == null || !StationFeed.Ready())
                return;

            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return;

            if (string.IsNullOrEmpty(text))
                text = "";

            StationPullFilter.AppendFilterHover(ref text, smelter, prependLink: false);
            AppendFillToMaxLine(ref text);
            AppendAutoFillLine(ref text, nv);
            CookingAutoDrop.AppendHover(ref text, smelter);
            SmelterCatchUp.AppendHover(ref text, smelter); // SAC-CATCHUP
            StationLink.PrependHover(ref text, StationLink.Get(smelter), chest: false);
        }

        private static void AppendFillToMaxLine(ref string text)
        {
            text += "\n[<color=yellow><b>" + Loc.T("Shift+E", "Umschalt+E") + "</b></color>] "
                + Loc.T("Fill to max", "Auffüllen bis voll");
        }

        private static void AppendAutoFillLine(ref string text, ZNetView nv)
        {
            string key = KeyUtil.Format(Plugin.Settings.AutoFillKey.Value);
            if (string.IsNullOrEmpty(key))
                key = "B";

            // World Override: forced on, B does nothing, so no key prompt.
            if (OverrideActive(nv))
            {
                text += "\n" + Loc.T("Auto-fill", "Auto-Fill") + " (" + Loc.T("World Override", "Welt-Override") + ")";
                return;
            }

            text += "\n[<color=yellow><b>" + key + "</b></color>] "
                + Loc.T("Auto-fill", "Auto-Fill")
                + " (" + (IsOn(nv) ? Loc.T("on", "an") : Loc.T("off", "aus")) + ")";
        }

        /// <summary>
        /// After vanilla [E] / [1-8]: Alt+E → Shift+E → B → N. Link on top.
        /// </summary>
        public static void AppendCookingHover(ref string text, CookingStation station)
        {
            if (station == null)
                return;

            StationPullFilter.AppendFilterHover(ref text, station);
            AppendHover(ref text, station.GetComponent<ZNetView>());
            CookingAutoDrop.AppendHover(ref text, station);
            StationLink.PrependHover(ref text, StationLink.Get(station), chest: false);
        }

        /// <summary>
        /// Fireplace / fermenter: Alt+E (if filter) → Shift+E → B. Same chord→single order.
        /// </summary>
        public static void AppendFuelStationHover(ref string text, Component station, ZNetView nv)
        {
            if (station == null || nv == null || !nv.IsValid() || !StationFeed.Ready())
                return;

            if (station is Fermenter fermenter)
                StationPullFilter.AppendFilterHover(ref text, fermenter);

            AppendHover(ref text, nv);
        }

        public static bool TryToggle()
        {
            if (InventoryGui.IsVisible() || StationFilterMenu.IsOpen || DisplayTypeMenu.IsOpen || DisplayRangeMenu.IsOpen)
                return false;
            if (!StationFeed.Ready())
                return false;

            Player player = Player.m_localPlayer;
            if (player == null)
                return false;

            Smelter smelter = StationPullFilter.HoveredSmelter();
            CookingStation oven = smelter == null ? HoveredOven() : null;
            Fermenter fermenter = smelter == null && oven == null ? HoveredFermenter() : null;
            Fireplace fire = smelter == null && oven == null && fermenter == null ? HoveredFireplace() : null;
            Turret turret = smelter == null && oven == null && fermenter == null && fire == null
                ? HoveredTurret() : null;
            ItemStand tray = smelter == null && oven == null && fermenter == null && fire == null && turret == null
                ? HoveredFoodTray() : null;

            Container trough = smelter == null && oven == null && fermenter == null && fire == null
                && turret == null && tray == null ? StationPullFilter.HoveredTrough() : null;

            Scarecrow scarecrow = smelter == null && oven == null && fermenter == null && fire == null
                && turret == null && tray == null && trough == null ? StationPullFilter.HoveredScarecrow() : null;

            Component station = (Component)smelter ?? oven ?? (Component)fermenter ?? fire
                ?? (Component)turret ?? (Component)tray ?? (Component)trough ?? scarecrow;
            ZNetView nv = station != null ? station.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return false;

            Vector3 pos = station.transform.position;
            if (!PrivateArea.CheckAccess(pos, 0f, false, true))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone", 0, null, false);
                return true;
            }

            // World Override forces auto-fill on: B must not change the torch.
            if (OverrideActive(nv))
            {
                player.Message(MessageHud.MessageType.Center,
                    Loc.T("World Override is on: auto-fill is forced on", "Welt-Override ist an: Auto-Fill ist erzwungen"),
                    0, null, false);
                return true;
            }

            if (!nv.IsOwner())
                nv.ClaimOwnership();

            bool next = !StoredOn(nv);
            nv.GetZDO().Set(ZdoKey, next ? 1 : 0);
            ClearQuiet(smelter);
            ClearQuiet(oven);
            ClearQuiet(fermenter);
            ClearQuiet(fire);
            ClearQuiet(turret);
            ClearQuiet(tray);
            ClearQuiet(trough);
            if (scarecrow != null)
                scarecrow.MarkDirty();

            player.Message(
                MessageHud.MessageType.Center,
                next
                    ? Loc.T("Auto-fill on", "Auto-Fill an")
                    : Loc.T("Auto-fill off", "Auto-Fill aus"),
                0, null, false);
            ActivityLog.Note(StationOutput.StationLabel(station),
                next ? Loc.T("Auto-fill on", "Auto-Fill an") : Loc.T("Auto-fill off", "Auto-Fill aus"));
            return true;
        }

        /// <summary>Nudge the next auto-fill pulse sooner (vanilla Update* saw a needy station).</summary>
        public static void RequestSoon()
        {
            float soon = Time.unscaledTime + 0.35f;
            if (_nextPulse > soon)
                _nextPulse = soon;
        }

        /// <summary>
        /// Called from the vanilla 1 s station updates (every loaded station, every client).
        /// Only pull the pulse forward when this client could fill that station right now:
        /// in auto-fill range of the local player and actually missing fuel/ore/food/ammo.
        /// An unconditional nudge made every client run a full pulse about once per second.
        /// </summary>
        internal static void NudgeIfNeedy(Component station)
        {
            if (station == null || Plugin.Settings == null)
                return;
            Player player = Player.m_localPlayer;
            if (player == null)
                return;
            float range = Plugin.Settings.AutoFillRange.Value;
            if (ContainerFilter.SqrDistance(player.transform.position, station.transform.position) > range * range)
                return;
            if (!StationNeedsFill(station))
                return;
            RequestSoon();
        }

        public static void Tick()
        {
            if (!StationFeed.Ready())
                return;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead())
                return;

            if (Time.unscaledTime < _nextPulse)
                return;
            _nextPulse = Time.unscaledTime + Interval;
            StationMissingLabel.Validate();

            // Nested Push/Pop can leave ActiveId stuck if a path returns early.
            StationLink.ResetFrame();

            if (Time.unscaledTime >= _nextPrune)
            {
                PruneDead();
                _nextPrune = Time.unscaledTime + 10f;
            }

            float range = Plugin.Settings.AutoFillRange.Value;
            float rangeSq = range * range;
            Vector3 origin = player.transform.position;
            RefreshTroughs();

            // No station with auto-fill ON in range → no chest snapshot / fill work.
            if (!AnyOnInRange(origin, rangeSq))
                return;

            // Stations are on but none needs anything and there is no catch-up credit:
            // skip the chest work. RoundRobinFill would drop every handover timer here anyway.
            if (!AnyNeedyInRange(origin, rangeSq)
                && !SmelterCatchUp.HasWorkInRange(Smelters, origin, rangeSq))
            {
                NeedyUntil.Clear();
                return;
            }

            var stationOrigins = new List<Vector3>(16);
            CollectOnStationOrigins(origin, rangeSq, stationOrigins);

            int bursts = 0;
            int checks = 0;

            float chestRange = Plugin.Settings.AutoFillChestReach();
            StationFeed.PullRangeOverride = chestRange;
            _inPulse = true;
            try
            {
                StationFeed.BeginAutoFillPulse(player, chestRange, stationOrigins);
                // One shared ring over every station type: no type is preferred, the cursor
                // continues where the last pulse stopped. Only stations that need something
                // take a check slot.
                AllStations.Clear();
                AddStations(Fires);
                AddStations(Smelters);
                AddStations(Ovens);
                AddStations(Fermenters);
                AddStations(Turrets);
                AddStations(FoodTrays);
                AddStations(Troughs);
                RoundRobinFill(
                    AllStations, ref _stationCursor, origin, rangeSq, player, ref bursts, checks,
                    StationNeedsFill, FillStation);
                AllStations.Clear();
                SmelterCatchUp.RunPulse(Smelters, origin, rangeSq, player); // SAC-CATCHUP
            }
            finally
            {
                StationFeed.EndAutoFillPulse();
                StationFeed.PullRangeOverride = 0f;
                StationFeed.PullOriginOverride = null;
                _inPulse = false;
            }
        }

        private static bool AnyNeedyInRange(Vector3 origin, float rangeSq)
        {
            return AnyNeedy(Fires, origin, rangeSq)
                || AnyNeedy(Smelters, origin, rangeSq)
                || AnyNeedy(Ovens, origin, rangeSq)
                || AnyNeedy(Fermenters, origin, rangeSq)
                || AnyNeedy(Turrets, origin, rangeSq)
                || AnyNeedy(FoodTrays, origin, rangeSq)
                || AnyNeedy(Troughs, origin, rangeSq);
        }

        private static bool AnyNeedy<T>(List<T> list, Vector3 origin, float rangeSq) where T : Component
        {
            for (int i = 0; i < list.Count; i++)
            {
                T station = list[i];
                if (station == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, station.transform.position) > rangeSq)
                    continue;
                if (StationNeedsFill(station))
                    return true;
            }
            return false;
        }

        private static void AddStations<T>(List<T> list) where T : Component
        {
            for (int i = 0; i < list.Count; i++)
                AllStations.Add(list[i]);
        }

        private static bool StationNeedsFill(Component station)
        {
            if (station == null)
                return false;

            bool needs;
            if (station is Fireplace f)
                needs = f.m_canRefill && !IsQuiet(f, "fire") && IsOn(f) && ReadFireFuel(f) <= 0.01f;
            else if (station is Smelter s)
                needs = IsOn(s) && (SmelterNeedsFuel(s) || SmelterNeedsOre(s));
            else if (station is CookingStation o)
                needs = IsOn(o.GetComponent<ZNetView>()) && (OvenNeedsFuel(o) || OvenNeedsFood(o));
            else if (station is Fermenter m)
                needs = !IsQuiet(m, "mead") && IsOn(m.GetComponent<ZNetView>())
                    && ReadEnumInt(FermenterGetStatus, m) == 0;
            else if (station is Turret t)
                needs = !IsQuiet(t, "ammo") && IsOn(t.GetComponent<ZNetView>())
                    && t.m_maxAmmo > 0
                    && Mathf.RoundToInt(ReadNumber(TurretGetAmmo, t)) < t.m_maxAmmo;
            else if (station is ItemStand tray)
                needs = !IsQuiet(tray, "tray") && IsOn(tray.GetComponent<ZNetView>())
                    && !IsTrue(ItemStandHaveAttachment, tray);
            else if (station is Container trough)
                needs = !IsQuiet(trough, "feed") && IsOn(trough.GetComponent<ZNetView>())
                    && !trough.IsInUse() && TroughEmpty(trough);
            else
                return false;

            return needs && PrivateArea.CheckAccess(station.transform.position, 0f, false, true);
        }

        private static bool FillStation(Component station, Player player)
        {
            if (station is Fireplace f)
                return FillFireWhenEmpty(f, player);
            if (station is Smelter s)
                return FillSmelterWhenEmpty(s, player);
            if (station is CookingStation o)
                return FillOvenWhenEmpty(o, player);
            if (station is Fermenter m)
                return FillFermenterWhenEmpty(m, player);
            if (station is Turret t)
                return FillTurretWhenEmpty(t, player);
            if (station is ItemStand tray)
                return FillFoodTrayWhenEmpty(tray, player);
            if (station is Container trough)
                return FillTroughWhenLow(trough, player);
            return false;
        }

        private static void CollectOnStationOrigins(Vector3 playerOrigin, float rangeSq, List<Vector3> dest)
        {
            if (dest == null)
                return;
            AppendOrigins(Smelters, playerOrigin, rangeSq, dest, s => IsOn(s));
            AppendOrigins(Fires, playerOrigin, rangeSq, dest, f => f != null && f.m_canRefill && IsOn(f));
            AppendOrigins(Ovens, playerOrigin, rangeSq, dest, o => IsOn(o.GetComponent<ZNetView>()));
            AppendOrigins(Fermenters, playerOrigin, rangeSq, dest, f => IsOn(f.GetComponent<ZNetView>()));
            AppendOrigins(Turrets, playerOrigin, rangeSq, dest, t => IsOn(t.GetComponent<ZNetView>()));
            AppendOrigins(FoodTrays, playerOrigin, rangeSq, dest, s => IsOn(s.GetComponent<ZNetView>()));
            AppendOrigins(Troughs, playerOrigin, rangeSq, dest, c => IsOn(c.GetComponent<ZNetView>()));
        }

        private static void AppendOrigins<T>(
            List<T> list,
            Vector3 playerOrigin,
            float rangeSq,
            List<Vector3> dest,
            System.Func<T, bool> on) where T : Component
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                T t = list[i];
                if (t == null || !on(t))
                    continue;
                if (ContainerFilter.SqrDistance(playerOrigin, t.transform.position) > rangeSq)
                    continue;
                dest.Add(t.transform.position);
            }
        }

        private static bool AnyOnInRange(Vector3 origin, float rangeSq)
        {
            for (int i = 0; i < Smelters.Count; i++)
            {
                Smelter s = Smelters[i];
                if (s == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, s.transform.position) > rangeSq)
                    continue;
                if (IsOn(s))
                    return true;
            }

            for (int i = 0; i < Fires.Count; i++)
            {
                Fireplace f = Fires[i];
                if (f == null || !f.m_canRefill)
                    continue;
                if (ContainerFilter.SqrDistance(origin, f.transform.position) > rangeSq)
                    continue;
                if (IsOn(f))
                    return true;
            }

            for (int i = 0; i < Ovens.Count; i++)
            {
                CookingStation o = Ovens[i];
                if (o == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, o.transform.position) > rangeSq)
                    continue;
                if (IsOn(o.GetComponent<ZNetView>()))
                    return true;
            }

            for (int i = 0; i < Fermenters.Count; i++)
            {
                Fermenter f = Fermenters[i];
                if (f == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, f.transform.position) > rangeSq)
                    continue;
                if (IsOn(f.GetComponent<ZNetView>()))
                    return true;
            }

            for (int i = 0; i < Turrets.Count; i++)
            {
                Turret t = Turrets[i];
                if (t == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, t.transform.position) > rangeSq)
                    continue;
                if (IsOn(t.GetComponent<ZNetView>()))
                    return true;
            }

            for (int i = 0; i < FoodTrays.Count; i++)
            {
                ItemStand s = FoodTrays[i];
                if (s == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, s.transform.position) > rangeSq)
                    continue;
                if (IsOn(s.GetComponent<ZNetView>()))
                    return true;
            }

            for (int i = 0; i < Troughs.Count; i++)
            {
                Container t = Troughs[i];
                if (t == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, t.transform.position) > rangeSq)
                    continue;
                if (IsOn(t.GetComponent<ZNetView>()))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Walk the list from a rotating cursor. Only ON / candidate stations consume the check budget.
        /// </summary>
        private static int RoundRobinFill<T>(
            List<T> list,
            ref int cursor,
            Vector3 origin,
            float rangeSq,
            Player player,
            ref int bursts,
            int checks,
            System.Func<T, bool> isCandidate,
            System.Func<T, Player, bool> tryFill) where T : Component
        {
            if (list == null || list.Count == 0 || isCandidate == null || tryFill == null)
                return checks;

            int start = cursor % list.Count;
            for (int n = 0; n < list.Count; n++)
            {
                if (bursts >= MaxBurstsPerPulse || checks >= MaxChecksPerPulse)
                    break;

                int i = (start + n) % list.Count;
                T station = list[i];
                if (station == null)
                    continue;
                if (ContainerFilter.SqrDistance(origin, station.transform.position) > rangeSq
                    || !isCandidate(station))
                {
                    NeedyUntil.Remove(station.GetInstanceID());
                    continue;
                }
                if (!MayFillHere(station))
                    continue;

                cursor = i + 1;
                checks++;
                StationLink.PushStation(station);
                StationFeed.PullOriginOverride = station.transform.position;
                try
                {
                    if (tryFill(station, player))
                        bursts++;
                }
                finally
                {
                    StationFeed.PullOriginOverride = null;
                    StationLink.Pop();
                }
            }

            return checks;
        }

        /// <summary>
        /// Fill paths ClaimOwnership and run the add RPC locally. Two clients doing that on the
        /// same empty station within sync latency both spend chest items, and one ZDO write loses.
        /// </summary>
        private static bool MayFillHere(Component station)
        {
            ZNetView nv = station != null ? station.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid())
                return false;

            int id = station.GetInstanceID();
            if (nv.IsOwner())
            {
                NeedyUntil.Remove(id);
                return true;
            }

            float now = Time.unscaledTime;
            float until;
            if (!NeedyUntil.TryGetValue(id, out until))
            {
                NeedyUntil[id] = now + HandoverSeconds + UnityEngine.Random.Range(0f, HandoverJitterSeconds);
                return false;
            }
            if (now < until)
                return false;

            NeedyUntil.Remove(id);
            return true;
        }

        /// <summary>
        /// Manual Shift+[E] fill: top up fuel and/or ore from bag/chests without waiting for auto-fill.
        /// </summary>
        internal static bool ManualFillSmelterToMax(Smelter smelter, Player player, bool fuel, bool ore)
        {
            if (smelter == null || player == null)
                return false;
            StationLink.PushStation(smelter);
            StationFeed.PullRangeOverride = Plugin.Settings != null ? Plugin.Settings.CraftRange.Value : 0f;
            try
            {
                bool did = false;
                if (fuel && HasFuelSlot(smelter))
                    did |= FillFuelToMax(smelter, player);
                if (ore && HasOreSlot(smelter))
                    did |= FillOreToMax(smelter, player);
                return did;
            }
            finally
            {
                StationFeed.PullRangeOverride = 0f;
                StationLink.Pop();
            }
        }

        private static bool FillSmelterWhenEmpty(Smelter smelter, Player player)
        {
            bool did = false;
            if (SmelterNeedsFuel(smelter))
                did |= FillFuelToMax(smelter, player);
            if (SmelterNeedsOre(smelter))
                did |= FillOreToMax(smelter, player);
            return did;
        }

        private static bool SmelterNeedsFuel(Smelter smelter)
        {
            return HasFuelSlot(smelter) && !IsQuiet(smelter, "fuel") && ReadNumber(SmelterGetFuel, smelter) <= 0.01f;
        }

        private static bool SmelterNeedsOre(Smelter smelter)
        {
            return HasOreSlot(smelter) && !IsQuiet(smelter, "ore") && ReadNumber(SmelterGetQueue, smelter) < 1f;
        }

        private static bool HasFuelSlot(Smelter smelter)
        {
            // Fuel works via OnAddFuel or RPC_AddFuel; Switch refs are optional.
            return smelter != null && smelter.m_fuelItem != null && smelter.m_maxFuel > 0;
        }

        private static bool HasOreSlot(Smelter smelter)
        {
            return smelter != null && smelter.m_maxOre > 0
                && smelter.m_conversion != null && smelter.m_conversion.Count > 0;
        }

        // Auto-fill / Shift+[E] fill-to-max never touch the bag — chests only
        // (StationLink + [I]/[H] still apply).

        /// <summary>Vanilla RPC_AddFuel accepts while fuel ≤ max−1.</summary>
        private static int SmelterFuelFree(Smelter smelter)
        {
            if (smelter == null || smelter.m_maxFuel <= 0)
                return 0;
            float fuel = ReadSmelterFuel(smelter);
            // Prior remote bug could push ZDO past max — clamp so we can run again.
            if (fuel > smelter.m_maxFuel)
            {
                WriteSmelterFuel(smelter, smelter.m_maxFuel);
                fuel = smelter.m_maxFuel;
            }
            if (fuel > smelter.m_maxFuel - 1f)
                return 0;
            return Mathf.Max(0, Mathf.FloorToInt(smelter.m_maxFuel - fuel));
        }

        private static int SmelterOreFree(Smelter smelter)
        {
            if (smelter == null || smelter.m_maxOre <= 0)
                return 0;
            int have = Mathf.Max(0, Mathf.RoundToInt(ReadNumber(SmelterGetQueue, smelter)));
            if (have >= smelter.m_maxOre)
                return 0;
            return smelter.m_maxOre - have;
        }

        private static float ReadSmelterFuel(Smelter smelter)
        {
            float viaMethod = ReadNumber(SmelterGetFuel, smelter);
            ZNetView nv = smelter != null ? smelter.GetComponent<ZNetView>() : null;
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return viaMethod;
            if (FuelHash != 0)
                return zdo.GetFloat(FuelHash, viaMethod);
            return zdo.GetFloat("fuel", viaMethod);
        }

        private static void WriteSmelterFuel(Smelter smelter, float fuel)
        {
            if (smelter == null)
                return;
            if (SmelterSetFuel != null)
            {
                try
                {
                    SmelterSetFuel.Invoke(smelter, new object[] { fuel });
                    return;
                }
                catch
                {
                }
            }

            ZNetView nv = smelter.GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return;
            if (FuelHash != 0)
                zdo.Set(FuelHash, fuel);
            else
                zdo.Set("fuel", fuel);
        }

        private static bool FillFuelToMax(Smelter smelter, Player player)
        {
            string fuel = StationFeed.SharedFrom(smelter.m_fuelItem);
            if (string.IsNullOrEmpty(fuel))
                return false;

            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return false;
            int need = SmelterFuelFree(smelter);
            if (need <= 0)
                return false;

            // One chest pass for the whole load (one consume RPC / Save per chest) instead of
            // one pass per unit. Per-unit pulls were 20+ RPCs and chest Saves per refill when
            // another player owned the chests.
            int linkId = StationLink.Get(smelter);
            int got = TakeFromChests(player, fuel, need, linkId);
            if (got <= 0)
            {
                Quiet(smelter, "fuel", QuietEmptySeconds);
                StationMissingLabel.SetMissing(smelter, "fuel", DisplayFilters.ItemLabel(fuel));
                return false;
            }
            StationMissingLabel.ClearMissing(smelter, "fuel");

            if (!nv.IsOwner())
                nv.ClaimOwnership();
            int added = 0;
            BeginSilence();
            try
            {
                while (added < got)
                {
                    // Match vanilla: full when fuel > max−1 (RPC would no-op).
                    if (ReadSmelterFuel(smelter) > smelter.m_maxFuel - 1f)
                        break;
                    nv.InvokeRPC("RPC_AddFuel");
                    added++;
                }
            }
            finally
            {
                EndSilence();
            }
            ReturnUnused(smelter, fuel, got - added);

            if (added > 0)
            {
                ActivityLog.FromChest(
                    StationOutput.StationLabel(smelter),
                    added,
                    DisplayFilters.ItemLabel(fuel));
            }
            return added > 0;
        }

        /// <summary>
        /// Red label text for an empty input: the item itself when there is one, otherwise a word that fits the
        /// station (the kiln takes wood, the windmill grain, only smelters take ore).
        /// </summary>
        private static string MissingInputLabel(Smelter smelter, List<string> allowed)
        {
            if (allowed.Count == 1)
                return DisplayFilters.ItemLabel(allowed[0]);
            string n = Utils.GetPrefabName(smelter.gameObject).ToLowerInvariant();
            if (n.Contains("kiln"))
                return Loc.T("Wood", "Holz");
            if (n.Contains("windmill"))
                return Loc.T("Grain", "Getreide");
            if (n.Contains("spinning"))
                return Loc.T("Flax", "Flachs");
            if (n.Contains("smelter") || n.Contains("blast"))
                return Loc.T("Ore", "Erz");

            // Other stations (mods): the real item names, at most three.
            var names = new List<string>();
            for (int i = 0; i < allowed.Count && i < 3; i++)
                names.Add(DisplayFilters.ItemLabel(allowed[i]));
            return string.Join(", ", names.ToArray()) + (allowed.Count > 3 ? ", ..." : "");
        }

        private static bool FillOreToMax(Smelter smelter, Player player)
        {
            List<string> allowed = StationPullFilter.AllowedOreNames(smelter);
            if (allowed == null || allowed.Count == 0)
            {
                Quiet(smelter, "ore", QuietEmptySeconds);
                StationMissingLabel.ClearMissing(smelter, "ore");
                return false;
            }

            ZNetView nv = smelter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return false;
            int need = SmelterOreFree(smelter);
            if (need <= 0)
                return false;

            int linkId = StationLink.Get(smelter);
            int added = 0;
            var byShared = new Dictionary<string, int>(4);
            // Same order as before (first allowed type the chests have), but each type is
            // taken in one chest pass instead of one pass per ore.
            for (int guard = 0; guard < allowed.Count && added < need; guard++)
            {
                string shared = FirstChestItem(player, allowed, linkId);
                string prefab = PrefabName(shared);
                if (string.IsNullOrEmpty(prefab))
                    break;

                int got = TakeFromChests(player, shared, need - added, linkId);
                if (got <= 0)
                    break;

                if (!nv.IsOwner())
                    nv.ClaimOwnership();
                int put = 0;
                BeginSilence();
                try
                {
                    while (put < got)
                    {
                        if (Mathf.RoundToInt(ReadNumber(SmelterGetQueue, smelter)) >= smelter.m_maxOre)
                            break;
                        nv.InvokeRPC("RPC_AddOre", prefab, false);
                        put++;
                    }
                }
                finally
                {
                    EndSilence();
                }
                ReturnUnused(smelter, shared, got - put);

                if (put > 0)
                {
                    added += put;
                    int n;
                    byShared.TryGetValue(shared, out n);
                    byShared[shared] = n + put;
                }
                if (put < got)
                    break;
            }

            if (added == 0)
            {
                Quiet(smelter, "ore", QuietEmptySeconds);
                StationMissingLabel.SetMissing(smelter, "ore", MissingInputLabel(smelter, allowed));
                return false;
            }
            StationMissingLabel.ClearMissing(smelter, "ore");

            string station = StationOutput.StationLabel(smelter);
            foreach (KeyValuePair<string, int> kv in byShared)
                ActivityLog.FromChest(station, kv.Value, DisplayFilters.ItemLabel(kv.Key));
            return true;
        }

        /// <summary>
        /// Take up to <paramref name="want"/> of one item from the allowed chests in a single pass.
        /// Returns how many were taken (0 = chests have none).
        /// </summary>
        private static int TakeFromChests(Player player, string shared, int want, int linkId)
        {
            if (player == null || want <= 0 || string.IsNullOrEmpty(shared))
                return 0;
            int have = RequirementBridge.CountNearby(player, shared, linkId, -1);
            if (have <= 0)
                return 0;
            int got = StationFeed.ConsumeFromChests(player, shared, Mathf.Min(want, have), linkId);
            // Same-frame counts are cached; the next type / station must see the new stock.
            NearbyIndex.InvalidateCounts();
            return got;
        }

        /// <summary>
        /// Safety net for a batch pull the station could not take (it filled up in between):
        /// put the rest back into a nearby chest, else drop it at the station. Normally 0.
        /// </summary>
        private static void ReturnUnused(Component station, string shared, int amount)
        {
            if (station == null || amount <= 0 || string.IsNullOrEmpty(shared))
                return;
            string prefabName = PrefabName(shared);
            if (string.IsNullOrEmpty(prefabName))
                return;
            string ignored;
            if (StationOutput.TryDepositNear(station, prefabName, amount, out ignored))
                return;

            GameObject prefab = ItemIds.PrefabFromToken(shared);
            ItemDrop proto = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (proto == null || proto.m_itemData == null || proto.m_itemData.m_shared == null)
                return;
            int maxStack = Mathf.Max(1, proto.m_itemData.m_shared.m_maxStackSize);
            Vector3 pos = station.transform.position + Vector3.up * 1f;
            // Linked station: the ground stack may only go back into matching [lN] chests.
            int linkId = StationLink.Get(station);
            while (amount > 0)
            {
                int stack = Mathf.Min(amount, maxStack);
                amount -= stack;
                ItemDrop drop = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity).GetComponent<ItemDrop>();
                if (drop == null)
                    continue;
                drop.m_itemData.m_stack = stack;
                ItemDrop.OnCreateNew(drop);
                if (linkId >= 1)
                    StationOutput.SetIntakeLink(drop, linkId);
            }
        }

        private static bool FillFireWhenEmpty(Fireplace fire, Player player)
        {
            if (fire == null)
                return false;
            if (ReadFireFuel(fire) > 0.01f)
                return false;
            return FillFireToMax(fire, player);
        }

        /// <summary>Top up fireplace/torch fuel to m_maxFuel from bag and chests (Shift+[E]).</summary>
        internal static bool ManualFillFireToMax(Fireplace fire, Player player)
        {
            if (fire == null || player == null || !fire.m_canRefill)
                return false;
            StationLink.PushStation(fire);
            StationFeed.PullRangeOverride = Plugin.Settings != null ? Plugin.Settings.CraftRange.Value : 0f;
            try
            {
                return FillFireToMax(fire, player);
            }
            finally
            {
                StationFeed.PullRangeOverride = 0f;
                StationLink.Pop();
            }
        }

        private static bool FillFireToMax(Fireplace fire, Player player)
        {
            ZNetView nv = fire.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return false;

            string fuel = StationFeed.SharedFrom(fire.m_fuelItem);
            if (string.IsNullOrEmpty(fuel) || fire.m_maxFuel <= 0f || fire.m_infiniteFuel)
                return false;

            // Vanilla: add while Ceil(fuel) < m_maxFuel.
            int have = Mathf.Max(0, Mathf.CeilToInt(ReadFireFuel(fire)));
            int max = Mathf.Max(1, Mathf.FloorToInt(fire.m_maxFuel));
            int need = max - have;
            if (need <= 0)
                return false;

            QuietUntil.Remove(QuietKey(fire, "fire"));

            int linkId = StationLink.Get(fire);
            int got = TakeFromChests(player, fuel, need, linkId);
            if (got <= 0)
            {
                Quiet(fire, "fire", QuietEmptySeconds);
                StationMissingLabel.SetMissing(fire, "fire", DisplayFilters.ItemLabel(fuel));
                return false;
            }
            StationMissingLabel.ClearMissing(fire, "fire");

            if (!nv.IsOwner())
                nv.ClaimOwnership();
            int added = 0;
            BeginSilence();
            try
            {
                while (added < got)
                {
                    if (Mathf.CeilToInt(ReadFireFuel(fire)) >= max)
                        break;
                    nv.InvokeRPC("RPC_AddFuel");
                    added++;
                }
            }
            finally
            {
                EndSilence();
            }
            ReturnUnused(fire, fuel, got - added);

            if (added > 0)
            {
                ActivityLog.FromChest(
                    StationOutput.StationLabel(fire),
                    added,
                    DisplayFilters.ItemLabel(fuel));
            }
            return added > 0;
        }

        private static bool FillOvenWhenEmpty(CookingStation oven, Player player)
        {
            bool did = false;
            if (OvenNeedsFuel(oven))
                did |= FillOvenFuel(oven, player);
            // Free slots, not only fully empty: stone oven / spit can top up after
            // auto-drop clears done food. OnUseItem needs a lit fire under stone ovens;
            // FillOvenFood uses RPC_AddItem so baking can queue without that block.
            if (OvenNeedsFood(oven))
                did |= FillOvenFood(oven, player);
            return did;
        }

        private static bool OvenNeedsFuel(CookingStation oven)
        {
            return oven != null && oven.m_useFuel && oven.m_fuelItem != null && oven.m_maxFuel > 0
                && !IsQuiet(oven, "fuel") && ReadNumber(CookGetFuel, oven) <= 0.01f;
        }

        private static bool OvenNeedsFood(CookingStation oven)
        {
            return oven != null && !IsQuiet(oven, "food") && !IsTrue(CookIsFull, oven);
        }

        private static bool FillOvenFuel(CookingStation oven, Player player)
        {
            string fuel = StationFeed.SharedFrom(oven.m_fuelItem);
            if (string.IsNullOrEmpty(fuel))
                return false;

            ZNetView nv = oven.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return false;
            int have = Mathf.Max(0, Mathf.FloorToInt(ReadNumber(CookGetFuel, oven)));
            int need = Mathf.Max(0, oven.m_maxFuel - have);
            if (need <= 0)
                return false;

            int linkId = StationLink.Get(oven);
            int got = TakeFromChests(player, fuel, need, linkId);
            if (got <= 0)
            {
                Quiet(oven, "fuel", QuietEmptySeconds);
                StationMissingLabel.SetMissing(oven, "fuel", DisplayFilters.ItemLabel(fuel));
                return false;
            }
            StationMissingLabel.ClearMissing(oven, "fuel");

            if (!nv.IsOwner())
                nv.ClaimOwnership();
            int added = 0;
            BeginSilence();
            try
            {
                while (added < got)
                {
                    if (ReadNumber(CookGetFuel, oven) > oven.m_maxFuel - 1f)
                        break;
                    nv.InvokeRPC("RPC_AddFuel");
                    added++;
                }
            }
            finally
            {
                EndSilence();
            }
            ReturnUnused(oven, fuel, got - added);

            if (added > 0)
            {
                ActivityLog.FromChest(
                    StationOutput.StationLabel(oven),
                    added,
                    DisplayFilters.ItemLabel(fuel));
            }
            return added > 0;
        }

        /// <summary>Shift+[E] on cooking fuel: top up wood/fuel to max.</summary>
        internal static bool ManualFillOvenFuelToMax(CookingStation oven, Player player)
        {
            if (oven == null || player == null || !oven.m_useFuel || oven.m_fuelItem == null || oven.m_maxFuel <= 0)
                return false;
            StationLink.PushStation(oven);
            StationFeed.PullRangeOverride = Plugin.Settings != null ? Plugin.Settings.CraftRange.Value : 0f;
            try
            {
                return FillOvenFuel(oven, player);
            }
            finally
            {
                StationFeed.PullRangeOverride = 0f;
                StationLink.Pop();
            }
        }

        private static bool FillOvenFood(CookingStation oven, Player player)
        {
            List<string> foods = CookingOnInteractPatch.FoodNames(oven);
            if (foods == null || foods.Count == 0)
            {
                Quiet(oven, "food", QuietEmptySeconds);
                return false;
            }

            ZNetView nv = oven.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return false;

            int linkId = StationLink.Get(oven);
            int added = 0;
            var byShared = new Dictionary<string, int>(4);
            int guard = oven.m_slots != null ? oven.m_slots.Length : 5;
            while (guard-- > 0 && !IsTrue(CookIsFull, oven))
            {
                if (!ChestsHaveAny(player, foods, linkId))
                {
                    if (added == 0)
                        Quiet(oven, "food", QuietEmptySeconds);
                    break;
                }

                string shared = FirstChestItem(player, foods, linkId);
                string prefab = StationFeed.CookPrefabName(oven, shared);
                if (string.IsNullOrEmpty(prefab))
                    prefab = PrefabName(shared);
                if (string.IsNullOrEmpty(prefab) || string.IsNullOrEmpty(shared))
                    break;
                if (StationFeed.ConsumeFromChests(player, shared, 1, linkId) < 1)
                    break;

                if (!nv.IsOwner())
                    nv.ClaimOwnership();
                BeginSilence();
                try
                {
                    nv.InvokeRPC("RPC_AddItem", prefab, false);
                }
                finally
                {
                    EndSilence();
                }
                added++;
                int n;
                byShared.TryGetValue(shared, out n);
                byShared[shared] = n + 1;
            }

            if (byShared.Count > 0)
            {
                string station = StationOutput.StationLabel(oven);
                foreach (KeyValuePair<string, int> kv in byShared)
                    ActivityLog.FromChest(station, kv.Value, DisplayFilters.ItemLabel(kv.Key));
            }
            return added > 0;
        }

        private static bool FillFermenterWhenEmpty(Fermenter fermenter, Player player)
        {
            if (ReadEnumInt(FermenterGetStatus, fermenter) != 0)
                return false;

            // Only mead bases allowed in the fermenter filter (Alt+E). All blocked = a setting, not missing.
            List<string> meads = StationPullFilter.AllowedMeadNames(fermenter);
            if (meads == null || meads.Count == 0)
            {
                Quiet(fermenter, "mead", QuietEmptySeconds);
                StationMissingLabel.ClearMissing(fermenter, "mead");
                return false;
            }

            int linkId = StationLink.Get(fermenter);
            if (!ChestsHaveAny(player, meads, linkId))
            {
                Quiet(fermenter, "mead", QuietEmptySeconds);
                StationMissingLabel.SetMissing(fermenter, "mead", meads.Count == 1
                    ? DisplayFilters.ItemLabel(meads[0])
                    : Loc.T("Mead base", "Met-Basis"));
                return false;
            }
            StationMissingLabel.ClearMissing(fermenter, "mead");

            string shared = FirstChestItem(player, meads, linkId);
            string prefab = PrefabName(shared);
            int hash = PrefabHash(prefab);
            ZNetView nv = fermenter.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || hash == 0
                || StationFeed.ConsumeFromChests(player, shared, 1, linkId) < 1)
            {
                Quiet(fermenter, "mead", QuietEmptySeconds);
                return false;
            }

            BeginSilence();
            try
            {
                nv.InvokeRPC("RPC_AddItem", hash, false);
            }
            finally
            {
                EndSilence();
            }
            // Batch: same base up to the F10 limit in one go (RPCs reach the owner in order).
            int more = FermenterBatch.MaxBatch() - 1;
            int extra = more > 0 ? StationFeed.ConsumeFromChests(player, shared, more, linkId) : 0;
            if (extra > 0)
                FermenterBatch.AddCounted(fermenter, hash, extra);
            ActivityLog.FromChest(
                StationOutput.StationLabel(fermenter),
                1 + extra,
                DisplayFilters.ItemLabel(shared));
            return true;
        }

        private static bool FillTurretWhenEmpty(Turret turret, Player player)
        {
            if (turret == null || player == null || turret.m_maxAmmo <= 0)
                return false;

            int ammo = Mathf.RoundToInt(ReadNumber(TurretGetAmmo, turret));
            if (ammo >= turret.m_maxAmmo)
                return false;

            ZNetView nv = turret.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
                return false;

            int linkId = StationLink.Get(turret);
            bool lockType = ammo > 0;
            int added = 0;
            int need = turret.m_maxAmmo - ammo;
            var byShared = new Dictionary<string, int>(2);

            for (int i = 0; i < need; i++)
            {
                List<string> allowed = TurretAmmoSharedNames(turret, lockType);
                if (allowed == null || allowed.Count == 0)
                {
                    if (added == 0)
                        Quiet(turret, "ammo", QuietEmptySeconds);
                    break;
                }

                if (!ChestsHaveAny(player, allowed, linkId))
                {
                    if (added == 0)
                    {
                        Quiet(turret, "ammo", QuietEmptySeconds);
                        if (ammo <= 0)
                            StationMissingLabel.SetMissing(turret, "ammo", allowed.Count == 1
                                ? DisplayFilters.ItemLabel(allowed[0])
                                : Loc.T("Ammo", "Munition"));
                    }
                    break;
                }
                StationMissingLabel.ClearMissing(turret, "ammo");

                string shared = FirstChestItem(player, allowed, linkId);
                string prefab = PrefabName(shared);
                if (string.IsNullOrEmpty(prefab))
                    prefab = shared;
                if (string.IsNullOrEmpty(prefab)
                    || StationFeed.ConsumeFromChests(player, shared, 1, linkId) < 1)
                {
                    if (added == 0)
                        Quiet(turret, "ammo", QuietEmptySeconds);
                    break;
                }

                if (!nv.IsOwner())
                    nv.ClaimOwnership();
                BeginSilence();
                try
                {
                    nv.InvokeRPC("RPC_AddAmmo", prefab);
                }
                finally
                {
                    EndSilence();
                }
                added++;
                lockType = true;
                if (!string.IsNullOrEmpty(shared))
                {
                    int n;
                    byShared.TryGetValue(shared, out n);
                    byShared[shared] = n + 1;
                }
            }

            if (byShared.Count > 0)
            {
                string station = StationOutput.StationLabel(turret);
                foreach (KeyValuePair<string, int> kv in byShared)
                    ActivityLog.FromChest(station, kv.Value, DisplayFilters.ItemLabel(kv.Key));
            }
            return added > 0;
        }

        private static List<string> TurretAmmoSharedNames(Turret turret, bool onlyLoadedType)
        {
            var names = new List<string>();
            if (turret == null)
                return names;

            if (onlyLoadedType)
            {
                string loaded = TurretGetAmmoType != null
                    ? TurretGetAmmoType.Invoke(turret, null) as string
                    : null;

                if (!string.IsNullOrEmpty(loaded))
                {
                    // GetAmmoType returns prefab name; map to shared token when possible.
                    GameObject go = ItemIds.PrefabFromToken(loaded);
                    ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    string shared = StationFeed.SharedFrom(drop);
                    if (!string.IsNullOrEmpty(shared))
                        names.Add(shared);
                    else
                        names.Add(loaded);
                    return names;
                }
            }

            if (turret.m_defaultAmmo != null)
            {
                string shared = StationFeed.SharedFrom(turret.m_defaultAmmo);
                if (!string.IsNullOrEmpty(shared))
                    names.Add(shared);
            }

            if (turret.m_allowedAmmo != null)
            {
                for (int i = 0; i < turret.m_allowedAmmo.Count; i++)
                {
                    ItemDrop ammoDrop = turret.m_allowedAmmo[i].m_ammo;
                    if (ammoDrop == null)
                        continue;
                    string shared = StationFeed.SharedFrom(ammoDrop);
                    if (string.IsNullOrEmpty(shared) || names.Contains(shared))
                        continue;
                    names.Add(shared);
                }
            }

            return names;
        }

        private static bool FillFoodTrayWhenEmpty(ItemStand stand, Player player)
        {
            // ItemStand.UseItem needs a bag stack — auto-fill is chests-only and must not
            // stage through inventory. No safe chest→tray RPC in this mod; skip quietly.
            if (stand == null || player == null)
                return false;
            if (IsTrue(ItemStandHaveAttachment, stand))
                return false;
            Quiet(stand, "tray", QuietEmptySeconds);
            return false;
        }

        private static string FirstChestItem(Player player, List<string> sharedNames)
        {
            return FirstChestItem(player, sharedNames, StationLink.ActiveId);
        }

        private static string FirstChestItem(Player player, List<string> sharedNames, int linkId)
        {
            if (sharedNames == null)
                return null;
            foreach (string shared in sharedNames)
            {
                if (!string.IsNullOrEmpty(shared) && StationFeed.ChestsHave(player, shared, linkId))
                    return shared;
            }
            return null;
        }

        private static string PrefabName(string shared)
        {
            if (string.IsNullOrEmpty(shared))
                return null;
            GameObject prefab = ItemIds.PrefabFromToken(shared);
            return prefab != null ? ItemIds.StripClone(prefab.name) : null;
        }

        private static int PrefabHash(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return 0;
            MethodInfo hash = AccessTools.Method(typeof(StringExtensionMethods), "GetStableHashCode");
            if (hash == null)
                return 0;
            try
            {
                object value = hash.Invoke(null, new object[] { prefabName });
                return value is int ? (int)value : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool ChestsHaveAny(Player player, List<string> sharedNames)
        {
            return ChestsHaveAny(player, sharedNames, StationLink.ActiveId);
        }

        private static bool ChestsHaveAny(Player player, List<string> sharedNames, int linkId)
        {
            if (sharedNames == null)
                return false;
            foreach (string shared in sharedNames)
            {
                if (StationFeed.ChestsHave(player, shared, linkId))
                    return true;
            }
            return false;
        }

        private static void BeginSilence()
        {
            Silence++;
        }

        private static void EndSilence()
        {
            if (Silence > 0)
                Silence--;
        }

        /// <summary>Settings menu switch (same ZDO flag as [B]).</summary>
        internal static void SetOn(Component station, bool on)
        {
            ZNetView nv = station != null ? station.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                return;
            if (!nv.IsOwner())
                nv.ClaimOwnership();
            nv.GetZDO().Set(ZdoKey, on ? 1 : 0);
            ClearQuiet(station);
            ActivityLog.Note(StationOutput.StationLabel(station),
                on ? Loc.T("Auto-fill on", "Auto-Fill an") : Loc.T("Auto-fill off", "Auto-Fill aus"));
        }

        private static void RefreshTroughs()
        {
            Troughs.Clear();
            for (int i = 0; i < FeedTrough.Live.Count; i++)
            {
                FeedTrough t = FeedTrough.Live[i];
                Container c = t != null ? t.GetComponent<Container>() : null;
                if (c != null)
                    Troughs.Add(c);
            }
        }

        /// <summary>Cached flag from the trough itself (no inventory walk on every pulse).</summary>
        private static bool TroughEmpty(Container trough)
        {
            FeedTrough t = trough != null ? trough.GetComponent<FeedTrough>() : null;
            return t != null && !t.HasFood;
        }

        private static int TroughFoodCount(Container trough)
        {
            Inventory inv = trough != null ? trough.GetInventory() : null;
            List<ItemDrop.ItemData> items = inv != null ? inv.GetAllItems() : null;
            if (items == null)
                return 0;
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].m_stack > 0)
                    n += items[i].m_stack;
            }
            return n;
        }

        /// <summary>
        /// Empty feed trough: fill up to TroughTarget with allowed animal food
        /// from (linked) chests. Troughs are never a source here (no self / trough-to-trough fill).
        /// </summary>
        private static bool FillTroughWhenLow(Container trough, Player player)
        {
            if (trough == null || player == null || trough.IsInUse())
                return false;
            ZNetView nv = trough.GetComponent<ZNetView>();
            Inventory inv = trough.GetInventory();
            if (nv == null || !nv.IsValid() || inv == null)
                return false;

            List<string> allowed = StationPullFilter.AllowedTroughFoods(trough);
            if (allowed == null || allowed.Count == 0)
            {
                Quiet(trough, "feed", QuietEmptySeconds);
                StationMissingLabel.ClearMissing(trough, "feed");
                return false;
            }

            int need = TroughTarget - TroughFoodCount(trough);
            if (need <= 0)
                return false;

            int linkId = StationLink.Get(trough);
            int added = 0;
            var byShared = new Dictionary<string, int>(4);
            StationLink.ExcludeTroughs = true;
            try
            {
                for (int guard = 0; guard < allowed.Count && added < need; guard++)
                {
                    string shared = FirstChestItem(player, allowed, linkId);
                    GameObject prefab = ItemIds.PrefabFromToken(shared);
                    if (prefab == null)
                        break;

                    int got = TakeFromChests(player, shared, need - added, linkId);
                    if (got <= 0)
                        break;

                    if (!nv.IsOwner())
                        nv.ClaimOwnership();
                    int fit = got;
                    while (fit > 0 && !inv.CanAddItem(prefab, fit))
                        fit--;
                    if (fit > 0 && !inv.AddItem(prefab, fit))
                        fit = 0;
                    ReturnUnused(trough, shared, got - fit);
                    if (fit <= 0)
                        break;

                    added += fit;
                    int n;
                    byShared.TryGetValue(shared, out n);
                    byShared[shared] = n + fit;
                }
            }
            finally
            {
                StationLink.ExcludeTroughs = false;
            }

            if (added == 0)
            {
                Quiet(trough, "feed", QuietEmptySeconds);
                StationMissingLabel.SetMissing(trough, "feed", allowed.Count == 1
                    ? DisplayFilters.ItemLabel(allowed[0])
                    : Loc.T("Animal food", "Tierfutter"));
                return false;
            }

            ContainerFilter.SaveInventory(trough);
            StationMissingLabel.ClearMissing(trough, "feed");
            string label = StationOutput.StationLabel(trough);
            foreach (KeyValuePair<string, int> kv in byShared)
                ActivityLog.FromChest(label, kv.Value, DisplayFilters.ItemLabel(kv.Key));
            return true;
        }

        /// <summary>Auto-fill switched on for this station (missing-label keep check).</summary>
        internal static bool IsAutoFillOn(Component station)
        {
            if (station is Smelter s)
                return IsOn(s);
            if (station is Fireplace f)
                return IsOn(f);
            return station != null && IsOn(station.GetComponent<ZNetView>());
        }

        /// <summary>
        /// Slot still empty (same reads as StationNeedsFill, ignoring the quiet timer). Used on the
        /// pulse to drop a missing-label once the station got fuel/ore some other way.
        /// </summary>
        internal static bool SlotStillEmpty(Component station, string slot)
        {
            if (station is Smelter s)
            {
                if (slot == "fuel")
                    return HasFuelSlot(s) && ReadNumber(SmelterGetFuel, s) <= 0.01f;
                if (slot == "ore")
                    return HasOreSlot(s) && ReadNumber(SmelterGetQueue, s) < 1f;
                return false;
            }
            if (station is Fireplace f)
                return slot == "fire" && ReadFireFuel(f) <= 0.01f;
            if (station is CookingStation o)
                return slot == "fuel" && ReadNumber(CookGetFuel, o) <= 0.01f;
            if (station is Fermenter m)
                return slot == "mead" && ReadEnumInt(FermenterGetStatus, m) == 0;
            if (station is Turret t)
                return slot == "ammo" && Mathf.RoundToInt(ReadNumber(TurretGetAmmo, t)) <= 0;
            if (station is Container trough)
                return slot == "feed" && TroughEmpty(trough);
            if (station is Scarecrow sc)
                return slot == "seed" && sc.MissingSeeds;
            return false;
        }

        private static bool IsQuiet(UnityEngine.Object obj, string slot)
        {
            if (obj == null)
                return true;
            float until;
            return QuietUntil.TryGetValue(QuietKey(obj, slot), out until) && Time.unscaledTime < until;
        }

        private static void Quiet(UnityEngine.Object obj, string slot, float seconds)
        {
            if (obj == null)
                return;
            QuietUntil[QuietKey(obj, slot)] = Time.unscaledTime + seconds;
        }

        private static bool ClearQuiet(UnityEngine.Object obj)
        {
            if (obj == null)
                return false;
            int id = obj.GetInstanceID();
            bool any = false;
            any |= QuietUntil.Remove(id + ":fuel");
            any |= QuietUntil.Remove(id + ":ore");
            any |= QuietUntil.Remove(id + ":fire");
            any |= QuietUntil.Remove(id + ":food");
            any |= QuietUntil.Remove(id + ":mead");
            any |= QuietUntil.Remove(id + ":ammo");
            any |= QuietUntil.Remove(id + ":tray");
            any |= QuietUntil.Remove(id + ":feed");
            return any;
        }

        /// <summary>Filter / link changed on this station: check it again on the next pulse.</summary>
        internal static void WakeStation(Component station)
        {
            if (ClearQuiet(station))
                RequestSoon();
        }

        /// <summary>
        /// A chest's contents changed: stations within chest reach leave their empty-chest
        /// silence so new items are picked up in about a second instead of up to 25 s.
        /// Own auto-fill pulls are skipped (_inPulse), else every refill would re-wake itself.
        /// </summary>
        internal static void WakeNearChest(Container chest)
        {
            if (_inPulse || chest == null || Player.m_localPlayer == null || Plugin.Settings == null
                || QuietUntil.Count == 0)
                return;

            int id = chest.GetInstanceID();
            float now = Time.unscaledTime;
            float until;
            if (ChestWakeUntil.TryGetValue(id, out until) && now < until)
                return;
            if (ChestWakeUntil.Count > 512)
                ChestWakeUntil.Clear();
            ChestWakeUntil[id] = now + ChestWakeCooldown;

            Vector3 pos = chest.transform.position;
            float reach = Plugin.Settings.AutoFillChestReach();
            float sq = reach * reach;
            bool woke = false;
            woke |= WakeList(Fires, pos, sq);
            woke |= WakeList(Smelters, pos, sq);
            woke |= WakeList(Ovens, pos, sq);
            woke |= WakeList(Fermenters, pos, sq);
            woke |= WakeList(Turrets, pos, sq);
            woke |= WakeList(FoodTrays, pos, sq);
            if (woke)
                RequestSoon();
        }

        private static bool WakeList<T>(List<T> list, Vector3 chestPos, float reachSq) where T : Component
        {
            bool woke = false;
            for (int i = 0; i < list.Count; i++)
            {
                T station = list[i];
                if (station == null)
                    continue;
                if (ContainerFilter.SqrDistance(chestPos, station.transform.position) > reachSq)
                    continue;
                woke |= ClearQuiet(station);
            }
            return woke;
        }

        private static string QuietKey(UnityEngine.Object obj, string slot)
        {
            return obj.GetInstanceID() + ":" + slot;
        }

        /// <summary>Remaining burn time in green right after the fire name (first hover line).</summary>
        public static void InsertBurnTime(ref string text, Fireplace fire)
        {
            if (string.IsNullOrEmpty(text) || fire == null || fire.m_infiniteFuel || fire.m_secPerFuel <= 0f)
                return;
            if (Plugin.Settings == null || !Plugin.Settings.ModEnabled.Value)
                return;

            float fuel = ReadFireFuel(fire);
            if (fuel <= 0f)
                return;

            int minutes = Mathf.CeilToInt(fuel * fire.m_secPerFuel / 60f);
            string tag = " <color=#6EE06E>(" + minutes + " min)</color>";
            int nl = text.IndexOf('\n');
            text = nl < 0 ? text + tag : text.Insert(nl, tag);
        }

        private static float ReadFireFuel(Fireplace fire)
        {
            // Fireplace has no GetFuel(): the fuel is the ZDO float "fuel".
            ZNetView nv = fire.GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            if (zdo == null)
                return 0f;
            if (FuelHash != 0)
                return zdo.GetFloat(FuelHash, 0f);
            return zdo.GetFloat("fuel", 0f);
        }

        private static bool IsTrue(MethodInfo method, object target)
        {
            if (method == null || target == null)
                return false;
            try
            {
                object value = method.Invoke(target, null);
                return value is bool && (bool)value;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadEnumInt(MethodInfo method, object target)
        {
            if (method == null || target == null)
                return -1;
            try
            {
                object value = method.Invoke(target, null);
                return value == null ? -1 : Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static float ReadNumber(MethodInfo method, object target)
        {
            if (method == null || target == null)
                return 0f;
            try
            {
                return ToFloat(method.Invoke(target, null));
            }
            catch
            {
                return 0f;
            }
        }

        private static float ToFloat(object value)
        {
            if (value is float)
                return (float)value;
            if (value is int)
                return (int)value;
            if (value is double)
                return (float)(double)value;
            return 0f;
        }

        private static int ReadFuelHash()
        {
            FieldInfo field = AccessTools.Field(typeof(ZDOVars), "s_fuel");
            if (field == null)
                return 0;
            try
            {
                object value = field.GetValue(null);
                if (value is int)
                    return (int)value;
            }
            catch
            {
            }
            return 0;
        }

        private static Fireplace HoveredFireplace()
        {
            return HoveredComponent<Fireplace>();
        }

        private static CookingStation HoveredOven()
        {
            return HoveredComponent<CookingStation>();
        }

        private static Fermenter HoveredFermenter()
        {
            return HoveredComponent<Fermenter>();
        }

        private static Turret HoveredTurret()
        {
            return HoveredComponent<Turret>();
        }

        private static ItemStand HoveredFoodTray()
        {
            ItemStand stand = HoveredComponent<ItemStand>();
            return IsFoodServingTray(stand) ? stand : null;
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

        private static void PruneDead()
        {
            SmelterIds.Clear();
            for (int i = Smelters.Count - 1; i >= 0; i--)
            {
                if (Smelters[i] == null)
                {
                    Smelters.RemoveAt(i);
                    continue;
                }
                SmelterIds.Add(Smelters[i].GetInstanceID());
            }

            FireIds.Clear();
            for (int i = Fires.Count - 1; i >= 0; i--)
            {
                if (Fires[i] == null)
                {
                    Fires.RemoveAt(i);
                    continue;
                }
                FireIds.Add(Fires[i].GetInstanceID());
            }

            OvenIds.Clear();
            for (int i = Ovens.Count - 1; i >= 0; i--)
            {
                if (Ovens[i] == null)
                {
                    Ovens.RemoveAt(i);
                    continue;
                }
                OvenIds.Add(Ovens[i].GetInstanceID());
            }

            FermenterIds.Clear();
            for (int i = Fermenters.Count - 1; i >= 0; i--)
            {
                if (Fermenters[i] == null)
                {
                    Fermenters.RemoveAt(i);
                    continue;
                }
                FermenterIds.Add(Fermenters[i].GetInstanceID());
            }

            TurretIds.Clear();
            for (int i = Turrets.Count - 1; i >= 0; i--)
            {
                if (Turrets[i] == null)
                {
                    Turrets.RemoveAt(i);
                    continue;
                }
                TurretIds.Add(Turrets[i].GetInstanceID());
            }

            FoodTrayIds.Clear();
            for (int i = FoodTrays.Count - 1; i >= 0; i--)
            {
                if (FoodTrays[i] == null)
                {
                    FoodTrays.RemoveAt(i);
                    continue;
                }
                FoodTrayIds.Add(FoodTrays[i].GetInstanceID());
            }

            if (QuietUntil.Count == 0)
                return;
            var dead = new List<string>();
            foreach (KeyValuePair<string, float> pair in QuietUntil)
            {
                if (Time.unscaledTime >= pair.Value)
                    dead.Add(pair.Key);
            }
            for (int i = 0; i < dead.Count; i++)
                QuietUntil.Remove(dead[i]);
        }
    }

    [HarmonyPatch]
    internal static class AutoFillSilencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Humanoid.Message is inherited from Character; GetDeclaredMethods(Humanoid)
            // returned nothing and Harmony crashed on load.
            foreach (MethodInfo method in MethodsNamed(typeof(Character), "Message"))
                yield return method;
            foreach (MethodInfo method in MethodsNamed(typeof(Humanoid), "Message"))
                yield return method;
            foreach (MethodInfo method in MethodsNamed(typeof(Player), "Message"))
                yield return method;
            foreach (MethodInfo method in MethodsNamed(typeof(MessageHud), "ShowMessage"))
                yield return method;
        }

        private static IEnumerable<MethodInfo> MethodsNamed(Type type, string name)
        {
            IEnumerable<MethodInfo> methods;
            try
            {
                methods = AccessTools.GetDeclaredMethods(type);
            }
            catch
            {
                yield break;
            }

            if (methods == null)
                yield break;

            foreach (MethodInfo method in methods)
            {
                if (method != null && method.Name == name)
                    yield return method;
            }
        }

        private static bool Prefix()
        {
            return StationAutoFill.Silence <= 0;
        }
    }

    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class SmelterAwakePatch
    {
        private static void Postfix(Smelter __instance)
        {
            StationAutoFill.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverEmptyOre")]
    internal static class SmelterHoverEmptyOreAutoFillPatch
    {
        private static void Postfix(Smelter __instance, ref string __result)
        {
            if (__instance == null)
                return;
            StationAutoFill.AppendSmelterHover(ref __result, __instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), "Awake")]
    internal static class FireplaceAwakePatch
    {
        private static void Postfix(Fireplace __instance)
        {
            StationAutoFill.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class CookingAwakeAutoFillPatch
    {
        private static void Postfix(CookingStation __instance)
        {
            StationAutoFill.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class FermenterAwakeAutoFillPatch
    {
        private static void Postfix(Fermenter __instance)
        {
            StationAutoFill.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Turret), "Awake")]
    internal static class TurretAwakeAutoFillPatch
    {
        private static void Postfix(Turret __instance)
        {
            StationAutoFill.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemStand), "Awake")]
    internal static class ItemStandAwakeAutoFillPatch
    {
        private static void Postfix(ItemStand __instance)
        {
            StationAutoFill.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverAddFuel")]
    internal static class SmelterHoverAddFuelAutoFillPatch
    {
        private static void Postfix(Smelter __instance, ref string __result)
        {
            if (__instance == null)
                return;
            StationAutoFill.AppendSmelterHover(ref __result, __instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    internal static class FireplaceHoverAutoFillPatch
    {
        private static void Postfix(Fireplace __instance, ref string __result)
        {
            if (__instance == null || !__instance.m_canRefill)
                return;
            StationAutoFill.InsertBurnTime(ref __result, __instance);
            StationAutoFill.AppendFuelStationHover(ref __result, __instance, __instance.GetComponent<ZNetView>());
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
    internal static class CookingHoverAutoFillPatch
    {
        private static void Postfix(CookingStation __instance, ref string __result)
        {
            // Stone oven: vanilla returns "" when m_addFoodSwitch is set (food = Switch,
            // wood = OnHoverFuelSwitch). Appending here invented a third empty-body hover.
            if (__instance == null || __instance.m_addFoodSwitch != null)
                return;
            if (string.IsNullOrEmpty(__result))
                return;
            StationAutoFill.AppendCookingHover(ref __result, __instance);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnHoverFuelSwitch")]
    internal static class CookingHoverFuelAutoFillPatch
    {
        private static void Postfix(CookingStation __instance, ref string __result)
        {
            StationAutoFill.AppendCookingHover(ref __result, __instance);
        }
    }

    /// <summary>
    /// Stone oven food door uses Switch.m_hoverText, and CookingStation.GetHoverText
    /// returns "" when m_addFoodSwitch is set — so spit hover patches never ran there.
    /// Fuel switch already goes through OnHoverFuelSwitch (do not append twice).
    /// </summary>
    [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
    internal static class CookingSwitchHoverAutoFillPatch
    {
        private static void Postfix(Switch __instance, ref string __result)
        {
            if (__instance == null || string.IsNullOrEmpty(__result))
                return;
            CookingStation oven = __instance.GetComponentInParent<CookingStation>();
            if (oven == null || __instance != oven.m_addFoodSwitch)
                return;
            StationAutoFill.AppendCookingHover(ref __result, oven);
        }
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
    internal static class FermenterHoverAutoFillPatch
    {
        private static void Postfix(Fermenter __instance, ref string __result)
        {
            if (__instance == null)
                return;
            FermenterBatch.AppendHover(ref __result, __instance);
            StationAutoFill.AppendFuelStationHover(ref __result, __instance, __instance.GetComponent<ZNetView>());
            CookingAutoDrop.AppendHover(ref __result, __instance);
        }
    }

    [HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))]
    internal static class TurretHoverAutoFillPatch
    {
        private static void Postfix(Turret __instance, ref string __result)
        {
            if (__instance == null)
                return;
            StationAutoFill.AppendHover(ref __result, __instance.GetComponent<ZNetView>(), includeManualFill: false);
        }
    }

    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.GetHoverText))]
    internal static class ItemStandHoverAutoFillPatch
    {
        private static void Postfix(ItemStand __instance, ref string __result)
        {
            if (__instance == null || !StationAutoFill.IsFoodServingTray(__instance))
                return;
            StationAutoFill.AppendHover(ref __result, __instance.GetComponent<ZNetView>(), includeManualFill: false);
        }
    }

    /// <summary>When a vanilla station update runs and autofill is on, nudge our pulse sooner.</summary>
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterUpdateAutofillNudgePatch
    {
        private static void Postfix(Smelter __instance)
        {
            if (__instance != null && StationAutoFill.IsOn(__instance))
                StationAutoFill.NudgeIfNeedy(__instance);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    internal static class CookingUpdateAutofillNudgePatch
    {
        private static void Postfix(CookingStation __instance)
        {
            if (__instance != null && StationAutoFill.IsOn(__instance.GetComponent<ZNetView>()))
                StationAutoFill.NudgeIfNeedy(__instance);
        }
    }

    // Read-only hooks: the chest wipe guard lives on Save / Inventory.Load and is not touched.
    [HarmonyPatch(typeof(Container), "OnContainerChanged")]
    internal static class ContainerChangedWakeStationsPatch
    {
        private static void Postfix(Container __instance)
        {
            StationAutoFill.WakeNearChest(__instance);
        }
    }

    // Load() is true only when the ZDO data revision changed (another player edited the chest).
    [HarmonyPatch(typeof(Container), "Load")]
    internal static class ContainerLoadWakeStationsPatch
    {
        private static void Postfix(Container __instance, bool __result)
        {
            if (__result)
                StationAutoFill.WakeNearChest(__instance);
        }
    }

    // Piece.SetCreator is called once, by Player.PlacePiece, on the freshly placed piece (verified in
    // the game code): the only moment a torch counts as "new" for TorchAutoFillDefault.
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class TorchPlacedDefaultPatch
    {
        private static void Postfix(Piece __instance)
        {
            StationAutoFill.ApplyTorchDefault(__instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
    internal static class FireplaceUpdateAutofillNudgePatch
    {
        private static void Postfix(Fireplace __instance)
        {
            if (__instance != null && __instance.m_canRefill && StationAutoFill.IsOn(__instance))
                StationAutoFill.NudgeIfNeedy(__instance);
        }
    }
}
