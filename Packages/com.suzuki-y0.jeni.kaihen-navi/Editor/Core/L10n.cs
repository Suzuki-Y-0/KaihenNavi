using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 画面の文言を言語ファイル（Localization/*.json）から引く。
    /// 言語ファイルは { "キー": "文言" } だけの平らな JSON。翻訳者が直接編集できる形にしている。
    /// </summary>
    internal static class L10n
    {
        public static readonly string[] Codes = { "ja", "en", "zh-Hans", "ko" };
        public static readonly string[] DisplayNames = { "日本語", "English", "简体中文", "한국어" };

        private const string PrefKey = "Jeni.KaihenNavi.Language";
        private const string FallbackCode = "ja";

        private static Dictionary<string, string> _table;
        private static Dictionary<string, string> _fallback;
        private static string _loadedCode;

        /// <summary>言語が切り替わったときに呼ばれる。</summary>
        public static event Action Changed;

        public static string Current
        {
            get { return EditorPrefs.GetString(PrefKey, DefaultCode()); }
            set
            {
                if (Array.IndexOf(Codes, value) < 0) value = FallbackCode;
                if (value == Current && _loadedCode == value) return;
                EditorPrefs.SetString(PrefKey, value);
                _table = null;
                if (Changed != null) Changed();
            }
        }

        public static int CurrentIndex
        {
            get
            {
                var i = Array.IndexOf(Codes, Current);
                return i < 0 ? 0 : i;
            }
        }

        public static string Tr(string key)
        {
            EnsureLoaded();
            string value;
            if (_table != null && _table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
            if (_fallback != null && _fallback.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
            return key;
        }

        public static string Tr(string key, params object[] args)
        {
            var format = Tr(key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>言語ファイルを読み直す（翻訳の編集中に使う）。</summary>
        public static void Reload()
        {
            _table = null;
            _fallback = null;
            if (Changed != null) Changed();
        }

        private static string DefaultCode()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh-Hans";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.English: return "en";
                default: return "ja";
            }
        }

        private static void EnsureLoaded()
        {
            var code = Current;
            if (_table != null && _loadedCode == code) return;

            if (_fallback == null) _fallback = Load(FallbackCode);
            _table = code == FallbackCode ? _fallback : Load(code);
            _loadedCode = code;
        }

        private static Dictionary<string, string> Load(string code)
        {
            var path = PackagePaths.Localization + "/" + code + ".json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogWarning("[かいへんナビ] 言語ファイルが見つかりません: " + path);
                return new Dictionary<string, string>();
            }

            try
            {
                return FlatJson.Parse(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[かいへんナビ] 言語ファイルの読み込みに失敗しました: " + path + "\n" + e.Message);
                return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>{ "key": "value", ... } 形式だけを読む小さな JSON パーサ。</summary>
    internal static class FlatJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            var i = 0;
            SkipWhitespace(json, ref i);
            Expect(json, ref i, '{');
            SkipWhitespace(json, ref i);
            if (Peek(json, i) == '}') return result;

            while (true)
            {
                SkipWhitespace(json, ref i);
                var key = ReadString(json, ref i);
                SkipWhitespace(json, ref i);
                Expect(json, ref i, ':');
                SkipWhitespace(json, ref i);
                var value = ReadString(json, ref i);
                result[key] = value;
                SkipWhitespace(json, ref i);
                var c = Peek(json, i);
                i++;
                if (c == ',') continue;
                if (c == '}') break;
                throw new FormatException("',' か '}' が必要です（位置 " + (i - 1) + "）");
            }

            return result;
        }

        private static char Peek(string s, int i)
        {
            return i < s.Length ? s[i] : '\0';
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (Peek(s, i) != c) throw new FormatException("'" + c + "' が必要です（位置 " + i + "）");
            i++;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length) break;
                var e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u の後ろが短すぎます");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("不明なエスケープ \\" + e);
                }
            }

            throw new FormatException("文字列が閉じられていません");
        }
    }
}
