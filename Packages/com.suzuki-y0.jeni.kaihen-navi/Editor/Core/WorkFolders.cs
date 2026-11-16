using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// ツールが作るファイルの置き場所。フォルダの形はここだけで決める（ほかのツールからも探しやすくするため）。
    /// 改変プロジェクトの標準の形（AvatarPrefabMaterialExporter の出力と同じ）に合わせる。
    ///
    /// Assets/〈アバター〉_kaihen/          ルート（インポート振り分けの振り分け先と同じ。ImportSorter.RootForAvatar が決める）
    ///   〈用途〉/                          振り分けたアイテム（Cloth・Hair など）
    ///   〈アバター本体〉/ Editor/          配布物・作業用スクリプト（ツールは触らない）
    ///   Variant/
    ///     〈アバター〉_Variant_Base.prefab 改変のベース（元アバターのプレハブバリアント）。同じ元アバターから作った
    ///                                      改変アバターはすべてこれのバリアントになるので、歩き方など共通の設定はここで変える
    ///     〈アバター〉_〈改変名〉/          改変アバター 1 体につき 1 フォルダ（フォルダ名は自由。Blue なども可）
    ///       〈アバター〉_〈改変名〉.prefab 改変アバター（ベースのプレハブバリアント）
    ///       avatar.json                    ほかのツール向けの説明（元アバター、作った日時など）
    ///       Materials/                     複製したマテリアル（〈元の名前〉_〈改変名〉.mat）
    ///       Textures/                      作ったテクスチャ
    ///       Meshes/ Menus/ Animations/     必要になったときだけ作る
    ///       （ユーザーが足した服などの Prefab は触らない）
    ///   KaihenNavi/                        ツールの内部データ
    ///     README.md                        この形の説明
    ///     Ledger/items.json               取り込んだ商品の台帳（プロジェクトで 1 つ）
    ///     Backups/〈改変フォルダ名〉/       自動バックアップ
    ///
    /// 旧構成（Assets/KaihenNavi/{Avatars,Bases,Ledger}）もそのまま開ける。「Variant へ移す」（LayoutMigration）で新しい形へ移せる。
    /// </summary>
    internal static class WorkFolders
    {
        /// <summary>旧構成のルート。新しいアバターはここに作らない（旧構成のまま開くため、読む・保存するのには使う）。</summary>
        public const string Root = "Assets/KaihenNavi";
        public const string AvatarsRoot = Root + "/Avatars";
        public const string BasesRoot = Root + "/Bases";
        public const string BaseSuffix = " Base";
        public const string ManifestName = "avatar.json";
        public const int ManifestFormat = 1;

        /// <summary>ルート直下の、改変アバターとベースを置くフォルダ。</summary>
        public const string VariantName = "Variant";
        /// <summary>ルート直下の、ツールの内部データ（台帳・バックアップ）を置くフォルダ。</summary>
        public const string DataName = "KaihenNavi";
        /// <summary>ベースの Prefab の名前（〈アバター〉_Variant_Base.prefab）。</summary>
        public const string VariantBaseSuffix = "_Variant_Base";
        internal const string LedgerRelative = "/Ledger/items.json";

        // 旧構成：Textures・Menus などは、使うときに初めて作る（空のフォルダを増やさない）
        private static readonly string[] AvatarSubfolders = { "Materials", "Backups" };
        // 新構成：AvatarPrefabMaterialExporter と同じく Materials と Textures を作る（バックアップは KaihenNavi/Backups）
        private static readonly string[] VariantSubfolders = { "Materials", "Textures" };

        // ---------------------------------------------------------------- パスの形（System.IO だけ。Unity なしで確かめられる）

        /// <summary>"/" 区切りの部分。".." や空の部分があれば null（パストラバーサルを受け付けない）。</summary>
        private static string[] Parts(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Replace('\\', '/').Split('/');
            return parts.Any(p => p.Length == 0 || p == ".." || p == ".") ? null : parts;
        }

        /// <summary>パスが入っているルート（Assets/〈アバター〉_kaihen）。ルートの中でなければ null。</summary>
        internal static string KaihenRootOf(string path)
        {
            var parts = Parts(path);
            if (parts == null || parts.Length < 2 || parts[0] != "Assets" || !ImportSorter.IsRootName(parts[1])) return null;
            return "Assets/" + parts[1];
        }

        public static string VariantFolder(string root) { return root + "/" + VariantName; }
        public static string DataFolder(string root) { return root + "/" + DataName; }

        /// <summary>ルートの名前からアバター名（Assets/Cazalis_kaihen → Cazalis）。</summary>
        internal static string AvatarNameOfRoot(string root)
        {
            var name = Path.GetFileName((root ?? "").TrimEnd('/'));
            return ImportSorter.IsRootName(name) ? name.Substring(0, name.Length - ImportSorter.RootSuffix.Length) : name;
        }

        /// <summary>旧構成（Assets/KaihenNavi）の中か。</summary>
        internal static bool IsLegacyPath(string path)
        {
            var parts = Parts(path);
            return parts != null && parts.Length >= 3 && parts[0] == "Assets" && parts[1] == DataName;
        }

        /// <summary>ツールの内部データ（旧構成のルート全体か、〈ルート〉/KaihenNavi）の中か。</summary>
        internal static bool IsDataPath(string path)
        {
            if (IsLegacyPath(path)) return true;
            var parts = Parts(path);
            return parts != null && parts.Length >= 4 && KaihenRootOf(path) != null && parts[2] == DataName;
        }

        /// <summary>
        /// パスが入っている改変フォルダ（Assets/〈ルート〉/Variant/〈フォルダ〉）。改変フォルダの中のファイル・フォルダでなければ null。
        /// Variant 直下のファイル（ベース・ルートメニュー）は改変フォルダに入らない。
        /// </summary>
        internal static string VariantAvatarFolderOf(string path)
        {
            var parts = Parts(path);
            if (parts == null || parts.Length < 5 || KaihenRootOf(path) == null || parts[2] != VariantName) return null;
            return string.Join("/", parts, 0, 4);
        }

        /// <summary>folder が改変フォルダ（Assets/〈ルート〉/Variant/〈フォルダ〉）そのものか。</summary>
        internal static bool IsVariantAvatarFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && VariantAvatarFolderOf(folder + "/_") == folder.Replace('\\', '/');
        }

        /// <summary>改変フォルダの直下の Prefab（Assets/〈ルート〉/Variant/〈フォルダ〉/〈名前〉.prefab）か。</summary>
        internal static bool IsVariantAvatarPath(string path)
        {
            var parts = Parts(path);
            return parts != null && parts.Length == 5 && VariantAvatarFolderOf(path) != null && IsPrefab(path);
        }

        /// <summary>ベース（Assets/〈ルート〉/Variant/〈アバター〉_Variant_Base.prefab。同じ名前があったときの「 1」付きも）か。</summary>
        internal static bool IsVariantBasePath(string path)
        {
            var parts = Parts(path);
            return parts != null && parts.Length == 4 && KaihenRootOf(path) != null && parts[2] == VariantName && IsPrefab(path) &&
                Path.GetFileNameWithoutExtension(parts[3]).Contains(VariantBaseSuffix);
        }

        private static bool IsPrefab(string path)
        {
            return path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 改変フォルダの名前（〈アバター〉_〈改変名〉。AvatarPrefabMaterialExporter と同じ）。
        /// 名前がすでにアバター名で始まっていれば重ねない（「Cazalis 改変」→ Cazalis_改変、「Cazalis_Alice」→ そのまま）。
        /// </summary>
        internal static string FolderName(string avatarName, string kaihenName)
        {
            avatarName = Sanitize(avatarName);
            kaihenName = Sanitize(kaihenName);
            if (string.Equals(kaihenName, avatarName, System.StringComparison.OrdinalIgnoreCase)) return kaihenName;
            if (kaihenName.StartsWith(avatarName + "_", System.StringComparison.OrdinalIgnoreCase)) return kaihenName;
            if (kaihenName.StartsWith(avatarName + " ", System.StringComparison.OrdinalIgnoreCase))
            {
                var rest = kaihenName.Substring(avatarName.Length).Trim();
                if (rest.Length > 0) return avatarName + "_" + rest;
            }
            return avatarName + "_" + kaihenName;
        }

        /// <summary>改変名（Cazalis_Alice → Alice。アバター名で始まらない名前（Blue など）はそのまま）。</summary>
        internal static string ModificationName(string name, string avatarName)
        {
            name = name ?? "";
            if (!string.IsNullOrEmpty(avatarName) && name.Length > avatarName.Length + 1 &&
                name.StartsWith(avatarName + "_", System.StringComparison.OrdinalIgnoreCase))
                return name.Substring(avatarName.Length + 1);
            return name;
        }

        /// <summary>
        /// 複製マテリアルの名前（AvatarPrefabMaterialExporter と同じ：〈元の名前〉_〈改変名〉、重なれば 〈元の名前〉_2_〈改変名〉…）。
        /// display は Material の名前、file は拡張子なしのファイル名。taken はファイル名が使われているか。
        /// </summary>
        internal static void CopyMaterialName(string sourceName, string modification, System.Func<string, bool> taken,
            out string display, out string file)
        {
            for (var number = 1; ; number++)
            {
                display = number == 1 ? sourceName + "_" + modification : sourceName + "_" + number + "_" + modification;
                file = Sanitize(display);
                if (taken == null || !taken(file)) return;
            }
        }

        /// <summary>改変フォルダのバックアップの置き場所。新構成は 〈ルート〉/KaihenNavi/Backups/〈フォルダ名〉、旧構成はフォルダの中。</summary>
        internal static string BackupsFolderFor(string avatarFolder)
        {
            avatarFolder = (avatarFolder ?? "").Replace('\\', '/');
            if (IsVariantAvatarFolder(avatarFolder))
                return DataFolder(KaihenRootOf(avatarFolder)) + "/Backups/" + Path.GetFileName(avatarFolder);
            return avatarFolder + "/Backups";
        }

        /// <summary>
        /// 台帳の場所。旧構成の台帳があればそれ（移すまで使い続ける）。無ければ、ルートの KaihenNavi にある台帳
        /// （今のルートを優先）。どこにも無ければ、今のルート（無ければ最初のルート）に作る。ルートが無ければ旧構成の場所。
        /// </summary>
        internal static string LedgerFileFor(IList<string> roots, string current, System.Func<string, bool> exists)
        {
            if (exists(Root + LedgerRelative)) return Root + LedgerRelative;
            var ordered = new List<string>();
            if (!string.IsNullOrEmpty(current)) ordered.Add(current);
            ordered.AddRange((roots ?? new string[0]).Where(r => r != current));
            foreach (var root in ordered)
                if (exists(DataFolder(root) + LedgerRelative)) return DataFolder(root) + LedgerRelative;
            return ordered.Count == 0 ? Root + LedgerRelative : DataFolder(ordered[0]) + LedgerRelative;
        }

        /// <summary>いまの台帳のファイル（LedgerFileFor）。</summary>
        public static string LedgerFile()
        {
            return LedgerFileFor(ImportSorter.FindRoots(), ImportSorter.CurrentRoot(), File.Exists);
        }

        /// <summary>台帳のファイルから、内部データのフォルダ（README を置く場所）。</summary>
        internal static string DataFolderOfLedger(string ledger)
        {
            return ledger.Substring(0, ledger.Length - LedgerRelative.Length);
        }

        /// <summary>台帳と README（ツールが書き換える内部データのファイル）か。旧構成と各ルートの KaihenNavi のもの。</summary>
        internal static bool IsManagedDataFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var p = path.EndsWith(".meta", System.StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
            var root = KaihenRootOf(p);
            foreach (var data in root == null ? new[] { Root } : new[] { Root, DataFolder(root) })
                if (p == data + LedgerRelative || p == data + "/README.md") return true;
            return false;
        }

        /// <summary>今あるルート（Assets 直下の *_kaihen）。</summary>
        private static List<string> Roots()
        {
            return ImportSorter.FindRoots().Where(AssetDatabase.IsValidFolder).ToList();
        }

        /// <summary>内部データのフォルダのうち、今あるもの（旧構成のルートと、各ルートの KaihenNavi）。バックアップを探すのに使う。</summary>
        public static string[] DataFolders()
        {
            var result = new List<string>();
            if (AssetDatabase.IsValidFolder(Root)) result.Add(Root);
            foreach (var root in Roots())
                if (AssetDatabase.IsValidFolder(DataFolder(root))) result.Add(DataFolder(root));
            return result.ToArray();
        }

        /// <summary>ツールが作るファイルのあるフォルダのうち、今あるもの（内部データと各ルートの Variant）。</summary>
        public static string[] ToolFolders()
        {
            var result = DataFolders().ToList();
            foreach (var root in Roots())
                if (AssetDatabase.IsValidFolder(VariantFolder(root))) result.Add(VariantFolder(root));
            return result.ToArray();
        }

        // ---------------------------------------------------------------- アバターごとの場所

        /// <summary>
        /// このアバターのファイルを置くフォルダ。改変アバターならその Prefab があるフォルダ（新構成なら Variant/〈フォルダ〉）。
        /// まだなら、旧構成のフォルダ（前の版で複製を置いたもの）があればそれ、無ければ 〈ルート〉/Variant/〈アバター〉_〈名前〉。
        /// </summary>
        public static string ForAvatar(GameObject avatar)
        {
            string originalPath = null;
            if (avatar != null && PrefabUtility.IsPartOfPrefabInstance(avatar))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(avatar);
                var path = root == null ? null : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (MyAvatar.IsMinePath(path)) return Path.GetDirectoryName(path).Replace('\\', '/');
                originalPath = path;
            }

            var name = Sanitize(avatar == null ? null : avatar.name);
            var legacy = AvatarsRoot + "/" + name;
            if (AssetDatabase.IsValidFolder(legacy)) return legacy;
            string kaihenRoot, avatarName;
            Placement(originalPath, name, out kaihenRoot, out avatarName);
            return VariantFolder(kaihenRoot) + "/" + FolderName(avatarName, name);
        }

        /// <summary>
        /// 元アバター（かベース）の Prefab のパスから、ルートとアバター名を決める（インポート振り分けと同じルートの決め方）。
        /// ベースを渡したときは、ベースの名前（〈アバター〉_Variant_Base）のアバター名とそのルートを使う。
        /// </summary>
        internal static void Placement(string originalPath, string fallbackName, out string root, out string avatarName)
        {
            avatarName = IsVariantBasePath(originalPath) ? AvatarNameOfBase(originalPath)
                : ImportSorter.AvatarNameFromPath(originalPath, Sanitize(fallbackName));
            root = ImportSorter.RootForAvatar(originalPath, avatarName);
        }

        /// <summary>ベースの名前からアバター名（Cazalis_Variant_Base 1.prefab → Cazalis）。</summary>
        internal static string AvatarNameOfBase(string basePath)
        {
            var name = Path.GetFileNameWithoutExtension(basePath ?? "");
            var index = name.IndexOf(VariantBaseSuffix, System.StringComparison.Ordinal);
            return index > 0 ? name.Substring(0, index) : AvatarNameOfRoot(KaihenRootOf(basePath));
        }

        /// <summary>改変名（複製マテリアルの名前の末尾に付ける）。旧構成のフォルダなら null（前と同じ名前のまま複製する）。</summary>
        internal static string ModificationNameFor(GameObject avatar)
        {
            var folder = ForAvatar(avatar);
            if (!IsVariantAvatarFolder(folder)) return null;
            var name = Path.GetFileName(folder);
            if (avatar != null && PrefabUtility.IsPartOfPrefabInstance(avatar))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(avatar);
                var path = root == null ? null : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                // Exporter の形（Blue/Cazalis_Blue.prefab）では、フォルダ名より Prefab の名前が 〈アバター〉_〈改変名〉
                if (MyAvatar.IsMinePath(path)) name = Path.GetFileNameWithoutExtension(path);
            }
            return ModificationName(name, AvatarNameOfRoot(KaihenRootOf(folder)));
        }

        /// <summary>改変アバター用のフォルダを、旧構成（Assets/KaihenNavi/Avatars）に新しく作る。旧構成の改変アバターの複製だけに使う。</summary>
        public static string CreateAvatarFolder(string name)
        {
            EnsureReadme(Root);
            Ensure(AvatarsRoot);
            var folder = AssetDatabase.GenerateUniqueAssetPath(AvatarsRoot + "/" + Sanitize(name));
            Ensure(folder);
            foreach (var sub in AvatarSubfolders) Ensure(folder + "/" + sub);
            return folder;
        }

        /// <summary>改変フォルダ（〈ルート〉/Variant/〈アバター〉_〈改変名〉）を新しく作る。同じ名前があれば番号を付ける。</summary>
        public static string CreateAvatarFolder(string root, string avatarName, string kaihenName)
        {
            EnsureReadme(DataFolder(root));
            Ensure(VariantFolder(root));
            var folder = AssetDatabase.GenerateUniqueAssetPath(VariantFolder(root) + "/" + FolderName(avatarName, kaihenName));
            Ensure(folder);
            EnsureSubfolders(folder);
            return folder;
        }

        /// <summary>改変フォルダの決まったサブフォルダを作る（新構成は Materials・Textures、旧構成は Materials・Backups）。</summary>
        public static void EnsureSubfolders(string folder)
        {
            if (IsVariantAvatarFolder(folder)) EnsureReadme(DataFolder(KaihenRootOf(folder)));
            else EnsureReadme(Root);
            foreach (var sub in IsVariantAvatarFolder(folder) ? VariantSubfolders : AvatarSubfolders) Ensure(folder + "/" + sub);
        }

        /// <summary>ほかのツール向けの説明ファイル（avatar.json）を書く。</summary>
        public static void WriteManifest(string prefabPath, GameObject basePrefab, GameObject parentPrefab = null, string kind = "avatar")
        {
            var folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var parentPath = parentPrefab == null ? "" : AssetDatabase.GetAssetPath(parentPrefab);
            var manifest = new AvatarManifest
            {
                format = ManifestFormat,
                kind = kind,
                parentPrefabPath = parentPath,
                parentPrefabGuid = parentPath.Length == 0 ? "" : AssetDatabase.AssetPathToGUID(parentPath),
                tool = "KaihenNavi " + Edition.Version,
                name = Path.GetFileNameWithoutExtension(prefabPath),
                prefab = Path.GetFileName(prefabPath),
                basePrefabPath = basePrefab == null ? "" : AssetDatabase.GetAssetPath(basePrefab),
                basePrefabGuid = basePrefab == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(basePrefab)),
                createdAt = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            };

            var old = ReadManifest(folder);
            if (old != null && !string.IsNullOrEmpty(old.createdAt)) manifest.createdAt = old.createdAt;
            if (old != null && basePrefab == null)
            {
                manifest.basePrefabPath = old.basePrefabPath;
                manifest.basePrefabGuid = old.basePrefabGuid;
            }

            if (old != null && parentPrefab == null)
            {
                manifest.parentPrefabPath = old.parentPrefabPath;
                manifest.parentPrefabGuid = old.parentPrefabGuid;
                if (!string.IsNullOrEmpty(old.kind)) manifest.kind = old.kind;
            }

            var path = folder + "/" + ManifestName;
            File.WriteAllText(path, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }

        public static AvatarManifest ReadManifest(string folder)
        {
            var path = folder + "/" + ManifestName;
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<AvatarManifest>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>内部データのフォルダ（旧構成のルートか 〈ルート〉/KaihenNavi）に、フォルダの形の説明を置く（形が変わったら書き直す）。</summary>
        public static void EnsureReadme(string dataFolder)
        {
            Ensure(dataFolder);
            var path = dataFolder + "/README.md";
            var text =
                "# KaihenNavi\n\n" +
                "This folder holds KaihenNavi (かいへんナビ) data. Edited avatars live in the Variant folder next to it.\n" +
                "かいへんナビの内部データです。改変アバターは隣の Variant フォルダにあります。\n\n" +
                "```\n" +
                "Assets/<avatar>_kaihen/\n" +
                "  Variant/\n" +
                "    <avatar>_Variant_Base.prefab  Shared base (Prefab Variant of the original) / 改変のベース（共通の設定はここ）\n" +
                "    <avatar>_<name>/              One folder per edited avatar / 改変アバター 1 体につき 1 フォルダ\n" +
                "      <avatar>_<name>.prefab      Edited avatar (Prefab Variant of the base) / 改変アバター\n" +
                "      avatar.json                 Info for other tools (format " + ManifestFormat + ") / ほかのツール向けの説明\n" +
                "      Materials/                  Copied materials (<material>_<name>.mat) / 複製したマテリアル\n" +
                "      Textures/                   Generated textures / 作ったテクスチャ\n" +
                "      Meshes/ Menus/ Animations/  Created when needed / 必要なときだけ\n" +
                "      Thumbnail.png               Upload thumbnail / アップロード用サムネイル\n" +
                "  KaihenNavi/\n" +
                "    Ledger/items.json             Imported packages and file locations / 取り込んだ商品とファイルの場所\n" +
                "    Backups/<folder>/             Automatic backups / 自動バックアップ\n" +
                "```\n\n" +
                "Older versions used Assets/KaihenNavi/{Avatars,Bases,Ledger}. \"Move to Variant\" in KaihenNavi moves them here.\n" +
                "前の版は Assets/KaihenNavi/{Avatars,Bases,Ledger} を使っていました。かいへんナビの「Variant へ移す」で移せます。\n\n" +
                "The original avatar files are never modified. / 元のアバターのファイルは変更しません。\n";
            // ツールが管理する説明なので、形が変わったら書き直す
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return;
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }

        public static string Materials(GameObject avatar)
        {
            return ForAvatar(avatar) + "/Materials";
        }

        public static string Backups(GameObject avatar)
        {
            return BackupsFolderFor(ForAvatar(avatar));
        }

        /// <summary>
        /// このアバターのフォルダにある（＝このアバター専用で、書き換えてよい）アセットか。フォルダで決めるので、
        /// AvatarPrefabMaterialExporter などで作った改変フォルダの Materials もこのアバターのもの。
        /// ベース（Variant 直下）とほかの改変フォルダのアセットは含めない。
        /// </summary>
        public static bool IsOwnedBy(Object asset, GameObject avatar)
        {
            if (asset == null || avatar == null) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && path.StartsWith(ForAvatar(avatar) + "/");
        }

        /// <summary>
        /// ツールが作る・書き換えてよいファイルの場所か：旧構成のルートの中、〈ルート〉/KaihenNavi の中、
        /// 改変フォルダ（Variant/〈フォルダ〉）の中、ベース。Variant 直下のほかのファイル（ルートメニューなど）と、
        /// 配布物・用途フォルダは含めない。消す処理はさらに印（userData）や名前で自分の作ったものに絞る。
        /// </summary>
        internal static bool IsToolPath(string path)
        {
            // パストラバーサルや Root と前方一致する別フォルダは Parts・各判定が受け付けない
            return IsLegacyPath(path) || IsDataPath(path) || VariantAvatarFolderOf(path) != null || IsVariantBasePath(path);
        }

        /// <summary>メニュー整理が作ったメニュー（改変フォルダの Menus か旧構成の中）か。ユーザーが改変フォルダに置いたメニューは含めない。</summary>
        internal static bool IsToolMenuPath(string path)
        {
            if (IsLegacyPath(path)) return true;
            var parts = Parts(path);
            return parts != null && parts.Length >= 6 && VariantAvatarFolderOf(path) != null && parts[4] == "Menus";
        }

        /// <summary>このツールが作ったアセットかどうか。</summary>
        public static bool IsOwned(Object asset)
        {
            if (asset == null) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            return IsToolPath(path);
        }

        /// <summary>フォルダを（親も含めて）作る。</summary>
        public static string Ensure(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return folder;

            var parent = Path.GetDirectoryName(folder);
            parent = parent == null ? "Assets" : parent.Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            return folder;
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Avatar";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(System.Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' ? '_' : c);
            }

            var result = sb.ToString().Trim().TrimEnd('.');
            return result.Length == 0 ? "Avatar" : result;
        }
    }

    /// <summary>avatar.json の中身。ほかのツールが読むので、項目は増やしても消さない。</summary>
    [System.Serializable]
    internal sealed class AvatarManifest
    {
        public int format;
        /// <summary>"avatar"（改変アバター）か "base"（改変のベース）。</summary>
        public string kind;
        public string tool;
        public string name;
        public string prefab;
        public string basePrefabPath;
        public string basePrefabGuid;
        public string createdAt;
        /// <summary>直接の親（改変アバターならベース）。ベースなら元アバターと同じ。</summary>
        public string parentPrefabPath;
        public string parentPrefabGuid;
    }
}
