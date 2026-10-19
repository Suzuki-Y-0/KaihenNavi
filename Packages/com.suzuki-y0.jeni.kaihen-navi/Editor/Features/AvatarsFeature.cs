using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 11：自分用アバターを管理したい（製品版）。
    /// 自分用アバターを一覧にし、シーンに出す・別バージョン（衣装違いなど）を作る・名前を変える、をまとめて行う。
    /// </summary>
    internal sealed class AvatarsFeature : FeatureBase
    {
        public override string Id { get { return "avatars"; } }
        public override int Number { get { return 11; } }
        public override Color Accent { get { return Ui.Hex("#7E8CE0"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("avatars.intro"), "info"));

            var prefabs = MyAvatar.All();
            if (prefabs.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("avatars.none"), "warn"));
                return root;
            }

            var list = Ui.Box("kn-list");
            foreach (var prefab in prefabs) list.Add(BuildRow(ctx, prefab));
            root.Add(list);
            return root;
        }

        private static VisualElement BuildRow(FeatureContext ctx, GameObject prefab)
        {
            var row = Ui.Box("kn-list__row", "kn-avatar-row");

            var thumb = Ui.Box("kn-avatar-row__thumb");
            SetThumbnail(thumb, prefab, 0);
            row.Add(thumb);

            var texts = Ui.Box("kn-list__texts");
            var name = new TextField { value = prefab.name };
            name.AddToClassList("kn-list__field");
            name.tooltip = L10n.Tr("avatars.renameTip");
            name.RegisterCallback<FocusOutEvent>(e => Rename(ctx, prefab, name));
            texts.Add(name);

            var original = MyAvatar.OriginalOf(prefab);
            var inScene = MyAvatar.CountInScene(prefab);
            var sub = L10n.Tr("avatars.from", original == null ? "-" : original.name);
            sub += "  ・  " + (inScene > 0 ? L10n.Tr("avatars.inScene", inScene) : L10n.Tr("avatars.notInScene"));
            texts.Add(Ui.Text(sub, "kn-list__sub"));

            var buttons = Ui.Box("kn-row", "kn-avatar-row__buttons");
            buttons.Add(Ui.Button(L10n.Tr("avatars.place"), () =>
            {
                var placed = MyAvatar.PlaceInScene(prefab, L10n.Tr("undo.place"));
                if (placed != null && ctx.SelectAvatar != null) ctx.SelectAvatar(placed);
                else ctx.Rebuild();
            }, "kn-button--small", "kn-button--primary"));
            buttons.Add(Ui.Button(L10n.Tr("avatars.duplicate"), () =>
            {
                var path = MyAvatar.Duplicate(prefab, prefab.name + " " + L10n.Tr("avatars.copySuffix"));
                if (path == null)
                {
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("avatars.duplicateFailed"), L10n.Tr("dialog.ok"));
                    return;
                }

                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                ctx.Rebuild();
            }, "kn-button--small"));
            buttons.Add(Ui.Button(L10n.Tr("common.where"), () => EditorGUIUtility.PingObject(prefab), "kn-button--small", "kn-button--ghost"));
            texts.Add(buttons);

            row.Add(texts);
            return row;
        }

        private static void Rename(FeatureContext ctx, GameObject prefab, TextField field)
        {
            if (prefab == null || field.value == prefab.name) return;
            var error = MyAvatar.Rename(prefab, field.value);
            if (error != null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("avatars.renameFailed"), L10n.Tr("dialog.ok"));
                field.SetValueWithoutNotify(prefab.name);
                return;
            }

            ctx.Rebuild();
        }

        /// <summary>サムネイルは作られるまで少しかかるので、出来るまで何度か見に行く。</summary>
        private static void SetThumbnail(VisualElement box, GameObject prefab, int tries)
        {
            var texture = AssetPreview.GetAssetPreview(prefab);
            if (texture != null)
            {
                box.style.backgroundImage = new StyleBackground(texture);
                return;
            }

            if (tries == 0) box.style.backgroundImage = new StyleBackground(AssetPreview.GetMiniThumbnail(prefab));
            if (tries < 20) box.schedule.Execute(() => SetThumbnail(box, prefab, tries + 1)).StartingIn(250);
        }
    }
}
