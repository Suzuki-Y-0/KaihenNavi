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

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
#if KAIHENNAVI_MA
            root.Add(Ui.Notice(L10n.Tr("outfit.intro"), "info"));

            root.Add(Ui.Heading(L10n.Tr("outfit.pick")));
            root.Add(ItemPicker.DropZone(L10n.Tr("outfit.drop"), ItemKind.Outfit, prefab => Wear(ctx, prefab)));

            var suggestions = ItemPicker.Suggestions(ctx.Avatar, ItemKind.Outfit);
            if (suggestions.Count > 0)
            {
                root.Add(Ui.Text(L10n.Tr("outfit.suggest"), "kn-hint"));
                root.Add(ItemPicker.SuggestionList(suggestions, prefab => Wear(ctx, prefab)));
            }

            // 着ている服（このツールや Setup Outfit で着せたもの）
            var worn = Worn(ctx.Avatar);
            if (worn.Count > 0)
            {
                root.Add(Ui.Heading(L10n.Tr("outfit.wearing")));
                var list = Ui.Box("kn-list");
                foreach (var outfit in worn) list.Add(WornRow(ctx, outfit));
                root.Add(list);
                root.Add(Ui.Notice(L10n.Tr("outfit.afterTips"), "ok"));
                root.Add(Ui.Button(L10n.Tr("outfit.toToggle"), () =>
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
            ctx.BeginEdit();
            UndoHelper.Separate();

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) return;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            Undo.SetTransformParent(instance.transform, avatar.transform, undoName);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            ctx.NoteCreated(instance);

            if (!SetupOutfit(instance))
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("outfit.setupFailed"), L10n.Tr("dialog.ok"));
            }
            else
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("outfit.done", prefab.name), L10n.Tr("dialog.ok"));
            }

            Selection.activeGameObject = instance;
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

            // 消すと「元に戻す」で戻せないので、脱がす＝隠すにする（アップロードにも含まれなくなる）
            var visible = outfit.activeSelf;
            row.Add(Ui.Button(visible ? L10n.Tr("outfit.takeOff") : L10n.Tr("outfit.putOn"), () =>
            {
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.RecordObject(outfit, L10n.Tr("undo.outfit"));
                outfit.SetActive(!visible);
                ctx.Rebuild();
            }, "kn-button--small"));
            row.Add(Ui.Button(L10n.Tr("common.where"), () =>
            {
                EditorGUIUtility.PingObject(outfit);
                Selection.activeGameObject = outfit;
            }, "kn-button--small", "kn-button--ghost"));
            return row;
        }
#endif
    }
}
