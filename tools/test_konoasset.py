#!/usr/bin/env python3
"""Unity なしで名前照合・台帳の参照・ファイル名だけの走査を検証する。
JsonUtility / 画像のデコード / Undo の実処理は Unity 上で確認する。
"""
from pathlib import Path
import hashlib
import subprocess
import tempfile

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
    subprocess.run(["mono", str(executable), str(data), str(temp / "external"), str(symlink_workspace)], cwd=temp, check=True)
    assert before == snapshot(), "external files were modified"
    print("External source files unchanged.")
