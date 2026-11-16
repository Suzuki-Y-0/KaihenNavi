using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 振り分け（フォルダの移動）は Unity の Undo の対象外なので、移動の記録を Editor 内のオブジェクトに控える。
    /// Ctrl+Z では元の場所へ戻し、やり直しでは再び移す。台帳（ItemLedger）と取り込みの Undo の控え（PackageImportUndo）も追従させる。
    /// アバターには付けない。InnerColorAssetUndo と同じく、Undo には目印（applied）だけを載せる。
    /// </summary>
    [InitializeOnLoad]
    internal sealed class ImportSortUndo : ScriptableObject
    {
        [Serializable]
        internal sealed class RemovedFolder
        {
            public string path;
            public string meta;
        }

        [SerializeField] private List<string> createFolders = new List<string>();
        [SerializeField] private List<SortMove> moves = new List<SortMove>();
        [SerializeField] private List<string> shops = new List<string>();
        [SerializeField] private List<RemovedFolder> removed = new List<RemovedFolder>();
        [SerializeField] private List<string> created = new List<string>();
        [SerializeField] private bool applied;
        [SerializeField] private long order;
        [NonSerialized] private bool observed;

        /// <summary>移動・戻しが終わったあとに、画面へ知らせる。</summary>
        internal static event Action Changed;

        static ImportSortUndo() { Undo.undoRedoPerformed += Replay; }
        private void OnEnable() { observed = applied; }

        /// <summary>移して、成功したら Ctrl+Z 1 回分として記録する。失敗したら途中までの移動を戻して理由を返す。</summary>
        internal static string Run(List<string> createFolders, List<SortMove> moves, List<string> shops, string undoName)
        {
            var journal = CreateInstance<ImportSortUndo>();
            journal.hideFlags = HideFlags.HideAndDontSave;
            journal.createFolders = createFolders ?? new List<string>();
            journal.moves = moves ?? new List<SortMove>();
            journal.shops = shops ?? new List<string>();
            journal.order = DateTime.UtcNow.Ticks;
            var error = journal.Forward();
            if (error != null)
            {
                DestroyImmediate(journal);
                if (Changed != null) Changed();
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
            if (Changed != null) Changed();
            return null;
        }

        private static void Replay()
        {
            var journals = Resources.FindObjectsOfTypeAll<ImportSortUndo>().Where(j => j.applied != j.observed).ToList();
            if (journals.Count == 0) return;
            // 戻すときは新しい順、やり直すときは古い順
            foreach (var journal in journals.Where(j => !j.applied).OrderByDescending(j => j.order)
                .Concat(journals.Where(j => j.applied).OrderBy(j => j.order)))
            {
                try
                {
                    var error = journal.applied ? journal.Forward() : journal.Backward();
                    if (error != null) Debug.LogWarning("[KaihenNavi] " + error);
                }
                catch (Exception e) { Debug.LogWarning("[KaihenNavi] Import sort undo: " + e.Message); }
                journal.observed = journal.applied;
            }
            if (Changed != null) Changed();
        }

        /// <summary>移す。途中で失敗したら、そこまでを戻して理由を返す。</summary>
        private string Forward()
        {
            created.Clear();
            removed.Clear();
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
                    if (!AssetDatabase.IsValidFolder(move.from)) throw new IOException(L10n.Tr("sort.missing", move.from));
                    if (Directory.Exists(move.to) || File.Exists(move.to)) throw new IOException(L10n.Tr("sort.exists", move.to));
                    var error = AssetDatabase.MoveAsset(move.from, move.to);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    done.Add(move);
                }
                // 空になったショップのフォルダだけを消す（以前の取り込みが残っていれば残す）
                foreach (var shop in shops)
                {
                    if (!Directory.Exists(shop) || Directory.EnumerateFileSystemEntries(shop).Any()) continue;
                    var meta = File.Exists(shop + ".meta") ? File.ReadAllText(shop + ".meta") : null;
                    AssetDatabase.DeleteAsset(shop);
                    removed.Add(new RemovedFolder { path = shop, meta = meta });
                }
                FollowPaths(done, true);
                return null;
            }
            catch (Exception e)
            {
                moves = done;
                try { Backward(); } catch (Exception inner) { Debug.LogWarning("[KaihenNavi] Import sort rollback: " + inner.Message); }
                return L10n.Tr("sort.failed", e.Message);
            }
        }

        /// <summary>元の場所へ戻す。消したショップのフォルダは同じ .meta（同じ GUID）で作り直す。</summary>
        private string Backward()
        {
            var errors = new List<string>();
            foreach (var folder in removed)
            {
                if (Directory.Exists(folder.path)) continue;
                Directory.CreateDirectory(folder.path);
                if (!string.IsNullOrEmpty(folder.meta)) File.WriteAllText(folder.path + ".meta", folder.meta);
                AssetDatabase.ImportAsset(folder.path, ImportAssetOptions.ForceSynchronousImport);
            }
            var back = new List<SortMove>();
            foreach (var move in Enumerable.Reverse(moves))
            {
                if (!AssetDatabase.IsValidFolder(move.to) || Directory.Exists(move.from)) { errors.Add(move.to); continue; }
                var parent = Path.GetDirectoryName(move.from);
                if (!string.IsNullOrEmpty(parent)) WorkFolders.Ensure(parent.Replace('\\', '/'));
                var error = AssetDatabase.MoveAsset(move.to, move.from);
                if (string.IsNullOrEmpty(error)) back.Add(move);
                else errors.Add(error);
            }
            // 移動のために作った用途フォルダやルートは、空のときだけ消す（あとから入れた物は消さない）
            foreach (var folder in Enumerable.Reverse(created))
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(folder);
            FollowPaths(back, false);
            return errors.Count == 0 ? null : L10n.Tr("sort.undoFailed", string.Join(", ", errors));
        }

        /// <summary>台帳と、取り込みの Undo の控え（Library）のパスを移動に合わせる。</summary>
        private static void FollowPaths(List<SortMove> done, bool forward)
        {
            if (done.Count == 0) return;
            Func<string, string> map = p => ImportSorter.RewritePath(p, done, forward);
            ItemLedger.RewritePaths(map);
            PackageImportUndo.RewritePaths(map);
        }
    }
}
