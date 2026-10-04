using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.morda.storeandcraft";
        public const string ModName = "StoreAndCraft";
        public const string ModVersion = "1.4.2";
        public const string ModAuthor = "Morda";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal static ModConfig Settings { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Settings = new ModConfig(Config);
            try
            {
                Config.Save();
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("Config save: " + ex.Message);
            }

            Config.SettingChanged += OnSettingChanged;
            ConfigWatch.Start();
            Favorites.Load();
            ActivityLog.LoadFromConfig();

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            EpicLootBridge.TryRegister();

            Logger.LogInfo(ModName + " v" + ModVersion + " by " + ModAuthor + " loaded.");
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            // Station capacities: re-apply once next frame (also for values synced from the server).
            StationCaps.MarkDirty();
            if (ConfigSync.IsApplyingRemoteConfig)
                return;
            if (!AdminUtil.IsServer())
                return;
            ConfigSync.BroadcastConfig();
        }

        private void Update()
        {
            ConfigWatch.Tick();
            StationCaps.Tick();
            TransferService.Tick();
            SmelterCatchUp.ServerTick(); // SAC-CATCHUP

            if (ZNet.instance != null && ZNet.instance.IsDedicated())
            {
                AutoIntake.TickDedicated();
                return;
            }

            if (Player.m_localPlayer == null)
                return;

            NearbyIndex.Tick();
            PendingChestDebit.Tick();
            ReleaseNews.Tick();
            // Listen host: intake around every player, not only the host.
            // Clients still run local intake so they can RPC drops they own.
            if (ZNet.instance != null && ZNet.instance.IsServer())
                AutoIntake.TickDedicated();
            else
                AutoIntake.Tick();
            AutoStack.Tick();
            SearchPing.Tick();
            // Mouse-down edge for display scale/mode — must not wait for LateUpdate.
            StorageDisplayBoard.TickCycleInput();
            StorageDisplayBoard.TickLocateInput();
            DisplayTypeMenu.Tick();
            DisplayRangeMenu.Tick();
            DisplaySmallOptions.Tick();
            StationFilterMenu.Tick();
            SettingsPanel.Tick();
            ModManagerPanel.Tick();
            StationAutoFill.Tick();
        }

        private void LateUpdate()
        {
            // Never leave chest-counting enabled across frames (inventory FPS collapse).
            StationHover.ResetFrame();
            // Nested station Push/Pop must not leak ActiveId into the next frame.
            StationLink.ResetFrame();

            if (Player.m_localPlayer == null)
                return;

            Hotkeys.Tick();
            // Only turns existing missing-labels to the camera (returns at once when none).
            StationMissingLabel.Tick();
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            EpicLootBridge.TryUnregister();
            _harmony?.UnpatchSelf();
            if (Instance == this)
                Instance = null;
        }
    }
}
