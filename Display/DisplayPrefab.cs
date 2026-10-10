using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    internal enum DisplayKind
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    internal static class DisplayPrefab
    {
        public const string MediumName = "sac_storage_display";
        public const string SmallName = "sac_storage_display_small";
        public const string LargeName = "sac_storage_display_large";

        public const string SmallCarvedName = "sac_storage_display_small_carved";
        public const string SmallWideName = "sac_storage_display_small_wide";
        public const string MediumCarvedName = "sac_storage_display_carved";
        public const string LargeCarvedName = "sac_storage_display_large_carved";
        // Horizontal carved boards are their own hammer pieces (no Classic/Compact mesh swap).
        public const string MediumCarvedWideName = "sac_storage_display_carved_wide";
        public const string LargeCarvedWideName = "sac_storage_display_large_carved_wide";

        /// <summary>Wood chest piece: hammer category / usage for all storage displays (Storage tab).</summary>
        private static Piece _chestPiece;

        private static readonly Dictionary<int, GameObject> ByHash = new Dictionary<int, GameObject>();
        private static readonly List<GameObject> AllPrefabs = new List<GameObject>();
        private static GameObject _hide;
        private static bool _busy;

        public static bool IsDisplay(Piece piece)
        {
            return piece != null && piece.GetComponent<StorageDisplayBoard>() != null;
        }

        public static GameObject PrefabForHash(int hash)
        {
            GameObject go;
            return ByHash.TryGetValue(hash, out go) ? go : null;
        }

        public static void RegisterForScene(ZNetScene scene)
        {
            if (scene == null || _busy)
                return;

            _busy = true;
            try
            {
                RegisterScene(scene);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("StoreAndCraft storage display skipped: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        public static void TryRegisterHammer()
        {
            try
            {
                RegisterHammer();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Storage display hammer: " + ex.Message);
            }
        }

        private static void RegisterScene(ZNetScene scene)
        {
            if (scene == null)
                return;

            if (AllPrefabs.Count > 0)
            {
                for (int i = 0; i < AllPrefabs.Count; i++)
                    EnsureNamed(scene, AllPrefabs[i]);
                return;
            }

            GameObject sign = scene.GetPrefab("sign") ?? scene.GetPrefab("Sign");
            if (sign == null)
            {
                Plugin.Log.LogWarning("StoreAndCraft: wood sign prefab not found, storage display skipped.");
                return;
            }

            GameObject woodChest = scene.GetPrefab("piece_chest_wood");
            _chestPiece = woodChest != null ? woodChest.GetComponent<Piece>() : null;

            if (_hide == null)
            {
                _hide = new GameObject("sac_display_hide");
                Object.DontDestroyOnLoad(_hide);
                _hide.SetActive(false);
            }

            // Vanilla wood signs (always available).
            BuildVariant(scene, sign, SmallName, DisplayKind.Small, null,
                "Small Storage Display",
                "Shows one category or up to 4 items from nearby chests. Press [E] to select.",
                Vector3.one);

            BuildVariant(scene, sign, MediumName, DisplayKind.Medium, null,
                "Medium Storage Display",
                "Shows nearby chest totals by category. Press [E] and click types, Shift+RMB changes the layout.",
                new Vector3(2.5f, 2.1f, 1f));

            BuildVariant(scene, sign, LargeName, DisplayKind.Large, null,
                "Large Storage Display",
                "Wide board with flowing category sections from nearby chests. Press [E] and click types.",
                new Vector3(7.5f, 6.3f, 1f));

            // Carved Unity boards — hidden until the prefabs are ready for players.
            if (CarvedDisplaysExposed && DisplayVisual.Available("small_vertical"))
            {
                BuildVariant(scene, sign, SmallCarvedName, DisplayKind.Small, "small_vertical",
                    "Small Storage Display (Carved)",
                    "Carved hanging board. Shows one category or up to 4 items from nearby chests. Press [E] to select.",
                    Vector3.one);

                BuildVariant(scene, sign, SmallWideName, DisplayKind.Small, "small_horizontal",
                    "Small Wide Storage Display (Carved)",
                    "Wide carved hanging board. Shows one category or up to 4 items from nearby chests. Press [E] to select.",
                    Vector3.one);

                BuildVariant(scene, sign, MediumCarvedName, DisplayKind.Medium, "medium_vertical",
                    "Medium Storage Display (Carved)",
                    "Carved board. Shows nearby chest totals by category. Press [E] and click types, Shift+RMB changes the layout.",
                    Vector3.one);

                if (DisplayVisual.Available("medium_horizontal"))
                    BuildVariant(scene, sign, MediumCarvedWideName, DisplayKind.Medium, "medium_horizontal",
                        "Medium Wide Storage Display (Carved)",
                        "Wide carved board. Shows nearby chest totals by category. Press [E] and click types, Shift+RMB changes the layout.",
                        Vector3.one);

                BuildVariant(scene, sign, LargeCarvedName, DisplayKind.Large, "large_vertical",
                    "Large Storage Display (Carved)",
                    "Large carved board with category sections. Press [E] and click types.",
                    Vector3.one);

                if (DisplayVisual.Available("large_horizontal"))
                    BuildVariant(scene, sign, LargeCarvedWideName, DisplayKind.Large, "large_horizontal",
                        "Large Wide Storage Display (Carved)",
                        "Large wide carved board with category sections. Press [E] and click types.",
                        Vector3.one);

                Plugin.Log.LogDebug("StoreAndCraft storage displays registered (vanilla + carved).");
            }
            else
            {
                Plugin.Log.LogDebug("StoreAndCraft storage displays registered (vanilla only).");
            }
        }

        /// <summary>Carved Unity boards in the hammer (Storage tab). Needs sac_displays next to the DLL.</summary>
        private static bool CarvedDisplaysExposed => true;

        /// <summary>
        /// Carved piece name → visual base (same mapping as the BuildVariant calls above).
        /// Instantiate does not copy StorageDisplayBoard.VisualBase (internal field, not
        /// serialized), so ghosts, placed and loaded boards re-derive it from their name.
        /// </summary>
        internal static string VisualBaseForPrefab(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;
            string n = objectName.Replace("(Clone)", "").Trim();
            if (n == SmallCarvedName)
                return "small_vertical";
            if (n == SmallWideName)
                return "small_horizontal";
            // Fixed mesh per piece (was "medium"/"large" + Classic/Compact swap). Boards already
            // placed as medium/large carved now always show the vertical mesh.
            if (n == MediumCarvedName)
                return "medium_vertical";
            if (n == MediumCarvedWideName)
                return "medium_horizontal";
            if (n == LargeCarvedName)
                return "large_vertical";
            if (n == LargeCarvedWideName)
                return "large_horizontal";
            return null;
        }

        /// <summary>
        /// Vanilla-sign display → layout definition id (sign_small / sign_medium / sign_large).
        /// Signs have no Unity mesh; with a v2 definition they show its layouts like carved boards.
        /// </summary>
        internal static string SignLayoutForPrefab(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;
            string n = objectName.Replace("(Clone)", "").Trim();
            if (n == SmallName)
                return "sign_small";
            if (n == MediumName)
                return "sign_medium";
            if (n == LargeName)
                return "sign_large";
            return null;
        }

        private static void BuildVariant(
            ZNetScene scene,
            GameObject sign,
            string prefabName,
            DisplayKind kind,
            string visualBase,
            string pieceName,
            string pieceDesc,
            Vector3 scaleMul)
        {
            bool wasActive = sign.activeSelf;
            sign.SetActive(false);
            GameObject clone = Object.Instantiate(sign, _hide.transform);
            sign.SetActive(wasActive);

            clone.name = prefabName;
            clone.SetActive(true);

            ZNetView znv = clone.GetComponent<ZNetView>();
            if (znv == null)
            {
                Plugin.Log.LogWarning("StoreAndCraft: storage display clone lost ZNetView (" + prefabName + ").");
                Object.Destroy(clone);
                return;
            }

            znv.m_persistent = true;
            // Keep distant sync closer to normal furniture — scaled signs otherwise stay visible too far.
            znv.m_distant = false;

            bool custom = !string.IsNullOrEmpty(visualBase) && DisplayVisual.Available(visualBase);
            if (custom)
                CarvedVisual[clone] = visualBase;
            clone.transform.localScale = custom
                ? Vector3.one
                : Vector3.Scale(clone.transform.localScale, scaleMul);

            Piece piece = clone.GetComponent<Piece>();
            Piece source = sign.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = pieceName;
                piece.m_description = pieceDesc;
                if (piece.m_placeEffect == null && source != null)
                    piece.m_placeEffect = source.m_placeEffect;
                if (piece.m_placeEffect == null)
                    piece.m_placeEffect = new EffectList { m_effectPrefabs = new EffectList.EffectData[0] };
                if (piece.m_resources == null && source != null)
                    piece.m_resources = source.m_resources;
                // Carved boards have their own recipe (the vanilla signs keep the sign recipe).
                if (custom)
                    ApplyCarvedRecipe(piece, visualBase);
                // Hammer: Storage tab, next to the chests (same as the feed trough).
                if (_chestPiece != null)
                {
                    piece.m_category = _chestPiece.m_category;
                    piece.m_usage = _chestPiece.m_usage;
                }
                // Carved boards: own hammer icon (Content/UI/icon_display_<visual>.png); else keep sign icon.
                if (custom)
                {
                    Sprite icon = UiAssets.Get("icon_display_" + visualBase + ".png");
                    if (icon != null)
                        piece.m_icon = icon;
                }
            }

            StorageDisplayBoard board = clone.GetComponent<StorageDisplayBoard>();
            if (board == null)
                board = clone.AddComponent<StorageDisplayBoard>();
            board.Configure(kind);
            board.VisualBase = visualBase;
            if (string.IsNullOrEmpty(visualBase))
                board.SignLayout = SignLayoutForPrefab(prefabName);
            if (custom)
            {
                // Full layout on the hammer template too: Valheim placement uses this
                // prefab's colliders/snaps for the ghost. Mesh-only left the vanilla
                // sign box at the feet (build only at bottom center).
                DisplayVisual.Ensure(board, templateMeshOnly: false);
            }
            else
            {
                // Vanilla wood signs: thin Medium/Large depth without authored YAML collider.
                SoftenColliders(clone, kind);
            }

            int hash = prefabName.GetStableHashCode();
            ByHash[hash] = clone;
            AllPrefabs.Add(clone);
            EnsureNamed(scene, clone);
        }

        /// <summary>
        /// PrefabStudio preview only: re-apply collider / snap / ItemGrid layout on the hammer
        /// templates of this visual (same Ensure path as registration), then rebuild the
        /// placement ghost so a new piece uses the new snaps / collider right away.
        /// </summary>
        internal static void RefreshTemplates(string visualId)
        {
            if (string.IsNullOrEmpty(visualId))
                return;
            bool any = false;
            for (int i = 0; i < AllPrefabs.Count; i++)
            {
                GameObject prefab = AllPrefabs[i];
                StorageDisplayBoard board = prefab != null ? prefab.GetComponent<StorageDisplayBoard>() : null;
                if (board == null
                    || !string.Equals(board.CurrentVisualId(), visualId, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                board.AppliedLayoutGen = 0;
                DisplayVisual.Ensure(board, templateMeshOnly: false);
                any = true;
            }
            if (any)
                RefreshPlacementGhost();
        }

        /// <summary>
        /// Player.SetupPlacementGhost is private; it re-instantiates the ghost from the selected
        /// (now updated) prefab. Harmless when nothing is selected. Used by display + trough preview.
        /// </summary>
        internal static void RefreshPlacementGhost()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;
            try
            {
                System.Reflection.MethodInfo setup = HarmonyLib.AccessTools.Method(typeof(Player), "SetupPlacementGhost");
                setup?.Invoke(player, null);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Display preview: placement ghost refresh skipped: " + ex.Message);
            }
        }

        /// <summary>
        /// Large/Medium signs scale into full walls. Keep solid colliders (placement needs them)
        /// but shrink depth so they are harder to stand on / use as traps.
        /// Do not use triggers — that breaks PlacePiece (mats eaten, nothing built).
        /// </summary>
        private static void SoftenColliders(GameObject clone, DisplayKind kind)
        {
            if (clone == null || kind == DisplayKind.Small)
                return;

            // Local Z thickness after root scale: keep a real solid slab, just thinner.
            float depth = kind == DisplayKind.Large ? 0.04f : 0.06f;

            Collider[] cols = clone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider col = cols[i];
                if (col == null)
                    continue;

                // Never destroy — Destroy() on a prefab breaks later PlacePiece/GetPrefab use.
                BoxCollider box = col as BoxCollider;
                if (box != null)
                {
                    Vector3 size = box.size;
                    // Prefer thinning the shallowest axis (sign face depth).
                    if (size.z <= size.x && size.z <= size.y)
                        size.z = Mathf.Min(size.z, depth);
                    else if (size.x <= size.y)
                        size.x = Mathf.Min(size.x, depth);
                    else
                        size.y = Mathf.Min(size.y, depth);
                    box.size = size;
                    box.isTrigger = false;
                    continue;
                }

                // Mesh colliders stay as-is (needed for placement); cannot safely thin them.
            }
        }

        // Carved board prefab -> visual id (recipe is set again once ObjectDB exists).
        private static readonly Dictionary<GameObject, string> CarvedVisual = new Dictionary<GameObject, string>();
        private static readonly HashSet<GameObject> _carvedRecipeSet = new HashSet<GameObject>();

        /// <summary>Recipe of a carved board by visual id; null for anything else (signs keep theirs).</summary>
        private static List<Piece.Requirement> CarvedRecipe(string visualBase)
        {
            int wood, tar, leather, nails;
            int chain = 2;
            switch (visualBase)
            {
                case "large_vertical": wood = 10; tar = 3; leather = 5; nails = 2; break;
                case "large_horizontal": wood = 10; tar = 3; leather = 5; nails = 4; break;
                case "medium_vertical":
                case "medium_horizontal": wood = 6; tar = 2; leather = 3; nails = 2; break;
                case "small_vertical":
                case "small_horizontal": wood = 1; tar = 1; leather = 1; nails = 0; break;
                default: return null;
            }
            var list = new List<Piece.Requirement>();
            ScarecrowPrefab.AddReq(list, "Wood", wood);
            ScarecrowPrefab.AddReq(list, "Tar", tar);
            ScarecrowPrefab.AddReq(list, "LeatherScraps", leather);
            ScarecrowPrefab.AddReq(list, "Chain", chain);
            if (nails > 0)
                ScarecrowPrefab.AddReq(list, "BronzeNails", nails);
            return list;
        }

        private static void ApplyCarvedRecipe(Piece piece, string visualBase)
        {
            if (piece == null || ObjectDB.instance == null)
                return; // RegisterHammer applies it as soon as ObjectDB exists
            List<Piece.Requirement> list = CarvedRecipe(visualBase);
            if (list != null && list.Count > 0)
                piece.m_resources = list.ToArray();
        }

        private static void RegisterHammer()
        {
            if (AllPrefabs.Count == 0 || ObjectDB.instance == null)
                return;

            foreach (KeyValuePair<GameObject, string> kv in CarvedVisual)
            {
                Piece carved = kv.Key != null ? kv.Key.GetComponent<Piece>() : null;
                if (carved != null && !_carvedRecipeSet.Contains(kv.Key))
                {
                    ApplyCarvedRecipe(carved, kv.Value);
                    _carvedRecipeSet.Add(kv.Key);
                }
            }

            GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            if (hammer == null)
                return;
            ItemDrop drop = hammer.GetComponent<ItemDrop>();
            PieceTable table = drop != null && drop.m_itemData?.m_shared != null
                ? drop.m_itemData.m_shared.m_buildPieces
                : null;
            if (table == null || table.m_pieces == null)
                return;

            for (int i = 0; i < AllPrefabs.Count; i++)
            {
                GameObject prefab = AllPrefabs[i];
                if (prefab != null && !table.m_pieces.Contains(prefab))
                    table.m_pieces.Add(prefab);
            }
        }

        private static void EnsureNamed(ZNetScene scene, GameObject prefab)
        {
            if (prefab == null || scene == null)
                return;

            if (scene.m_prefabs != null && !scene.m_prefabs.Contains(prefab))
                scene.m_prefabs.Add(prefab);

            FieldInfo named = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
            var map = named != null ? named.GetValue(scene) as Dictionary<int, GameObject> : null;
            if (map != null)
                map[prefab.name.GetStableHashCode()] = prefab;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneDisplayPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            DisplayPrefab.RegisterForScene(__instance);
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartDisplayPatch
    {
        private static void Postfix()
        {
            if (ZNetScene.instance != null)
                DisplayPrefab.RegisterForScene(ZNetScene.instance);
            DisplayPrefab.TryRegisterHammer();
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerDisplayTablePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;
            if (ZNetScene.instance != null)
                DisplayPrefab.RegisterForScene(ZNetScene.instance);
            DisplayPrefab.TryRegisterHammer();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PlaceDisplayPatch
    {
        private static void Prefix(Piece piece, ref bool cheated)
        {
            if (cheated && DisplayPrefab.IsDisplay(piece))
                cheated = false;
        }

        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception == null)
                return null;
            Plugin.Log.LogWarning("PlacePiece failed: " + __exception.Message);
            return null;
        }
    }

    [HarmonyPatch(typeof(Sign), "UpdateText")]
    internal static class SignDisplayUpdateTextPatch
    {
        private static bool Prefix(Sign __instance)
        {
            return __instance == null || __instance.GetComponent<StorageDisplayBoard>() == null;
        }
    }

    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    internal static class SignDisplayInteractPatch
    {
        private static bool Prefix(Sign __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold || __instance == null)
                return true;
            StorageDisplayBoard board = __instance.GetComponent<StorageDisplayBoard>();
            if (board == null)
                return true;
            __result = board.TryOpenMenu(character, alt);
            return false;
        }
    }

    /// <summary>
    /// Shift + hover display: block the swing. Scale/mode cycle runs on mouse-down in TickCycleInput only.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class HumanoidDisplayScaleAttackPatch
    {
        private static bool Prefix(Humanoid __instance, bool secondaryAttack)
        {
            if (__instance == null || __instance != Player.m_localPlayer)
                return true;
            // Block primary (scale) and secondary (layout) while Shift+hovering a display.
            if (secondaryAttack && StorageDisplayBoard.ShouldBlockSecondaryForLocate())
                return false; // plain right click on a display item: locate its chest instead
            return !StorageDisplayBoard.ShouldBlockAttackForCycle();
        }
    }

    [HarmonyPatch(typeof(Sign), nameof(Sign.GetHoverText))]
    internal static class SignDisplayHoverPatch
    {
        private static void Postfix(Sign __instance, ref string __result)
        {
            StorageDisplayBoard board = __instance.GetComponent<StorageDisplayBoard>();
            if (board == null)
                return;
            __result = board.HoverLabel();
        }
    }

    [HarmonyPatch(typeof(Player), "UseHotbarItem")]
    internal static class PlayerHotbarDisplayPatch
    {
        private static bool Prefix(Player __instance, int index)
        {
            return !StorageDisplayBoard.TryAssignFromHotbar(__instance, index);
        }
    }
}
