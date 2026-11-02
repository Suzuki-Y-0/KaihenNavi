using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    [Serializable]
    internal sealed class KonoAssetDescription
    {
        public string name = null;
        public string creator = null;
        public string imageFilename = null;
        public string[] tags = null;
        public string memo = null;
        public long createdAt = 0;
        // boothItemId は利用しないので宣言しない。整数 / null のどちらも JsonUtility が無視する。
    }

    [Serializable]
    internal sealed class KonoAssetItem
    {
        public string id = null;
        public KonoAssetDescription description = null;
        public string category = null;
        public string[] supportedAvatars = null;
        // 外部データには書かない。索引用のファイル名だけを Library に保存する。
        public string[] packageNames = null;
        public string sourceFile = null;
    }

    /// <summary>外部フォルダでは列挙と読み取りだけを行う。一覧の作成ではパッケージの本文を開かない。</summary>
    [InitializeOnLoad]
    internal static class KonoAssetLink
    {
        private const string RootKey = "KaihenNavi.KonoAsset.Root";
        private const string CachePath = "Library/KaihenNavi/konoasset-index.json";
        private static readonly string[] MetadataFiles = { "avatars.json", "avatarWearables.json", "worldObjects.json", "otherAssets.json" };
        private static readonly Dictionary<string, KonoAssetItem> ById = new Dictionary<string, KonoAssetItem>(StringComparer.Ordinal);
        // null は同名商品が複数あることを表す。勝手に先頭の商品を選ばない。
        private static readonly Dictionary<string, KonoAssetItem> ByPackage = new Dictionary<string, KonoAssetItem>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> Images = new Dictionary<string, Texture2D>();
        private static readonly Queue<string> ImageOrder = new Queue<string>();
        private static string _root;
        private static long[] _stamps;
        private static double _nextCheck;
        private static bool _checked;
        private static int _revision;
        public static int Revision { get { EnsureLoaded(); return _revision; } }
        public static event Action Changed;

        [Serializable] private sealed class Header { public int version = 0; }
        [Serializable] private sealed class Container { public int version = 0; public KonoAssetItem[] data = null; }
        [Serializable] private sealed class Preference { public PreferenceData data = null; }
        [Serializable] private sealed class PreferenceData { public string dataDirPath = null; }
        [Serializable] private sealed class Index
        {
            public int version;
            public string root;
            public long[] stamps;
            public KonoAssetItem[] items;
        }

        static KonoAssetLink() { AssemblyReloadEvents.beforeAssemblyReload += ClearImages; }

        public static bool IsAvailable { get { EnsureLoaded(); return _root != null; } }
        public static int Count { get { EnsureLoaded(); return ById.Count; } }
        public static IEnumerable<KonoAssetItem> All { get { EnsureLoaded(); return ById.Values; } }

        public static void ChooseFolder()
        {
            var selected = EditorUtility.OpenFolderPanel(L10n.Tr("kono.chooseFolder"), _root ?? "", "");
            if (string.IsNullOrEmpty(selected)) return;
            if (!IsRoot(selected))
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("kono.invalidFolder"), L10n.Tr("dialog.ok"));
                return;
            }
            EditorPrefs.SetString(RootKey, selected);
            Reload();
        }

        public static void Reload()
        {
            _checked = false;
            _stamps = null;
            ClearImages();
            EnsureLoaded(true);
            EditorApplication.delayCall -= NotifyChanged;
            if (Changed != null) Changed();
        }

        private static void QueueChanged()
        {
            // UI の組み立て中に再帰して作り直さず、次の Editor 更新で表示をそろえる。
            EditorApplication.delayCall -= NotifyChanged;
            EditorApplication.delayCall += NotifyChanged;
        }

        private static void NotifyChanged() { if (Changed != null) Changed(); }

        private static bool IsRoot(string path)
        {
            try { return !string.IsNullOrEmpty(path) && MetadataFiles.Any(f => File.Exists(Path.Combine(path, "metadata", f))); }
            catch { return false; }
        }

        private static string LocateRoot()
        {
            var selected = EditorPrefs.GetString(RootKey, "");
            if (IsRoot(selected)) return Path.GetFullPath(selected);
            // 要確認: Tauri app_local_data_dir の実配置。Windows は通常
            // %LOCALAPPDATA%/dev.konoasset.app/preference.json。Mac / Linux も手動指定で補える。
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var preferences = new[]
            {
                Path.Combine(local, "dev.konoasset.app", "preference.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "dev.konoasset.app", "preference.json")
            };
            foreach (var file in preferences)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    var preference = JsonUtility.FromJson<Preference>(File.ReadAllText(file, Encoding.UTF8));
                    var path = preference != null && preference.data != null ? preference.data.dataDirPath : null;
                    if (IsRoot(path)) return Path.GetFullPath(path);
                }
                catch { /* 未導入・設定変更中・読めない形式では通常の候補表示を続ける。 */ }
            }
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fallback = Path.Combine(documents, "KonoAsset");
            return IsRoot(fallback) ? Path.GetFullPath(fallback) : null;
        }

        private static long[] ReadStamps(string root)
        {
            return MetadataFiles.Select(f =>
            {
                var info = new FileInfo(Path.Combine(root, "metadata", f));
                return info.Exists ? info.LastWriteTimeUtc.Ticks : -1L;
            }).ToArray();
        }

        private static void EnsureLoaded(bool force = false)
        {
            // UI の行ごとの呼び出しでディスクを何度も調べない。未導入時も最大5秒に1回だけ。
            if (!force && _checked && EditorApplication.timeSinceStartup < _nextCheck) return;
            _checked = true;
            _nextCheck = EditorApplication.timeSinceStartup + 5;
            try
            {
                var root = LocateRoot();
                var stamps = root == null ? null : ReadStamps(root);
                if (!force && root == _root && ((_stamps == null && stamps == null) ||
                    (_stamps != null && stamps != null && _stamps.SequenceEqual(stamps)))) return;
                _revision++;
                if (!force) QueueChanged();
                _root = root;
                _stamps = stamps;
                ById.Clear();
                ByPackage.Clear();
                ClearImages();
                if (root == null) return;
                Index index = null;
                if (!force && File.Exists(CachePath))
                {
                    try
                    {
                        var cached = JsonUtility.FromJson<Index>(File.ReadAllText(CachePath, Encoding.UTF8));
                        if (cached != null && cached.version == 2 && cached.root == root && cached.stamps != null &&
                            cached.stamps.SequenceEqual(stamps) && cached.items != null) index = cached;
                    }
                    catch { /* 壊れたキャッシュは元のメタデータから再作成する。 */ }
                }
                if (index == null)
                {
                    bool complete;
                    index = BuildIndex(root, stamps, out complete);
                    // 読み取り中のメタデータやアクセス失敗を永続キャッシュしない。
                    if (complete && stamps.SequenceEqual(ReadStamps(root))) SaveIndex(index);
                    else _stamps = null;
                }
                UseIndex(index.items);
            }
            catch
            {
                _revision++;
                if (!force) QueueChanged();
                _root = null;
                _stamps = null;
                ById.Clear();
                ByPackage.Clear();
                ClearImages();
            }
        }

        private static void UseIndex(KonoAssetItem[] items)
        {
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || item.description == null) continue;
                ById[item.id] = item;
                foreach (var name in item.packageNames ?? new string[0])
                {
                    var key = Normalize(name);
                    if (key.Length == 0) continue;
                    KonoAssetItem previous;
                    if (!ByPackage.TryGetValue(key, out previous)) ByPackage[key] = item;
                    else if (previous == null || previous.id != item.id) ByPackage[key] = null;
                }
            }
        }

        private static Index BuildIndex(string root, long[] stamps, out bool complete)
        {
            complete = true;
            var items = new List<KonoAssetItem>();
            foreach (var filename in MetadataFiles)
            {
                var path = Path.Combine(root, "metadata", filename);
                if (!File.Exists(path)) continue;
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var header = JsonUtility.FromJson<Header>(json);
                    if (header == null) { complete = false; continue; }
                    if (header.version != 3)
                    {
                        // Editor セッションで1回だけ。形式の違うファイルは変換も読み込みもしない。
                        const string warningKey = "KaihenNavi.KonoAsset.VersionWarning";
                        if (SessionState.GetString(warningKey, "") != "shown")
                        {
                            SessionState.SetString(warningKey, "shown");
                            Debug.LogWarning(L10n.Tr("kono.unsupportedVersion"));
                        }
                        continue;
                    }
                    var container = JsonUtility.FromJson<Container>(json);
                    if (container == null || container.data == null) { complete = false; continue; }
                    foreach (var item in container.data)
                    {
                        Guid id;
                        if (item == null || !Guid.TryParse(item.id, out id) || item.description == null) continue;
                        item.sourceFile = filename;
                        var names = new List<string>();
                        // ファイル本文には触れず、ディレクトリとファイル名だけを列挙する。
                        try { ListPackages(Path.Combine(root, "data", item.id), names); }
                        catch { complete = false; }
                        item.packageNames = names.Distinct().ToArray();
                        items.Add(item);
                    }
                }
                catch { complete = false; }
            }
            return new Index { version = 2, root = root, stamps = stamps, items = items.ToArray() };
        }

        private static void ListPackages(string directory, List<string> names)
        {
            if (!Directory.Exists(directory)) return;
            // リンク先の巡回やディレクトリの循環を避ける。
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.unitypackage"))
                names.Add(Path.GetFileNameWithoutExtension(file));
            foreach (var child in Directory.EnumerateDirectories(directory)) ListPackages(child, names);
        }

        /// <summary>選んだ商品の unitypackage だけを、外部データを変更せずに列挙する。</summary>
        public static List<string> Packages(KonoAssetItem item)
        {
            EnsureLoaded();
            Guid id;
            var files = new List<string>();
            if (_root == null || item == null || !Guid.TryParse(item.id, out id)) return files;
            try { ListPackageFiles(Path.Combine(_root, "data", item.id), files); }
            catch { /* 読めない場所は取り込み候補にしない。 */ }
            return files.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        private static void ListPackageFiles(string directory, List<string> files)
        {
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            files.AddRange(Directory.EnumerateFiles(directory).Where(p =>
                p.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) &&
                (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0));
            foreach (var child in Directory.EnumerateDirectories(directory)) ListPackageFiles(child, files);
        }

        /// <summary>商品名・ショップ名・保存ファイル名を同じ表記ルールで部分一致検索する。</summary>
        public static bool Matches(KonoAssetItem item, string query)
        {
            if (item == null) return false;
            return Contains(item.description == null ? null : item.description.name, query) ||
                Contains(item.description == null ? null : item.description.creator, query) ||
                (item.packageNames ?? new string[0]).Any(n => Contains(n, query));
        }

        public static bool Contains(string value, string query)
        {
            var key = Normalize(query);
            if (key.Length == 0) return true;
            var normalized = Normalize(value);
            if (normalized.Contains(key)) return true;
            // ひらがなを含む検索語だけ、ローマ字表記の商品名やファイル名にも当てる。
            // 英数字だけの検索語は今までどおり部分一致に留める。
            return key.Any(IsHiragana) && CachedVariants(key).Any(v => normalized.Contains(v));
        }

        // 検索は 1 文字ごとに全部の商品・項目で呼ばれるので、直前の検索語の候補を覚えておく。
        private static string _variantsKey;
        private static string[] _variants = new string[0];

        private static string[] CachedVariants(string key)
        {
            if (key != _variantsKey)
            {
                // 「あ」→「a」のような短い候補は、ほとんどの英語名に当たってしまうので使わない。
                _variants = RomajiVariants(key).Where(v => v.Length >= 3).ToArray();
                _variantsKey = key;
            }

            return _variants;
        }

        private static readonly Dictionary<string, string> HepburnRomaji = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "あ", "a" }, { "い", "i" }, { "う", "u" }, { "え", "e" }, { "お", "o" },
            { "ぁ", "a" }, { "ぃ", "i" }, { "ぅ", "u" }, { "ぇ", "e" }, { "ぉ", "o" },
            { "か", "ka" }, { "き", "ki" }, { "く", "ku" }, { "け", "ke" }, { "こ", "ko" },
            { "が", "ga" }, { "ぎ", "gi" }, { "ぐ", "gu" }, { "げ", "ge" }, { "ご", "go" },
            { "さ", "sa" }, { "し", "shi" }, { "す", "su" }, { "せ", "se" }, { "そ", "so" },
            { "ざ", "za" }, { "じ", "ji" }, { "ず", "zu" }, { "ぜ", "ze" }, { "ぞ", "zo" },
            { "た", "ta" }, { "ち", "chi" }, { "つ", "tsu" }, { "て", "te" }, { "と", "to" },
            { "だ", "da" }, { "ぢ", "ji" }, { "づ", "zu" }, { "で", "de" }, { "ど", "do" },
            { "な", "na" }, { "に", "ni" }, { "ぬ", "nu" }, { "ね", "ne" }, { "の", "no" },
            { "は", "ha" }, { "ひ", "hi" }, { "ふ", "fu" }, { "へ", "he" }, { "ほ", "ho" },
            { "ば", "ba" }, { "び", "bi" }, { "ぶ", "bu" }, { "べ", "be" }, { "ぼ", "bo" },
            { "ぱ", "pa" }, { "ぴ", "pi" }, { "ぷ", "pu" }, { "ぺ", "pe" }, { "ぽ", "po" },
            { "ま", "ma" }, { "み", "mi" }, { "む", "mu" }, { "め", "me" }, { "も", "mo" },
            { "や", "ya" }, { "ゆ", "yu" }, { "よ", "yo" },
            { "ゃ", "ya" }, { "ゅ", "yu" }, { "ょ", "yo" },
            { "ら", "ra" }, { "り", "ri" }, { "る", "ru" }, { "れ", "re" }, { "ろ", "ro" },
            { "わ", "wa" }, { "ゐ", "i" }, { "ゑ", "e" }, { "を", "o" }, { "ん", "n" },
            { "ゎ", "wa" }, { "ゔ", "vu" }
        };

        private static readonly Dictionary<string, string> KunreiRomaji = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "あ", "a" }, { "い", "i" }, { "う", "u" }, { "え", "e" }, { "お", "o" },
            { "ぁ", "a" }, { "ぃ", "i" }, { "ぅ", "u" }, { "ぇ", "e" }, { "ぉ", "o" },
            { "か", "ka" }, { "き", "ki" }, { "く", "ku" }, { "け", "ke" }, { "こ", "ko" },
            { "が", "ga" }, { "ぎ", "gi" }, { "ぐ", "gu" }, { "げ", "ge" }, { "ご", "go" },
            { "さ", "sa" }, { "し", "si" }, { "す", "su" }, { "せ", "se" }, { "そ", "so" },
            { "ざ", "za" }, { "じ", "zi" }, { "ず", "zu" }, { "ぜ", "ze" }, { "ぞ", "zo" },
            { "た", "ta" }, { "ち", "ti" }, { "つ", "tu" }, { "て", "te" }, { "と", "to" },
            { "だ", "da" }, { "ぢ", "di" }, { "づ", "du" }, { "で", "de" }, { "ど", "do" },
            { "な", "na" }, { "に", "ni" }, { "ぬ", "nu" }, { "ね", "ne" }, { "の", "no" },
            { "は", "ha" }, { "ひ", "hi" }, { "ふ", "hu" }, { "へ", "he" }, { "ほ", "ho" },
            { "ば", "ba" }, { "び", "bi" }, { "ぶ", "bu" }, { "べ", "be" }, { "ぼ", "bo" },
            { "ぱ", "pa" }, { "ぴ", "pi" }, { "ぷ", "pu" }, { "ぺ", "pe" }, { "ぽ", "po" },
            { "ま", "ma" }, { "み", "mi" }, { "む", "mu" }, { "め", "me" }, { "も", "mo" },
            { "や", "ya" }, { "ゆ", "yu" }, { "よ", "yo" },
            { "ゃ", "ya" }, { "ゅ", "yu" }, { "ょ", "yo" },
            { "ら", "ra" }, { "り", "ri" }, { "る", "ru" }, { "れ", "re" }, { "ろ", "ro" },
            { "わ", "wa" }, { "ゐ", "i" }, { "ゑ", "e" }, { "を", "o" }, { "ん", "n" },
            { "ゎ", "wa" }, { "ゔ", "vu" }
        };

        private static readonly Dictionary<string, string>[] RomajiTables = { HepburnRomaji, KunreiRomaji };

        private static bool IsHiragana(char c) { return c >= '\u3041' && c <= '\u3096'; }

        private static bool IsVowel(char c) { return c == 'a' || c == 'i' || c == 'u' || c == 'e' || c == 'o'; }

        private static bool IsSmallYa(char c) { return c == 'ゃ' || c == 'ゅ' || c == 'ょ'; }

        private static bool IsSmallVowel(char c) { return c == 'ぁ' || c == 'ぃ' || c == 'ぅ' || c == 'ぇ' || c == 'ぉ'; }

        /// <summary>ひらがなをヘボン式・訓令式のローマ字にし、長音を詰めた形も候補にする。</summary>
        internal static IEnumerable<string> RomajiVariants(string hiragana)
        {
            var key = Normalize(hiragana);
            var variants = new List<string>();
            if (key.Length == 0) return variants;
            foreach (var table in RomajiTables)
            {
                var romaji = ToRomaji(key, table);
                AddRomajiVariant(variants, romaji);
                AddRomajiVariant(variants, CompressLongVowels(romaji));
            }
            return variants;
        }

        private static void AddRomajiVariant(List<string> variants, string romaji)
        {
            if (!string.IsNullOrEmpty(romaji) && !variants.Contains(romaji)) variants.Add(romaji);
        }

        private static string CompressLongVowels(string romaji)
        {
            if (string.IsNullOrEmpty(romaji)) return romaji;
            return romaji.Replace("ou", "o").Replace("uu", "u").Replace("oo", "o");
        }

        private static string ToRomaji(string key, Dictionary<string, string> table)
        {
            var builder = new StringBuilder(key.Length * 2);
            var geminate = false;
            for (var i = 0; i < key.Length; i++)
            {
                if (key[i] == 'っ') { geminate = true; continue; }
                int consumed;
                var romaji = UnitRomaji(key, i, table, out consumed);
                i += consumed - 1;
                if (geminate)
                {
                    if (romaji.Length > 0)
                    {
                        // っち はヘボン式で tchi、訓令式で tti にする。
                        if (romaji.StartsWith("ch", StringComparison.Ordinal)) builder.Append('t');
                        else if (!IsVowel(romaji[0])) builder.Append(romaji[0]);
                    }
                    geminate = false;
                }
                builder.Append(romaji);
            }
            return builder.ToString();
        }

        private static string UnitRomaji(string key, int index, Dictionary<string, string> table, out int consumed)
        {
            consumed = 1;
            string baseRomaji;
            // 表にない文字（英数字や未知の文字）はそのまま通す。
            if (!table.TryGetValue(key[index].ToString(), out baseRomaji)) return key[index].ToString();
            if (index + 1 >= key.Length) return baseRomaji;
            var small = key[index + 1];
            var combined = IsSmallYa(small) ? CombineSmallYa(baseRomaji, small)
                : IsSmallVowel(small) ? CombineSmallVowel(baseRomaji, small) : null;
            if (combined == null) return baseRomaji;
            consumed = 2;
            return combined;
        }

        private static string CombineSmallYa(string baseRomaji, char small)
        {
            if (!baseRomaji.EndsWith("i", StringComparison.Ordinal)) return null;
            var stem = baseRomaji.Substring(0, baseRomaji.Length - 1);
            var vowel = small == 'ゃ' ? "a" : small == 'ゅ' ? "u" : "o";
            // し・ち・じ だけは sh/ch/j に母音を足す（sha, cha, ja）。
            if (baseRomaji == "shi" || baseRomaji == "chi" || baseRomaji == "ji") return stem + vowel;
            return stem + "y" + vowel;
        }

        private static string CombineSmallVowel(string baseRomaji, char small)
        {
            if (baseRomaji.Length == 0 || !IsVowel(baseRomaji[baseRomaji.Length - 1])) return null;
            var stem = baseRomaji.Substring(0, baseRomaji.Length - 1);
            // 母音だけのときは分けたままにして、不自然な短縮を避ける。
            if (stem.Length == 0) return null;
            var vowel = small == 'ぁ' ? "a" : small == 'ぃ' ? "i" : small == 'ぅ' ? "u" : small == 'ぇ' ? "e" : "o";
            return stem + vowel;
        }

        private static void SaveIndex(Index index)
        {
            try
            {
                // ユーザーがプロジェクト内を指定した場合も外部ルート内には書かない。
                var target = Path.GetFullPath(CachePath);
                var root = Path.GetFullPath(index.root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
                // Library 自体やキャッシュが外部フォルダへのリンクなら書き込まない。
                for (var current = target; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target, JsonUtility.ToJson(index), new UTF8Encoding(false));
            }
            catch { /* キャッシュを保存できなくても連携は使える。 */ }
        }

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // カタカナをひらがなにそろえ、全角英数・半角カナと大小文字の違いも吸収する。
            return new string(name.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
                .Select(c => c >= 'ァ' && c <= 'ヶ' ? (char)(c - 0x60) : c).Where(char.IsLetterOrDigit).ToArray());
        }

        public static KonoAssetItem Find(string id)
        {
            EnsureLoaded();
            KonoAssetItem item;
            return id != null && ById.TryGetValue(id, out item) ? item : null;
        }

        public static KonoAssetItem FindByPackageName(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;
            if (name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            KonoAssetItem item;
            return ByPackage.TryGetValue(Normalize(name), out item) ? item : null;
        }

        public static KonoAssetItem FindByFolderName(string folder)
        {
            EnsureLoaded();
            var key = Normalize(folder);
            if (key.Length < 4) return null;
            KonoAssetItem match = null;
            foreach (var item in ById.Values)
            {
                var names = (item.packageNames ?? new string[0]).Concat(new[] { item.description.name });
                if (!names.Select(Normalize).Any(n => n.Length >= 4 && (n.Contains(key) || key.Contains(n)))) continue;
                if (match != null && match.id != item.id) return null;
                match = item;
            }
            return match;
        }

        /// <summary>
        /// プロジェクト内のアバターの置き場所（Assets/Cazalis/... など）から、KonoAsset のアバター商品を探し、
        /// 対応アバター欄で使われている呼び名（「カザリス」など）を返す。英語のフォルダ名だけでは、
        /// 日本語で書かれた対応アバター欄と照合できないため。
        /// </summary>
        public static List<string> AvatarNamesFor(string assetPath)
        {
            EnsureLoaded();
            var result = new List<string>();
            if (string.IsNullOrEmpty(assetPath)) return result;
            var parts = assetPath.Replace('\\', '/').Split('/');
            KonoAssetItem product = null;
            for (var i = 1; i < parts.Length - 1 && product == null; i++)
            {
                var found = FindByFolderName(parts[i]);
                if (found != null && found.sourceFile == "avatars.json") product = found;
            }

            if (product == null || product.description == null) return result;
            // 服の対応アバター欄に出てくる呼び名のうち、アバター商品の名前・タグに入っているもの
            var vocabulary = ById.Values.Where(v => v.supportedAvatars != null).SelectMany(v => v.supportedAvatars)
                .Where(n => !string.IsNullOrEmpty(n) && n != "ALL").Distinct().ToList();
            var text = Normalize(product.description.name + " " + string.Join(" ", product.description.tags ?? new string[0]));
            foreach (var name in vocabulary)
            {
                var key = Normalize(name);
                if (key.Length >= 2 && text.Contains(key)) result.Add(name);
            }

            return result;
        }

        /// <summary>対応アバター欄が「ALL」（どのアバターでも使える）か。</summary>
        public static bool SupportsAll(KonoAssetItem item)
        {
            return item != null && item.supportedAvatars != null && item.supportedAvatars.Any(a => string.Equals(a, "ALL", StringComparison.OrdinalIgnoreCase));
        }

        public static bool SupportsAvatar(KonoAssetItem item, IEnumerable<string> keywords)
        {
            if (item == null || item.supportedAvatars == null || keywords == null) return false;
            var keys = keywords.Select(Normalize).Where(k => k.Length >= 2).ToArray();
            return item.supportedAvatars.Select(Normalize).Any(a => a.Length >= 2 &&
                keys.Any(k => a == k || a.StartsWith(k, StringComparison.Ordinal) || k.StartsWith(a, StringComparison.Ordinal)));
        }

        public static Texture2D Thumbnail(KonoAssetItem item)
        {
            EnsureLoaded();
            if (_root == null || item == null || item.description == null || string.IsNullOrEmpty(item.description.imageFilename)) return null;
            Texture2D image;
            if (Images.TryGetValue(item.id, out image)) return image;
            image = null;
            try
            {
                var name = item.description.imageFilename;
                if (name != Path.GetFileName(name) || name.Contains("\\") || name.Contains("/")) return null;
                var path = Path.Combine(_root, "images", name);
                if (File.Exists(path))
                {
                    image = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                    if (!image.LoadImage(File.ReadAllBytes(path), true)) { UnityEngine.Object.DestroyImmediate(image); image = null; }
                }
            }
            catch { if (image != null) UnityEngine.Object.DestroyImmediate(image); image = null; }
            // 候補24行と所持30行を収める64件。失敗もキャッシュし、毎描画で再読み込みしない。
            while (Images.Count >= 64)
            {
                var oldest = ImageOrder.Dequeue();
                if (Images[oldest] != null) UnityEngine.Object.DestroyImmediate(Images[oldest]);
                Images.Remove(oldest);
            }
            Images[item.id] = image;
            ImageOrder.Enqueue(item.id);
            return image;
        }

        private static void ClearImages()
        {
            foreach (var image in Images.Values) if (image != null) UnityEngine.Object.DestroyImmediate(image);
            Images.Clear();
            ImageOrder.Clear();
        }
    }
}
