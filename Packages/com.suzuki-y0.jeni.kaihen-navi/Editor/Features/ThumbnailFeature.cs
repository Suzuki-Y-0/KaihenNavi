using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 10：きれいなサムネイルにしたい（製品版）。
    /// アップロード用のサムネイル（4:3）を、狭い画角のカメラで撮る。広い画角（60° 前後）だと顔がゆがんで見えるため。
    /// 撮った画像はアバターのフォルダに Thumbnail.png として保存し、VRChat SDK のアップロード画面で選んでもらう。
    /// </summary>
    internal sealed class ThumbnailFeature : FeatureBase
    {
        private const int Width = 1200;
        private const int Height = 900;
        private const int PreviewWidth = 480;
        private const int PreviewHeight = 360;
        private const float FieldOfView = 18f;

        private enum Framing { Face, Upper, Full }

        private static readonly string[] FramingKeys = { "thumb.face", "thumb.upper", "thumb.full" };
        private static readonly float[] Angles = { 0f, -25f, 25f };
        private static readonly string[] AngleKeys = { "thumb.front", "thumb.left", "thumb.right" };

        // 背景（最初はシーンのまま）
        private static readonly Color?[] Backgrounds =
        {
            null,
            new Color(1f, 1f, 1f),
            new Color(1f, 0.89f, 0.93f),
            new Color(0.86f, 0.93f, 1f),
            new Color(0.91f, 0.88f, 1f),
            new Color(0.86f, 0.97f, 0.91f),
            new Color(1f, 0.96f, 0.85f),
            new Color(0.16f, 0.16f, 0.19f),
            new Color(0.02f, 0.02f, 0.03f),
            null, // 好きな色（_custom を使う）
        };

        private static readonly string[] BackgroundKeys =
        {
            "thumb.bgScene", "thumb.bgWhite", "thumb.bgPink", "thumb.bgBlue", "thumb.bgLavender", "thumb.bgMint",
            "thumb.bgCream", "thumb.bgDark", "thumb.bgBlack", "thumb.bgCustom",
        };

        private static Color _custom = new Color(0.98f, 0.8f, 0.6f);

        private static int _framing;
        private static int _angle;
        private static int _background = 1;
        private static float _zoom = 1f;

        private Texture2D _preview;

        public override string Id { get { return "thumbnail"; } }
        public override int Number { get { return 10; } }
        public override Color Accent { get { return Ui.Hex("#8E7DBE"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var avatar = ctx.Avatar;
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("thumb.intro"), "info"));

            var preview = Ui.Box("kn-thumb-preview");
            root.Add(preview);
            RefreshPreview(avatar, preview);

            root.Add(Ui.Heading(L10n.Tr("thumb.framing")));
            root.Add(Ui.Chips(Labels(FramingKeys), _framing, i =>
            {
                _framing = i;
                RefreshPreview(avatar, preview);
                ctx.Rebuild();
            }));

            root.Add(Ui.Heading(L10n.Tr("thumb.angle")));
            root.Add(Ui.Text(L10n.Tr("thumb.angleHint"), "kn-hint"));
            root.Add(Ui.Chips(Labels(AngleKeys), _angle, i =>
            {
                _angle = i;
                RefreshPreview(avatar, preview);
                ctx.Rebuild();
            }));

            root.Add(Ui.Heading(L10n.Tr("thumb.background")));
            root.Add(Ui.Chips(Labels(BackgroundKeys), _background, i =>
            {
                _background = i;
                RefreshPreview(avatar, preview);
                ctx.Rebuild();
            }));
            if (_background == BackgroundKeys.Length - 1)
            {
                var picker = new UnityEditor.UIElements.ColorField(L10n.Tr("thumb.bgCustom")) { value = _custom, showAlpha = false };
                picker.AddToClassList("kn-field");
                picker.RegisterValueChangedCallback(e =>
                {
                    _custom = e.newValue;
                    RefreshPreview(avatar, preview);
                });
                root.Add(picker);
            }

            root.Add(Ui.Slider(L10n.Tr("thumb.zoom"), 70f, 140f, _zoom * 100f, v =>
            {
                _zoom = v / 100f;
                RefreshPreview(avatar, preview);
            }));

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("thumb.save"), () => Save(avatar), "kn-button--primary"));
            root.Add(buttons);
            root.Add(Ui.Notice(L10n.Tr("thumb.howToUse"), "ok"));
            return root;
        }

        public override void OnClosed()
        {
            if (_preview != null) Object.DestroyImmediate(_preview);
            _preview = null;
        }

        private static List<string> Labels(string[] keys)
        {
            var list = new List<string>();
            foreach (var k in keys) list.Add(L10n.Tr(k));
            return list;
        }

        private void RefreshPreview(VRCAvatarDescriptor avatar, VisualElement box)
        {
            if (_preview != null) Object.DestroyImmediate(_preview);
            _preview = Render(avatar, PreviewWidth, PreviewHeight);
            if (_preview != null) box.style.backgroundImage = new StyleBackground(_preview);
        }

        private static void Save(VRCAvatarDescriptor avatar)
        {
            var texture = Render(avatar, Width, Height);
            if (texture == null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("thumb.failed"), L10n.Tr("dialog.ok"));
                return;
            }

            var folder = WorkFolders.Ensure(WorkFolders.ForAvatar(avatar.gameObject));
            var path = folder + "/Thumbnail.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
            EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("thumb.saved", path), L10n.Tr("dialog.ok"));
        }

        // ---------------------------------------------------------------- 撮影

        /// <summary>一時的なカメラで撮る。シーンには何も残さない。</summary>
        private static Texture2D Render(VRCAvatarDescriptor avatar, int width, int height)
        {
            if (avatar == null) return null;

            Vector3 center;
            float span;
            Region(avatar, (Framing)_framing, out center, out span);

            var up = avatar.transform.up;
            var toTarget = Quaternion.AngleAxis(Angles[_angle], up) * -avatar.transform.forward;
            // 少しだけ上から見下ろすと、顔がきれいに見える
            toTarget = Quaternion.AngleAxis(4f, Vector3.Cross(up, toTarget)) * toTarget;

            var aspect = (float)width / height;
            var halfHeight = span * 0.5f / _zoom;
            var distance = halfHeight / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);

            var holder = new GameObject("KaihenNavi_ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            try
            {
                var camera = holder.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = FieldOfView;
                camera.aspect = aspect;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = distance + span * 4f + 10f;
                var background = _background == BackgroundKeys.Length - 1 ? (Color?)_custom : Backgrounds[_background];
                camera.clearFlags = background.HasValue ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
                if (background.HasValue) camera.backgroundColor = background.Value;
                camera.transform.position = center - toTarget * distance;
                camera.transform.rotation = Quaternion.LookRotation(toTarget, up);
                camera.targetTexture = texture;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = texture;
                var result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;
                return result;
            }
            finally
            {
                Object.DestroyImmediate(holder);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>写す範囲（中心と縦の長さ）。</summary>
        private static void Region(VRCAvatarDescriptor avatar, Framing framing, out Vector3 center, out float span)
        {
            var height = AvatarMeasure.Height(avatar);
            var feet = avatar.transform.position;
            var up = avatar.transform.up;
            var eyes = AvatarMeasure.EyeCenter(avatar) ?? feet + up * height * 0.92f;
            var hips = AvatarMeasure.Bone(avatar, HumanBodyBones.Hips);
            var hipsPos = hips != null ? hips.position : feet + up * height * 0.5f;

            switch (framing)
            {
                case Framing.Face:
                    span = height * 0.2f;
                    center = eyes - up * (height * 0.02f);
                    break;
                case Framing.Upper:
                    var top = feet + up * height;
                    var bottom = hipsPos - up * (height * 0.05f);
                    center = (top + bottom) * 0.5f;
                    span = Vector3.Distance(top, bottom) * 1.1f;
                    break;
                default:
                    center = feet + up * (height * 0.5f);
                    span = height * 1.1f;
                    break;
            }
        }
    }
}
