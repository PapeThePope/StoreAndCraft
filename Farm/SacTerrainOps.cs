using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Two own terrain operations for the scarecrow buttons, registered next to Valheim's TerrainOps
    /// (ObjectDB.m_terrainOpsByHash, looked up by prefab name hash on every client):
    /// level = the height point at the operation is set exactly to the operation's height, paint dirt;
    /// cultivate = paint cultivated only, no smoothing (cultivate_v2 smooths and bends the levelled edge).
    /// Both are transient: TerrainOp applies itself to the terrain compiler and destroys itself.
    /// The mod is required on every client, so every client knows the two hashes.
    /// </summary>
    internal static class SacTerrainOps
    {
        internal const string LevelName = "sac_level_op";
        internal const string CultivateName = "sac_cultivate_op";

        private static TerrainOp _level;
        private static TerrainOp _cultivate;

        /// <summary>Inactive template: an instance stays inactive until SetActive(true), then Awake applies it.</summary>
        internal static GameObject LevelPrefab
        {
            get { Ensure(); return _level != null ? _level.gameObject : null; }
        }

        internal static GameObject CultivatePrefab
        {
            get { Ensure(); return _cultivate != null ? _cultivate.gameObject : null; }
        }

        private static void Ensure()
        {
            if (_level == null)
            {
                _level = Make(LevelName);
                TerrainOp.Settings s = _level.m_settings;
                s.m_level = true;
                s.m_levelRadius = 0f;      // exactly one height point per operation
                s.m_square = true;
                s.m_levelOffset = 0f;      // the operation's own height is the target height
                s.m_paintCleared = true;
                s.m_paintType = TerrainModifier.PaintType.Dirt;
                s.m_paintRadius = 1f;      // only the point itself (the neighbours are 1 m away = no paint)
            }
            if (_cultivate == null)
            {
                _cultivate = Make(CultivateName);
                TerrainOp.Settings s = _cultivate.m_settings;
                s.m_level = false;
                s.m_smooth = false;
                s.m_paintCleared = true;
                s.m_paintType = TerrainModifier.PaintType.Cultivate;
                s.m_paintRadius = 1f;
            }
        }

        private static TerrainOp Make(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            return go.AddComponent<TerrainOp>();
        }

        internal static void Register(ObjectDB db)
        {
            if (db == null || db.m_terrainOpsByHash == null)
                return;
            Ensure();
            db.m_terrainOpsByHash[LevelName.GetStableHashCode()] = _level;
            db.m_terrainOpsByHash[CultivateName.GetStableHashCode()] = _cultivate;
        }
    }

    /// <summary>ObjectDB rebuilds its terrain op table on every (re)load: add ours again.</summary>
    [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
    internal static class SacTerrainOpsRegisterPatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            SacTerrainOps.Register(__instance);
        }
    }
}
