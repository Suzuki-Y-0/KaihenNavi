using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    internal enum LedgerItemKind { Outfit, Accessory, Avatar, Other }

    [Serializable]
    internal sealed class LedgerPrefab
    {
        public string path;
        public LedgerItemKind kind;
    }

    [Serializable]
    internal sealed class LedgerPackage
    {
        public string packageName;
        public string importedAt;
        public bool scanned;
        public string commonFolder;
        // Prefab 以外のファイルも PackageOf で引けるように残す。
        public List<string> assetPaths = new List<string>();
        public List<LedgerPrefab> prefabs = new List<LedgerPrefab>();
    }

    /// <summary>購入したファイルは動かさず、取り込み元だけを Editor 内に記録する。</summary>
    [InitializeOnLoad]
    internal static class ItemLedger
    {
        private const string LedgerPath = WorkFolders.Root + "/Ledger/items.json";
        private static List<LedgerPackage> _cachedPackages;

        private const string PendingKey = "KaihenNavi.ItemLedger.Pending";

        [Serializable]
        private sealed class LedgerData
        {
            public List<LedgerPackage> packages = new List<LedgerPackage>();
        }

        [Serializable]
        private sealed class PendingImport
        {
            public string packageName;
            public List<string> paths = new List<string>();
        }

        static ItemLedger()
        {
            AssetDatabase.importPackageStarted += Started;
            AssetDatabase.importPackageCompleted += Completed;
            AssetDatabase.importPackageCancelled += Cancelled;
            AssetDatabase.importPackageFailed += Failed;
        }

        public static List<LedgerPackage> All()
        {
            try
            {
                return _cachedPackages ?? (_cachedPackages = ReadPackages());
            }
            catch (Exception e)
            {
                Warn(e);
                return _cachedPackages = new List<LedgerPackage>();
            }
        }

        private static List<LedgerPackage> ReadPackages()
        {
            if (!File.Exists(LedgerPath)) return new List<LedgerPackage>();
            var data = JsonUtility.FromJson<LedgerData>(File.ReadAllText(LedgerPath, Encoding.UTF8));
            if (data == null || data.packages == null) throw new InvalidDataException("Invalid item ledger.");
            return data.packages;
        }

        public static string PackageOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var path = assetPath.Replace('\\', '/');
            // 複数の商品が同じファイルを含むときは、最後に取り込んだ商品を表示する。
            var package = All().Where(p => p != null && p.assetPaths != null && p.assetPaths.Contains(path))
                .OrderByDescending(p => p.importedAt, StringComparer.Ordinal).FirstOrDefault();
            return package == null ? null : package.packageName;
        }

        private static void Started(string packageName)
        {
            try
            {
                SessionState.SetString(PendingKey, JsonUtility.ToJson(new PendingImport { packageName = packageName }));
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void Collect(string[] importedAssets)
        {
            try
            {
                var pending = ReadPending();
                if (pending == null) return;
                pending.paths = pending.paths.Concat(importedAssets.Select(p => p.Replace('\\', '/')))
                    .Distinct().ToList();
                // unitypackage 内のスクリプトでドメインリロードされても収集を続ける。
                SessionState.SetString(PendingKey, JsonUtility.ToJson(pending));
            }
            catch (Exception e) { Warn(e); }
        }

        private static PendingImport ReadPending()
        {
            var json = SessionState.GetString(PendingKey, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PendingImport>(json);
        }

        private static void Completed(string packageName)
        {
            try
            {
                var pending = ReadPending();
                // 保存した JSON 自身のインポートを収集しないよう先に終了する。
                SessionState.EraseString(PendingKey);
                if (pending == null || pending.packageName != packageName) return;
                // 壊れた台帳を空の台帳で上書きしない。読めなければ警告だけで終了する。
                var packages = ReadPackages();
                var package = packages.FirstOrDefault(p => p != null && !p.scanned && p.packageName == packageName);
                if (package == null)
                {
                    package = new LedgerPackage { packageName = packageName };
                    packages.Add(package);
                }
                package.importedAt = DateTime.UtcNow.ToString("o");
                package.assetPaths = (package.assetPaths ?? new List<string>()).Concat(pending.paths)
                    .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
                package.commonFolder = CommonFolder(package.assetPaths);
                package.prefabs = package.assetPaths.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    .Select(p => new LedgerPrefab { path = p, kind = Classify(p) }).ToList();
                package.scanned = false;
                Save(packages);
            }
            catch (Exception e) { Warn(e); }
        }

        private static void Save(List<LedgerPackage> packages)
        {
            try
            {
                WorkFolders.EnsureReadme();
                WorkFolders.Ensure(WorkFolders.Root + "/Ledger");
                // 取り込み・発見の記録はシーンの Undo とは独立して保存する。
                File.WriteAllText(LedgerPath, JsonUtility.ToJson(new LedgerData { packages = packages }, true), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(LedgerPath);
            }
            finally { _cachedPackages = null; }
        }

        /// <summary>購入したファイルを変更せず、今ある Prefab を台帳に登録する。</summary>
        public static void ScanExisting()
        {
            try
            {
                EditorUtility.DisplayProgressBar(L10n.Tr("pick.owned"), L10n.Tr("pick.scanning"), 0f);
                // 壊れた台帳は上書きしない。取り込み由来の記録はそのまま残す。
                var packages = ReadPackages().Where(p => p != null && !p.scanned).ToList();
                var imported = new HashSet<string>(packages.SelectMany(p =>
                    (p.assetPaths ?? new List<string>()).Concat(
                        (p.prefabs ?? new List<LedgerPrefab>()).Where(f => f != null).Select(f => f.path))), StringComparer.Ordinal);
                var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(IsScanPath)
                    .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
                var folders = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var group in paths.GroupBy(p => p.Split('/').Length > 2 ? p.Split('/')[1] : ""))
                {
                    var root = group.Key.Length == 0 ? "Assets" : "Assets/" + group.Key;
                    GroupFolders(root, group.ToList(), 0, folders);
                }
                var scanned = new Dictionary<string, LedgerPackage>(StringComparer.Ordinal);
                for (var i = 0; i < paths.Count; i++)
                {
                    var path = paths[i];
                    EditorUtility.DisplayProgressBar(L10n.Tr("pick.owned"),
                        L10n.Tr("pick.scanning") + " (" + (i + 1) + "/" + paths.Count + ")", (float)i / paths.Count);
                    if (imported.Contains(path)) continue;
                    var folder = folders[path];
                    LedgerPackage package;
                    if (!scanned.TryGetValue(folder, out package))
                    {
                        package = new LedgerPackage { packageName = Path.GetFileName(folder), commonFolder = folder, scanned = true };
                        scanned.Add(folder, package);
                    }
                    package.assetPaths.Add(path);
                    package.prefabs.Add(new LedgerPrefab { path = path, kind = Classify(path) });
                }
                packages.AddRange(scanned.Values);
                Save(packages);
            }
            catch (Exception e)
            {
                Warn(e);
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("pick.scanFailed"), L10n.Tr("dialog.ok"));
            }
            finally
            {
                _cachedPackages = null;
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool IsScanPath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.StartsWith(WorkFolders.Root + "/", StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return false;
            var parts = path.Split('/');
            return !parts.Take(parts.Length - 1).Any(p =>
                p.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Samples", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Packages", StringComparison.OrdinalIgnoreCase));
        }

        private static void GroupFolders(string folder, List<string> paths, int depth, Dictionary<string, string> result)
        {
            var originalFolder = folder;
            // _Project/Items/Clothing/作者名/商品名 のような深い置き場もあるので、5 段まで分岐を探す。
            // 分岐が見つからなければ、元の商品フォルダ名を維持する。
            while (true)
            {
                var direct = paths.Any(p => p.LastIndexOf('/') == folder.Length);
                var children = paths.Where(p => p.LastIndexOf('/') > folder.Length)
                    .GroupBy(p => p.Substring(0, p.IndexOf('/', folder.Length + 1))).ToList();
                if (depth >= 5 || direct || children.Count == 0) break;
                if (children.Count >= 2)
                {
                    foreach (var child in children) GroupFolders(child.Key, child.ToList(), depth + 1, result);
                    return;
                }
                folder = children[0].Key;
                depth++;
            }
            foreach (var path in paths) result[path] = originalFolder;
        }

        private static LedgerItemKind Classify(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponentInChildren<VRCAvatarDescriptor>(true) != null)
                return LedgerItemKind.Avatar;
            var kind = ItemPicker.KindOf(prefab);
            return kind == ItemKind.Outfit ? LedgerItemKind.Outfit
                : kind == ItemKind.Accessory ? LedgerItemKind.Accessory : LedgerItemKind.Other;
        }

        private static string CommonFolder(List<string> paths)
        {
            string common = null;
            foreach (var path in paths)
            {
                var folder = AssetDatabase.IsValidFolder(path) ? path : (Path.GetDirectoryName(path) ?? "").Replace('\\', '/');
                if (common == null) common = folder;
                while (common.Length > 0 && folder != common && !folder.StartsWith(common + "/", StringComparison.Ordinal))
                    common = (Path.GetDirectoryName(common) ?? "").Replace('\\', '/');
            }
            return common ?? "";
        }

        private static void Cancelled(string packageName)
        {
            try { SessionState.EraseString(PendingKey); }
            catch (Exception e) { Warn(e); }
        }

        private static void Failed(string packageName, string errorMessage) { Cancelled(packageName); }

        private static void Warn(Exception e) { Debug.LogWarning("[KaihenNavi] Item ledger: " + e.Message); }
    }

    internal sealed class ItemLedgerPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            ItemLedger.Collect(importedAssets);
        }
    }
}
