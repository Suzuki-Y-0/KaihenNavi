// インポート振り分けの判定（ショップ階層・用途フォルダ・除外・注意）と、移動・Ctrl+Z・台帳と取り込みの控えの書き換えを、
// 一時フォルダの本物のファイルと動作する API スタブで確かめる。Unity の MoveAsset（GUID 維持）と窓の見た目は Unity で確かめる。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    internal static class ImportSortTests
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

        private static string Paths(LedgerPackage p)
        {
            return p.commonFolder + "|" + string.Join(",", p.assetPaths) + "|" + string.Join(",", p.prefabs.Select(f => f.path));
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

        public static void Main()
        {
            var original = Environment.CurrentDirectory;
            var temp = Path.Combine(Path.GetTempPath(), "kn-sort-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                Environment.CurrentDirectory = temp;
                Hook();
                Pure();
                Targets();
                Paths();
                MoveAndUndo();
            }
            finally
            {
                Environment.CurrentDirectory = original;
                try { Directory.Delete(temp, true); } catch (Exception) { }
            }
            Console.WriteLine("Import sort checks passed (" + checks + "). MoveAsset GUIDs, the window and Unity's Undo require Unity.");
        }

        /// <summary>AssetDatabase の移動・作成・削除をディスクの操作にする（.meta も一緒に動かす）。</summary>
        private static void Hook()
        {
            AssetDatabase.FolderForTests = Directory.Exists;
            AssetDatabase.CreateFolderForTests = (parent, name) => Folder(parent + "/" + name);
            AssetDatabase.MoveResultForTests = (from, to) =>
            {
                if (!Directory.Exists(Path.GetDirectoryName(to))) return "Parent directory is not in asset database";
                Directory.Move(from, to);
                if (System.IO.File.Exists(from + ".meta")) System.IO.File.Move(from + ".meta", to + ".meta");
                return "";
            };
            AssetDatabase.DeleteForTests = path =>
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
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

        private static void Pure()
        {
            Check(ImportSorter.ParseCategories("").SequenceEqual(ImportSorter.DefaultCategories), "An empty setting uses the default purposes");
            Check(ImportSorter.ParseCategories("Cloth\n hair \ncloth\n\nWeapon").SequenceEqual(new[] { "Cloth", "hair", "Weapon" }),
                "Saved purposes are trimmed and de-duplicated case-insensitively");
            Check(ImportSorter.ParseCategories(ImportSorter.FormatCategories(new string[0])).Count == 0, "An emptied list stays empty");
            Check(ImportSorter.IsValidCategoryName("Shoes", new[] { "Cloth" }) && !ImportSorter.IsValidCategoryName("cloth", new[] { "Cloth" })
                && !ImportSorter.IsValidCategoryName("Editor", new string[0]) && !ImportSorter.IsValidCategoryName("a/b", new string[0])
                && !ImportSorter.IsValidCategoryName(" x", new string[0]) && !ImportSorter.IsValidCategoryName("Resources", new string[0]),
                "Purpose names must be unique folder names and not special Unity folders");

            Check(ImportSorter.ChooseRoot(new string[0], "") == null, "No root → ask to create");
            Check(ImportSorter.ChooseRoot(new[] { "Assets/A_kaihen" }, "Assets/B_kaihen") == "Assets/A_kaihen", "One root is used as is");
            Check(ImportSorter.ChooseRoot(new[] { "Assets/A_kaihen", "Assets/B_kaihen" }, "") == null, "Several roots without a saved choice → ask");
            Check(ImportSorter.ChooseRoot(new[] { "Assets/A_kaihen", "Assets/B_kaihen" }, "Assets/B_kaihen") == "Assets/B_kaihen", "The saved root is used");
            Check(ImportSorter.NewRootPath("Rurune") == "Assets/Rurune_kaihen" && ImportSorter.NewRootPath("Rurune_kaihen") == "Assets/Rurune_kaihen",
                "The new root is <avatar>_kaihen");
            Check(ImportSorter.AvatarNameFromPath("Assets/Cazalis/Prefab/Cazalis.prefab", "x") == "Cazalis"
                && ImportSorter.AvatarNameFromPath("Assets/Cazalis_kaihen/Cazalis/Cazalis.prefab", "x") == "Cazalis"
                && ImportSorter.AvatarNameFromPath("Assets/KaihenNavi/Avatars/A/A.prefab", "fallback") == "fallback"
                && ImportSorter.AvatarNameFromPath(null, "fallback") == "fallback", "The avatar name comes from the original prefab folder");
            Check(ImportSorter.IsRootName("Cazalis_kaihen") && !ImportSorter.IsRootName("_kaihen") && !ImportSorter.IsRootName("Cazalis_Kaihen"),
                "Roots end with _kaihen");

            var tops = ImportSorter.TopFoldersOf(new[]
            {
                "Assets/foo.prefab", "Assets/Shop", "Assets/Shop/Item/a.prefab", "Assets/Shop/Item/b.mat", "Assets\\Other\\x.png",
                "Assets/Cazalis_kaihen/Cloth/x.prefab", "Assets/KaihenNavi/Ledger/items.json", "Assets/Plugins/x.dll", "Assets/VRCSDK/a.cs",
                "Assets/TextMesh Pro/a.asset", "Assets/Editor Default Resources/a.png", "Assets/Gizmos/a.png", "Assets/Standard Assets/a.cs",
                "Assets/EditorEye.app/a", "Assets/settings/a.asset", "Packages/com.x/a.cs",
            });
            Check(tops.SequenceEqual(new[] { "Shop", "Other" }), "Only item folders under Assets are targets: " + string.Join(",", tops));
        }

        private static void Targets()
        {
            // ショップ階層：以前からある OldItem と、今回取り込んだ NewItem
            File("Assets/Shop/OldItem/old.prefab");
            File("Assets/Shop/NewItem/Prefab/new.prefab");
            File("Assets/Shop/NewItem/Texture/new.png");
            Folder("Assets/Shop");
            // アイテムの中身らしいフォルダだけのアイテム（ショップ階層ではない）
            File("Assets/Plain/Prefabs/p.prefab");
            File("Assets/Plain/Materials/p.mat");
            // 直下にファイルがあるアイテム
            File("Assets/Loose/readme.txt");
            File("Assets/Loose/Sub/l.prefab");
            Check(ImportSorter.IsShopLayer("Assets/Shop"), "A folder that only holds item folders is a shop layer");
            Check(!ImportSorter.IsShopLayer("Assets/Plain"), "Prefabs/Materials folders mean the folder is the item itself");
            Check(!ImportSorter.IsShopLayer("Assets/Loose"), "A folder with files is the item itself");

            var targets = ImportSorter.BuildTargets(new[] { "Assets/Shop", "Assets/Shop/NewItem", "Assets/Shop/NewItem/Prefab/new.prefab",
                "Assets/Shop/NewItem/Texture/new.png", "Assets/Plain/Prefabs/p.prefab", "Assets/top.prefab" });
            Check(targets.Count == 2, "Imported targets: " + string.Join(",", targets.Select(t => t.source)));
            Check(targets[0].source == "Assets/Shop/NewItem" && targets[0].shop == "Assets/Shop" && targets[0].ShopName == "Shop"
                && targets[0].ItemName == "NewItem" && targets[0].fileCount == 2 && targets[0].risks.Count == 0 && targets[0].choice == "",
                "Only the item imported this time is taken out of the shop");
            Check(targets[1].source == "Assets/Plain" && string.IsNullOrEmpty(targets[1].shop) && targets[1].fileCount == 2, "A plain item is moved as a whole");
            var manual = ImportSorter.BuildTargets(null).Select(t => t.source).ToList();
            Check(manual.Contains("Assets/Shop/OldItem") && manual.Contains("Assets/Shop/NewItem") && manual.Contains("Assets/Loose"),
                "The manual list shows every item folder under Assets");
            Check(ImportSorter.TargetsForFolder("Assets/Shop/OldItem", null).Single().shop == "Assets/Shop", "Selecting an item inside a shop works");
            Check(ImportSorter.TargetsForFolder("Assets/Plugins", null).Count == 0 && ImportSorter.TargetsForFolder("Assets/Shop/OldItem/x", null).Count == 0,
                "Excluded and deep folders are not targets");
            Check(ImportSorter.CanSortFolder("Assets/Shop") && ImportSorter.CanSortFolder("Assets/Shop/OldItem") && !ImportSorter.CanSortFolder("Assets/Plugins")
                && !ImportSorter.CanSortFolder("Assets/Shop/OldItem/x") && !ImportSorter.CanSortFolder("Assets") && !ImportSorter.CanSortFolder("Assets/Missing"),
                "The context menu is offered only for item and shop folders");

            // パスに依存しうるもの
            File("Assets/Risky/Script/Setup.cs");
            File("Assets/Shader1/s.shader", "Shader \"x\" { CGINCLUDE\n#include \"Assets/Shader1/common.cginc\"\nENDCG }");
            File("Assets/Shader2/s.shader", "#include \"common.cginc\"");
            File("Assets/Special/Resources/r.png");
            File("Assets/Dll/lib.DLL");
            Check(ImportSorter.DetectRisks("Assets/Risky").SequenceEqual(new[] { SortRisk.Script }), "Scripts are flagged");
            Check(ImportSorter.DetectRisks("Assets/Dll").SequenceEqual(new[] { SortRisk.Script }), "DLLs are flagged (case-insensitive)");
            Check(ImportSorter.DetectRisks("Assets/Shader1").SequenceEqual(new[] { SortRisk.ShaderInclude }), "Absolute shader includes are flagged");
            Check(ImportSorter.DetectRisks("Assets/Shader2").Count == 0, "Relative shader includes are fine");
            Check(ImportSorter.DetectRisks("Assets/Special").SequenceEqual(new[] { SortRisk.SpecialFolder }), "Special folders are flagged");
            Check(ImportSorter.TargetsForFolder("Assets/Risky", null).Single().choice == SortTarget.NotMove, "Risky items default to \"don't move\"");

            // 用途フォルダの解決（同じ名前だけ。大文字小文字は無視。昔のつづりの別名は扱わない）
            Folder("Assets/Cazalis_kaihen");
            Folder("Assets/Cazalis_kaihen/cloth");
            Folder("Assets/Cazalis_kaihen/Accesary");
            Folder("Assets/Cazalis_kaihen/Variant");
            Folder("Assets/Cazalis_kaihen/Editor");
            File("Assets/Cazalis_kaihen/Cazalis/Cazalis.prefab");
            Folder("Assets/Cazalis_kaihen/Shoes/Boots");
            Check(ImportSorter.ResolveCategory("Assets/Cazalis_kaihen", "Cloth") == "Assets/Cazalis_kaihen/cloth", "Existing folders match case-insensitively");
            Check(ImportSorter.ResolveCategory("Assets/Cazalis_kaihen", "Accessory") == "Assets/Cazalis_kaihen/Accessory", "No aliases: a missing folder is created");
            Check(ImportSorter.FindRoots().SequenceEqual(new[] { "Assets/Cazalis_kaihen" }), "Roots are found under Assets");
            var unregistered = ImportSorter.UnregisteredFolders("Assets/Cazalis_kaihen", ImportSorter.DefaultCategories);
            Check(unregistered.SequenceEqual(new[] { "Accesary", "Shoes" }), "Unregistered folders skip Variant, Editor and the avatar folder: " + string.Join(",", unregistered));

            // 計画：同じ名前があれば「名前 1」、同じ回の中の重複も避ける
            Folder("Assets/Cazalis_kaihen/cloth/NewItem");
            var a = new SortTarget { source = "Assets/Shop/NewItem", shop = "Assets/Shop" };
            var b = new SortTarget { source = "Assets/Plain", shop = "" };
            var c = new SortTarget { source = "Assets/Other/Plain", shop = "" };
            List<string> create; List<SortMove> moves; List<string> shops;
            ImportSorter.Plan("Assets/Cazalis_kaihen", new[]
            {
                new KeyValuePair<SortTarget, string>(a, "Cloth"), new KeyValuePair<SortTarget, string>(b, "Hair"),
                new KeyValuePair<SortTarget, string>(c, "Hair"), new KeyValuePair<SortTarget, string>(new SortTarget { source = "Assets/X" }, SortTarget.NotMove),
            }, out create, out moves, out shops);
            Check(create.SequenceEqual(new[] { "Assets/Cazalis_kaihen/Hair" }), "Only missing purpose folders are created");
            Check(moves.Count == 3 && moves[0].to == "Assets/Cazalis_kaihen/cloth/NewItem 1" && moves[1].to == "Assets/Cazalis_kaihen/Hair/Plain"
                && moves[2].to == "Assets/Cazalis_kaihen/Hair/Plain 1", "Destinations are unique: " + string.Join(",", moves.Select(m => m.to)));
            Check(shops.SequenceEqual(new[] { "Assets/Shop" }), "Shops are remembered for clean-up");
            Directory.Delete("Assets/Cazalis_kaihen/cloth/NewItem");
            System.IO.File.Delete("Assets/Cazalis_kaihen/cloth/NewItem.meta");
        }

        private static void Paths()
        {
            var moves = new List<SortMove> { new SortMove { from = "Assets/Shop/Item", to = "Assets/A_kaihen/Cloth/Item" } };
            Check(ImportSorter.RewritePath("Assets/Shop/Item", moves, true) == "Assets/A_kaihen/Cloth/Item", "The folder itself moves");
            Check(ImportSorter.RewritePath("Assets/Shop/Item.meta", moves, true) == "Assets/A_kaihen/Cloth/Item.meta", "The folder .meta moves");
            Check(ImportSorter.RewritePath("Assets/Shop/Item/a/b.prefab", moves, true) == "Assets/A_kaihen/Cloth/Item/a/b.prefab", "Contents move");
            Check(ImportSorter.RewritePath("Assets/Shop/Item2/b.prefab", moves, true) == "Assets/Shop/Item2/b.prefab", "Similar prefixes stay");
            Check(ImportSorter.RewritePath("Assets/Shop", moves, true) == "Assets/Shop", "The shop folder entry stays");
            Check(ImportSorter.RewritePath("Assets/A_kaihen/Cloth/Item/b.prefab", moves, false) == "Assets/Shop/Item/b.prefab", "Undo maps back");

            var packages = new List<LedgerPackage>
            {
                new LedgerPackage { packageName = "Item", commonFolder = "Assets/Shop",
                    assetPaths = new List<string> { "Assets/Shop", "Assets/Shop/Item", "Assets/Shop/Item/b.prefab" },
                    prefabs = new List<LedgerPrefab> { new LedgerPrefab { path = "Assets/Shop/Item/b.prefab" } } },
                new LedgerPackage { packageName = "Scan", scanned = true, commonFolder = "Assets/Shop/Item",
                    assetPaths = new List<string> { "Assets/Shop/Item/b.prefab" }, prefabs = new List<LedgerPrefab>() },
                new LedgerPackage { packageName = "Other", commonFolder = "Assets/Other", assetPaths = new List<string> { "Assets/Other/o.prefab" } },
            };
            var before = string.Join("\n", packages.Select(Paths));
            Check(ItemLedger.RewritePackages(packages, p => ImportSorter.RewritePath(p, moves, true)), "The ledger changes");
            Check(packages[0].assetPaths.SequenceEqual(new[] { "Assets/A_kaihen/Cloth/Item", "Assets/A_kaihen/Cloth/Item/b.prefab", "Assets/Shop" })
                && packages[0].prefabs[0].path == "Assets/A_kaihen/Cloth/Item/b.prefab" && packages[0].commonFolder == "Assets/Shop",
                "Imported packages follow the move (the shop entry and package name stay for KonoAsset)");
            Check(packages[1].commonFolder == "Assets/A_kaihen/Cloth/Item", "Scanned packages keep the item folder name (KonoAsset folder match)");
            Check(packages[2].assetPaths[0] == "Assets/Other/o.prefab", "Unrelated packages are untouched");
            ItemLedger.RewritePackages(packages, p => ImportSorter.RewritePath(p, moves, false));
            Check(string.Join("\n", packages.Select(Paths)) == before,
                "Rewriting back restores the ledger exactly");

            var snapshot = new PackageImportUndo.Snapshot();
            snapshot.files.Add(new PackageImportUndo.FileState { path = "Assets/Shop/Item/b.prefab" });
            snapshot.files.Add(new PackageImportUndo.FileState { path = "Assets/Shop/Item.meta" });
            snapshot.files.Add(new PackageImportUndo.FileState { path = "Assets/Shop.meta" });
            snapshot.files.Add(new PackageImportUndo.FileState { path = ItemLedger.LedgerFile });
            snapshot.directories.Add("Assets/Shop");
            snapshot.directories.Add("Assets/Shop/Item");
            Check(PackageImportUndo.RewriteSnapshot(snapshot, p => ImportSorter.RewritePath(p, moves, true)), "The import snapshot changes");
            Check(snapshot.files.Select(f => f.path).SequenceEqual(new[] { "Assets/A_kaihen/Cloth/Item/b.prefab", "Assets/A_kaihen/Cloth/Item.meta",
                "Assets/Shop.meta", ItemLedger.LedgerFile }) && snapshot.directories.SequenceEqual(new[] { "Assets/Shop", "Assets/A_kaihen/Cloth/Item" }),
                "Import Undo follows moved files and keeps tool files");
        }

        private static void MoveAndUndo()
        {
            // ルートの無いプロジェクト：Assets/Rurune_kaihen を作って移す
            Directory.Delete("Assets", true);
            File("Assets/Booth/Dress/Dress.prefab");
            File("Assets/Booth/Dress/Tex/d.png");
            Folder("Assets/Booth");
            File("Assets/Hat/hat.prefab");
            var shopMeta = System.IO.File.ReadAllText("Assets/Booth.meta");
            var dressMeta = System.IO.File.ReadAllText("Assets/Booth/Dress/Dress.prefab.meta");
            // 台帳と取り込みの控え
            var dataType = typeof(ItemLedger).GetNestedType("LedgerData", BindingFlags.NonPublic);
            var data = Activator.CreateInstance(dataType, true);
            ((List<LedgerPackage>)dataType.GetField("packages").GetValue(data)).Add(new LedgerPackage
            {
                packageName = "Dress", commonFolder = "Assets/Booth",
                assetPaths = new List<string> { "Assets/Booth", "Assets/Booth/Dress", "Assets/Booth/Dress/Dress.prefab" },
                prefabs = new List<LedgerPrefab> { new LedgerPrefab { path = "Assets/Booth/Dress/Dress.prefab" } },
            });
            var ledger = JsonUtility.ToJson(data);
            Directory.CreateDirectory("Assets/KaihenNavi/Ledger");
            System.IO.File.WriteAllText(ItemLedger.LedgerFile, ledger);
            Directory.CreateDirectory("Library/KaihenNavi/Imports/x");
            var state = new PackageImportUndo.Snapshot();
            state.files.Add(new PackageImportUndo.FileState { path = "Assets/Booth/Dress/Dress.prefab", after = true });
            state.directories.Add("Assets/Booth/Dress");
            System.IO.File.WriteAllText("Library/KaihenNavi/Imports/x/state.json", JsonUtility.ToJson(state));
            ItemLedger.Invalidate();

            var targets = ImportSorter.BuildTargets(new[] { "Assets/Booth/Dress/Dress.prefab", "Assets/Booth/Dress/Tex/d.png", "Assets/Hat/hat.prefab" });
            Check(targets.Count == 2, "Two imported items");
            int moved;
            var error = ImportSorter.Move("Assets/Rurune_kaihen", new[]
            {
                new KeyValuePair<SortTarget, string>(targets[0], "Cloth"), new KeyValuePair<SortTarget, string>(targets[1], SortTarget.NotMove),
            }, out moved);
            Check(error == null && moved == 1, "Move succeeds: " + error);
            Check(System.IO.File.Exists("Assets/Rurune_kaihen/Cloth/Dress/Dress.prefab") && System.IO.File.Exists("Assets/Rurune_kaihen/Cloth/Dress/Tex/d.png"),
                "The item is moved to <root>/<purpose>/<item>");
            Check(System.IO.File.ReadAllText("Assets/Rurune_kaihen/Cloth/Dress/Dress.prefab.meta") == dressMeta, "The .meta (GUID) moves with the file");
            Check(!Directory.Exists("Assets/Booth") && !System.IO.File.Exists("Assets/Booth.meta"), "The emptied shop folder is removed");
            Check(Directory.Exists("Assets/Hat"), "\"Don't move\" items stay");
            Check(ItemLedger.All()[0].prefabs[0].path == "Assets/Rurune_kaihen/Cloth/Dress/Dress.prefab", "The ledger follows the move");
            var rewritten = JsonUtility.FromJson<PackageImportUndo.Snapshot>(System.IO.File.ReadAllText("Library/KaihenNavi/Imports/x/state.json"));
            Check(rewritten.files[0].path == "Assets/Rurune_kaihen/Cloth/Dress/Dress.prefab" && rewritten.directories[0] == "Assets/Rurune_kaihen/Cloth/Dress",
                "The import Undo snapshot follows the move");

            // Ctrl+Z：Unity が目印を戻したのと同じ状態にして、undoRedoPerformed を流す
            var journal = instances.OfType<ImportSortUndo>().Single();
            var applied = typeof(ImportSortUndo).GetField("applied", BindingFlags.NonPublic | BindingFlags.Instance);
            applied.SetValue(journal, false);
            Undo.ReplayForTests();
            Check(System.IO.File.Exists("Assets/Booth/Dress/Dress.prefab") && System.IO.File.ReadAllText("Assets/Booth/Dress/Dress.prefab.meta") == dressMeta,
                "Undo moves the item back with its GUID");
            Check(System.IO.File.ReadAllText("Assets/Booth.meta") == shopMeta, "The shop folder comes back with the same GUID");
            Check(!Directory.Exists("Assets/Rurune_kaihen"), "Folders created for the move are removed when empty");
            Check(ItemLedger.All()[0].prefabs[0].path == "Assets/Booth/Dress/Dress.prefab" && ItemLedger.All()[0].assetPaths.Contains("Assets/Booth"),
                "The ledger goes back");
            rewritten = JsonUtility.FromJson<PackageImportUndo.Snapshot>(System.IO.File.ReadAllText("Library/KaihenNavi/Imports/x/state.json"));
            Check(rewritten.files[0].path == "Assets/Booth/Dress/Dress.prefab" && rewritten.directories[0] == "Assets/Booth/Dress", "The snapshot goes back");

            // やり直し
            applied.SetValue(journal, true);
            Undo.ReplayForTests();
            Check(System.IO.File.Exists("Assets/Rurune_kaihen/Cloth/Dress/Dress.prefab") && !Directory.Exists("Assets/Booth"), "Redo moves again");
            Check(ItemLedger.All()[0].prefabs[0].path == "Assets/Rurune_kaihen/Cloth/Dress/Dress.prefab", "Redo rewrites the ledger again");

            // 以前の取り込みが残っているショップは消さない。失敗したら途中までを戻す
            File("Assets/Shop2/Old/o.prefab");
            File("Assets/Shop2/New/n.prefab");
            Folder("Assets/Shop2");
            var keep = ImportSorter.BuildTargets(new[] { "Assets/Shop2/New/n.prefab" }).Single();
            error = ImportSorter.Move("Assets/Rurune_kaihen", new[] { new KeyValuePair<SortTarget, string>(keep, "Cloth") }, out moved);
            Check(error == null && Directory.Exists("Assets/Shop2/Old") && System.IO.File.Exists("Assets/Rurune_kaihen/Cloth/New/n.prefab"),
                "A shop with earlier imports is kept");
            File("Assets/Fail1/f.prefab");
            File("Assets/Fail2/f.prefab");
            AssetDatabase.MoveResultForTests = (from, to) =>
            {
                if (from == "Assets/Fail2") return "Simulated failure";
                Directory.Move(from, to);
                if (System.IO.File.Exists(from + ".meta")) System.IO.File.Move(from + ".meta", to + ".meta");
                return "";
            };
            var fails = ImportSorter.BuildTargets(new[] { "Assets/Fail1/f.prefab", "Assets/Fail2/f.prefab" });
            var count = instances.OfType<ImportSortUndo>().Count();
            error = ImportSorter.Move("Assets/Rurune_kaihen", fails.Select(t => new KeyValuePair<SortTarget, string>(t, "Weapon")).ToList(), out moved);
            Check(error != null && moved == 0 && Directory.Exists("Assets/Fail1") && Directory.Exists("Assets/Fail2")
                && !Directory.Exists("Assets/Rurune_kaihen/Weapon"), "A failed move rolls back everything it did: " + error);
            Check(instances.OfType<ImportSortUndo>().Count() == count + 1, "Failed moves create a journal that is discarded");
        }
    }
}
