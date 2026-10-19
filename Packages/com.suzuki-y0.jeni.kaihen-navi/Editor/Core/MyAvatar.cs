using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>シーンのアバターが、自分用アバター（ツールが作った Prefab）につながっているか。</summary>
    internal enum AvatarKind
    {
        /// <summary>Assets/KaihenNavi 以下の Prefab（自分用アバター）につながっている。改変は自動でそこへ保存される。</summary>
        Mine,

        /// <summary>買ったときの Prefab につながっている。改変はシーンにしか残らない。</summary>
        Original,

        /// <summary>Prefab とのつながりが無い（Unpack 済み・最初から Prefab でない）。</summary>
        Unlinked,
    }

    /// <summary>
    /// 自分用アバター。中身は Unity 標準のプレハブバリアントだが、画面では「自分用アバター」と呼ぶ。
    /// 1 体につき Assets/KaihenNavi/名前/ のフォルダを 1 つ使い、Prefab・複製マテリアル・バックアップをまとめて置く。
    /// 元の Prefab には一切書き込まない（保存先は必ず Assets/KaihenNavi 以下の Prefab）。
    /// </summary>
    internal static class MyAvatar
    {
        public static AvatarKind KindOf(VRCAvatarDescriptor avatar)
        {
            var path = PrefabPath(avatar);
            if (path == null) return AvatarKind.Unlinked;
            return IsMinePath(path) ? AvatarKind.Mine : AvatarKind.Original;
        }

        /// <summary>シーンのアバターがつながっている Prefab のパス。つながっていなければ null。</summary>
        public static string PrefabPath(VRCAvatarDescriptor avatar)
        {
            var root = InstanceRoot(avatar);
            if (root == null) return null;
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        public static bool IsMinePath(string path)
        {
            return path != null && path.Replace('\\', '/').StartsWith(WorkFolders.Root + "/") && path.EndsWith(".prefab");
        }

        private static GameObject InstanceRoot(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            if (!PrefabUtility.IsPartOfPrefabInstance(avatar.gameObject)) return null;
            return PrefabUtility.GetOutermostPrefabInstanceRoot(avatar.gameObject);
        }

        // ---------------------------------------------------------------- 作る

        /// <summary>
        /// 今のアバターを自分用アバターとして保存し、シーンのアバターをそれにつなぎ替える。
        /// 買ったときの Prefab につながっていればプレハブバリアント（元の更新も受け取れる）、
        /// つながりが切れていれば普通の Prefab になる。今までの改変はそのまま引き継ぐ。
        /// </summary>
        public static string Create(VRCAvatarDescriptor avatar, BackupService backup)
        {
            if (avatar == null || KindOf(avatar) == AvatarKind.Mine) return null;

            var target = InstanceRoot(avatar);
            if (target == null) target = avatar.gameObject;

            // つなぎ替えるとバックアップの照合用 ID が変わるので、先に控えておく
            var backups = backup.List(avatar);

            var folder = WorkFolders.Ensure(FolderFor(avatar.gameObject));
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + WorkFolders.Sanitize(avatar.name) + ".prefab");
            bool ok;
            PrefabUtility.SaveAsPrefabAssetAndConnect(target, path, InteractionMode.UserAction, out ok);
            if (!ok) return null;

            backup.Rekey(avatar, backups);
            return path;
        }

        /// <summary>
        /// 自分用アバターを置くフォルダ。すでにツールのフォルダ（複製マテリアルなど）があればそれを使う。
        /// ただし別の自分用アバターが入っていれば、新しいフォルダにする。
        /// </summary>
        private static string FolderFor(GameObject avatar)
        {
            var folder = WorkFolders.Root + "/" + WorkFolders.Sanitize(avatar.name);
            if (!AssetDatabase.IsValidFolder(folder)) return folder;
            if (AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Length == 0) return folder;
            return AssetDatabase.GenerateUniqueAssetPath(folder);
        }

        // ---------------------------------------------------------------- 自動保存

        /// <summary>シーン上の改変のうち、まだ自分用アバターに保存していないものがあるか。</summary>
        public static bool HasUnsaved(VRCAvatarDescriptor avatar)
        {
            if (KindOf(avatar) != AvatarKind.Mine) return false;
            var root = InstanceRoot(avatar);
            return root != null && PrefabUtility.HasPrefabInstanceAnyOverrides(root, false);
        }

        /// <summary>
        /// シーン上の改変を自分用アバターに書き込む。書き込み先が自分用アバターでなければ何もしない
        /// （買ったときの Prefab を書き換えないための確認）。
        /// </summary>
        public static bool Save(VRCAvatarDescriptor avatar)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            var root = InstanceRoot(avatar);
            if (root == null) return false;

            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (!IsMinePath(path)) return false;
            if (!PrefabUtility.HasPrefabInstanceAnyOverrides(root, false)) return false;

            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            return true;
        }

        /// <summary>このアバター用に複製したマテリアルの変更をファイルに書き出す（Ctrl+S を押し忘れても残るように）。</summary>
        public static void SaveMaterials(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return;
            var done = new HashSet<Material>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !done.Add(m) || !WorkFolders.IsOwnedBy(m, avatar.gameObject)) continue;
                    AssetDatabase.SaveAssetIfDirty(m);
                }
            }
        }

        // ---------------------------------------------------------------- 一覧（製品版）

        /// <summary>自分用アバターの Prefab（Assets/KaihenNavi 以下の、Avatar Descriptor が付いた Prefab）。</summary>
        public static List<GameObject> All()
        {
            var result = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(WorkFolders.Root)) return result;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { WorkFolders.Root }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<VRCAvatarDescriptor>() != null) result.Add(prefab);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        /// <summary>元になった買ったときの Prefab（自分用アバターをさらに複製したものは、たどれるところまでたどる）。</summary>
        public static GameObject OriginalOf(GameObject prefab)
        {
            var current = prefab;
            for (var i = 0; i < 16 && current != null; i++)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) return current == prefab ? null : current;
                if (!IsMinePath(AssetDatabase.GetAssetPath(source))) return source;
                current = source;
            }

            return null;
        }

        /// <summary>
        /// 別バージョン（衣装違いなど）を作る。ファイルごと複製するので、元のバージョンとは別々に改変できる。
        /// マテリアルは最初に色を変えたときに、新しいバージョンのフォルダへ複製される。
        /// </summary>
        public static string Duplicate(GameObject prefab, string name)
        {
            var source = AssetDatabase.GetAssetPath(prefab);
            if (!IsMinePath(source)) return null;

            name = WorkFolders.Sanitize(name);
            var folder = AssetDatabase.GenerateUniqueAssetPath(WorkFolders.Root + "/" + name);
            WorkFolders.Ensure(folder);
            var path = folder + "/" + Path.GetFileName(folder) + ".prefab";
            return AssetDatabase.CopyAsset(source, path) ? path : null;
        }

        /// <summary>名前を変える（Prefab と、それだけが入ったフォルダの両方）。失敗したら理由を返す。</summary>
        public static string Rename(GameObject prefab, string name)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            if (!IsMinePath(path)) return "not-mine";
            name = WorkFolders.Sanitize(name);
            if (name == prefab.name) return null;

            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            var error = AssetDatabase.RenameAsset(path, name);
            if (!string.IsNullOrEmpty(error)) return error;

            // フォルダがこの Prefab 専用（フォルダ名が元の名前と同じ）なら、フォルダ名もそろえる
            if (Path.GetFileName(folder) == Path.GetFileNameWithoutExtension(path) && folder != WorkFolders.Root)
            {
                var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.RenameAsset(folder, name);
            }

            return null;
        }

        /// <summary>シーンに出す。アップロードで迷わないよう、ほかのアバターは隠す（Ctrl+Z で戻せる）。</summary>
        public static VRCAvatarDescriptor PlaceInScene(GameObject prefab, string undoName)
        {
            foreach (var other in AvatarFinder.FindInOpenScenes())
            {
                if (!other.gameObject.activeSelf) continue;
                Undo.RecordObject(other.gameObject, undoName);
                other.gameObject.SetActive(false);
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) return null;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            Selection.activeGameObject = instance;
            return instance.GetComponent<VRCAvatarDescriptor>();
        }

        /// <summary>このアバターがシーンに何体出ているか。</summary>
        public static int CountInScene(GameObject prefab)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            var count = 0;
            foreach (var a in AvatarFinder.FindInOpenScenes())
            {
                if (PrefabPath(a) == path) count++;
            }

            return count;
        }
    }
}
