using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 6：アクセをつけたい。
    /// アクセの Prefab を選び、「頭」「首」などの場所を選ぶだけで体の骨に付ける（ヒエラルキーの親子関係は触らせない）。
    /// 付けたあとは、同じページのスライダーで骨からの位置・向き・大きさを直す。
    /// </summary>
    internal sealed class TransformFeature : FeatureBase
    {
        private const string BaselineName = "KaihenNavi_AttachBaseline";

        private sealed class Baseline
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        private static GameObject _target;
        private static System.Action _finishAdjustment;

        /// <summary>スライダーを操作中は、ウィンドウの自動保存を待たせる。</summary>
        internal static bool IsAdjusting { get { return _finishAdjustment != null; } }

        internal static void FinishAdjustment()
        {
            var finish = _finishAdjustment;
            if (finish != null) finish();
        }

        /// <summary>Undo/Redo 後に古いスライダーの終了通知から保存しない。</summary>
        internal static void CancelAdjustment()
        {
            _finishAdjustment = null;
        }

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
        private readonly Dictionary<GameObject, Baseline> _baselines = new Dictionary<GameObject, Baseline>();

        public override string Id { get { return "transform"; } }
        public override int Number { get { return 6; } }
        public override Color Accent { get { return Ui.Hex("#6FA8DC"); } }
        public override bool InTrial { get { return true; } }

        public override bool RefreshOnHierarchyChange { get { return true; } }

        /// <summary>既存の候補一覧と同じ方法で、装着済みの数を表示する。</summary>
        public override string NavBadge(VRCAvatarDescriptor avatar)
        {
            var count = avatar == null ? 0 : FindTargets(avatar).Count;
            return count == 0 ? null : count.ToString();
        }

        public override VisualElement Build(FeatureContext ctx)
        {
            FinishAdjustment();
            var root = new VisualElement();
            var avatar = ctx.Avatar;
            var targets = FindTargets(avatar);

            if (_target != null && (!_target.transform.IsChildOf(avatar.transform) || _target == avatar.gameObject))
            {
                _target = null;
            }

            var overview = new VisualElement();
            // 一覧の「調整」と装着直後で、同じ調整パネルを使う。
            overview.Add(Ui.Heading(L10n.Tr("attach.attached")));
            if (targets.Count == 0) overview.Add(Ui.Text(L10n.Tr("transform.none"), "kn-hint"));
            var list = Ui.Box("kn-list");
            foreach (var t in targets)
            {
                var accessory = t.gameObject;
                var row = Ui.Box("kn-list__row");
                row.Add(Ui.Text(Label(avatar, t), "kn-grow"));
                row.Add(Ui.Button(L10n.Tr("attach.adjust"), () =>
                {
                    _target = accessory;
                    UndoHelper.Separate();
                    ctx.Rebuild();
                }, "kn-button--small"));
                row.Add(Ui.Button(L10n.Tr("attach.remove"), () => Remove(ctx, accessory),
                    "kn-button--small", "kn-button--ghost"));
                list.Add(row);
            }
            overview.Add(list);
            overview.Add(Ui.Button(L10n.Tr("transform.useSelection"), () =>
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

            if (_target == null)
            {
                root.Add(BuildAttach(ctx));
                root.Add(overview);
                return root;
            }

            var target = _target.transform;
            Baseline baseline;
            if (!_baselines.TryGetValue(_target, out baseline))
            {
                // 装着時の推定値は EditorOnly の空オブジェクトで保持する。
                // 独自コンポーネントを付けず、保存・再読み込み後も同じ位置へ戻せる。
                var saved = target.Find(BaselineName);
                baseline = ReadBaseline(saved != null ? saved : target);
                _baselines[_target] = baseline;
            }

            if (target.GetComponent<SkinnedMeshRenderer>() != null && target.childCount == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("transform.skinnedWarn"), "warn"));
            }

            root.Add(Ui.Heading(L10n.Tr("attach.adjusting", target.name)));
            root.Add(Ui.Text(L10n.Tr("transform.localHint"), "kn-hint"));
            var height = AvatarMeasure.Height(avatar);
            root.Add(Ui.Heading(L10n.Tr("transform.position")));
            AddPositionSlider(root, ctx, target, "transform.localY", 1, height);
            AddPositionSlider(root, ctx, target, "transform.localZ", 2, height);
            AddPositionSlider(root, ctx, target, "transform.localX", 0, height);

            root.Add(Ui.Heading(L10n.Tr("transform.rotation")));
            AddRotationSlider(root, ctx, target, "transform.rotationX", 0);
            AddRotationSlider(root, ctx, target, "transform.rotationY", 1);
            AddRotationSlider(root, ctx, target, "transform.rotationZ", 2);

            root.Add(Ui.Heading(L10n.Tr("transform.size")));
            root.Add(EditSlider(ctx, target, L10n.Tr("transform.sizeLabel"), 10f, 300f,
                SizePercent(target, baseline), L10n.Tr("undo.transformScale"),
                v => target.localScale = baseline.LocalScale * (v / 100f)));

            var actions = Ui.Box("kn-row");
            actions.Add(Ui.Button(L10n.Tr("transform.reset"), () =>
            {
                FinishAdjustment();
                var undoName = L10n.Tr("undo.transformReset");
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                Undo.RecordObject(target, undoName);
                target.localPosition = baseline.LocalPosition;
                target.localRotation = baseline.LocalRotation;
                target.localScale = baseline.LocalScale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                ctx.CompleteEdit(group);
                ctx.Rebuild();
                SceneView.RepaintAll();
            }));
            actions.Add(Ui.Button(L10n.Tr("transform.frame"), () => Frame(target), "kn-button--ghost"));
            actions.Add(Ui.Button(L10n.Tr("attach.finish"), () =>
            {
                _target = null;
                UndoHelper.Separate();
                ctx.Rebuild();
            }, "kn-button--primary"));
            root.Add(actions);
            root.Add(overview);
            root.Add(BuildAttach(ctx));

            return root;
        }

        public override void OnClosed()
        {
            FinishAdjustment();
            _target = null;
        }

        // ---------------------------------------------------------------- つける

        private VisualElement BuildAttach(FeatureContext ctx)
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

                box.Add(ItemPicker.OwnedItems(ItemKind.Accessory, prefab =>
                {
                    _pending = prefab;
                    ctx.Rebuild();
                }));
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

            box.Add(Ui.Button(L10n.Tr("feature.transform.nav"), () => Attach(ctx, _pending, Places[_placeIndex]), "kn-button--primary"));
            return box;
        }

        /// <summary>
        /// アバター用らしいアクセは足元基準の位置を保ち、それ以外は見た目の中心を骨の近くへ寄せる。
        /// 推定は初期値に過ぎないため、装着直後に必ず調整パネルを開く。
        /// </summary>
        private void Attach(FeatureContext ctx, GameObject prefab, HumanBodyBones place)
        {
            var avatar = ctx.Avatar;
            if (prefab == null || avatar == null) return;
            FinishAdjustment();
            var animator = avatar.GetComponent<Animator>();
            var bone = animator != null && animator.isHuman ? animator.GetBoneTransform(place) : null;
            if (bone == null && place == HumanBodyBones.Chest && animator != null && animator.isHuman) bone = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (bone == null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("attach.noBone"), L10n.Tr("dialog.ok"));
                return;
            }

            var height = AvatarMeasure.Height(avatar);
            var undoName = L10n.Tr("undo.attach");
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) return;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            var target = instance.transform;
            Undo.SetTransformParent(target, avatar.transform, undoName);
            Undo.RecordObject(target, undoName);
            target.localPosition = prefab.transform.localPosition;
            target.localRotation = prefab.transform.localRotation;
            target.localScale = prefab.transform.localScale;

            Bounds bounds;
            var hasBounds = TryBounds(instance, out bounds);
            var keepPosition = MatchesAvatar(avatar, prefab) ||
                (hasBounds && Vector3.Distance(bounds.center, bone.position) <= height * 0.2f);
            Undo.SetTransformParent(target, bone, undoName);
            Undo.RecordObject(target, undoName);
            if (!keepPosition)
            {
                // 頭は骨より上、首は少し前。ほかは骨の中心を出発点にする。
                var offset = place == HumanBodyBones.Head ? avatar.transform.up * (height * 0.06f) :
                    place == HumanBodyBones.Neck ? avatar.transform.forward * (height * 0.02f) : Vector3.zero;
                target.position += bone.position + offset - (hasBounds ? bounds.center : target.position);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);

            var baseline = ReadBaseline(target);
            _baselines[instance] = baseline;
            var marker = new GameObject(BaselineName) { tag = "EditorOnly" };
            Undo.RegisterCreatedObjectUndo(marker, undoName);
            Undo.SetTransformParent(marker.transform, target, undoName);
            Undo.RecordObject(marker.transform, undoName);
            marker.transform.localPosition = baseline.LocalPosition;
            marker.transform.localRotation = baseline.LocalRotation;
            marker.transform.localScale = baseline.LocalScale;

            ctx.NoteCreated(instance);
            ctx.CompleteEdit(group);
            _target = instance;
            _pending = null;
            UndoHelper.Separate();
            ctx.Rebuild();
            Frame(target);
        }

        /// <summary>元アバターの名前を Prefab 名・フォルダ名と比べる。短すぎる語は誤一致するため使わない。</summary>
        private static bool MatchesAvatar(VRCAvatarDescriptor avatar, GameObject prefab)
        {
            var original = MyAvatar.OriginalOf(avatar.gameObject);
            var name = original != null ? original.name : avatar.name;
            var token = name.Split(new[] { ' ', '_', '-', '（', '(', '.' },
                System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (token == null || token.Length < 3) return false;
            var path = AssetDatabase.GetAssetPath(prefab) + "/" + prefab.name;
            return path.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Baseline ReadBaseline(Transform target)
        {
            return new Baseline { LocalPosition = target.localPosition,
                LocalRotation = target.localRotation, LocalScale = target.localScale };
        }

        private static void Remove(FeatureContext ctx, GameObject accessory)
        {
            if (accessory == null || accessory == ctx.Avatar.gameObject ||
                !accessory.transform.IsChildOf(ctx.Avatar.transform)) return;
            // 体の骨を含むまとまりはアクセとして削除しない。
            var bones = AvatarFinder.HumanoidBones(ctx.Avatar);
            if (accessory.GetComponentsInChildren<Transform>(true).Any(bones.Contains)) return;
            FinishAdjustment();
            var undoName = L10n.Tr("undo.detach");
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.DestroyObjectImmediate(accessory);
            ctx.CompleteEdit(group);
            if (_target == accessory) _target = null;
            ctx.Rebuild();
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

            // 自前の骨を持つアクセも、装着時の目印からまとまりを拾う。
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != BaselineName || t.parent == null) continue;
                result.RemoveAll(candidate => candidate != t.parent && candidate.IsChildOf(t.parent));
                if (!result.Contains(t.parent)) result.Add(t.parent);
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

        private static float Axis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private static Vector3 WithAxis(Vector3 value, int axis, float next)
        {
            if (axis == 0) value.x = next;
            else if (axis == 1) value.y = next;
            else value.z = next;
            return value;
        }

        private static void AddPositionSlider(VisualElement root, FeatureContext ctx, Transform target,
            string key, int axis, float height)
        {
            // 骨のスケールを含めて cm に換算する。表示値の原点は骨の位置。
            var scale = target.parent == null ? 1f : Mathf.Abs(Axis(target.parent.lossyScale, axis));
            scale = Mathf.Max(0.0001f, scale);
            var cm = Axis(target.localPosition, axis) * scale * 100f;
            var range = Mathf.Max(height * 25f, Mathf.Abs(cm) + height * 5f);
            root.Add(EditSlider(ctx, target, L10n.Tr(key), -range, range, cm,
                L10n.Tr("undo.transformMove"), value =>
                    target.localPosition = WithAxis(target.localPosition, axis, value / (scale * 100f))));
        }

        private static void AddRotationSlider(VisualElement root, FeatureContext ctx, Transform target,
            string key, int axis)
        {
            var degrees = Mathf.DeltaAngle(0f, Axis(target.localEulerAngles, axis));
            root.Add(EditSlider(ctx, target, L10n.Tr(key), -180f, 180f, degrees,
                L10n.Tr("undo.transformRotate"), value =>
                    target.localEulerAngles = WithAxis(target.localEulerAngles, axis, value)));
        }

        /// <summary>ドラッグは離すまで、数値入力は欄を離れるまでを 1 操作にし、最後に保存する。</summary>
        private static Slider EditSlider(FeatureContext ctx, Transform target, string label, float min,
            float max, float value, string undoName, System.Action<float> change)
        {
            var group = -1;
            System.Action finish = null;
            finish = () =>
            {
                // Undo 後や別の欄の終了後に届いた、古い通知は無視する。
                if (_finishAdjustment != finish) return;
                _finishAdjustment = null;
                var completedGroup = group;
                group = -1;
                if (target != null) ctx.CompleteEdit(completedGroup);
                UndoHelper.Separate();
            };
            var slider = Ui.Slider(label, min, max, value, next =>
            {
                if (target == null) return;
                if (_finishAdjustment != finish)
                {
                    FinishAdjustment();
                    ctx.BeginEdit();
                    UndoHelper.Separate();
                    Undo.IncrementCurrentGroup();
                    group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName(undoName);
                    _finishAdjustment = finish;
                }
                Undo.RecordObject(target, undoName);
                change(next);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                Undo.CollapseUndoOperations(group);
                SceneView.RepaintAll();
            });
            slider.RegisterCallback<PointerCaptureOutEvent>(e => finish());
            slider.RegisterCallback<FocusOutEvent>(e => finish());
            return slider;
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
            Bounds bounds;
            if (view != null) view.Frame(TryBounds(target.gameObject, out bounds) ? bounds :
                new Bounds(target.position, Vector3.one * 0.2f), false);
            Tools.current = Tool.Move;
        }
    }
}
