using System.IO;
using UnityEditor;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// パッケージ内ファイルの場所を返す。
    /// VPM で Packages/ に入った場合と、unitypackage で Assets/ に入った場合の両方に対応する。
    /// </summary>
    internal static class PackagePaths
    {
        public const string PackageName = "com.suzuki-y0.jeni.kaihen-navi";
        private const string AsmdefName = "Jeni.KaihenNavi.Editor";

        private static string _root;

        /// <summary>パッケージのルート（例: "Packages/com.suzuki-y0.jeni.kaihen-navi"）。</summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_root)) return _root;

                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePaths).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.assetPath))
                {
                    _root = info.assetPath;
                    return _root;
                }

                // unitypackage で Assets 配下に置かれた場合は asmdef の位置から逆算する
                foreach (var guid in AssetDatabase.FindAssets(AsmdefName + " t:AssemblyDefinitionAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) != AsmdefName) continue;
                    var editorDir = Path.GetDirectoryName(path);
                    var root = Path.GetDirectoryName(editorDir);
                    if (root == null) continue;
                    _root = root.Replace('\\', '/');
                    return _root;
                }

                _root = "Packages/" + PackageName;
                return _root;
            }
        }

        public static string Editor { get { return Root + "/Editor"; } }
        public static string StyleSheet { get { return Editor + "/UI/KaihenNavi.uss"; } }
        public static string Localization { get { return Editor + "/Localization"; } }
    }
}
