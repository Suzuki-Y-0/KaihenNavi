using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
#if KAIHENNAVI_MA
using nadena.dev.modular_avatar.core;
#endif

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 12：服を着せたい。
    /// 服の Prefab をアバターの中に置き、Modular Avatar の「Setup Outfit」で体の骨に合わせる（非破壊）。
    /// ヒエラルキーや Setup Outfit という言葉は画面に出さない。
    /// </summary>
    internal sealed class OutfitFeature : FeatureBase
    {
#if KAIHENNAVI_MA
        // MA のバージョンによってメニューの名前が違うので、順に試す
        private static readonly string[] SetupOutfitMenus =
        {
            "GameObject/ModularAvatar/Setup Outfit",
            "GameObject/Modular Avatar/Setup Outfit",
        };
#endif

        public override string Id { get { return "outfit"; } }
        public override int Number { get { return 12; } }
        public override Color Accent { get { return Ui.Hex("#B07CD8"); } }
        public override bool InTrial { get { return true; } }

        public override bool RefreshOnHierarchyChange { get { return true; } }

        /// <summary>既存の候補一覧と同じ方法で、装着済みの数を表示する。</summary>
        public override string NavBadge(VRCAvatarDescriptor avatar)
        {
#if KAIHENNAVI_MA
            var count = avatar == null ? 0 : Worn(avatar).Count;
            return count == 0 ? null : count.ToString();
#else
            return null;
#endif
        }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
#if KAIHENNAVI_MA
            // 着ている服（このツールや Setup Outfit で着せたもの）
            var worn = Worn(ctx.Avatar);
            if (worn.Count > 0)
            {
                root.Add(Ui.Heading(L10n.Tr("outfit.wearing")));
                var list = Ui.Box("kn-list");
                foreach (var outfit in worn) list.Add(WornRow(ctx, outfit));
                root.Add(list);
            }

            root.Add(ItemLibrary.Build(ctx.Avatar, ItemKind.Outfit, prefab => Wear(ctx, prefab)));
            var drop = ItemPicker.DropZone(L10n.Tr("outfit.drop"), ItemKind.Outfit, prefab => Wear(ctx, prefab));
            drop.AddToClassList("kn-drop--compact");
            root.Add(drop);

            if (worn.Count > 0)
            {
                root.Add(Ui.Notice(L10n.Tr("outfit.afterTips", L10n.Tr("feature.toggle.nav"), L10n.Tr("feature.skin.nav")), "ok"));
                root.Add(Ui.Button(L10n.Tr("library.goTo", L10n.Tr("feature.toggle.nav")), () =>
                {
                    if (ctx.OpenFeature != null) ctx.OpenFeature("toggle");
                }, "kn-button--ghost"));
            }
#else
            root.Add(Ui.Notice(L10n.Tr("outfit.needMa"), "warn"));
            root.Add(Ui.Button(L10n.Tr("toggle.openMa"), () => Application.OpenURL("https://modular-avatar.nadena.dev/ja/"), "kn-button--primary"));
#endif
            return root;
        }

#if KAIHENNAVI_MA
        /// <summary>服をアバターの中に置き、体の骨に合わせる。</summary>
        private static void Wear(FeatureContext ctx, GameObject prefab)
        {
            var avatar = ctx.Avatar;
            if (avatar == null || prefab == null) return;

            var undoName = L10n.Tr("undo.outfit");
            // バックアップの作成は操作グループの外、シーンの変更は MA の内部操作も含めて1手にする。
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            var setup = false;
            try
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null) return;
                Undo.RegisterCreatedObjectUndo(instance, undoName);
                Undo.SetTransformParent(instance.transform, avatar.transform, undoName);
                Undo.RecordObject(instance.transform, undoName);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                // MA が付け足す部品・骨の設定も、複製したインスタンスにだけ反映する。
                Undo.RegisterFullObjectHierarchyUndo(instance, undoName);
                setup = SetupOutfit(instance);
                ctx.NoteCreated(instance);
                // 後の自動保存で「追加した服」が Prefab に移っても、Undo が分かれないようここで一緒に保存する。
                MyAvatar.Save(avatar);
                Selection.activeGameObject = instance;
            }
            catch (System.Exception e)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogException(e);
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("outfit.failed"), L10n.Tr("dialog.ok"));
                return;
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
                UndoHelper.Separate();
            }
            if (setup) ctx.Tell(L10n.Tr("outfit.done", prefab.name));
            else EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("outfit.setupFailed"), L10n.Tr("dialog.ok"));
            ctx.Rebuild();
        }

        /// <summary>
        /// MA の Setup Outfit を呼ぶ。MA が公開している SetupOutfitUI(GameObject) を使う
        /// （メニューを ExecuteMenuItem で呼ぶと、対象のオブジェクトが渡らず「選択されていません」になるため）。
        /// MA のエディタ用アセンブリには直接の参照を持たないので、名前で探して呼ぶ。古い MA ならメニューを試す。
        /// </summary>
        private static bool SetupOutfit(GameObject outfit)
        {
            Selection.activeGameObject = outfit;
            var api = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("nadena.dev.modular_avatar.core.editor.SetupOutfit", false))
                .FirstOrDefault(t => t != null);
            var method = api == null ? null : api.GetMethod("SetupOutfitUI", new[] { typeof(GameObject) });
            if (method != null)
            {
                method.Invoke(null, new object[] { outfit });
                return outfit.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null;
            }

            foreach (var menu in SetupOutfitMenus)
            {
                if (EditorApplication.ExecuteMenuItem(menu)) return outfit.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null;
            }

            return false;
        }

        /// <summary>アバター直下にある、着せ済みの服（MA Merge Armature が付いたもの）。</summary>
        private static List<GameObject> Worn(VRCAvatarDescriptor avatar)
        {
            var result = new List<GameObject>();
            foreach (Transform child in avatar.transform)
            {
                if (child.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null) result.Add(child.gameObject);
            }

            return result;
        }

        private static VisualElement WornRow(FeatureContext ctx, GameObject outfit)
        {
            var row = Ui.Box("kn-list__row");
            var texts = Ui.Box("kn-list__texts");
            texts.Add(Ui.Text(outfit.name, "kn-list__title"));
            texts.Add(Ui.Text(outfit.activeSelf ? L10n.Tr("toggle.stateVisible") : L10n.Tr("toggle.stateHidden"), "kn-list__sub"));
            row.Add(texts);

            // 脱がす＝隠す（アップロードにも含まれなくなる）。完全に取り除くときは「取り除く」。
            var visible = outfit.activeSelf;
            row.Add(Ui.Button(visible ? L10n.Tr("outfit.takeOff") : L10n.Tr("outfit.putOn"), () =>
            {
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(L10n.Tr("undo.outfit"));
                try
                {
                    Undo.RecordObject(outfit, L10n.Tr("undo.outfit"));
                    outfit.SetActive(!visible);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(outfit);
                    MyAvatar.Save(ctx.Avatar);
                }
                finally { Undo.CollapseUndoOperations(group); }
                ctx.Rebuild();
            }, "kn-button--small"));
            row.Add(Ui.Button(L10n.Tr("common.where"), () =>
            {
                EditorGUIUtility.PingObject(outfit);
                Selection.activeGameObject = outfit;
            }, "kn-button--small", "kn-button--ghost"));
            // 着せた服をアバターから取り除く。Ctrl+Z で戻せる（自分用アバターの保存も同じ 1 手にまとめる）。
            row.Add(Ui.Button(L10n.Tr("outfit.remove"), () =>
            {
                var undoName = L10n.Tr("undo.outfitRemove");
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                Undo.DestroyObjectImmediate(outfit);
                ctx.CompleteEdit(group);
                ctx.Tell(L10n.Tr("outfit.removed"));
                ctx.Rebuild();
            }, "kn-button--small", "kn-button--ghost"));
            return row;
        }
#endif
    }
}
