using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>商品単位の一覧項目。外部の商品と、実際に見つかった Prefab をまとめる。</summary>
    internal sealed class LibraryItem
    {
        public string Key;
        public KonoAssetItem Product;
        public string Name;
        public string Shop;
        public readonly List<string> Paths = new List<string>();
        public readonly List<string> Names = new List<string>();

        public bool Matches(string query)
        {
            return KonoAssetLink.Contains(Name, query) || KonoAssetLink.Contains(Shop, query) ||
                (Product != null && KonoAssetLink.Matches(Product, query)) ||
                Names.Concat(Paths).Any(n => KonoAssetLink.Contains(n, query));
        }

        public bool ForAvatar(IEnumerable<string> keywords)
        {
            return KonoAssetLink.SupportsAvatar(Product, keywords) ||
                Paths.Any(p => keywords.Any(k => KonoAssetLink.Contains(p, k)));
        }
    }

    /// <summary>服とアクセで共用するライブラリ。選択は詳細表示だけ、確定ボタンで初めて使う。</summary>
    internal static class ItemLibrary
    {
        private const int PageSize = 24;

        /// <summary>全商品の索引と台帳を結合する。未取り込みの商品も検索対象から落とさない。</summary>
        internal static List<LibraryItem> Collect(IEnumerable<KonoAssetItem> products, IEnumerable<LedgerPackage> packages, ItemKind kind)
        {
            var items = new Dictionary<string, LibraryItem>(StringComparer.Ordinal);
            foreach (var product in products)
            {
                if (product == null || product.description == null || string.IsNullOrEmpty(product.id) ||
                    product.sourceFile == "avatars.json" || product.sourceFile == "worldObjects.json") continue;
                items["kono:" + product.id] = new LibraryItem { Key = "kono:" + product.id, Product = product,
                    Name = product.description.name ?? "", Shop = product.description.creator ?? "" };
            }
            var ledgerKind = kind == ItemKind.Outfit ? LedgerItemKind.Outfit : LedgerItemKind.Accessory;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var package in packages.Where(p => p != null))
            {
                var product = ItemLedger.KonoItem(package);
                var key = product != null ? "kono:" + product.id : "folder:" + (package.commonFolder ?? package.packageName);
                foreach (var entry in package.prefabs ?? new List<LedgerPrefab>())
                {
                    if (entry == null || entry.kind != ledgerKind || string.IsNullOrEmpty(entry.path) || !seen.Add(entry.path)) continue;
                    LibraryItem item;
                    if (!items.TryGetValue(key, out item))
                    {
                        item = new LibraryItem { Key = key, Product = product,
                            Name = product == null ? package.packageName : product.description.name,
                            Shop = product == null ? "" : product.description.creator };
                        items[key] = item;
                    }
                    item.Paths.Add(entry.path);
                    item.Names.Add(package.packageName);
                    item.Names.Add(package.commonFolder);
                }
            }
            return items.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static VisualElement Build(VRCAvatarDescriptor avatar, ItemKind kind, Action<GameObject> onUse)
        {
            var root = new VisualElement();
            root.Add(Ui.Heading(L10n.Tr(kind == ItemKind.Outfit ? "library.outfits" : "library.accessories")));
            var prefix = "KaihenNavi.Library." + kind + ".";
            var keywords = ItemPicker.Keywords(avatar);
            var query = new TextField { label = L10n.Tr("library.search"), value = SessionState.GetString(prefix + "query", "") };
            root.Add(query);
            var filter = new VisualElement();
            root.Add(filter);
            var onlyAvatar = SessionState.GetString(prefix + "filter", "avatar") != "all";
            var controls = Ui.Box("kn-row");
            controls.Add(Ui.Text(KonoAssetLink.IsAvailable ? L10n.Tr("kono.connected", KonoAssetLink.Count) : L10n.Tr("kono.notFound"), "kn-hint"));
            controls.Add(Ui.Button(L10n.Tr("kono.reload"), () =>
            {
                KonoAssetLink.Reload();
                ItemLedger.Invalidate();
            }, "kn-button--small", "kn-button--ghost"));
            root.Add(controls);
            var settings = new Foldout { text = L10n.Tr("library.settings"), value = !KonoAssetLink.IsAvailable };
            settings.Add(Ui.Text(L10n.Tr("library.folderHint"), "kn-hint"));
            settings.Add(Ui.Button(L10n.Tr("kono.chooseFolder"), KonoAssetLink.ChooseFolder, "kn-button--small", "kn-button--ghost"));
            root.Add(settings);

            // 発見は読み取りのみ。検索・服の装着では台帳ファイルを変更しない。
            var packages = ItemLedger.ProjectItems();
            var items = Collect(KonoAssetLink.All, packages, kind);
            foreach (var item in items) item.Paths.RemoveAll(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) == null);
            items.RemoveAll(i => i.Product == null && i.Paths.Count == 0);
            var results = Ui.Box("kn-list");
            root.Add(results);
            var page = 0;
            Action render = null;
            Action renderFilter = null;
            renderFilter = () =>
            {
                filter.Clear();
                filter.Add(Ui.Chips(new[] { L10n.Tr("library.forAvatar"), L10n.Tr("library.all") }, onlyAvatar ? 0 : 1, index =>
                {
                    onlyAvatar = index == 0;
                    SessionState.SetString(prefix + "filter", onlyAvatar ? "avatar" : "all");
                    page = 0;
                    renderFilter();
                    render();
                }));
            };
            render = () =>
            {
                results.Clear();
                var matches = items.Where(i => i.Matches(query.value) && (!onlyAvatar || i.ForAvatar(keywords))).ToList();
                if (matches.Count == 0)
                {
                    results.Add(Ui.Text(L10n.Tr("library.empty"), "kn-hint"));
                    return;
                }
                page = Math.Min(page, (matches.Count - 1) / PageSize);
                var selected = SessionState.GetString(prefix + "selected", "");
                foreach (var item in matches.Skip(page * PageSize).Take(PageSize))
                {
                    var row = new Button(() =>
                    {
                        SessionState.SetString(prefix + "selected", selected == item.Key ? "" : item.Key);
                        render();
                    });
                    row.AddToClassList("kn-list__row");
                    row.AddToClassList("kn-suggest__row");
                    if (item.Key == selected) row.AddToClassList("kn-suggest__row--selected");
                    row.Add(Thumbnail(item, false));
                    var texts = Ui.Box("kn-list__texts");
                    texts.Add(Ui.Text(item.Name, "kn-list__title"));
                    texts.Add(Ui.Text(item.Shop, "kn-list__sub"));
                    row.Add(texts);
                    results.Add(row);
                    if (item.Key == selected) results.Add(Details(item, kind, keywords, onUse));
                }
                if (matches.Count <= PageSize) return;
                var pages = Ui.Box("kn-row");
                var previous = Ui.Button(L10n.Tr("library.previous"), () => { page--; render(); }, "kn-button--small");
                previous.SetEnabled(page > 0);
                pages.Add(previous);
                pages.Add(Ui.Text(L10n.Tr("library.page", page + 1, (matches.Count + PageSize - 1) / PageSize, matches.Count), "kn-hint"));
                var next = Ui.Button(L10n.Tr("library.next"), () => { page++; render(); }, "kn-button--small");
                next.SetEnabled((page + 1) * PageSize < matches.Count);
                pages.Add(next);
                results.Add(pages);
            };
            query.RegisterValueChangedCallback(e =>
            {
                SessionState.SetString(prefix + "query", e.newValue ?? "");
                page = 0;
                render();
            });
            // インポートやドメインリロード後も、選んだ商品のあるページを開く。
            var restored = items.Where(i => i.Matches(query.value) && (!onlyAvatar || i.ForAvatar(keywords))).ToList();
            var selectedIndex = restored.FindIndex(i => i.Key == SessionState.GetString(prefix + "selected", ""));
            if (selectedIndex >= 0) page = selectedIndex / PageSize;
            renderFilter();
            render();
            return root;
        }

        private static VisualElement Thumbnail(LibraryItem item, bool large)
        {
            var thumb = Ui.Box("kn-suggest__thumb");
            if (large) thumb.AddToClassList("kn-library__image");
            var prefab = item.Paths.Count == 0 ? null : AssetDatabase.LoadAssetAtPath<GameObject>(item.Paths[0]);
            var texture = KonoAssetLink.Thumbnail(item.Product);
            if (texture == null && prefab != null)
            {
                texture = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                // Prefab のプレビュー生成は非同期。最初に間に合わなくても、出来上がった画像に切り替える。
                var attempts = 0;
                IVisualElementScheduledItem preview = null;
                preview = thumb.schedule.Execute(() =>
                {
                    var image = prefab == null ? null : AssetPreview.GetAssetPreview(prefab);
                    if (image != null) thumb.style.backgroundImage = new StyleBackground(image);
                    if (image != null || ++attempts >= 20) preview.Pause();
                }).Every(150);
            }
            if (texture != null) thumb.style.backgroundImage = new StyleBackground(texture);
            return thumb;
        }

        private static VisualElement Details(LibraryItem item, ItemKind kind, List<string> keywords, Action<GameObject> onUse)
        {
            var detail = Ui.Box("kn-library__detail");
            detail.Add(Thumbnail(item, true));
            detail.Add(Ui.Text(item.Name, "kn-list__title"));
            detail.Add(Ui.Text(item.Shop, "kn-list__sub"));
            var paths = item.Paths.OrderByDescending(p => keywords.Any(k => KonoAssetLink.Contains(p, k)))
                .ThenBy(p => p, StringComparer.Ordinal).ToList();
            if (paths.Count > 0)
            {
                var chosen = paths[0];
                var labels = paths.Select(p => p.Substring("Assets/".Length)).ToList();
                var popup = new PopupField<string>(labels, 0) { label = L10n.Tr(kind == ItemKind.Outfit ? "library.prefab" : "library.accessoryPrefab") };
                detail.Add(popup);
                var location = Ui.Text(chosen, "kn-hint");
                detail.Add(location);
                popup.RegisterValueChangedCallback(e =>
                {
                    chosen = paths[labels.IndexOf(e.newValue)];
                    location.text = chosen;
                });
                if (kind == ItemKind.Accessory) detail.Add(Ui.Text(L10n.Tr("library.attachHint"), "kn-hint"));
                var actions = Ui.Box("kn-row");
                actions.Add(Ui.Button(L10n.Tr("common.where"), () =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(chosen);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }, "kn-button--small", "kn-button--ghost"));
                actions.Add(Ui.Button(L10n.Tr(kind == ItemKind.Outfit ? "outfit.putOn" : "attach.do"), () =>
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(chosen);
                    if (prefab != null) onUse(prefab);
                }, "kn-button--primary"));
                detail.Add(actions);
            }
            else
            {
                detail.Add(Ui.Text(L10n.Tr("library.notImported"), "kn-hint"));
                var files = KonoAssetLink.Packages(item.Product);
                if (files.Count == 0) detail.Add(Ui.Text(L10n.Tr("library.noPackage"), "kn-hint"));
                else
                {
                    var file = files[0];
                    var labels = files.Select(p => p.Substring(Path.GetDirectoryName(files[0]).Length + 1)).ToList();
                    // 別フォルダのファイルは絶対パスも表示し、同名の版を取り違えない。
                    if (files.Select(Path.GetDirectoryName).Distinct().Count() > 1) labels = files.ToList();
                    var popup = new PopupField<string>(labels, 0) { label = L10n.Tr("library.package") };
                    popup.RegisterValueChangedCallback(e => file = files[labels.IndexOf(e.newValue)]);
                    detail.Add(popup);
                    detail.Add(Ui.Button(L10n.Tr("library.import"), () =>
                    {
                        // 商品 ID と選択状態は SessionState にあるため、スクリプトを含む取り込み後も復元できる。
                        ItemLedger.ImportProduct(item.Product, file);
                    }, "kn-button--primary"));
                }
            }
            return detail;
        }
    }
}
