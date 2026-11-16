using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 仕上げ：アイテムの置き場所。取り込んだ服やアクセを Assets/〈アバター〉_kaihen/〈用途〉/〈アイテム〉/ へ振り分ける設定と、
    /// Assets 直下に残っているアイテムの手動の振り分け。ファイルを動かすのは ImportSorter / ImportSortUndo（Ctrl+Z で戻る）。
    /// </summary>
    internal sealed class PlacementFeature : FeatureBase
    {
        private static string _newRootName;
        private static string _newCategory = "";
        private Action _onChanged;

        public override string Id { get { return "placement"; } }
        public override int Number { get { return 15; } }
        public override Color Accent { get { return Ui.Hex("#7D9DBE"); } }
        public override bool InTrial { get { return true; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            // 振り分けの窓で移したときや Ctrl+Z のときに、残っているアイテムの一覧を取り直す
            if (_onChanged != null) ImportSortUndo.Changed -= _onChanged;
            _onChanged = () => UnityEditor.EditorApplication.delayCall += () => { if (_onChanged != null) ctx.Rebuild(); };
            ImportSortUndo.Changed += _onChanged;
            root.Add(Ui.Notice(L10n.Tr("placement.intro"), "info"));

            var enabled = new Toggle(L10n.Tr("placement.enabled")) { value = ImportSorter.Enabled };
            enabled.AddToClassList("kn-field");
            enabled.RegisterValueChangedCallback(e => ImportSorter.Enabled = e.newValue);
            root.Add(enabled);
            root.Add(Ui.Text(L10n.Tr("placement.enabledHint"), "kn-hint"));

            // 振り分け先ルート
            if (_newRootName == null) _newRootName = ImportSortWindow.SuggestAvatarName(ctx.Avatar);
            var current = ImportSortWindow.BuildRootSection(root, false, () => _newRootName, n => _newRootName = n, ctx.Rebuild);
            if (ImportSorter.FindRoots().Count == 0)
            {
                var create = Ui.Box("kn-row");
                create.Add(Ui.Button(L10n.Tr("placement.createRoot"), () =>
                {
                    if (string.IsNullOrEmpty((_newRootName ?? "").Trim())) return;
                    var error = ImportSorter.CreateRoot(_newRootName);
                    if (error != null) UnityEditor.EditorUtility.DisplayDialog(L10n.Tr("app.name"), error, L10n.Tr("dialog.ok"));
                    else ctx.Tell(L10n.Tr("placement.rootCreated", ImportSorter.NewRootPath(_newRootName)));
                    ctx.Rebuild();
                }, "kn-button--primary"));
                root.Add(create);
            }

            BuildCategories(root, ctx, current);
            BuildRemaining(root, ctx);
            return root;
        }

        public override void OnClosed()
        {
            if (_onChanged != null) ImportSortUndo.Changed -= _onChanged;
            _onChanged = null;
        }

        /// <summary>用途の一覧（追加・並べ替え・名前変更・削除）と、ルート直下の未登録フォルダ。</summary>
        private static void BuildCategories(VisualElement root, FeatureContext ctx, string current)
        {
            root.Add(Ui.Heading(L10n.Tr("placement.categories")));
            root.Add(Ui.Text(L10n.Tr("placement.categoriesHint"), "kn-hint"));
            var categories = ImportSorter.Categories;
            var list = Ui.Box("kn-list");
            for (var i = 0; i < categories.Count; i++)
            {
                var index = i;
                var row = Ui.Box("kn-list__row");
                var field = new TextField { value = categories[i] };
                field.AddToClassList("kn-list__field");
                field.AddToClassList("kn-grow");
                field.tooltip = L10n.Tr("placement.renameTip");
                field.RegisterCallback<FocusOutEvent>(e =>
                {
                    var name = (field.value ?? "").Trim();
                    var others = categories.Where((c, j) => j != index);
                    if (name == categories[index]) return;
                    if (!ImportSorter.IsValidCategoryName(name, others))
                    {
                        ctx.Tell(L10n.Tr("placement.invalidName", name));
                        field.SetValueWithoutNotify(categories[index]);
                        return;
                    }
                    var next = ImportSorter.Categories;
                    next[index] = name;
                    ImportSorter.Categories = next;
                    ctx.Rebuild();
                });
                row.Add(field);
                var up = Ui.Button("↑", () => Swap(index, index - 1, ctx), "kn-button--small", "kn-button--ghost");
                up.tooltip = L10n.Tr("placement.up");
                up.SetEnabled(index > 0);
                row.Add(up);
                var down = Ui.Button("↓", () => Swap(index, index + 1, ctx), "kn-button--small", "kn-button--ghost");
                down.tooltip = L10n.Tr("placement.down");
                down.SetEnabled(index < categories.Count - 1);
                row.Add(down);
                row.Add(Ui.Button(L10n.Tr("placement.remove"), () =>
                {
                    var next = ImportSorter.Categories;
                    next.RemoveAt(index);
                    ImportSorter.Categories = next;
                    ctx.Rebuild();
                }, "kn-button--small", "kn-button--ghost"));
                list.Add(row);
            }
            root.Add(list);

            var add = Ui.Box("kn-row");
            var newField = new TextField { value = _newCategory };
            newField.AddToClassList("kn-list__field");
            newField.AddToClassList("kn-grow");
            newField.RegisterValueChangedCallback(e => _newCategory = e.newValue);
            add.Add(newField);
            add.Add(Ui.Button(L10n.Tr("placement.add"), () => AddCategory((_newCategory ?? "").Trim(), ctx), "kn-button--small"));
            add.Add(Ui.Button(L10n.Tr("placement.resetCategories"), () =>
            {
                ImportSorter.Categories = ImportSorter.DefaultCategories.ToList();
                ctx.Rebuild();
            }, "kn-button--small", "kn-button--ghost"));
            root.Add(add);

            // ルート直下にあって一覧に無いフォルダ
            var unregistered = ImportSorter.UnregisteredFolders(current, categories);
            if (unregistered.Count == 0) return;
            root.Add(Ui.Heading(L10n.Tr("placement.unregistered")));
            var folders = Ui.Box("kn-list");
            foreach (var name in unregistered)
            {
                var n = name;
                var row = Ui.Box("kn-list__row");
                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(n, "kn-list__title"));
                row.Add(texts);
                row.Add(Ui.Button(L10n.Tr("placement.addToList"), () => AddCategory(n, ctx), "kn-button--small"));
                folders.Add(row);
            }
            root.Add(folders);
        }

        private static void AddCategory(string name, FeatureContext ctx)
        {
            var categories = ImportSorter.Categories;
            if (!ImportSorter.IsValidCategoryName(name, categories))
            {
                ctx.Tell(L10n.Tr("placement.invalidName", name));
                return;
            }
            categories.Add(name);
            ImportSorter.Categories = categories;
            if (name == _newCategory) _newCategory = "";
            ctx.Rebuild();
        }

        private static void Swap(int a, int b, FeatureContext ctx)
        {
            var categories = ImportSorter.Categories;
            if (a < 0 || b < 0 || a >= categories.Count || b >= categories.Count) return;
            var t = categories[a];
            categories[a] = categories[b];
            categories[b] = t;
            ImportSorter.Categories = categories;
            ctx.Rebuild();
        }

        /// <summary>Assets 直下に残っているアイテムのフォルダと、手動の「振り分ける」。</summary>
        private static void BuildRemaining(VisualElement root, FeatureContext ctx)
        {
            root.Add(Ui.Heading(L10n.Tr("placement.remaining")));
            List<SortTarget> targets;
            try { targets = ImportSorter.BuildTargets(null); }
            catch (Exception e)
            {
                root.Add(Ui.Notice(L10n.Tr("panel.error", e.Message), "warn"));
                return;
            }
            if (targets.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("placement.noneRemaining"), "ok"));
                return;
            }
            root.Add(Ui.Text(L10n.Tr("placement.remainingHint"), "kn-hint"));
            var list = Ui.Box("kn-list");
            foreach (var target in targets)
            {
                var t = target;
                var row = Ui.Box("kn-list__row");
                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(t.ItemName, "kn-list__title"));
                var sub = string.IsNullOrEmpty(t.shop) ? L10n.Tr("sort.files", t.fileCount) : L10n.Tr("sort.shopFiles", t.ShopName, t.fileCount);
                if (t.risks.Count > 0) sub += " · " + L10n.Tr("placement.hasRisk");
                texts.Add(Ui.Text(sub, "kn-list__sub"));
                row.Add(texts);
                row.Add(Ui.Button(L10n.Tr("placement.sort"), () => ImportSortWindow.Open(new List<SortTarget> { t }), "kn-button--small"));
                list.Add(row);
            }
            root.Add(list);
            var all = Ui.Box("kn-row");
            all.Add(Ui.Button(L10n.Tr("placement.sortAll", targets.Count), () => ImportSortWindow.Open(targets), "kn-button--small", "kn-button--ghost"));
            root.Add(all);
        }
    }
}
