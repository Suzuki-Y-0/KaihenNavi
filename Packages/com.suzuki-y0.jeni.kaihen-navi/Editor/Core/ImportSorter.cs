using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>移すとパスが変わって動かなくなるかもしれない理由。</summary>
    internal enum SortRisk { Script, ShaderInclude, SpecialFolder }

    /// <summary>振り分けの対象 1 件（取り込んだアイテムのフォルダ）。窓がドメインリロードをまたいで持つので Serializable。</summary>
    [Serializable]
    internal sealed class SortTarget
    {
        /// <summary>移すフォルダ（Assets/ショップ/アイテム か Assets/アイテム）。</summary>
        public string source;
        /// <summary>入っていたショップのフォルダ。ショップ階層でなければ空。</summary>
        public string shop;
        public int fileCount;
        public List<SortRisk> risks = new List<SortRisk>();
        /// <summary>窓で選んだ用途の名前。空なら未選択、NotMove なら移動しない。</summary>
        public string choice = "";

        public const string NotMove = "\n";
        public string ItemName { get { return Path.GetFileName(source); } }
        public string ShopName { get { return string.IsNullOrEmpty(shop) ? "" : Path.GetFileName(shop); } }
    }

    /// <summary>1 つのフォルダの移動（移動前と移動後のパス）。</summary>
    [Serializable]
    internal sealed class SortMove
    {
        public string from;
        public string to;
    }

    /// <summary>
    /// 取り込み直後に「どの用途に入れますか？」と聞き、Assets/〈アバター〉_kaihen/〈用途〉/〈アイテム〉/ へ移す。
    /// 判定は System.IO だけを使う静的な関数にまとめ、Unity なしで確かめられるようにする（tools/tests/ImportSortTests.cs）。
    /// 移動そのものと Ctrl+Z は ImportSortUndo、画面は ImportSortWindow と PlacementFeature。
    /// </summary>
    [InitializeOnLoad]
    internal static class ImportSorter
    {
        // 用途の一覧と ON/OFF は PC ごと（EditorPrefs）。振り分け先ルートはプロジェクトごと（UserSettings/、Assets には置かない）
        private const string EnabledKey = "KaihenNavi.ImportSort.Enabled";
        private const string CategoriesKey = "KaihenNavi.ImportSort.Categories";
        private const string RootKey = "KaihenNavi.ImportSort.Root";
        // 取り込み完了から窓を開くまでの間にドメインリロードされても続けるための控え
        private const string PendingKey = "KaihenNavi.ImportSort.Pending";
        public const string RootSuffix = "_kaihen";

        public static readonly string[] DefaultCategories =
            { "Accessory", "Cloth", "Gimmick", "Hair", "My_Mask", "Rim_Matcap", "Texture", "Weapon" };

        // 取り込みでも手動でも振り分けない Assets 直下のフォルダ（大文字小文字は無視）
        private static readonly HashSet<string> ExcludedTops = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "KaihenNavi", "EditorEye.app", "VRCSDK", "Plugins", "TextMesh Pro", "Settings", "Resources", "StreamingAssets",
            "Editor Default Resources", "Gizmos", "Standard Assets",
        };

        // ルート直下で用途フォルダとして扱わない名前
        private static readonly HashSet<string> NonCategory = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Variant", "Editor" };

        // パスに依存しうる特別なフォルダ（Unity が名前で扱いを変える）
        private static readonly HashSet<string> SpecialFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Resources", "Editor Default Resources", "Plugins", "StreamingAssets", "Gizmos" };

        private static readonly HashSet<string> ScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".js", ".asmdef", ".asmref", ".dll" };

        private static readonly HashSet<string> ShaderExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".shader", ".cginc", ".hlsl", ".compute" };

        private static readonly Regex AbsoluteInclude = new Regex("#\\s*include(_with_pragmas)?\\s*\"Assets/", RegexOptions.Compiled);

        // 中身がフォルダだけでも、この名前のフォルダがあれば「ショップ階層」ではなく 1 つのアイテム
        // （Assets/アイテム/{FBX, Materials, Prefab, Texture} のような配布物があるため）
        private static readonly HashSet<string> AssetTypeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fbx", "model", "models", "mesh", "meshes", "prefab", "prefabs", "material", "materials", "mat", "texture", "textures",
            "tex", "animation", "animations", "anim", "animator", "animators", "shader", "shaders", "script", "scripts", "editor",
            "sound", "sounds", "audio", "particle", "particles", "menu", "menus", "expressions", "fx", "controller", "controllers",
            "unitypackage", "psd", "blend", "documents", "document", "readme", "license", "materials_liltoon", "mats",
        };

        static ImportSorter()
        {
            // 完了通知のあとの遅延処理の前にドメインリロードされた場合も、窓を開く
            if (SessionState.GetString(PendingKey, "").Length > 0) Schedule(0);
        }

        // ---------------------------------------------------------------- 設定

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(EnabledKey, true); }
            set { EditorPrefs.SetBool(EnabledKey, value); }
        }

        public static List<string> Categories
        {
            get { return ParseCategories(EditorPrefs.GetString(CategoriesKey, "")); }
            set { EditorPrefs.SetString(CategoriesKey, FormatCategories(value)); }
        }

        /// <summary>複数のルートがあるときに選んだもの（プロジェクトごと）。</summary>
        public static string SavedRoot
        {
            get { return EditorUserSettings.GetConfigValue(RootKey) ?? ""; }
            set { EditorUserSettings.SetConfigValue(RootKey, value ?? ""); }
        }

        /// <summary>いまの振り分け先ルート。無いか、複数あって選ばれていなければ null。</summary>
        public static string CurrentRoot()
        {
            return ChooseRoot(FindRoots(), SavedRoot);
        }

        internal static List<string> ParseCategories(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return DefaultCategories.ToList();
            var result = new List<string>();
            foreach (var name in saved.Split('\n').Select(n => n.Trim()))
                if (name.Length > 0 && !result.Contains(name, StringComparer.OrdinalIgnoreCase)) result.Add(name);
            return result;
        }

        internal static string FormatCategories(IEnumerable<string> categories)
        {
            var list = (categories ?? new string[0]).Where(c => !string.IsNullOrEmpty(c)).ToList();
            // 空の一覧を保存すると初期値に戻ってしまうので、空のときは改行だけを残す
            return list.Count == 0 ? "\n" : string.Join("\n", list);
        }

        /// <summary>用途の名前として使えるか（フォルダ名にできる・重複しない・特別な名前でない）。</summary>
        internal static bool IsValidCategoryName(string name, IEnumerable<string> existing)
        {
            if (string.IsNullOrEmpty(name) || name.Trim() != name || WorkFolders.Sanitize(name) != name) return false;
            if (NonCategory.Contains(name) || SpecialFolders.Contains(name)) return false;
            return !(existing ?? new string[0]).Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------- ルートと用途フォルダ

        internal static bool IsRootName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length > RootSuffix.Length && name.EndsWith(RootSuffix, StringComparison.Ordinal);
        }

        /// <summary>Assets 直下で名前が _kaihen で終わるフォルダ。</summary>
        internal static List<string> FindRoots()
        {
            if (!Directory.Exists("Assets")) return new List<string>();
            return Directory.GetDirectories("Assets").Select(Normalize).Where(d => IsRootName(Path.GetFileName(d)))
                .OrderBy(d => d, StringComparer.Ordinal).ToList();
        }

        /// <summary>1 つならそれ。複数なら覚えているもの。それ以外は null（窓で選ばせる）。</summary>
        internal static string ChooseRoot(IList<string> roots, string saved)
        {
            if (roots == null || roots.Count == 0) return null;
            if (roots.Count == 1) return roots[0];
            return roots.FirstOrDefault(r => r == saved);
        }

        /// <summary>新しく作るルートのパス（Assets/〈アバター名〉_kaihen）。</summary>
        internal static string NewRootPath(string avatarName)
        {
            var name = WorkFolders.Sanitize(avatarName);
            return "Assets/" + (IsRootName(name) ? name : name + RootSuffix);
        }

        /// <summary>
        /// アバターの Prefab のパスから、ルートの名前に使うアバター名を推す（Assets/Cazalis/... → Cazalis）。
        /// すでにルートの中にあればアバター本体フォルダの名前。分からなければ fallback。
        /// </summary>
        internal static string AvatarNameFromPath(string assetPath, string fallback)
        {
            var parts = (assetPath ?? "").Replace('\\', '/').Split('/');
            if (parts.Length >= 3 && parts[0] == "Assets" && parts[1] != "KaihenNavi")
            {
                if (!IsRootName(parts[1])) return parts[1];
                if (parts.Length >= 4) return parts[1].Substring(0, parts[1].Length - RootSuffix.Length);
            }
            return fallback;
        }

        /// <summary>用途の実フォルダ。ルート直下に同じ名前（大文字小文字は無視）があればそれ、無ければ移すときに作る場所。</summary>
        internal static string ResolveCategory(string root, string category)
        {
            if (Directory.Exists(root))
                foreach (var directory in Directory.GetDirectories(root).Select(Normalize))
                    if (string.Equals(Path.GetFileName(directory), category, StringComparison.OrdinalIgnoreCase)) return directory;
            return root + "/" + category;
        }

        /// <summary>ルート直下にあって一覧に無いフォルダ（Variant・Editor・直下にファイルがあるアバター本体フォルダは除く）。</summary>
        internal static List<string> UnregisteredFolders(string root, IEnumerable<string> categories)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return result;
            var known = new HashSet<string>(categories ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.GetDirectories(root).Select(Normalize).OrderBy(d => d, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(directory);
                if (known.Contains(name) || NonCategory.Contains(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                if (HasFiles(directory)) continue;
                result.Add(name);
            }
            return result;
        }

        // ---------------------------------------------------------------- 対象の判定

        /// <summary>振り分けない Assets 直下のフォルダか。</summary>
        internal static bool IsExcludedTop(string name)
        {
            return string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal) || ExcludedTops.Contains(name) || IsRootName(name);
        }

        /// <summary>取り込んだパスの、Assets 直下のフォルダ名（除外と直下のファイルだけの取り込みは外す）。</summary>
        internal static List<string> TopFoldersOf(IEnumerable<string> importedPaths)
        {
            var result = new List<string>();
            foreach (var raw in importedPaths ?? new string[0])
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var parts = Normalize(raw).Split('/');
                // Assets/foo.prefab や Assets/Shop（フォルダ自身）だけでは対象にしない
                if (parts.Length < 3 || parts[0] != "Assets") continue;
                if (IsExcludedTop(parts[1]) || result.Contains(parts[1])) continue;
                result.Add(parts[1]);
            }
            return result;
        }

        /// <summary>中身がフォルダだけ（.meta 以外のファイルが無い）で、アイテムの中身らしい名前のフォルダが無ければショップ階層。</summary>
        internal static bool IsShopLayer(string folder)
        {
            if (!Directory.Exists(folder) || HasFiles(folder)) return false;
            var children = Directory.GetDirectories(folder).Select(Path.GetFileName).Where(n => !n.StartsWith(".", StringComparison.Ordinal)).ToList();
            return children.Count > 0 && !children.Any(n => AssetTypeFolders.Contains(n));
        }

        /// <summary>
        /// 取り込んだパスから振り分けの対象を作る。importedPaths が null なら手動（Assets 直下の全部のアイテム）。
        /// ショップ階層なら、今回取り込んだアイテムのフォルダだけを対象にする（以前の取り込みはショップに残す）。
        /// </summary>
        internal static List<SortTarget> BuildTargets(IEnumerable<string> importedPaths)
        {
            var result = new List<SortTarget>();
            List<string> tops;
            HashSet<string> imported = null;
            if (importedPaths == null)
            {
                tops = Directory.Exists("Assets") ? Directory.GetDirectories("Assets").Select(Path.GetFileName)
                    .Where(n => !IsExcludedTop(n) && !string.Equals(n, "Scenes", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(n, "Samples", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
            }
            else
            {
                imported = new HashSet<string>(importedPaths.Where(p => !string.IsNullOrEmpty(p)).Select(Normalize), StringComparer.Ordinal);
                tops = TopFoldersOf(imported);
            }
            foreach (var top in tops) result.AddRange(TargetsForFolder("Assets/" + top, imported));
            return result;
        }

        /// <summary>Assets 直下の 1 フォルダ、または Assets/ショップ/アイテム を対象にする（手動メニュー用も兼ねる）。</summary>
        internal static List<SortTarget> TargetsForFolder(string folder, ICollection<string> imported)
        {
            var result = new List<SortTarget>();
            folder = Normalize(folder).TrimEnd('/');
            if (!Directory.Exists(folder)) return result;
            var parts = folder.Split('/');
            if (parts.Length < 2 || parts[0] != "Assets" || IsExcludedTop(parts[1])) return result;
            if (parts.Length == 3)
            {
                // ショップの中のアイテムを 1 つだけ選んだとき
                result.Add(Describe(folder, "Assets/" + parts[1]));
                return result;
            }
            if (parts.Length != 2) return result;
            if (!IsShopLayer(folder))
            {
                result.Add(Describe(folder, ""));
                return result;
            }
            foreach (var item in Directory.GetDirectories(folder).Select(Normalize).OrderBy(d => d, StringComparer.Ordinal))
            {
                if (Path.GetFileName(item).StartsWith(".", StringComparison.Ordinal)) continue;
                if (imported != null && !imported.Any(p => p == item || p.StartsWith(item + "/", StringComparison.Ordinal))) continue;
                result.Add(Describe(item, folder));
            }
            return result;
        }

        private static SortTarget Describe(string source, string shop)
        {
            var target = new SortTarget { source = source, shop = shop, fileCount = CountFiles(source), risks = DetectRisks(source) };
            if (target.risks.Count > 0) target.choice = SortTarget.NotMove;
            return target;
        }

        internal static int CountFiles(string folder)
        {
            if (!Directory.Exists(folder)) return 0;
            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Count(f => !IsMeta(f));
        }

        /// <summary>移すとパスが変わって動かなくなるかもしれないものを調べる。VRCFury・MA・Prefab・Material は GUID で参照するので問題ない。</summary>
        internal static List<SortRisk> DetectRisks(string folder)
        {
            var risks = new List<SortRisk>();
            if (!Directory.Exists(folder)) return risks;
            var directories = new[] { folder }.Concat(Directory.GetDirectories(folder, "*", SearchOption.AllDirectories));
            if (directories.Any(d => SpecialFolders.Contains(Path.GetFileName(d.TrimEnd('/', '\\'))))) risks.Add(SortRisk.SpecialFolder);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                if (ScriptExtensions.Contains(extension) && !risks.Contains(SortRisk.Script)) risks.Add(SortRisk.Script);
                if (ShaderExtensions.Contains(extension) && !risks.Contains(SortRisk.ShaderInclude) && HasAbsoluteInclude(file))
                    risks.Add(SortRisk.ShaderInclude);
            }
            risks.Sort();
            return risks;
        }

        private static bool HasAbsoluteInclude(string file)
        {
            try
            {
                // 大きすぎるファイルは読まない（シェーダーのソースは普通そこまで大きくない）
                if (new FileInfo(file).Length > 8 * 1024 * 1024) return false;
                return AbsoluteInclude.IsMatch(File.ReadAllText(file));
            }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- 移動の計画と、パスの書き換え

        /// <summary>
        /// 移動の計画を作る。作るフォルダ（ルート・用途フォルダ。親から順）と、移動（同じ名前があれば「名前 1」）。
        /// choices は 〈対象〉→〈用途の名前〉。移動しない対象は入れない。
        /// </summary>
        internal static void Plan(string root, IList<KeyValuePair<SortTarget, string>> choices,
            out List<string> createFolders, out List<SortMove> moves, out List<string> shops)
        {
            createFolders = new List<string>();
            moves = new List<SortMove>();
            shops = new List<string>();
            if (!Directory.Exists(root)) createFolders.Add(root);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in choices)
            {
                var target = pair.Key;
                if (target == null || string.IsNullOrEmpty(pair.Value) || pair.Value == SortTarget.NotMove) continue;
                var category = ResolveCategory(root, pair.Value);
                if (!Directory.Exists(category) && !createFolders.Contains(category)) createFolders.Add(category);
                var destination = UniquePath(category + "/" + target.ItemName, taken);
                taken.Add(destination);
                moves.Add(new SortMove { from = target.source, to = destination });
                if (!string.IsNullOrEmpty(target.shop) && !shops.Contains(target.shop)) shops.Add(target.shop);
            }
        }

        /// <summary>AssetDatabase.GenerateUniqueAssetPath と同じ「名前 1」「名前 2」の形。まだ無いフォルダの中でも使えるよう、ディスクと計画中のパスで調べる。</summary>
        internal static string UniquePath(string path, ICollection<string> taken)
        {
            Func<string, bool> used = p => Directory.Exists(p) || File.Exists(p) || (taken != null && taken.Contains(p));
            if (!used(path)) return path;
            for (var i = 1; ; i++)
            {
                var candidate = path + " " + i;
                if (!used(candidate)) return candidate;
            }
        }

        /// <summary>移動に合わせてパスを書き換える（フォルダ自身・その .meta・中のファイル）。関係ないパスはそのまま。</summary>
        internal static string RewritePath(string path, IEnumerable<SortMove> moves, bool forward)
        {
            if (string.IsNullOrEmpty(path)) return path;
            foreach (var move in moves)
            {
                var from = forward ? move.from : move.to;
                var to = forward ? move.to : move.from;
                if (path == from) return to;
                if (path == from + ".meta") return to + ".meta";
                if (path.StartsWith(from + "/", StringComparison.Ordinal)) return to + path.Substring(from.Length);
            }
            return path;
        }

        // ---------------------------------------------------------------- 取り込みから窓まで

        [Serializable]
        private sealed class PendingSort
        {
            public List<string> paths = new List<string>();
        }

        /// <summary>
        /// ItemLedger が取り込みを台帳に記録した直後に呼ぶ。取り込みは止めず、窓は取り込みの Undo の記録が済んでから開く。
        /// 機能が OFF のときと、対象が無いときは何もしない。
        /// </summary>
        internal static void OnImported(IEnumerable<string> importedPaths)
        {
            try
            {
                if (!Enabled) return;
                var paths = (importedPaths ?? new string[0]).Where(p => !string.IsNullOrEmpty(p)).Select(Normalize).ToList();
                if (TopFoldersOf(paths).Count == 0) return;
                SessionState.SetString(PendingKey, JsonUtility.ToJson(new PendingSort { paths = paths }));
                Schedule(0);
            }
            catch (Exception e) { Debug.LogWarning("[KaihenNavi] Import sort: " + e.Message); }
        }

        private static void Schedule(int tries)
        {
            EditorApplication.delayCall += () => OpenPending(tries);
        }

        private static void OpenPending(int tries)
        {
            try
            {
                // 取り込みの Undo（PackageImportUndo の delayCall）の後、コンパイルや取り込みが終わってから開く
                if (tries < 600 && (EditorApplication.isCompiling || EditorApplication.isUpdating || PackageImportUndo.IsPending || tries == 0))
                {
                    Schedule(tries + 1);
                    return;
                }
                var json = SessionState.GetString(PendingKey, "");
                SessionState.EraseString(PendingKey);
                if (json.Length == 0 || !Enabled) return;
                var pending = JsonUtility.FromJson<PendingSort>(json);
                var targets = BuildTargets(pending == null ? new List<string>() : pending.paths);
                if (targets.Count == 0) return;
                ImportSortWindow.Open(targets);
            }
            catch (Exception e) { Debug.LogWarning("[KaihenNavi] Import sort: " + e.Message); }
        }

        // ---------------------------------------------------------------- 手動メニュー

        private const string MenuPath = "Assets/かいへんナビ/アイテムを振り分ける…";

        /// <summary>Project で選んだフォルダを振り分ける（「Tools/かいへんナビ」は窓を開く項目なので、その下には置けない）。</summary>
        [MenuItem(MenuPath, false, 1200)]
        private static void SortSelected()
        {
            var targets = SelectedTargets();
            if (targets.Count == 0) return;
            ImportSortWindow.Open(targets);
        }

        [MenuItem(MenuPath, true, 1200)]
        private static bool CanSortSelected()
        {
            // 右クリックのたびに中身を数えない（大きいフォルダで重くなるため）。場所だけで判断する
            return (Selection.assetGUIDs ?? new string[0]).Select(AssetDatabase.GUIDToAssetPath).Any(CanSortFolder);
        }

        /// <summary>手動で振り分けられる場所か（Assets/アイテム か Assets/ショップ/アイテム。除外とルートの中は不可）。</summary>
        internal static bool CanSortFolder(string path)
        {
            var parts = Normalize(path).TrimEnd('/').Split('/');
            return (parts.Length == 2 || parts.Length == 3) && parts[0] == "Assets" && !IsExcludedTop(parts[1]) && Directory.Exists(Normalize(path));
        }

        private static List<SortTarget> SelectedTargets()
        {
            var result = new List<SortTarget>();
            foreach (var guid in Selection.assetGUIDs ?? new string[0])
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) continue;
                foreach (var target in TargetsForFolder(path, null))
                    if (!result.Any(t => t.source == target.source)) result.Add(target);
            }
            return result;
        }

        // ---------------------------------------------------------------- 移動（Ctrl+Z 1 回で戻る）

        /// <summary>
        /// 選んだ用途へまとめて移す。root が無ければ作る。Ctrl+Z 1 回で全部戻る。
        /// 戻り値は失敗の理由（成功なら null）。移したものは moved に入る。
        /// </summary>
        internal static string Move(string root, IList<KeyValuePair<SortTarget, string>> choices, out int moved)
        {
            moved = 0;
            if (string.IsNullOrEmpty(root)) return L10n.Tr("sort.noRoot");
            List<string> createFolders;
            List<SortMove> moves;
            List<string> shops;
            Plan(root, choices, out createFolders, out moves, out shops);
            if (moves.Count == 0 && createFolders.Count == 0) return null;
            var error = ImportSortUndo.Run(createFolders, moves, shops, L10n.Tr("undo.importSort"));
            if (error == null) moved = moves.Count;
            return error;
        }

        /// <summary>ルートだけを作る（Ctrl+Z で消える）。用途フォルダは使うときに作る。</summary>
        internal static string CreateRoot(string avatarName)
        {
            var root = NewRootPath(avatarName);
            if (Directory.Exists(root)) return null;
            return ImportSortUndo.Run(new List<string> { root }, new List<SortMove>(), new List<string>(), L10n.Tr("undo.importSortRoot"));
        }

        // ---------------------------------------------------------------- 小さな道具

        internal static string Normalize(string path)
        {
            return (path ?? "").Replace('\\', '/');
        }

        private static bool IsMeta(string path)
        {
            return path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasFiles(string folder)
        {
            return Directory.GetFiles(folder).Any(f => !IsMeta(f) && !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal));
        }
    }
}
