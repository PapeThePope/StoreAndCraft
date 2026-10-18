using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StoreAndCraft
{
    internal static class Loc
    {
        // Translations by the English text: Localization/<language>.json (embedded), loaded when that language is
        // selected in Valheim. A text that is not in the file stays English.
        private static readonly Dictionary<string, string> LanguageFiles = new Dictionary<string, string>
        {
            { "Polish", "pl.json" }
        };

        private static string _language;
        private static Dictionary<string, string> _table;
        private static bool _hooked;

        public static string T(string en, string de)
        {
            if (en == null)
                return null;
            Dictionary<string, string> table = Table();
            string text;
            return table != null && table.TryGetValue(en, out text) ? text : en;
        }

        private static Dictionary<string, string> Table()
        {
            global::Localization loc = global::Localization.instance;
            if (loc == null)
                return null;
            if (!_hooked)
            {
                _hooked = true;
                // Valheim language changed in the settings: look again at the next text.
                global::Localization.OnLanguageChange += () => _language = null;
            }
            if (_language != null)
                return _table;

            _language = loc.GetSelectedLanguage() ?? "";
            _table = null;
            string file;
            if (LanguageFiles.TryGetValue(_language, out file))
                _table = Load(file);
            return _table;
        }

        private static Dictionary<string, string> Load(string file)
        {
            try
            {
                using (Stream stream = typeof(Loc).Assembly.GetManifestResourceStream("StoreAndCraft.Localization." + file))
                {
                    if (stream == null)
                    {
                        Plugin.Log.LogWarning("Translation " + file + " not found in the DLL.");
                        return null;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        Dictionary<string, string> table = ParseFlatJson(reader.ReadToEnd());
                        Plugin.Log.LogInfo("Translation " + file + ": " + table.Count + " texts.");
                        return table;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Translation " + file + " could not be read: " + ex.Message);
                return null;
            }
        }

        /// <summary>A flat JSON object of strings: { "English": "Translation", ... }.</summary>
        private static Dictionary<string, string> ParseFlatJson(string json)
        {
            var table = new Dictionary<string, string>(System.StringComparer.Ordinal);
            int i = 0;
            SkipSpace(json, ref i);
            Expect(json, ref i, '{');
            SkipSpace(json, ref i);
            if (i < json.Length && json[i] == '}')
                return table;
            while (true)
            {
                SkipSpace(json, ref i);
                string key = ReadString(json, ref i);
                SkipSpace(json, ref i);
                Expect(json, ref i, ':');
                SkipSpace(json, ref i);
                string value = ReadString(json, ref i);
                if (!string.IsNullOrEmpty(value))
                    table[key] = value;
                SkipSpace(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    continue;
                }
                Expect(json, ref i, '}');
                return table;
            }
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '﻿'))
                i++;
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c)
                throw new FormatException("'" + c + "' expected at position " + i);
            i++;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                            throw new FormatException("bad \\u escape at position " + i);
                        sb.Append((char)System.Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }
            throw new FormatException("unterminated text");
        }

        private sealed class FormatException : System.Exception
        {
            public FormatException(string message) : base(message)
            {
            }
        }
    }
}
