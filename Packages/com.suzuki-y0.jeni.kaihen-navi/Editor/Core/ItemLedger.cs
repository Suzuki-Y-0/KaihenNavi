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
                return ReadPackages();
            }
            catch (Exception e)
            {
                Warn(e);
                return new List<LedgerPackage>();
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
                var package = packages.FirstOrDefault(p => p != null && p.packageName == packageName);
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
                WorkFolders.EnsureReadme();
                WorkFolders.Ensure(WorkFolders.Root + "/Ledger");
                // 取り込みの事実を残す台帳なので、シーンの Undo とは独立して保存する。
                File.WriteAllText(LedgerPath, JsonUtility.ToJson(new LedgerData { packages = packages }, true), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(LedgerPath);
            }
            catch (Exception e) { Warn(e); }
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
