using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
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
#endif
        };

        private string _sessionKey;
        private BackupSnapshot _current;
        private BackupSnapshot _pending;
        private BackupSnapshot _restoredTarget;
        private VRCAvatarDescriptor _editingAvatar;
        private long _sessionTicks;
        private int _lastGroup = -1;
        private static long _lastTicks;
        private readonly HashSet<BackupSnapshot> _liveSteps = new HashSet<BackupSnapshot>();

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
            _liveSteps.Add(snapshot);
            _lastGroup = group;
            Prune(_editingAvatar);
            Undo.SetCurrentGroupName(undoName);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            SaveMetadata(snapshot);
            RaiseChanged();
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
            foreach (var guid in AssetDatabase.FindAssets("t:BackupSnapshot", new[] { WorkFolders.Root }))
            {
                var snapshot = AssetDatabase.LoadAssetAtPath<BackupSnapshot>(AssetDatabase.GUIDToAssetPath(guid));
                if (snapshot != null && (includeHidden || !snapshot.hiddenFromHistory) && SameAvatar(snapshot, id, avatar.name)) result.Add(snapshot);
            }

            result.Sort((a, b) => b.createdTicks.CompareTo(a.createdTicks));
            return result;
        }

        /// <summary>
        /// アバターの照合用 ID が変わったとき（自分用アバターにつなぎ替えたときなど）、
        /// 前の ID で取ったバックアップを今のアバターのものとして付け替える。
        /// </summary>
        public void Rekey(VRCAvatarDescriptor avatar, List<BackupSnapshot> snapshots)
        {
            if (avatar == null || snapshots == null) return;
            var id = AvatarId(avatar);
            foreach (var s in snapshots)
            {
                if (s == null) continue;
                s.avatarId = id;
                s.avatarName = avatar.name;
                EditorUtility.SetDirty(s);
            }

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

        /// <summary>直前の手順をこのウィンドウで Undo に載せたか。再起動後はバックアップで戻す。</summary>
        public bool HasLiveUndo(VRCAvatarDescriptor avatar)
        {
            var latest = List(avatar).FirstOrDefault();
            return (_restoredTarget != null && _restoredTarget.hiddenFromHistory) ||
                   (latest != null && _liveSteps.Contains(latest));
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
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            UndoHelper.Separate();

            EndSession();
            _restoredTarget = target;
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
        /// </summary>
        private static bool SameAvatar(BackupSnapshot snapshot, string id, string name)
        {
            if (snapshot.avatarId == id) return true;
            var unsaved = (snapshot.avatarId != null && snapshot.avatarId.Contains(UnsavedSceneMarker)) ||
                          id.Contains(UnsavedSceneMarker);
            return unsaved && snapshot.avatarName == name;
        }

        // ---------------------------------------------------------------- 保存

        private static BackupSnapshot Capture(VRCAvatarDescriptor avatar, string featureId)
        {
            var root = avatar.transform;
            var snapshot = ScriptableObject.CreateInstance<BackupSnapshot>();
            snapshot.avatarId = AvatarId(avatar);
            snapshot.avatarName = avatar.name;
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
                    if (m != null && WorkFolders.IsOwned(m)) ownedMaterials.Add(m);
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
}
