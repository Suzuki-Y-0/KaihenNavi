using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// ツールが作るファイルの置き場所。フォルダの形はここだけで決める（ほかのツールからも探しやすくするため）。
    ///
    /// Assets/KaihenNavi/
    ///   README.md                    この形の説明
    ///   Ledger/items.json           取り込んだ商品の台帳（取り込み元とファイルの場所）
    ///   Bases/
    ///     元アバター名/              元アバター 1 体につき 1 フォルダ（初めて改変アバターを作るときに自動で作る）
    ///       元アバター名 Base.prefab 改変のベース（元アバターのプレハブバリアント）。
    ///                                同じ元アバターから作った改変アバターはすべてこれのバリアントになるので、
    ///                                歩き方（アニメーションコントローラー）など共通の設定はここで変えれば全員に効く
    ///       avatar.json              kind = "base"
    ///   Avatars/
    ///     改変アバター名/            改変アバター 1 体につき 1 フォルダ
    ///       改変アバター名.prefab    改変アバター（ベースのプレハブバリアント）
    ///       avatar.json              ほかのツール向けの説明（元アバター、作った日時など）
    ///       Materials/               色変えなどで複製したマテリアル
    ///       Meshes/                  面を分けた複製メッシュ（必要になったときだけ）
    ///       Textures/                作ったテクスチャ（必要になったときだけ）
    ///       Menus/                   メニュー整理で使う空のメニュー（必要になったときだけ）
    ///       Backups/                 自動バックアップ
    /// </summary>
    internal static class WorkFolders
    {
        public const string Root = "Assets/KaihenNavi";
        public const string AvatarsRoot = Root + "/Avatars";
        public const string BasesRoot = Root + "/Bases";
        public const string BaseSuffix = " Base";
        public const string ManifestName = "avatar.json";
        public const int ManifestFormat = 1;

        // Textures・Menus などは、使うときに初めて作る（空のフォルダを増やさない）
        private static readonly string[] AvatarSubfolders = { "Materials", "Backups" };

        /// <summary>
        /// このアバターのファイルを置くフォルダ。自分用アバターならその Prefab があるフォルダ、
        /// まだなら Assets/KaihenNavi/アバター名。
        /// </summary>
        public static string ForAvatar(GameObject avatar)
        {
            if (avatar != null && PrefabUtility.IsPartOfPrefabInstance(avatar))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(avatar);
                var path = root == null ? null : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (MyAvatar.IsMinePath(path)) return Path.GetDirectoryName(path).Replace('\\', '/');
            }

            return AvatarsRoot + "/" + Sanitize(avatar.name);
        }

        /// <summary>改変アバター用のフォルダを、決まった形で新しく作る。同じ名前があれば番号を付ける。</summary>
        public static string CreateAvatarFolder(string name)
        {
            EnsureReadme();
            Ensure(AvatarsRoot);
            var folder = AssetDatabase.GenerateUniqueAssetPath(AvatarsRoot + "/" + Sanitize(name));
            Ensure(folder);
            foreach (var sub in AvatarSubfolders) Ensure(folder + "/" + sub);
            return folder;
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

        /// <summary>ルートにフォルダの形の説明を置く（無いときだけ）。</summary>
        public static void EnsureReadme()
        {
            Ensure(Root);
            var path = Root + "/README.md";
            var text =
                "# KaihenNavi\n\n" +
                "This folder is managed by KaihenNavi (かいへんナビ). Each edited avatar has its own folder.\n" +
                "かいへんナビが管理するフォルダです。改変アバター 1 体につき 1 フォルダを使います。\n\n" +
                "```\n" +
                "Ledger/\n" +
                "  items.json      Imported packages and file locations / 取り込んだ商品とファイルの場所\n" +
                "Bases/<original>/\n" +
                "  <original> Base.prefab  Shared base (Prefab Variant of the original) / 改変のベース（共通の設定はここ）\n" +
                "  avatar.json             kind = \"base\"\n" +
                "Avatars/<name>/\n" +
                "  <name>.prefab   Edited avatar (Prefab Variant of the base) / 改変アバター\n" +
                "  avatar.json     Info for other tools (format " + ManifestFormat + ") / ほかのツール向けの説明\n" +
                "  Materials/      Copied materials / 複製したマテリアル\n" +
                "  Meshes/         Copied meshes, when needed / 複製したメッシュ（必要なときだけ）\n" +
                "  Textures/       Generated textures, when needed / 作ったテクスチャ（必要なときだけ）\n" +
                "  Menus/          Empty root menu for menu tidying, when needed / メニュー整理用（必要なときだけ）\n" +
                "  Backups/        Automatic backups / 自動バックアップ\n" +
                "  Thumbnail.png   Upload thumbnail / アップロード用サムネイル\n" +
                "```\n\n" +
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
            return ForAvatar(avatar) + "/Backups";
        }

        /// <summary>このアバターのフォルダにある（＝このアバター専用で、書き換えてよい）アセットか。</summary>
        public static bool IsOwnedBy(Object asset, GameObject avatar)
        {
            if (asset == null || avatar == null) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && path.StartsWith(ForAvatar(avatar) + "/");
        }

        internal static bool IsToolPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // パストラバーサルや Root と前方一致する別フォルダを受け付けない。
            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith(Root + "/", System.StringComparison.Ordinal) &&
                !normalized.Split('/').Any(segment => segment == ".." || segment == ".");
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
