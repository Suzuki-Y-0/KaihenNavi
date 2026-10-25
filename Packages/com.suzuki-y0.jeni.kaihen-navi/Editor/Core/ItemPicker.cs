using System;
using System.Collections.Generic;
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
    /// 服やアクセの Prefab を Project から選ぶ補助部品。通常は商品のライブラリを使う。
    /// 初心者がヒエラルキーの親子関係を触らなくて済むようにするための入口。
    /// </summary>
    internal static class ItemPicker
    {

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
            Ui.DashedBorder(zone);
            var icon = Ui.NavigationIcon("uploadCheck");
            icon.AddToClassList("kn-drop__icon");
            zone.Add(icon);
            var texts = Ui.Box("kn-drop__texts");
            texts.Add(Ui.Text(hint, "kn-drop__text"));
            texts.Add(Ui.Text(L10n.Tr("pick.dropHint"), "kn-drop__hint"));
            zone.Add(texts);

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

            // 選択だけでは装着しない。既存の確定ボタンを補助画面でも使う。
            var choose = Ui.Button(L10n.Tr("pick.file"), null, "kn-button--small", "kn-button--ghost");
            choose.clicked += () => PrefabPickerWindow.Open(prefab =>
            {
                // 選択中にページやアバターを切り替えていたら、古い対象には装着しない。
                if (zone.panel != null) Use(prefab, kind, onPicked);
            });
            zone.Add(choose);

            return zone;
        }

        /// <summary>Project の Prefab を選び、確定してから呼び出し元に渡す小さな画面。</summary>
        private sealed class PrefabPickerWindow : EditorWindow
        {
            public static void Open(Action<GameObject> onPicked)
            {
                var window = CreateInstance<PrefabPickerWindow>();
                window.titleContent = new GUIContent(L10n.Tr("pick.file"));
                window.minSize = new Vector2(420, 90);
                window.maxSize = window.minSize;
                var root = window.rootVisualElement;
                root.AddToClassList("kn-root");
                root.AddToClassList(EditorGUIUtility.isProSkin ? "kn-dark" : "kn-light");
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackagePaths.StyleSheet);
                if (sheet != null) root.styleSheets.Add(sheet);
                FontLoader.Apply(root, L10n.Current);
                root.Add(PickRow(typeof(GameObject), value =>
                {
                    var prefab = value as GameObject;
                    if (prefab == null) return;
                    window.Close();
                    onPicked(prefab);
                }));
                // オブジェクト選択画面にフォーカスが移っても閉じない補助ウィンドウを使う。
                window.ShowUtility();
            }

            private void OnDisable()
            {
                FontLoader.Release(rootVisualElement);
            }
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

        /// <summary>候補を探すときに使う、元のアバターの名前（小文字）。</summary>
        internal static List<string> Keywords(VRCAvatarDescriptor avatar)
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
                if (head != null && head.Length >= 2) result.Add(head.ToLowerInvariant());
            }

            return result.Distinct().ToList();
        }

    }
}
