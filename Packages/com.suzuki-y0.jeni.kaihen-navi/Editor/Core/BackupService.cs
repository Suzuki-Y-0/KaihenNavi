using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// Transform やマテリアルだけでは表せない状態（ツールが作ったメニューなど）をバックアップに含めるための仕組み。
    /// </summary>
    internal interface IBackupParticipant
    {
        /// <summary>バックアップ内で区別するための名前。</summary>
        string Key { get; }

        /// <summary>今の状態を JSON にする。保存するものが無ければ null。</summary>
        string Capture(VRCAvatarDescriptor avatar);

        /// <summary>
        /// JSON の状態に戻す。Transform・表示状態・マテリアルはこの後で BackupService が戻すので、ここでは触らない。
        /// </summary>
        void Restore(VRCAvatarDescriptor avatar, string json, string undoName);
    }

    /// <summary>
    /// 自動バックアップと「元に戻す」。
    /// 各 Undo 操作の直前を保存し、同じ機能を続けて使った分を作業のまとまりとして表示する。
    /// 復元も Undo の 1 手にし、履歴のファイルは消さずに表示状態を Undo で切り替える。
    /// </summary>
    internal sealed class BackupService
    {
        public const int MaxBackupsPerAvatar = 20;

        /// <summary>バックアップに参加する機能。</summary>
        private static readonly List<IBackupParticipant> Participants = new List<IBackupParticipant>
        {
            new ToggleMenuBackup(),
            new MenuTidyBackup(),
            new InnerColorBackup(),
#if KAIHENNAVI_MA
            new BodyScaleBackup(),
            new MeshSettingsBackup(),
#endif
        };

        /// <summary>
        /// このウィンドウで Unity の Undo に載せた手順。どのアバターの手順か、載せた直後の Undo の積み方も控え、
        /// 「元に戻す」の時点で上に別の操作（カメラの移動・別のアバターの復元など）が積まれていないか確かめる。
        /// </summary>
        private sealed class LiveStep
        {
            public string AvatarId;
            public string UndoState;
            /// <summary>この手順の Undo グループ番号（自動保存を同じグループにまとめたかの確認用）。</summary>
            public int Group;
        }

        private string _sessionKey;
        private BackupSnapshot _current;
        private BackupSnapshot _pending;
        private BackupSnapshot _restoredTarget;
        private LiveStep _restoredStep;
        private VRCAvatarDescriptor _editingAvatar;
        private long _sessionTicks;
        private int _lastGroup = -1;
        private static long _lastTicks;
        private readonly Dictionary<BackupSnapshot, LiveStep> _liveSteps = new Dictionary<BackupSnapshot, LiveStep>();

        /// <summary>同じ時刻に操作が重なっても、履歴の順番とまとまりを取り違えない。</summary>
        private static long NextTicks()
        {
            _lastTicks = Math.Max(DateTime.Now.Ticks, _lastTicks + 1);
            return _lastTicks;
        }

        /// <summary>バックアップが増えた・減ったときに呼ばれる。</summary>
        public event Action Changed;

        /// <summary>変更の直前に呼ぶ。必要ならバックアップを取る。</summary>
        public void BeginEdit(VRCAvatarDescriptor avatar, string featureId, string mergeKey = null)
        {
            if (avatar == null) return;
            var key = AvatarId(avatar) + "|" + featureId;
            if (_sessionKey != key)
            {
                EndSession();
                _sessionKey = key;
                _sessionTicks = NextTicks();
            }
            // ResetAll のように、同じ操作の中で BeginEdit が重なる場合も 1 手にする。
            if (_pending != null) return;
            if (_current != null && UndoHelper.WillMerge(mergeKey)) return;

            _editingAvatar = avatar;
            _pending = Capture(avatar, featureId);
            _pending.sessionTicks = _sessionTicks;
            _pending.hiddenFromHistory = true;
            // Undo グループ名は操作側がこの後で付けるため、入力処理の終了後に読み取る。
            EditorApplication.delayCall += CompletePending;
        }

        /// <summary>操作が付けた Undo グループ名で、変更前の状態を履歴に載せる。</summary>
        public void CompletePending()
        {
            CompletePending(Undo.GetCurrentGroup());
        }

        /// <summary>内側で別グループを使ってからまとめた操作は、まとめ先を明示する。</summary>
        public void CompletePending(int group)
        {
            EditorApplication.delayCall -= CompletePending;
            if (_pending == null) return;
            var snapshot = _pending;
            _pending = null;
            if (_editingAvatar == null) { ReleaseUnsaved(snapshot); return; }
            if (_current != null && group == _lastGroup)
            {
                foreach (var path in snapshot.createdPaths)
                    if (!_current.createdPaths.Contains(path)) _current.createdPaths.Add(path);
                SaveMetadata(_current);
                ReleaseUnsaved(snapshot);
                MarkLive(_current, _editingAvatar, group);
                return;
            }
            snapshot.operationName = Undo.GetCurrentGroupName();
            snapshot.operationKey = L10n.UndoKey(snapshot.operationName);
            SaveSnapshot(_editingAvatar, snapshot);
            // アバターと履歴の表示状態を同じ Undo グループに入れる。Redo でも履歴が戻る。
            var undoName = string.IsNullOrEmpty(snapshot.operationName) ?
                L10n.Tr("history.operation", L10n.Tr("feature." + snapshot.featureId + ".nav")) : snapshot.operationName;
            Undo.RecordObject(snapshot, undoName);
            snapshot.hiddenFromHistory = false;
            _current = snapshot;
            _lastGroup = group;
            Prune(_editingAvatar);
            Undo.SetCurrentGroupName(undoName);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            SaveMetadata(snapshot);
            MarkLive(snapshot, _editingAvatar, group);
            RaiseChanged();
        }

        /// <summary>
        /// 操作が失敗して Undo.RevertAllDownToGroup などで取り消したときに呼ぶ。
        /// 取りかけのバックアップを履歴に載せずに捨てる（何も変わっていない手順を残さない）。
        /// </summary>
        public void CancelPending()
        {
            EditorApplication.delayCall -= CompletePending;
            if (_pending != null) ReleaseUnsaved(_pending);
            _pending = null;
        }

        /// <summary>今の操作のまとまりを終える（別の機能やアバターに切り替えたとき）。</summary>
        public void EndSession()
        {
            CompletePending();
            _sessionKey = null;
            _current = null;
            _editingAvatar = null;
            _lastGroup = -1;
        }

        /// <summary>Undo 後は、戻した状態から新しいまとまりとして操作を始める。</summary>
        public void OnUndoRedo()
        {
            // Undo の通知中に記録を追加しない。
            EditorApplication.delayCall -= CompletePending;
            if (_pending != null) ReleaseUnsaved(_pending);
            _pending = null;
            _sessionKey = null;
            _current = null;
            _editingAvatar = null;
            _lastGroup = -1;
            // Undo で戻った履歴の表示状態や照合用 ID を、Undo の通知が終わってからファイルにも書く。
            EditorApplication.delayCall -= SaveDirtySnapshots;
            EditorApplication.delayCall += SaveDirtySnapshots;
            RaiseChanged();
        }

        /// <summary>ツールがオブジェクトを作ったら呼ぶ。元に戻すときに削除される。</summary>
        public void NoteCreated(VRCAvatarDescriptor avatar, GameObject created)
        {
            var snapshot = _pending ?? _current;
            if (snapshot == null || avatar == null || created == null) return;
            var path = HierarchyPath.Get(avatar.transform, created.transform);
            if (path == null || snapshot.createdPaths.Contains(path)) return;
            // 作った物の記録も Undo で戻す（取り消した作成物を「作った物」として残さない）
            if (snapshot != _pending) Undo.RecordObject(snapshot, Undo.GetCurrentGroupName());
            snapshot.createdPaths.Add(path);
            if (snapshot != _pending) SaveMetadata(snapshot);
        }

        /// <summary>このアバターのバックアップ（新しい順）。</summary>
        public List<BackupSnapshot> List(VRCAvatarDescriptor avatar, bool includeHidden = false)
        {
            var result = new List<BackupSnapshot>();
            if (avatar == null || !AssetDatabase.IsValidFolder(WorkFolders.Root)) return result;

            var id = AvatarId(avatar);
            foreach (var snapshot in AllSnapshots())
            {
                if ((includeHidden || !snapshot.hiddenFromHistory) && SameAvatar(snapshot, id, avatar)) result.Add(snapshot);
            }

            result.Sort((a, b) => b.createdTicks.CompareTo(a.createdTicks));
            return result;
        }

        // ---------------------------------------------------------------- 読み込みの控え

        private static List<BackupSnapshot> _all;
        private static HashSet<string> _allPaths;

        /// <summary>
        /// すべてのバックアップ（読み込みは 1 回だけ）。フッターの更新のたびに全アバター分を探し直さない。
        /// 表示状態や照合用 ID はアセットの中身を見るので、作成・削除・移動のときだけ読み直せばよい。
        /// </summary>
        private static List<BackupSnapshot> AllSnapshots()
        {
            if (_all == null)
            {
                _all = new List<BackupSnapshot>();
                _allPaths = new HashSet<string>();
                if (AssetDatabase.IsValidFolder(WorkFolders.Root))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:BackupSnapshot", new[] { WorkFolders.Root }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var snapshot = AssetDatabase.LoadAssetAtPath<BackupSnapshot>(path);
                        if (snapshot == null) continue;
                        _all.Add(snapshot);
                        _allPaths.Add(path);
                    }
                }
            }

            // 消されたアセットは Unity の null になる
            _all.RemoveAll(s => s == null);
            return _all;
        }

        /// <summary>次の一覧でファイルから読み直す。</summary>
        internal static void InvalidateCache()
        {
            _all = null;
            _allPaths = null;
        }

        /// <summary>バックアップのファイルやフォルダが増えた・消えた・動いたら読み直す（BackupAssetWatcher から呼ぶ）。</summary>
        internal static void OnAssetsChanged(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_all == null) return;
            if (AffectsBackups(deleted, false) || AffectsBackups(moved, false) || AffectsBackups(movedFrom, false) ||
                AffectsBackups(imported, true)) InvalidateCache();
        }

        private static bool AffectsBackups(string[] paths, bool imported)
        {
            if (paths == null) return false;
            foreach (var raw in paths)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var path = raw.Replace('\\', '/');
                if (path != WorkFolders.Root && !path.StartsWith(WorkFolders.Root + "/")) continue;
                // フォルダごとの移動・削除や、バックアップのファイル。マテリアルなどの保存では読み直さない。
                var isFolder = string.IsNullOrEmpty(System.IO.Path.GetExtension(path));
                if (!isFolder && !path.Contains("/Backups/")) continue;
                // 読み込み済みのファイルの再インポート（表示状態の保存など）は、同じオブジェクトのまま
                if (imported && !isFolder && _allPaths != null && _allPaths.Contains(path)) continue;
                return true;
            }
            return false;
        }

        private static void SaveDirtySnapshots()
        {
            EditorApplication.delayCall -= SaveDirtySnapshots;
            if (_all == null) return;
            foreach (var snapshot in _all)
                if (snapshot != null) AssetDatabase.SaveAssetIfDirty(snapshot);
        }

        // ---------------------------------------------------------------- 古いバックアップの片付け

        private static long _sessionStartTicks;

        /// <summary>このエディタを起動した時刻。これより前のファイルは、今の Undo から参照されていない。</summary>
        internal static DateTime SessionStart
        {
            get
            {
                if (_sessionStartTicks == 0)
                    _sessionStartTicks = DateTime.Now.AddSeconds(-EditorApplication.timeSinceStartup).Ticks;
                return new DateTime(_sessionStartTicks, DateTimeKind.Local);
            }
        }

        /// <summary>
        /// 履歴に出ないバックアップ（復元で隠した・上限を超えた）のファイルを消す。Prune は Undo のために隠すだけなので、ここで消す。
        /// Undo が参照し得るのは、このエディタの起動後に書いたファイルだけ。起動前から変わっていないファイルに限って消す。
        /// </summary>
        /// <param name="sessionStart">エディタを起動した時刻。</param>
        /// <param name="lastWrite">アセットのパスから、ファイルの最終更新時刻を返す。</param>
        /// <returns>消した数。</returns>
        internal static int DeleteStale(DateTime sessionStart, Func<string, DateTime> lastWrite)
        {
            var stale = new List<BackupSnapshot>();
            foreach (var group in AllSnapshots().GroupBy(s => s.avatarId ?? ""))
            {
                var sorted = group.OrderByDescending(s => s.createdTicks).ToList();
                var oldSessions = new HashSet<long>(sorted.Where(s => !s.hiddenFromHistory).Select(s => s.SessionTicks)
                    .Distinct().Skip(MaxBackupsPerAvatar));
                stale.AddRange(sorted.Where(s => s.hiddenFromHistory || oldSessions.Contains(s.SessionTicks) ||
                                                 FromEarlierUnsavedScene(s, sessionStart)));
            }

            var paths = new List<string>();
            foreach (var snapshot in stale)
            {
                var path = AssetDatabase.GetAssetPath(snapshot);
                if (string.IsNullOrEmpty(path) || EditorUtility.IsDirty(snapshot)) continue;
                if (lastWrite(path) >= sessionStart) continue;
                paths.Add(path);
            }
            if (paths.Count == 0) return 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths) AssetDatabase.DeleteAsset(path);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                InvalidateCache();
            }
            return paths.Count;
        }

        /// <summary>
        /// 前の起動のうちに保存前のシーンで取ったバックアップ（シーンを保存しなかった・古い版で取った）。
        /// 名前で照合できるのはこの起動中に取ったものだけなので、もうどのアバターの履歴にも出ず、消してよい。
        /// </summary>
        private static bool FromEarlierUnsavedScene(BackupSnapshot snapshot, DateTime sessionStart)
        {
            return snapshot.avatarId != null && snapshot.avatarId.Contains(UnsavedSceneMarker) &&
                   snapshot.createdTicks < sessionStart.Ticks;
        }

        // ---------------------------------------------------------------- 保存前のシーン

        /// <summary>
        /// 保存前のシーンで取ったバックアップは、シーンを保存すると照合用 ID が変わる。
        /// 保存したシーンのアバターの ID に付け替え、再起動後も同じアバターの履歴として出す。
        /// </summary>
        internal static void OnSceneSaved(Scene scene)
        {
            if (!scene.IsValid() || !AssetDatabase.IsValidFolder(WorkFolders.Root)) return;
            var roots = scene.GetRootGameObjects();
            if (roots == null) return;
            var changed = new List<BackupSnapshot>();
            foreach (var root in roots)
            {
                foreach (var avatar in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                {
                    var id = AvatarId(avatar);
                    if (id.Contains(UnsavedSceneMarker)) continue;
                    foreach (var snapshot in AllSnapshots())
                    {
                        if (snapshot.avatarId == null || !snapshot.avatarId.Contains(UnsavedSceneMarker) ||
                            !SameUnsavedScene(snapshot, avatar)) continue;
                        snapshot.avatarId = id;
                        EditorUtility.SetDirty(snapshot);
                        changed.Add(snapshot);
                    }
                }
            }
            foreach (var snapshot in changed) AssetDatabase.SaveAssetIfDirty(snapshot);
        }

        /// <summary>
        /// アバターの照合用 ID が変わったとき（自分用アバターにつなぎ替えたときなど）、
        /// 前の ID で取ったバックアップを今のアバターのものとして付け替える。
        /// 付け替えも今の Undo グループに入れる。つなぎ替えを Ctrl+Z すると ID も元に戻り、履歴が離れない。
        /// 隠れたバックアップ（復元で隠したものなど）も、Undo で再び出たときのために一緒に付け替える。
        /// </summary>
        public void Rekey(VRCAvatarDescriptor avatar, List<BackupSnapshot> snapshots)
        {
            if (avatar == null || snapshots == null) return;
            var id = AvatarId(avatar);
            var undoName = Undo.GetCurrentGroupName();
            foreach (var s in snapshots)
            {
                if (s == null) continue;
                Undo.RecordObject(s, undoName);
                s.avatarId = id;
                s.avatarName = avatar.name;
                EditorUtility.SetDirty(s);
            }

            Undo.FlushUndoRecordObjects();
            AssetDatabase.SaveAssets();
            EndSession();
            RaiseChanged();
        }

        /// <summary>Undo・Redo で変わった履歴の表示状態も、ドメインリロード前に保存する。</summary>
        public void SaveHistory(VRCAvatarDescriptor avatar)
        {
            foreach (var snapshot in List(avatar, true)) AssetDatabase.SaveAssetIfDirty(snapshot);
        }

        public int Count(VRCAvatarDescriptor avatar)
        {
            return List(avatar).Select(s => s.SessionTicks).Distinct().Count();
        }

        /// <summary>
        /// Unity の Undo の 1 手目が、このアバターの直前の手順（またはこのアバターの復元）か。
        /// 上に別の操作が積まれている・別のアバターの手順・再起動後などは false にし、バックアップで戻す。
        /// </summary>
        public bool HasLiveUndo(VRCAvatarDescriptor avatar)
        {
            return TopStep(avatar) != null;
        }

        private static bool IsTop(LiveStep step, string avatarId, string state)
        {
            return step != null && step.AvatarId == avatarId && step.UndoState == state;
        }

        /// <summary>HasLiveUndo が true のとき、その手順（Unity の Undo の 1 手目）。違えば null。</summary>
        private LiveStep TopStep(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            var state = UndoState();
            if (state == null) return null;
            var id = AvatarId(avatar);
            if (_restoredTarget != null && _restoredTarget.hiddenFromHistory && IsTop(_restoredStep, id, state)) return _restoredStep;
            var latest = List(avatar).FirstOrDefault();
            LiveStep step;
            return latest != null && _liveSteps.TryGetValue(latest, out step) && IsTop(step, id, state) ? step : null;
        }

        /// <summary>
        /// 自動保存の直前に呼ぶ。このアバターの直前の手順が Unity の Undo の 1 手目なら、その Undo グループ番号を返す（違えば -1）。
        /// </summary>
        public int LiveGroup(VRCAvatarDescriptor avatar)
        {
            var step = TopStep(avatar);
            return step == null ? -1 : step.Group;
        }

        /// <summary>
        /// 自動保存（Prefab への書き込み）を LiveGroup のグループにまとめた後に呼ぶ。保存で Undo の積み方は変わるが、
        /// Ctrl+Z 1 回で編集と保存が一緒に戻るので、フッターの「元に戻す」も引き続き Unity の Undo を使えるよう控え直す。
        /// stateBefore（保存前の積み方）が控えと同じ手順だけを更新する。
        /// </summary>
        public void KeepLive(VRCAvatarDescriptor avatar, int group, string stateBefore)
        {
            var state = UndoState();
            if (avatar == null || group < 0 || state == null || stateBefore == null) return;
            var id = AvatarId(avatar);
            var steps = new List<LiveStep>(_liveSteps.Values);
            if (_restoredStep != null) steps.Add(_restoredStep);
            foreach (var step in steps)
                if (step.Group == group && IsTop(step, id, stateBefore)) step.UndoState = state;
        }

        /// <summary>Undo に載せた直後の積み方を控える（取れない環境では控えず、バックアップで戻す）。</summary>
        private LiveStep Live(VRCAvatarDescriptor avatar, int group)
        {
            var state = UndoState();
            return avatar == null || state == null ? null : new LiveStep { AvatarId = AvatarId(avatar), UndoState = state, Group = group };
        }

        private void MarkLive(BackupSnapshot snapshot, VRCAvatarDescriptor avatar, int group)
        {
            var step = Live(avatar, group);
            if (step == null) _liveSteps.Remove(snapshot);
            else _liveSteps[snapshot] = step;
        }

        private static bool _undoRecordsSearched;
        private static MethodInfo _undoRecords;

        /// <summary>
        /// 今の Undo の積み方（件数と名前）の要約。Unity の公開 API には Undo の中身を調べる方法が無く、
        /// グループ番号はクリックだけでも進むため、内部の Undo.GetRecords を使う。見つからなければ null。
        /// </summary>
        internal static string UndoState()
        {
            if (!_undoRecordsSearched)
            {
                _undoRecordsSearched = true;
                _undoRecords = typeof(Undo).GetMethod("GetRecords", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(List<string>), typeof(List<string>) }, null);
            }
            if (_undoRecords == null) return null;
            try
            {
                var undo = new List<string>();
                _undoRecords.Invoke(null, new object[] { undo, new List<string>() });
                if (undo.Count == 0) return null;
                var hash = 14695981039346656037UL;
                unchecked
                {
                    foreach (var name in undo)
                    {
                        foreach (var c in name ?? "") hash = (hash ^ c) * 1099511628211UL;
                        hash = (hash ^ 0xFFFFUL) * 1099511628211UL;
                    }
                }
                return undo.Count + ":" + hash.ToString("x16");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>いちばん新しいバックアップに戻す。</summary>
        public bool RestoreLatest(VRCAvatarDescriptor avatar)
        {
            var list = List(avatar);
            if (list.Count == 0) return false;
            Restore(avatar, list[0]);
            return true;
        }

        /// <summary>
        /// 指定した操作の直前に戻す。その操作と新しい操作は履歴から隠し、Ctrl+Z で表示も元に戻す。
        /// Unity の Undo（Ctrl+Z）でも、元に戻す前の状態に戻れる。
        /// </summary>
        public void Restore(VRCAvatarDescriptor avatar, BackupSnapshot target)
        {
            if (avatar == null || target == null) return;
            EndSession();

            // 新しい順に適用すると、間の操作で作られたオブジェクトもすべて消せる
            var newer = List(avatar).Where(s => s.createdTicks >= target.createdTicks).ToList();

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(L10n.Tr("undo.restore"));

            foreach (var snapshot in newer) DeleteCreated(avatar, snapshot);
            RestoreExtras(avatar, target);
            Apply(avatar, target);

            foreach (var snapshot in newer)
            {
                Undo.RecordObject(snapshot, L10n.Tr("undo.restore"));
                snapshot.hiddenFromHistory = true;
                EditorUtility.SetDirty(snapshot);
            }
            // 戻した状態を改変アバターの Prefab にも書き込む（FeatureContext.CompleteEdit と同じ形）。
            // 同じグループに入れるので、Ctrl+Z で Prefab も復元前に戻る。
            Undo.FlushUndoRecordObjects();
            try
            {
                MyAvatar.Save(avatar);
            }
            finally
            {
                Undo.SetCurrentGroupName(L10n.Tr("undo.restore"));
                Undo.CollapseUndoOperations(group);
            }
            UndoHelper.Separate();

            EndSession();
            _restoredTarget = target;
            _restoredStep = Live(avatar, group);
            AssetDatabase.SaveAssets();
            RaiseChanged();
        }

        public static string AvatarId(VRCAvatarDescriptor avatar)
        {
            return GlobalObjectId.GetGlobalObjectIdSlow(avatar.gameObject).ToString();
        }

        private const string UnsavedSceneMarker = "-00000000000000000000000000000000-";

        /// <summary>
        /// 同じアバターのバックアップか。保存前のシーンでは識別子が保存後に変わってしまうため、
        /// どちらかが保存前のシーンで取ったものなら名前で照合する。
        /// 名前だけでは、別のシーンや前回の起動の同名アバターと取り違えるので、この起動中に同じシーンで取ったものに限る。
        /// </summary>
        private static bool SameAvatar(BackupSnapshot snapshot, string id, VRCAvatarDescriptor avatar)
        {
            if (snapshot.avatarId == id) return true;
            var unsaved = (snapshot.avatarId != null && snapshot.avatarId.Contains(UnsavedSceneMarker)) ||
                          id.Contains(UnsavedSceneMarker);
            return unsaved && SameUnsavedScene(snapshot, avatar);
        }

        /// <summary>シーンのハンドルは起動中だけ一意なので、起動後に取ったものかも確かめる（古いバックアップは 0）。</summary>
        private static bool SameUnsavedScene(BackupSnapshot snapshot, VRCAvatarDescriptor avatar)
        {
            return snapshot.avatarName == avatar.name && snapshot.sceneHandle != 0 &&
                   snapshot.sceneHandle == avatar.gameObject.scene.handle && snapshot.createdTicks >= SessionStart.Ticks;
        }

        // ---------------------------------------------------------------- 保存

        private static BackupSnapshot Capture(VRCAvatarDescriptor avatar, string featureId)
        {
            var root = avatar.transform;
            var snapshot = ScriptableObject.CreateInstance<BackupSnapshot>();
            snapshot.avatarId = AvatarId(avatar);
            snapshot.avatarName = avatar.name;
            snapshot.sceneHandle = avatar.gameObject.scene.handle;
            snapshot.createdTicks = NextTicks();
            snapshot.featureId = featureId;
            snapshot.viewPosition = avatar.ViewPosition;

            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                snapshot.transforms.Add(new BackupSnapshot.TransformState
                {
                    path = HierarchyPath.Get(root, t),
                    active = t.gameObject.activeSelf,
                    localPosition = t.localPosition,
                    localRotation = t.localRotation,
                    localScale = t.localScale,
                });
            }

            var ownedMaterials = new HashSet<Material>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                snapshot.renderers.Add(new BackupSnapshot.RendererState
                {
                    path = HierarchyPath.Get(root, r.transform),
                    materials = mats,
                    rendererIndex = Array.IndexOf(r.GetComponents<Renderer>(), r),
                    hasMesh = r is SkinnedMeshRenderer,
                    mesh = r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : null,
                });
                foreach (var m in mats)
                {
                    // ほかのアバターのフォルダのマテリアルは保存しない（復元でそのアバターの変更を上書きしないため）。
                    if (m != null && WorkFolders.IsOwnedBy(m, avatar.gameObject)) ownedMaterials.Add(m);
                }

                var smr = r as SkinnedMeshRenderer;
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    var weights = new float[smr.sharedMesh.blendShapeCount];
                    for (var i = 0; i < weights.Length; i++) weights[i] = smr.GetBlendShapeWeight(i);
                    snapshot.blendShapes.Add(new BackupSnapshot.BlendShapeState
                    {
                        path = HierarchyPath.Get(root, r.transform),
                        weights = weights,
                    });
                }
            }

            foreach (var participant in Participants)
            {
                string json = null;
                try
                {
                    json = participant.Capture(avatar);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                if (json != null) snapshot.extras.Add(new BackupSnapshot.ExtraState { key = participant.Key, json = json });
            }

            foreach (var live in ownedMaterials)
            {
                var saved = Object.Instantiate(live);
                saved.name = live.name;
                saved.hideFlags = HideFlags.HideInHierarchy;
                snapshot.materials.Add(new BackupSnapshot.MaterialState { live = live, saved = saved });
            }
            return snapshot;
        }

        private static void SaveSnapshot(VRCAvatarDescriptor avatar, BackupSnapshot snapshot)
        {
            var folder = WorkFolders.Ensure(WorkFolders.Backups(avatar.gameObject));
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Backup_" +
                snapshot.CreatedAt.ToString("yyyyMMdd_HHmmss_fffffff") + ".asset");
            AssetDatabase.CreateAsset(snapshot, path);
            foreach (var material in snapshot.materials) AssetDatabase.AddObjectToAsset(material.saved, snapshot);
            SaveMetadata(snapshot);
            if (_all != null && !_all.Contains(snapshot))
            {
                _all.Add(snapshot);
                _allPaths.Add(AssetDatabase.GetAssetPath(snapshot) ?? path);
            }
        }

        private static void SaveMetadata(BackupSnapshot snapshot)
        {
            EditorUtility.SetDirty(snapshot);
            AssetDatabase.SaveAssetIfDirty(snapshot);
        }

        private static void ReleaseUnsaved(BackupSnapshot snapshot)
        {
            foreach (var material in snapshot.materials)
                if (material.saved != null) Object.DestroyImmediate(material.saved);
            Object.DestroyImmediate(snapshot);
        }

        private void Prune(VRCAvatarDescriptor avatar)
        {
            var list = List(avatar);
            var oldSessions = new HashSet<long>(list.Select(s => s.SessionTicks).Distinct().Skip(MaxBackupsPerAvatar));
            foreach (var snapshot in list.Where(s => oldSessions.Contains(s.SessionTicks)))
            {
                // Undo が参照するアセットを削除しない。新しい操作を Undo すれば表示件数も戻る。
                Undo.RecordObject(snapshot, L10n.Tr("history.record"));
                snapshot.hiddenFromHistory = true;
                SaveMetadata(snapshot);
            }
        }

        // ---------------------------------------------------------------- 復元

        private static void DeleteCreated(VRCAvatarDescriptor avatar, BackupSnapshot snapshot)
        {
            // 深い階層から消す（親を先に消すと子のパスが引けなくなるため）
            var paths = snapshot.createdPaths.OrderByDescending(p => p.Split('/').Length).ToList();
            foreach (var path in paths)
            {
                var t = HierarchyPath.Find(avatar.transform, path);
                if (t != null && t != avatar.transform) Undo.DestroyObjectImmediate(t.gameObject);
            }
        }

        private static void RestoreExtras(VRCAvatarDescriptor avatar, BackupSnapshot snapshot)
        {
            var undoName = L10n.Tr("undo.restore");
            foreach (var participant in Participants)
            {
                var state = snapshot.extras.FirstOrDefault(e => e.key == participant.Key);
                try
                {
                    participant.Restore(avatar, state == null ? null : state.json, undoName);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void Apply(VRCAvatarDescriptor avatar, BackupSnapshot snapshot)
        {
            var root = avatar.transform;
            var undoName = L10n.Tr("undo.restore");

            foreach (var state in snapshot.transforms)
            {
                var t = HierarchyPath.Find(root, state.path);
                if (t == null) continue;
                if (t.localPosition != state.localPosition || t.localRotation != state.localRotation ||
                    t.localScale != state.localScale)
                {
                    Undo.RecordObject(t, undoName);
                    t.localPosition = state.localPosition;
                    t.localRotation = state.localRotation;
                    t.localScale = state.localScale;
                }

                if (t.gameObject.activeSelf != state.active)
                {
                    Undo.RecordObject(t.gameObject, undoName);
                    t.gameObject.SetActive(state.active);
                }
            }

            foreach (var state in snapshot.renderers)
            {
                var t = HierarchyPath.Find(root, state.path);
                var r = t == null ? null : t.GetComponents<Renderer>().ElementAtOrDefault(state.rendererIndex);
                if (r == null || state.materials == null) continue;
                var skinned = r as SkinnedMeshRenderer;
                var meshChanged = state.hasMesh && skinned != null && skinned.sharedMesh != state.mesh;
                if (!meshChanged && r.sharedMaterials.SequenceEqual(state.materials)) continue;
                Undo.RecordObject(r, undoName);
                r.sharedMaterials = state.materials;
                if (state.hasMesh && skinned != null) skinned.sharedMesh = state.mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }

            InnerColorBackup.RestoreMissingMeshes(avatar, snapshot, undoName);

            foreach (var state in snapshot.blendShapes)
            {
                var t = HierarchyPath.Find(root, state.path);
                var smr = t == null ? null : t.GetComponent<SkinnedMeshRenderer>();
                if (smr == null || smr.sharedMesh == null || state.weights == null) continue;
                var count = Mathf.Min(smr.sharedMesh.blendShapeCount, state.weights.Length);
                var recorded = false;
                for (var i = 0; i < count; i++)
                {
                    if (Mathf.Approximately(smr.GetBlendShapeWeight(i), state.weights[i])) continue;
                    if (!recorded)
                    {
                        Undo.RecordObject(smr, undoName);
                        recorded = true;
                    }

                    smr.SetBlendShapeWeight(i, state.weights[i]);
                }
            }

            foreach (var state in snapshot.materials)
            {
                if (state.live == null || state.saved == null) continue;
                // 古いバックアップには、ほかのアバターの複製マテリアルが入っていることがある。このアバターの物だけ戻す。
                if (!WorkFolders.IsOwnedBy(state.live, avatar.gameObject)) continue;
                Undo.RecordObject(state.live, undoName);
                var name = state.live.name;
                EditorUtility.CopySerialized(state.saved, state.live);
                state.live.name = name;
                EditorUtility.SetDirty(state.live);
            }

            if (avatar.ViewPosition != snapshot.viewPosition)
            {
                Undo.RecordObject(avatar, undoName);
                avatar.ViewPosition = snapshot.viewPosition;
            }
        }

        private void RaiseChanged()
        {
            if (Changed != null) Changed();
        }
    }

    /// <summary>バックアップのファイルが増えた・消えたことを BackupService の読み込みの控えに伝える。</summary>
    internal sealed class BackupAssetWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            BackupService.OnAssetsChanged(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
        }
    }

    /// <summary>
    /// エディタの起動時に、履歴に出ない古いバックアップを片付ける（ドメインリロードのたびではなく、起動ごとに 1 回）。
    /// シーンの保存時には、保存前のシーンで取ったバックアップの ID を付け替える。
    /// </summary>
    [InitializeOnLoad]
    internal static class BackupMaintenance
    {
        private const string CleanedKey = "KaihenNavi.BackupsCleaned";

        static BackupMaintenance()
        {
            EditorSceneManager.sceneSaved += BackupService.OnSceneSaved;
            if (SessionState.GetString(CleanedKey, "") == "1") return;
            EditorApplication.delayCall += Clean;
        }

        private static void Clean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetString(CleanedKey, "1");
            try
            {
                BackupService.DeleteStale(BackupService.SessionStart, path => System.IO.File.GetLastWriteTime(path));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // 消した履歴だけが指していた、色を変えた画像も片付ける（起動直後なので Undo から参照されていない）
            try
            {
                ColorLayers.SweepUnusedBakes(BackupService.SessionStart, path => System.IO.File.GetLastWriteTime(path));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
