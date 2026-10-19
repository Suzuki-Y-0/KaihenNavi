using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 6：アクセをつけたい。
    /// アクセの Prefab を選び、「頭」「首」などの場所を選ぶだけで体の骨に付ける（ヒエラルキーの親子関係は触らせない）。
    /// 付けたあとは、大きな矢印ボタンとスライダーで位置・向き・大きさを直す。向きはアバターから見た向き。
    /// </summary>
    internal sealed class TransformFeature : FeatureBase
    {
        private static readonly float[] Steps = { 0.001f, 0.005f, 0.02f };
        private const float RotateStep = 5f;

        private sealed class Baseline
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        private static GameObject _target;

        // これから付けるアクセ（Prefab）と、付ける場所
        private static GameObject _pending;
        private static int _placeIndex;

        private static readonly HumanBodyBones[] Places =
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Chest, HumanBodyBones.Hips,
            HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.RightFoot, HumanBodyBones.LeftFoot,
        };

        private static readonly string[] PlaceKeys =
        {
            "attach.head", "attach.neck", "attach.chest", "attach.hips",
            "attach.rightHand", "attach.leftHand", "attach.rightFoot", "attach.leftFoot",
        };
        private static int _stepIndex = 1;
        private readonly Dictionary<GameObject, Baseline> _baselines = new Dictionary<GameObject, Baseline>();
        private Slider _size;

        public override string Id { get { return "transform"; } }
        public override int Number { get { return 6; } }
        public override Color Accent { get { return Ui.Hex("#6FA8DC"); } }
        public override bool InTrial { get { return true; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            var avatar = ctx.Avatar;
            root.Add(BuildAttach(ctx));

            var targets = FindTargets(avatar);

            if (_target != null && (!_target.transform.IsChildOf(avatar.transform) || _target == avatar.gameObject))
            {
                _target = null;
            }

            if (_target != null && !targets.Contains(_target.transform)) targets.Insert(0, _target.transform);

            // どれを直す？
            root.Add(Ui.Heading(L10n.Tr("transform.which")));
            root.Add(Ui.Text(L10n.Tr("transform.whichHint"), "kn-hint"));
            var pickRow = Ui.Box("kn-row");
            if (targets.Count > 0)
            {
                // 先頭は「えらんでください」
                var labels = new List<string> { L10n.Tr("transform.choose") };
                foreach (var t in targets)
                {
                    var label = Label(avatar, t);
                    var unique = label;
                    var n = 2;
                    while (labels.Contains(unique)) unique = label + " #" + n++;
                    labels.Add(unique);
                }

                var index = _target == null ? 0 : targets.IndexOf(_target.transform) + 1;
                var popup = new PopupField<string>(labels, Mathf.Max(0, index));
                popup.AddToClassList("kn-grow");
                popup.RegisterValueChangedCallback(e =>
                {
                    var i = labels.IndexOf(e.newValue);
                    if (i <= 0) return;
                    _target = targets[i - 1].gameObject;
                    UndoHelper.Separate();
                    ctx.Rebuild();
                });
                pickRow.Add(popup);
            }

            pickRow.Add(Ui.Button(L10n.Tr("transform.useSelection"), () =>
            {
                var selected = Selection.activeTransform;
                if (selected == null || selected == avatar.transform || !selected.IsChildOf(avatar.transform))
                {
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("transform.selectionInvalid"), L10n.Tr("dialog.ok"));
                    return;
                }

                _target = selected.gameObject;
                UndoHelper.Separate();
                ctx.Rebuild();
            }, "kn-button--ghost"));
            root.Add(pickRow);

            if (_target == null)
            {
                root.Add(Ui.Notice(targets.Count > 0 ? L10n.Tr("transform.pickFirst") : L10n.Tr("transform.none"), "info"));
                return root;
            }

            var target = _target.transform;
            Baseline baseline;
            if (!_baselines.TryGetValue(_target, out baseline))
            {
                baseline = new Baseline
                {
                    LocalPosition = target.localPosition,
                    LocalRotation = target.localRotation,
                    LocalScale = target.localScale,
                };
                _baselines[_target] = baseline;
            }

            if (target.GetComponent<SkinnedMeshRenderer>() != null && target.childCount == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("transform.skinnedWarn"), "warn"));
            }

            // 位置
            root.Add(Ui.Heading(L10n.Tr("transform.position")));
            var stepLabels = new List<string> { L10n.Tr("transform.stepFine"), L10n.Tr("transform.stepNormal"), L10n.Tr("transform.stepLarge") };
            root.Add(Ui.Chips(stepLabels, _stepIndex, i =>
            {
                _stepIndex = i;
                ctx.Rebuild();
            }));

            var up = avatar.transform.up;
            var right = avatar.transform.right;
            var forward = avatar.transform.forward;

            var pads = Ui.Box("kn-pads");
            var vertical = Ui.Box("kn-pad", "kn-pad--vertical");
            vertical.Add(Ui.Arrow("↑", L10n.Tr("transform.up"), () => Move(ctx, target, up)));
            vertical.Add(Ui.Arrow("↓", L10n.Tr("transform.down"), () => Move(ctx, target, -up)));
            pads.Add(vertical);

            var cross = Ui.Box("kn-pad", "kn-pad--cross");
            var top = Ui.Box("kn-pad__row");
            top.Add(Ui.Arrow("↑", L10n.Tr("transform.forward"), () => Move(ctx, target, forward)));
            cross.Add(top);
            var middle = Ui.Box("kn-pad__row");
            // アバターと向かい合ったときに画面と同じ向きになるよう、左右ボタンはアバターの右手側を「右」とする
            middle.Add(Ui.Arrow("←", L10n.Tr("transform.left"), () => Move(ctx, target, -right)));
            middle.Add(Ui.Arrow("→", L10n.Tr("transform.right"), () => Move(ctx, target, right)));
            cross.Add(middle);
            var bottom = Ui.Box("kn-pad__row");
            bottom.Add(Ui.Arrow("↓", L10n.Tr("transform.back"), () => Move(ctx, target, -forward)));
            cross.Add(bottom);
            pads.Add(cross);
            root.Add(pads);
            root.Add(Ui.Text(L10n.Tr("transform.directionNote"), "kn-hint"));

            // 向き
            root.Add(Ui.Heading(L10n.Tr("transform.rotation")));
            root.Add(RotateRow(ctx, target, L10n.Tr("transform.twist"), up));
            root.Add(RotateRow(ctx, target, L10n.Tr("transform.tiltForward"), right));
            root.Add(RotateRow(ctx, target, L10n.Tr("transform.tiltSide"), forward));

            // 大きさ
            root.Add(Ui.Heading(L10n.Tr("transform.size")));
            var currentPercent = SizePercent(target, baseline);
            _size = Ui.Slider(L10n.Tr("transform.sizeLabel"), 10f, 300f, currentPercent, v => Resize(ctx, target, baseline, v));
            root.Add(_size);
            var sizeButtons = Ui.Box("kn-row");
            sizeButtons.Add(Ui.Button("−5%", () => _size.value = Mathf.Clamp(_size.value - 5f, 10f, 300f), "kn-button--small"));
            sizeButtons.Add(Ui.Button("+5%", () => _size.value = Mathf.Clamp(_size.value + 5f, 10f, 300f), "kn-button--small"));
            sizeButtons.Add(Ui.Button("100%", () => _size.value = 100f, "kn-button--small"));
            root.Add(sizeButtons);

            // まとめ
            var actions = Ui.Box("kn-row");
            actions.Add(Ui.Button(L10n.Tr("transform.reset"), () =>
            {
                var undoName = L10n.Tr("undo.transformReset");
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.RecordObject(target, undoName);
                target.localPosition = baseline.LocalPosition;
                target.localRotation = baseline.LocalRotation;
                target.localScale = baseline.LocalScale;
                UndoHelper.Separate();
                _size.SetValueWithoutNotify(100f);
            }));
            actions.Add(Ui.Button(L10n.Tr("transform.frame"), () => Frame(target), "kn-button--ghost"));
            root.Add(actions);

            return root;
        }

        public override void OnClosed()
        {
            _baselines.Clear();
        }

        // ---------------------------------------------------------------- つける

        private static VisualElement BuildAttach(FeatureContext ctx)
        {
            var box = Ui.Box("kn-attach");
            box.Add(Ui.Heading(L10n.Tr("attach.pick")));

            if (_pending == null)
            {
                box.Add(ItemPicker.DropZone(L10n.Tr("attach.drop"), ItemKind.Accessory, prefab =>
                {
                    _pending = prefab;
                    ctx.Rebuild();
                }));

                var suggestions = ItemPicker.Suggestions(ctx.Avatar, ItemKind.Accessory);
                if (suggestions.Count > 0)
                {
                    box.Add(Ui.Text(L10n.Tr("attach.suggest"), "kn-hint"));
                    box.Add(ItemPicker.SuggestionList(suggestions, prefab =>
                    {
                        _pending = prefab;
                        ctx.Rebuild();
                    }));
                }

                return box;
            }

            var chosen = Ui.Box("kn-row", "kn-attach__chosen");
            chosen.Add(Ui.Text(L10n.Tr("attach.chosen", _pending.name), "kn-grow"));
            chosen.Add(Ui.Button(L10n.Tr("attach.cancel"), () =>
            {
                _pending = null;
                ctx.Rebuild();
            }, "kn-button--small", "kn-button--ghost"));
            box.Add(chosen);

            box.Add(Ui.Heading(L10n.Tr("attach.where")));
            var labels = new List<string>();
            foreach (var key in PlaceKeys) labels.Add(L10n.Tr(key));
            box.Add(Ui.Chips(labels, _placeIndex, i =>
            {
                _placeIndex = i;
                ctx.Rebuild();
            }));

            box.Add(Ui.Button(L10n.Tr("attach.do"), () => Attach(ctx, _pending, Places[_placeIndex]), "kn-button--primary"));
            return box;
        }

        /// <summary>
        /// アクセを体の骨に付ける。まずアバターの足元を基準に置き（アバター用に作られたアクセはこれで正しい位置になる）、
        /// 見た目が付ける場所から大きく離れていれば、その場所へ寄せる。
        /// </summary>
        private static void Attach(FeatureContext ctx, GameObject prefab, HumanBodyBones place)
        {
            var avatar = ctx.Avatar;
            var animator = avatar.GetComponent<Animator>();
            var bone = animator != null && animator.isHuman ? animator.GetBoneTransform(place) : null;
            if (bone == null && place == HumanBodyBones.Chest && animator != null && animator.isHuman) bone = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (bone == null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("attach.noBone"), L10n.Tr("dialog.ok"));
                return;
            }

            var undoName = L10n.Tr("undo.attach");
            ctx.BeginEdit();
            UndoHelper.Separate();

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) return;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            instance.transform.SetParent(avatar.transform, false);
            Undo.SetTransformParent(instance.transform, bone, undoName);

            Bounds bounds;
            if (TryBounds(instance, out bounds))
            {
                var limit = Mathf.Max(0.25f, avatar.ViewPosition.y * 0.25f);
                if (Vector3.Distance(bounds.center, bone.position) > limit)
                {
                    instance.transform.position += bone.position - bounds.center;
                }
            }

            ctx.NoteCreated(instance);
            _target = instance;
            _pending = null;
            UndoHelper.Separate();
            ctx.Rebuild();
            Frame(instance.transform);
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return found;
        }

        // ---------------------------------------------------------------- 候補

        /// <summary>
        /// 位置を直せるもの：体の骨（Head など）の下に付いているアクセサリーと、骨の入っていない小物。
        /// 体に合わせて変形する服（ボーンに直接ウェイトが乗っているもの）は位置を変えても動かないので除く。
        /// </summary>
        private static List<Transform> FindTargets(VRCAvatarDescriptor avatar)
        {
            var result = new List<Transform>();
            var root = avatar.transform;
            var body = AvatarFinder.FindBody(avatar);
            var bones = AvatarFinder.HumanoidBones(avatar);
            var menuRoot = root.Find(ToggleFeature.MenuObjectName);

            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (r == body) continue;
                if (menuRoot != null && r.transform.IsChildOf(menuRoot)) continue;

                Transform anchor;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null)
                {
                    anchor = smr.rootBone;
                    if (anchor == null && smr.bones != null && smr.bones.Length > 0) anchor = smr.bones[0];
                    if (anchor == null || bones.Contains(anchor)) continue;
                }
                else if (r is MeshRenderer)
                {
                    anchor = r.transform;
                }
                else
                {
                    continue;
                }

                // 体の骨の直下まで親をたどる（アクセサリーのまとまりごと動かす）
                var t = anchor;
                while (t.parent != null && t.parent != root && !bones.Contains(t.parent)) t = t.parent;
                if (t.parent == null) continue;
                if (t.parent == root && smr != null) continue; // アバター直下の衣装（別アーマチュア）は対象外

                if (!result.Contains(t)) result.Add(t);
            }

            return result;
        }

        private static string Label(VRCAvatarDescriptor avatar, Transform t)
        {
            var parent = t.parent;
            if (parent == null || parent == avatar.transform) return t.name;
            return t.name + "  (" + parent.name + ")";
        }

        // ---------------------------------------------------------------- 操作

        private static void Move(FeatureContext ctx, Transform target, Vector3 direction)
        {
            var undoName = L10n.Tr("undo.transformMove");
            ctx.BeginEdit();
            UndoHelper.Begin("transform-move", undoName);
            Undo.RecordObject(target, undoName);
            target.position += direction.normalized * Steps[_stepIndex];
            UndoHelper.End();
        }

        private static VisualElement RotateRow(FeatureContext ctx, Transform target, string label, Vector3 axis)
        {
            var row = Ui.Box("kn-row", "kn-rotate-row");
            row.Add(Ui.Text(label, "kn-rotate-row__label"));
            row.Add(Ui.Button("−" + RotateStep + "°", () => Rotate(ctx, target, axis, -RotateStep), "kn-button--small"));
            row.Add(Ui.Button("+" + RotateStep + "°", () => Rotate(ctx, target, axis, RotateStep), "kn-button--small"));
            return row;
        }

        private static void Rotate(FeatureContext ctx, Transform target, Vector3 axis, float degrees)
        {
            var undoName = L10n.Tr("undo.transformRotate");
            ctx.BeginEdit();
            UndoHelper.Begin("transform-rotate", undoName);
            Undo.RecordObject(target, undoName);
            target.rotation = Quaternion.AngleAxis(degrees, axis) * target.rotation;
            UndoHelper.End();
        }

        private static void Resize(FeatureContext ctx, Transform target, Baseline baseline, float percent)
        {
            var undoName = L10n.Tr("undo.transformScale");
            ctx.BeginEdit();
            UndoHelper.Begin("transform-scale", undoName);
            Undo.RecordObject(target, undoName);
            target.localScale = baseline.LocalScale * (percent / 100f);
            UndoHelper.End();
        }

        private static float SizePercent(Transform target, Baseline baseline)
        {
            var b = baseline.LocalScale;
            var c = target.localScale;
            var reference = Mathf.Abs(b.x) > 1e-6f ? b.x : (Mathf.Abs(b.y) > 1e-6f ? b.y : b.z);
            var current = Mathf.Abs(b.x) > 1e-6f ? c.x : (Mathf.Abs(b.y) > 1e-6f ? c.y : c.z);
            if (Mathf.Abs(reference) < 1e-6f) return 100f;
            return Mathf.Clamp(current / reference * 100f, 10f, 300f);
        }

        private static void Frame(Transform target)
        {
            Selection.activeGameObject = target.gameObject;
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.FrameSelected();
        }
    }
}
