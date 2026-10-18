using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Clones the simple wood bed, narrows it, and adds a Container so it works as a feed trough.
    /// Avoid DestroyImmediate on colliders/physics — that breaks PlacePiece (mats eaten, nothing built).
    /// </summary>
    internal static class FeedTroughPrefab
    {
        /// <summary>Horizontal scale vs simple bed (half of the earlier 0.75).</summary>
        private const float WidthScale = 0.375f;

        private static GameObject _prefab;
        private static GameObject _hide;
        private static bool _busy;
        private static readonly Dictionary<int, GameObject> ByHash = new Dictionary<int, GameObject>();

        public static GameObject PrefabForHash(int hash)
        {
            GameObject go;
            return ByHash.TryGetValue(hash, out go) ? go : null;
        }

        public static void RegisterForScene(ZNetScene scene)
        {
            if (scene == null || _busy)
                return;
            if (Plugin.Settings != null && !Plugin.Settings.FeedTroughEnabled.Value)
                return;

            _busy = true;
            try
            {
                RegisterScene(scene);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("StoreAndCraft feed trough skipped: " + ex.Message);
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
                Plugin.Log.LogDebug("Feed trough hammer: " + ex.Message);
            }
        }

        private static void RegisterScene(ZNetScene scene)
        {
            if (_prefab != null)
            {
                EnsureNamed(scene, _prefab);
                return;
            }

            // Vanilla simple bed (hammer Furniture).
            GameObject bed = scene.GetPrefab("bed");
            GameObject woodChest = scene.GetPrefab("piece_chest_wood");
            if (bed == null)
            {
                Plugin.Log.LogWarning("StoreAndCraft: bed prefab missing, feed trough skipped.");
                return;
            }

            if (_hide == null)
            {
                _hide = new GameObject("sac_feed_trough_hide");
                Object.DontDestroyOnLoad(_hide);
                _hide.SetActive(false);
            }

            bool wasActive = bed.activeSelf;
            bed.SetActive(false);
            GameObject clone = Object.Instantiate(bed, _hide.transform);
            bed.SetActive(wasActive);

            clone.name = FeedTrough.PrefabName;
            clone.SetActive(true);

            ZNetView znv = clone.GetComponent<ZNetView>();
            if (znv == null)
            {
                Plugin.Log.LogWarning("StoreAndCraft: feed trough lost ZNetView.");
                Object.Destroy(clone);
                return;
            }

            znv.m_persistent = true;
            znv.m_distant = false;

            // No sleeping on the trough — remove Bed only (do not touch colliders / original bed).
            Bed bedComp = clone.GetComponent<Bed>();
            if (bedComp != null)
                Object.DestroyImmediate(bedComp);

            // Own Unity model from sac_displays when present; else the squashed bed mesh.
            bool custom = TryAttachCustomModel(clone);
            // PrefabStudio definition (feed_trough.json): colliders / snaps / model scale.
            if (custom)
                ApplyDefinition(clone);

            // Clone-only: squash the tall headboard end down to the short foot height.
            if (!custom)
                SymmetrizeEnds(clone);

            Piece piece = clone.GetComponent<Piece>();
            if (piece == null)
            {
                Plugin.Log.LogWarning("StoreAndCraft: feed trough lost Piece.");
                Object.Destroy(clone);
                return;
            }

            piece.m_name = Loc.T("Feed Trough", "Futtertrog");
            piece.m_description = Loc.T(
                "Open it and put animal food inside (carrots, mushrooms, berries). Hungry tames walk up to it and eat, like food on the ground.",
                "Öffnen und Tierfutter hineinlegen (Karotten, Pilze, Beeren). Hungrige Zahme laufen hin und fressen, wie bei Futter auf dem Boden.");
            // Build-menu tabs are UsageTagFlags (not PieceCategory): our own "Store and Craft" tag.
            piece.m_category = Piece.PieceCategory.Furniture;
            Piece chestPiece = woodChest != null ? woodChest.GetComponent<Piece>() : null;
            if (chestPiece != null)
                piece.m_category = chestPiece.m_category;
            piece.m_usage = BuildMenuTag.UsageFor(chestPiece);
            // Not a bed for comfort.
            piece.m_comfort = 0;
            piece.m_comfortGroup = Piece.ComfortGroup.None;
            piece.m_canBeRemoved = true;
            piece.m_isUpgrade = false;
            if (piece.m_placeEffect == null)
                piece.m_placeEffect = new EffectList { m_effectPrefabs = new EffectList.EffectData[0] };

            // Hammer icon: rendered filled trough (Content/UI/icon_feed_trough.png). Fallback:
            // squeeze vanilla bed sprite (keeps Valheim shadow style, true alpha).
            Sprite own = UiAssets.Get("icon_feed_trough.png");
            Piece bedPiece = bed.GetComponent<Piece>();
            if (own != null)
                piece.m_icon = own;
            else if (bedPiece != null && bedPiece.m_icon != null)
            {
                Sprite narrow = MakeNarrowIcon(bedPiece.m_icon, WidthScale);
                if (narrow != null)
                    piece.m_icon = narrow;
            }

            // Cheaper than a full chest — wood from chest recipe / fallback.
            if (woodChest != null)
                piece.m_resources = BuildRecipe(woodChest);
            else if (piece.m_resources == null || piece.m_resources.Length == 0)
                piece.m_resources = BuildWoodFallback();

            // Inventory: copy container settings from wood chest (bed has none).
            EnsureContainer(clone, woodChest);

            if (clone.GetComponent<FeedTrough>() == null)
                clone.AddComponent<FeedTrough>();

            if (!custom)
            {
                // Half of previous 75% width → 37.5% of the bed (long thin trough). Keep length/height.
                Vector3 s = clone.transform.localScale;
                clone.transform.localScale = new Vector3(s.x * WidthScale, s.y, s.z);
                AddInteractCollider(clone);
            }

            _prefab = clone;
            ByHash[FeedTrough.PrefabName.GetStableHashCode()] = clone;
            EnsureNamed(scene, clone);
            Plugin.Log.LogDebug("StoreAndCraft feed trough registered (" + (custom ? "Unity model" : "bed base") + ").");
        }

        /// <summary>Definition id (displays/definitions/feed_trough.json, PrefabStudio F8).</summary>
        internal const string DefinitionId = "feed_trough";

        private static void ApplyDefinition(GameObject trough)
        {
            Transform empty = trough.transform.Find(ModelEmptyName);
            Transform full = trough.transform.Find(ModelFullName);
            DisplayLayouts.TryApplyPiece(trough, DefinitionId, empty, full);
        }

        /// <summary>
        /// PrefabStudio preview (DisplayLayouts.EditorPreview): re-apply on the hammer template and
        /// every loaded trough, rebuild their eat points, refresh the placement ghost.
        /// </summary>
        internal static void ApplyPreview(string id)
        {
            if (!string.Equals(id, DefinitionId, System.StringComparison.OrdinalIgnoreCase))
                return;
            if (_prefab != null && _prefab.transform.Find(ModelEmptyName) != null)
                ApplyDefinition(_prefab);
            for (int i = 0; i < FeedTrough.Live.Count; i++)
            {
                FeedTrough trough = FeedTrough.Live[i];
                if (trough == null || trough.transform.Find(ModelEmptyName) == null)
                    continue;
                ApplyDefinition(trough.gameObject);
                trough.ResetBaits();
            }
            DisplayPrefab.RefreshPlacementGhost();
        }

        /// <summary>Unity models in sac_displays: empty trough / trough with food.</summary>
        internal const string ModelEmpty = "SM_FeedTrough";
        internal const string ModelFull = "SM_FeedTroughFull";
        internal const string ModelEmptyName = "SacTroughModel";
        internal const string ModelFullName = "SacTroughModelFull";

        /// <summary>
        /// Swap the bed look for the Unity trough. Bed renderers / colliders are only disabled
        /// (never destroyed — see class note). The hit box is fitted to the model and drives the
        /// animal eat points (FeedTrough.BaitOffset). Full model starts hidden; FeedTrough toggles.
        /// </summary>
        private static bool TryAttachCustomModel(GameObject clone)
        {
            GameObject empty = DisplayVisual.LoadPrefab(ModelEmpty);
            if (empty == null)
                return false;
            GameObject full = DisplayVisual.LoadPrefab(ModelFull);

            Renderer[] bedRenderers = clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < bedRenderers.Length; i++)
            {
                if (bedRenderers[i] != null)
                    bedRenderers[i].enabled = false;
            }
            Collider[] bedColliders = clone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < bedColliders.Length; i++)
            {
                if (bedColliders[i] != null)
                    bedColliders[i].enabled = false;
            }

            GameObject emptyGo = AttachModel(clone, empty, ModelEmptyName);
            if (full != null)
            {
                GameObject fullGo = AttachModel(clone, full, ModelFullName);
                fullGo.SetActive(false);
            }

            // Hit / placement box = model bounds (clone space, root scale stays 1).
            Bounds b;
            if (!ModelBounds(clone.transform, emptyGo.transform, out b))
                b = new Bounds(new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.55f, 2.2f));
            var hit = new GameObject("SacTroughHit");
            hit.transform.SetParent(clone.transform, false);
            BoxCollider box = hit.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(0.1f, b.size.x), Mathf.Max(0.1f, b.size.y), Mathf.Max(0.1f, b.size.z));
            box.isTrigger = false;
            return true;
        }

        private static GameObject AttachModel(GameObject clone, GameObject prefab, string name)
        {
            GameObject model = Object.Instantiate(prefab, clone.transform, false);
            model.name = name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            DisplayVisual.ApplyValheimShaders(model);
            return model;
        }

        private static bool ModelBounds(Transform root, Transform model, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i] != null ? filters[i].sharedMesh : null;
                if (mesh == null)
                    continue;
                Bounds mb = mesh.bounds;
                Transform t = filters[i].transform;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = new Vector3(
                        (c & 1) == 0 ? mb.min.x : mb.max.x,
                        (c & 2) == 0 ? mb.min.y : mb.max.y,
                        (c & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 local = root.InverseTransformPoint(t.TransformPoint(corner));
                    if (!any)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }
            return any;
        }

        private static void EnsureContainer(GameObject clone, GameObject woodChest)
        {
            Container container = clone.GetComponent<Container>();
            if (container == null)
                container = clone.AddComponent<Container>();

            Container src = woodChest != null ? woodChest.GetComponent<Container>() : null;
            if (src != null)
            {
                container.m_open = src.m_open;
                container.m_closed = src.m_closed;
                container.m_openEffects = src.m_openEffects;
                container.m_closeEffects = src.m_closeEffects;
            }

            container.m_name = Loc.T("Feed Trough", "Futtertrog");
            container.m_width = 6;
            container.m_height = 2;
            container.m_privacy = Container.PrivacySetting.Public;
            container.m_checkGuardStone = true;
        }

        /// <summary>
        /// Root is squashed on X; restore a usable hover/use box so the trough is not
        /// almost impossible to click.
        /// </summary>
        private static void AddInteractCollider(GameObject clone)
        {
            if (clone == null)
                return;

            Transform existing = clone.transform.Find("SacTroughHit");
            if (existing != null)
                return;

            var hit = new GameObject("SacTroughHit");
            hit.transform.SetParent(clone.transform, false);
            float undo = WidthScale > 0.01f ? 1f / WidthScale : 1f;
            hit.transform.localScale = new Vector3(undo, 1f, 1f);
            hit.transform.localPosition = Vector3.zero;

            BoxCollider box = hit.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.25f, 0f);
            box.size = new Vector3(0.9f, 0.55f, 2.2f);
            box.isTrigger = false;
        }

        /// <summary>
        /// On the trough clone only: copy meshes and flatten the taller bed end to the shorter end's height.
        /// Never mutates the shared bed mesh / original prefab.
        /// </summary>
        private static void SymmetrizeEnds(GameObject clone)
        {
            if (clone == null)
                return;

            MeshFilter[] filters = clone.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;

                Mesh src = mf.sharedMesh;
                Vector3[] srcVerts = src.vertices;
                if (srcVerts == null || srcVerts.Length < 8)
                    continue;

                Bounds b = src.bounds;
                bool lengthIsZ = b.size.z >= b.size.x;
                float lenMin = lengthIsZ ? b.min.z : b.min.x;
                float lenMax = lengthIsZ ? b.max.z : b.max.x;
                float lenRange = lenMax - lenMin;
                if (lenRange < 0.05f)
                    continue;

                const float endFrac = 0.25f;
                float lowZone = lenMin + endFrac * lenRange;
                float highZone = lenMax - endFrac * lenRange;

                float maxYLow = float.NegativeInfinity;
                float maxYHigh = float.NegativeInfinity;
                for (int v = 0; v < srcVerts.Length; v++)
                {
                    Vector3 p = srcVerts[v];
                    float along = lengthIsZ ? p.z : p.x;
                    if (along <= lowZone && p.y > maxYLow)
                        maxYLow = p.y;
                    if (along >= highZone && p.y > maxYHigh)
                        maxYHigh = p.y;
                }

                if (float.IsNegativeInfinity(maxYLow) || float.IsNegativeInfinity(maxYHigh))
                    continue;

                float delta = Mathf.Abs(maxYHigh - maxYLow);
                if (delta < 0.02f)
                    continue;

                float targetY = Mathf.Min(maxYLow, maxYHigh);
                bool squashHigh = maxYHigh > maxYLow;

                Mesh copy = Object.Instantiate(src);
                copy.name = src.name + "_trough";
                Vector3[] verts = copy.vertices;
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 p = verts[v];
                    float along = lengthIsZ ? p.z : p.x;
                    bool inTallEnd = squashHigh ? along >= highZone : along <= lowZone;
                    if (inTallEnd && p.y > targetY)
                    {
                        p.y = targetY;
                        verts[v] = p;
                    }
                }

                copy.vertices = verts;
                copy.RecalculateBounds();
                copy.RecalculateNormals();
                mf.sharedMesh = copy;

                MeshCollider col = mf.GetComponent<MeshCollider>();
                if (col != null && col.sharedMesh == src)
                    col.sharedMesh = copy;
            }
        }

        /// <summary>
        /// Copy a vanilla piece icon, squeeze horizontally, center on transparent square with a soft drop shadow
        /// (matches other hammer icons).
        /// </summary>
        private static Sprite MakeNarrowIcon(Sprite src, float widthScale)
        {
            if (src == null || widthScale <= 0f || widthScale > 1f)
                return null;

            try
            {
                Texture2D readable = CopySpriteReadable(src);
                if (readable == null)
                    return null;

                int w = readable.width;
                int h = readable.height;
                if (w < 2 || h < 2)
                {
                    Object.Destroy(readable);
                    return null;
                }

                // Trim opaque bounds so we don't keep empty atlas padding.
                int minX = w, minY = h, maxX = -1, maxY = -1;
                Color32[] srcPx = readable.GetPixels32();
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (srcPx[row + x].a < 8)
                            continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < minX)
                {
                    Object.Destroy(readable);
                    return null;
                }

                int cw = maxX - minX + 1;
                int ch = maxY - minY + 1;
                int side = Mathf.Max(cw, ch, 32);
                // Padding so shadow + squeeze still fit.
                side = Mathf.Max(side, Mathf.RoundToInt(Mathf.Max(cw * widthScale, ch) * 1.15f));

                var dst = new Texture2D(side, side, TextureFormat.RGBA32, false);
                dst.name = "sac_feed_trough_icon";
                var clear = new Color32[side * side];
                dst.SetPixels32(clear);

                int narrowW = Mathf.Max(1, Mathf.RoundToInt(cw * widthScale));
                int shadowOx = 2;
                int shadowOy = -2;
                int drawX = (side - narrowW) / 2;
                int drawY = (side - ch) / 2;

                // Soft drop shadow (Valheim hammer icons sit slightly above a dark blur).
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < narrowW; x++)
                    {
                        float u = (x + 0.5f) / narrowW;
                        int sx = minX + Mathf.Clamp(Mathf.FloorToInt(u * cw), 0, cw - 1);
                        Color32 s = srcPx[(minY + y) * w + sx];
                        if (s.a < 8)
                            continue;
                        int dx = drawX + x + shadowOx;
                        int dy = drawY + y + shadowOy;
                        if (dx < 0 || dy < 0 || dx >= side || dy >= side)
                            continue;
                        byte a = (byte)(s.a * 90 / 255);
                        dst.SetPixel(dx, dy, new Color32(0, 0, 0, a));
                    }
                }

                // Foreground squeezed art.
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < narrowW; x++)
                    {
                        float u = (x + 0.5f) / narrowW;
                        int sx = minX + Mathf.Clamp(Mathf.FloorToInt(u * cw), 0, cw - 1);
                        Color32 s = srcPx[(minY + y) * w + sx];
                        if (s.a < 8)
                            continue;
                        int dx = drawX + x;
                        int dy = drawY + y;
                        if (dx < 0 || dy < 0 || dx >= side || dy >= side)
                            continue;
                        Color32 under = dst.GetPixel(dx, dy);
                        // Alpha composite over shadow.
                        float sa = s.a / 255f;
                        float da = under.a / 255f;
                        float outA = sa + da * (1f - sa);
                        if (outA <= 0.001f)
                            continue;
                        float r = (s.r / 255f * sa + under.r / 255f * da * (1f - sa)) / outA;
                        float g = (s.g / 255f * sa + under.g / 255f * da * (1f - sa)) / outA;
                        float b = (s.b / 255f * sa + under.b / 255f * da * (1f - sa)) / outA;
                        dst.SetPixel(dx, dy, new Color32(
                            (byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), (byte)(outA * 255f)));
                    }
                }

                dst.Apply(false, false);
                Object.DontDestroyOnLoad(dst);
                Object.Destroy(readable);

                float ppu = src.pixelsPerUnit > 0f ? src.pixelsPerUnit : 100f;
                Sprite created = Sprite.Create(dst, new Rect(0f, 0f, side, side), new Vector2(0.5f, 0.5f), ppu);
                created.name = "sac_feed_trough_icon";
                return created;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Feed trough icon: " + ex.Message);
                return null;
            }
        }

        private static Texture2D CopySpriteReadable(Sprite sprite)
        {
            Texture2D tex = sprite.texture;
            if (tex == null)
                return null;

            Rect rect = sprite.textureRect;
            int rw = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int rh = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            RenderTexture full = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, full);
                RenderTexture.active = full;

                var slice = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
                slice.ReadPixels(new Rect(rect.x, rect.y, rw, rh), 0, 0);
                slice.Apply(false, false);
                return slice;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(full);
            }
        }

        private static Piece.Requirement[] BuildRecipe(GameObject woodChest)
        {
            Piece src = woodChest.GetComponent<Piece>();
            if (src != null && src.m_resources != null && src.m_resources.Length > 0)
            {
                var copy = new Piece.Requirement[src.m_resources.Length];
                for (int i = 0; i < src.m_resources.Length; i++)
                {
                    Piece.Requirement r = src.m_resources[i];
                    copy[i] = new Piece.Requirement
                    {
                        m_resItem = r.m_resItem,
                        m_amount = Mathf.Max(1, r.m_amount / 2),
                        m_amountPerLevel = 0,
                        m_recover = r.m_recover
                    };
                }
                return copy;
            }

            return BuildWoodFallback();
        }

        private static Piece.Requirement[] BuildWoodFallback()
        {
            GameObject wood = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Wood") : null;
            ItemDrop woodDrop = wood != null ? wood.GetComponent<ItemDrop>() : null;
            if (woodDrop == null)
                return new Piece.Requirement[0];

            return new[]
            {
                new Piece.Requirement
                {
                    m_resItem = woodDrop,
                    m_amount = 8,
                    m_amountPerLevel = 0,
                    m_recover = true
                }
            };
        }

        private static void RegisterHammer()
        {
            if (_prefab == null || ObjectDB.instance == null)
                return;

            GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            if (hammer == null)
                return;
            ItemDrop drop = hammer.GetComponent<ItemDrop>();
            PieceTable table = drop != null && drop.m_itemData?.m_shared != null
                ? drop.m_itemData.m_shared.m_buildPieces
                : null;
            if (table == null || table.m_pieces == null)
                return;
            if (!table.m_pieces.Contains(_prefab))
                table.m_pieces.Add(_prefab);
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
    internal static class ZNetSceneFeedTroughPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            FeedTroughPrefab.RegisterForScene(__instance);
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartFeedTroughPatch
    {
        private static void Postfix()
        {
            if (ZNetScene.instance != null)
                FeedTroughPrefab.RegisterForScene(ZNetScene.instance);
            FeedTroughPrefab.TryRegisterHammer();
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerFeedTroughTablePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;
            if (ZNetScene.instance != null)
                FeedTroughPrefab.RegisterForScene(ZNetScene.instance);
            FeedTroughPrefab.TryRegisterHammer();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PlaceFeedTroughPatch
    {
        private static void Prefix(Piece piece, ref bool cheated)
        {
            if (cheated && FeedTrough.IsTroughPiece(piece))
                cheated = false;
        }

        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception == null)
                return null;
            Plugin.Log.LogWarning("FeedTrough PlacePiece failed: " + __exception.Message);
            return null;
        }
    }
}
