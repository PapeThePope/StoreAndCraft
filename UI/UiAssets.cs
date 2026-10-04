using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>Loads PNG UI sprites embedded from Content/UI.</summary>
    internal static class UiAssets
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static MethodInfo _loadImage;
        private static bool _tried;
        private static bool _loadResolved;

        public static Sprite Track => Get("track.png");
        public static Sprite KnobOff => Get("knob_off.png");
        public static Sprite KnobOn => Get("knob_on.png");
        public static Sprite ToggleOn => Get("toggle_button_on.png");
        public static Sprite ToggleOff => Get("toggle_button_off.png");
        public static Sprite LinkSelected => Get("link_selected.png");
        public static Sprite ShowDisplayDisabled => Get("show_display_disabled.png");

        // Select Types — Photoshop mockup assets (Content/UI)
        public static Sprite PanelLeft => GetSliced("panel_left.png", 48f);
        public static Sprite PanelRight => GetSliced("panel_right.png", 48f);
        public static Sprite PanelLeftInset => GetSliced("panel_left_inset.png", 24f);
        public static Sprite PanelRightInset => GetSliced("panel_right_inset.png", 24f);
        public static Sprite RawCategory => GetSliced("raw_category.png", 12f);
        public static Sprite RawCategoryFocus => GetSliced("raw_category_focus.png", 12f);
        public static Sprite ItemCell => GetSliced("item_cell.png", 12f);
        public static Sprite BtnApply => GetSliced("btn_apply.png", 8f);
        public static Sprite ScrollTrack => GetSliced("scroll_track.png", 6f);
        public static Sprite ScrollThumb => Get("scroll_thumb.png");

        // Legacy aliases (older chrome — keep if still referenced elsewhere)
        public static Sprite PanelWood => PanelLeft;
        public static Sprite PanelInset => PanelRightInset;
        public static Sprite BtnWood => BtnApply;
        public static Sprite BtnWoodAccent => BtnApply;
        public static Sprite RowFocus => RawCategoryFocus;
        public static Sprite RowIdle => RawCategory;
        public static Sprite HeaderBar => null;
        public static Sprite IconWell => null;
        public static Sprite Divider => null;

        public static Sprite Link(int id)
        {
            if (id < 1 || id > 9)
                return null;
            return Get("link" + id + ".png");
        }

        public static Sprite Get(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            EnsureLoaded();
            Sprite cached;
            return Cache.TryGetValue(fileName.ToLowerInvariant(), out cached) ? cached : null;
        }

        /// <summary>Same as Get, but recreates with 9-slice borders if missing from cache key.</summary>
        public static Sprite GetSliced(string fileName, float border)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            string key = fileName.ToLowerInvariant() + "#s" + border.ToString("0");
            EnsureLoaded();
            Sprite cached;
            if (Cache.TryGetValue(key, out cached))
                return cached;

            Sprite baseSprite = Get(fileName);
            if (baseSprite == null)
                return null;

            Texture2D tex = baseSprite.texture;
            Sprite sliced = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            Cache[key] = sliced;
            return sliced;
        }

        private static void EnsureLoaded()
        {
            if (_tried)
                return;
            _tried = true;

            Assembly asm = typeof(UiAssets).Assembly;
            string[] names = asm.GetManifestResourceNames();
            if (names == null)
                return;

            for (int i = 0; i < names.Length; i++)
            {
                string res = names[i];
                if (res == null || !res.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = res.Split('.');
                string file = parts.Length >= 2
                    ? parts[parts.Length - 2] + ".png"
                    : res;
                file = file.ToLowerInvariant();

                try
                {
                    using (Stream stream = asm.GetManifestResourceStream(res))
                    {
                        if (stream == null)
                            continue;
                        byte[] bytes = ReadAll(stream);
                        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!LoadPng(tex, bytes))
                            continue;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        // Keep dark chrome / wood (do not punch near-black to alpha).
                        if (!file.StartsWith("toggle_button_")
                            && !file.StartsWith("panel_")
                            && !file.StartsWith("btn_")
                            && !file.StartsWith("raw_")
                            && !file.StartsWith("row_")
                            && !file.StartsWith("scroll_")
                            && !file.StartsWith("item_")
                            && !file.StartsWith("header_")
                            && !file.StartsWith("icon_")
                            && file != "divider.png")
                            MakeNearBlackTransparent(tex);
                        // Link / outline sprites are padded in a wide canvas — crop tight.
                        Rect rect = (file.StartsWith("link") || file == "link_selected.png")
                            ? OpaqueBounds(tex)
                            : new Rect(0, 0, tex.width, tex.height);

                        Sprite sprite = Sprite.Create(
                            tex,
                            rect,
                            new Vector2(0.5f, 0.5f),
                            100f);
                        Cache[file] = sprite;
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("UI asset " + res + ": " + ex.Message);
                }
            }

            Plugin.Log.LogDebug("StoreAndCraft UI sprites: " + Cache.Count);
        }

        /// <summary>
        /// ImageConversion lives in a netstandard2.1 module — call via reflection from net472.
        /// </summary>
        private static bool LoadPng(Texture2D tex, byte[] bytes)
        {
            if (!_loadResolved)
            {
                _loadResolved = true;
                System.Type t = System.Type.GetType(
                    "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (t != null)
                {
                    _loadImage = t.GetMethod(
                        "LoadImage",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Texture2D), typeof(byte[]) },
                        null);
                }
                if (_loadImage == null)
                {
                    _loadImage = typeof(Texture2D).GetMethod(
                        "LoadImage",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(byte[]) },
                        null);
                }
            }

            if (_loadImage == null)
                return false;

            try
            {
                if (_loadImage.IsStatic)
                    return (bool)_loadImage.Invoke(null, new object[] { tex, bytes });
                return (bool)_loadImage.Invoke(tex, new object[] { bytes });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("PNG load failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>PSD exports often use solid black instead of alpha.</summary>
        private static void MakeNearBlackTransparent(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a > 0 && p.r <= 6 && p.g <= 6 && p.b <= 6)
                {
                    p.a = 0;
                    pixels[i] = p;
                    changed = true;
                }
            }
            if (changed)
            {
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
            }
        }

        /// <summary>Tight rect around non-transparent pixels (Unity y-up).</summary>
        private static Rect OpaqueBounds(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            int w = tex.width;
            int h = tex.height;
            int minX = w;
            int minY = h;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x].a < 8)
                        continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return new Rect(0, 0, w, h);

            // Pad 1px so edges aren't clipped.
            minX = Mathf.Max(0, minX - 1);
            minY = Mathf.Max(0, minY - 1);
            maxX = Mathf.Min(w - 1, maxX + 1);
            maxY = Mathf.Min(h - 1, maxY + 1);
            return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
