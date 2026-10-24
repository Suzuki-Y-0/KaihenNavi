using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// ①アバターをえらぶ。「新しく改変アバターを作る」「前に作った改変アバターを直す」の二択から始める。
    /// 新しく作るときは、元アバターのプレハブバリアントと決まった形のフォルダを自動で作る。
    /// </summary>
    internal sealed class AvatarChooser
    {
        public enum Mode
        {
            Start,
            New,
            Existing,
        }

        public Mode CurrentMode = Mode.Start;

        /// <summary>アバターが決まったとき。</summary>
        public Action<VRCAvatarDescriptor> Picked;

        /// <summary>画面を作り直してほしいとき。</summary>
        public Action Changed;

        /// <summary>今のアバターに戻る（直している途中で「ほかのアバターにする」を押したとき用）。null なら出さない。</summary>
        public Action Cancel;

        private GameObject _base;
        private string _name;

        public VisualElement Build()
        {
            switch (CurrentMode)
            {
                case Mode.New: return BuildNew();
                case Mode.Existing: return BuildExisting();
                default: return BuildStart();
            }
        }

        private void Go(Mode mode)
        {
            CurrentMode = mode;
            if (Changed != null) Changed();
        }

        // ---------------------------------------------------------------- 二択

        private VisualElement BuildStart()
        {
            var root = new VisualElement();
            var choices = Ui.Box("kn-choices");
            choices.Add(Choice("＋", L10n.Tr("start.new.title"), L10n.Tr("start.new.desc"), () =>
            {
                MyAvatar.ForgetBaseAvatars();
                _base = null;
                Go(Mode.New);
            }));
            choices.Add(Choice("↗", L10n.Tr("start.existing.title"), L10n.Tr("start.existing.desc"), () => Go(Mode.Existing)));
            root.Add(choices);

            // 前からシーンに置いて改変していたアバター（改変アバターになっていないもの）
            var loose = new List<VRCAvatarDescriptor>();
            foreach (var a in AvatarFinder.FindInOpenScenes())
            {
                if (MyAvatar.KindOf(a) != AvatarKind.Mine) loose.Add(a);
            }

            if (loose.Count > 0)
            {
                root.Add(Ui.Heading(L10n.Tr("start.loose")));
                root.Add(Ui.Text(L10n.Tr("start.looseHint"), "kn-hint"));
                var list = Ui.Box("kn-list");
                foreach (var a in loose)
                {
                    var avatar = a;
                    var row = Ui.Box("kn-list__row");
                    var texts = Ui.Box("kn-list__texts");
                    texts.Add(Ui.Text(avatar.name, "kn-list__title"));
                    texts.Add(Ui.Text(avatar.gameObject.activeInHierarchy ? L10n.Tr("toggle.stateVisible") : L10n.Tr("toggle.stateHidden"), "kn-list__sub"));
                    row.Add(texts);
                    row.Add(Ui.Button(L10n.Tr("start.looseOpen"), () => Pick(avatar), "kn-button--small"));
                    list.Add(row);
                }

                root.Add(list);
            }

            if (Cancel != null) root.Add(Ui.Button(L10n.Tr("start.back"), () => Cancel(), "kn-button--ghost"));
            return root;
        }

        private static VisualElement Choice(string icon, string title, string desc, Action onClick)
        {
            var card = new Button(onClick);
            card.AddToClassList("kn-choice");
            card.Add(Ui.Text(icon, "kn-choice__icon"));
            var texts = Ui.Box("kn-choice__texts");
            texts.Add(Ui.Text(title, "kn-choice__title"));
            texts.Add(Ui.Text(desc, "kn-choice__desc"));
            card.Add(texts);
            return card;
        }

        private VisualElement BackRow()
        {
            var row = Ui.Box("kn-row");
            row.Add(Ui.Button(L10n.Tr("start.toStart"), () => Go(Mode.Start), "kn-button--small", "kn-button--ghost"));
            return row;
        }

        // ---------------------------------------------------------------- 新しく作る

        private VisualElement BuildNew()
        {
            var root = new VisualElement();
            root.Add(BackRow());
            root.Add(Ui.Heading(L10n.Tr("new.pickBase")));

            var bases = MyAvatar.BaseAvatars();
            if (bases.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("new.noBase"), "warn"));
                return root;
            }

            var list = Ui.Box("kn-list", "kn-suggest");
            foreach (var b in bases)
            {
                var prefab = b;
                var row = new Button(() =>
                {
                    _base = prefab;
                    _name = prefab.name + " " + L10n.Tr("new.nameSuffix");
                    if (Changed != null) Changed();
                });
                row.AddToClassList("kn-list__row");
                row.AddToClassList("kn-suggest__row");
                row.EnableInClassList("kn-suggest__row--selected", prefab == _base);
                row.Add(Ui.Thumbnail(prefab, "kn-suggest__thumb"));
                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(prefab.name, "kn-list__title"));
                texts.Add(Ui.Text(Folder(prefab), "kn-list__sub"));
                row.Add(texts);
                list.Add(row);
            }

            root.Add(list);

            if (_base == null) return root;

            root.Add(Ui.Heading(L10n.Tr("new.name")));
            var field = new TextField { value = _name };
            field.AddToClassList("kn-list__field");
            root.Add(field);
            var where = Ui.Text(L10n.Tr("new.where", WorkFolders.AvatarsRoot + "/" + WorkFolders.Sanitize(_name ?? "")), "kn-hint");
            root.Add(where);
            root.Add(Ui.Text(L10n.Tr(MyAvatar.BaseOf(_base) == null ? "new.baseFirst" : "new.baseReuse"), "kn-hint"));
            field.RegisterValueChangedCallback(e =>
            {
                _name = e.newValue;
                where.text = L10n.Tr("new.where", WorkFolders.AvatarsRoot + "/" + WorkFolders.Sanitize(_name ?? ""));
            });

            root.Add(Ui.Button(L10n.Tr("new.create"), () =>
            {
                var name = string.IsNullOrEmpty(_name) ? _base.name : _name;
                var avatar = MyAvatar.CreateNew(_base, name, L10n.Tr("undo.newAvatar"));
                if (avatar == null)
                {
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("mine.failed"), L10n.Tr("dialog.ok"));
                    return;
                }

                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("new.done", avatar.name), L10n.Tr("dialog.ok"));
                _base = null;
                Pick(avatar);
            }, "kn-button--primary"));
            return root;
        }

        // ---------------------------------------------------------------- 前に作ったものを直す

        private VisualElement BuildExisting()
        {
            var root = new VisualElement();
            root.Add(BackRow());
            root.Add(Ui.Heading(L10n.Tr("existing.pick")));

            var prefabs = MyAvatar.All();
            if (prefabs.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("existing.none"), "warn"));
                root.Add(Ui.Button(L10n.Tr("start.new.title"), () =>
                {
                    MyAvatar.ForgetBaseAvatars();
                    Go(Mode.New);
                }, "kn-button--primary"));
                return root;
            }

            var list = Ui.Box("kn-list");
            foreach (var p in prefabs) list.Add(ExistingRow(p));
            root.Add(list);

            if (Edition.IsTrial) root.Add(Ui.Text(L10n.Tr("existing.fullOnly"), "kn-hint"));
            return root;
        }

        private VisualElement ExistingRow(GameObject prefab)
        {
            var row = Ui.Box("kn-list__row", "kn-avatar-row");
            // サムネイルを撮ってあれば、それを見せる（Prefab のプレビューより見分けやすい）
            var shot = AssetDatabase.LoadAssetAtPath<Texture2D>(
                System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)).Replace('\\', '/') + "/Thumbnail.png");
            row.Add(shot != null ? Ui.Image(shot, "kn-avatar-row__thumb") : Ui.Thumbnail(prefab, "kn-avatar-row__thumb"));

            var texts = Ui.Box("kn-list__texts");
            if (Edition.IsTrial)
            {
                texts.Add(Ui.Text(prefab.name, "kn-list__title"));
            }
            else
            {
                // 製品版：名前をその場で変えられる（ファイルとフォルダの名前も変わる）
                var field = new TextField { value = prefab.name };
                field.AddToClassList("kn-list__field");
                field.tooltip = L10n.Tr("avatars.renameTip");
                field.RegisterCallback<FocusOutEvent>(e =>
                {
                    if (field.value == prefab.name) return;
                    if (MyAvatar.Rename(prefab, field.value) != null)
                    {
                        EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("avatars.renameFailed"), L10n.Tr("dialog.ok"));
                        field.SetValueWithoutNotify(prefab.name);
                        return;
                    }

                    if (Changed != null) Changed();
                });
                texts.Add(field);
            }

            var original = MyAvatar.OriginalOf(prefab);
            texts.Add(Ui.Text(L10n.Tr("avatars.from", original == null ? "-" : original.name), "kn-list__sub"));

            // ベースを通っていない古い改変アバター：共通の設定（歩き方など）が効かないので、つなぎ直しを勧める
            if (original != null && MyAvatar.ParentBaseOf(prefab) == null)
            {
                texts.Add(Ui.Text(L10n.Tr("existing.noBase"), "kn-hint"));
            }

            var buttons = Ui.Box("kn-row", "kn-avatar-row__buttons");
            buttons.Add(Ui.Button(L10n.Tr("existing.open"), () =>
            {
                var avatar = MyAvatar.Open(prefab, L10n.Tr("undo.place"));
                if (avatar != null) Pick(avatar);
            }, "kn-button--small", "kn-button--primary"));

            if (!Edition.IsTrial)
            {
                buttons.Add(Ui.Button(L10n.Tr("avatars.duplicate"), () =>
                {
                    var path = MyAvatar.Duplicate(prefab, prefab.name + " " + L10n.Tr("avatars.copySuffix"));
                    if (path == null)
                    {
                        EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("avatars.duplicateFailed"), L10n.Tr("dialog.ok"));
                        return;
                    }

                    if (Changed != null) Changed();
                }, "kn-button--small"));
            }

            if (original != null && MyAvatar.ParentBaseOf(prefab) == null)
            {
                buttons.Add(Ui.Button(L10n.Tr("existing.rebase"), () =>
                {
                    if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("existing.rebaseConfirm", prefab.name, original.name),
                            L10n.Tr("existing.rebase"), L10n.Tr("dialog.cancel")))
                    {
                        return;
                    }

                    var error = MyAvatar.Rebase(prefab);
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"),
                        error == null ? L10n.Tr("existing.rebaseDone", prefab.name) : L10n.Tr("existing.rebaseFailed", error),
                        L10n.Tr("dialog.ok"));
                    if (Changed != null) Changed();
                }, "kn-button--small"));
            }

            buttons.Add(Ui.Button(L10n.Tr("common.where"), () => EditorGUIUtility.PingObject(prefab), "kn-button--small", "kn-button--ghost"));
            texts.Add(buttons);
            row.Add(texts);
            return row;
        }

        private void Pick(VRCAvatarDescriptor avatar)
        {
            CurrentMode = Mode.Start;
            if (Picked != null) Picked(avatar);
        }

        private static string Folder(GameObject prefab)
        {
            var dir = (Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)) ?? "").Replace('\\', '/');
            return dir.StartsWith("Assets/") ? dir.Substring("Assets/".Length) : dir;
        }
    }
}
