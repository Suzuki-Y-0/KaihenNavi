using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 取り込み直後に出す小さな窓「アイテムの振り分け」。取り込んだアイテムごとに用途を選び、まとめて移す。
    /// 見た目はナビと同じ（UI Toolkit・KaihenNavi.uss・Ui の部品）。対象はドメインリロードをまたいで保つ。
    /// </summary>
    internal sealed class ImportSortWindow : EditorWindow
    {
        [SerializeField] private List<SortTarget> _targets = new List<SortTarget>();
        [SerializeField] private string _newRootName;

        /// <summary>対象を加えて開く。すでに開いていれば、まだ載っていない対象だけ足す。</summary>
        internal static ImportSortWindow Open(List<SortTarget> targets)
        {
            var window = GetWindow<ImportSortWindow>(true, L10n.Tr("sort.title"), true);
            if (window == null) return null;
            foreach (var target in targets ?? new List<SortTarget>())
                if (target != null && !window._targets.Any(t => t.source == target.source)) window._targets.Add(target);
            window.minSize = new Vector2(440, 260);
            window.ShowUtility();
            window.Build();
            return window;
        }

        /// <summary>テストや一時スクリプトから：いま載っている対象。</summary>
        internal List<SortTarget> Targets { get { return _targets; } }

        private void OnEnable()
        {
            ImportSortUndo.Changed += Build;
            L10n.Changed += Build;
        }

        private void OnDisable()
        {
            ImportSortUndo.Changed -= Build;
            L10n.Changed -= Build;
            if (rootVisualElement != null) FontLoader.Release(rootVisualElement);
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Build()
        {
            var root = rootVisualElement;
            if (root == null) return;
            titleContent = new GUIContent(L10n.Tr("sort.title"));
            root.Clear();
            root.AddToClassList("kn-root");
            root.EnableInClassList("kn-dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("kn-light", !EditorGUIUtility.isProSkin);
            FontLoader.Apply(root, L10n.Current);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackagePaths.StyleSheet);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);

            // もう無くなった対象（Ctrl+Z の取り込みの取り消しなど）は出さない
            _targets.RemoveAll(t => t == null || !System.IO.Directory.Exists(t.source));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("kn-sort-scroll");
            var body = Ui.Box("kn-sort");
            scroll.Add(body);
            root.Add(scroll);

            body.Add(Ui.Notice(L10n.Tr("sort.intro"), "info"));
            if (_newRootName == null) _newRootName = SuggestAvatarName(CurrentNaviAvatar());
            Button move = null;
            var chosen = _targets.Count(t => !string.IsNullOrEmpty(t.choice) && t.choice != SortTarget.NotMove);
            BuildRootSection(body, true, () => _newRootName, n =>
            {
                _newRootName = n;
                if (move != null) move.SetEnabled(TargetRoot() != null && chosen > 0);
            }, Build);

            if (_targets.Count == 0)
            {
                body.Add(Ui.Notice(L10n.Tr("sort.nothing"), "ok"));
                var close = Ui.Box("kn-row");
                close.Add(Ui.Button(L10n.Tr("sort.close"), Close));
                body.Add(close);
                return;
            }

            var categories = ImportSorter.Categories;
            var labels = categories.Concat(new[] { L10n.Tr("sort.notMove") }).ToList();
            body.Add(Ui.Heading(L10n.Tr("sort.items", _targets.Count)));
            var list = Ui.Box("kn-list");
            foreach (var target in _targets)
            {
                var t = target;
                var row = Ui.Box("kn-list__row", "kn-sort-row");
                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(t.ItemName, "kn-list__title"));
                texts.Add(Ui.Text(string.IsNullOrEmpty(t.shop) ? L10n.Tr("sort.files", t.fileCount)
                    : L10n.Tr("sort.shopFiles", t.ShopName, t.fileCount), "kn-list__sub"));
                row.Add(texts);
                foreach (var risk in t.risks) row.Add(Ui.Notice(L10n.Tr("sort.risk." + RiskKey(risk)), "warn"));
                var selected = t.choice == SortTarget.NotMove ? labels.Count - 1
                    : categories.FindIndex(c => string.Equals(c, t.choice, StringComparison.OrdinalIgnoreCase));
                row.Add(Ui.Chips(labels, selected, i =>
                {
                    t.choice = i == labels.Count - 1 ? SortTarget.NotMove : categories[i];
                    Build();
                }));
                list.Add(row);
            }
            body.Add(list);

            if (ImportSorter.FindRoots().Count > 1 && TargetRoot() == null) body.Add(Ui.Text(L10n.Tr("sort.chooseRoot"), "kn-hint"));
            var buttons = Ui.Box("kn-row");
            move = Ui.Button(L10n.Tr("sort.moveAll", chosen), () => MoveChosen(TargetRoot()), "kn-button--primary");
            move.SetEnabled(TargetRoot() != null && chosen > 0);
            buttons.Add(move);
            buttons.Add(Ui.Button(L10n.Tr("sort.later"), Close, "kn-button--ghost"));
            body.Add(buttons);
            body.Add(Ui.Text(L10n.Tr("sort.laterHint"), "kn-hint"));
        }

        /// <summary>移す先のルート。無ければ入力したアバター名で作る予定のパス。複数あって選んでいなければ null。</summary>
        internal string TargetRoot()
        {
            var roots = ImportSorter.FindRoots();
            if (roots.Count > 0) return ImportSorter.ChooseRoot(roots, ImportSorter.SavedRoot);
            return string.IsNullOrEmpty((_newRootName ?? "").Trim()) ? null : ImportSorter.NewRootPath(_newRootName);
        }

        /// <summary>選んだものをまとめて移す。終わったら閉じる（選ばなかったものはそのまま残る）。</summary>
        internal void MoveChosen(string targetRoot)
        {
            var choices = _targets.Where(t => !string.IsNullOrEmpty(t.choice) && t.choice != SortTarget.NotMove)
                .Select(t => new KeyValuePair<SortTarget, string>(t, t.choice)).ToList();
            int moved;
            var error = ImportSorter.Move(targetRoot, choices, out moved);
            if (error != null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), error, L10n.Tr("dialog.ok"));
                Build();
                return;
            }
            if (!string.IsNullOrEmpty(targetRoot) && ImportSorter.FindRoots().Count > 1) ImportSorter.SavedRoot = targetRoot;
            Debug.Log("[KaihenNavi] " + L10n.Tr("sort.done", moved));
            Close();
        }

        private static string RiskKey(SortRisk risk)
        {
            return risk == SortRisk.Script ? "script" : risk == SortRisk.ShaderInclude ? "shader" : "folder";
        }

        // ---------------------------------------------------------------- 窓とページで共通の部品

        /// <summary>
        /// 振り分け先ルートの表示と選択。1 つなら表示だけ、複数なら選ぶチップ（選んだものをプロジェクトごとに覚える）、
        /// 無ければアバター名の入力欄。戻り値はいまのルート（無い・決まらなければ null）。
        /// </summary>
        internal static string BuildRootSection(VisualElement parent, bool createOnMove, Func<string> getName, Action<string> setName, Action rebuild)
        {
            parent.Add(Ui.Heading(L10n.Tr("sort.root")));
            var roots = ImportSorter.FindRoots();
            var current = ImportSorter.ChooseRoot(roots, ImportSorter.SavedRoot);
            if (roots.Count == 1)
            {
                parent.Add(Ui.Text(roots[0], "kn-hint"));
                return roots[0];
            }
            if (roots.Count > 1)
            {
                parent.Add(Ui.Text(L10n.Tr("sort.manyRoots"), "kn-hint"));
                parent.Add(Ui.Chips(roots.Select(r => r.Substring("Assets/".Length)).ToList(), roots.IndexOf(current), i =>
                {
                    ImportSorter.SavedRoot = roots[i];
                    rebuild();
                }));
                return current;
            }
            parent.Add(Ui.Notice(L10n.Tr(createOnMove ? "sort.noRootCreate" : "sort.noRootPage"), "warn"));
            var field = new TextField { value = getName() ?? "" };
            field.AddToClassList("kn-list__field");
            parent.Add(field);
            var where = Ui.Text(L10n.Tr("sort.willCreate", ImportSorter.NewRootPath(getName())), "kn-hint");
            parent.Add(where);
            field.RegisterValueChangedCallback(e =>
            {
                setName(e.newValue);
                where.text = L10n.Tr("sort.willCreate", ImportSorter.NewRootPath(e.newValue));
            });
            return null;
        }

        /// <summary>ナビでいま選んでいるアバター（ナビが開いていなければ null）。</summary>
        internal static VRCAvatarDescriptor CurrentNaviAvatar()
        {
            var window = Resources.FindObjectsOfTypeAll<KaihenNaviWindow>().FirstOrDefault();
            return window == null ? null : window.SelectedAvatar;
        }

        /// <summary>ルートの名前に使うアバター名。改変アバターなら元の Prefab の置き場所（Assets/Cazalis/... → Cazalis）。</summary>
        internal static string SuggestAvatarName(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return "";
            var path = MyAvatar.PrefabPath(avatar);
            if (path != null && (MyAvatar.IsMinePath(path) || MyAvatar.IsBasePath(path)))
            {
                var original = MyAvatar.OriginalOf(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                path = original == null ? null : AssetDatabase.GetAssetPath(original);
            }
            return ImportSorter.AvatarNameFromPath(path, avatar.name);
        }
    }
}
