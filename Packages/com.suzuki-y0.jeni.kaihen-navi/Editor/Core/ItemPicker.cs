using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
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

                var actual = KindOf(prefab);
                if (actual != kind && actual != ItemKind.Other)
                {
                    var key = actual == ItemKind.Outfit ? "pick.looksOutfit" : "pick.looksAccessory";
                    if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr(key, prefab.name), L10n.Tr("pick.useAnyway"), L10n.Tr("dialog.cancel"))) return;
                }

                onPicked(prefab);
            });

            return zone;
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
        /// </summary>
        public static List<GameObject> Suggestions(VRCAvatarDescriptor avatar, ItemKind kind)
        {
            var result = new List<GameObject>();
            if (avatar == null) return result;

            var keywords = Keywords(avatar);
            if (keywords.Count == 0) return result;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(WorkFolders.Root + "/")) continue;
                var lower = path.ToLowerInvariant();
                if (!keywords.Any(k => lower.Contains(k))) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || KindOf(prefab) != kind) continue;
                if (IsSameAsAvatar(prefab, avatar)) continue;

                result.Add(prefab);
                if (result.Count >= MaxSuggestions) break;
            }

            result.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            return result;
        }

        /// <summary>候補を並べる。押すと onPicked が呼ばれる。</summary>
        public static VisualElement SuggestionList(List<GameObject> items, Action<GameObject> onPicked)
        {
            var list = Ui.Box("kn-list", "kn-suggest");
            foreach (var item in items)
            {
                var prefab = item;
                var row = new Button(() => onPicked(prefab));
                row.AddToClassList("kn-list__row");
                row.AddToClassList("kn-suggest__row");

                var thumb = Ui.Box("kn-suggest__thumb");
                var texture = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                if (texture != null) thumb.style.backgroundImage = new StyleBackground(texture);
                row.Add(thumb);

                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(prefab.name, "kn-list__title"));
                texts.Add(Ui.Text(ShortFolder(prefab), "kn-list__sub"));
                row.Add(texts);
                list.Add(row);
            }

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
                var original = MyAvatar.IsMinePath(path) ? MyAvatar.OriginalOf(prefab) : prefab;
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
