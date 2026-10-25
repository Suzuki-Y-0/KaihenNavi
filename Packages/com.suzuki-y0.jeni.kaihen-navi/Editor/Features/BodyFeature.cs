using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
#if KAIHENNAVI_MA
using nadena.dev.modular_avatar.core;
#endif

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 4：体型・身長を変えたい（製品版。旧「体型」「身長」を 1 つにまとめたもの）。
    /// アバターの正面シルエットに、簡単なボーン図（Unity のリグ設定画面のような点と線）を重ねて表示する。
    /// 点をつまんで上下に動かすと長さ、左右に動かすと太さが変わる。「全身」の点は身長。
    /// 部位の大きさは Modular Avatar の Scale Adjuster（子の骨の大きさに影響しない）で非破壊に変える。
    /// 長さを変えたときは、子（すねに対する足首など）の位置だけを骨の向きに沿って動かす（BoneFollow）。
    /// 太さを変えても子は動かさない。身長は Armature の大きさを変えて、視点も目の位置に合わせ直す。
    /// </summary>
    internal sealed class BodyFeature : FeatureBase
    {
#if KAIHENNAVI_MA
        private const int ImageWidth = 360;
        private const int ImageHeight = 480;
        private const float DragPercentPerPixel = 0.4f;

        /// <summary>体の部位。左右があるものは両方を同時に変える。</summary>
        private sealed class BoneGroup
        {
            public string Key;
            public HumanBodyBones[] Bones;
            public bool Uniform;
            public bool Whole;
        }

        private static readonly BoneGroup[] Groups =
        {
            new BoneGroup { Key = "body.whole", Bones = new[] { HumanBodyBones.Hips }, Whole = true },
            new BoneGroup { Key = "body.head", Bones = new[] { HumanBodyBones.Head }, Uniform = true },
            new BoneGroup { Key = "body.neck", Bones = new[] { HumanBodyBones.Neck } },
            new BoneGroup { Key = "body.chest", Bones = new[] { HumanBodyBones.Chest } },
            new BoneGroup { Key = "body.waist", Bones = new[] { HumanBodyBones.Spine } },
            new BoneGroup { Key = "body.shoulder", Bones = new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder } },
            new BoneGroup { Key = "body.upperArm", Bones = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm } },
            new BoneGroup { Key = "body.lowerArm", Bones = new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm } },
            new BoneGroup { Key = "body.hand", Bones = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand }, Uniform = true },
            new BoneGroup { Key = "body.upperLeg", Bones = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg } },
            new BoneGroup { Key = "body.lowerLeg", Bones = new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg } },
            new BoneGroup { Key = "body.foot", Bones = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }, Uniform = true },
        };

        private static int _selected;

        private VRCAvatarDescriptor _avatar;
        private Texture2D _silhouette;
        private readonly Dictionary<HumanBodyBones, Vector2> _points = new Dictionary<HumanBodyBones, Vector2>();
        private readonly List<KeyValuePair<HumanBodyBones, HumanBodyBones>> _lines = new List<KeyValuePair<HumanBodyBones, HumanBodyBones>>();
        private VisualElement _canvas;
        private Slider _length;
        private Slider _width;
        private Label _heightLabel;
        private Label _partLabel;
        private Label _dragHint;
        private VisualElement _widthRow;
        private readonly List<Button> _partButtons = new List<Button>();
        private bool _dragging;
        private bool _updatingControls;
        private Vector2 _dragStart;
        private float _dragLength;
        private float _dragWidth;
        private IVisualElementScheduledItem _refresh;
#endif

        public override string Id { get { return "body"; } }
        public override int Number { get { return 4; } }
        public override Color Accent { get { return Ui.Hex("#C38D9E"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
#if KAIHENNAVI_MA
            // 手動更新でページを作り直す場合も、前の撮影予約と画像を解放する。
            OnClosed();
            _avatar = ctx.Avatar;
            if (AvatarMeasure.HumanAnimator(_avatar) == null)
            {
                root.Add(Ui.Notice(L10n.Tr("body.notHumanoid"), "warn"));
                return root;
            }

            root.Add(Ui.Notice(L10n.Tr("body.intro"), "info"));

            var layout = Ui.Box("kn-figure");
            _canvas = Ui.Box("kn-figure__canvas");
            _canvas.generateVisualContent += DrawOverlay;
            _canvas.RegisterCallback<PointerDownEvent>(e => OnPointerDown(ctx, e));
            _canvas.RegisterCallback<PointerMoveEvent>(e => OnPointerMove(ctx, e));
            _canvas.RegisterCallback<PointerUpEvent>(e => OnPointerUp(e));
            layout.Add(_canvas);

            var side = Ui.Box("kn-figure__side");
            side.Add(BuildPartList(ctx));
            layout.Add(side);
            root.Add(layout);

            root.Add(BuildControls(ctx));
            Refresh();
#else
            root.Add(Ui.Notice(L10n.Tr("body.needMa"), "warn"));
            root.Add(Ui.Button(L10n.Tr("toggle.openMa"), () => Application.OpenURL("https://modular-avatar.nadena.dev/ja/"), "kn-button--primary"));
#endif
            return root;
        }

        public override bool RefreshValues()
        {
#if KAIHENNAVI_MA
            if (_avatar == null || _canvas == null || _partLabel == null) return false;
            UpdateControls();
            Refresh();
            return true;
#else
            return false;
#endif
        }

        public override void OnClosed()
        {
#if KAIHENNAVI_MA
            if (_refresh != null) _refresh.Pause();
            _dragging = false;
            if (_silhouette != null) Object.DestroyImmediate(_silhouette);
            _silhouette = null;
            _canvas = null;
#endif
        }

#if KAIHENNAVI_MA
        // ---------------------------------------------------------------- 画面

        private VisualElement BuildPartList(FeatureContext ctx)
        {
            var list = Ui.Box("kn-figure__parts");
            _partButtons.Clear();
            for (var i = 0; i < Groups.Length; i++)
            {
                var index = i;
                var button = Ui.Button(L10n.Tr(Groups[i].Key), () =>
                {
                    SelectPart(index);
                }, "kn-button--small", "kn-figure__part");
                if (i == _selected) button.AddToClassList("kn-figure__part--selected");
                list.Add(button);
                _partButtons.Add(button);
            }

            return list;
        }

        private VisualElement BuildControls(FeatureContext ctx)
        {
            var box = Ui.Box("kn-figure__controls");
            _partLabel = Ui.Heading("");
            box.Add(_partLabel);
            _heightLabel = Ui.Text("", "kn-readout");
            box.Add(_heightLabel);
            // 選択のたびに要素を作り直さず、ラベル・範囲・値だけを切り替える。
            _length = Ui.Slider("", 50f, 150f, 100f, v =>
            {
                if (_updatingControls) return;
                var group = Groups[_selected];
                if (group.Whole) SetHeight(ctx, v);
                else SetPart(ctx, group, v, group.Uniform ? v : _width.value);
            });
            box.Add(_length);
            _width = Ui.Slider(L10n.Tr("body.width"), 50f, 150f, 100f,
                v => { if (!_updatingControls) SetPart(ctx, Groups[_selected], _length.value, v); });
            _widthRow = _width;
            box.Add(_width);
            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("body.resetPart"), () => ResetGroup(ctx, Groups[_selected]), "kn-button--small"));
            buttons.Add(Ui.Button(L10n.Tr("body.resetAll"), () => ResetAll(ctx), "kn-button--small", "kn-button--ghost"));
            box.Add(buttons);
            _dragHint = Ui.Text("", "kn-hint");
            box.Add(_dragHint);
            UpdateControls();
            return box;
        }

        /// <summary>点と部位ボタンは同じ部分更新を使う。選択だけではアバターを変更しない。</summary>
        private void SelectPart(int index)
        {
            if (_selected == index) return;
            _selected = index;
            UndoHelper.Separate();
            UpdateControls();
            _canvas.MarkDirtyRepaint();
        }

        /// <summary>入力通知を出さずに、選択中の部位の値を表示する。</summary>
        private void UpdateControls()
        {
            _updatingControls = true;
            try
            {
                var group = Groups[_selected];
                for (var i = 0; i < _partButtons.Count; i++)
                    _partButtons[i].EnableInClassList("kn-figure__part--selected", i == _selected);
                _partLabel.text = L10n.Tr(group.Key);
                _heightLabel.style.display = group.Whole ? DisplayStyle.Flex : DisplayStyle.None;
                _widthRow.style.display = !group.Whole && !group.Uniform ? DisplayStyle.Flex : DisplayStyle.None;
                _length.label = L10n.Tr(group.Whole ? "body.height" : group.Uniform ? "body.size" : "body.length");
                _length.lowValue = group.Whole ? 80f : 50f;
                _length.highValue = group.Whole ? 120f : 150f;
                if (group.Whole)
                {
                    var armature = Armature(_avatar);
                    _length.SetValueWithoutNotify(armature == null ? 100f : armature.localScale.y / BaseArmatureScale(armature) * 100f);
                    UpdateHeightLabel();
                }
                else
                {
                    Vector3 current;
                    int axis;
                    Read(group, out current, out axis);
                    _length.SetValueWithoutNotify((group.Uniform ? current.x : current[axis]) * 100f);
                    _width.SetValueWithoutNotify((axis == 0 ? current.y : current.x) * 100f);
                }
                _dragHint.text = L10n.Tr(group.Whole ? "body.dragHintWhole" : "body.dragHint");
            }
            finally { _updatingControls = false; }
        }

        // ---------------------------------------------------------------- シルエットとボーン図

        /// <summary>シルエットを撮り直す（変更のたびに呼ばれるので、少し待ってまとめて撮る）。</summary>
        private void Refresh()
        {
            if (_canvas == null) return;
            if (_refresh != null) _refresh.Pause();
            // スライダーを動かしている間は待ち、止まったら 1 回だけ測り直して撮る（メッシュを測るのが重いため）
            _refresh = _canvas.schedule.Execute(() =>
            {
                AvatarMeasure.Forget();
                UpdateHeightLabel();
                RenderSilhouette();
                _canvas.MarkDirtyRepaint();
            }).StartingIn(150);
        }

        private void RenderSilhouette()
        {
            if (_avatar == null || _canvas == null) return;

            var height = AvatarMeasure.Height(_avatar);
            var up = _avatar.transform.up;
            var center = _avatar.transform.position + up * (height * 0.5f);
            var holder = new GameObject("KaihenNavi_SilhouetteCamera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            try
            {
                var camera = holder.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = height * 0.55f;
                camera.aspect = (float)ImageWidth / ImageHeight;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = height * 4f + 10f;
                camera.transform.position = center + _avatar.transform.forward * (height * 2f);
                camera.transform.rotation = Quaternion.LookRotation(-_avatar.transform.forward, up);
                camera.targetTexture = target;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                if (_silhouette == null) _silhouette = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                _silhouette.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
                RenderTexture.active = previous;

                // 色は使わず、形だけの影絵にする
                var pixels = _silhouette.GetPixels32();
                var fill = new Color32(150, 155, 175, 255);
                var empty = new Color32(0, 0, 0, 0);
                // シェーダーによってはアルファを書かないので、そのときは色があるかどうかで見分ける
                var useAlpha = pixels.Any(p => p.a > 12);
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var solid = useAlpha ? p.a > 12 : p.r + p.g + p.b > 6;
                    pixels[i] = solid ? fill : empty;
                }
                _silhouette.SetPixels32(pixels);
                _silhouette.Apply();
                _canvas.style.backgroundImage = new StyleBackground(_silhouette);

                // ボーンの点の位置（画像の中の割合）
                _points.Clear();
                var animator = AvatarMeasure.HumanAnimator(_avatar);
                var bones = new Dictionary<Transform, HumanBodyBones>();
                for (var b = 0; b < (int)HumanBodyBones.LastBone; b++)
                {
                    var t = animator.GetBoneTransform((HumanBodyBones)b);
                    if (t == null || IsFinger((HumanBodyBones)b)) continue;
                    var vp = camera.WorldToViewportPoint(t.position);
                    _points[(HumanBodyBones)b] = new Vector2(vp.x, vp.y);
                    bones[t] = (HumanBodyBones)b;
                }

                // 線：それぞれの骨から、いちばん近い親の骨へ
                _lines.Clear();
                foreach (var pair in bones)
                {
                    var p = pair.Key.parent;
                    while (p != null && !bones.ContainsKey(p)) p = p.parent;
                    if (p != null) _lines.Add(new KeyValuePair<HumanBodyBones, HumanBodyBones>(bones[p], pair.Value));
                }
            }
            finally
            {
                Object.DestroyImmediate(holder);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static bool IsFinger(HumanBodyBones b)
        {
            var i = (int)b;
            return (i >= (int)HumanBodyBones.LeftThumbProximal && i <= (int)HumanBodyBones.RightLittleDistal) ||
                   b == HumanBodyBones.LeftToes || b == HumanBodyBones.RightToes || b == HumanBodyBones.Jaw ||
                   b == HumanBodyBones.LeftEye || b == HumanBodyBones.RightEye;
        }

        /// <summary>画像がキャンバスの中で表示される範囲（縦横比を保って中央に置かれる）。</summary>
        private Rect ImageRect()
        {
            var r = _canvas.contentRect;
            var scale = Mathf.Min(r.width / ImageWidth, r.height / ImageHeight);
            var w = ImageWidth * scale;
            var h = ImageHeight * scale;
            return new Rect(r.x + (r.width - w) * 0.5f, r.y + (r.height - h) * 0.5f, w, h);
        }

        private Vector2 ToCanvas(Vector2 viewport)
        {
            var rect = ImageRect();
            return new Vector2(rect.x + viewport.x * rect.width, rect.y + (1f - viewport.y) * rect.height);
        }

        private void DrawOverlay(MeshGenerationContext mgc)
        {
            if (_points.Count == 0) return;
            var painter = mgc.painter2D;
            var group = Groups[_selected];

            painter.lineWidth = 2f;
            painter.strokeColor = new Color(1f, 1f, 1f, 0.55f);
            foreach (var line in _lines)
            {
                Vector2 a, b;
                if (!_points.TryGetValue(line.Key, out a) || !_points.TryGetValue(line.Value, out b)) continue;
                painter.BeginPath();
                painter.MoveTo(ToCanvas(a));
                painter.LineTo(ToCanvas(b));
                painter.Stroke();
            }

            foreach (var pair in _points)
            {
                var selected = group.Whole ? pair.Key == HumanBodyBones.Hips : group.Bones.Contains(pair.Key);
                var clickable = GroupOf(pair.Key) >= 0;
                var radius = selected ? 7f : clickable ? 5f : 3f;
                painter.fillColor = selected ? new Color(0.227f, 0.475f, 0.733f, 1f) : clickable ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.5f);
                Circle(painter, ToCanvas(pair.Value), radius);
            }
        }

        /// <summary>円（多角形で近似）。</summary>
        private static void Circle(Painter2D painter, Vector2 center, float radius)
        {
            const int segments = 16;
            painter.BeginPath();
            for (var i = 0; i <= segments; i++)
            {
                var a = i * Mathf.PI * 2f / segments;
                var p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }

            painter.ClosePath();
            painter.Fill();
        }

        private static int GroupOf(HumanBodyBones bone)
        {
            for (var i = 1; i < Groups.Length; i++)
            {
                if (Groups[i].Bones.Contains(bone)) return i;
            }

            return bone == HumanBodyBones.Hips ? 0 : -1;
        }

        // ---------------------------------------------------------------- つまんで動かす

        private void OnPointerDown(FeatureContext ctx, PointerDownEvent e)
        {
            var hit = -1;
            var best = 14f;
            foreach (var pair in _points)
            {
                var group = GroupOf(pair.Key);
                if (group < 0) continue;
                var d = Vector2.Distance(ToCanvas(pair.Value), new Vector2(e.localPosition.x, e.localPosition.y));
                if (d < best)
                {
                    best = d;
                    hit = group;
                }
            }

            if (hit < 0) return;
            if (hit != _selected)
            {
                SelectPart(hit);
                return;
            }

            _dragging = true;
            _dragStart = new Vector2(e.localPosition.x, e.localPosition.y);
            _dragLength = _length != null ? _length.value : 100f;
            _dragWidth = _width.value;
            _canvas.CapturePointer(e.pointerId);
        }

        private void OnPointerMove(FeatureContext ctx, PointerMoveEvent e)
        {
            if (!_dragging) return;
            var delta = new Vector2(e.localPosition.x, e.localPosition.y) - _dragStart;
            // 上へ動かすと長く（大きく）、右へ動かすと太く
            if (_length != null) _length.value = Mathf.Clamp(_dragLength - delta.y * DragPercentPerPixel, _length.lowValue, _length.highValue);
            if (!Groups[_selected].Whole && !Groups[_selected].Uniform) _width.value = Mathf.Clamp(_dragWidth + delta.x * DragPercentPerPixel, _width.lowValue, _width.highValue);
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            _canvas.ReleasePointer(e.pointerId);
            UndoHelper.Separate();
        }

        // ---------------------------------------------------------------- 大きさを変える

        /// <summary>部位の今の大きさ（Scale Adjuster の値）と、骨の長さ方向の軸。</summary>
        private void Read(BoneGroup group, out Vector3 scale, out int axis)
        {
            var bone = AvatarMeasure.Bone(_avatar, group.Bones[0]);
            axis = BoneFollow.LengthAxis(bone, AvatarMeasure.HumanAnimator(_avatar));
            scale = Vector3.one;
            if (bone == null) return;
            if (group.Uniform)
            {
                // 頭・手・足は骨ごと大きさを変えている（髪や指、靴も一緒に変わる）
                scale = Vector3.one * BoneFollow.UniformScale(bone);
                return;
            }

            var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            if (adjuster != null) scale = adjuster.Scale;
        }

        private void SetPart(FeatureContext ctx, BoneGroup group, float lengthPercent, float widthPercent)
        {
            var undoName = L10n.Tr("undo.body");
            var animator = AvatarMeasure.HumanAnimator(_avatar);
            ctx.BeginEdit("body:" + group.Key);
            UndoHelper.Begin("body:" + group.Key, undoName);
            var viewBefore = AvatarMeasure.EyeCenter(_avatar);
            var feetBefore = BoneFollow.FeetHeight(animator);
            foreach (var b in group.Bones)
            {
                var bone = AvatarMeasure.Bone(_avatar, b);
                if (bone == null) continue;
                if (group.Uniform)
                {
                    // 前の版で付けた Scale Adjuster が残っていれば外してから、骨ごと大きさを変える
                    if (bone.GetComponent<ModularAvatarScaleAdjuster>() != null) RemoveAdjuster(_avatar, bone);
                    BoneFollow.SetUniformScale(bone, lengthPercent / 100f, undoName);
                    continue;
                }

                var axis = BoneFollow.LengthAxis(bone, animator);
                var w = widthPercent / 100f;
                var scale = group.Uniform ? Vector3.one * (lengthPercent / 100f) : new Vector3(w, w, w);
                if (!group.Uniform) scale[axis] = lengthPercent / 100f;

                var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
                if (adjuster == null) adjuster = Undo.AddComponent<ModularAvatarScaleAdjuster>(bone.gameObject);
                var old = adjuster.Scale;
                Undo.RecordObject(adjuster, undoName);
                adjuster.Scale = scale;
                EditorUtility.SetDirty(adjuster);
                BoneFollow.Apply(bone, animator, old, scale, undoName);
            }

            BoneFollow.KeepFeetOnFloor(animator, feetBefore, undoName);
            FollowView(viewBefore, undoName);
            UndoHelper.End();
            Refresh();
        }

        private void ResetGroup(FeatureContext ctx, BoneGroup group)
        {
            if (group.Whole)
            {
                SetHeight(ctx, 100f);
            }
            else
            {
                ctx.BeginEdit();
                UndoHelper.Separate();
                UndoHelper.Begin("body-reset", L10n.Tr("undo.body"));
                var viewBefore = AvatarMeasure.EyeCenter(_avatar);
                var animator = AvatarMeasure.HumanAnimator(_avatar);
                var feetBefore = BoneFollow.FeetHeight(animator);
                foreach (var b in group.Bones) RemoveAdjuster(_avatar, AvatarMeasure.Bone(_avatar, b));
                BoneFollow.KeepFeetOnFloor(animator, feetBefore, L10n.Tr("undo.body"));
                FollowView(viewBefore, L10n.Tr("undo.body"));
                UndoHelper.End();
            }

            UndoHelper.Separate();
            UpdateControls();
            Refresh();
        }

        private void ResetAll(FeatureContext ctx)
        {
            if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("body.resetAllConfirm"), L10n.Tr("body.resetAll"), L10n.Tr("dialog.cancel"))) return;
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(L10n.Tr("undo.body"));
            var eyesBefore = AvatarMeasure.EyeCenter(_avatar);
            var allAnimator = AvatarMeasure.HumanAnimator(_avatar);
            var allFeet = BoneFollow.FeetHeight(allAnimator);
            foreach (var g in Groups)
            {
                if (g.Whole) continue;
                foreach (var b in g.Bones) RemoveAdjuster(_avatar, AvatarMeasure.Bone(_avatar, b));
            }

            BoneFollow.KeepFeetOnFloor(allAnimator, allFeet, L10n.Tr("undo.body"));

            FollowView(eyesBefore, L10n.Tr("undo.body"));
            SetHeight(ctx, 100f);
            Undo.SetCurrentGroupName(L10n.Tr("undo.body"));
            Undo.CollapseUndoOperations(undoGroup);
            // 身長の内側で増えたグループに、履歴だけが残らないようまとめ先を渡す。
            ctx.Backup.CompletePending(undoGroup);
            UndoHelper.Separate();
            UpdateControls();
            Refresh();
        }

        private static void RemoveAdjuster(VRCAvatarDescriptor avatar, Transform bone)
        {
            if (bone == null) return;
            var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            // 子の位置も元に戻す（Scale Adjuster が無くても、前の版でずれたままの子を直す）
            BoneFollow.Apply(bone, AvatarMeasure.HumanAnimator(avatar), adjuster != null ? adjuster.Scale : Vector3.one, Vector3.one, L10n.Tr("undo.body"));
            if (adjuster != null) Undo.DestroyObjectImmediate(adjuster);
            // 頭・手・足は骨ごとの大きさも元に戻す
            if (BoneFollow.IsUniform(AvatarMeasure.HumanAnimator(avatar), bone)) BoneFollow.SetUniformScale(bone, 1f, L10n.Tr("undo.body"));
        }

        /// <summary>
        /// 脚や胴の長さが変わると目の高さも変わるので、視点を同じだけ動かす。
        /// 視点の機能で微調整した分を消さないよう、置き直すのではなく差の分だけずらす。
        /// </summary>
        /// <param name="before">変える前の目の位置（ワールド）。</param>
        private void FollowView(Vector3? before, string undoName)
        {
            // 毎回メッシュを測り直すと重いので、目の骨がどれだけ動いたかだけを見る
            var after = AvatarMeasure.EyeCenter(_avatar);
            if (!before.HasValue || !after.HasValue) return;
            var delta = _avatar.transform.InverseTransformVector(after.Value - before.Value);
            delta.x = 0f;
            if (delta.sqrMagnitude < 1e-8f) return;
            Undo.RecordObject(_avatar, undoName);
            _avatar.ViewPosition += delta;
            EditorUtility.SetDirty(_avatar);
        }

        // ---------------------------------------------------------------- 身長

        /// <summary>骨全体の親（Hips の親）。アバター直下にあるはず。</summary>
        private static Transform Armature(VRCAvatarDescriptor avatar)
        {
            var hips = AvatarMeasure.Bone(avatar, HumanBodyBones.Hips);
            if (hips == null || hips.parent == null || hips.parent == avatar.transform) return null;
            return hips.parent;
        }

        // 元の Prefab が無いアバター用に、最初に見たときの大きさを 100% として覚えておく
        private static readonly Dictionary<int, Vector3> FirstSeenScale = new Dictionary<int, Vector3>();

        /// <summary>改変前の Armature の大きさ（元の Prefab の値。無ければ最初に見たときの値）。</summary>
        private static Vector3 BaseArmatureScaleVector(Transform armature)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(armature);
            if (source != null && source != armature) return source.localScale;

            Vector3 seen;
            var id = armature.GetInstanceID();
            if (!FirstSeenScale.TryGetValue(id, out seen))
            {
                seen = armature.localScale;
                FirstSeenScale[id] = seen;
            }

            return seen;
        }

        private static float BaseArmatureScale(Transform armature)
        {
            var y = BaseArmatureScaleVector(armature).y;
            return Mathf.Abs(y) < 1e-5f ? 1f : y;
        }

        private void SetHeight(FeatureContext ctx, float percent)
        {
            var armature = Armature(_avatar);
            if (armature == null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("body.noArmature"), L10n.Tr("dialog.ok"));
                return;
            }

            var undoName = L10n.Tr("undo.height");
            ctx.BeginEdit("body:height");
            UndoHelper.Begin("body:height", undoName);
            var baseScale = BaseArmatureScaleVector(armature);
            var eyesBefore = AvatarMeasure.EyeCenter(_avatar);
            Undo.RecordObject(armature, undoName);
            armature.localScale = baseScale * (percent / 100f);

            // 目の位置が変わるので、視点も同じだけ動かす（測り直しは重いので、目の骨の動きだけを見る）
            FollowView(eyesBefore, undoName);

            UndoHelper.End();
            Refresh();
        }

        private void UpdateHeightLabel()
        {
            if (_heightLabel == null) return;
            _heightLabel.text = L10n.Tr("body.heightReadout", (AvatarMeasure.Height(_avatar) * 100f).ToString("0"));
        }
#endif
    }

#if KAIHENNAVI_MA
    /// <summary>
    /// 部位の長さを変えたときに、子（次の骨やアクセ）の位置を骨の向きに沿って動かす。
    /// 子の大きさは変えない（Scale Adjuster のまま）。太さを変えても子は動かさない。
    /// 頭・手・足（全体の大きさを変える部位）は、目や指の付け根が合うよう全方向に動かす。
    /// 位置は「ベースの Prefab での位置 × 長さの倍率」で毎回計算し直すので、何度変えてもずれがたまらない。
    /// </summary>
    internal static class BoneFollow
    {
        private static readonly HumanBodyBones[] UniformBones =
        {
            HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        /// <summary>骨の長さ方向（次の骨がある向き）。x=0, y=1, z=2。分からなければ y。</summary>
        public static int LengthAxis(Transform bone, Animator animator)
        {
            if (bone == null || bone.childCount == 0) return 1;
            var child = NextBone(bone, animator);
            var d = child.localPosition;
            var ax = Mathf.Abs(d.x);
            var ay = Mathf.Abs(d.y);
            var az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) return 0;
            return ay >= az ? 1 : 2;
        }

        /// <summary>子のうち、人型の骨（すねなら足）を優先する。無ければいちばん遠い子。</summary>
        private static Transform NextBone(Transform bone, Animator animator)
        {
            Transform best = null;
            var bestLength = -1f;
            foreach (Transform c in bone)
            {
                if (animator != null && IsHumanBone(animator, c)) return c;
                var length = c.localPosition.sqrMagnitude;
                if (length > bestLength)
                {
                    best = c;
                    bestLength = length;
                }
            }

            return best;
        }

        private static bool IsHumanBone(Animator animator, Transform t)
        {
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                if (animator.GetBoneTransform(b) == t) return true;
            }

            return false;
        }

        public static bool IsUniform(Animator animator, Transform bone)
        {
            if (animator == null) return false;
            foreach (var b in UniformBones)
            {
                if (animator.GetBoneTransform(b) == bone) return true;
            }

            return false;
        }

        // 元の Prefab が無いとき用に、最初に見たときの大きさを 100% として覚えておく
        private static readonly Dictionary<int, Vector3> FirstSeenScale = new Dictionary<int, Vector3>();

        /// <summary>改変前の骨の大きさ（ベースの Prefab の値。無ければ最初に見たときの値）。</summary>
        public static Vector3 BaselineScale(Transform bone)
        {
            var source = BaselineTransform(bone);
            if (source != null) return source.localScale;
            Vector3 seen;
            if (!FirstSeenScale.TryGetValue(bone.GetInstanceID(), out seen))
            {
                seen = bone.localScale;
                FirstSeenScale[bone.GetInstanceID()] = seen;
            }

            return seen;
        }

        /// <summary>頭・手・足の今の大きさ（改変前を 1 とした倍率）。</summary>
        public static float UniformScale(Transform bone)
        {
            var b = BaselineScale(bone).y;
            return Mathf.Abs(b) < 1e-5f ? 1f : bone.localScale.y / b;
        }

        /// <summary>頭・手・足を骨ごと大きくする（髪・指・靴・付けたアクセも一緒に変わる）。</summary>
        public static void SetUniformScale(Transform bone, float factor, string undoName)
        {
            var next = BaselineScale(bone) * factor;
            if ((next - bone.localScale).sqrMagnitude < 1e-12f) return;
            Undo.RecordObject(bone, undoName);
            bone.localScale = next;
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        }

        /// <summary>大きさが oldScale から newScale に変わったときに、子の位置を合わせる。</summary>
        public static void Apply(Transform bone, Animator animator, Vector3 oldScale, Vector3 newScale, string undoName)
        {
            if (bone == null) return;
            // 頭・手・足は骨ごと大きさを変えるので、子の位置は Transform の大きさで自然に合う。
            // ここに来るのは前の版の Scale Adjuster を外すときだけなので、全方向をベースの位置に戻す
            var uniform = IsUniform(animator, bone);
            var axis = LengthAxis(bone, animator);

            foreach (Transform child in bone)
            {
                // MA のプレビュー用など、隠れたオブジェクトは触らない
                if (child.gameObject.hideFlags != HideFlags.None) continue;

                var current = child.localPosition;
                var next = current;
                var baseline = BaselinePosition(child);
                for (var i = 0; i < 3; i++)
                {
                    if (!uniform && i != axis) continue;
                    next[i] = baseline.HasValue
                        ? baseline.Value[i] * newScale[i]
                        : current[i] * newScale[i] / Mathf.Max(1e-4f, oldScale[i]);
                }

                if ((next - current).sqrMagnitude < 1e-12f) continue;
                Undo.RecordObject(child, undoName);
                child.localPosition = next;
                PrefabUtility.RecordPrefabInstancePropertyModifications(child);
            }
        }

        /// <summary>足の高さ（左右の足首・つま先の骨のうち、いちばん低いもの）。測れなければ null。</summary>
        public static float? FeetHeight(Animator animator)
        {
            if (animator == null) return null;
            float? lowest = null;
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var t = animator.GetBoneTransform(b);
                if (t == null) continue;
                var y = t.position.y;
                if (!lowest.HasValue || y < lowest.Value) lowest = y;
            }

            return lowest;
        }

        /// <summary>
        /// 脚の長さが変わると足が床にめり込んだり浮いたりするので、腰（Hips）を上下させて足を元の高さに戻す。
        /// 目標は「ベースの Prefab での足の高さ × 身長の倍率」。ベースが分からなければ、変える前の高さに合わせる。
        /// </summary>
        public static void KeepFeetOnFloor(Animator animator, float? feetBefore, string undoName)
        {
            if (animator == null) return;
            var after = FeetHeight(animator);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!after.HasValue || hips == null) return;

            var target = BaselineFeetHeight(animator, hips) ?? feetBefore;
            if (!target.HasValue) return;
            var lift = target.Value - after.Value;
            if (Mathf.Abs(lift) < 1e-5f) return;
            Undo.RecordObject(hips, undoName);
            hips.position += Vector3.up * lift;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hips);
        }

        private static float? BaselineFeetHeight(Animator animator, Transform hips)
        {
            float? lowest = null;
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var t = animator.GetBoneTransform(b);
                var source = t == null ? null : BaselineTransform(t);
                if (source == null) continue;
                var y = source.position.y - source.root.position.y;
                if (!lowest.HasValue || y < lowest.Value) lowest = y;
            }

            if (!lowest.HasValue) return null;

            // 身長（Armature の大きさ）を変えていれば、その分だけ足の高さも変わる
            var ratio = 1f;
            var armature = hips.parent;
            var baseArmature = armature == null ? null : BaselineTransform(armature);
            if (baseArmature != null && Mathf.Abs(baseArmature.localScale.y) > 1e-5f) ratio = armature.localScale.y / baseArmature.localScale.y;

            var root = animator.transform;
            return root.position.y + lowest.Value * ratio * root.lossyScale.y;
        }

        /// <summary>改変する前の位置（改変アバターの Prefab より手前＝ベースや元アバターでの位置）。無ければ null。</summary>
        private static Vector3? BaselinePosition(Transform t)
        {
            var source = BaselineTransform(t);
            return source == null ? (Vector3?)null : source.localPosition;
        }

        /// <summary>改変アバターの Prefab より手前（ベースや元アバター）で対応するもの。無ければ null。</summary>
        private static Transform BaselineTransform(Transform t)
        {
            Object current = t;
            for (var depth = 0; depth < 8; depth++)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current) as Transform;
                if (source == null) return null;
                if (!MyAvatar.IsMinePath(AssetDatabase.GetAssetPath(source))) return source;
                current = source;
            }

            return null;
        }
    }

    /// <summary>体型（Scale Adjuster）をバックアップに含め、「元に戻す」で消したり戻したりできるようにする。</summary>
    internal sealed class BodyScaleBackup : IBackupParticipant
    {
        [System.Serializable]
        private sealed class Entry
        {
            public string path;
            public Vector3 scale;
        }

        [System.Serializable]
        private sealed class Data
        {
            public List<Entry> entries = new List<Entry>();
            /// <summary>骨ごと大きさを変えている頭・手・足の localScale。</summary>
            public List<Entry> bones = new List<Entry>();
        }

        private static readonly HumanBodyBones[] WholeBones =
        {
            HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        public string Key { get { return "body-scale"; } }

        public string Capture(VRCAvatarDescriptor avatar)
        {
            var data = new Data();
            foreach (var a in avatar.GetComponentsInChildren<ModularAvatarScaleAdjuster>(true))
            {
                data.entries.Add(new Entry { path = HierarchyPath.Get(avatar.transform, a.transform), scale = a.Scale });
            }

            var animator = AvatarMeasure.HumanAnimator(avatar);
            if (animator != null)
            {
                foreach (var b in WholeBones)
                {
                    var t = animator.GetBoneTransform(b);
                    if (t != null) data.bones.Add(new Entry { path = HierarchyPath.Get(avatar.transform, t), scale = t.localScale });
                }
            }

            return JsonUtility.ToJson(data);
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
            var data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json);
            var wanted = data.entries.ToDictionary(e => e.path, e => e.scale);
            var animator = AvatarMeasure.HumanAnimator(avatar);
            var feetBefore = BoneFollow.FeetHeight(animator);

            foreach (var a in avatar.GetComponentsInChildren<ModularAvatarScaleAdjuster>(true))
            {
                var path = HierarchyPath.Get(avatar.transform, a.transform);
                var old = a.Scale;
                var bone = a.transform;
                Vector3 scale;
                if (path != null && wanted.TryGetValue(path, out scale))
                {
                    Undo.RecordObject(a, undoName);
                    a.Scale = scale;
                    wanted.Remove(path);
                }
                else
                {
                    scale = Vector3.one;
                    Undo.DestroyObjectImmediate(a);
                }

                BoneFollow.Apply(bone, animator, old, scale, undoName);
            }

            foreach (var pair in wanted)
            {
                var t = HierarchyPath.Find(avatar.transform, pair.Key);
                if (t == null) continue;
                var added = Undo.AddComponent<ModularAvatarScaleAdjuster>(t.gameObject);
                added.Scale = pair.Value;
                BoneFollow.Apply(t, animator, Vector3.one, pair.Value, undoName);
            }

            // 頭・手・足の大きさ（古いバックアップには無いので、そのときはベースの大きさに戻す）
            if (animator != null)
            {
                var savedBones = (data.bones ?? new List<Entry>()).Where(e => e.path != null).ToDictionary(e => e.path, e => e.scale);
                foreach (var b in WholeBones)
                {
                    var t = animator.GetBoneTransform(b);
                    if (t == null) continue;
                    var path = HierarchyPath.Get(avatar.transform, t);
                    Vector3 scale;
                    var next = path != null && savedBones.TryGetValue(path, out scale) ? scale : BoneFollow.BaselineScale(t);
                    if ((next - t.localScale).sqrMagnitude < 1e-12f) continue;
                    Undo.RecordObject(t, undoName);
                    t.localScale = next;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                }
            }

            BoneFollow.KeepFeetOnFloor(animator, feetBefore, undoName);
        }
    }
#endif
}
