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
        public string konoAssetId;
        // 空の ID + true はユーザーが明示的に結び付けを外した状態。
        public bool konoAssetManual;
        [NonSerialized] internal int konoRevision = -1;
        [NonSerialized] internal KonoAssetItem konoResolved;
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
        private static Dictionary<string, LedgerPackage> _recordsByPath;
        private static List<LedgerPackage> _projectItems;

        public static event Action Changed;
        private static readonly Dictionary<string, KonoAssetLedgerLinkUndo> LinkUndo = new Dictionary<string, KonoAssetLedgerLinkUndo>();

        internal const string LedgerFile = LedgerPath;

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
            Undo.undoRedoPerformed += RestoreLinks;
            KonoAssetLink.Changed += () => _projectItems = null;
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

        public static LedgerPackage RecordOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (_recordsByPath == null)
            {
                _recordsByPath = new Dictionary<string, LedgerPackage>(StringComparer.Ordinal);
                // 同じファイルが複数の取り込みにある場合は、最後の取り込みを優先する。
                foreach (var package in All().Where(p => p != null).OrderBy(p => p.importedAt, StringComparer.Ordinal))
                {
                    var paths = (package.assetPaths ?? new List<string>()).Concat(
                        (package.prefabs ?? new List<LedgerPrefab>()).Where(p => p != null).Select(p => p.path));
                    foreach (var path in paths.Where(p => !string.IsNullOrEmpty(p))) _recordsByPath[path.Replace('\\', '/')] = package;
                }
            }
            LedgerPackage record;
            return _recordsByPath.TryGetValue(assetPath.Replace('\\', '/'), out record) ? record : null;
        }

        public static string PackageOf(string assetPath)
        {
            var package = RecordOf(assetPath);
            return package == null ? null : package.packageName;
        }

        public static KonoAssetItem KonoItem(LedgerPackage package)
        {
            if (package == null) return null;
            var revision = KonoAssetLink.Revision;
            if (package.konoRevision == revision) return package.konoResolved;
            package.konoRevision = revision;
            package.konoResolved = !string.IsNullOrEmpty(package.konoAssetId) ? KonoAssetLink.Find(package.konoAssetId)
                : package.konoAssetManual ? null
                : package.scanned ? KonoAssetLink.FindByFolderName(Path.GetFileName(package.commonFolder ?? package.packageName))
                : KonoAssetLink.FindByPackageName(package.packageName);
            return package.konoResolved;
        }

        private static string LinkKey(LedgerPackage package)
        {
            return package.scanned ? "folder:" + package.commonFolder + "\n" +
                (package.assetPaths ?? new List<string>()).OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
                : "package:" + package.packageName;
        }

        public static void SetKonoItemForPath(string path, KonoAssetItem item)
        {
            try
            {
                var package = RecordOf(path);
                if (package == null)
                {
                    var packages = ReadPackages();
                    var folder = (Path.GetDirectoryName(path) ?? "").Replace('\\', '/');
                    package = new LedgerPackage { packageName = Path.GetFileName(folder), commonFolder = folder, scanned = true };
                    packages.Add(package);
                    package.assetPaths.Add(path);
                    package.prefabs.Add(new LedgerPrefab { path = path, kind = Classify(path) });
                    Save(packages);
                }
                SetKonoItem(package, item);
            }
            catch (Exception e) { Warn(e); }
        }

        public static void SetKonoItem(LedgerPackage package, KonoAssetItem item)
        {
            try
            {
                // 常にディスクから読み、古い表示の台帳で新しい取り込みを上書きしない。
                var packages = ReadPackages();
                var key = LinkKey(package);
                var current = packages.FirstOrDefault(p => p != null && LinkKey(p) == key);
                if (current == null) return;
                KonoAssetLedgerLinkUndo state;
                if (!LinkUndo.TryGetValue(key, out state))
                {
                    state = ScriptableObject.CreateInstance<KonoAssetLedgerLinkUndo>();
                    state.hideFlags = HideFlags.HideAndDontSave;
                    LinkUndo[key] = state;
                }
                state.itemId = current.konoAssetId;
                state.manual = current.konoAssetManual;
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(L10n.Tr("kono.selectProduct"));
                Undo.RecordObject(state, L10n.Tr("kono.selectProduct"));
                state.itemId = item == null ? null : item.id;
                state.manual = true;
                current.konoAssetId = state.itemId;
                current.konoAssetManual = true;
                Save(packages);
            }
            catch (Exception e) { Warn(e); }
        }

        private static void RestoreLinks()
        {
            if (LinkUndo.Count == 0) return;
            try
            {
                var packages = ReadPackages();
                var changed = false;
                foreach (var package in packages.Where(p => p != null))
                {
                    KonoAssetLedgerLinkUndo state;
                    if (!LinkUndo.TryGetValue(LinkKey(package), out state) || state == null) continue;
                    if (package.konoAssetId == state.itemId && package.konoAssetManual == state.manual) continue;
                    package.konoAssetId = state.itemId;
                    package.konoAssetManual = state.manual;
                    changed = true;
                }
                if (changed) Save(packages);
            }
            catch (Exception e) { Warn(e); }
        }

        /// <summary>選んだ商品の取り込み元を、同名パッケージがある場合も確実に記録する。</summary>
        public static void ImportProduct(KonoAssetItem product, string path)
        {
            if (product == null || !KonoAssetLink.Packages(product).Contains(path)) return;
            if (!PackageImportUndo.Begin(path)) return;
            SessionState.SetString("KaihenNavi.ItemLedger.ImportProduct", product.id);
            try { AssetDatabase.ImportPackage(path, true); }
            catch
            {
                SessionState.EraseString("KaihenNavi.ItemLedger.ImportProduct");
                PackageImportUndo.Cancel();
                throw;
            }
        }

        internal static void Invalidate()
        {
            _cachedPackages = null;
            _recordsByPath = null;
            _projectItems = null;
            if (Changed != null) Changed();
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
                if (!package.konoAssetManual)
                {
                    var selectedId = SessionState.GetString("KaihenNavi.ItemLedger.ImportProduct", "");
                    var linked = KonoAssetLink.Find(selectedId) ?? KonoAssetLink.FindByPackageName(packageName);
                    if (linked != null) package.konoAssetId = linked.id;
                }
                Save(packages);
                SessionState.EraseString("KaihenNavi.ItemLedger.ImportProduct");
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
            finally { _cachedPackages = null; _recordsByPath = null; _projectItems = null; }
            if (Changed != null) Changed();
        }

        /// <summary>プロジェクト内の服を読み取りだけで発見する。検索入力のたびには走査しない。</summary>
        public static List<LedgerPackage> ProjectItems(bool reload = false)
        {
            if (!reload && _projectItems != null) return _projectItems;
            var packages = All().Where(p => p != null && (!p.scanned || p.konoAssetManual)).ToList();
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
                if (imported.Contains(path)) continue;
                // KonoAsset に同じ名前の商品があれば、そのフォルダを商品のまとまりにする（置き場や分類のフォルダより確か）
                KonoAssetItem konoItem;
                var folder = KonoFolderOf(path, out konoItem) ?? folders[path];
                LedgerPackage package;
                if (!scanned.TryGetValue(folder, out package))
                {
                    package = new LedgerPackage { packageName = Path.GetFileName(folder), commonFolder = folder, scanned = true };
                    var linked = konoItem ?? KonoAssetLink.FindByFolderName(package.packageName);
                    if (linked != null) package.konoAssetId = linked.id;
                    scanned.Add(folder, package);
                }
                package.assetPaths.Add(path);
                package.prefabs.Add(new LedgerPrefab { path = path, kind = Classify(path) });
            }
            packages.AddRange(scanned.Values);
            return _projectItems = packages;
        }

        // 置き場や分類によく使われる名前。KonoAsset の商品名と偶然一致しやすいので照合しない
        private static readonly HashSet<string> GenericFolders = new HashSet<string>(StringComparer.Ordinal)
        {
            "assets", "project", "items", "item", "prefab", "prefabs", "clothing", "clothes", "cloth", "outfit", "outfits",
            "accessories", "accessory", "material", "materials", "texture", "textures", "fbx", "model", "models",
            "booth", "gimmick", "gimmicks", "avatar", "avatars", "shader", "shaders", "animation", "animations", "editor",
        };

        /// <summary>
        /// Prefab の親フォルダを浅い順にたどり、KonoAsset の商品名・unitypackage 名と 1 つだけ一致するフォルダを返す。
        /// 深い所（アバター名の付いたフォルダなど）は別の商品と一致しやすいので、浅い所を優先する。
        /// </summary>
        private static string KonoFolderOf(string assetPath, out KonoAssetItem item)
        {
            item = null;
            if (!KonoAssetLink.IsAvailable) return null;
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length - 1; i++)
            {
                current += "/" + parts[i];
                var name = parts[i];
                if (name.StartsWith("_", StringComparison.Ordinal)) continue;
                if (GenericFolders.Contains(KonoAssetLink.Normalize(name))) continue;
                var found = KonoAssetLink.FindByFolderName(name);
                if (found == null) continue;
                item = found;
                return current;
            }

            return null;
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
            try { SessionState.EraseString(PendingKey); SessionState.EraseString("KaihenNavi.ItemLedger.ImportProduct"); }
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
