using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 7：視点の高さを直したい（製品版）。
    /// VRChat の中で「目の高さ」になる場所（View Position）を、目の位置に自動で合わせ、矢印で少しずつ直す。
    /// シーンには、視点の場所を目立つ印で表示する。
    /// </summary>
    internal sealed class ViewFeature : FeatureBase
    {
        private static readonly float[] Steps = { 0.002f, 0.005f, 0.02f };
        private static int _stepIndex = 1;

        private VRCAvatarDescriptor _shown;
        private Label _readout;

        public override string Id { get { return "view"; } }
        public override int Number { get { return 7; } }
        public override Color Accent { get { return Ui.Hex("#5DA9E9"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var avatar = ctx.Avatar;
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("view.intro"), "info"));

            ShowMarker(avatar);

            _readout = Ui.Text("", "kn-readout");
            root.Add(_readout);
            UpdateReadout(avatar);

            var suggested = AvatarMeasure.SuggestedViewPosition(avatar);
            var auto = Ui.Box("kn-row");
            var autoButton = Ui.Button(L10n.Tr("view.auto"), () =>
            {
                if (!suggested.HasValue) return;
                Set(ctx, suggested.Value, L10n.Tr("undo.viewAuto"));
            }, "kn-button--primary");
            autoButton.SetEnabled(suggested.HasValue);
            auto.Add(autoButton);
            auto.Add(Ui.Button(L10n.Tr("view.look"), () => Frame(avatar), "kn-button--ghost"));
            root.Add(auto);
            if (!suggested.HasValue) root.Add(Ui.Notice(L10n.Tr("view.noEyes"), "warn"));

            // 少しずつ直す
            root.Add(Ui.Heading(L10n.Tr("view.adjust")));
            var stepLabels = new System.Collections.Generic.List<string>
            {
                L10n.Tr("transform.stepFine"), L10n.Tr("transform.stepNormal"), L10n.Tr("transform.stepLarge"),
            };
            root.Add(Ui.Chips(stepLabels, _stepIndex, i =>
            {
                _stepIndex = i;
                ctx.Rebuild();
            }));

            var pads = Ui.Box("kn-pads");
            var vertical = Ui.Box("kn-pad", "kn-pad--vertical");
            vertical.Add(Ui.Arrow("↑", L10n.Tr("transform.up"), () => Move(ctx, Vector3.up)));
            vertical.Add(Ui.Arrow("↓", L10n.Tr("transform.down"), () => Move(ctx, Vector3.down)));
            pads.Add(vertical);
            var depth = Ui.Box("kn-pad", "kn-pad--vertical");
            depth.Add(Ui.Arrow("↑", L10n.Tr("transform.forward"), () => Move(ctx, Vector3.forward)));
            depth.Add(Ui.Arrow("↓", L10n.Tr("transform.back"), () => Move(ctx, Vector3.back)));
            pads.Add(depth);
            root.Add(pads);
            root.Add(Ui.Text(L10n.Tr("view.tip"), "kn-hint"));
            return root;
        }

        public override void OnClosed()
        {
            HideMarker();
        }

        private void Set(FeatureContext ctx, Vector3 value, string undoName)
        {
            var avatar = ctx.Avatar;
            ctx.BeginEdit("view");
            UndoHelper.Begin("view", undoName);
            Undo.RecordObject(avatar, undoName);
            avatar.ViewPosition = value;
            EditorUtility.SetDirty(avatar);
            UndoHelper.End();
            UpdateReadout(avatar);
            SceneView.RepaintAll();
        }

        private void Move(FeatureContext ctx, Vector3 direction)
        {
            var avatar = ctx.Avatar;
            Set(ctx, avatar.ViewPosition + direction * Steps[_stepIndex], L10n.Tr("undo.viewMove"));
        }

        private void UpdateReadout(VRCAvatarDescriptor avatar)
        {
            if (_readout == null || avatar == null) return;
            var v = avatar.ViewPosition;
            _readout.text = L10n.Tr("view.readout", v.y.ToString("0.000"), v.z.ToString("0.000"));
        }

        // ---------------------------------------------------------------- シーンの印

        private void ShowMarker(VRCAvatarDescriptor avatar)
        {
            HideMarker();
            _shown = avatar;
            SceneView.duringSceneGui += DrawMarker;
            SceneView.RepaintAll();
        }

        private void HideMarker()
        {
            SceneView.duringSceneGui -= DrawMarker;
            _shown = null;
            SceneView.RepaintAll();
        }

        private void DrawMarker(SceneView view)
        {
            if (_shown == null) return;
            var world = _shown.transform.TransformPoint(_shown.ViewPosition);
            // 小さすぎて見つけにくかったので、画面上でいつも同じくらいの大きさ（指先ほど）に見えるようにする
            var size = HandleUtility.GetHandleSize(world) * 0.22f;
            var zTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always; // 頭の中にあっても見えるように
            Handles.color = new Color(0.227f, 0.475f, 0.733f, 0.95f);
            Handles.SphereHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
            Handles.color = Color.white;
            var toCamera = view.camera != null ? view.camera.transform.forward : Vector3.forward;
            Handles.DrawWireDisc(world, toCamera, size * 0.6f, 2f);
            Handles.color = new Color(0.227f, 0.475f, 0.733f, 0.95f);
            Handles.DrawLine(world, world + _shown.transform.forward * size * 3f, 3f);
            Handles.ConeHandleCap(0, world + _shown.transform.forward * size * 3f, Quaternion.LookRotation(_shown.transform.forward), size * 0.6f, EventType.Repaint);
            Handles.zTest = zTest;
        }

        private static void Frame(VRCAvatarDescriptor avatar)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            var world = avatar.transform.TransformPoint(avatar.ViewPosition);
            var height = AvatarMeasure.Height(avatar);
            view.LookAt(world, Quaternion.LookRotation(-avatar.transform.forward + avatar.transform.right * 0.6f), height * 0.12f);
        }
    }
}
