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
    internal enum ItemKind
    {
        /// <summary>体の骨（Hips など）を持つ衣装。</summary>
        Outfit,

        /// <summary>髪飾り・耳・メガネなど、骨のどこかに付けるもの。</summary>
        Accessory,

        /// <summary>アバター本体や、見た目を持たないもの。</summary>
        Other,
    }

    /// <summary>
    /// 服やアクセの Prefab を選ぶ部品。Project からドラッグして入れるか、このアバター用らしい候補から選ぶ。
    /// 初心者がヒエラルキーの親子関係を触らなくて済むようにするための入口。
    /// </summary>
    internal static class ItemPicker
    {
        private const int MaxSuggestions = 24;

        /// <summary>Prefab の中身から、服かアクセかを見分ける。</summary>
        public static ItemKind KindOf(GameObject prefab)
        {
            if (prefab == null || prefab.GetComponent<VRCAvatarDescriptor>() != null) return ItemKind.Other;
            if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0) return ItemKind.Other;

            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name.ToLowerInvariant();
                if (name == "hips" || name.EndsWith("_hips") || name.EndsWith(".hips") || name.Contains("j_bip_c_hips")) return ItemKind.Outfit;
            }

            return ItemKind.Accessory;
        }

        /// <summary>ドラッグして入れる枠。種類が違う Prefab を入れたときは、そのことを伝える。</summary>
        public static VisualElement DropZone(string hint, ItemKind kind, Action<GameObject> onPicked)
        {
            var zone = Ui.Box("kn-drop");
            zone.Add(Ui.Text(hint, "kn-drop__text"));

            zone.RegisterCallback<DragUpdatedEvent>(e =>
            {
                DragAndDrop.visualMode = PickFromDrag() != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                zone.AddToClassList("kn-drop--over");
            });
            zone.RegisterCallback<DragLeaveEvent>(e => zone.RemoveFromClassList("kn-drop--over"));
            zone.RegisterCallback<DragPerformEvent>(e =>
            {
                zone.RemoveFromClassList("kn-drop--over");
                var prefab = PickFromDrag();
                if (prefab == null) return;
                DragAndDrop.AcceptDrag();
                Use(prefab, kind, onPicked);
            });

            // ドラッグが難しいとき用：一覧から選ぶ（Unity 標準の選択画面が開く）
            zone.Add(PickRow(typeof(GameObject), o =>
            {
                var prefab = o as GameObject;
                if (prefab != null) Use(prefab, kind, onPicked);
            }));

            return zone;
        }

        /// <summary>
        /// 一覧から選ぶ欄と「これにする」ボタン。Unity の選択画面は 1 回クリックしただけで値が入るので、
        /// 選んだだけでは何もせず、ボタンを押したときに使う（選び直している途中で服を何着も着せないように）。
        /// </summary>
        public static VisualElement PickRow(Type type, Action<UnityEngine.Object> onUse)
        {
            var row = Ui.Box("kn-row", "kn-drop__pick");
            var field = new ObjectField(L10n.Tr("pick.orChoose")) { objectType = type, allowSceneObjects = false };
            field.AddToClassList("kn-drop__field");
            row.Add(field);
            var button = Ui.Button(L10n.Tr("pick.use"), () =>
            {
                var value = field.value;
                if (value == null) return;
                field.SetValueWithoutNotify(null);
                onUse(value);
            }, "kn-button--small", "kn-button--primary");
            button.SetEnabled(false);
            field.RegisterValueChangedCallback(e => button.SetEnabled(e.newValue != null));
            row.Add(button);
            return row;
        }

        private static void Use(GameObject prefab, ItemKind kind, Action<GameObject> onPicked)
        {
            var actual = KindOf(prefab);
            if (actual != kind && actual != ItemKind.Other)
            {
                var key = actual == ItemKind.Outfit ? "pick.looksOutfit" : "pick.looksAccessory";
                if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr(key, prefab.name), L10n.Tr("pick.useAnyway"), L10n.Tr("dialog.cancel"))) return;
            }

            onPicked(prefab);
        }

        /// <summary>ドラッグ中のもののうち、Project にある Prefab。</summary>
        private static GameObject PickFromDrag()
        {
            foreach (var o in DragAndDrop.objectReferences)
            {
                var go = o as GameObject;
                if (go == null) continue;
                var path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab")) return go;

                // シーンに置いたものをドラッグしてきた場合は、その元の Prefab を使う
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (source != null) return source;
                }
            }

            return null;
        }

        /// <summary>
        /// このアバター用らしい候補。Prefab の場所や名前に、元のアバターの名前が入っているものを集める
        /// （BOOTH の服やアクセは「アバター名」入りのフォルダに対応版が入っていることが多い）。
        /// 台帳に記録された同じ種類のものも加え、アバター名が合うものから先に並べる。
        /// </summary>
        public static List<GameObject> Suggestions(VRCAvatarDescriptor avatar, ItemKind kind)
        {
            var result = new List<GameObject>();
            if (avatar == null) return result;

            var keywords = Keywords(avatar);
            var konoAvailable = KonoAssetLink.IsAvailable;
            var ledgerKind = kind == ItemKind.Outfit ? LedgerItemKind.Outfit
                : kind == ItemKind.Accessory ? LedgerItemKind.Accessory : LedgerItemKind.Other;
            var ledgerPaths = ItemLedger.All().Where(p => p != null && p.prefabs != null)
                .SelectMany(p => p.prefabs).Where(p => p != null && p.kind == ledgerKind)
                .Select(p => p.path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            var matchingPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => keywords.Any(k => p.ToLowerInvariant().Contains(k)));
            var paths = matchingPaths.Concat(ledgerPaths).Distinct()
                .Where(p => !p.StartsWith(WorkFolders.Root + "/", StringComparison.Ordinal))
                .OrderByDescending(p => konoAvailable && KonoAssetLink.SupportsAvatar(ItemLedger.KonoItem(ItemLedger.RecordOf(p)), keywords))
                .ThenByDescending(p => keywords.Any(k => p.ToLowerInvariant().Contains(k)))
                .ThenBy(p => p, StringComparer.Ordinal);

            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || KindOf(prefab) != kind) continue;
                if (IsSameAsAvatar(prefab, avatar)) continue;

                result.Add(prefab);
                if (result.Count >= MaxSuggestions) break;
            }

            return result;
        }

        private static readonly Dictionary<ItemKind, string> OwnedQueries = new Dictionary<ItemKind, string>();

        /// <summary>台帳から名前で探す共通 UI。入力中は台帳のファイルや全 Prefab を読み直さない。</summary>
        public static VisualElement OwnedItems(ItemKind kind, Action<GameObject> onPicked)
        {
            var root = new VisualElement();
            root.Add(Ui.Heading(L10n.Tr("pick.owned")));
            var connection = Ui.Box("kn-row");
            root.Add(connection);
            Action refreshConnection = () =>
            {
                connection.Clear();
                connection.Add(Ui.Text(KonoAssetLink.IsAvailable ? L10n.Tr("kono.connected", KonoAssetLink.Count)
                    : L10n.Tr("kono.notFound"), "kn-hint"));
                connection.Add(Ui.Button(L10n.Tr("kono.chooseFolder"), KonoAssetLink.ChooseFolder,
                    "kn-button--small", "kn-button--ghost"));
                if (KonoAssetLink.IsAvailable)
                    connection.Add(Ui.Button(L10n.Tr("kono.reload"), KonoAssetLink.Reload,
                        "kn-button--small", "kn-button--ghost"));
            };
            refreshConnection();
            // アクセを足すと画面が作り直されるので、入力した言葉は種類ごとに覚えておく
            string remembered;
            var field = new TextField { value = OwnedQueries.TryGetValue(kind, out remembered) ? remembered : "" };
            field.label = L10n.Tr("pick.searchName");
            root.Add(field);
            var controls = new VisualElement();
            root.Add(controls);
            var results = new VisualElement();
            root.Add(results);
            var ledgerKind = kind == ItemKind.Outfit ? LedgerItemKind.Outfit
                : kind == ItemKind.Accessory ? LedgerItemKind.Accessory : LedgerItemKind.Other;

            Action refresh = () =>
            {
                results.Clear();
                var query = (field.value ?? "").Trim();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var items = new List<GameObject>();
                foreach (var package in ItemLedger.All())
                {
                    if (package == null || package.prefabs == null) continue;
                    var product = ItemLedger.KonoItem(package);
                    foreach (var entry in package.prefabs)
                    {
                        if (entry == null || entry.kind != ledgerKind || string.IsNullOrEmpty(entry.path)) continue;
                        // パスには Prefab 名と途中のフォルダ名が含まれる。
                        if (query.Length > 0 && !ContainsName(entry.path, query) &&
                            !ContainsName(package.packageName, query) && !ContainsName(package.commonFolder, query) &&
                            !(product != null && (ContainsName(product.description.name, query) || ContainsName(product.description.creator, query)))) continue;
                        if (!seen.Add(entry.path)) continue;
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
                        if (prefab == null) continue;
                        items.Add(prefab);
                        if (items.Count >= 30) break;
                    }
                    if (items.Count >= 30) break;
                }
                if (items.Count > 0) results.Add(SuggestionList(items, onPicked));
                else results.Add(Ui.Text(L10n.Tr("pick.noOwnedResults"), "kn-hint"));
            };
            Action rebuildControls = null;
            rebuildControls = () =>
            {
                controls.Clear();
                var empty = !ItemLedger.All().Any(p => p != null && p.prefabs != null && p.prefabs.Count > 0);
                controls.Add(Ui.Button(L10n.Tr(empty ? "pick.scanExisting" : "pick.rescan"), () =>
                {
                    ItemLedger.ScanExisting();
                    rebuildControls();
                    refresh();
                }, "kn-button--small", "kn-button--ghost"));
            };
            field.RegisterValueChangedCallback(e =>
            {
                OwnedQueries[kind] = e.newValue ?? "";
                refresh();
            });
            rebuildControls();
            refresh();
            return root;
        }

        private static bool ContainsName(string value, string query)
        {
            return value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>候補を並べる。押すと onPicked が呼ばれる。</summary>
        public static VisualElement SuggestionList(List<GameObject> items, Action<GameObject> onPicked)
        {
            var list = Ui.Box("kn-list", "kn-suggest");
            Action build = () =>
            {
                list.Clear();
                foreach (var item in items)
                {
                    var prefab = item;
                    var package = ItemLedger.RecordOf(AssetDatabase.GetAssetPath(prefab));
                    var product = ItemLedger.KonoItem(package);
                    var container = Ui.Box("kn-kono-row");
                    var row = new Button(() => onPicked(prefab));
                    row.AddToClassList("kn-list__row");
                    row.AddToClassList("kn-suggest__row");
                    row.style.flexGrow = 1;
                    var thumb = Ui.Box("kn-suggest__thumb");
                    var texture = KonoAssetLink.Thumbnail(product) ?? AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                    if (texture != null) thumb.style.backgroundImage = new StyleBackground(texture);
                    row.Add(thumb);

                    var texts = Ui.Box("kn-list__texts");
                    texts.Add(Ui.Text(prefab.name, "kn-list__title"));
                    if (product != null)
                        texts.Add(Ui.Text(L10n.Tr("kono.productCredit", product.description.name ?? "", product.description.creator ?? ""), "kn-list__sub"));
                    texts.Add(Ui.Text(ShortFolder(prefab), "kn-list__sub"));
                    if (product == null && package != null && !string.IsNullOrEmpty(package.packageName))
                        texts.Add(Ui.Text(L10n.Tr("pick.fromPackage", package.packageName), "kn-list__sub"));
                    row.Add(texts);
                    container.Add(row);
                    if (package != null || KonoAssetLink.IsAvailable)
                    {
                        // 行を選ぶボタンとメニューを兄弟にし、メニュー操作で服を着せない。
                        var menuButton = Ui.Button(L10n.Tr("kono.more"), () =>
                        {
                            var menu = new GenericMenu();
                            if (KonoAssetLink.IsAvailable)
                                menu.AddItem(new GUIContent(L10n.Tr("kono.selectProduct")), false, () =>
                                    KonoAssetProductPicker.Open(selected => ItemLedger.SetKonoItemForPath(AssetDatabase.GetAssetPath(prefab), selected)));
                            else menu.AddDisabledItem(new GUIContent(L10n.Tr("kono.selectProduct")));
                            if (product != null || (package != null && !string.IsNullOrEmpty(package.konoAssetId)))
                                menu.AddItem(new GUIContent(L10n.Tr("kono.unlink")), false, () => ItemLedger.SetKonoItem(package, null));
                            menu.ShowAsContext();
                        }, "kn-button--small", "kn-button--ghost");
                        menuButton.tooltip = L10n.Tr("kono.selectProduct");
                        container.Add(menuButton);
                    }
                    list.Add(container);
                }
            };
            build();

            return list;
        }

        /// <summary>候補を探すときに使う、元のアバターの名前（小文字）。</summary>
        private static List<string> Keywords(VRCAvatarDescriptor avatar)
        {
            var names = new List<string> { avatar.name };
            var path = MyAvatar.PrefabPath(avatar);
            if (path != null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var original = MyAvatar.IsMinePath(path) || MyAvatar.IsBasePath(path) ? MyAvatar.OriginalOf(prefab) : prefab;
                if (original != null) names.Add(original.name);
            }

            var result = new List<string>();
            foreach (var n in names)
            {
                // 「Cazalis_Kisekae」「Cazalis Variant」などから、最初の語（Cazalis）を取り出す
                var head = n.Split(new[] { '_', ' ', '-', '(', '（', '.' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (head != null && head.Length >= 3) result.Add(head.ToLowerInvariant());
            }

            return result.Distinct().ToList();
        }

        private static bool IsSameAsAvatar(GameObject prefab, VRCAvatarDescriptor avatar)
        {
            var path = MyAvatar.PrefabPath(avatar);
            return path != null && AssetDatabase.GetAssetPath(prefab) == path;
        }

        private static string ShortFolder(GameObject prefab)
        {
            var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)) ?? "";
            dir = dir.Replace('\\', '/');
            if (dir.StartsWith("Assets/")) dir = dir.Substring("Assets/".Length);
            return dir;
        }
    }
}
