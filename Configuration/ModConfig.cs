using BepInEx.Configuration;
using UnityEngine;

namespace StoreAndCraft
{
    public class ModConfig
    {
        // Bump when package layout or shared station behavior changes. v18 = SAC-CATCHUP settings synced.
        // v19 = TorchAutoFillDefault synced.
        // v20 = station capacities (6 - Stations) + fermenter batch synced.
        // v21 = TorchAutoFillOverride synced (World Override for torches); chest flags (SAC_chestFlags) in the chest ZDO.
        public const int ProtocolVersion = 22;
        /// <summary>Highest station capacity the F10 panel / config accepts.</summary>
        public const float MaxStationCap = 50f;
        public const float MaxRange = 1000f;

        public ConfigEntry<bool> LockConfig { get; }
        public ConfigEntry<bool> ModEnabled { get; }
        public ConfigEntry<bool> StoreEnabled { get; }
        public ConfigEntry<bool> AutoIntakeEnabled { get; }
        public ConfigEntry<bool> CraftEnabled { get; }
        public ConfigEntry<bool> MustHaveExisting { get; }
        public ConfigEntry<bool> LeaveOneItem { get; }
        public ConfigEntry<bool> IgnoreHotbar { get; }
        public ConfigEntry<bool> HighlightOnStore { get; }
        public ConfigEntry<bool> PingOnStore { get; }
        public ConfigEntry<float> PlayerDumpRange { get; }
        public ConfigEntry<float> StoreRange { get; }
        public ConfigEntry<float> StorageRange { get; }
        public ConfigEntry<float> DisplayRange { get; }
        public ConfigEntry<bool> AutoStackEnabled { get; }
        public ConfigEntry<float> CraftRange { get; }
        public ConfigEntry<float> AutoFillRange { get; }
        public ConfigEntry<float> AutoFillChestRange { get; }
        public ConfigEntry<float> ScarecrowRange { get; }
        public ConfigEntry<float> ScarecrowChestRange { get; }
        public ConfigEntry<bool> AutomationLoadedArea { get; }
        public ConfigEntry<float> IntakeInterval { get; }
        public ConfigEntry<int> MaxTransfersPerTick { get; }
        public ConfigEntry<KeyboardShortcut> DumpKey { get; }
        public ConfigEntry<KeyboardShortcut> HoverStoreKey { get; }
        public ConfigEntry<KeyboardShortcut> SearchKey { get; }
        public ConfigEntry<KeyboardShortcut> RenameKey { get; }
        public ConfigEntry<KeyboardShortcut> TakeStackKey { get; }
        public ConfigEntry<KeyboardShortcut> FavoriteKey { get; }
        public ConfigEntry<KeyboardShortcut> AutoFillKey { get; }
        public ConfigEntry<KeyboardShortcut> AutoDropKey { get; }
        public ConfigEntry<KeyboardShortcut> ActivityLogKey { get; }
        public ConfigEntry<bool> ActivityLogVisible { get; }
        public ConfigEntry<KeyboardShortcut> DisplayRangeKey { get; }
        public ConfigEntry<KeyboardShortcut> BuildGrabKey { get; }
        public ConfigEntry<bool> FeedTroughEnabled { get; }
        public ConfigEntry<float> FeedTroughRange { get; }
        // SAC-CATCHUP
        public ConfigEntry<bool> CatchUpEnabled { get; }
        public ConfigEntry<float> CatchUpMaxHours { get; }
        public ConfigEntry<double> CatchUpSince { get; }
        public ConfigEntry<bool> TorchAutoFillDefault { get; }
        public ConfigEntry<bool> TorchAutoFillOverride { get; }
        public ConfigEntry<bool> ArmorStandSwap { get; }
        // Station capacities: 0 = vanilla, else 1–50 (StationCaps applies them live).
        public ConfigEntry<float> KilnMaxWood { get; }
        public ConfigEntry<float> SmelterMaxOre { get; }
        public ConfigEntry<float> SmelterMaxCoal { get; }
        public ConfigEntry<float> BlastFurnaceMaxOre { get; }
        public ConfigEntry<float> BlastFurnaceMaxCoal { get; }
        public ConfigEntry<float> EitrRefineryMaxInput { get; }
        public ConfigEntry<float> EitrRefineryMaxSap { get; }
        public ConfigEntry<float> BeehiveMaxHoney { get; }
        public ConfigEntry<float> FermenterBatch { get; }
        // Local only (not synced): Hugin release news once per version.
        public ConfigEntry<bool> ShowReleaseNews { get; }
        public ConfigEntry<string> ReleaseNewsSeen { get; }
        // Local only (not synced): Stack / Sort buttons on the inventory and on the chest panel.
        public ConfigEntry<bool> ShowBagSortButtons { get; }
        public ConfigEntry<bool> ShowChestSortButtons { get; }
        // Local only (not synced): "Chest full" / "No linked chest" / "No chest nearby" notice above stations.
        public ConfigEntry<bool> ShowStationNotices { get; }
        // Local only (not synced): item lists set in the F10 panel (see Store/ItemLists.cs).
        public ConfigEntry<string> GroundIgnoreItems { get; }
        public ConfigEntry<string> KeepInBagItems { get; }
        public ConfigEntry<string> RestockItems { get; }
        public ConfigEntry<KeyboardShortcut> RestockKey { get; }

        public ModConfig(ConfigFile file)
        {
            var range = new AcceptableValueRange<float>(0f, MaxRange);
            LockConfig = file.Bind("1 - General", "LockConfig", true,
                "If true (recommended on dedicated), gameplay values below are owned by the SERVER. Edit com.morda.storeandcraft.cfg on the server only — clients receive them on join. Hotkeys stay local.");
            ModEnabled = file.Bind("1 - General", "ModEnabled", true,
                "Turns the whole mod on or off without uninstalling.");
            StoreEnabled = file.Bind("2 - Store", "StoreEnabled", true,
                "If enabled, dump / middle-click store, take-stack, and auto-stack work. Ground auto-store is controlled separately by AutoIntakeEnabled.");
            AutoIntakeEnabled = file.Bind("2 - Store", "AutoIntakeEnabled", true,
                "If enabled, ground items are pulled into nearby matching chests automatically. Turn off to leave drops on the ground (feed pets, trade) while dump / middle-click still work. Synced from server when LockConfig is on. Chat: /store enable|disable.");
            CraftEnabled = file.Bind("3 - Craft", "CraftEnabled", true,
                "If enabled, crafting, building, station refill ([E] on smelters, kilns, ovens, torches, fires, fermenters, turrets), and kiln/smelter/torch auto-fill can use items stored in nearby chests.");
            MustHaveExisting = file.Bind("2 - Store", "MustHaveExisting", true,
                "If enabled, a chest only accepts an item if that item is already inside it. Empty chests will not vacuum new item types.");
            LeaveOneItem = file.Bind("3 - Craft", "LeaveOneItem", true,
                "If enabled, every pull from a chest (craft, build, plant, station [E], Ctrl+middle-click fill) leaves 1 item so auto-store can keep filling that stack. That leftover item cannot be spent (same as smelter [E]).");
            IgnoreHotbar = file.Bind("2 - Store", "IgnoreHotbar", true,
                "If enabled, dump will not move items from the hotbar (first inventory row). Equipment / quick-slot mod overflow (outside the live bag grid) is always skipped. Wider/Deeper Pockets bag rows are dumped normally.");
            HighlightOnStore = file.Bind("2 - Store", "HighlightOnStore", true,
                "If enabled, a chest flashes when something is stored into it.");
            PingOnStore = file.Bind("2 - Store", "PingOnStore", false,
                "If enabled, a map ping is placed on the chest after a store.");
            PlayerDumpRange = file.Bind("2 - Store", "PlayerDumpRange", 8f, new ConfigDescription(
                "Dump / middle-click store range in meters (player → chest). Synced from server when LockConfig is on.", range));
            StoreRange = file.Bind("2 - Store", "StoreRange", 10f, new ConfigDescription(
                "Auto-store range in meters (ground item → chest). Synced from server when LockConfig is on.", range));
            StorageRange = file.Bind("2 - Store", "StorageRange", 10f, new ConfigDescription(
                "Extra reach for take-stack and search (meters). Synced from server when LockConfig is on.", range));
            DisplayRange = file.Bind("2 - Store", "DisplayRange", 10f, new ConfigDescription(
                "Not used any more: every Storage Display sets its own range with Alt+R (5–50 m, 10 m until set). Kept so config files and the server sync stay compatible.", range));
            AutoStackEnabled = file.Bind("2 - Store", "AutoStackEnabled", false,
                "If enabled, stacks already inside a chest are compacted toward the Valheim max. Never moves items from your inventory; dump and middle-click do that.");
            CraftRange = file.Bind("3 - Craft", "CraftRange", 20f, new ConfigDescription(
                "Craft / build / station-[E] pull range in meters (player → chest). Synced from server when LockConfig is on.", range));
            AutoFillRange = file.Bind("3 - Craft", "AutoFillRange", 20f, new ConfigDescription(
                "Auto-fill range in meters: player → station (kiln/smelter/blast furnace/oven/fermenter/torch). Stations farther away pause until you come closer. Chests are searched around each station (see AutoFillChestRange). Auto-fill never takes from your bag, only from matching linked / untagged chests. Independent from CraftRange. Synced from server when LockConfig is on.", range));
            AutoFillChestRange = file.Bind("3 - Craft", "AutoFillChestRange", 0f, new ConfigDescription(
                "Station → chest range in meters for auto-fill materials and auto-store output. 0 = same as AutoFillRange. Tip: large AutoFillRange (e.g. 80) with a small AutoFillChestRange (e.g. 10) keeps a whole base running while each station only uses chests right next to it. Synced from server when LockConfig is on.", range));
            ScarecrowRange = file.Bind("3 - Craft", "ScarecrowRange", 0f, new ConfigDescription(
                "Scarecrow range in meters: player → scarecrow. It plants and harvests only while you are at most this far away. 0 = same as AutoFillRange. Synced from server when LockConfig is on.", range));
            ScarecrowChestRange = file.Bind("3 - Craft", "ScarecrowChestRange", 0f, new ConfigDescription(
                "Scarecrow → chest range in meters: chests that supply its seeds and take its harvest. 0 = same as AutoFillChestRange (and that one 0 = AutoFillRange). Synced from server when LockConfig is on.", range));
            AutomationLoadedArea = file.Bind("3 - Craft", "AutomationLoadedArea", true,
                "Auto-fill, auto-store and scarecrows work everywhere in the area loaded around a player, not only within AutoFillRange / StoreRange / ScarecrowRange of the player. Stations and ground items still only use chests within their own chest range. Off = the player ranges as before. Synced from server when LockConfig is on.");
            IntakeInterval = file.Bind("2 - Store", "IntakeInterval", 5f,
                "Seconds between automatic scans for ground items. Lower = snappier, higher = less CPU.");
            MaxTransfersPerTick = file.Bind("1 - General", "MaxTransfersPerTick", 8,
                "Maximum item moves per frame. Raise only if storing feels too slow.");
            DumpKey = file.Bind("4 - Keys", "DumpKey", new KeyboardShortcut(KeyCode.Period),
                "Hotkey: move allowed inventory stacks into nearby chests that already hold those items.");
            HoverStoreKey = file.Bind("4 - Keys", "HoverStoreKey", new KeyboardShortcut(KeyCode.Mouse2),
                "Hotkey: store only the inventory item under the cursor into a nearby chest that already holds it.");
            SearchKey = file.Bind("4 - Keys", "SearchKey", new KeyboardShortcut(KeyCode.Y),
                "While inventory is open: hover an item and press to ping/blink the nearest chest that contains it (blinks 3 times).");
            RenameKey = file.Bind("4 - Keys", "RenameKey", new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
                "Settings (default Alt+E): look at a chest (name / ignore / display / link), a small Storage Display (name/amount), or a multi-input kiln/smelter/grill (pull filter + link). Shift+E (Valheim alt-use) also opens chest settings. Prefix [I] = fully ignore; [H] = hide from dump/store/craft but still show on Storage Displays; [M] = manual fill (no dump/intake into chest, station pull + [lN] links still work).");
            TakeStackKey = file.Bind("4 - Keys", "TakeStackKey", new KeyboardShortcut(KeyCode.Mouse2, KeyCode.LeftControl),
                "Hotkey: fill the hovered inventory stack from nearby chests, only up to max stack / carry weight.");
            FavoriteKey = file.Bind("4 - Keys", "FavoriteKey", new KeyboardShortcut(KeyCode.F),
                "Hotkey: while inventory is open, hover an item and press to favorite / unfavorite. Favorites are skipped by dump, hover-store, and inventory sort (local, not synced).");
            AutoFillKey = file.Bind("4 - Keys", "AutoFillKey", new KeyboardShortcut(KeyCode.B),
                "Look at a kiln, smelter, blast furnace, cooking / stone oven, fermenter, or torch / fire with the inventory closed and press to toggle auto-fill. The station pull filter still applies. Local, not synced.");
            AutoDropKey = file.Bind("4 - Keys", "AutoDropKey", new KeyboardShortcut(KeyCode.N),
                "Look at a kiln/smelter/blast furnace, cooking spit/oven, fermenter, or beehive with the inventory closed and press to toggle auto-store. Finished bars / food / mead / honey go into a nearby chest (ground only if no space). Independent from auto-fill (B). Per piece.");
            ActivityLogKey = file.Bind("4 - Keys", "ActivityLogKey", new KeyboardShortcut(KeyCode.F10),
                "Opens the StoreAndCraft panel: activity log checkbox (anyone, local) and range sliders with Save (host / server admin from the adminlist; everyone in an offline / own world). Do not use F11, Valheim saves a screenshot on F11.");
            // Old default F11 is also Valheim's screenshot key.
            if (ActivityLogKey.Value.MainKey == KeyCode.F11 && !ActivityLogKey.Value.Modifiers.GetEnumerator().MoveNext())
                ActivityLogKey.Value = new KeyboardShortcut(KeyCode.F10);
            ActivityLogVisible = file.Bind("4 - Keys", "ActivityLogVisible", false,
                "Remember whether the activity log is shown. Local only, not synced. Changed by the F10 panel checkbox.");
            DisplayRangeKey = file.Bind("4 - Keys", "DisplayRangeKey", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt),
                "Look at a Storage Display and press to set that board's chest-scan range (5–50 m). Per display.");
            BuildGrabKey = file.Bind("4 - Keys", "BuildGrabKey", new KeyboardShortcut(KeyCode.C),
                "Hold this key while confirming a hammer place to grab that piece's materials from nearby chests (nothing is placed). Default C — Shift stays free for no-snap. Local, not synced.");
            FeedTroughEnabled = file.Bind("5 - Feed Trough", "FeedTroughEnabled", true,
                "If enabled, the Feed Trough hammer piece is available. Hungry tames walk to it and eat matching food, like drops on the ground. Synced from server when LockConfig is on.");
            FeedTroughRange = file.Bind("5 - Feed Trough", "FeedTroughRange", 8f, new ConfigDescription(
                "How far (meters) hungry animals notice a Feed Trough and walk to it. Also limited by each animal's own food-search range. Synced from server when LockConfig is on.", range));
            // SAC-CATCHUP
            CatchUpEnabled = file.Bind("3 - Craft", "CatchUpWhileAway", false,
                "Kilns / smelters / blast furnaces / windmills / spinning wheels / eitr refineries with auto-fill (B) AND auto-store (N) catch up the time their zone was unloaded (nobody nearby). When someone comes back, ore + fuel are taken from the chests and the finished items are put into a chest, in packages of one stack. Only time after switching this on counts. Synced from server when LockConfig is on.");
            CatchUpMaxHours = file.Bind("3 - Craft", "CatchUpMaxHours", 8f, new ConfigDescription(
                "Most game time (hours) one station catches up after being unloaded. Synced from server when LockConfig is on.",
                new AcceptableValueRange<float>(0.5f, 48f)));
            CatchUpSince = file.Bind("3 - Craft", "CatchUpSince", 0d,
                "Set automatically: world time (seconds) when CatchUpWhileAway was switched on. Time before this is never caught up. Do not edit.");
            TorchAutoFillDefault = file.Bind("3 - Craft", "TorchAutoFillDefault", false,
                "Auto-fill state of torches (standing, wall, green / blue / mist, modded *torch*) that are placed from now on. On = new torches start with auto-fill ON, off = B needed. Placed torches are never changed by this. B on a torch still switches it. Synced from server when LockConfig is on.");
            TorchAutoFillOverride = file.Bind("3 - Craft", "TorchAutoFillOverride", false,
                "World Override: while on, auto-fill is active on EVERY torch, including already placed ones, whatever their own B setting says. The torches' own setting is not changed, so switching this off brings it back. Synced from server when LockConfig is on.");

            ShowReleaseNews = file.Bind("1 - General", "ShowReleaseNews", true,
                "After an update, Hugin (the raven) visits once and tells you what is new in StoreAndCraft. Local only, not synced.");
            ArmorStandSwap = file.Bind("1 - General", "ArmorStandSwap", true,
                "Armor stand: [E] swaps the armor on the stand with the armor you wear (helmet, chest, legs, cape) instead of dropping it on the ground. Off = vanilla. Synced from server when LockConfig is on.");
            ShowBagSortButtons = file.Bind("1 - General", "ShowBagSortButtons", true,
                "Show the Stack and Sort buttons on top of your inventory. Local only, not synced.");
            ShowChestSortButtons = file.Bind("1 - General", "ShowChestSortButtons", true,
                "Show the Stack and Sort buttons next to Take all on the chest panel. Local only, not synced.");
            ShowStationNotices = file.Bind("1 - General", "ShowStationNotices", true,
                "Show the notice above a station when its output has nowhere to go: Chest full, No linked chest, No chest nearby. Only the text: a smelter still waits while its chest is full. Local only, not synced.");
            GroundIgnoreItems = file.Bind("2 - Store", "GroundIgnoreItems", "",
                "Items auto-store never picks up from the ground (e.g. eggs). Item names separated by commas, set in the F10 panel. Local only, not synced.");
            RestockItems = file.Bind("2 - Store", "RestockItems", "",
                "Items of the keep list that the restock key fills up to their amount from nearby chests. Item names separated by commas, set in the F10 panel (the Fill box of a tile). Local only, not synced.");
            RestockKey = file.Bind("4 - Keys", "RestockKey", new KeyboardShortcut(KeyCode.Alpha0),
                "Hotkey: bring the items marked Fill in the keep list (F10) up to their amount from nearby chests. Chests set to Ignore or Only stations are skipped, the carry weight is not a limit. Local only, not synced.");
            KeepInBagItems = file.Bind("2 - Store", "KeepInBagItems", "",
                "Items dump leaves in your inventory: name=amount separated by commas (e.g. $item_wood=10), set in the F10 panel. Only the dump key, not middle-click. Local only, not synced.");
            ReleaseNewsSeen = file.Bind("1 - General", "ReleaseNewsSeen", "",
                "Set automatically: the StoreAndCraft version whose news Hugin already told you. Do not edit.");

            var cap = new AcceptableValueRange<float>(0f, MaxStationCap);
            const string capNote = " 0 = vanilla, otherwise 1-50. Applied live to every station. Synced from server when LockConfig is on.";
            KilnMaxWood = file.Bind("6 - Stations", "KilnMaxWood", 0f, new ConfigDescription(
                "Charcoal kiln: most wood in the queue." + capNote, cap));
            SmelterMaxOre = file.Bind("6 - Stations", "SmelterMaxOre", 0f, new ConfigDescription(
                "Smelter: most ore in the queue." + capNote, cap));
            SmelterMaxCoal = file.Bind("6 - Stations", "SmelterMaxCoal", 0f, new ConfigDescription(
                "Smelter: most coal." + capNote, cap));
            BlastFurnaceMaxOre = file.Bind("6 - Stations", "BlastFurnaceMaxOre", 0f, new ConfigDescription(
                "Blast furnace: most ore in the queue." + capNote, cap));
            BlastFurnaceMaxCoal = file.Bind("6 - Stations", "BlastFurnaceMaxCoal", 0f, new ConfigDescription(
                "Blast furnace: most coal." + capNote, cap));
            EitrRefineryMaxInput = file.Bind("6 - Stations", "EitrRefineryMaxInput", 0f, new ConfigDescription(
                "Eitr refinery: most soft tissue in the queue." + capNote, cap));
            EitrRefineryMaxSap = file.Bind("6 - Stations", "EitrRefineryMaxSap", 0f, new ConfigDescription(
                "Eitr refinery: most sap." + capNote, cap));
            BeehiveMaxHoney = file.Bind("6 - Stations", "BeehiveMaxHoney", 0f, new ConfigDescription(
                "Beehive: most honey it collects before it is full." + capNote, cap));
            FermenterBatch = file.Bind("6 - Stations", "FermenterBatch", 5f, new ConfigDescription(
                "Fermenter: how many of the same mead base fit in one batch. More can be added only in the first 60 seconds; all finish in the time of one. 1 = vanilla. Synced from server when LockConfig is on.",
                new AcceptableValueRange<float>(1f, MaxStationCap)));
        }

        public float StationPullRange()
        {
            return Mathf.Max(CraftRange.Value, AutoFillRange.Value);
        }

        /// <summary>Station → chest reach for auto-fill / auto-store: the whole loaded area when AutomationLoadedArea is on.</summary>
        public float AutoFillChestReach()
        {
            return AutomationLoadedArea.Value ? LoadedAreaReach : AutoFillChestMeters();
        }

        /// <summary>Station → chest range in meters as set (0 falls back to AutoFillRange). Network checks use this.</summary>
        public float AutoFillChestMeters()
        {
            float chest = AutoFillChestRange.Value;
            return chest > 0f ? chest : AutoFillRange.Value;
        }

        /// <summary>Player → scarecrow reach (0 falls back to AutoFillRange).</summary>
        public float ScarecrowReach()
        {
            return ScarecrowRange.Value > 0f ? ScarecrowRange.Value : AutoFillRange.Value;
        }

        /// <summary>Scarecrow → chest reach for its seeds and harvest: the whole loaded area when AutomationLoadedArea is on.</summary>
        public float ScarecrowChestReach()
        {
            return AutomationLoadedArea.Value ? LoadedAreaReach : ScarecrowChestMeters();
        }

        /// <summary>Scarecrow → chest range in meters as set (0 falls back to the station chest range). Network checks use this.</summary>
        public float ScarecrowChestMeters()
        {
            return ScarecrowChestRange.Value > 0f ? ScarecrowChestRange.Value : AutoFillChestMeters();
        }

        // Player → chest reaches the features use: the whole loaded area when AutomationLoadedArea is on,
        // else the slider value. Network checks (TransferService) keep the slider values plus the loaded-area check.
        public float DumpReach() { return Reach(PlayerDumpRange); }
        public float CraftReach() { return Reach(CraftRange); }
        public float StorageReach() { return Reach(StorageRange); }
        public float StoreReach() { return Reach(StoreRange); }

        private float Reach(ConfigEntry<float> meters)
        {
            return AutomationLoadedArea.Value ? LoadedAreaReach : meters.Value;
        }

        /// <summary>
        /// Distance that counts as "the whole loaded area". Valheim loads a square of zones around the player (up to
        /// about 180 m from the player to a corner, the square is about 190 m wide), so two loaded points (station and
        /// chest) can be up to about 270 m apart: 400 m covers it with room to spare.
        /// </summary>
        public const float LoadedAreaReach = 400f;

        /// <summary>Player → station reach for auto-fill: the whole loaded area when AutomationLoadedArea is on.</summary>
        public float AutoFillPlayerReach()
        {
            return AutomationLoadedArea.Value ? LoadedAreaReach : AutoFillRange.Value;
        }

        /// <summary>Player → scarecrow reach: the whole loaded area when AutomationLoadedArea is on.</summary>
        public float ScarecrowPlayerReach()
        {
            return AutomationLoadedArea.Value ? LoadedAreaReach : ScarecrowReach();
        }

        /// <summary>Farthest player → chest distance auto-fill can need (player → station / scarecrow → chest).</summary>
        public float AutoFillPlayerToChestRange()
        {
            return Mathf.Max(AutoFillRange.Value + AutoFillChestMeters(), ScarecrowReach() + ScarecrowChestMeters());
        }

        public float MaxGameplayRange()
        {
            return Mathf.Max(
                PlayerDumpRange.Value,
                StoreRange.Value,
                StorageRange.Value,
                DisplayRange.Value,
                CraftRange.Value,
                AutoFillRange.Value,
                ScarecrowReach(),
                FeedTroughRange.Value);
        }

        public void WriteToPackage(ZPackage pkg)
        {
            pkg.Write(LockConfig.Value);
            pkg.Write(ModEnabled.Value);
            pkg.Write(StoreEnabled.Value);
            pkg.Write(AutoIntakeEnabled.Value);
            pkg.Write(CraftEnabled.Value);
            pkg.Write(MustHaveExisting.Value);
            pkg.Write(LeaveOneItem.Value);
            pkg.Write(PlayerDumpRange.Value);
            pkg.Write(StoreRange.Value);
            pkg.Write(StorageRange.Value);
            pkg.Write(DisplayRange.Value);
            pkg.Write(AutoStackEnabled.Value);
            pkg.Write(CraftRange.Value);
            pkg.Write(AutoFillRange.Value);
            pkg.Write(IntakeInterval.Value);
            pkg.Write(MaxTransfersPerTick.Value);
            pkg.Write(FeedTroughEnabled.Value);
            pkg.Write(FeedTroughRange.Value);
            pkg.Write(AutoFillChestRange.Value);
            pkg.Write(CatchUpEnabled.Value);
            pkg.Write(CatchUpMaxHours.Value);
            pkg.Write(CatchUpSince.Value);
            pkg.Write(TorchAutoFillDefault.Value);
            // v20
            pkg.Write(KilnMaxWood.Value);
            pkg.Write(SmelterMaxOre.Value);
            pkg.Write(SmelterMaxCoal.Value);
            pkg.Write(BlastFurnaceMaxOre.Value);
            pkg.Write(BlastFurnaceMaxCoal.Value);
            pkg.Write(EitrRefineryMaxInput.Value);
            pkg.Write(EitrRefineryMaxSap.Value);
            pkg.Write(BeehiveMaxHoney.Value);
            pkg.Write(FermenterBatch.Value);
            // v21
            pkg.Write(TorchAutoFillOverride.Value);
            pkg.Write(ArmorStandSwap.Value);
            // Appended later, same protocol: a reader only takes them when they are there (an older server sends less).
            pkg.Write(ScarecrowRange.Value);
            pkg.Write(ScarecrowChestRange.Value);
            // v22
            pkg.Write(AutomationLoadedArea.Value);
        }

        public void ReadFromPackage(ZPackage pkg)
        {
            LockConfig.Value = pkg.ReadBool();
            ModEnabled.Value = pkg.ReadBool();
            StoreEnabled.Value = pkg.ReadBool();
            AutoIntakeEnabled.Value = pkg.ReadBool();
            CraftEnabled.Value = pkg.ReadBool();
            MustHaveExisting.Value = pkg.ReadBool();
            LeaveOneItem.Value = pkg.ReadBool();
            PlayerDumpRange.Value = pkg.ReadSingle();
            StoreRange.Value = pkg.ReadSingle();
            StorageRange.Value = pkg.ReadSingle();
            DisplayRange.Value = pkg.ReadSingle();
            AutoStackEnabled.Value = pkg.ReadBool();
            CraftRange.Value = pkg.ReadSingle();
            AutoFillRange.Value = pkg.ReadSingle();
            IntakeInterval.Value = pkg.ReadSingle();
            MaxTransfersPerTick.Value = pkg.ReadInt();
            FeedTroughEnabled.Value = pkg.ReadBool();
            FeedTroughRange.Value = pkg.ReadSingle();
            AutoFillChestRange.Value = pkg.ReadSingle();
            CatchUpEnabled.Value = pkg.ReadBool();
            CatchUpMaxHours.Value = pkg.ReadSingle();
            CatchUpSince.Value = pkg.ReadDouble();
            TorchAutoFillDefault.Value = pkg.ReadBool();
            // v20
            KilnMaxWood.Value = pkg.ReadSingle();
            SmelterMaxOre.Value = pkg.ReadSingle();
            SmelterMaxCoal.Value = pkg.ReadSingle();
            BlastFurnaceMaxOre.Value = pkg.ReadSingle();
            BlastFurnaceMaxCoal.Value = pkg.ReadSingle();
            EitrRefineryMaxInput.Value = pkg.ReadSingle();
            EitrRefineryMaxSap.Value = pkg.ReadSingle();
            BeehiveMaxHoney.Value = pkg.ReadSingle();
            FermenterBatch.Value = pkg.ReadSingle();
            // v21
            TorchAutoFillOverride.Value = pkg.ReadBool();
            ArmorStandSwap.Value = pkg.ReadBool();
            // Appended later, same protocol (an older server sends less, an older client ignores the rest).
            if (pkg.GetPos() < pkg.Size())
                ScarecrowRange.Value = pkg.ReadSingle();
            if (pkg.GetPos() < pkg.Size())
                ScarecrowChestRange.Value = pkg.ReadSingle();
            // v22
            if (pkg.GetPos() < pkg.Size())
                AutomationLoadedArea.Value = pkg.ReadBool();
        }
    }
}
