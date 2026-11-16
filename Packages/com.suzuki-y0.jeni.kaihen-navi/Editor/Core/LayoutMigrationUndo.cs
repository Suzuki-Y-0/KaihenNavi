using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 「Variant へ移す」（LayoutMigration）の控え。ファイルの移動は Unity の Undo の対象外なので、ImportSortUndo と同じく
    /// 移動の記録を Editor 内のオブジェクトに持ち、Undo には目印（applied）だけを載せる。Ctrl+Z で元の場所へ戻し、やり直しで再び移す。
    /// 中に書いたパス（avatar.json・台帳・取り込みの控え・履歴とインナーカラーの控え）も一緒に書き換える。アバターには付けない。
    /// </summary>
    [InitializeOnLoad]
    internal sealed class LayoutMigrationUndo : ScriptableObject
    {
        [Serializable]
        internal sealed class RemovedAsset
        {
            public string path;
            public bool folder;
            public string meta;
            /// <summary>ファイルの中身（Base64）。</summary>
            public string content;
        }

        [SerializeField] private List<string> createFolders = new List<string>();
        [SerializeField] private List<SortMove> moves = new List<SortMove>();
        [SerializeField] private List<string> deleteFiles = new List<string>();
        [SerializeField] private List<string> deleteFolders = new List<string>();
        [SerializeField] private List<SortMove> manifests = new List<SortMove>();
        [SerializeField] private List<string> dataFolders = new List<string>();
        [SerializeField] private List<string> created = new List<string>();
        [SerializeField] private List<string> createdFiles = new List<string>();
        [SerializeField] private List<RemovedAsset> removed = new List<RemovedAsset>();
        [SerializeField] private bool followed;
        [SerializeField] private bool applied;
        [SerializeField] private long order;
        [NonSerialized] private bool observed;

        /// <summary>移動・戻しが終わったあとに、画面へ知らせる。</summary>
        internal static event Action Changed;

        static LayoutMigrationUndo() { Undo.undoRedoPerformed += Replay; }
        private void OnEnable() { observed = applied; }

        /// <summary>移して、成功したら Ctrl+Z 1 回分として記録する。失敗したら途中までを戻して理由を返す。</summary>
        internal static string Run(LayoutMigration.Plan plan, string undoName)
        {
            var journal = CreateInstance<LayoutMigrationUndo>();
            journal.hideFlags = HideFlags.HideAndDontSave;
            journal.createFolders = plan.createFolders.ToList();
            journal.moves = plan.moves.ToList();
            journal.deleteFiles = plan.deleteFiles.ToList();
            journal.deleteFolders = plan.deleteFolders.ToList();
            journal.manifests = plan.manifests.ToList();
            journal.dataFolders = plan.dataFolders.ToList();
            journal.order = DateTime.UtcNow.Ticks;
            var error = journal.Forward();
            if (error != null)
            {
                DestroyImmediate(journal);
                Notify();
                return error;
            }
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            // 内容の記録を完成させてから、目印だけを Undo に載せる
            Undo.RecordObject(journal, undoName);
            journal.applied = true;
            journal.observed = true;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Notify();
            return null;
        }

        private static void Replay()
        {
            var journals = Resources.FindObjectsOfTypeAll<LayoutMigrationUndo>().Where(j => j.applied != j.observed).ToList();
            if (journals.Count == 0) return;
            foreach (var journal in journals.Where(j => !j.applied).OrderByDescending(j => j.order)
                .Concat(journals.Where(j => j.applied).OrderBy(j => j.order)))
            {
                try
                {
                    var error = journal.applied ? journal.Forward() : journal.Backward();
                    if (error != null) Debug.LogWarning("[KaihenNavi] " + error);
                }
                catch (Exception e) { Debug.LogWarning("[KaihenNavi] Layout migration undo: " + e.Message); }
                journal.observed = journal.applied;
            }
            Notify();
        }

        private static void Notify()
        {
            // 一覧・ベース・履歴・台帳の読み込みの控えを捨てる（場所が変わった）
            MyAvatar.ForgetBaseAvatars();
            BackupService.InvalidateCache();
            ItemLedger.Invalidate();
            if (Changed != null) Changed();
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>移す。途中で失敗したら、そこまでを戻して理由を返す。</summary>
        private string Forward()
        {
            created.Clear();
            createdFiles.Clear();
            removed.Clear();
            followed = false;
            var done = new List<SortMove>();
            try
            {
                foreach (var folder in createFolders)
                {
                    if (AssetDatabase.IsValidFolder(folder)) continue;
                    WorkFolders.Ensure(folder);
                    created.Add(folder);
                }
                foreach (var move in moves)
                {
                    if (!Exists(move.from)) throw new IOException(L10n.Tr("sort.missing", move.from));
                    if (Exists(move.to)) throw new IOException(L10n.Tr("sort.exists", move.to));
                    var parent = Path.GetDirectoryName(move.to);
                    if (!string.IsNullOrEmpty(parent)) WorkFolders.Ensure(parent.Replace('\\', '/'));
                    var error = AssetDatabase.MoveAsset(move.from, move.to);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    done.Add(move);
                }
                // 消すファイルは中身を控えてから（ベースの avatar.json など、小さいテキストだけ）
                foreach (var file in deleteFiles)
                {
                    if (!File.Exists(file)) continue;
                    removed.Add(new RemovedAsset
                    {
                        path = file, content = Convert.ToBase64String(File.ReadAllBytes(file)),
                        meta = File.Exists(file + ".meta") ? File.ReadAllText(file + ".meta") : null,
                    });
                    AssetDatabase.DeleteAsset(file);
                }
                // 空になった旧構成のフォルダだけを消す（残り物があれば残す）
                foreach (var folder in deleteFolders)
                {
                    if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) continue;
                    removed.Add(new RemovedAsset { path = folder, folder = true, meta = File.Exists(folder + ".meta") ? File.ReadAllText(folder + ".meta") : null });
                    AssetDatabase.DeleteAsset(folder);
                }
                Follow(done, true);
                followed = true;
                foreach (var data in dataFolders)
                {
                    var readme = data + "/README.md";
                    var existed = File.Exists(readme);
                    WorkFolders.EnsureReadme(data);
                    if (!existed && File.Exists(readme)) createdFiles.Add(readme);
                }
                return null;
            }
            catch (Exception e)
            {
                moves = done;
                try { Backward(); } catch (Exception inner) { Debug.LogWarning("[KaihenNavi] Layout migration rollback: " + inner.Message); }
                return L10n.Tr("layout.failed", e.Message);
            }
        }

        /// <summary>元の場所へ戻す。消したフォルダ・ファイルは同じ .meta（同じ GUID）で作り直す。</summary>
        private string Backward()
        {
            var errors = new List<string>();
            foreach (var file in createdFiles)
                if (File.Exists(file)) AssetDatabase.DeleteAsset(file);
            createdFiles.Clear();
            // 中に書いたパスは、ファイルがまだ移した先にあるうちに戻す
            if (followed) Follow(moves, false);
            followed = false;
            foreach (var asset in Enumerable.Reverse(removed))
            {
                if (Exists(asset.path)) continue;
                if (asset.folder) Directory.CreateDirectory(asset.path);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(asset.path));
                    File.WriteAllBytes(asset.path, Convert.FromBase64String(asset.content ?? ""));
                }
                if (!string.IsNullOrEmpty(asset.meta)) File.WriteAllText(asset.path + ".meta", asset.meta);
                AssetDatabase.ImportAsset(asset.path, ImportAssetOptions.ForceSynchronousImport);
            }
            foreach (var move in Enumerable.Reverse(moves))
            {
                if (!Exists(move.to) || Exists(move.from)) { errors.Add(move.to); continue; }
                var parent = Path.GetDirectoryName(move.from);
                if (!string.IsNullOrEmpty(parent)) WorkFolders.Ensure(parent.Replace('\\', '/'));
                var error = AssetDatabase.MoveAsset(move.to, move.from);
                if (!string.IsNullOrEmpty(error)) errors.Add(error);
            }
            // 移すために作ったフォルダは、空のときだけ消す（あとから入れた物は消さない）
            foreach (var folder in Enumerable.Reverse(created))
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(folder);
            return errors.Count == 0 ? null : L10n.Tr("layout.undoFailed", string.Join(", ", errors));
        }

        /// <summary>avatar.json・台帳・取り込みの控え・履歴とインナーカラーの控えに書いたパスを、移動に合わせる。</summary>
        private void Follow(List<SortMove> done, bool forward)
        {
            if (done.Count == 0) return;
            foreach (var manifest in manifests)
            {
                // 書き換えるときは、ファイルは移した先にある（戻すときも、移動を戻す前に書く）
                var prefab = forward ? manifest.to : manifest.from;
                var folder = Path.GetDirectoryName(manifest.to).Replace('\\', '/');
                var data = WorkFolders.ReadManifest(folder);
                if (data == null) continue;
                data.basePrefabPath = LayoutMigration.MapPath(data.basePrefabPath, done, forward);
                data.parentPrefabPath = LayoutMigration.MapPath(data.parentPrefabPath, done, forward);
                data.name = Path.GetFileNameWithoutExtension(prefab);
                data.prefab = Path.GetFileName(prefab);
                var path = folder + "/" + WorkFolders.ManifestName;
                File.WriteAllText(path, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
            }
            Func<string, string> map = p => LayoutMigration.MapPath(p, done, forward);
            ItemLedger.RewritePaths(map);
            PackageImportUndo.RewritePaths(map, true);
            LayoutMigration.FollowStatePaths(done, forward);
        }
    }
}
