using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
#if KAIHENNAVI_MA
using nadena.dev.modular_avatar.core;
#endif

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 3：服からはみ出る肌を隠したい（製品版）。
    /// (1) 素体のシュリンク用シェイプキー（Shrink_〜 など）を縮める。「いつも隠す」なら値を直接変え、
    ///     「この服を着ているときだけ」なら服の中に MA Shape Changer を置く（服を脱ぐと肌が戻る）。
    /// (2) シュリンクのシェイプキーが無い素体は、MA の Mesh Cutter で切り取る（上級。入っている MA にあれば使う）。
    /// </summary>
    internal sealed class SkinFeature : FeatureBase
    {
        public const string HiderObjectName = "KaihenNavi_SkinHide";
        public const string CutterObjectName = "KaihenNavi_MeshCutter";
        private const string MeshCutterTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMeshCutter";

        // シュリンク用のシェイプキーによく使われる名前
        private static readonly Regex ShrinkPattern = new Regex(
            @"shrink|hide|_off\b|^off_|cut|kill|縮|隠|消|非表示", RegexOptions.IgnoreCase);

        private static int _mode;          // 0 = シェイプキー, 1 = Mesh Cutter
        private static string _meshPath;   // 選んだ肌のメッシュ（アバターからの階層パス）
        private static int _targetIndex;   // 0 = いつも, 1.. = 着ている服
        private static bool _showAll;

        public override string Id { get { return "skin"; } }
        public override int Number { get { return 3; } }
        public override Color Accent { get { return Ui.Hex("#E8A87C"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            var bodies = SkinMeshes(ctx.Avatar);
            var body = PickBody(ctx.Avatar, bodies);
            if (body == null || body.sharedMesh == null)
            {
                root.Add(Ui.Notice(L10n.Tr("skin.noBody"), "warn"));
                return root;
            }

            root.Add(Ui.Notice(L10n.Tr("skin.intro"), "info"));
            root.Add(Ui.Chips(new List<string> { L10n.Tr("skin.modeShrink"), L10n.Tr("skin.modeCutter") }, _mode, i =>
            {
                _mode = i;
                ctx.Rebuild();
            }));

            if (_mode == 0)
            {
                // 顔と体が別のメッシュのアバターもあるので、どこの肌かを選べるようにする
                if (bodies.Count > 1)
                {
                    root.Add(Ui.Heading(L10n.Tr("skin.mesh")));
                    var labels = bodies.Select(b => b.name + "  " + ShrinkCount(b)).ToList();
                    var meshPopup = new PopupField<string>(labels, Mathf.Max(0, bodies.IndexOf(body)));
                    meshPopup.AddToClassList("kn-grow");
                    meshPopup.RegisterValueChangedCallback(e =>
                    {
                        var i = labels.IndexOf(e.newValue);
                        if (i < 0) return;
                        _meshPath = HierarchyPath.Get(ctx.Avatar.transform, bodies[i].transform);
                        ctx.Rebuild();
                    });
                    root.Add(meshPopup);
                }

                BuildShrink(ctx, root, body);
            }
            else BuildCutter(ctx, root, body);
            return root;
        }

        // ---------------------------------------------------------------- 肌のメッシュ

        /// <summary>肌がありそうなメッシュ（シェイプキーがあり、着せた服の中ではないもの）。</summary>
        private static List<SkinnedMeshRenderer> SkinMeshes(VRCAvatarDescriptor avatar)
        {
            var outfits = WornOutfits(avatar);
            return avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                .Where(r => !outfits.Any(o => r.transform.IsChildOf(o.transform)))
                .ToList();
        }

        private static int ShrinkCount(SkinnedMeshRenderer r)
        {
            var count = 0;
            for (var i = 0; i < r.sharedMesh.blendShapeCount; i++)
            {
                if (ShrinkPattern.IsMatch(r.sharedMesh.GetBlendShapeName(i))) count++;
            }

            return count;
        }

        /// <summary>選んだメッシュ。まだ選んでいなければ、縮める用のシェイプキーがいちばん多いもの（無ければ体の本体）。</summary>
        private static SkinnedMeshRenderer PickBody(VRCAvatarDescriptor avatar, List<SkinnedMeshRenderer> bodies)
        {
            if (!string.IsNullOrEmpty(_meshPath))
            {
                var chosen = bodies.FirstOrDefault(b => HierarchyPath.Get(avatar.transform, b.transform) == _meshPath);
                if (chosen != null) return chosen;
            }

            var best = bodies.OrderByDescending(ShrinkCount).FirstOrDefault();
            if (best != null && ShrinkCount(best) > 0) return best;
            return AvatarFinder.FindBody(avatar) ?? bodies.FirstOrDefault();
        }

        // ---------------------------------------------------------------- シェイプキーで縮める

        private static void BuildShrink(FeatureContext ctx, VisualElement root, SkinnedMeshRenderer body)
        {
            var mesh = body.sharedMesh;
            var shapes = new List<int>();
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (_showAll || ShrinkPattern.IsMatch(mesh.GetBlendShapeName(i))) shapes.Add(i);
            }

            // いつ隠すか（服を選ぶと、その服を着ているときだけ）
            var outfits = WornOutfits(ctx.Avatar);
            if (_targetIndex > outfits.Count) _targetIndex = 0;
            root.Add(Ui.Heading(L10n.Tr("skin.when")));
            var choices = new List<string> { L10n.Tr("skin.always") };
            choices.AddRange(outfits.Select(o => L10n.Tr("skin.onlyWith", o.name)));
            var popup = new PopupField<string>(choices, _targetIndex);
            popup.AddToClassList("kn-grow");
            popup.RegisterValueChangedCallback(e =>
            {
                _targetIndex = Mathf.Max(0, choices.IndexOf(e.newValue));
                ctx.Rebuild();
            });
            root.Add(popup);

            root.Add(Ui.Heading(L10n.Tr("skin.shapes")));
            if (shapes.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr(_showAll ? "skin.noShapesAtAll" : "skin.noShrink"), "warn"));
            }
            else
            {
                var target = _targetIndex == 0 ? null : outfits[_targetIndex - 1];
                var list = Ui.Box("kn-list");
                foreach (var index in shapes)
                {
                    list.Add(target == null ? AlwaysRow(ctx, body, index) : OutfitRow(ctx, body, target, index));
                }

                root.Add(list);
            }

            var toggle = new Toggle(L10n.Tr("skin.showAll")) { value = _showAll };
            toggle.RegisterValueChangedCallback(e =>
            {
                _showAll = e.newValue;
                ctx.Rebuild();
            });
            root.Add(toggle);
            root.Add(Ui.Text(L10n.Tr("skin.tip"), "kn-hint"));
        }

        /// <summary>いつも隠す：シェイプキーの値を直接変える（改変アバターに保存される。元の Prefab は変わらない）。</summary>
        private static VisualElement AlwaysRow(FeatureContext ctx, SkinnedMeshRenderer body, int index)
        {
            var name = body.sharedMesh.GetBlendShapeName(index);
            return Ui.Slider(Pretty(name), 0f, 100f, body.GetBlendShapeWeight(index), v =>
            {
                var undoName = L10n.Tr("undo.skin");
                ctx.BeginEdit();
                UndoHelper.Begin("skin:" + index, undoName);
                Undo.RecordObject(body, undoName);
                body.SetBlendShapeWeight(index, v);
                UndoHelper.End();
            });
        }

        /// <summary>この服を着ているときだけ：服の中に置いた MA Shape Changer に、縮めるシェイプキーを登録する。</summary>
        private static VisualElement OutfitRow(FeatureContext ctx, SkinnedMeshRenderer body, GameObject outfit, int index)
        {
            var name = body.sharedMesh.GetBlendShapeName(index);
            var row = Ui.Box("kn-list__row");
#if KAIHENNAVI_MA
            var toggle = new Toggle { value = HasShape(outfit, body, name) };
            toggle.AddToClassList("kn-list__toggle");
            toggle.RegisterValueChangedCallback(e => SetShape(ctx, outfit, body, name, e.newValue));
            row.Add(toggle);
#endif
            row.Add(Ui.Text(Pretty(name), "kn-list__title", "kn-grow"));
            return row;
        }

        private static string Pretty(string shapeName)
        {
            return shapeName.Replace('_', ' ').Replace('.', ' ').Trim();
        }

        /// <summary>着せた服（MA で着せたもの）。</summary>
        private static List<GameObject> WornOutfits(VRCAvatarDescriptor avatar)
        {
            var result = new List<GameObject>();
#if KAIHENNAVI_MA
            foreach (Transform child in avatar.transform)
            {
                if (child.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null) result.Add(child.gameObject);
            }
#endif
            return result;
        }

#if KAIHENNAVI_MA
        private static ModularAvatarShapeChanger Hider(GameObject outfit, bool create, FeatureContext ctx, string undoName)
        {
            var t = outfit.transform.Find(HiderObjectName);
            if (t != null) return t.GetComponent<ModularAvatarShapeChanger>();
            if (!create) return null;

            var go = new GameObject(HiderObjectName);
            Undo.RegisterCreatedObjectUndo(go, undoName);
            go.transform.SetParent(outfit.transform, false);
            ctx.NoteCreated(go);
            return Undo.AddComponent<ModularAvatarShapeChanger>(go);
        }

        private static bool HasShape(GameObject outfit, SkinnedMeshRenderer body, string shapeName)
        {
            var changer = Hider(outfit, false, null, null);
            if (changer == null || changer.Shapes == null) return false;
            return changer.Shapes.Any(s => s.ShapeName == shapeName && s.Object != null && s.Object.Get(changer) == body.gameObject);
        }

        private static void SetShape(FeatureContext ctx, GameObject outfit, SkinnedMeshRenderer body, string shapeName, bool on)
        {
            var undoName = L10n.Tr("undo.skin");
            ctx.BeginEdit();
            UndoHelper.Separate();
            var changer = Hider(outfit, on, ctx, undoName);
            if (changer == null) return;

            Undo.RecordObject(changer, undoName);
            var shapes = changer.Shapes != null ? new List<ChangedShape>(changer.Shapes) : new List<ChangedShape>();
            shapes.RemoveAll(s => s.ShapeName == shapeName && s.Object != null && s.Object.Get(changer) == body.gameObject);
            if (on)
            {
                var reference = new AvatarObjectReference();
                reference.Set(body.gameObject);
                shapes.Add(new ChangedShape { Object = reference, ShapeName = shapeName, ChangeType = ShapeChangeType.Set, Value = 100f });
            }

            changer.Shapes = shapes;
            EditorUtility.SetDirty(changer);
            UndoHelper.Separate();
        }
#endif

        // ---------------------------------------------------------------- Mesh Cutter

        private static void BuildCutter(FeatureContext ctx, VisualElement root, SkinnedMeshRenderer body)
        {
            var type = FindMeshCutter();
            if (type == null)
            {
                root.Add(Ui.Notice(L10n.Tr("skin.noCutter"), "warn"));
                return;
            }

            root.Add(Ui.Notice(L10n.Tr("skin.cutterIntro"), "info"));
            var outfits = WornOutfits(ctx.Avatar);
            var places = new List<GameObject> { null };
            places.AddRange(outfits);

            var list = Ui.Box("kn-list");
            foreach (var place in places)
            {
                var p = place;
                var row = Ui.Box("kn-list__row");
                row.Add(Ui.Text(p == null ? L10n.Tr("skin.always") : L10n.Tr("skin.onlyWith", p.name), "kn-list__title", "kn-grow"));
                row.Add(Ui.Button(L10n.Tr("skin.addCutter"), () => AddCutter(ctx, type, p, body), "kn-button--small", "kn-button--primary"));
                list.Add(row);
            }

            root.Add(list);
            root.Add(Ui.Text(L10n.Tr("skin.cutterHow"), "kn-hint"));
        }

        /// <summary>入っている MA に Mesh Cutter があれば、その型（バージョンによっては無い）。</summary>
        private static Type FindMeshCutter()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = assembly.GetType(MeshCutterTypeName, false);
                if (t != null && typeof(Component).IsAssignableFrom(t)) return t;
            }

            return null;
        }

        /// <summary>Mesh Cutter を置いて選ぶ（切り取る範囲は MA の画面で決めてもらう）。</summary>
        private static void AddCutter(FeatureContext ctx, Type type, GameObject outfit, SkinnedMeshRenderer body)
        {
            var undoName = L10n.Tr("undo.skin");
            ctx.BeginEdit();
            UndoHelper.Separate();

            var parent = outfit != null ? outfit.transform : ctx.Avatar.transform;
            var go = new GameObject(CutterObjectName);
            Undo.RegisterCreatedObjectUndo(go, undoName);
            go.transform.SetParent(parent, false);
            ctx.NoteCreated(go);
#if KAIHENNAVI_MA
            var cutter = Undo.AddComponent(go, type);
            // 切り取る相手（肌のメッシュ）は先に入れておく。範囲だけを MA の画面で決めてもらう
            var objectProperty = type.GetProperty("Object");
            if (cutter != null && body != null && objectProperty != null && objectProperty.PropertyType == typeof(AvatarObjectReference))
            {
                var reference = new AvatarObjectReference();
                reference.Set(body.gameObject);
                objectProperty.SetValue(cutter, reference, null);
                EditorUtility.SetDirty(cutter);
            }
#else
            Undo.AddComponent(go, type);
#endif

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("skin.cutterAdded"), L10n.Tr("dialog.ok"));
        }
    }
}
