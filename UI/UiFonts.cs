using TMPro;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>Valheim TMP fonts — thin Norse for body, Norsebold for titles.</summary>
    internal static class UiFonts
    {
        private static TMP_FontAsset _thin;
        private static Material _thinMat;
        private static TMP_FontAsset _bold;
        private static Material _boldMat;

        /// <summary>Thin Valheim body font (Valheim-Norse). Falls back to any non-Liberation TMP font.</summary>
        public static TMP_FontAsset ThinNorse()
        {
            if (_thin != null)
                return _thin;

            TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (TryPickThin(all[i], exactNorse: true, anyNorse: false, averia: false, any: false))
                        return _thin;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    if (TryPickThin(all[i], exactNorse: false, anyNorse: true, averia: false, any: false))
                        return _thin;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    if (TryPickThin(all[i], exactNorse: false, anyNorse: false, averia: true, any: false))
                        return _thin;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    if (TryPickThin(all[i], exactNorse: false, anyNorse: false, averia: false, any: true))
                        return _thin;
                }
            }

            TMP_Text[] live = Resources.FindObjectsOfTypeAll<TMP_Text>();
            if (live == null)
                return null;
            for (int i = 0; i < live.Length; i++)
            {
                TMP_Text t = live[i];
                if (t == null || t.font == null || t.font.material == null || t.fontSharedMaterial == null)
                    continue;
                if (t.font.name != null
                    && t.font.name.IndexOf("Liberation", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                _thin = t.font;
                _thinMat = t.fontSharedMaterial;
                return _thin;
            }

            return null;
        }

        /// <summary>Bold Valheim title font (Norsebold).</summary>
        public static TMP_FontAsset BoldNorse()
        {
            if (_bold != null)
                return _bold;

            TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    TMP_FontAsset f = all[i];
                    if (f == null || string.IsNullOrEmpty(f.name) || f.material == null)
                        continue;
                    string n = f.name;
                    if (n.IndexOf("Liberation", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (n.Equals("Norsebold", System.StringComparison.OrdinalIgnoreCase)
                        || n.Equals("Valheim-Norsebold", System.StringComparison.OrdinalIgnoreCase)
                        || (n.IndexOf("Norse", System.StringComparison.OrdinalIgnoreCase) >= 0
                            && n.IndexOf("bold", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        _bold = f;
                        _boldMat = f.material;
                        return _bold;
                    }
                }
            }

            return ThinNorse();
        }

        public static Material ThinMaterial()
        {
            if (_thinMat != null)
                return _thinMat;
            TMP_FontAsset font = ThinNorse();
            return font != null ? font.material : null;
        }

        public static Material BoldMaterial()
        {
            if (_boldMat != null)
                return _boldMat;
            TMP_FontAsset font = BoldNorse();
            return font != null ? font.material : null;
        }

        /// <summary>
        /// Creates a TextMeshProUGUI without the LiberationSans missing-asset warning
        /// (TMP Awake looks for that default when the GO is active).
        /// </summary>
        public static TextMeshProUGUI CreateLabel(GameObject go, float size = 16f)
        {
            bool wasActive = go.activeSelf;
            if (wasActive)
                go.SetActive(false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            StyleThinLabel(tmp, size);
            if (wasActive)
                go.SetActive(true);
            return tmp;
        }

        public static TextMeshProUGUI CreateBoldLabel(GameObject go, float size = 16f)
        {
            bool wasActive = go.activeSelf;
            if (wasActive)
                go.SetActive(false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            StyleBoldLabel(tmp, size);
            if (wasActive)
                go.SetActive(true);
            return tmp;
        }

        public static void StyleThinLabel(TMP_Text tmp, float size = 16f)
        {
            if (tmp == null)
                return;
            TMP_FontAsset font = ThinNorse();
            if (font != null)
            {
                tmp.font = font;
                Material mat = ThinMaterial();
                if (mat != null)
                    tmp.fontSharedMaterial = mat;
            }
            tmp.fontStyle = FontStyles.Normal;
            tmp.fontSize = size;
            tmp.enableAutoSizing = false;
            tmp.richText = false;
            tmp.raycastTarget = false;
        }

        public static void StyleBoldLabel(TMP_Text tmp, float size = 16f)
        {
            if (tmp == null)
                return;
            TMP_FontAsset font = BoldNorse();
            if (font != null)
            {
                tmp.font = font;
                Material mat = BoldMaterial();
                if (mat != null)
                    tmp.fontSharedMaterial = mat;
            }
            tmp.fontStyle = FontStyles.Normal;
            tmp.fontSize = size;
            tmp.enableAutoSizing = false;
            tmp.richText = false;
            tmp.raycastTarget = false;
        }

        private static bool TryPickThin(TMP_FontAsset f, bool exactNorse, bool anyNorse, bool averia, bool any)
        {
            if (f == null || string.IsNullOrEmpty(f.name))
                return false;
            // A font without material (other mods' fallback / dynamic fonts) makes TMP throw while measuring the text
            // (NullReferenceException in TMPro.MaterialReference..ctor): never pick one.
            if (f.material == null)
                return false;
            string n = f.name;
            if (n.IndexOf("Liberation", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (exactNorse)
            {
                if (!(n.Equals("Valheim-Norse", System.StringComparison.OrdinalIgnoreCase)
                    || n.Equals("Norse", System.StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            else if (anyNorse)
            {
                if (n.IndexOf("Norse", System.StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (n.IndexOf("bold", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            else if (averia)
            {
                if (n.IndexOf("AveriaSans", System.StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (n.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            else if (any)
            {
                if (n.IndexOf("bold", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            else
                return false;

            _thin = f;
            _thinMat = f.material;
            return true;
        }
    }
}
