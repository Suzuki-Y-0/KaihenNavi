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
    /// 部位の大きさは Modular Avatar の Scale Adjuster（子の骨に影響しない）で非破壊に変え、
    /// 身長は Armature の大きさを変えて、視点も目の位置に合わせ直す。
    /// </summary>
    internal sealed class BodyFeature : FeatureBase
    {
#if KAIHENNAVI_MA
        private const int ImageWidth = 360;
        private const int ImageHeight = 480;
        private const float DragPercentPerPixel = 0.4f;

        /// <summary>体の部位。左右があるものは両方を同時に変える。</summary>
        private sealed class Group
        {
            public string Key;
            public HumanBodyBones[] Bones;
            public bool Uniform;
            public bool Whole;
        }

        private static readonly Group[] Groups =
        {
            new Group { Key = "body.whole", Bones = new[] { HumanBodyBones.Hips }, Whole = true },
            new Group { Key = "body.head", Bones = new[] { HumanBodyBones.Head }, Uniform = true },
            new Group { Key = "body.neck", Bones = new[] { HumanBodyBones.Neck } },
            new Group { Key = "body.chest", Bones = new[] { HumanBodyBones.Chest } },
            new Group { Key = "body.waist", Bones = new[] { HumanBodyBones.Spine } },
            new Group { Key = "body.shoulder", Bones = new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder } },
            new Group { Key = "body.upperArm", Bones = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm } },
            new Group { Key = "body.lowerArm", Bones = new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm } },
            new Group { Key = "body.hand", Bones = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand }, Uniform = true },
            new Group { Key = "body.upperLeg", Bones = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg } },
            new Group { Key = "body.lowerLeg", Bones = new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg } },
            new Group { Key = "body.foot", Bones = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }, Uniform = true },
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
        private bool _dragging;
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

        public override void OnClosed()
        {
#if KAIHENNAVI_MA
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
            for (var i = 0; i < Groups.Length; i++)
            {
                var index = i;
                var button = Ui.Button(L10n.Tr(Groups[i].Key), () =>
                {
                    _selected = index;
                    UndoHelper.Separate();
                    ctx.Rebuild();
                }, "kn-button--small", "kn-figure__part");
                if (i == _selected) button.AddToClassList("kn-figure__part--selected");
                list.Add(button);
            }

            return list;
        }

        private VisualElement BuildControls(FeatureContext ctx)
        {
            var box = Ui.Box("kn-figure__controls");
            var group = Groups[_selected];
            box.Add(Ui.Heading(L10n.Tr(group.Key)));

            if (group.Whole)
            {
                var armature = Armature(_avatar);
                var percent = armature == null ? 100f : armature.localScale.y / BaseArmatureScale(armature) * 100f;
                _heightLabel = Ui.Text("", "kn-readout");
                box.Add(_heightLabel);
                _length = Ui.Slider(L10n.Tr("body.height"), 80f, 120f, percent, v => SetHeight(ctx, v));
                box.Add(_length);
                _width = null;
                UpdateHeightLabel();
            }
            else
            {
                Vector3 current;
                int axis;
                Read(group, out current, out axis);
                if (group.Uniform)
                {
                    _length = Ui.Slider(L10n.Tr("body.size"), 50f, 150f, current.x * 100f, v => SetPart(ctx, group, v, v));
                    _width = null;
                }
                else
                {
                    var thick = axis == 0 ? current.y : current.x;
                    _length = Ui.Slider(L10n.Tr("body.length"), 50f, 150f, current[axis] * 100f, v => SetPart(ctx, group, v, _width != null ? _width.value : 100f));
                    _width = Ui.Slider(L10n.Tr("body.width"), 50f, 150f, thick * 100f, v => SetPart(ctx, group, _length.value, v));
                    box.Add(_length);
                    box.Add(_width);
                }

                if (group.Uniform) box.Add(_length);
            }

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("body.resetPart"), () => ResetGroup(ctx, group), "kn-button--small"));
            buttons.Add(Ui.Button(L10n.Tr("body.resetAll"), () => ResetAll(ctx), "kn-button--small", "kn-button--ghost"));
            box.Add(buttons);
            box.Add(Ui.Text(L10n.Tr(group.Whole ? "body.dragHintWhole" : "body.dragHint"), "kn-hint"));
            return box;
        }

        // ---------------------------------------------------------------- シルエットとボーン図

        /// <summary>シルエットを撮り直す（変更のたびに呼ばれるので、少し待ってまとめて撮る）。</summary>
        private void Refresh()
        {
            if (_canvas == null) return;
            if (_refresh != null) _refresh.Pause();
            _refresh = _canvas.schedule.Execute(() =>
            {
                RenderSilhouette();
                _canvas.MarkDirtyRepaint();
            }).StartingIn(60);
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
                painter.fillColor = selected ? new Color(0.93f, 0.48f, 0.66f, 1f) : clickable ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.5f);
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
                _selected = hit;
                UndoHelper.Separate();
                ctx.Rebuild();
                return;
            }

            _dragging = true;
            _dragStart = new Vector2(e.localPosition.x, e.localPosition.y);
            _dragLength = _length != null ? _length.value : 100f;
            _dragWidth = _width != null ? _width.value : 100f;
            _canvas.CapturePointer(e.pointerId);
        }

        private void OnPointerMove(FeatureContext ctx, PointerMoveEvent e)
        {
            if (!_dragging) return;
            var delta = new Vector2(e.localPosition.x, e.localPosition.y) - _dragStart;
            // 上へ動かすと長く（大きく）、右へ動かすと太く
            if (_length != null) _length.value = Mathf.Clamp(_dragLength - delta.y * DragPercentPerPixel, _length.lowValue, _length.highValue);
            if (_width != null) _width.value = Mathf.Clamp(_dragWidth + delta.x * DragPercentPerPixel, _width.lowValue, _width.highValue);
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
        private void Read(Group group, out Vector3 scale, out int axis)
        {
            var bone = AvatarMeasure.Bone(_avatar, group.Bones[0]);
            axis = LengthAxis(bone);
            scale = Vector3.one;
            if (bone == null) return;
            var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            if (adjuster != null) scale = adjuster.Scale;
        }

        /// <summary>骨の長さ方向（子の骨がある向き）。x=0, y=1, z=2。分からなければ y。</summary>
        private static int LengthAxis(Transform bone)
        {
            if (bone == null || bone.childCount == 0) return 1;
            var d = bone.GetChild(0).localPosition;
            var ax = Mathf.Abs(d.x);
            var ay = Mathf.Abs(d.y);
            var az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) return 0;
            return ay >= az ? 1 : 2;
        }

        private void SetPart(FeatureContext ctx, Group group, float lengthPercent, float widthPercent)
        {
            var undoName = L10n.Tr("undo.body");
            ctx.BeginEdit();
            UndoHelper.Begin("body:" + group.Key, undoName);
            foreach (var b in group.Bones)
            {
                var bone = AvatarMeasure.Bone(_avatar, b);
                if (bone == null) continue;
                var axis = LengthAxis(bone);
                var w = widthPercent / 100f;
                var scale = group.Uniform ? Vector3.one * (lengthPercent / 100f) : new Vector3(w, w, w);
                if (!group.Uniform) scale[axis] = lengthPercent / 100f;

                var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
                if (adjuster == null) adjuster = Undo.AddComponent<ModularAvatarScaleAdjuster>(bone.gameObject);
                Undo.RecordObject(adjuster, undoName);
                adjuster.Scale = scale;
                EditorUtility.SetDirty(adjuster);
            }

            UndoHelper.End();
            Refresh();
        }

        private void ResetGroup(FeatureContext ctx, Group group)
        {
            if (group.Whole)
            {
                SetHeight(ctx, 100f);
            }
            else
            {
                ctx.BeginEdit();
                UndoHelper.Separate();
                foreach (var b in group.Bones) RemoveAdjuster(AvatarMeasure.Bone(_avatar, b));
            }

            UndoHelper.Separate();
            ctx.Rebuild();
        }

        private void ResetAll(FeatureContext ctx)
        {
            if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("body.resetAllConfirm"), L10n.Tr("body.resetAll"), L10n.Tr("dialog.cancel"))) return;
            ctx.BeginEdit();
            UndoHelper.Separate();
            foreach (var g in Groups)
            {
                if (g.Whole) continue;
                foreach (var b in g.Bones) RemoveAdjuster(AvatarMeasure.Bone(_avatar, b));
            }

            SetHeight(ctx, 100f);
            UndoHelper.Separate();
            ctx.Rebuild();
        }

        private static void RemoveAdjuster(Transform bone)
        {
            if (bone == null) return;
            var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            if (adjuster != null) Undo.DestroyObjectImmediate(adjuster);
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
            ctx.BeginEdit();
            UndoHelper.Begin("body:height", undoName);
            var baseScale = BaseArmatureScaleVector(armature);
            Undo.RecordObject(armature, undoName);
            armature.localScale = baseScale * (percent / 100f);

            // 目の位置が変わるので、視点も合わせ直す
            var view = AvatarMeasure.SuggestedViewPosition(_avatar);
            if (view.HasValue)
            {
                Undo.RecordObject(_avatar, undoName);
                _avatar.ViewPosition = view.Value;
                EditorUtility.SetDirty(_avatar);
            }

            UndoHelper.End();
            UpdateHeightLabel();
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
        }

        public string Key { get { return "body-scale"; } }

        public string Capture(VRCAvatarDescriptor avatar)
        {
            var data = new Data();
            foreach (var a in avatar.GetComponentsInChildren<ModularAvatarScaleAdjuster>(true))
            {
                data.entries.Add(new Entry { path = HierarchyPath.Get(avatar.transform, a.transform), scale = a.Scale });
            }

            return JsonUtility.ToJson(data);
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
            var data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json);
            var wanted = data.entries.ToDictionary(e => e.path, e => e.scale);

            foreach (var a in avatar.GetComponentsInChildren<ModularAvatarScaleAdjuster>(true))
            {
                var path = HierarchyPath.Get(avatar.transform, a.transform);
                Vector3 scale;
                if (path != null && wanted.TryGetValue(path, out scale))
                {
                    Undo.RecordObject(a, undoName);
                    a.Scale = scale;
                    wanted.Remove(path);
                }
                else
                {
                    Undo.DestroyObjectImmediate(a);
                }
            }

            foreach (var pair in wanted)
            {
                var t = HierarchyPath.Find(avatar.transform, pair.Key);
                if (t == null) continue;
                var added = Undo.AddComponent<ModularAvatarScaleAdjuster>(t.gameObject);
                added.Scale = pair.Value;
            }
        }
    }
#endif
}
