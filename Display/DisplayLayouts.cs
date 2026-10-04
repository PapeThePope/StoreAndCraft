using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Applies collider / snaps / ItemGrid from baked Prefab Editor values (shipped in the DLL).
    /// Optional disk JSON (plugins/displays/definitions or MordaPrefabEditor) overrides while authoring.
    /// End users only need StoreAndCraft.dll + displays/sac_displays.
    /// </summary>
    internal static class DisplayLayouts
    {
        private static readonly Dictionary<string, PrefabDef> Cache = new Dictionary<string, PrefabDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> CacheWrite = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        // PrefabStudio (F8) APPLY: in-memory definition that wins over disk/baked until cleared.
        private static readonly Dictionary<string, PrefabDef> Preview = new Dictionary<string, PrefabDef>(StringComparer.OrdinalIgnoreCase);
        private static int _generation = 1;
        private static string _sourceLabel = "none";

        /// <summary>
        /// Resolved definition file per visual id (null path = none on disk) + its write time.
        /// Find() runs several times per frame per carved display; the file system is only asked
        /// again after PathRecheckSeconds. EditorPreview / EnsureFresh drop entries explicitly.
        /// </summary>
        private sealed class PathProbe
        {
            public string Path;
            public DateTime WriteUtc;
            public float CheckedAt;
        }

        private static readonly Dictionary<string, PathProbe> PathCache = new Dictionary<string, PathProbe>(StringComparer.OrdinalIgnoreCase);
        private const float PathRecheckSeconds = 2f;
        // Baked definitions are never mutated (only freshly parsed defs get prefabId) — build once.
        private static readonly Dictionary<string, PrefabDef> BakedCache = new Dictionary<string, PrefabDef>(StringComparer.OrdinalIgnoreCase);

        public static int Generation => _generation;
        public static string SourceLabel => _sourceLabel ?? "none";

        public static void EnsureFresh()
        {
            if (!FileChangedOnDisk())
                return;
            Cache.Clear();
            CacheWrite.Clear();
            PathCache.Clear();
            _generation++;
            _sourceLabel = "reload";
            Plugin.Log.LogDebug("Display definitions changed on disk → reload gen=" + _generation);
        }

        /// <summary>
        /// Entry point for PrefabStudio (called via reflection, keep name/signature stable).
        /// json = PrefabDefinition JSON → live preview for every placed board of this visual,
        /// applied through the same code path as shipped definitions. json = null/empty → drop
        /// the preview and fall back to disk / baked. Returns false if the JSON did not parse.
        /// </summary>
        public static bool EditorPreview(string visualId, string json)
        {
            if (string.IsNullOrEmpty(visualId))
                return false;

            if (string.IsNullOrEmpty(json))
            {
                if (!Preview.Remove(visualId))
                    return true;
            }
            else
            {
                PrefabDef def;
                try
                {
                    def = PrefabDef.Parse(json);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Display editor preview parse failed (" + visualId + "): " + ex.Message);
                    return false;
                }
                if (def == null)
                    return false;
                def.prefabId = visualId;
                Preview[visualId] = def;
            }

            // Disk files may have been rewritten by SAVE in the same call — re-read them too.
            Cache.Remove(visualId);
            CacheWrite.Remove(visualId);
            PathCache.Remove(visualId);
            _generation++;
            StorageDisplayBoard.RebuildVisual(visualId);
            // Hammer template + placement ghost too, so snaps / collider apply to new pieces.
            DisplayPrefab.RefreshTemplates(visualId);
            FeedTroughPrefab.ApplyPreview(visualId);
            ScarecrowPrefab.ApplyPreview(visualId);
            return true;
        }

        public static bool TryApply(StorageDisplayBoard board, GameObject model, string visualId)
        {
            if (board == null || model == null || string.IsNullOrEmpty(visualId))
                return false;

            PrefabDef def = Find(visualId);
            if (def == null)
                return false;

            if (def.model != null && def.model.localScale != null)
                model.transform.localScale = def.model.localScale.ToVector3();

            // v2 sections build their own canvases — no v1 ItemGrid marker (AlignCanvas keys off it).
            ApplyItemGrid(model, board.transform, HasSections(def) ? null : def.itemGrid);
            ApplySnaps(board.transform, model.transform, def.snapPoints);
            ApplyColliders(board.gameObject, def.colliders);
            return true;
        }

        /// <summary>
        /// Other SAC pieces (feed trough): model scale, snaps and colliders from the definition,
        /// same apply code as the displays. Only sections the definition actually has are applied
        /// (an empty collider list must not strip the piece of its hit box).
        /// </summary>
        public static bool TryApplyPiece(GameObject root, string id, params Transform[] models)
        {
            if (root == null || string.IsNullOrEmpty(id))
                return false;
            PrefabDef def = Find(id);
            if (def == null)
                return false;

            if (def.model != null && def.model.localScale != null && models != null)
            {
                for (int i = 0; i < models.Length; i++)
                {
                    if (models[i] != null)
                        models[i].localScale = def.model.localScale.ToVector3();
                }
            }
            if (def.snapPoints != null && def.snapPoints.Length > 0)
                ApplySnaps(root.transform, null, def.snapPoints);
            if (def.colliders != null && def.colliders.Length > 0)
                ApplyColliders(root, def.colliders);
            return true;
        }

        /// <summary>Enabled eat points (piece-local) or null when the definition has none.</summary>
        public static List<Vector3> GetEatPoints(string id)
        {
            PrefabDef def = Find(id);
            if (def == null || def.eatPoints == null || def.eatPoints.Length == 0)
                return null;
            var list = new List<Vector3>(def.eatPoints.Length);
            for (int i = 0; i < def.eatPoints.Length; i++)
            {
                EatDef e = def.eatPoints[i];
                if (e != null && e.enabled)
                    list.Add(e.localPosition != null ? e.localPosition.ToVector3() : Vector3.zero);
            }
            return list.Count > 0 ? list : null;
        }

        public static bool HasColliderOverride(string visualId)
        {
            PrefabDef def = Find(visualId);
            return def != null && def.colliders != null && def.colliders.Length > 0;
        }

        public static bool HasSnapOverride(string visualId)
        {
            PrefabDef def = Find(visualId);
            return def != null && def.snapPoints != null && def.snapPoints.Length > 0;
        }

        public static bool HasItemGridOverride(string visualId)
        {
            if (string.IsNullOrEmpty(visualId))
                return false;
            PrefabDef def = Find(visualId);
            // Schema v2 (layouts/sections) owns the face — never also the single v1 ItemGrid.
            return def != null && def.itemGrid != null && def.itemGrid.enabled && !HasSections(def);
        }

        /// <summary>Schema v2: definition has at least one layout with sections.</summary>
        public static bool HasSections(string visualId)
        {
            if (string.IsNullOrEmpty(visualId))
                return false;
            return HasSections(Find(visualId));
        }

        private static bool HasSections(PrefabDef def)
        {
            if (def == null || def.layouts == null)
                return false;
            for (int i = 0; i < def.layouts.Length; i++)
            {
                if (def.layouts[i] != null && def.layouts[i].sections != null && def.layouts[i].sections.Length > 0)
                    return true;
            }
            return false;
        }

        public static int LayoutCount(string visualId)
        {
            PrefabDef def = Find(visualId);
            return HasSections(def) ? def.layouts.Length : 0;
        }

        /// <summary>Layout by index (clamped). Null when the visual has no v2 layouts.</summary>
        public static LayoutDef GetLayout(string visualId, int index)
        {
            PrefabDef def = Find(visualId);
            if (!HasSections(def))
                return null;
            index = Mathf.Clamp(index, 0, def.layouts.Length - 1);
            return def.layouts[index];
        }

        /// <summary>Columns/rows from Prefab Editor when itemGrid.enabled.</summary>
        public static bool TryGetItemGrid(string visualId, out int columns, out int rows)
        {
            columns = 0;
            rows = 0;
            Vector3 pos;
            Vector3 euler;
            Vector2 face;
            return TryGetItemGridFace(visualId, out columns, out rows, out pos, out euler, out face);
        }

        /// <summary>
        /// Full ItemGrid face from Prefab Editor (baked or disk): pos/euler under SacModel, size in meters.
        /// </summary>
        public static bool TryGetItemGridFace(
            string visualId,
            out int columns,
            out int rows,
            out Vector3 localPosition,
            out Vector3 localEuler,
            out Vector2 faceSizeMeters)
        {
            columns = 0;
            rows = 0;
            localPosition = Vector3.zero;
            localEuler = Vector3.zero;
            faceSizeMeters = Vector2.one;
            if (string.IsNullOrEmpty(visualId))
                return false;
            PrefabDef def = Find(visualId);
            if (def == null || def.itemGrid == null || !def.itemGrid.enabled || HasSections(def))
                return false;

            ItemGridDef g = def.itemGrid;
            columns = Mathf.Max(1, g.columns);
            rows = Mathf.Max(1, g.rows);
            localPosition = new Vector3(g.offsetX, g.offsetY, g.offsetZ);
            localEuler = g.euler != null ? g.euler.ToVector3() : Vector3.zero;
            float sx = Mathf.Max(0.01f, g.spacingX);
            float sy = Mathf.Max(0.01f, g.spacingY);
            faceSizeMeters = new Vector2(columns * sx, rows * sy);
            return true;
        }

        public static int SnapCount(string visualId)
        {
            PrefabDef def = Find(visualId);
            return def != null && def.snapPoints != null ? def.snapPoints.Length : 0;
        }

        public static bool FileChangedOnDisk()
        {
            // Any cached file with newer mtime forces generation bump on next Find.
            foreach (var kv in CacheWrite)
            {
                string path = ResolvePathForId(kv.Key);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;
                if (File.GetLastWriteTimeUtc(path) != kv.Value)
                    return true;
            }
            return false;
        }

        private static PrefabDef Find(string visualId)
        {
            if (string.IsNullOrEmpty(visualId))
                return null;

            PrefabDef preview;
            if (Preview.TryGetValue(visualId, out preview) && preview != null)
            {
                _sourceLabel = "preview";
                return preview;
            }

            // Live Prefab Editor / definitions folder can override baked while authoring.
            PrefabDef fromDisk = TryLoadDisk(visualId);
            if (fromDisk != null)
                return fromDisk;

            PrefabDef baked = TryBaked(visualId);
            if (baked != null)
            {
                _sourceLabel = "baked";
                return baked;
            }

            return null;
        }

        private static PrefabDef TryLoadDisk(string visualId)
        {
            DateTime writeUtc;
            string path = ResolvePathCached(visualId, out writeUtc);
            if (string.IsNullOrEmpty(path))
            {
                Cache.Remove(visualId);
                CacheWrite.Remove(visualId);
                return null;
            }

            PrefabDef cached;
            DateTime cachedWrite;
            if (Cache.TryGetValue(visualId, out cached)
                && CacheWrite.TryGetValue(visualId, out cachedWrite)
                && cachedWrite == writeUtc)
            {
                _sourceLabel = "json";
                return cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                PrefabDef def = PrefabDef.Parse(json);
                if (def != null && string.IsNullOrEmpty(def.prefabId))
                    def.prefabId = visualId;
                Cache[visualId] = def;
                CacheWrite[visualId] = writeUtc;
                _generation++;
                _sourceLabel = "json";
                Plugin.Log.LogDebug("Display definition loaded: " + path);
                return def;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Display definition load failed (" + visualId + "): " + ex.Message);
                Cache.Remove(visualId);
                CacheWrite.Remove(visualId);
                return null;
            }
        }

        /// <summary>
        /// Frozen Prefab Editor saves baked into the DLL so users need no JSON files.
        /// Update a branch when you F8 APPLY+SAVE a visual in the tool.
        /// </summary>
        private static PrefabDef TryBaked(string visualId)
        {
            PrefabDef baked;
            if (BakedCache.TryGetValue(visualId, out baked))
                return baked;
            if (string.Equals(visualId, "large_vertical", StringComparison.OrdinalIgnoreCase))
                baked = LargeVerticalBaked();
            else if (string.Equals(visualId, "small_horizontal", StringComparison.OrdinalIgnoreCase))
                baked = SmallHorizontalBaked();
            BakedCache[visualId] = baked; // null cached too: no baked def for this id
            return baked;
        }

        // Prefab Editor SAVE 2026-09-29 — large_vertical
        private static PrefabDef LargeVerticalBaked()
        {
            return new PrefabDef
            {
                schemaVersion = 1,
                prefabId = "large_vertical",
                model = new ModelDef { localScale = new Vec3 { x = 1f, y = 1f, z = 1f } },
                itemGrid = new ItemGridDef
                {
                    enabled = true,
                    columns = 7,
                    rows = 10,
                    spacingX = 0.41f,
                    spacingY = 0.43f,
                    offsetX = 0f,
                    offsetY = 2.49f,
                    offsetZ = 0f,
                    cellSize = 0.32f,
                    euler = new Vec3()
                },
                colliders = new[]
                {
                    new ColliderDef
                    {
                        id = "BoxCollider_0",
                        kind = "Box",
                        enabled = true,
                        center = new Vec3 { x = 0f, y = 2.63f, z = -0.04f },
                        size = new Vec3 { x = 3.74f, y = 5.2f, z = 0.23f },
                        radius = 0.5f,
                        height = 1f
                    }
                },
                snapPoints = new[]
                {
                    new SnapDef
                    {
                        id = "SnapPoint_2",
                        enabled = true,
                        localPosition = new Vec3 { x = 1.86f, y = 0.09f, z = 0f },
                        localEuler = new Vec3()
                    },
                    new SnapDef
                    {
                        id = "SnapPoint_1",
                        enabled = true,
                        localPosition = new Vec3 { x = -1.85f, y = 0.07f, z = 0f },
                        localEuler = new Vec3()
                    }
                }
            };
        }

        // Prefab Editor SAVE — small_horizontal
        private static PrefabDef SmallHorizontalBaked()
        {
            return new PrefabDef
            {
                schemaVersion = 1,
                prefabId = "small_horizontal",
                model = new ModelDef { localScale = new Vec3 { x = 1f, y = 1f, z = 1f } },
                itemGrid = new ItemGridDef
                {
                    enabled = true,
                    columns = 4,
                    rows = 2,
                    spacingX = 0.325f,
                    spacingY = 0.415f,
                    offsetX = 0f,
                    offsetY = 0.37f,
                    offsetZ = -0.04f,
                    cellSize = 0.3f,
                    euler = new Vec3()
                },
                colliders = new[]
                {
                    new ColliderDef
                    {
                        id = "BoxCollider_0",
                        kind = "Box",
                        enabled = true,
                        center = new Vec3 { x = 0f, y = 0.35f, z = -0.05f },
                        size = new Vec3 { x = 1.7f, y = 1.15f, z = 0.35f },
                        radius = 0.5f,
                        height = 1f
                    }
                },
                snapPoints = new[]
                {
                    new SnapDef
                    {
                        id = "SnapPoint_0",
                        enabled = true,
                        localPosition = new Vec3 { x = -0.55f, y = 1.42f, z = -0.04f },
                        localEuler = new Vec3()
                    },
                    new SnapDef
                    {
                        id = "SnapPoint_1",
                        enabled = true,
                        localPosition = new Vec3 { x = 0.5f, y = 1.37f, z = -0.14f },
                        localEuler = new Vec3()
                    }
                }
            };
        }

        /// <summary>
        /// ResolvePathForId, but the file system is asked at most every PathRecheckSeconds per id.
        /// Same file choice (newest of the three locations); only how often it is re-checked changes.
        /// </summary>
        private static string ResolvePathCached(string visualId, out DateTime writeUtc)
        {
            float now = Time.unscaledTime;
            PathProbe probe;
            if (PathCache.TryGetValue(visualId, out probe) && now - probe.CheckedAt < PathRecheckSeconds)
            {
                writeUtc = probe.WriteUtc;
                return probe.Path;
            }

            string path = ResolvePathForId(visualId, out writeUtc);
            if (probe == null)
            {
                probe = new PathProbe();
                PathCache[visualId] = probe;
            }
            probe.Path = path;
            probe.WriteUtc = writeUtc;
            probe.CheckedAt = now;
            return path;
        }

        /// <summary>Real file system check (FileChangedOnDisk / EnsureFresh rely on it).</summary>
        private static string ResolvePathForId(string visualId)
        {
            DateTime unused;
            return ResolvePathForId(visualId, out unused);
        }

        private static string ResolvePathForId(string visualId, out DateTime bestWriteUtc)
        {
            string safe = Sanitize(visualId);
            string sacDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string[] candidates =
            {
                Path.Combine(sacDir, "displays", "definitions", safe + ".json"),
                Path.Combine(sacDir, "definitions", safe + ".json"),
                Path.Combine(BepInExConfigDir(), "MordaPrefabEditor", safe + ".json"),
            };

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                string full = Path.GetFullPath(candidates[i]);
                if (!File.Exists(full))
                    continue;
                DateTime t = File.GetLastWriteTimeUtc(full);
                if (best == null || t > bestTime)
                {
                    best = full;
                    bestTime = t;
                }
            }
            bestWriteUtc = bestTime;
            return best;
        }

        private static string BepInExConfigDir()
        {
            try
            {
                return BepInEx.Paths.ConfigPath;
            }
            catch
            {
                string plugins = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                return Path.GetFullPath(Path.Combine(plugins, "..", "..", "config"));
            }
        }

        private static string Sanitize(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "unnamed";
            var sb = new StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        #region Apply

        private static void ApplyItemGrid(GameObject model, Transform pieceRoot, ItemGridDef grid)
        {
            Transform parent = model != null ? model.transform : pieceRoot;
            Transform existing = FindNamed(parent, "ItemGrid");
            if (existing == null && pieceRoot != null)
                existing = FindNamed(pieceRoot, "ItemGrid");
            if (existing != null)
            {
                existing.name = "ItemGrid_old";
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            if (grid == null || !grid.enabled)
                return;

            int cols = Mathf.Max(1, grid.columns);
            int rows = Mathf.Max(1, grid.rows);
            float sx = Mathf.Max(0.01f, grid.spacingX);
            float sy = Mathf.Max(0.01f, grid.spacingY);

            var rootGo = new GameObject("ItemGrid");
            Transform root = rootGo.transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(grid.offsetX, grid.offsetY, grid.offsetZ);
            root.localEulerAngles = grid.euler != null ? grid.euler.ToVector3() : Vector3.zero;
            // Scale = full authored face (cols×spacing × rows×spacing). One UI cell per grid cell.
            root.localScale = new Vector3(Mathf.Max(0.05f, cols * sx), Mathf.Max(0.05f, rows * sy), 1f);
        }

        private static void ApplySnaps(Transform pieceRoot, Transform model, SnapDef[] snaps)
        {
            if (pieceRoot == null)
                return;
            ClearSnaps(pieceRoot, model);
            if (model != null)
                ClearSnaps(model, null);
            if (snaps == null)
                return;

            for (int i = 0; i < snaps.Length; i++)
            {
                SnapDef s = snaps[i];
                if (s == null || !s.enabled)
                    continue;
                var go = new GameObject("SacSnap");
                go.transform.SetParent(pieceRoot, false);
                go.transform.localPosition = s.localPosition != null ? s.localPosition.ToVector3() : Vector3.zero;
                go.transform.localEulerAngles = s.localEuler != null ? s.localEuler.ToVector3() : Vector3.zero;
                go.transform.localScale = Vector3.one;
                try { go.tag = "snappoint"; } catch (Exception) { }
            }
        }

        private static void ClearSnaps(Transform root, Transform skip)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || child == skip) continue;
                if (child.name == "SacSnap" || IsSnapPoint(child))
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static void ApplyColliders(GameObject root, ColliderDef[] defs)
        {
            if (root == null)
                return;

            // Remove previous managed hosts.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("MordaCollider_", StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(child.gameObject);
            }

            // Disable every existing box (vanilla sign / FitCollider leftovers).
            BoxCollider[] allBoxes = root.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < allBoxes.Length; i++)
            {
                if (allBoxes[i] != null)
                    allBoxes[i].enabled = false;
            }

            if (defs == null || defs.Length == 0)
                return;

            bool wroteRootBox = false;
            for (int i = 0; i < defs.Length; i++)
            {
                ColliderDef d = defs[i];
                if (d == null || !d.enabled)
                    continue;
                string kind = (d.kind ?? "Box").Trim();

                // First Box goes on the Piece root — Valheim placement ghost uses that.
                if (kind == "Box" && !wroteRootBox)
                {
                    BoxCollider rootBox = root.GetComponent<BoxCollider>();
                    if (rootBox == null)
                        rootBox = root.AddComponent<BoxCollider>();
                    rootBox.enabled = true;
                    rootBox.isTrigger = d.isTrigger;
                    rootBox.center = d.center != null ? d.center.ToVector3() : Vector3.zero;
                    Vector3 size = d.size != null ? d.size.ToVector3() : new Vector3(1f, 1f, 0.12f);
                    size.x = Mathf.Max(0.05f, size.x);
                    size.y = Mathf.Max(0.05f, size.y);
                    size.z = Mathf.Max(0.05f, size.z);
                    rootBox.size = size;
                    wroteRootBox = true;
                    continue;
                }

                var host = new GameObject("MordaCollider_" + (string.IsNullOrEmpty(d.id) ? i.ToString() : d.id));
                host.transform.SetParent(root.transform, false);

                if (kind == "Sphere")
                {
                    SphereCollider s = host.AddComponent<SphereCollider>();
                    s.center = d.center != null ? d.center.ToVector3() : Vector3.zero;
                    s.radius = Mathf.Max(0.05f, d.radius);
                    s.isTrigger = d.isTrigger;
                }
                else if (kind == "Capsule")
                {
                    CapsuleCollider c = host.AddComponent<CapsuleCollider>();
                    c.center = d.center != null ? d.center.ToVector3() : Vector3.zero;
                    c.radius = Mathf.Max(0.05f, d.radius);
                    c.height = Mathf.Max(0.05f, d.height);
                    c.direction = Mathf.Clamp(d.direction, 0, 2);
                    c.isTrigger = d.isTrigger;
                }
                else
                {
                    BoxCollider b = host.AddComponent<BoxCollider>();
                    b.center = d.center != null ? d.center.ToVector3() : Vector3.zero;
                    Vector3 size = d.size != null ? d.size.ToVector3() : new Vector3(1f, 1f, 0.12f);
                    size.x = Mathf.Max(0.05f, size.x);
                    size.y = Mathf.Max(0.05f, size.y);
                    size.z = Mathf.Max(0.05f, size.z);
                    b.size = size;
                    b.isTrigger = d.isTrigger;
                }
            }
        }

        private static bool IsSnapPoint(Transform snap)
        {
            if (snap.name.StartsWith("snappoint", StringComparison.OrdinalIgnoreCase))
                return true;
            try { return snap.CompareTag("snappoint"); }
            catch { return false; }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform f = FindNamed(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        #endregion

        #region DTO + Parse

        [Serializable]
        private class Vec3
        {
            public float x, y, z;
            public Vector3 ToVector3() { return new Vector3(x, y, z); }
        }

        [Serializable]
        private class ModelDef
        {
            public Vec3 localScale;
        }

        [Serializable]
        private class ItemGridDef
        {
            public bool enabled = true;
            public int columns = 3;
            public int rows = 3;
            public float spacingX = 0.42f;
            public float spacingY = 0.42f;
            public float offsetX, offsetY, offsetZ;
            public float cellSize = 0.35f;
            public Vec3 euler;
        }

        [Serializable]
        private class ColliderDef
        {
            public string id;
            public string kind;
            public bool enabled = true;
            public bool isTrigger;
            public Vec3 center;
            public Vec3 size;
            public float radius = 0.5f;
            public float height = 1f;
            public int direction;
        }

        [Serializable]
        private class SnapDef
        {
            public string id;
            public bool enabled = true;
            public Vec3 localPosition;
            public Vec3 localEuler;
        }

        /// <summary>Feed trough: where animals stand to eat (piece-local, meters).</summary>
        [Serializable]
        private class EatDef
        {
            public string id;
            public bool enabled = true;
            public Vec3 localPosition;
        }

        // ---- Schema v2: several layouts, each with category sections (grid + label). ----

        /// <summary>One Shift+RMB layout variant of a visual.</summary>
        internal sealed class LayoutDef
        {
            public string name = "Standard";
            public SectionDef[] sections = Array.Empty<SectionDef>();
        }

        /// <summary>
        /// One area on the face. category: "1".."12" = n-th selected category (display filter
        /// order), "rest" = selected categories no numbered section shows, "all" = everything.
        /// </summary>
        internal sealed class SectionDef
        {
            public string id = "Section";
            public string category = "1";
            public bool showAmount = true;
            public float amountScale = 1f;
            public float iconScale = 1f;
            public string sort = "count"; // count | name
            public string fill = "top"; // top | bottom (first item in the bottom row, filling upward)
            public string emptyText = "0";
            public string amountColor = "";
            public SectionGridDef grid = new SectionGridDef();
            public SectionLabelDef label = new SectionLabelDef();
        }

        internal sealed class SectionGridDef
        {
            public int columns = 4;
            public int rows = 2;
            public float spacingX = 0.42f;
            public float spacingY = 0.42f;
            public Vector3 offset;
            public Vector3 euler;
        }

        internal sealed class SectionLabelDef
        {
            public bool enabled = true;
            public string text = ""; // empty = category name
            public Vector3 offset;
            public Vector3 euler;
            public float width = 1.5f;
            public float height = 0.25f;
            public float fontSize = 0.16f; // meters (text height)
            public string align = "left"; // left | center | right
            public string color = "#FFD966";
            public bool uppercase = true;
        }

        [Serializable]
        private class PrefabDef
        {
            public int schemaVersion = 1;
            public string prefabId;
            public ModelDef model;
            public ItemGridDef itemGrid;
            public ColliderDef[] colliders;
            public SnapDef[] snapPoints;
            public LayoutDef[] layouts;
            public EatDef[] eatPoints;

            /// <summary>Manual parse. JsonUtility drops nested arrays in this IL2CPP build.</summary>
            public static PrefabDef Parse(string json)
            {
                if (string.IsNullOrEmpty(json))
                    return null;

                var def = new PrefabDef
                {
                    schemaVersion = Mathf.Max(1, ReadInt(json, "schemaVersion", 1)),
                    prefabId = ReadString(json, "prefabId", ""),
                    model = new ModelDef { localScale = new Vec3 { x = 1f, y = 1f, z = 1f } },
                    itemGrid = new ItemGridDef(),
                    colliders = Array.Empty<ColliderDef>(),
                    snapPoints = Array.Empty<SnapDef>()
                };

                string modelBlock = ExtractObject(json, "model");
                if (modelBlock != null)
                {
                    Vec3 scale = ParseVec3(modelBlock, "localScale");
                    if (scale != null)
                        def.model.localScale = scale;
                }

                string gridBlock = ExtractObject(json, "itemGrid");
                if (gridBlock != null)
                    def.itemGrid = ParseItemGrid(gridBlock);

                string colsArr = ExtractArray(json, "colliders");
                if (colsArr != null)
                    def.colliders = ParseColliders(colsArr);

                string snapsArr = ExtractArray(json, "snapPoints");
                if (snapsArr != null)
                    def.snapPoints = ParseSnaps(snapsArr);

                string layoutsArr = ExtractArray(json, "layouts");
                if (layoutsArr != null)
                    def.layouts = ParseLayouts(layoutsArr);

                string eatArr = ExtractArray(json, "eatPoints");
                if (eatArr != null)
                    def.eatPoints = ParseEatPoints(eatArr);

                return def;
            }

            private static EatDef[] ParseEatPoints(string arrayBody)
            {
                List<string> objects = SplitObjects(arrayBody);
                var list = new List<EatDef>(objects.Count);
                for (int i = 0; i < objects.Count; i++)
                {
                    string o = objects[i];
                    list.Add(new EatDef
                    {
                        id = ReadString(o, "id", "EatPoint_" + i),
                        enabled = ReadBool(o, "enabled", true),
                        localPosition = ParseVec3(o, "localPosition") ?? new Vec3()
                    });
                }
                return list.ToArray();
            }

            private static LayoutDef[] ParseLayouts(string arrayBody)
            {
                List<string> objects = SplitObjects(arrayBody);
                var list = new List<LayoutDef>(objects.Count);
                for (int i = 0; i < objects.Count; i++)
                {
                    string o = objects[i];
                    // Read "name" before the sections array so a nested key cannot shadow it.
                    int secKey = IndexOfKey(o, "sections");
                    string head = secKey > 0 ? o.Substring(0, secKey) : o;
                    var layout = new LayoutDef { name = ReadString(head, "name", "Layout " + (i + 1)) };
                    string secArr = ExtractArray(o, "sections");
                    if (secArr != null)
                        layout.sections = ParseSections(secArr);
                    list.Add(layout);
                }
                return list.ToArray();
            }

            private static SectionDef[] ParseSections(string arrayBody)
            {
                List<string> objects = SplitObjects(arrayBody);
                var list = new List<SectionDef>(objects.Count);
                for (int i = 0; i < objects.Count; i++)
                {
                    string o = objects[i];
                    string gridBlock = ExtractObject(o, "grid");
                    string labelBlock = ExtractObject(o, "label");
                    // Section scalars only from the part outside grid/label blocks.
                    string flat = o;
                    if (gridBlock != null) flat = flat.Replace(gridBlock, "{}");
                    if (labelBlock != null) flat = flat.Replace(labelBlock, "{}");

                    var s = new SectionDef
                    {
                        id = ReadString(flat, "id", "Section_" + i),
                        category = ReadScalar(flat, "category", (i + 1).ToString(CultureInfo.InvariantCulture)),
                        showAmount = ReadBool(flat, "showAmount", true),
                        amountScale = Mathf.Clamp(ReadFloat(flat, "amountScale", 1f), 0.2f, 4f),
                        iconScale = Mathf.Clamp(ReadFloat(flat, "iconScale", 1f), 0.2f, 2f),
                        sort = ReadString(flat, "sort", "count"),
                        fill = ReadString(flat, "fill", "top"),
                        emptyText = ReadString(flat, "emptyText", "0"),
                        amountColor = ReadString(flat, "amountColor", "")
                    };
                    if (gridBlock != null)
                    {
                        s.grid = new SectionGridDef
                        {
                            columns = Mathf.Clamp(ReadInt(gridBlock, "columns", 4), 1, 32),
                            rows = Mathf.Clamp(ReadInt(gridBlock, "rows", 2), 1, 32),
                            spacingX = Mathf.Max(0.01f, ReadFloat(gridBlock, "spacingX", 0.42f)),
                            spacingY = Mathf.Max(0.01f, ReadFloat(gridBlock, "spacingY", 0.42f)),
                            offset = ReadOffset(gridBlock),
                            euler = (ParseVec3(gridBlock, "euler") ?? new Vec3()).ToVector3()
                        };
                    }
                    if (labelBlock != null)
                    {
                        s.label = new SectionLabelDef
                        {
                            enabled = ReadBool(labelBlock, "enabled", true),
                            text = ReadString(labelBlock, "text", ""),
                            offset = ReadOffset(labelBlock),
                            euler = (ParseVec3(labelBlock, "euler") ?? new Vec3()).ToVector3(),
                            width = Mathf.Max(0.05f, ReadFloat(labelBlock, "width", 1.5f)),
                            height = Mathf.Max(0.02f, ReadFloat(labelBlock, "height", 0.25f)),
                            fontSize = Mathf.Max(0.01f, ReadFloat(labelBlock, "fontSize", 0.16f)),
                            align = ReadString(labelBlock, "align", "left"),
                            color = ReadString(labelBlock, "color", "#FFD966"),
                            uppercase = ReadBool(labelBlock, "uppercase", true)
                        };
                    }
                    list.Add(s);
                }
                return list.ToArray();
            }

            /// <summary>String or bare number/word ("category": 2 or "category": "rest").</summary>
            private static string ReadScalar(string json, string key, string fallback)
            {
                int i = IndexOfKey(json, key);
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                if (colon < 0) return fallback;
                int j = colon + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                if (j >= json.Length) return fallback;
                if (json[j] == '"')
                    return ReadString(json, key, fallback);
                int k = j;
                while (k < json.Length && json[k] != ',' && json[k] != '}' && json[k] != '\n' && json[k] != '\r')
                    k++;
                string raw = json.Substring(j, k - j).Trim();
                return raw.Length > 0 ? raw : fallback;
            }

            private static Vector3 ReadOffset(string block)
            {
                return new Vector3(
                    ReadFloat(block, "offsetX", 0f),
                    ReadFloat(block, "offsetY", 0f),
                    ReadFloat(block, "offsetZ", 0f));
            }

            private static ItemGridDef ParseItemGrid(string section)
            {
                return new ItemGridDef
                {
                    enabled = ReadBool(section, "enabled", true),
                    columns = Mathf.Max(1, ReadInt(section, "columns", 3)),
                    rows = Mathf.Max(1, ReadInt(section, "rows", 3)),
                    spacingX = ReadFloat(section, "spacingX", 0.42f),
                    spacingY = ReadFloat(section, "spacingY", 0.42f),
                    offsetX = ReadFloat(section, "offsetX", 0f),
                    offsetY = ReadFloat(section, "offsetY", 0f),
                    offsetZ = ReadFloat(section, "offsetZ", 0f),
                    cellSize = ReadFloat(section, "cellSize", 0.35f),
                    euler = ParseVec3(section, "euler") ?? new Vec3()
                };
            }

            private static ColliderDef[] ParseColliders(string arrayBody)
            {
                List<string> objects = SplitObjects(arrayBody);
                var list = new List<ColliderDef>(objects.Count);
                for (int i = 0; i < objects.Count; i++)
                {
                    string o = objects[i];
                    list.Add(new ColliderDef
                    {
                        id = ReadString(o, "id", "Collider_" + i),
                        kind = ReadString(o, "kind", "Box"),
                        enabled = ReadBool(o, "enabled", true),
                        isTrigger = ReadBool(o, "isTrigger", false),
                        center = ParseVec3(o, "center") ?? new Vec3(),
                        size = ParseVec3(o, "size") ?? new Vec3 { x = 1f, y = 1f, z = 0.12f },
                        radius = ReadFloat(o, "radius", 0.5f),
                        height = ReadFloat(o, "height", 1f),
                        direction = ReadInt(o, "direction", 0)
                    });
                }
                return list.ToArray();
            }

            private static SnapDef[] ParseSnaps(string arrayBody)
            {
                List<string> objects = SplitObjects(arrayBody);
                var list = new List<SnapDef>(objects.Count);
                for (int i = 0; i < objects.Count; i++)
                {
                    string o = objects[i];
                    list.Add(new SnapDef
                    {
                        id = ReadString(o, "id", "SnapPoint_" + i),
                        enabled = ReadBool(o, "enabled", true),
                        localPosition = ParseVec3(o, "localPosition") ?? new Vec3(),
                        localEuler = ParseVec3(o, "localEuler") ?? new Vec3()
                    });
                }
                return list.ToArray();
            }

            private static Vec3 ParseVec3(string section, string name)
            {
                string block = ExtractObject(section, name);
                if (block == null)
                    return null;
                return new Vec3
                {
                    x = ReadFloat(block, "x", 0f),
                    y = ReadFloat(block, "y", 0f),
                    z = ReadFloat(block, "z", 0f)
                };
            }

            private static string ExtractObject(string json, string key)
            {
                int start = IndexOfKey(json, key);
                if (start < 0) return null;
                int brace = json.IndexOf('{', start);
                if (brace < 0) return null;
                int end = Match(json, brace, '{', '}');
                if (end < 0) return null;
                return json.Substring(brace, end - brace + 1);
            }

            private static string ExtractArray(string json, string key)
            {
                int start = IndexOfKey(json, key);
                if (start < 0) return null;
                int bracket = json.IndexOf('[', start);
                if (bracket < 0) return null;
                int end = Match(json, bracket, '[', ']');
                if (end < 0) return null;
                if (end <= bracket + 1) return "";
                return json.Substring(bracket + 1, end - bracket - 1);
            }

            private static int IndexOfKey(string json, string key)
            {
                return json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            }

            private static int Match(string json, int open, char openCh, char closeCh)
            {
                int depth = 0;
                for (int i = open; i < json.Length; i++)
                {
                    char c = json[i];
                    if (c == openCh) depth++;
                    else if (c == closeCh)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
                return -1;
            }

            private static List<string> SplitObjects(string arrayBody)
            {
                var list = new List<string>();
                if (string.IsNullOrEmpty(arrayBody))
                    return list;
                int depth = 0;
                int start = -1;
                for (int i = 0; i < arrayBody.Length; i++)
                {
                    char c = arrayBody[i];
                    if (c == '{')
                    {
                        if (depth == 0) start = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0 && start >= 0)
                        {
                            list.Add(arrayBody.Substring(start, i - start + 1));
                            start = -1;
                        }
                    }
                }
                return list;
            }

            private static string ReadString(string json, string key, string fallback)
            {
                int i = IndexOfKey(json, key);
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                if (colon < 0) return fallback;
                int q1 = json.IndexOf('"', colon + 1);
                if (q1 < 0) return fallback;
                int q2 = q1 + 1;
                while (q2 < json.Length && !(json[q2] == '"' && json[q2 - 1] != '\\'))
                    q2++;
                if (q2 >= json.Length) return fallback;
                return json.Substring(q1 + 1, q2 - q1 - 1);
            }

            private static bool ReadBool(string json, string key, bool fallback)
            {
                int i = IndexOfKey(json, key);
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                if (colon < 0) return fallback;
                string rest = json.Substring(colon + 1).TrimStart();
                if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase)) return false;
                return fallback;
            }

            private static int ReadInt(string json, string key, int fallback)
            {
                return Mathf.RoundToInt(ReadFloat(json, key, fallback));
            }

            private static float ReadFloat(string json, string key, float fallback)
            {
                int i = IndexOfKey(json, key);
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                if (colon < 0) return fallback;
                int j = colon + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                int k = j;
                if (k < json.Length && (json[k] == '-' || json[k] == '+')) k++;
                while (k < json.Length && (char.IsDigit(json[k]) || json[k] == '.' || json[k] == 'e' || json[k] == 'E'))
                    k++;
                if (k <= j) return fallback;
                float parsed;
                if (float.TryParse(json.Substring(j, k - j), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
                return fallback;
            }
        }

        #endregion
    }
}
