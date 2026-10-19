using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#if KAIHENNAVI_MA
using nadena.dev.modular_avatar.core;
#endif

namespace Jeni.KaihenNavi
{
#pragma warning disable 0649 // JSON から読み込む（MA が無いときは使わない）
    /// <summary>メニュー整理の状態（バックアップに入れる）。元のメニューと、項目ごとの変更だけを持つ。</summary>
    [Serializable]
    internal sealed class MenuEdits
    {
        public bool extracted;
        public string originalMenu;
        public List<MenuEdit> items = new List<MenuEdit>();
    }

    [Serializable]
    internal sealed class MenuEdit
    {
        /// <summary>元のメニューでの場所（"2.0.3" のような番号の並び）。</summary>
        public string key;
        public string label;
        public bool hidden;
        public bool other;
        public int order;
    }

#pragma warning restore 0649

    /// <summary>
    /// 機能 14：メニューを整理したい（製品版）。
    /// 元アバターの Expression メニューを、改変アバターの中の MA メニュー項目として取り出し（元のメニューのファイルは変えない）、
    /// 項目ごとに「使う／使わない」・並べ替え・名前の変更・「その他」にまとめる、ができるようにする。
    /// 使わない項目は KaihenNavi_HiddenMenu（EditorOnly）の下へ移すだけなので、いつでも戻せる。
    /// メニューから外しても、その機能は初期値のまま固定されるだけで、FX やパラメータは残る（画面で伝える）。
    /// </summary>
    internal sealed class MenuFeature : FeatureBase
    {
        public const string RootName = "KaihenNavi_AvatarMenu";
        public const string HiddenName = "KaihenNavi_HiddenMenu";
        public const string OtherName = "others";
        private const int MaxDepth = 8;

        public override string Id { get { return "menu"; } }
        public override int Number { get { return 14; } }
        public override Color Accent { get { return Ui.Hex("#9AA5B1"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("menu.intro"), "info"));
#if KAIHENNAVI_MA
            var avatar = ctx.Avatar;
            var menuRoot = avatar.transform.Find(RootName);

            if (menuRoot == null)
            {
                var original = avatar.expressionsMenu;
                if (original == null || original.controls == null || original.controls.Count == 0)
                {
                    root.Add(Ui.Notice(L10n.Tr("menu.noMenu"), "warn"));
                    return root;
                }

                root.Add(Ui.Text(L10n.Tr("menu.extractHint", original.controls.Count), "kn-hint"));
                root.Add(Ui.Button(L10n.Tr("menu.extract"), () =>
                {
                    var undoName = L10n.Tr("undo.menu");
                    ctx.BeginEdit();
                    UndoHelper.Separate();
                    var edits = new MenuEdits { extracted = true, originalMenu = AssetDatabase.GetAssetPath(original) };
                    var built = BuildMenu(avatar, edits, undoName);
                    if (built != null) ctx.NoteCreated(built);
                    UndoHelper.Separate();
                    ctx.Rebuild();
                }, "kn-button--primary"));
                return root;
            }

            root.Add(Ui.Notice(L10n.Tr("menu.hiddenNote"), "ok"));
            var list = Ui.Box("kn-list");
            AddRows(ctx, list, menuRoot, 0);
            var others = menuRoot.Find(OtherName);
            var hidden = menuRoot.Find(HiddenName);
            root.Add(list);

            if (hidden != null && hidden.childCount > 0)
            {
                root.Add(Ui.Heading(L10n.Tr("menu.hiddenHeading", hidden.childCount)));
                var hiddenList = Ui.Box("kn-list");
                foreach (Transform t in hidden) hiddenList.Add(Row(ctx, t, 0, true));
                root.Add(hiddenList);
            }

            if (others != null && others.childCount == 0) root.Add(Ui.Text(L10n.Tr("menu.othersEmpty"), "kn-hint"));

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("menu.restore"), () =>
            {
                if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("menu.restoreConfirm"), L10n.Tr("dialog.ok"), L10n.Tr("dialog.cancel"))) return;
                ctx.BeginEdit();
                UndoHelper.Separate();
                RemoveMenu(avatar, L10n.Tr("undo.menu"));
                UndoHelper.Separate();
                ctx.Rebuild();
            }, "kn-button--ghost"));
            root.Add(buttons);
            root.Add(Ui.Text(L10n.Tr("menu.tip"), "kn-hint"));
#else
            root.Add(Ui.Notice(L10n.Tr("menu.needMa"), "warn"));
#endif
            return root;
        }

#if KAIHENNAVI_MA
        // ---------------------------------------------------------------- 一覧

        private static void AddRows(FeatureContext ctx, VisualElement list, Transform parent, int depth)
        {
            foreach (Transform t in parent)
            {
                if (t.name == HiddenName) continue;
                var item = t.GetComponent<ModularAvatarMenuItem>();
                if (item == null) continue;
                list.Add(Row(ctx, t, depth, false));
                if (depth < MaxDepth && IsSubmenu(item) && item.MenuSource == SubmenuSource.Children) AddRows(ctx, list, t, depth + 1);
            }
        }

        private static bool IsSubmenu(ModularAvatarMenuItem item)
        {
            return item.Control != null && item.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
        }

        private static VisualElement Row(FeatureContext ctx, Transform t, int depth, bool isHidden)
        {
            var item = t.GetComponent<ModularAvatarMenuItem>();
            var row = Ui.Box("kn-list__row", "kn-menu__row");
            row.style.paddingLeft = 6 + depth * 18;
            if (isHidden) row.AddToClassList("kn-menu__row--hidden");

            // 使う／使わない
            var use = new Toggle { value = !isHidden };
            use.AddToClassList("kn-list__toggle");
            use.tooltip = L10n.Tr("menu.useTip");
            use.RegisterValueChangedCallback(e => SetHidden(ctx, t, !e.newValue));
            row.Add(use);

            var icon = Ui.Box("kn-menu__icon");
            if (item != null && item.Control != null && item.Control.icon != null) icon.style.backgroundImage = new StyleBackground(item.Control.icon);
            row.Add(icon);

            var texts = Ui.Box("kn-list__texts");
            var field = new TextField { value = Label(t) };
            field.AddToClassList("kn-list__field");
            field.RegisterCallback<FocusOutEvent>(e => Rename(ctx, t, field.value));
            texts.Add(field);
            texts.Add(Ui.Text(Describe(ctx.Avatar, item, isHidden), "kn-list__sub"));
            row.Add(texts);

            if (!isHidden)
            {
                row.Add(Ui.Button("↑", () => Move(ctx, t, -1), "kn-button--small", "kn-button--ghost"));
                row.Add(Ui.Button("↓", () => Move(ctx, t, 1), "kn-button--small", "kn-button--ghost"));
                var menuRoot = ctx.Avatar.transform.Find(RootName);
                if (t.parent == menuRoot && t.name != OtherName)
                {
                    row.Add(Ui.Button(L10n.Tr("menu.toOthers"), () => ToOthers(ctx, t), "kn-button--small"));
                }
                else if (t.parent != null && t.parent.name == OtherName && t.parent.parent == menuRoot)
                {
                    row.Add(Ui.Button(L10n.Tr("menu.fromOthers"), () => FromOthers(ctx, t), "kn-button--small"));
                }
            }

            return row;
        }

        private static string Label(Transform t)
        {
            var item = t.GetComponent<ModularAvatarMenuItem>();
            if (item != null && !string.IsNullOrEmpty(item.label)) return item.label;
            return item != null && item.Control != null && !string.IsNullOrEmpty(item.Control.name) ? item.Control.name : t.name;
        }

        /// <summary>種類と、使わないときに固定される状態の説明。</summary>
        private static string Describe(VRCAvatarDescriptor avatar, ModularAvatarMenuItem item, bool isHidden)
        {
            if (item == null || item.Control == null) return "";
            var control = item.Control;
            string kind;
            switch (control.type)
            {
                case VRCExpressionsMenu.Control.ControlType.SubMenu: kind = L10n.Tr("menu.kindSub"); break;
                case VRCExpressionsMenu.Control.ControlType.Toggle: kind = L10n.Tr("menu.kindToggle"); break;
                case VRCExpressionsMenu.Control.ControlType.Button: kind = L10n.Tr("menu.kindButton"); break;
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet: kind = L10n.Tr("menu.kindRadial"); break;
                default: kind = L10n.Tr("menu.kindOther"); break;
            }

            if (!isHidden) return kind;

            // 使わないときは、その機能がどの状態で止まるかを出す
            string fixedState;
            if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
            {
                fixedState = L10n.Tr("menu.fixedSub");
            }
            else
            {
                var name = control.parameter != null ? control.parameter.name : null;
                var parameter = FindParameter(avatar, name);
                if (parameter == null)
                {
                    fixedState = L10n.Tr("menu.fixedUnknown");
                }
                else if (control.type == VRCExpressionsMenu.Control.ControlType.Toggle || control.type == VRCExpressionsMenu.Control.ControlType.Button)
                {
                    fixedState = Mathf.Approximately(parameter.defaultValue, control.value) ? L10n.Tr("menu.fixedOn") : L10n.Tr("menu.fixedOff");
                }
                else
                {
                    fixedState = L10n.Tr("menu.fixedValue", parameter.defaultValue.ToString("0.##"));
                }
            }

            return kind + " — " + fixedState;
        }

        private static VRCExpressionParameters.Parameter FindParameter(VRCAvatarDescriptor avatar, string name)
        {
            if (string.IsNullOrEmpty(name) || avatar.expressionParameters == null || avatar.expressionParameters.parameters == null) return null;
            return avatar.expressionParameters.parameters.FirstOrDefault(p => p != null && p.name == name);
        }

        // ---------------------------------------------------------------- 変える

        private static void SetHidden(FeatureContext ctx, Transform t, bool hide)
        {
            var avatar = ctx.Avatar;
            var menuRoot = avatar.transform.Find(RootName);
            if (menuRoot == null) return;
            var undoName = L10n.Tr("undo.menu");
            ctx.BeginEdit();
            UndoHelper.Separate();
            if (hide)
            {
                var holder = Holder(menuRoot, undoName);
                Undo.SetTransformParent(t, holder, undoName);
            }
            else
            {
                // 元の場所（番号の上の段）に戻す。見つからなければいちばん上の段
                var parent = ParentFor(menuRoot, t.name) ?? menuRoot;
                Undo.SetTransformParent(t, parent, undoName);
                SortByKey(parent, t);
            }

            UndoHelper.Separate();
            ctx.Rebuild();
        }

        private static void Rename(FeatureContext ctx, Transform t, string label)
        {
            var item = t.GetComponent<ModularAvatarMenuItem>();
            if (item == null || label == Label(t)) return;
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.RecordObject(item, L10n.Tr("undo.menu"));
            item.label = label;
            EditorUtility.SetDirty(item);
            UndoHelper.Separate();
        }

        private static void Move(FeatureContext ctx, Transform t, int delta)
        {
            var index = t.GetSiblingIndex() + delta;
            if (index < 0 || t.parent == null || index >= t.parent.childCount) return;
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.RegisterFullObjectHierarchyUndo(t.parent.gameObject, L10n.Tr("undo.menu"));
            t.SetSiblingIndex(index);
            UndoHelper.Separate();
            ctx.Rebuild();
        }

        private static void ToOthers(FeatureContext ctx, Transform t)
        {
            var menuRoot = ctx.Avatar.transform.Find(RootName);
            var undoName = L10n.Tr("undo.menu");
            ctx.BeginEdit();
            UndoHelper.Separate();
            var others = menuRoot.Find(OtherName);
            if (others == null)
            {
                var go = new GameObject(OtherName);
                Undo.RegisterCreatedObjectUndo(go, undoName);
                go.transform.SetParent(menuRoot, false);
                ctx.NoteCreated(go);
                var item = Undo.AddComponent<ModularAvatarMenuItem>(go);
                item.Control = new VRCExpressionsMenu.Control
                {
                    name = L10n.Tr("menu.othersName"),
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                };
                item.MenuSource = SubmenuSource.Children;
                item.label = L10n.Tr("menu.othersName");
                others = go.transform;
            }

            Undo.SetTransformParent(t, others, undoName);
            others.SetAsLastSibling();
            UndoHelper.Separate();
            ctx.Rebuild();
        }

        private static void FromOthers(FeatureContext ctx, Transform t)
        {
            var menuRoot = ctx.Avatar.transform.Find(RootName);
            var undoName = L10n.Tr("undo.menu");
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.SetTransformParent(t, menuRoot, undoName);
            SortByKey(menuRoot, t);
            UndoHelper.Separate();
            ctx.Rebuild();
        }

        // ---------------------------------------------------------------- 作る・消す

        /// <summary>
        /// 元のメニューから MA のメニュー項目を作り、変更（edits）を当てる。アバター設定のメニューは空のメニューに差し替える
        /// （元のメニューのファイルは変えない。空のメニューはアバターのフォルダに作る）。
        /// </summary>
        public static GameObject BuildMenu(VRCAvatarDescriptor avatar, MenuEdits edits, string undoName)
        {
            var original = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(edits.originalMenu);
            if (original == null) return null;

            var old = avatar.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, undoName);
            root.transform.SetParent(avatar.transform, false);
            Undo.AddComponent<ModularAvatarMenuInstaller>(root);
            Undo.AddComponent<ModularAvatarMenuGroup>(root);

            var byKey = edits.items.Where(e => e != null && !string.IsNullOrEmpty(e.key)).GroupBy(e => e.key).ToDictionary(g => g.Key, g => g.First());
            var created = new Dictionary<string, Transform>();
            BuildLevel(original, root.transform, "", 0, new HashSet<VRCExpressionsMenu> { original }, byKey, created);

            // 「その他」と「使わない」を当てる
            if (byKey.Values.Any(e => e.other))
            {
                var others = new GameObject(OtherName);
                others.transform.SetParent(root.transform, false);
                var item = others.AddComponent<ModularAvatarMenuItem>();
                item.Control = new VRCExpressionsMenu.Control { name = L10n.Tr("menu.othersName"), type = VRCExpressionsMenu.Control.ControlType.SubMenu };
                item.MenuSource = SubmenuSource.Children;
                item.label = L10n.Tr("menu.othersName");
                foreach (var e in byKey.Values.Where(e => e.other && !e.hidden).OrderBy(e => e.order))
                {
                    Transform t;
                    if (created.TryGetValue(e.key, out t)) t.SetParent(others.transform, false);
                }
            }

            foreach (var e in byKey.Values.Where(e => e.hidden))
            {
                Transform t;
                if (created.TryGetValue(e.key, out t)) t.SetParent(Holder(root.transform, null), false);
            }

            // 並び順（段ごとに、覚えている順番で並べ直す。覚えていないものは元の順）
            foreach (var group in created.Values.Where(t => t.parent != null && t.parent.name != HiddenName).GroupBy(t => t.parent))
            {
                var ordered = group.Select((t, i) =>
                {
                    MenuEdit e;
                    var order = byKey.TryGetValue(t.name, out e) && e.order >= 0 ? e.order : 1000 + i;
                    return new { t, order };
                }).OrderBy(x => x.order).ToList();
                for (var i = 0; i < ordered.Count; i++) ordered[i].t.SetSiblingIndex(i);
            }

            var others2 = root.transform.Find(OtherName);
            if (others2 != null) others2.SetAsLastSibling();
            var holder = root.transform.Find(HiddenName);
            if (holder != null) holder.SetAsLastSibling();

            // アバター設定のメニューを空のメニューにする（MA が中身を入れる）
            Undo.RecordObject(avatar, undoName);
            avatar.expressionsMenu = EmptyMenu(avatar);
            EditorUtility.SetDirty(avatar);
            return root;
        }

        private static void BuildLevel(VRCExpressionsMenu menu, Transform parent, string prefix, int depth,
            HashSet<VRCExpressionsMenu> visiting, Dictionary<string, MenuEdit> edits, Dictionary<string, Transform> created)
        {
            if (menu == null || menu.controls == null) return;
            for (var i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null) continue;
                var key = prefix.Length == 0 ? i.ToString() : prefix + "." + i;

                var go = new GameObject(key);
                go.transform.SetParent(parent, false);
                created[key] = go.transform;
                var item = go.AddComponent<ModularAvatarMenuItem>();
                item.Control = Clone(control);
                // オブジェクト名は番号（元の場所）にしているので、メニューに出る名前は label に入れる
                MenuEdit edit;
                item.label = edits.TryGetValue(key, out edit) && !string.IsNullOrEmpty(edit.label) ? edit.label : control.name;

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    // 中の項目も 1 つずつ扱えるように、サブメニューも取り出す（同じメニューが何度も出てくるときは、そこで止める）
                    if (control.subMenu != null && depth < MaxDepth && !visiting.Contains(control.subMenu))
                    {
                        item.MenuSource = SubmenuSource.Children;
                        visiting.Add(control.subMenu);
                        BuildLevel(control.subMenu, go.transform, key, depth + 1, visiting, edits, created);
                        visiting.Remove(control.subMenu);
                    }
                    else
                    {
                        item.MenuSource = SubmenuSource.MenuAsset;
                    }
                }
            }
        }

        private static VRCExpressionsMenu.Control Clone(VRCExpressionsMenu.Control c)
        {
            return new VRCExpressionsMenu.Control
            {
                type = c.type != 0 ? c.type : VRCExpressionsMenu.Control.ControlType.Button,
                name = c.name,
                icon = c.icon,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = c.parameter != null ? c.parameter.name : "" },
                subMenu = c.subMenu,
                subParameters = c.subParameters != null
                    ? c.subParameters.Select(p => new VRCExpressionsMenu.Control.Parameter { name = p != null ? p.name : "" }).ToArray()
                    : new VRCExpressionsMenu.Control.Parameter[0],
                labels = c.labels != null ? c.labels.ToArray() : new VRCExpressionsMenu.Control.Label[0],
                style = c.style,
                value = c.value,
            };
        }

        /// <summary>使わない項目を置く場所。メニュー項目の部品を付けないので MA は中を見ない。念のため EditorOnly にする。</summary>
        private static Transform Holder(Transform menuRoot, string undoName)
        {
            var holder = menuRoot.Find(HiddenName);
            if (holder != null) return holder;
            var go = new GameObject(HiddenName) { tag = "EditorOnly" };
            if (undoName != null) Undo.RegisterCreatedObjectUndo(go, undoName);
            go.transform.SetParent(menuRoot, false);
            go.transform.SetAsLastSibling();
            return go.transform;
        }

        /// <summary>番号（"2.0.3"）から、元の上の段（"2.0"）のオブジェクトを探す。</summary>
        private static Transform ParentFor(Transform menuRoot, string key)
        {
            var dot = key.LastIndexOf('.');
            if (dot < 0) return menuRoot;
            var parentKey = key.Substring(0, dot);
            return menuRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == parentKey && x.GetComponent<ModularAvatarMenuItem>() != null);
        }

        /// <summary>元の番号の順になるところへ入れる（「その他」と使わない置き場はいつも最後）。</summary>
        private static void SortByKey(Transform parent, Transform t)
        {
            var mine = LastNumber(t.name);
            var index = 0;
            foreach (Transform sibling in parent)
            {
                if (sibling == t || sibling.name == OtherName || sibling.name == HiddenName) continue;
                if (LastNumber(sibling.name) < mine) index = sibling.GetSiblingIndex() + 1;
            }

            t.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
            var others = parent.Find(OtherName);
            if (others != null) others.SetAsLastSibling();
            var holder = parent.Find(HiddenName);
            if (holder != null) holder.SetAsLastSibling();
        }

        private static int LastNumber(string key)
        {
            var part = key.Substring(key.LastIndexOf('.') + 1);
            int n;
            return int.TryParse(part, out n) ? n : int.MaxValue;
        }

        private static VRCExpressionsMenu EmptyMenu(VRCAvatarDescriptor avatar)
        {
            var folder = WorkFolders.Ensure(WorkFolders.ForAvatar(avatar.gameObject) + "/Menus");
            var path = folder + "/EmptyMenu.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (existing != null) return existing;
            var empty = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            AssetDatabase.CreateAsset(empty, path);
            return empty;
        }

        /// <summary>取り出したメニューを消し、アバター設定のメニューを元に戻す。</summary>
        public static void RemoveMenu(VRCAvatarDescriptor avatar, string undoName)
        {
            var edits = ReadEdits(avatar);
            var root = avatar.transform.Find(RootName);
            if (root != null) Undo.DestroyObjectImmediate(root.gameObject);
            var original = edits != null ? AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(edits.originalMenu) : null;
            if (original == null) original = OriginalMenu(avatar);
            if (original == null) return;
            Undo.RecordObject(avatar, undoName);
            avatar.expressionsMenu = original;
            EditorUtility.SetDirty(avatar);
        }

        // ---------------------------------------------------------------- 状態を読む（バックアップ用）

        /// <summary>元のメニュー（取り出す前のもの）。改変アバターなら元アバターの設定から探す。</summary>
        public static VRCExpressionsMenu OriginalMenu(VRCAvatarDescriptor avatar)
        {
            var current = avatar.expressionsMenu;
            if (current != null && !AssetDatabase.GetAssetPath(current).StartsWith(WorkFolders.Root + "/")) return current;
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(avatar);
            return source != null ? source.expressionsMenu : null;
        }

        public static MenuEdits ReadEdits(VRCAvatarDescriptor avatar)
        {
            var original = OriginalMenu(avatar);
            var root = avatar.transform.Find(RootName);
            var edits = new MenuEdits
            {
                extracted = root != null,
                originalMenu = original != null ? AssetDatabase.GetAssetPath(original) : "",
            };
            if (root == null) return original == null ? null : edits;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var item = t.GetComponent<ModularAvatarMenuItem>();
                if (item == null || t == root || t.name == OtherName) continue;
                var hidden = t.parent != null && t.parent.name == HiddenName;
                var other = t.parent != null && t.parent.name == OtherName;
                edits.items.Add(new MenuEdit
                {
                    key = t.name,
                    label = item.label,
                    hidden = hidden,
                    other = other,
                    order = hidden ? -1 : t.GetSiblingIndex(),
                });
            }

            return edits;
        }
#endif
    }

    /// <summary>メニュー整理をバックアップに入れ、「元に戻す」で作り直す。</summary>
    internal sealed class MenuTidyBackup : IBackupParticipant
    {
        public string Key { get { return "menu-tidy"; } }

        public string Capture(VRCAvatarDescriptor avatar)
        {
#if KAIHENNAVI_MA
            var edits = MenuFeature.ReadEdits(avatar);
            return edits == null ? null : JsonUtility.ToJson(edits);
#else
            return null;
#endif
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
#if KAIHENNAVI_MA
            if (string.IsNullOrEmpty(json)) return;
            var edits = JsonUtility.FromJson<MenuEdits>(json);
            if (edits == null) return;
            if (edits.extracted) MenuFeature.BuildMenu(avatar, edits, undoName);
            else MenuFeature.RemoveMenu(avatar, undoName);
#endif
        }
    }
}
