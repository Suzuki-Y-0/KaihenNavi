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
            var basePrefab = PrefabUtility.IsPartOfPrefabInstance(target) ? PrefabUtility.GetCorrespondingObjectFromSource(target) : null;

            var folder = FolderFor(avatar.gameObject);
            var path = folder + "/" + System.IO.Path.GetFileName(folder) + ".prefab";
            bool ok;
            PrefabUtility.SaveAsPrefabAssetAndConnect(target, path, InteractionMode.UserAction, out ok);
            if (!ok) return null;

            WorkFolders.WriteManifest(path, basePrefab);
            backup.Rekey(avatar, backups);
            return path;
        }

        /// <summary>
        /// 新しく改変アバターを作る。元のアバター（買ったときの Prefab）からプレハブバリアントを作り、
        /// 決まった形のフォルダに置いて、シーンに出す（ほかのアバターは隠す）。
        /// </summary>
        public static VRCAvatarDescriptor CreateNew(GameObject basePrefab, string name, string undoName)
        {
            if (basePrefab == null || basePrefab.GetComponent<VRCAvatarDescriptor>() == null) return null;

            var folder = WorkFolders.CreateAvatarFolder(name);
            var path = folder + "/" + System.IO.Path.GetFileName(folder) + ".prefab";

            HideOthers(undoName);
            var instance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            if (instance == null) return null;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            instance.name = System.IO.Path.GetFileName(folder);

            bool ok;
            PrefabUtility.SaveAsPrefabAssetAndConnect(instance, path, InteractionMode.UserAction, out ok);
            if (!ok) return null;

            WorkFolders.WriteManifest(path, basePrefab);
            Selection.activeGameObject = instance;
            return instance.GetComponent<VRCAvatarDescriptor>();
        }

        /// <summary>
        /// 前に作った改変アバターを開く。シーンにあればそれを表示し、なければシーンに出す。ほかのアバターは隠す。
        /// </summary>
        public static VRCAvatarDescriptor Open(GameObject prefab, string undoName)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            VRCAvatarDescriptor found = null;
            foreach (var a in AvatarFinder.FindInOpenScenes())
            {
                if (found == null && PrefabPath(a) == path) found = a;
            }

            if (found == null) return PlaceInScene(prefab, undoName);

            HideOthers(undoName, found.gameObject);
            if (!found.gameObject.activeSelf)
            {
                Undo.RecordObject(found.gameObject, undoName);
                found.gameObject.SetActive(true);
            }

            Selection.activeGameObject = found.gameObject;
            return found;
        }

        /// <summary>シーンのアバターのうち、keep 以外を隠す（アップロードの対象を 1 体にするため。Ctrl+Z で戻せる）。</summary>
        private static void HideOthers(string undoName, GameObject keep = null)
        {
            foreach (var other in AvatarFinder.FindInOpenScenes())
            {
                if (other.gameObject == keep || !other.gameObject.activeSelf) continue;
                Undo.RecordObject(other.gameObject, undoName);
                other.gameObject.SetActive(false);
            }
        }

        private static List<GameObject> _baseCache;

        /// <summary>
        /// 改変のもとにできるアバター（Assets 以下で、Avatar Descriptor が付いた Prefab。改変アバター自身は除く）。
        /// Prefab を 1 つずつ開いて調べるので、プロジェクトが変わるまで覚えておく。
        /// </summary>
        public static List<GameObject> BaseAvatars()
        {
            if (_baseCache != null && _baseCache.TrueForAll(p => p != null)) return _baseCache;

            var result = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(WorkFolders.Root + "/")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<VRCAvatarDescriptor>() != null) result.Add(prefab);
            }

            result.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            _baseCache = result;
            return result;
        }

        public static void ForgetBaseAvatars()
        {
            _baseCache = null;
        }

        /// <summary>
        /// 自分用アバターを置くフォルダ。すでにツールのフォルダ（複製マテリアルなど）があればそれを使い、
        /// 足りないサブフォルダを作る。別の自分用アバターが入っていれば、新しいフォルダにする。
        /// </summary>
        private static string FolderFor(GameObject avatar)
        {
            var folder = WorkFolders.ForAvatar(avatar);
            if (AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Length == 0)
            {
                WorkFolders.EnsureReadme();
                foreach (var sub in new[] { "Materials", "Textures", "Backups" }) WorkFolders.Ensure(folder + "/" + sub);
                return folder;
            }

            return WorkFolders.CreateAvatarFolder(avatar.name);
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

            var folder = WorkFolders.CreateAvatarFolder(name);
            var path = folder + "/" + Path.GetFileName(folder) + ".prefab";
            if (!AssetDatabase.CopyAsset(source, path)) return null;
            WorkFolders.WriteManifest(path, OriginalOf(prefab));
            return path;
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

            // avatar.json の名前もそろえる（元アバターの情報は前のまま）
            WorkFolders.WriteManifest(AssetDatabase.GetAssetPath(prefab), null);
            return null;
        }

        /// <summary>シーンに出す。アップロードで迷わないよう、ほかのアバターは隠す（Ctrl+Z で戻せる）。</summary>
        public static VRCAvatarDescriptor PlaceInScene(GameObject prefab, string undoName)
        {
            HideOthers(undoName);
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
