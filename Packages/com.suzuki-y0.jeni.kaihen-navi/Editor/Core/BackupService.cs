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
    /// 「やりたいこと」を選んで最初に何かを変える直前に、アバター全体の状態を 1 件保存する。
    /// 同じ「やりたいこと」の中で続けて操作している間は追加で保存しない（1 操作のまとまり＝1 件）。
    /// </summary>
    internal sealed class BackupService
    {
        public const int MaxBackupsPerAvatar = 20;

        /// <summary>バックアップに参加する機能。</summary>
        private static readonly List<IBackupParticipant> Participants = new List<IBackupParticipant>
        {
            new ToggleMenuBackup(),
        };

        private string _sessionKey;
        private BackupSnapshot _current;

        /// <summary>バックアップが増えた・減ったときに呼ばれる。</summary>
        public event Action Changed;

        /// <summary>変更の直前に呼ぶ。必要ならバックアップを取る。</summary>
        public void BeginEdit(VRCAvatarDescriptor avatar, string featureId)
        {
            if (avatar == null) return;
            var key = AvatarId(avatar) + "|" + featureId;
            if (_sessionKey == key && _current != null) return;

            _current = Capture(avatar, featureId);
            _sessionKey = key;
            Prune(avatar);
            RaiseChanged();
        }

        /// <summary>今の操作のまとまりを終える（別の「やりたいこと」やアバターに切り替えたとき）。</summary>
        public void EndSession()
        {
            _sessionKey = null;
            _current = null;
        }

        /// <summary>ツールがオブジェクトを作ったら呼ぶ。元に戻すときに削除される。</summary>
        public void NoteCreated(VRCAvatarDescriptor avatar, GameObject created)
        {
            if (_current == null || avatar == null || created == null) return;
            var path = HierarchyPath.Get(avatar.transform, created.transform);
            if (path == null || _current.createdPaths.Contains(path)) return;
            _current.createdPaths.Add(path);
            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssetIfDirty(_current);
        }

        /// <summary>このアバターのバックアップ（新しい順）。</summary>
        public List<BackupSnapshot> List(VRCAvatarDescriptor avatar)
        {
            var result = new List<BackupSnapshot>();
            if (avatar == null || !AssetDatabase.IsValidFolder(WorkFolders.Root)) return result;

            var id = AvatarId(avatar);
            foreach (var guid in AssetDatabase.FindAssets("t:BackupSnapshot", new[] { WorkFolders.Root }))
            {
                var snapshot = AssetDatabase.LoadAssetAtPath<BackupSnapshot>(AssetDatabase.GUIDToAssetPath(guid));
                if (snapshot != null && SameAvatar(snapshot, id, avatar.name)) result.Add(snapshot);
            }

            result.Sort((a, b) => b.createdTicks.CompareTo(a.createdTicks));
            return result;
        }

        public int Count(VRCAvatarDescriptor avatar)
        {
            return List(avatar).Count;
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
        /// 指定したバックアップの状態に戻す。そのバックアップと、それより新しいバックアップは使い終わったので削除する。
        /// Unity の Undo（Ctrl+Z）でも、元に戻す前の状態に戻れる。
        /// </summary>
        public void Restore(VRCAvatarDescriptor avatar, BackupSnapshot target)
        {
            if (avatar == null || target == null) return;

            // 新しい順に適用すると、間の操作で作られたオブジェクトもすべて消せる
            var newer = List(avatar).Where(s => s.createdTicks >= target.createdTicks).ToList();

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(L10n.Tr("undo.restore"));

            foreach (var snapshot in newer) DeleteCreated(avatar, snapshot);
            RestoreExtras(avatar, target);
            Apply(avatar, target);

            Undo.CollapseUndoOperations(group);
            UndoHelper.Separate();

            EndSession();
            foreach (var snapshot in newer) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(snapshot));
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
            snapshot.createdTicks = DateTime.Now.Ticks;
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

            var folder = WorkFolders.Ensure(WorkFolders.Backups(avatar.gameObject));
            var path = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asset");
            AssetDatabase.CreateAsset(snapshot, path);

            foreach (var live in ownedMaterials)
            {
                var saved = Object.Instantiate(live);
                saved.name = live.name;
                saved.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(saved, snapshot);
                snapshot.materials.Add(new BackupSnapshot.MaterialState { live = live, saved = saved });
            }

            EditorUtility.SetDirty(snapshot);
            AssetDatabase.SaveAssetIfDirty(snapshot);
            return snapshot;
        }

        private void Prune(VRCAvatarDescriptor avatar)
        {
            var list = List(avatar);
            for (var i = MaxBackupsPerAvatar; i < list.Count; i++)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(list[i]));
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
                var r = t == null ? null : t.GetComponent<Renderer>();
                if (r == null || state.materials == null) continue;
                if (r.sharedMaterials.SequenceEqual(state.materials)) continue;
                Undo.RecordObject(r, undoName);
                r.sharedMaterials = state.materials;
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
