using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>ツールが作るファイルの置き場所（Assets/KaihenNavi/アバター名/...）。</summary>
    internal static class WorkFolders
    {
        public const string Root = "Assets/KaihenNavi";

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

            return Root + "/" + Sanitize(avatar.name);
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

        /// <summary>このツールが作ったアセットかどうか。</summary>
        public static bool IsOwned(Object asset)
        {
            if (asset == null) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && path.StartsWith(Root + "/");
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
}
