#!/usr/bin/env python3
"""Unity なしで名前照合・台帳の参照・ファイル名だけの走査を検証する。
JsonUtility / 画像のデコード / Undo の実処理は Unity 上で確認する。
"""
from pathlib import Path
import hashlib
import subprocess
import tempfile
import io
import tarfile

repo = Path(__file__).resolve().parent.parent
source = r'''
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jeni.KaihenNavi;

internal static class KonoAssetChecks
{
    private static int count;
    private static readonly Type Link = typeof(KonoAssetLink);
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        count++;
    }
    private static void Set(Type type, string field, object value)
    {
        type.GetField(field, PrivateStatic).SetValue(null, value);
    }
    private static object Call(string method, params object[] args)
    {
        return Link.GetMethod(method, PrivateStatic).Invoke(null, args);
    }
    private static KonoAssetItem Item(string id, string name, string[] packages)
    {
        return new KonoAssetItem { id = id, description = new KonoAssetDescription { name = name },
            packageNames = packages, supportedAvatars = new[] { "しなの", "Celestia" } };
    }
    private static void Use(params KonoAssetItem[] items)
    {
        ((IDictionary)Link.GetField("ById", PrivateStatic).GetValue(null)).Clear();
        ((IDictionary)Link.GetField("ByPackage", PrivateStatic).GetValue(null)).Clear();
        Call("UseIndex", (object)items);
        Set(Link, "_revision", (int)Link.GetField("_revision", PrivateStatic).GetValue(null) + 1);
    }
    public static void Main(string[] args)
    {
        // Unity のディスク探索はモックせず、純粋な照合の間だけ既存キャッシュとして固定する。
        Set(Link, "_checked", true);
        Set(Link, "_nextCheck", double.MaxValue);
        var monolith = Item("one", "MONOLITH", new[] { "MONOLITH_Shinano", "MONOLITH Shinano" });
        var hair = Item("two", "Long Hair", new[] { "Long-Hair", "Shared" });
        Use(monolith, hair);
        Check(KonoAssetLink.Normalize("ＭＯＮＯＬＩＴＨ _- しなの") == "monolithしなの", "Unicode normalization");
        Check(KonoAssetLink.FindByPackageName("MONOLITH - Shinano.unitypackage") == monolith, "package normalization");
        Check(KonoAssetLink.FindByPackageName("missing") == null, "unknown package");
        Check(KonoAssetLink.Find("one") == monolith && KonoAssetLink.Find(null) == null, "ID lookup");
        Check(KonoAssetLink.FindByFolderName("MONOLITH") == monolith, "product folder match");
        Check(KonoAssetLink.FindByFolderName("LongHair 2026") == hair, "folder contains product");
        Check(KonoAssetLink.FindByFolderName("MONO") == monolith, "product contains folder");
        Check(KonoAssetLink.FindByFolderName("MON") == null, "short folder rejected");
        Check(KonoAssetLink.FindByFolderName("MONOLITH LongHair") == null, "ambiguous folder rejected");
        Check(KonoAssetLink.SupportsAvatar(monolith, new[] { "しなの" }), "supported Japanese avatar");
        Check(KonoAssetLink.SupportsAvatar(monolith, new[] { "celestia" }), "supported Latin avatar");
        Check(!KonoAssetLink.SupportsAvatar(monolith, new[] { "マヌカ" }), "unsupported avatar");
        Check(!KonoAssetLink.SupportsAvatar(null, new[] { "しなの" }), "missing product");
        Check(!KonoAssetLink.SupportsAvatar(monolith, new[] { "" }), "empty keyword rejected");
        var duplicate = Item("three", "Other", new[] { "MONOLITH-Shinano", "Shared" });
        Use(monolith, hair, duplicate);
        Check(KonoAssetLink.FindByPackageName("MONOLITH_Shinano") == null, "same package in different products rejected");
        Check(KonoAssetLink.FindByPackageName("Shared") == null, "ambiguous package rejected");
        Check(KonoAssetLink.FindByFolderName("MONOLITH_Shinano") == null, "ambiguous filename folder rejected");
        Use(monolith, hair);
        Check(KonoAssetLink.FindByPackageName("MONOLITH_Shinano") == monolith, "aliases in one product are unambiguous");

        // 未取り込みの商品も全件を検索する。候補数や台帳の有無で検索対象を制限しない。
        var wicca = Item("wicca", "ＷＩＣＣＡ", new[] { "WICCA_Celestia" });
        wicca.description.creator = "ＭｙＳｈｏｐ";
        var sweet = Item("sweet", "スイートデビル", new[] { "Sweet_Devil" });
        sweet.description.creator = "小さなお店";
        Use(monolith, hair, wicca, sweet);
        var library = ItemLibrary.Collect(KonoAssetLink.All, new LedgerPackage[0], ItemKind.Outfit);
        Check(library.Single(i => i.Matches("wicca")).Product == wicca, "unimported WICCA found without ledger");
        Check(library.Single(i => i.Matches("すいーと")).Product == sweet, "hiragana product substring");
        Check(library.Single(i => i.Matches("ｽｲｰﾄ")).Product == sweet, "halfwidth katakana product substring");
        Check(library.Single(i => i.Matches("myshop")).Product == wicca, "normalized shop substring");
        Check(library.Single(i => i.Matches("devil")).Product == sweet, "package filename substring");
        var owned = new LedgerPackage { konoAssetId = "wicca", packageName = "購入した服", commonFolder = "Assets/Wardrobe", prefabs = new List<LedgerPrefab> {
            new LedgerPrefab { path = "Assets/Wardrobe/VersionA.prefab", kind = LedgerItemKind.Outfit },
            new LedgerPrefab { path = "Assets/Wardrobe/VersionB.prefab", kind = LedgerItemKind.Outfit },
            new LedgerPrefab { path = "Assets/Wardrobe/Earring.prefab", kind = LedgerItemKind.Accessory } } };
        library = ItemLibrary.Collect(KonoAssetLink.All, new[] { owned, owned }, ItemKind.Outfit);
        var combined = library.Single(i => i.Product == wicca);
        Check(combined.Paths.Count == 2, "one product merges prefabs, removes duplicates and filters kind");
        Check(combined.Matches("VERSIONb") && combined.Matches("wardrobe") && combined.Matches("購入した"), "prefab, folder and ledger names searched");
        Check(combined.ForAvatar(new[] { "セレスティア", "Celestia" }), "avatar metadata filter");
        Check(!combined.ForAvatar(new[] { "Manuka" }), "unrelated avatar filtered");
        var local = new LedgerPackage { packageName = "Local", commonFolder = "Assets/Local", prefabs = new List<LedgerPrefab> {
            new LedgerPrefab { path = "Assets/Local/ｾﾚｽﾃｨｱ/Hat.prefab", kind = LedgerItemKind.Accessory } } };
        var localItem = ItemLibrary.Collect(new KonoAssetItem[0], new[] { local }, ItemKind.Accessory).Single();
        Check(localItem.Matches("hat") && localItem.ForAvatar(new[] { "せれすてぃあ" }), "project-only item and normalized avatar path");
        var many = Enumerable.Range(0, 361).Select(i => Item("id" + i, "Product" + i, new string[0])).ToArray();
        Check(ItemLibrary.Collect(many, new LedgerPackage[0], ItemKind.Outfit).Count == 361, "all 361 products retained before pagination");
        Use(monolith, hair);

        var legacy = new LedgerPackage { packageName = "MONOLITH_Shinano" };
        Check(ItemLedger.KonoItem(legacy) == monolith && legacy.konoAssetId == null, "legacy record resolves without saving");
        var scanned = new LedgerPackage { scanned = true, commonFolder = "Assets/MONOLITH" };
        Check(ItemLedger.KonoItem(scanned) == monolith, "scanned folder resolves");
        var unlinked = new LedgerPackage { packageName = "MONOLITH_Shinano", konoAssetManual = true };
        Check(ItemLedger.KonoItem(unlinked) == null, "manual unlink stays unlinked");
        var manual = new LedgerPackage { packageName = "MONOLITH_Shinano", konoAssetId = "two", konoAssetManual = true };
        Check(ItemLedger.KonoItem(manual) == hair, "manual choice takes precedence");
        var deleted = new LedgerPackage { packageName = "MONOLITH_Shinano", konoAssetId = "deleted", konoAssetManual = true };
        Check(ItemLedger.KonoItem(deleted) == null, "deleted manual product is not replaced");
        Use(hair);
        Check(ItemLedger.KonoItem(legacy) == null, "metadata reload invalidates resolved record");
        var old = new LedgerPackage { packageName = "old", importedAt = "2025", assetPaths = new List<string> { "Assets/Item.prefab" } };
        var latest = new LedgerPackage { packageName = "new", importedAt = "2026", prefabs = new List<LedgerPrefab> { new LedgerPrefab { path = "Assets/Item.prefab" } } };
        Set(typeof(ItemLedger), "_cachedPackages", new List<LedgerPackage> { latest, old });
        Set(typeof(ItemLedger), "_recordsByPath", null);
        Check(ItemLedger.RecordOf("Assets\\Item.prefab") == latest, "most recent import wins including prefab-only records");
        Check(ItemLedger.RecordOf("Assets/Missing.prefab") == null, "unrecorded prefab");
        Check(ItemLedger.PackageOf("Assets/Item.prefab") == "new", "existing package API preserved");

        var names = new List<string>();
        var packageFile = Path.Combine(args[0], "nested", "Locked.unitypackage");
        // 読み取りを禁止した実ファイルも、名前だけなら走査できる。
        using (File.Open(packageFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Call("ListPackages", args[0], names);
        Check(names.OrderBy(x => x).SequenceEqual(new[] { "Locked", "Root" }), "nested filenames only; symlink cycle skipped");
        Check((bool)Call("IsRoot", args[1]), "metadata root recognized");
        Check(!(bool)Call("IsRoot", Path.Combine(args[1], "missing")), "missing root is unavailable");
        var stamps = (long[])Call("ReadStamps", args[1]);
        Check(stamps.Length == 4 && stamps[0] > 0 && stamps.Skip(1).All(t => t == -1), "metadata timestamps and missing files");
        var indexType = Link.GetNestedType("Index", BindingFlags.NonPublic);
        var index = Activator.CreateInstance(indexType, true);
        indexType.GetField("root").SetValue(index, args[1]);
        Call("SaveIndex", index);
        Check(File.Exists("Library/KaihenNavi/konoasset-index.json"), "cache is written beneath project Library");
        File.Delete("Library/KaihenNavi/konoasset-index.json");
        indexType.GetField("root").SetValue(index, Directory.GetCurrentDirectory());
        Call("SaveIndex", index);
        Check(!File.Exists("Library/KaihenNavi/konoasset-index.json"), "cache is not written inside external root");
        // unitypackage の実体を読み取りだけで検査し、既存アセットとの衝突を見逃さない。
        var importType = typeof(PackageImportUndo);
        var archive = (IList)importType.GetMethod("ReadArchive", PrivateStatic).Invoke(null, new object[] { args[3] });
        Check(archive.Count == 2, "unitypackage tar manifest read");
        var archiveEntry = archive[0];
        var archiveType = archiveEntry.GetType();
        Check((string)archiveType.GetField("path").GetValue(archiveEntry) == "Assets/服/Item.prefab", "Japanese pathname read");
        Check((string)archiveType.GetField("guid").GetValue(archiveEntry) == "0123456789abcdef0123456789abcdef", "asset GUID read for relocation conflict check");
        var existing = Path.Combine(args[0], "same.txt");
        File.WriteAllText(existing, "asset contents", new System.Text.UTF8Encoding(false));
        var checkExisting = importType.GetMethod("CheckExisting", PrivateStatic);
        checkExisting.Invoke(null, new[] { (object)existing, archiveType.GetField("assetHash").GetValue(archiveEntry) });
        count++;
        var refused = false;
        try { checkExisting.Invoke(null, new object[] { existing, "different" }); }
        catch (TargetInvocationException e) { refused = e.InnerException is IOException; }
        Check(refused && File.ReadAllText(existing) == "asset contents", "original asset overwrite refused");
        foreach (var invalid in new[] { "../outside", "Assets/../outside", "Packages/x", "Assets//x", "Assets/./x", "Assets/x\\y" })
        {
            refused = false;
            try { PackageImportUndo.ValidatePath(invalid); } catch (InvalidDataException) { refused = true; }
            Check(refused, "unsafe path refused: " + invalid);
        }
        Directory.CreateDirectory("Assets/Added");
        Directory.CreateDirectory("Assets/KaihenNavi/Ledger");
        var store = Path.Combine(args[0], "undo");
        Directory.CreateDirectory(store);
        File.WriteAllText("Assets/Added/Item.prefab", "new prefab");
        File.WriteAllText("Assets/Added/Item.prefab.meta", "new GUID");
        File.WriteAllText("Assets/Original.prefab", "unchanged original");
        File.WriteAllText(ItemLedger.LedgerFile, "new ledger");
        File.WriteAllText(Path.Combine(store, "0.after"), "new prefab");
        File.WriteAllText(Path.Combine(store, "1.after"), "new GUID");
        File.WriteAllText(Path.Combine(store, "3.before"), "old ledger");
        File.WriteAllText(Path.Combine(store, "3.after"), "new ledger");
        var snapshot = new PackageImportUndo.Snapshot {
            directories = new List<string> { "Assets/Added" },
            files = new List<PackageImportUndo.FileState> {
                new PackageImportUndo.FileState { path = "Assets/Added/Item.prefab", after = true },
                new PackageImportUndo.FileState { path = "Assets/Added/Item.prefab.meta", after = true },
                new PackageImportUndo.FileState { path = "Assets/Original.prefab", before = true, after = true },
                new PackageImportUndo.FileState { path = ItemLedger.LedgerFile, before = true, after = true } } };
        PackageImportUndo.Apply(snapshot, store, false);
        Check(!Directory.Exists("Assets/Added") && File.ReadAllText(ItemLedger.LedgerFile) == "old ledger", "Undo removes imported files and restores ledger");
        Check(File.ReadAllText("Assets/Original.prefab") == "unchanged original", "Undo never rewrites original assets");
        PackageImportUndo.Apply(snapshot, store, true);
        Check(File.ReadAllText("Assets/Added/Item.prefab") == "new prefab" && File.ReadAllText("Assets/Added/Item.prefab.meta") == "new GUID", "Redo restores prefab and exact meta GUID");
        Check(File.ReadAllText(ItemLedger.LedgerFile) == "new ledger", "Redo restores ledger together");
        File.Delete(existing);
        Directory.Delete(store, true);
        Directory.SetCurrentDirectory(args[2]);
        indexType.GetField("root").SetValue(index, args[1]);
        Call("SaveIndex", index);
        Check(File.ReadAllText(Path.Combine(args[1], "metadata", "avatars.json")).Contains("version"), "cache symlink cannot overwrite source metadata");
        Console.WriteLine("KonoAsset checks passed (" + count + "). JSON, image decoding and Undo require Unity.");
    }
}
'''

with tempfile.TemporaryDirectory(prefix="kaihennavi-kono-") as temporary:
    temp = Path(temporary)
    data = temp / "external" / "data"
    (data / "nested").mkdir(parents=True)
    (data / "nested" / "Locked.unitypackage").write_bytes(b"do not read package contents")
    (data / "Root.unitypackage").write_bytes(b"do not read package contents")
    (data / "ignored.txt").write_bytes(b"ignore")
    (data / "nested" / "cycle").symlink_to(data, target_is_directory=True)
    metadata = temp / "external" / "metadata"
    metadata.mkdir()
    (metadata / "avatars.json").write_text('{"version":3,"data":[]}', encoding="utf-8")

    package = temp / "test.unitypackage"
    with tarfile.open(package, "w:gz", format=tarfile.USTAR_FORMAT) as archive:
        for guid, pathname in [("first", "Assets/服/Item.prefab"), ("second", "Assets/Empty")]:
            for suffix, payload in [("pathname", pathname.encode()), ("asset", b"asset contents"),
                                    ("asset.meta", b"fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n")]:
                info = tarfile.TarInfo(guid + "/" + suffix)
                info.size = len(payload)
                archive.addfile(info, io.BytesIO(payload))

    def snapshot():
        return {str(p.relative_to(temp)): (p.stat().st_mtime_ns, hashlib.sha256(p.read_bytes()).hexdigest())
                for p in temp.glob("external/**/*") if p.is_file()}

    symlink_workspace = temp / "symlink-workspace"
    symlink_cache = symlink_workspace / "Library/KaihenNavi"
    symlink_cache.mkdir(parents=True)
    (symlink_cache / "konoasset-index.json").symlink_to(metadata / "avatars.json")
    before = snapshot()
    harness = temp / "Checks.cs"
    harness.write_text(source, encoding="utf-8")
    executable = temp / "Checks.exe"
    sources = sorted((repo / "Packages/com.suzuki-y0.jeni.kaihen-navi/Editor").rglob("*.cs"))
    subprocess.run(["mcs", "-nologo", "-langversion:7", "-r:System.Core.dll", "-out:" + str(executable),
                    str(repo / "tools/stubs/UnityStubs.cs"), str(repo / "tools/stubs/VrcAndMaStubs.cs"),
                    *map(str, sources), str(harness)], check=True)
    subprocess.run(["mono", str(executable), str(data), str(temp / "external"), str(symlink_workspace), str(package)], cwd=temp, check=True)
    assert before == snapshot(), "external files were modified"
    print("External source files unchanged.")
