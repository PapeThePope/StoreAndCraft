using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreAndCraft
{
    /// <summary>
    /// Loads the Unity display bundle (built with the same Unity version as Valheim).
    /// Mesh from sac_displays; ItemGrid / collider / snaps from DisplayLayouts (baked Prefab Editor).
    /// </summary>
    internal static class DisplayVisual
    {
        private const string BundleFile = "sac_displays";
        private const string ModelName = "SacModel";

        private static AssetBundle _bundle;
        private static bool _bundleFailed;
        private static readonly HashSet<string> Logged = new HashSet<string>();

        public static bool Available(string visualBase)
        {
            if (string.IsNullOrEmpty(visualBase))
                return false;
            if (visualBase == "medium")
                return HasPrefab("medium_vertical");
            if (visualBase == "large")
                return HasPrefab("large_vertical");
            return HasPrefab(visualBase);
        }

        /// <summary>ItemGrid marker under SacModel (Prefab Editor offsets). Null if missing.</summary>
        public static Transform FindItemGrid(StorageDisplayBoard board)
        {
            if (board == null)
                return null;
            Transform model = board.transform.Find(ModelName);
            if (model == null)
                return null;
            return FindNamed(model, "ItemGrid");
        }

        /// <param name="templateMeshOnly">
        /// True when preparing the hidden hammer prefab: load SacModel only.
        /// World instances must apply YAML layout (never skip based on parent — Unity
        /// Instantiate keeps the hide-root parent through Start/TryBuild).
        /// </param>
        public static void Ensure(StorageDisplayBoard board, bool templateMeshOnly = false)
        {
            if (board == null || string.IsNullOrEmpty(board.VisualBase))
                return;

            string id = board.CurrentVisualId();
            if (string.IsNullOrEmpty(id))
                return;

            GameObject model = EnsureMesh(board, id);
            if (model == null)
                return;

            if (templateMeshOnly)
                return;

            EnsureLayout(board, model, id);
            // Clones copy AppliedLayoutGen and skip TryApply — always re-snap the sign canvas
            // to ItemGrid so hammer ghost / placed boards do not keep the vanilla foot text face.
            AlignCanvas(board, model);
            HideSignWriteFace(board);
            RefreshWearRenderers(board.gameObject);
        }

        /// <summary>
        /// WearNTear caches child renderers in Awake. Swapping SacModel / ItemGrid afterwards left
        /// destroyed renderers in that list → NullReferenceException in WearNTear.UpdateBiome.
        /// Rebuild it like vanilla GetHighlightRenderers, skipping "_old" objects pending Destroy.
        /// </summary>
        private static void RefreshWearRenderers(GameObject root)
        {
            WearNTear wnt = root != null ? root.GetComponent<WearNTear>() : null;
            List<Renderer> list = Refs.WearRenderers(wnt);
            if (list == null)
                return; // Awake not run yet — vanilla collects the current children itself.

            list.Clear();
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                    continue;
                if (IsPendingDestroy(r.transform, root.transform))
                    continue;
                list.Add(r);
            }
        }

        private static bool IsPendingDestroy(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
            {
                // "_old" = our swapped SacModel / ItemGrid; MordaPrefabGizmo_ = PrefabStudio F8 preview.
                if (t.name.EndsWith("_old", StringComparison.Ordinal)
                    || t.name.StartsWith("MordaPrefabGizmo_", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Load or keep SacModel for this visual id. Does not apply layout.</summary>
        private static GameObject EnsureMesh(StorageDisplayBoard board, string id)
        {
            Transform existing = board.transform.Find(ModelName);
            if (board.AppliedVisual == id && existing != null)
                return existing.gameObject;

            GameObject prefab = LoadPrefab(id);
            if (prefab == null)
            {
                LogOnce("missing " + id, "Storage display bundle has no prefab '" + id + "'. Keeping the wood sign.");
                return null;
            }

            try
            {
                if (existing != null)
                {
                    // Destroy runs at frame end: rename + hide first so Find("SacModel")
                    // (canvas / ItemGrid lookups this frame) hits the new model, not this one.
                    existing.name = ModelName + "_old";
                    existing.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(existing.gameObject);
                }

                var model = UnityEngine.Object.Instantiate(prefab, board.transform, false);
                model.name = ModelName;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                ClearLegacyScale(board);
                ApplyValheimShaders(model);
                HideVanillaRenderers(board.gameObject, model.transform);

                // Template gets a mesh-fit collider so clones place; world EnsureLayout may override.
                FitCollider(board.gameObject, model);
                LiftSnapPoints(board, model);

                board.AppliedVisual = id;
                board.AppliedLayoutGen = 0;
                LogOnce("loaded " + id, "Storage display prefab loaded: " + id + ".");
                return model;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Storage display prefab " + id + " failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Apply baked/disk layout (or mesh fallbacks) on a world instance.</summary>
        private static void EnsureLayout(StorageDisplayBoard board, GameObject model, string id)
        {
            // Reload if PrefabStudio rewrote the file since last apply (bumps Generation).
            DisplayLayouts.EnsureFresh();

            Transform existingGrid = FindNamed(model.transform, "ItemGrid");
            bool genMatch = board.AppliedLayoutGen == DisplayLayouts.Generation && board.AppliedLayoutGen > 0;
            if (genMatch && existingGrid != null)
                return;

            bool layoutApplied = DisplayLayouts.TryApply(board, model, id);
            bool colliderOverride = layoutApplied && DisplayLayouts.HasColliderOverride(id);
            bool snapOverride = layoutApplied && DisplayLayouts.HasSnapOverride(id);

            if (!colliderOverride)
            {
                FitCollider(board.gameObject, model);
                if (board.Kind != DisplayKind.Small)
                    SoftenColliderDepth(board.gameObject, board.Kind);
            }

            if (!snapOverride)
                LiftSnapPoints(board, model);

            board.AppliedLayoutGen = DisplayLayouts.Generation;
            // Always Info so a failed place is obvious in LogOutput.
            Plugin.Log.LogDebug("layout applied id=" + id
                + " source=" + DisplayLayouts.SourceLabel
                + " hit=" + layoutApplied
                + " collider=" + (colliderOverride ? "def" : "fit")
                + " grid=" + (layoutApplied && DisplayLayouts.HasItemGridOverride(id) ? "def" : "none")
                + " snaps=" + DisplayLayouts.SnapCount(id)
                + " gen=" + DisplayLayouts.Generation);
        }

        /// <summary>
        /// Medium/Large without authored collider: keep solid placeable boxes but thinner face depth.
        /// </summary>
        internal static void SoftenColliderDepth(GameObject root, DisplayKind kind)
        {
            if (root == null || kind == DisplayKind.Small)
                return;

            float depth = kind == DisplayKind.Large ? 0.04f : 0.06f;
            Collider[] cols = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                BoxCollider box = cols[i] as BoxCollider;
                if (box == null)
                    continue;
                Vector3 size = box.size;
                if (size.z <= size.x && size.z <= size.y)
                    size.z = Mathf.Min(size.z, depth);
                else if (size.x <= size.y)
                    size.x = Mathf.Min(size.x, depth);
                else
                    size.y = Mathf.Min(size.y, depth);
                box.size = size;
                box.isTrigger = false;
            }
        }

        private static bool HasPrefab(string id)
        {
            return LoadPrefab(id) != null;
        }

        internal static GameObject LoadPrefab(string id)
        {
            AssetBundle bundle = Bundle();
            if (bundle == null)
                return null;
            GameObject prefab = bundle.LoadAsset<GameObject>(id);
            if (prefab != null)
                return prefab;

            string[] names = bundle.GetAllAssetNames();
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].EndsWith("/" + id + ".prefab", StringComparison.OrdinalIgnoreCase))
                    return bundle.LoadAsset<GameObject>(names[i]);
            }
            return null;
        }

        private static AssetBundle Bundle()
        {
            if (_bundle != null)
                return _bundle;
            if (_bundleFailed)
                return null;

            string path = Path.Combine(DisplaysDir(), BundleFile);
            if (!File.Exists(path))
            {
                _bundleFailed = true;
                LogOnce("nobundle", "Storage display bundle missing (" + path + "). Keeping the wood signs.");
                return null;
            }

            try
            {
                _bundle = AssetBundle.LoadFromFile(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Storage display bundle failed: " + ex.Message);
            }

            if (_bundle == null)
            {
                _bundleFailed = true;
                LogOnce("badbundle", "Storage display bundle could not be loaded. It must be built with Unity 6000.0.75f1.");
            }
            return _bundle;
        }

        private static string DisplaysDir()
        {
            string dll = Assembly.GetExecutingAssembly().Location;
            return Path.Combine(Path.GetDirectoryName(dll) ?? "", "displays");
        }

        private static Shader _pieceShader;

        /// <summary>
        /// Valheim's build shader. Shader.Find only sees shaders that are loaded right now (not yet at start-up on
        /// some machines), so the shader is also taken from a vanilla piece that is always in memory.
        /// </summary>
        internal static Shader PieceShader()
        {
            if (_pieceShader != null)
                return _pieceShader;
            Shader found = Shader.Find("Custom/Piece");
            if (found == null && ZNetScene.instance != null)
            {
                string[] pieces = { "wood_floor", "woodwall", "wood_wall_roof", "piece_chest_wood", "stone_wall_1x1" };
                for (int p = 0; p < pieces.Length && found == null; p++)
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(pieces[p]);
                    if (prefab == null)
                        continue;
                    foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m != null && m.shader != null && m.shader.name == "Custom/Piece")
                            {
                                found = m.shader;
                                break;
                            }
                        }
                        if (found != null)
                            break;
                    }
                }
            }
            if (found != null)
                _pieceShader = found;
            return found;
        }

        /// <summary>
        /// Swaps the bundle materials to Valheim's piece shader. Returns false when that shader is not available
        /// yet: the model then carries a PieceShaderFix, which tries again once it is in the world.
        /// </summary>
        internal static bool ApplyValheimShaders(GameObject model)
        {
            Shader piece = PieceShader();
            if (piece == null)
            {
                LogOnce("noshader", "Valheim piece shader was not found. Displays keep the Unity preview material until it is.");
                if (model != null && model.GetComponent<PieceShaderFix>() == null)
                    model.AddComponent<PieceShaderFix>();
                return false;
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.gameObject.name == "ItemGrid")
                    continue;
                Material shared = renderer.sharedMaterial;
                if (shared != null && shared.shader == piece)
                    continue;
                Material[] mats = renderer.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                        continue;
                    Texture tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : mat.mainTexture;
                    Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                    float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                    float gloss = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.3f;
                    string n = mat.name.ToLowerInvariant();
                    LogMaterialOnce(model.name, renderer.gameObject.name, mat, piece);
                    mat.shader = piece;
                    if (tex != null && mat.HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", tex);
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", color);
                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", metallic);
                    if (mat.HasProperty("_Glossiness"))
                        mat.SetFloat("_Glossiness", gloss);
                    if (mat.HasProperty("_MetalColor"))
                    {
                        // Untextured metal (e.g. DarkIron from Blender) keeps its own colour;
                        // textured materials keep the old silver tint.
                        Color metal = (n.Contains("gold") || n.Contains("nail"))
                            ? new Color(1f, 0.72f, 0.28f)
                            : tex == null ? color : new Color(0.75f, 0.75f, 0.78f);
                        mat.SetColor("_MetalColor", metal);
                    }
                }
                renderer.materials = mats;
            }
            return true;
        }

        /// <summary>
        /// Diagnostics for Blender/Unity → Valheim: once per material, log what the bundle
        /// material carries before the swap to Custom/Piece (only _MainTex/_Color/_Metallic/
        /// _Glossiness are carried over). Once: which texture slots Custom/Piece itself has.
        /// </summary>
        private static void LogMaterialOnce(string model, string part, Material mat, Shader piece)
        {
            if (mat == null)
                return;
            string key = "mat " + mat.name;
            if (Logged.Contains(key))
                return;
            Logged.Add(key);

            var sb = new System.Text.StringBuilder();
            sb.Append("Display material '").Append(mat.name.Replace(" (Instance)", ""))
                .Append("' on ").Append(model).Append('/').Append(part)
                .Append(" shader=").Append(mat.shader != null ? mat.shader.name : "null");
            if (mat.HasProperty("_Color"))
                sb.Append(" color=").Append(ColorUtility.ToHtmlStringRGBA(mat.GetColor("_Color")));
            string[] slots = mat.GetTexturePropertyNames();
            for (int i = 0; i < slots.Length; i++)
            {
                Texture t = mat.GetTexture(slots[i]);
                if (t == null)
                    continue;
                Vector2 tiling = mat.GetTextureScale(slots[i]);
                sb.Append(' ').Append(slots[i]).Append('=').Append(t.name)
                    .Append('(').Append(t.width).Append('x').Append(t.height).Append(')');
                if (tiling != Vector2.one)
                    sb.Append(" tiling=").Append(tiling.x.ToString("0.##")).Append('/').Append(tiling.y.ToString("0.##"));
            }
            if (mat.HasProperty("_Metallic"))
                sb.Append(" metallic=").Append(mat.GetFloat("_Metallic").ToString("0.##"));
            if (mat.HasProperty("_Glossiness"))
                sb.Append(" smooth=").Append(mat.GetFloat("_Glossiness").ToString("0.##"));
            Plugin.Log.LogDebug(sb.ToString());

            if (piece != null && Logged.Add("piece shader slots"))
            {
                var tmp = new Material(piece);
                Plugin.Log.LogDebug("Custom/Piece texture slots: " + string.Join(", ", tmp.GetTexturePropertyNames()));
                UnityEngine.Object.Destroy(tmp);
            }
        }

        private static void RemoveItemGrid(GameObject model)
        {
            Transform grid = FindNamed(model.transform, "ItemGrid");
            if (grid == null)
                return;
            UnityEngine.Object.Destroy(grid.gameObject);
        }

        private static void HideVanillaRenderers(GameObject root, Transform model)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.transform.IsChildOf(model))
                    continue;
                renderer.enabled = false;
            }
        }

        private static void FitCollider(GameObject root, GameObject model)
        {
            bool any = false;
            Bounds local = new Bounds(Vector3.zero, Vector3.zero);
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == null || filters[i].gameObject.name == "ItemGrid")
                    continue;
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                    continue;
                Encapsulate(ref local, ref any, root.transform, filters[i].transform, mesh.bounds);
            }
            if (!any)
                return;

            Vector3 size = local.size;
            if (size.z < 0.12f)
                size.z = 0.12f;

            BoxCollider[] boxes = root.GetComponentsInChildren<BoxCollider>(true);
            BoxCollider box = boxes.Length > 0 ? boxes[0] : root.AddComponent<BoxCollider>();
            if (box.transform == root.transform)
            {
                box.center = local.center;
                box.size = size;
            }
            box.isTrigger = false;
            for (int i = 1; i < boxes.Length; i++)
            {
                if (boxes[i] != null)
                    boxes[i].enabled = false;
            }

            // Definition with an empty collider list: ApplyColliders disabled every box first.
            // Keep the piece placeable with an enabled mesh-fit box on the root.
            if (!box.enabled)
            {
                BoxCollider rootBox = root.GetComponent<BoxCollider>();
                if (rootBox == null)
                    rootBox = root.AddComponent<BoxCollider>();
                rootBox.center = local.center;
                rootBox.size = size;
                rootBox.isTrigger = false;
                rootBox.enabled = true;
            }
        }

        private static void Encapsulate(ref Bounds local, ref bool any, Transform root, Transform piece, Bounds meshBounds)
        {
            Vector3 c = meshBounds.center;
            Vector3 e = meshBounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 inRoot = root.InverseTransformPoint(piece.TransformPoint(corner));
                if (!any)
                {
                    local = new Bounds(inRoot, Vector3.zero);
                    any = true;
                }
                else
                {
                    local.Encapsulate(inRoot);
                }
            }
        }

        private static void AlignCanvas(StorageDisplayBoard board, GameObject model)
        {
            Sign sign = board.GetComponent<Sign>();
            if (sign == null || sign.m_textWidget == null || sign.m_textWidget.canvas == null)
                return;
            // v2 sections build their own canvases; the sign canvas stays off.
            if (DisplayLayouts.HasSections(board.CurrentVisualId()))
                return;

            // Recreate marker if a previous build destroyed it (old RemoveItemGrid path).
            Transform grid = FindNamed(model.transform, "ItemGrid");
            if (grid == null || grid.name == "ItemGrid_old")
            {
                DisplayLayouts.TryApply(board, model, board.CurrentVisualId());
                grid = FindNamed(model.transform, "ItemGrid");
            }
            if (grid == null || grid.name == "ItemGrid_old")
                return;

            Canvas canvas = sign.m_textWidget.canvas;
            canvas.gameObject.SetActive(true);

            float worldW = Mathf.Abs(grid.lossyScale.x);
            float worldH = Mathf.Abs(grid.lossyScale.y);
            if (worldW < 0.05f || worldH < 0.05f)
                return;

            // Local snap under the piece — survives Instantiate for hammer ghost / place.
            Transform canvasTf = canvas.transform;
            if (canvasTf.parent != board.transform)
                canvasTf.SetParent(board.transform, true);

            canvasTf.position = grid.position + grid.forward * 0.02f;
            canvasTf.rotation = grid.rotation;

            Vector3 canvasScale = canvasTf.lossyScale;
            float sx = Mathf.Abs(canvasScale.x);
            float sy = Mathf.Abs(canvasScale.y);
            if (sx < 0.0001f || sy < 0.0001f)
                return;

            RectTransform text = sign.m_textWidget.rectTransform;
            text.anchorMin = new Vector2(0.5f, 0.5f);
            text.anchorMax = new Vector2(0.5f, 0.5f);
            text.pivot = new Vector2(0.5f, 0.5f);
            text.anchoredPosition = Vector2.zero;
            text.localRotation = Quaternion.identity;
            text.localScale = Vector3.one;
            text.sizeDelta = new Vector2(worldW / sx, worldH / sy);
        }

        /// <summary>Vanilla sign write face stays as TMP template only — never visible on storage boards.</summary>
        private static void HideSignWriteFace(StorageDisplayBoard board)
        {
            Sign sign = board != null ? board.GetComponent<Sign>() : null;
            if (sign == null || sign.m_textWidget == null)
                return;

            TextMeshProUGUI tmp = sign.m_textWidget;
            tmp.enabled = false;
            tmp.text = "";
            tmp.raycastTarget = false;

            // Hide any Image/raw graphic on the canvas that still looks like a writable plaque.
            Canvas canvas = tmp.canvas;
            if (canvas == null)
                return;
            Transform sacGrid = FindSacGrid(canvas.transform);
            var graphics = canvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null || graphics[i] == tmp)
                    continue;
                // SacDisplayGrid icons/amounts must stay visible.
                if (sacGrid != null && graphics[i].transform.IsChildOf(sacGrid))
                    continue;
                graphics[i].enabled = false;
            }
        }

        private static Transform FindSacGrid(Transform canvasRoot)
        {
            if (canvasRoot == null)
                return null;
            Transform t = canvasRoot.Find("SacDisplayGrid");
            if (t != null)
                return t;
            // Parent may be the piece after we reparented the canvas.
            if (canvasRoot.parent != null)
                return canvasRoot.parent.Find("SacDisplayGrid");
            return null;
        }

        private static void LiftSnapPoints(StorageDisplayBoard board, GameObject model)
        {
            var found = new List<Transform>();
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform snap = all[i];
                if (snap == null || snap == model.transform || snap.GetComponent<Renderer>() != null)
                    continue;
                if (!IsSnapPoint(snap))
                    continue;
                found.Add(snap);
            }
            if (found.Count == 0)
                return;

            Transform root = board.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || child == model.transform)
                    continue;
                if (child.name == "SacSnap" || IsSnapPoint(child))
                    UnityEngine.Object.Destroy(child.gameObject);
            }

            for (int i = 0; i < found.Count; i++)
            {
                Transform snap = found[i];
                snap.SetParent(root, true);
                snap.name = "SacSnap";
                try
                {
                    snap.gameObject.tag = "snappoint";
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool IsSnapPoint(Transform snap)
        {
            if (snap.name.StartsWith("snappoint", StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                return snap.CompareTag("snappoint");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Sign canvas even while it is inactive (Graphic.canvas is null then).</summary>
        internal static Canvas FindCanvas(Transform from)
        {
            for (Transform t = from; t != null; t = t.parent)
            {
                Canvas canvas = t.GetComponent<Canvas>();
                if (canvas != null)
                    return canvas;
            }
            return null;
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root == null)
                return null;
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamed(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void ClearLegacyScale(StorageDisplayBoard board)
        {
            Vector3 scale = board.transform.localScale;
            bool medium = Near(scale.x, 2.5f) && Near(scale.y, 2.1f);
            bool large = Near(scale.x, 7.5f) && Near(scale.y, 6.3f);
            if (!medium && !large)
                return;

            ZNetView view = board.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner())
                view.SetLocalScale(Vector3.one);
            else
                board.transform.localScale = Vector3.one;
        }

        private static bool Near(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.08f;
        }

        private static void LogOnce(string key, string message)
        {
            if (!Logged.Add(key))
                return;
            Plugin.Log.LogDebug(message);
        }
    }
    /// <summary>
    /// On a model whose shader swap could not run at start-up (Custom/Piece not loaded yet): every instance retries
    /// twice a second until it works, so the model never stays magenta. Magenta = a Unity shader this graphics
    /// API has no variant for.
    /// </summary>
    internal sealed class PieceShaderFix : MonoBehaviour
    {
        private float _next;
        private int _tries;

        private void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 0.5f;
            _tries++;
            if (DisplayVisual.ApplyValheimShaders(gameObject))
            {
                Plugin.Log.LogDebug("Piece shader applied late to " + gameObject.name + " (try " + _tries + ").");
                Destroy(this);
            }
            else if (_tries >= 240)
            {
                enabled = false; // 2 minutes: give up
            }
        }
    }
}
