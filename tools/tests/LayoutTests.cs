// 作業フォルダの新構成（<アバター>_kaihen/Variant・KaihenNavi）と旧構成（Assets/KaihenNavi）のパスの判定、ルートの決め方、
// アバターの持ち物の判定、「Variant へ移す」の計画・パスの書き換え・移動と Ctrl+Z、移したアイテムの取り込み直し（PackageImportUndo.Begin）を、
// 一時フォルダの本物のファイルと動作する API スタブで確かめる。Unity の MoveAsset（GUID 維持）・Prefab・窓は Unity で確かめる。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    internal static class LayoutTests
    {
        private static int checks;
        private static readonly List<Object> instances = new List<Object>();
        // JsonUtility のスタブ：文字列の代わりにキーを書き、中身はここに複製して持つ
        private static readonly Dictionary<string, object> json = new Dictionary<string, object>();

        private static object Clone(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType) return value;
            var list = value as System.Collections.IList;
            var result = Activator.CreateInstance(value.GetType(), true);
            if (list != null) foreach (var item in list) ((System.Collections.IList)result).Add(Clone(item));
            else foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(result, Clone(field.GetValue(value)));
            return result;
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }

        private static void File(string path, string text = "x")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, text);
            System.IO.File.WriteAllText(path + ".meta", "guid: " + Guid.NewGuid().ToString("N"));
        }

        private static void Folder(string path)
        {
            Directory.CreateDirectory(path);
            if (!System.IO.File.Exists(path + ".meta")) System.IO.File.WriteAllText(path + ".meta", "guid: " + Guid.NewGuid().ToString("N"));
        }

        private static string Meta(string path) { return System.IO.File.ReadAllText(path + ".meta"); }

        public static void Main()
        {
            var original = Environment.CurrentDirectory;
            var temp = Path.Combine(Path.GetTempPath(), "kn-layout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                Environment.CurrentDirectory = temp;
                Hook();
                PathShapes();
                Roots();
                Names();
                Ownership();
                Plan();
                MigrateAndUndo();
            }
            finally
            {
                Environment.CurrentDirectory = original;
                try { Directory.Delete(temp, true); } catch (Exception) { }
            }
            Console.WriteLine("Layout checks passed (" + checks + "). MoveAsset GUIDs, prefabs and the window require Unity.");
        }

        /// <summary>AssetDatabase の移動・作成・削除をディスクの操作にする（.meta も一緒に動かす。ファイルとフォルダの両方）。</summary>
        private static void Hook()
        {
            AssetDatabase.FolderForTests = Directory.Exists;
            AssetDatabase.CreateFolderForTests = (parent, name) => Folder(parent + "/" + name);
            AssetDatabase.MoveResultForTests = (from, to) =>
            {
                if (!Directory.Exists(Path.GetDirectoryName(to))) return "Parent directory is not in asset database";
                if (Directory.Exists(from)) Directory.Move(from, to);
                else System.IO.File.Move(from, to);
                if (System.IO.File.Exists(from + ".meta")) System.IO.File.Move(from + ".meta", to + ".meta");
                return "";
            };
            AssetDatabase.DeleteForTests = path =>
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                if (System.IO.File.Exists(path + ".meta")) System.IO.File.Delete(path + ".meta");
            };
            ScriptableObject.CreateForTests = type =>
            {
                var created = (ScriptableObject)Activator.CreateInstance(type, true);
                instances.Add(created);
                return created;
            };
            Resources.FindForTests = type => instances.Where(type.IsInstanceOfType).ToArray();
            JsonUtility.SerializeForTests = value =>
            {
                var key = "json:" + json.Count;
                json[key] = Clone(value);
                return key;
            };
            JsonUtility.DeserializeForTests = (key, type) => json.ContainsKey(key) ? Clone(json[key]) : null;
        }

        private static void PathShapes()
        {
            const string alice = "Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab";
            Check(WorkFolders.KaihenRootOf(alice) == "Assets/C_kaihen" && WorkFolders.KaihenRootOf("Assets/C/x.prefab") == null &&
                  WorkFolders.KaihenRootOf("Assets/C_kaihen/../x") == null, "The root of a path is Assets/<avatar>_kaihen");
            Check(WorkFolders.VariantAvatarFolderOf(alice) == "Assets/C_kaihen/Variant/C_Alice" &&
                  WorkFolders.VariantAvatarFolderOf("Assets/C_kaihen/Variant/C_Variant_Base.prefab") == null &&
                  WorkFolders.IsVariantAvatarFolder("Assets/C_kaihen/Variant/Blue") && !WorkFolders.IsVariantAvatarFolder("Assets/C_kaihen/Variant"),
                "Edit folders are the folders directly under Variant");

            // 改変アバター：新構成（Exporter の形・名前が自由なフォルダも）と旧構成
            Check(MyAvatar.IsMinePath(alice) && MyAvatar.IsMinePath("Assets/C_kaihen/Variant/Blue/C_Blue.prefab") &&
                  MyAvatar.IsMinePath("Assets\\C_kaihen\\Variant\\Test\\Test.prefab"), "Prefabs directly in a Variant folder are edited avatars");
            Check(MyAvatar.IsMinePath("Assets/KaihenNavi/Avatars/A/A.prefab") && !MyAvatar.IsMinePath("Assets/KaihenNavi/Avatars/A/Backups/A (before base).prefab"),
                "The old layout still counts as edited avatars");
            Check(!MyAvatar.IsMinePath("Assets/C_kaihen/Variant/C_Variant_Base.prefab") && !MyAvatar.IsMinePath("Assets/C_kaihen/Variant/C_Alice/Outfit/Dress.prefab") &&
                  !MyAvatar.IsMinePath("Assets/C_kaihen/Cloth/Dress/Dress.prefab") && !MyAvatar.IsMinePath("Assets/C_kaihen/Variant/C_Alice/Materials/a.mat") &&
                  !MyAvatar.IsMinePath("Assets/C_kaihen/Variant/../Variant/A/A.prefab") && !MyAvatar.IsMinePath("Assets/Other/Variant/A/A.prefab") &&
                  !MyAvatar.IsMinePath("Assets/KaihenNavi/Bases/C/C Base.prefab") && !MyAvatar.IsMinePath(null),
                "Bases, nested prefabs, sorted items and non-root folders are not edited avatars");

            // ベース
            Check(MyAvatar.IsBasePath("Assets/C_kaihen/Variant/C_Variant_Base.prefab") && MyAvatar.IsBasePath("Assets/C_kaihen/Variant/C_Variant_Base 1.prefab") &&
                  MyAvatar.IsBasePath("Assets/KaihenNavi/Bases/C/C Base.prefab"), "Bases in both layouts");
            Check(!MyAvatar.IsBasePath("Assets/C_kaihen/Variant/C_Root_Menu.asset") && !MyAvatar.IsBasePath("Assets/C_kaihen/Variant/Other.prefab") &&
                  !MyAvatar.IsBasePath("Assets/C_kaihen/Variant/C_Alice/C_Variant_Base.prefab"), "Other files under Variant are not bases");
            Check(WorkFolders.AvatarNameOfBase("Assets/R_kaihen/Variant/Rurune_Variant_Base 1.prefab") == "Rurune", "The avatar name comes from the base name");

            // ツールが書き換えてよい場所
            Check(WorkFolders.IsToolPath("Assets/C_kaihen/Variant/C_Alice/Materials/Hair_Alice.mat") && WorkFolders.IsToolPath("Assets/C_kaihen/KaihenNavi/Backups/C_Alice/b.asset") &&
                  WorkFolders.IsToolPath("Assets/KaihenNavi/Avatars/A/Materials/x.mat") && WorkFolders.IsToolPath("Assets/C_kaihen/Variant/C_Variant_Base.prefab"),
                "Edit folders, data and bases are tool paths");
            Check(!WorkFolders.IsToolPath("Assets/C_kaihen/Variant/C_Root_Menu.asset") && !WorkFolders.IsToolPath("Assets/C_kaihen/C/Materials/Hair.mat") &&
                  !WorkFolders.IsToolPath("Assets/C_kaihen/Cloth/Dress/d.mat") && !WorkFolders.IsToolPath("Assets/C_kaihen/KaihenNavi") &&
                  !WorkFolders.IsToolPath("Assets/C_kaihen/Variant/C_Alice/../../C/Hair.mat") && !WorkFolders.IsToolPath("Assets/KaihenNaviOther/x.mat"),
                "Original assets, sorted items and traversal are not tool paths");
            Check(WorkFolders.IsToolMenuPath("Assets/C_kaihen/Variant/C_Alice/Menus/Root.asset") && !WorkFolders.IsToolMenuPath("Assets/C_kaihen/Variant/C_Alice/Menu.asset") &&
                  !WorkFolders.IsToolMenuPath("Assets/C_kaihen/Variant/C_Root_Menu.asset"), "Only menus the tool made count as tool menus");
            Check(WorkFolders.IsDataPath("Assets/C_kaihen/KaihenNavi/Ledger/items.json") && WorkFolders.IsDataPath("Assets/KaihenNavi/Ledger/items.json") &&
                  !WorkFolders.IsDataPath("Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab"), "Data paths");
            Check(WorkFolders.IsManagedDataFile("Assets/KaihenNavi/Ledger/items.json") && WorkFolders.IsManagedDataFile("Assets/C_kaihen/KaihenNavi/Ledger/items.json.meta") &&
                  WorkFolders.IsManagedDataFile("Assets/C_kaihen/KaihenNavi/README.md") && !WorkFolders.IsManagedDataFile("Assets/C_kaihen/README.md") &&
                  !WorkFolders.IsManagedDataFile("Assets/Shop/Ledger/items.json"), "The ledger and README are managed in both layouts");
            Check(ItemLedger.IsToolPrefab("Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab") && ItemLedger.IsToolPrefab("Assets/C_kaihen/KaihenNavi/Backups/A/x.prefab") &&
                  !ItemLedger.IsToolPrefab("Assets/C_kaihen/Variant/C_Alice/Outfit/Dress.prefab") && !ItemLedger.IsToolPrefab("Assets/C_kaihen/Cloth/Dress/d.prefab"),
                "Avatar saves do not refresh the item list; sorted items do");
        }

        private static void Roots()
        {
            var roots = new[] { "Assets/C_kaihen", "Assets/R_kaihen" };
            Check(ImportSorter.ChooseAvatarRoot("Assets/R_kaihen/R/R.prefab", "R", roots, "Assets/C_kaihen") == "Assets/R_kaihen",
                "The root that holds the original prefab wins");
            Check(ImportSorter.ChooseAvatarRoot("Assets/R/R.prefab", "R", roots, "Assets/C_kaihen") == "Assets/R_kaihen",
                "A root named after the avatar comes next");
            Check(ImportSorter.ChooseAvatarRoot("Assets/X/X.prefab", "X", roots, "Assets/C_kaihen") == "Assets/C_kaihen",
                "Then the sorter's remembered root");
            Check(ImportSorter.ChooseAvatarRoot("Assets/X/X.prefab", "X", new[] { "Assets/C_kaihen" }, "") == "Assets/C_kaihen",
                "A single root is used");
            Check(ImportSorter.ChooseAvatarRoot("Assets/X/X.prefab", "X", roots, "") == "Assets/X_kaihen" &&
                  ImportSorter.ChooseAvatarRoot(null, "X", new string[0], "") == "Assets/X_kaihen", "Otherwise <avatar>_kaihen is created");

            // 台帳：旧構成があればそれ、無ければ今のルート → ほかのルートにあるもの → 今のルートに作る
            var files = new HashSet<string>();
            Func<string, bool> exists = files.Contains;
            Check(WorkFolders.LedgerFileFor(roots, "Assets/R_kaihen", exists) == "Assets/R_kaihen/KaihenNavi/Ledger/items.json", "A new ledger goes to the current root");
            Check(WorkFolders.LedgerFileFor(new string[0], null, exists) == "Assets/KaihenNavi/Ledger/items.json", "Without a root the old place is used");
            files.Add("Assets/C_kaihen/KaihenNavi/Ledger/items.json");
            Check(WorkFolders.LedgerFileFor(roots, "Assets/R_kaihen", exists) == "Assets/C_kaihen/KaihenNavi/Ledger/items.json", "An existing ledger in another root is kept");
            files.Add("Assets/KaihenNavi/Ledger/items.json");
            Check(WorkFolders.LedgerFileFor(roots, "Assets/R_kaihen", exists) == "Assets/KaihenNavi/Ledger/items.json", "The old ledger is used until it is moved");
        }

        private static void Names()
        {
            Check(WorkFolders.FolderName("Cazalis", "Alice") == "Cazalis_Alice" && WorkFolders.FolderName("Cazalis", "Cazalis 改変") == "Cazalis_改変" &&
                  WorkFolders.FolderName("Cazalis", "Cazalis_Alice") == "Cazalis_Alice" && WorkFolders.FolderName("Cazalis", "cazalis") == "cazalis" &&
                  WorkFolders.FolderName("Cazalis", "a/b") == "Cazalis_a_b", "Folder names follow <avatar>_<name>");
            Check(WorkFolders.ModificationName("Cazalis_Alice", "Cazalis") == "Alice" && WorkFolders.ModificationName("Blue", "Cazalis") == "Blue" &&
                  WorkFolders.ModificationName("Cazalis_", "Cazalis") == "Cazalis_", "The modification name drops the avatar prefix");
            string display, file;
            var taken = new HashSet<string> { "Hair_Alice", "Hair_2_Alice" };
            WorkFolders.CopyMaterialName("Hair", "Alice", null, out display, out file);
            Check(display == "Hair_Alice" && file == "Hair_Alice", "Copies are named like AvatarPrefabMaterialExporter");
            WorkFolders.CopyMaterialName("Hair", "Alice", taken.Contains, out display, out file);
            Check(display == "Hair_3_Alice" && file == "Hair_3_Alice", "Collisions use <name>_<n>_<modification>: " + file);
            WorkFolders.CopyMaterialName("Body/1", "Alice", null, out display, out file);
            Check(display == "Body/1_Alice" && file == "Body_1_Alice", "File names are sanitized, material names are not");
            Check(WorkFolders.BackupsFolderFor("Assets/C_kaihen/Variant/C_Alice") == "Assets/C_kaihen/KaihenNavi/Backups/C_Alice" &&
                  WorkFolders.BackupsFolderFor("Assets/KaihenNavi/Avatars/A") == "Assets/KaihenNavi/Avatars/A/Backups", "Backups per layout");
        }

        private static void Ownership()
        {
            Directory.CreateDirectory("Assets/C_kaihen/C");
            var paths = new Dictionary<Object, string>();
            AssetDatabase.PathForTests = o => o != null && paths.ContainsKey(o) ? paths[o] : "";
            Func<string, Material> material = p =>
            {
                var m = new Material((Shader)null) { name = Path.GetFileNameWithoutExtension(p) };
                paths[m] = p;
                return m;
            };
            var avatar = new GameObject("C_Alice");
            PrefabUtility.RootForTests = avatar;
            PrefabUtility.PathForTests = "Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab";
            Check(WorkFolders.ForAvatar(avatar) == "Assets/C_kaihen/Variant/C_Alice" && WorkFolders.Backups(avatar) == "Assets/C_kaihen/KaihenNavi/Backups/C_Alice",
                "An edited avatar uses its own Variant folder");
            Check(WorkFolders.IsOwnedBy(material("Assets/C_kaihen/Variant/C_Alice/Materials/Hair_Alice.mat"), avatar) &&
                  WorkFolders.IsOwnedBy(material("Assets/C_kaihen/Variant/C_Alice/Materials/Sub/Body_Alice.mat"), avatar),
                "Materials in the avatar's own folder are owned (made by the Exporter or the tool)");
            Check(!WorkFolders.IsOwnedBy(material("Assets/C_kaihen/Variant/C_Alice2/Materials/Hair.mat"), avatar) &&
                  !WorkFolders.IsOwnedBy(material("Assets/C_kaihen/Variant/C_Blue/Materials/Hair_Blue.mat"), avatar) &&
                  !WorkFolders.IsOwnedBy(material("Assets/C_kaihen/Variant/Hair_Base.mat"), avatar) &&
                  !WorkFolders.IsOwnedBy(material("Assets/C_kaihen/C/Materials/Hair.mat"), avatar),
                "Other edit folders, the base level and the original are not owned");
            Check(WorkFolders.ModificationNameFor(avatar) == "Alice", "The modification name comes from the prefab");
            PrefabUtility.PathForTests = "Assets/C_kaihen/Variant/Blue/C_Blue.prefab";
            Check(WorkFolders.ModificationNameFor(avatar) == "Blue" && WorkFolders.ForAvatar(avatar) == "Assets/C_kaihen/Variant/Blue",
                "Exporter folders with a free name use <avatar>_<name>.prefab");
            PrefabUtility.PathForTests = "Assets/KaihenNavi/Avatars/A/A.prefab";
            Check(WorkFolders.ForAvatar(avatar) == "Assets/KaihenNavi/Avatars/A" && WorkFolders.ModificationNameFor(avatar) == null &&
                  WorkFolders.IsOwnedBy(material("Assets/KaihenNavi/Avatars/A/Materials/Hair.mat"), avatar),
                "Old-layout avatars keep their folder and the old copy names");

            // まだ改変アバターでないアバター：元の Prefab のルートの Variant/<アバター>_<名前>
            var original = new GameObject("C 改変");
            PrefabUtility.RootForTests = original;
            PrefabUtility.PathForTests = "Assets/C_kaihen/C/C.prefab";
            Check(WorkFolders.ForAvatar(original) == "Assets/C_kaihen/Variant/C_改変", "New folders go under Variant: " + WorkFolders.ForAvatar(original));
            Check(WorkFolders.ModificationNameFor(original) == "改変", "Copies before saving already carry the modification name");
            Directory.CreateDirectory("Assets/KaihenNavi/Avatars/C 改変/Materials");
            Check(WorkFolders.ForAvatar(original) == "Assets/KaihenNavi/Avatars/C 改変", "Copies made by an older version keep their folder");
            Directory.Delete("Assets/KaihenNavi", true);
            PrefabUtility.RootForTests = null;
            PrefabUtility.PathForTests = null;
            AssetDatabase.PathForTests = null;
            Directory.Delete("Assets", true);
        }

        private static void Plan()
        {
            var disk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Assets/KaihenNavi/Avatars/Alice/Backups", "Assets/KaihenNavi/Avatars/Alice/avatar.json", "Assets/KaihenNavi/Bases/C/avatar.json",
                "Assets/C_kaihen", "Assets/C_kaihen/Variant", "Assets/C_kaihen/Variant/C_Variant_Base.prefab",
            };
            var avatars = new List<LayoutMigration.LegacyAvatar>
            {
                new LayoutMigration.LegacyAvatar { folder = "Assets/KaihenNavi/Avatars/Alice", prefab = "Assets/KaihenNavi/Avatars/Alice/Alice.prefab",
                    originalPath = "Assets/C_kaihen/C/C.prefab", originalName = "C" },
                new LayoutMigration.LegacyAvatar { folder = "Assets/KaihenNavi/Avatars/R 改変", prefab = "Assets/KaihenNavi/Avatars/R 改変/R 改変.prefab",
                    originalPath = "Assets/R/R.prefab", originalName = "R" },
            };
            var bases = new List<LayoutMigration.LegacyBase>
            {
                new LayoutMigration.LegacyBase { prefab = "Assets/KaihenNavi/Bases/C/C Base.prefab", originalPath = "Assets/C_kaihen/C/C.prefab", originalName = "C" },
            };
            var plan = LayoutMigration.MakePlan(avatars, bases, true, true, new[] { "Assets/C_kaihen" }, "", disk.Contains);
            var moves = plan.moves.Select(m => m.from + " -> " + m.to).ToList();
            Check(moves.SequenceEqual(new[]
            {
                "Assets/KaihenNavi/Bases/C/C Base.prefab -> Assets/C_kaihen/Variant/C_Variant_Base 1.prefab",
                "Assets/KaihenNavi/Avatars/Alice/Backups -> Assets/C_kaihen/KaihenNavi/Backups/C_Alice",
                "Assets/KaihenNavi/Avatars/Alice -> Assets/C_kaihen/Variant/C_Alice",
                "Assets/C_kaihen/Variant/C_Alice/Alice.prefab -> Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab",
                // ルートが 1 つだけなので、R も振り分けのルートへ（ImportSorter.ChooseAvatarRoot と同じ）
                "Assets/KaihenNavi/Avatars/R 改変 -> Assets/C_kaihen/Variant/R_改変",
                "Assets/C_kaihen/Variant/R_改変/R 改変.prefab -> Assets/C_kaihen/Variant/R_改変/R_改変.prefab",
                "Assets/KaihenNavi/Ledger -> Assets/C_kaihen/KaihenNavi/Ledger",
                "Assets/KaihenNavi/README.md -> Assets/C_kaihen/KaihenNavi/README.md",
            }), "Migration moves:\n" + string.Join("\n", moves));
            Check(plan.createFolders.SequenceEqual(new[] { "Assets/C_kaihen/KaihenNavi", "Assets/C_kaihen/KaihenNavi/Backups" }),
                "Only missing folders are created, parents first: " + string.Join(",", plan.createFolders));
            Check(plan.deleteFiles.SequenceEqual(new[] { "Assets/KaihenNavi/Bases/C/avatar.json" }) &&
                  plan.deleteFolders.SequenceEqual(new[] { "Assets/KaihenNavi/Bases/C", WorkFolders.AvatarsRoot, WorkFolders.BasesRoot, WorkFolders.Root }),
                "The base's avatar.json and emptied old folders are cleaned up (children first)");
            Check(plan.manifests.Count == 1 && plan.manifests[0].from == "Assets/KaihenNavi/Avatars/Alice/Alice.prefab" &&
                  plan.manifests[0].to == "Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab", "avatar.json follows the renamed prefab");
            Check(plan.dataFolders.SequenceEqual(new[] { "Assets/C_kaihen/KaihenNavi" }) && plan.avatars == 2 && plan.bases == 1 && plan.leftovers.Count == 0,
                "One data folder; counts");

            // ルートが無いプロジェクト：アバターごとに <アバター>_kaihen を作る
            var fresh = LayoutMigration.MakePlan(avatars, new List<LayoutMigration.LegacyBase>(), false, false, new string[0], "", p => false);
            Check(fresh.createFolders.SequenceEqual(new[] { "Assets/C_kaihen", "Assets/C_kaihen/Variant", "Assets/R_kaihen", "Assets/R_kaihen/Variant" }) &&
                  fresh.moves[0].to == "Assets/C_kaihen/Variant/C_Alice" && fresh.moves[2].to == "Assets/R_kaihen/Variant/R_改変" &&
                  fresh.dataFolders.SequenceEqual(new[] { "Assets/C_kaihen/KaihenNavi", "Assets/R_kaihen/KaihenNavi" }),
                "Without roots each avatar gets its own root: " + string.Join(",", fresh.createFolders));
            var noRoot = LayoutMigration.MakePlan(new List<LayoutMigration.LegacyAvatar>(), new List<LayoutMigration.LegacyBase>(), true, true, new string[0], "", p => false);
            Check(noRoot.moves.Count == 0 && noRoot.leftovers.SequenceEqual(new[] { "Assets/KaihenNavi/Ledger" }), "A ledger alone needs a root to move to");

            // パスの書き換え：移動を順に重ね、戻すと元どおり
            var path = "Assets/KaihenNavi/Avatars/Alice/Backups/InnerColorState.asset";
            Check(LayoutMigration.MapPath(path, plan.moves, true) == "Assets/C_kaihen/KaihenNavi/Backups/C_Alice/InnerColorState.asset" &&
                  LayoutMigration.MapPath("Assets/KaihenNavi/Avatars/Alice/Alice.prefab", plan.moves, true) == "Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab" &&
                  LayoutMigration.MapPath("Assets/KaihenNavi/Avatars/Alice/Materials/Hair.mat.meta", plan.moves, true) == "Assets/C_kaihen/Variant/C_Alice/Materials/Hair.mat.meta" &&
                  LayoutMigration.MapPath("Assets/KaihenNavi/Avatars/Alice2/a.mat", plan.moves, true) == "Assets/KaihenNavi/Avatars/Alice2/a.mat",
                "Paths follow chained moves; similar prefixes stay");
            Check(LayoutMigration.MapPath(LayoutMigration.MapPath(path, plan.moves, true), plan.moves, false) == path &&
                  LayoutMigration.MapPath("Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab", plan.moves, false) == "Assets/KaihenNavi/Avatars/Alice/Alice.prefab",
                "Mapping back restores the old paths");
            var state = "{\"meshes\":[{\"meshPath\":\"Assets/KaihenNavi/Avatars/Alice/Meshes/Hair.asset\",\"materialPath\":\"Assets/KaihenNavi/Avatars/Alice\"}]," +
                        "\"other\":\"Assets/KaihenNavi/Avatars/Alice2/x.mat\",\"source\":{\"path\":\"Assets/C_kaihen/C/Hair.mat\"}}";
            var mapped = LayoutMigration.MapJson(state, plan.moves, true);
            Check(mapped.Contains("\"Assets/C_kaihen/Variant/C_Alice/Meshes/Hair.asset\"") && mapped.Contains("\"materialPath\":\"Assets/C_kaihen/Variant/C_Alice\"") &&
                  mapped.Contains("\"Assets/KaihenNavi/Avatars/Alice2/x.mat\"") && mapped.Contains("\"Assets/C_kaihen/C/Hair.mat\""),
                "JSON paths (inner color state, history extras) follow the move: " + mapped);
            Check(LayoutMigration.MapJson(mapped, plan.moves, false) == state, "JSON maps back exactly");
        }

        private static void MigrateAndUndo()
        {
            // 旧構成のプロジェクト
            File("Assets/C_kaihen/C/C.prefab");
            File("Assets/KaihenNavi/README.md", "old readme");
            Folder("Assets/KaihenNavi");
            Folder("Assets/KaihenNavi/Avatars");
            Folder("Assets/KaihenNavi/Bases");
            File("Assets/KaihenNavi/Bases/C/C Base.prefab");
            Folder("Assets/KaihenNavi/Bases/C");
            File("Assets/KaihenNavi/Bases/C/avatar.json", "base manifest");
            File("Assets/KaihenNavi/Avatars/Alice/Alice.prefab");
            Folder("Assets/KaihenNavi/Avatars/Alice");
            File("Assets/KaihenNavi/Avatars/Alice/Materials/Hair.mat");
            File("Assets/KaihenNavi/Avatars/Alice/Backups/Backup_1.asset");
            File("Assets/KaihenNavi/Avatars/Orphan/Materials/x.mat");
            var manifest = new AvatarManifest
            {
                format = 1, kind = "avatar", name = "Alice", prefab = "Alice.prefab",
                basePrefabPath = "Assets/C_kaihen/C/C.prefab", parentPrefabPath = "Assets/KaihenNavi/Bases/C/C Base.prefab",
            };
            System.IO.File.WriteAllText("Assets/KaihenNavi/Avatars/Alice/avatar.json", JsonUtility.ToJson(manifest));
            var dataType = typeof(ItemLedger).GetNestedType("LedgerData", BindingFlags.NonPublic);
            var data = Activator.CreateInstance(dataType, true);
            ((List<LedgerPackage>)dataType.GetField("packages").GetValue(data)).Add(new LedgerPackage
                { packageName = "Dress", commonFolder = "Assets/Booth", assetPaths = new List<string> { "Assets/Booth/Dress/d.prefab" } });
            File("Assets/KaihenNavi/Ledger/items.json", JsonUtility.ToJson(data));
            Folder("Assets/KaihenNavi/Ledger");
            Directory.CreateDirectory("Library/KaihenNavi/Imports/x");
            var import = new PackageImportUndo.Snapshot();
            import.files.Add(new PackageImportUndo.FileState { path = "Assets/KaihenNavi/Ledger/items.json", before = true, after = true });
            import.files.Add(new PackageImportUndo.FileState { path = "Assets/Booth/Dress/d.prefab", after = true });
            System.IO.File.WriteAllText("Library/KaihenNavi/Imports/x/state.json", JsonUtility.ToJson(import));
            ItemLedger.Invalidate();
            var metas = new[] { "Assets/KaihenNavi/Avatars/Alice/Alice.prefab", "Assets/KaihenNavi/Avatars/Alice/Materials/Hair.mat", "Assets/KaihenNavi/Bases/C/C Base.prefab",
                "Assets/KaihenNavi/Bases/C/avatar.json", "Assets/KaihenNavi/Bases/C", "Assets/KaihenNavi", "Assets/KaihenNavi/Avatars/Alice/Backups/Backup_1.asset" }
                .ToDictionary(p => p, Meta);

            var avatars = new List<LayoutMigration.LegacyAvatar> { new LayoutMigration.LegacyAvatar { folder = "Assets/KaihenNavi/Avatars/Alice",
                prefab = "Assets/KaihenNavi/Avatars/Alice/Alice.prefab", originalPath = "Assets/C_kaihen/C/C.prefab", originalName = "C" } };
            var bases = new List<LayoutMigration.LegacyBase> { new LayoutMigration.LegacyBase { prefab = "Assets/KaihenNavi/Bases/C/C Base.prefab",
                originalPath = "Assets/C_kaihen/C/C.prefab", originalName = "C" } };
            var plan = LayoutMigration.MakePlan(avatars, bases, true, true, ImportSorter.FindRoots(), "", p => System.IO.File.Exists(p) || Directory.Exists(p));
            plan.leftovers.Add("Assets/KaihenNavi/Avatars/Orphan");
            Check(LayoutMigration.Run(plan) == null, "Migration succeeds");

            Check(System.IO.File.Exists("Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab") && Meta("Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab") == metas["Assets/KaihenNavi/Avatars/Alice/Alice.prefab"] &&
                  Meta("Assets/C_kaihen/Variant/C_Alice/Materials/Hair.mat") == metas["Assets/KaihenNavi/Avatars/Alice/Materials/Hair.mat"],
                "The avatar moves to Variant/<avatar>_<name>/<avatar>_<name>.prefab with its GUIDs");
            Check(Meta("Assets/C_kaihen/Variant/C_Variant_Base.prefab") == metas["Assets/KaihenNavi/Bases/C/C Base.prefab"], "The base moves to Variant/<avatar>_Variant_Base");
            Check(Meta("Assets/C_kaihen/KaihenNavi/Backups/C_Alice/Backup_1.asset") == metas["Assets/KaihenNavi/Avatars/Alice/Backups/Backup_1.asset"] &&
                  !Directory.Exists("Assets/C_kaihen/Variant/C_Alice/Backups"), "Backups move to the data folder");
            Check(System.IO.File.Exists("Assets/C_kaihen/KaihenNavi/Ledger/items.json") && ItemLedger.LedgerFile == "Assets/C_kaihen/KaihenNavi/Ledger/items.json" &&
                  ItemLedger.All().Count == 1, "The ledger moves and is found at its new place");
            Check(System.IO.File.ReadAllText("Assets/C_kaihen/KaihenNavi/README.md").Contains("Variant/"), "The README is moved and rewritten for the new layout");
            var moved = WorkFolders.ReadManifest("Assets/C_kaihen/Variant/C_Alice");
            Check(moved.parentPrefabPath == "Assets/C_kaihen/Variant/C_Variant_Base.prefab" && moved.basePrefabPath == "Assets/C_kaihen/C/C.prefab" &&
                  moved.name == "C_Alice" && moved.prefab == "C_Alice.prefab", "avatar.json follows the move");
            var rewritten = JsonUtility.FromJson<PackageImportUndo.Snapshot>(System.IO.File.ReadAllText("Library/KaihenNavi/Imports/x/state.json"));
            Check(rewritten.files[0].path == "Assets/C_kaihen/KaihenNavi/Ledger/items.json" && rewritten.files[1].path == "Assets/Booth/Dress/d.prefab",
                "The import Undo snapshot follows the moved ledger");
            Check(!Directory.Exists("Assets/KaihenNavi/Bases") && Directory.Exists("Assets/KaihenNavi/Avatars/Orphan") && !System.IO.File.Exists("Assets/KaihenNavi/README.md"),
                "Emptied old folders are removed; folders without an edited avatar stay");

            // Ctrl+Z：Unity が目印を戻したのと同じ状態にして、undoRedoPerformed を流す
            var journal = instances.OfType<LayoutMigrationUndo>().Single();
            var applied = typeof(LayoutMigrationUndo).GetField("applied", BindingFlags.NonPublic | BindingFlags.Instance);
            applied.SetValue(journal, false);
            Undo.ReplayForTests();
            Check(metas.All(p => (System.IO.File.Exists(p.Key) || Directory.Exists(p.Key)) && Meta(p.Key) == p.Value), "Undo restores every file and folder with its GUID");
            Check(System.IO.File.ReadAllText("Assets/KaihenNavi/Bases/C/avatar.json") == "base manifest" && System.IO.File.ReadAllText("Assets/KaihenNavi/README.md") != "",
                "Removed files come back");
            Check(!Directory.Exists("Assets/C_kaihen/Variant") && !Directory.Exists("Assets/C_kaihen/KaihenNavi") && Directory.Exists("Assets/C_kaihen/C"),
                "Folders created for the move are removed; the root that existed stays");
            Check(WorkFolders.ReadManifest("Assets/KaihenNavi/Avatars/Alice").parentPrefabPath == "Assets/KaihenNavi/Bases/C/C Base.prefab" &&
                  WorkFolders.ReadManifest("Assets/KaihenNavi/Avatars/Alice").name == "Alice", "avatar.json goes back");
            rewritten = JsonUtility.FromJson<PackageImportUndo.Snapshot>(System.IO.File.ReadAllText("Library/KaihenNavi/Imports/x/state.json"));
            Check(rewritten.files[0].path == "Assets/KaihenNavi/Ledger/items.json" && ItemLedger.LedgerFile == "Assets/KaihenNavi/Ledger/items.json",
                "The ledger and the import snapshot go back");

            // やり直し
            applied.SetValue(journal, true);
            Undo.ReplayForTests();
            Check(System.IO.File.Exists("Assets/C_kaihen/Variant/C_Alice/C_Alice.prefab") && System.IO.File.Exists("Assets/C_kaihen/Variant/C_Variant_Base.prefab") &&
                  !Directory.Exists("Assets/KaihenNavi/Bases") && ItemLedger.LedgerFile == "Assets/C_kaihen/KaihenNavi/Ledger/items.json", "Redo moves again");

            // 失敗したら途中までを戻す（行き先に同じ名前ができていた）
            File("Assets/KaihenNavi/Avatars/Bob/Bob.prefab");
            var failing = LayoutMigration.MakePlan(new List<LayoutMigration.LegacyAvatar> { new LayoutMigration.LegacyAvatar { folder = "Assets/KaihenNavi/Avatars/Bob",
                prefab = "Assets/KaihenNavi/Avatars/Bob/Bob.prefab", originalPath = "Assets/C_kaihen/C/C.prefab", originalName = "C" } },
                new List<LayoutMigration.LegacyBase>(), false, false, ImportSorter.FindRoots(), "", p => System.IO.File.Exists(p) || Directory.Exists(p));
            failing.moves.Add(new SortMove { from = "Assets/Missing", to = "Assets/C_kaihen/Variant/Missing" });
            var count = instances.OfType<LayoutMigrationUndo>().Count();
            var error = LayoutMigration.Run(failing);
            Check(error != null && System.IO.File.Exists("Assets/KaihenNavi/Avatars/Bob/Bob.prefab") && !Directory.Exists("Assets/C_kaihen/Variant/C_Bob"),
                "A failed migration rolls back: " + error);
            Check(instances.OfType<LayoutMigrationUndo>().Count() == count + 1, "Failed migrations create a journal that is discarded");
            Directory.Delete("Assets", true);
            Directory.Delete("Library", true);
        }
    }
}
