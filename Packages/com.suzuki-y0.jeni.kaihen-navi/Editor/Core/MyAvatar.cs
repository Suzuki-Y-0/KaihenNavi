using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Core;
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

        /// <summary>改変アバター（Avatars 以下の Prefab）か。ベースは含めない（自動保存で書き換えないため）。</summary>
        public static bool IsMinePath(string path)
        {
            if (path == null) return false;
            path = path.Replace('\\', '/');
            return path.StartsWith(WorkFolders.AvatarsRoot + "/") && path.EndsWith(".prefab") && !path.Contains("/Backups/");
        }

        /// <summary>改変のベース（Bases 以下の Prefab）か。</summary>
        public static bool IsBasePath(string path)
        {
            return path != null && path.Replace('\\', '/').StartsWith(WorkFolders.BasesRoot + "/") && path.EndsWith(".prefab");
        }

        /// <summary>ツールが作った Prefab（改変アバターかベース）か。</summary>
        private static bool IsToolPath(string path)
        {
            return path != null && path.Replace('\\', '/').StartsWith(WorkFolders.Root + "/");
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

            // つなぎ替えとバックアップの付け替えを 1 つの Undo グループにする（Ctrl+Z で両方戻り、履歴が離れない）
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                return CreateInGroup(avatar, backup);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        private static string CreateInGroup(VRCAvatarDescriptor avatar, BackupService backup)
        {
            var target = InstanceRoot(avatar);
            if (target == null) target = avatar.gameObject;

            // つなぎ替えるとバックアップの照合用 ID が変わるので、先に控えておく（隠れたバックアップも含める）
            var backups = backup.List(avatar, true);
            var basePrefab = PrefabUtility.IsPartOfPrefabInstance(target) ? PrefabUtility.GetCorrespondingObjectFromSource(target) : null;

            // 買ったときの Prefab につながっていれば、先にベースへつなぎ替える（今までの改変はそのまま残る）。
            // こうすると、新しく作った改変アバターと同じ「元アバター → ベース → 改変アバター」の形になる
            GameObject parent = null;
            if (basePrefab != null && !IsToolPath(AssetDatabase.GetAssetPath(basePrefab)))
            {
                parent = EnsureBase(basePrefab);
                if (parent != null) ReplaceSource(target, parent);
            }

            var folder = FolderFor(avatar.gameObject);
            var path = folder + "/" + System.IO.Path.GetFileName(folder) + ".prefab";
            bool ok;
            PrefabUtility.SaveAsPrefabAssetAndConnect(target, path, InteractionMode.UserAction, out ok);
            if (!ok) return null;

            WorkFolders.WriteManifest(path, basePrefab, parent);
            backup.Rekey(avatar, backups);
            return path;
        }

        /// <summary>
        /// 新しく改変アバターを作る。元のアバター（買ったときの Prefab）→ 改変のベース → 改変アバター、の
        /// 2 段のプレハブバリアントにする（ベースは元アバターごとに 1 つで、無ければ作る）。
        /// 決まった形のフォルダに置いて、シーンに出す（ほかのアバターは隠す）。
        /// </summary>
        public static VRCAvatarDescriptor CreateNew(GameObject basePrefab, string name, string undoName)
        {
            if (basePrefab == null || basePrefab.GetComponent<VRCAvatarDescriptor>() == null) return null;

            // 選んだのが元アバターならベースを用意する（ベースそのものを選んだときはそれを使う）
            var parent = IsBasePath(AssetDatabase.GetAssetPath(basePrefab)) ? basePrefab : EnsureBase(basePrefab);
            if (parent == null) return null;
            var original = IsBasePath(AssetDatabase.GetAssetPath(basePrefab)) ? OriginalOf(basePrefab) : basePrefab;

            var folder = WorkFolders.CreateAvatarFolder(name);
            var path = folder + "/" + System.IO.Path.GetFileName(folder) + ".prefab";

            HideOthers(undoName);
            var instance = PrefabUtility.InstantiatePrefab(parent) as GameObject;
            if (instance == null) return null;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            instance.name = System.IO.Path.GetFileName(folder);

            // 元のアバターをアップロードしたことがあると、その ID を引き継いで上書きしてしまうので外す
            ClearBlueprintId(instance);

            bool ok;
            PrefabUtility.SaveAsPrefabAssetAndConnect(instance, path, InteractionMode.UserAction, out ok);
            if (!ok) return null;

            WorkFolders.WriteManifest(path, original, parent);
            Selection.activeGameObject = instance;
            return instance.GetComponent<VRCAvatarDescriptor>();
        }

        // ---------------------------------------------------------------- 改変のベース

        /// <summary>
        /// 元アバターの「改変のベース」を返す。無ければ Bases/元アバター名/ に作る。
        /// ベースは元アバターのプレハブバリアントで、改変アバターはすべてこれのバリアントになる。
        /// 歩き方などの共通の設定はベースで変えれば、同じ元アバターから作った改変アバター全員に効く
        /// （改変アバター側で同じ所を変えていれば、そちらが優先）。元アバターが更新されれば、ベース経由で全員に届く。
        /// </summary>
        public static GameObject EnsureBase(GameObject original)
        {
            if (original == null) return null;
            var existing = BaseOf(original);
            if (existing != null) return existing;

            WorkFolders.EnsureReadme();
            WorkFolders.Ensure(WorkFolders.BasesRoot);
            var folder = AssetDatabase.GenerateUniqueAssetPath(WorkFolders.BasesRoot + "/" + WorkFolders.Sanitize(original.name));
            WorkFolders.Ensure(folder);
            var path = folder + "/" + WorkFolders.Sanitize(original.name) + WorkFolders.BaseSuffix + ".prefab";

            // シーンには残さない一時的なインスタンスから、バリアントとして保存する
            var instance = PrefabUtility.InstantiatePrefab(original) as GameObject;
            if (instance == null) return null;
            GameObject saved;
            try
            {
                ClearBlueprintId(instance);
                saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            if (saved == null) return null;
            WorkFolders.WriteManifest(path, original, original, "base");
            return saved;
        }

        /// <summary>
        /// シーンのインスタンスがつながっている Prefab を差し替える（Unity 標準の「Prefab の置き換え」と同じ）。
        /// 階層の場所で対応を取り、今までの変更（オーバーライド）は残す。ベースは元アバターと同じ階層なので全部残る。
        /// </summary>
        private static void ReplaceSource(GameObject instanceRoot, GameObject newSource)
        {
            var settings = new PrefabReplacingSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                changeRootNameToAssetName = false,
            };
            PrefabUtility.ReplacePrefabAssetOfPrefabInstance(instanceRoot, newSource, settings, InteractionMode.UserAction);
        }

        /// <summary>
        /// ベースを通っていない改変アバター（元アバターから直接作った古いもの）を、ベースにつなぎ直す。
        /// 一時的にシーンへ出して「元アバターのインスタンス＋改変」に戻し、ベースへ差し替えてから同じファイルに保存し直す
        /// （ファイルの GUID はそのままなので、シーンに置いてあるものもつながったまま）。
        /// 念のため、つなぎ直す前の Prefab を Backups フォルダにコピーしておく。失敗したら理由を返す。
        /// </summary>
        public static string Rebase(GameObject prefab)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            if (!IsMinePath(path)) return "not-mine";
            if (ParentBaseOf(prefab) != null) return null;
            var original = OriginalOf(prefab);
            if (original == null) return "no-original";
            // 改変アバターの直接の親が元アバターのときだけ（別バージョンをさらに重ねたものは対象外）
            if (PrefabUtility.GetCorrespondingObjectFromSource(prefab) != original) return "nested";

            var parent = EnsureBase(original);
            if (parent == null) return "no-base";

            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            var backupFolder = WorkFolders.Ensure(folder + "/Backups");
            var backupPath = AssetDatabase.GenerateUniqueAssetPath(backupFolder + "/" + prefab.name + " (before base).prefab");
            if (!AssetDatabase.CopyAsset(path, backupPath)) return "backup";

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) return "instantiate";
            try
            {
                // 改変アバターのインスタンスを 1 段ほどくと、元アバターのインスタンス＋改変（オーバーライド）になる
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                ReplaceSource(instance, parent);
                instance.name = prefab.name;
                if (PrefabUtility.SaveAsPrefabAsset(instance, path) == null) return "save";
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            WorkFolders.WriteManifest(path, original, parent);
            return null;
        }

        /// <summary>この元アバターのベース（無ければ null）。avatar.json の元アバターの GUID で探す。</summary>
        public static GameObject BaseOf(GameObject original)
        {
            if (original == null || !AssetDatabase.IsValidFolder(WorkFolders.BasesRoot)) return null;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
            foreach (var prefabGuid in AssetDatabase.FindAssets("t:Prefab", new[] { WorkFolders.BasesRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
                var manifest = WorkFolders.ReadManifest(Path.GetDirectoryName(path).Replace('\\', '/'));
                if (manifest == null || manifest.basePrefabGuid != guid) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) return prefab;
            }

            return null;
        }

        /// <summary>改変アバターの親になっているベース（元アバターから直接作った古い改変アバターなら null）。</summary>
        public static GameObject ParentBaseOf(GameObject prefab)
        {
            var current = prefab;
            for (var i = 0; i < 16 && current != null; i++)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) return null;
                var path = AssetDatabase.GetAssetPath(source);
                if (IsBasePath(path)) return source;
                if (!IsToolPath(path)) return null;
                current = source;
            }

            return null;
        }

        /// <summary>ベースから作った改変アバター（共通の設定を変えたときに影響を受けるもの）。</summary>
        public static List<GameObject> ChildrenOf(GameObject basePrefab)
        {
            var result = new List<GameObject>();
            foreach (var prefab in All())
            {
                if (ParentBaseOf(prefab) == basePrefab) result.Add(prefab);
            }

            return result;
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
                foreach (var sub in new[] { "Materials", "Backups" }) WorkFolders.Ensure(folder + "/" + sub);
                return folder;
            }

            return WorkFolders.CreateAvatarFolder(avatar.name);
        }

        // ---------------------------------------------------------------- アップロード先（ブループリント ID）

        /// <summary>アップロード先の ID。まだアップロードしていなければ空。</summary>
        public static string BlueprintId(GameObject avatar)
        {
            var pm = avatar == null ? null : avatar.GetComponent<PipelineManager>();
            return pm == null || string.IsNullOrEmpty(pm.blueprintId) ? "" : pm.blueprintId;
        }

        /// <summary>ID を外す（次のアップロードで新しいアバターになる）。外したら true。</summary>
        public static bool ClearBlueprintId(GameObject avatar)
        {
            var pm = avatar == null ? null : avatar.GetComponent<PipelineManager>();
            if (pm == null || string.IsNullOrEmpty(pm.blueprintId)) return false;
            Undo.RecordObject(pm, "KaihenNavi");
            pm.blueprintId = "";
            EditorUtility.SetDirty(pm);
            return true;
        }

        /// <summary>同じアップロード先を使っている、ほかの改変アバター（上書き事故の元）。</summary>
        public static List<GameObject> SameUploadTarget(VRCAvatarDescriptor avatar)
        {
            var result = new List<GameObject>();
            var id = BlueprintId(avatar.gameObject);
            if (id.Length == 0) return result;
            var own = PrefabPath(avatar);
            foreach (var prefab in All())
            {
                if (AssetDatabase.GetAssetPath(prefab) == own) continue;
                if (BlueprintId(prefab) == id) result.Add(prefab);
            }

            return result;
        }

        // ---------------------------------------------------------------- 自動保存

        /// <summary>
        /// シーン上の改変のうち、まだ自分用アバターに保存していないものがあるか。
        /// コンストレイントが動かしている骨の位置・回転・大きさのずれは、改変に数えない（毎秒呼ばれる）。
        /// </summary>
        public static bool HasUnsaved(VRCAvatarDescriptor avatar)
        {
            if (KindOf(avatar) != AvatarKind.Mine) return false;
            var root = InstanceRoot(avatar);
            return root != null && HasUserOverrides(root);
        }

        /// <summary>
        /// Prefab のインスタンスに、利用者の改変といえる変更があるか。
        /// コンストレイント（Unity・VRChat）は編集中もシーンの描画のたびに骨を動かし、ごくわずかな回転のずれが
        /// 「Prefab からの変更」になる。これを改変と見なすと、何もしていないのに 未保存 になり、自動保存が
        /// Prefab を何度も書き換えて Undo に「Apply instance to prefab」が積み上がる。
        /// まず安い HasPrefabInstanceAnyOverrides で確かめ、変更があるときだけ中身を調べる。
        /// </summary>
        internal static bool HasUserOverrides(GameObject root)
        {
            if (root == null || !PrefabUtility.HasPrefabInstanceAnyOverrides(root, false)) return false;
            if (PrefabUtility.GetAddedComponents(root).Count > 0 || PrefabUtility.GetRemovedComponents(root).Count > 0 ||
                PrefabUtility.GetAddedGameObjects(root).Count > 0 || PrefabUtility.GetRemovedGameObjects(root).Count > 0) return true;
            var driven = DrivenTransforms(root);
            Dictionary<Object, bool> drivenOnly = null;
            foreach (var o in PrefabUtility.GetObjectOverrides(root, false))
            {
                var t = o.instanceObject as Transform;
                if (t == null || !driven.Contains(t)) return true;
                if (drivenOnly == null) drivenOnly = DrivenOnlyTargets(root, driven);
                var source = PrefabUtility.GetCorrespondingObjectFromSource(t);
                bool only;
                // 対応が分からなければ、安全側（改変あり）に倒す
                if (source == null || !drivenOnly.TryGetValue(source, out only) || !only) return true;
            }
            return false;
        }

        /// <summary>
        /// コンストレイントが動かす Transform（の Prefab 側）ごとに、変更が位置・回転・大きさだけか。
        /// 変更の対象（PropertyModification.target）は 1 つ上の Prefab の部品なので、そちらで引く。
        /// </summary>
        private static Dictionary<Object, bool> DrivenOnlyTargets(GameObject root, HashSet<Transform> driven)
        {
            var sources = new HashSet<Object>();
            foreach (var t in driven)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(t);
                if (source != null) sources.Add(source);
            }
            var result = new Dictionary<Object, bool>();
            var modifications = PrefabUtility.GetPropertyModifications(root);
            if (modifications == null) return result;
            foreach (var m in modifications)
            {
                if (m == null || m.target == null || !sources.Contains(m.target) || PrefabUtility.IsDefaultOverride(m)) continue;
                bool only;
                if (!result.TryGetValue(m.target, out only)) only = true;
                result[m.target] = only && IsLocalTransformPath(m.propertyPath);
            }
            return result;
        }

        private static bool IsLocalTransformPath(string path)
        {
            return path != null && (path.StartsWith("m_LocalPosition") || path.StartsWith("m_LocalRotation") ||
                path.StartsWith("m_LocalEulerAnglesHint") || path.StartsWith("m_LocalScale"));
        }

        // ---------------------------------------------------------------- コンストレイントが動かす骨

        /// <summary>ヒエラルキーが変わるまで使い回す（部品の追加・削除・Undo でも hierarchyChanged が来る）。</summary>
        private static class DrivenCache
        {
            public static readonly Dictionary<GameObject, HashSet<Transform>> Sets = new Dictionary<GameObject, HashSet<Transform>>();
            public static readonly Dictionary<System.Type, bool> VrcTypes = new Dictionary<System.Type, bool>();

            static DrivenCache()
            {
                EditorApplication.hierarchyChanged += Sets.Clear;
            }
        }

        /// <summary>
        /// root 以下で、編集中もコンストレイントが位置・回転を動かしている Transform。
        /// Unity のコンストレイント（IConstraint）は付いている Transform を動かす。
        /// VRChat のコンストレイントは SDK が無くてもコンパイルできるよう型名（VRCConstraintBase の派生）で見分け、
        /// TargetTransform が指定されていればそれを、無ければ付いている Transform を動かす。
        /// </summary>
        internal static HashSet<Transform> DrivenTransforms(GameObject root)
        {
            HashSet<Transform> set;
            if (root == null) return new HashSet<Transform>();
            if (DrivenCache.Sets.TryGetValue(root, out set)) return set;
            set = new HashSet<Transform>();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c is UnityEngine.Animations.IConstraint)
                {
                    set.Add(c.transform);
                    continue;
                }
                if (!IsVrcConstraint(c.GetType())) continue;
                var target = VrcConstraintTarget(c);
                set.Add(target != null ? target : c.transform);
            }
            DrivenCache.Sets[root] = set;
            return set;
        }

        private static bool IsVrcConstraint(System.Type type)
        {
            bool result;
            if (DrivenCache.VrcTypes.TryGetValue(type, out result)) return result;
            for (var t = type; t != null && !result; t = t.BaseType)
                result = t.Name == "VRCConstraintBase" && t.Namespace != null && t.Namespace.StartsWith("VRC");
            DrivenCache.VrcTypes[type] = result;
            return result;
        }

        /// <summary>VRChat のコンストレイントの TargetTransform（フィールドかプロパティ）。無ければ null。</summary>
        private static Transform VrcConstraintTarget(Component constraint)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            var type = constraint.GetType();
            var field = type.GetField("TargetTransform", flags);
            if (field != null) return field.GetValue(constraint) as Transform;
            var property = type.GetProperty("TargetTransform", flags);
            return property != null && property.CanRead ? property.GetValue(constraint, null) as Transform : null;
        }

        /// <summary>
        /// シーン上の改変を自分用アバターに書き込む。書き込み先が自分用アバターでなければ何もしない
        /// （買ったときの Prefab を書き換えないための確認）。
        /// </summary>
        public static bool Save(VRCAvatarDescriptor avatar, bool recordUndo = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            var root = InstanceRoot(avatar);
            if (root == null) return false;

            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (!IsMinePath(path)) return false;
            // コンストレイントが動かした骨のずれだけなら書き込まない（Undo も積まない）
            if (!HasUserOverrides(root)) return false;

            // AutomatedAction はシーンの追加物を Undo 対象外の Prefab 部品にしてしまう。
            // 保存も編集と同じグループへ入れ、Ctrl+Z でアセットとシーンを一緒に戻す。
            // Undo コールバックからの復元だけは新しい履歴を作らない。
            var group = Undo.GetCurrentGroup();
            var undoName = Undo.GetCurrentGroupName();
            if (recordUndo) Undo.FlushUndoRecordObjects();
            // 配列が縮んだ後の範囲外の要素の変更は、Apply しても自分用 Prefab に残る（Unity の仕様）。
            // シーン側を先に片付け、縮む枠があるときだけ保存後にファイル側も片付ける
            RemoveStaleOverrides(root, recordUndo ? undoName : null);
            var shrinks = ShrinksMaterials(root);
            PrefabUtility.ApplyPrefabInstance(root, recordUndo ? InteractionMode.UserAction : InteractionMode.AutomatedAction);
            if (shrinks || HasStaleOverrides(AssetDatabase.LoadAssetAtPath<GameObject>(path))) RemoveStaleOverridesInAsset(path);
            if (recordUndo)
            {
                Undo.SetCurrentGroupName(undoName);
                Undo.CollapseUndoOperations(group);
            }
            return true;
        }

        // ---------------------------------------------------------------- 残った古いオーバーライド

        /// <summary>
        /// シーンのアバター（Prefab のインスタンス）に残った、もう使われていないオーバーライドを取り除く。
        /// 自分用アバターでなくても使える（シーンだけの改変でも、消したファイルへの参照を残さない）。
        /// </summary>
        internal static bool RemoveStaleOverrides(VRCAvatarDescriptor avatar, string undoName)
        {
            var root = InstanceRoot(avatar);
            return root != null && RemoveStaleOverrides(root, undoName);
        }

        /// <summary>
        /// Undo・やり直しの後に呼ぶ（Undo には記録しない）。やり直しでは保存（Apply）の記録が自分用 Prefab を
        /// 古い変更ごと戻し、ファイル側の片付け（SaveAsPrefabAsset）は Undo に載らないので、ここで片付け直す。
        /// 古い変更が無ければ Prefab は開かない。
        /// </summary>
        internal static void RemoveStaleOverridesWithoutUndo(VRCAvatarDescriptor avatar)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var root = InstanceRoot(avatar);
            if (root == null) return;
            RemoveStaleOverrides(root, null);
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (!IsMinePath(path)) return;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!HasStaleOverrides(asset)) return;
            // やり直しで戻った内容がまだファイルに書かれていなければ、先に書いてから開く
            AssetDatabase.SaveAssetIfDirty(asset);
            RemoveStaleOverridesInAsset(path);
        }

        /// <summary>
        /// root 以下の Prefab のインスタンスから、使われていないオーバーライドを取り除く。取り除いたら true。
        /// ・マテリアルの配列が縮んだ後に残る、範囲外の要素の変更（m_Materials.Array.data[i]）。
        ///   Unity は配列が短くなっても範囲外の要素の変更を消さないため、インナーカラーを戻した後も
        ///   内側用のマテリアルへの参照が残り、ファイルを片付けられなかった。範囲外なので見た目は変わらない。
        /// ・変更先の部品が無くなった変更のうち、インナーカラーが作ったファイルを参照しているもの。
        /// undoName を渡すと Undo に記録する（null なら記録しない）。
        /// </summary>
        private static bool RemoveStaleOverrides(GameObject root, string undoName)
        {
            if (root == null) return false;
            var changed = false;
            var seen = new HashSet<Object>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                var handle = PrefabUtility.GetPrefabInstanceHandle(go);
                if (handle == null || !seen.Add(handle)) continue;
                var cleaned = WithoutStale(PrefabUtility.GetPropertyModifications(go));
                if (cleaned == null) continue;
                if (undoName != null) Undo.RegisterCompleteObjectUndo(handle, undoName);
                PrefabUtility.SetPropertyModifications(go, cleaned);
                changed = true;
            }
            return changed;
        }

        /// <summary>取り除くものがあるか（読むだけ）。保存のたびに呼ぶので、Prefab は開かない。</summary>
        private static bool HasStaleOverrides(GameObject root)
        {
            if (root == null) return false;
            var seen = new HashSet<Object>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var handle = PrefabUtility.GetPrefabInstanceHandle(t.gameObject);
                if (handle == null || !seen.Add(handle)) continue;
                if (WithoutStale(PrefabUtility.GetPropertyModifications(t.gameObject)) != null) return true;
            }
            return false;
        }

        /// <summary>シーンの方が自分用 Prefab よりマテリアルの枠が少ない Renderer があるか（保存で配列が縮む）。</summary>
        private static bool ShrinksMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(r);
                if (source != null && r.sharedMaterials.Length < source.sharedMaterials.Length) return true;
            }
            return false;
        }

        /// <summary>
        /// 自分用 Prefab（ファイル。ベースのプレハブバリアント）に残った、使われていないオーバーライドを取り除いて保存する。
        /// Prefab 編集と同じ方法（LoadPrefabContents → SaveAsPrefabAsset）で開くので、ほかのオーバーライドはそのまま。
        /// Ctrl+Z では、同じグループの Apply の記録が保存前の Prefab に戻す（範囲外の変更は見た目に影響しない）。
        /// </summary>
        private static void RemoveStaleOverridesInAsset(string path)
        {
            if (!IsMinePath(path)) return;
            var contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null) return;
            try
            {
                if (RemoveStaleOverrides(contents, null)) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private const string MaterialElement = "m_Materials.Array.data[";
        private const string MaterialSize = "m_Materials.Array.size";

        /// <summary>使われていない変更を除いた一覧。除くものが無ければ null。</summary>
        private static PropertyModification[] WithoutStale(PropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0) return null;
            // 配列の長さは、この段の変更にあればその値、無ければ元（target）の長さ
            var sizes = new Dictionary<Object, int>();
            foreach (var m in modifications)
            {
                int size;
                if (m != null && m.target is Renderer && m.propertyPath == MaterialSize && int.TryParse(m.value, out size))
                    sizes[m.target] = size;
            }
            var result = new List<PropertyModification>(modifications.Length);
            foreach (var m in modifications)
            {
                if (m != null && IsStale(m, sizes)) continue;
                result.Add(m);
            }
            return result.Count == modifications.Length ? null : result.ToArray();
        }

        private static bool IsStale(PropertyModification m, Dictionary<Object, int> sizes)
        {
            if (m.target == null)
            {
                // 部品が無くなった変更は、インナーカラーの生成物を参照しているものだけ外す
                return m.objectReference != null && InnerColorAssets.IsGenerated(m.objectReference);
            }
            var renderer = m.target as Renderer;
            var path = m.propertyPath;
            if (renderer == null || path == null || !path.StartsWith(MaterialElement, System.StringComparison.Ordinal) ||
                !path.EndsWith("]", System.StringComparison.Ordinal)) return false;
            int index;
            if (!int.TryParse(path.Substring(MaterialElement.Length, path.Length - MaterialElement.Length - 1), out index)) return false;
            int size;
            if (!sizes.TryGetValue(m.target, out size)) size = renderer.sharedMaterials.Length;
            return index >= size;
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

        /// <summary>改変アバターの Prefab（Assets/KaihenNavi/Avatars 以下の、Avatar Descriptor が付いた Prefab。ベースは含めない）。</summary>
        public static List<GameObject> All()
        {
            var result = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(WorkFolders.Root)) return result;
            if (!AssetDatabase.IsValidFolder(WorkFolders.AvatarsRoot)) return result;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { WorkFolders.AvatarsRoot }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.Contains("/Backups/")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
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
                if (!IsToolPath(AssetDatabase.GetAssetPath(source))) return source;
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

            // 複製元と同じアップロード先にならないよう、ID を外す
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (ClearBlueprintId(contents)) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            WorkFolders.WriteManifest(path, OriginalOf(prefab), ParentBaseOf(prefab));
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
