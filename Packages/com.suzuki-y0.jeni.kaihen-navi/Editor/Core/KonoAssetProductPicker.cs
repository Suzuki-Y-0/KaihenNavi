using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>商品を検索して台帳の1件と対応付ける小さな選択画面。</summary>
    internal sealed class KonoAssetProductPicker : EditorWindow
    {
        private Action<KonoAssetItem> _onPicked;

        public static void Open(Action<KonoAssetItem> onPicked)
        {
            var window = CreateInstance<KonoAssetProductPicker>();
            window._onPicked = onPicked;
            window.titleContent = new GUIContent(L10n.Tr("kono.selectProduct"));
            window.minSize = new Vector2(360, 320);
            window.ShowUtility();
            window.Build();
        }

        private void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            var field = new TextField { label = L10n.Tr("kono.searchProducts") };
            root.Add(field);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1;
            root.Add(list);
            Action refresh = () =>
            {
                list.Clear();
                var query = (field.value ?? "").Trim();
                var matches = KonoAssetLink.All.Where(i => query.Length == 0 ||
                    KonoAssetLink.Matches(i, query))
                    .OrderBy(i => i.description.name, StringComparer.Ordinal).ToList();
                if (matches.Count == 0) list.Add(Ui.Text(L10n.Tr("kono.noProducts"), "kn-hint"));
                // 全商品の一覧を検索可能にする。候補を勝手に省略しない。
                foreach (var item in matches)
                {
                    var product = item;
                    list.Add(Ui.Button(L10n.Tr("kono.productCredit", item.description.name ?? "", item.description.creator ?? ""), () =>
                    {
                        if (_onPicked != null) _onPicked(product);
                        Close();
                    }));
                }
            };
            field.RegisterValueChangedCallback(e => refresh());
            refresh();
        }
    }
}
