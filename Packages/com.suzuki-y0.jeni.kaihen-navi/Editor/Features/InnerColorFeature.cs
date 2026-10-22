using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    internal sealed class InnerColorFeature : FeatureBase
    {
        // lilToon 公式ソースで確認（2026-10-22）。Main2nd のプロパティと既定値:
        // https://github.com/lilxyzw/lilToon/blob/master/Assets/lilToon/Shader/lts.shader
        // マスクは fd.uvMain（MainTex の拡縮・移動を含む UV0）の R を読む:
        // https://github.com/lilxyzw/lilToon/blob/master/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl
        private const string Use = "_UseMain2ndTex";
        private const string Tint = "_Color2nd";
        private const string Main = "_Main2ndTex";
        private const string Mask = "_Main2ndBlendMask";
        private static readonly string[] Floats =
        {
            Use, "_Main2ndTexBlendMode", "_Main2ndTexAlphaMode", "_Main2ndTexAngle",
            "_Main2ndTex_UVMode", "_Main2ndTex_Cull", "_Main2ndTexIsDecal",
            "_Main2ndTexIsLeftOnly", "_Main2ndTexIsRightOnly", "_Main2ndTexShouldCopy",
            "_Main2ndTexShouldFlipMirror", "_Main2ndTexShouldFlipCopy", "_Main2ndTexIsMSDF",
            "_Main2ndEnableLighting",
        };
        private static readonly string[] Vectors =
        {
            "_Main2ndTex_ScrollRotate", "_Main2ndDissolveParams", "_Main2ndDistanceFade",
            "_Main2ndTexDecalAnimation", "_Main2ndTexDecalSubParam",
        };
        private static readonly Vector4[] VectorDefaults =
        {
            new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0.5f, 0.1f), new Vector4(0.1f, 0.01f, 0, 0),
            new Vector4(1, 1, 1, 30), new Vector4(1, 1, 0, 1),
        };
        private static readonly string[] ModeKeys = { "layers", "lower", "rear" };
        private static readonly string[] ColorKeys = { "pink", "blue", "purple", "silver", "black" };
        private static readonly Color[] Colors =
        {
            new Color(1f, 0.35f, 0.6f), new Color(0.25f, 0.65f, 1f), new Color(0.65f, 0.35f, 1f),
            new Color(0.85f, 0.88f, 0.95f), new Color(0.06f, 0.05f, 0.08f),
        };

        private VRCAvatarDescriptor _avatar;
        private readonly HashSet<SkinnedMeshRenderer> _excluded = new HashSet<SkinnedMeshRenderer>();
        private Color _color = Colors[0];
        private int _mode;
        private int _size = 512;
        private float _thickness = 0.08f;
        private float _height = 0.5f;
        private bool _preview;
        private VisualElement _root;
        private Label _status;
        private IVisualElementScheduledItem _pending;

        public override string Id { get { return "innerColor"; } }
        public override int Number { get { return 13; } }
        public override Color Accent { get { return Ui.Hex("#B78BE8"); } }
        public override bool InTrial { get { return false; } }
        public override bool RefreshOnHierarchyChange { get { return true; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            CancelPending();
            if (_avatar != ctx.Avatar)
            {
                _avatar = ctx.Avatar;
                _excluded.Clear();
            }
            var state = InnerColorState.Load(ctx.Avatar);
            _preview = state != null && state.data.entries.Count > 0;
            _root = new VisualElement();
            _root.Add(Ui.Notice(L10n.Tr("innerColor.intro"), "info"));
            var targets = HairRenderers(ctx.Avatar);
            _root.Add(Ui.Heading(L10n.Tr("innerColor.which")));
            foreach (var renderer in targets)
            {
                var r = renderer;
                var hair = HairSlots(ctx.Avatar, r).Select(i => r.sharedMaterials[i]).ToList();
                var supported = hair.Any(Supported);
                var row = Ui.Box("kn-list__row");
                var toggle = new Toggle(r.name) { value = supported && !_excluded.Contains(r) };
                toggle.SetEnabled(supported);
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _excluded.Remove(r); else _excluded.Add(r);
                    Queue(ctx);
                });
                row.Add(toggle);
                _root.Add(row);
                if (hair.Any(m => !IsLil(m))) _root.Add(Ui.Text(L10n.Tr("innerColor.lilOnly", r.name), "kn-hint"));
                if (hair.Any(m => IsLil(m) && !Supported(m)))
                    _root.Add(Ui.Text(L10n.Tr("innerColor.noSecond", r.name), "kn-hint"));
            }
            if (targets.Count == 0) _root.Add(Ui.Notice(L10n.Tr("innerColor.noHair"), "warn"));
            if (AvatarMeasure.Bone(ctx.Avatar, HumanBodyBones.Head) == null)
                _root.Add(Ui.Notice(L10n.Tr("innerColor.noHead"), "warn"));

            var color = new ColorField(L10n.Tr("innerColor.color")) { value = _color, showAlpha = false, hdr = false };
            color.RegisterValueChangedCallback(e => { _color = e.newValue; _color.a = 1f; Queue(ctx); });
            _root.Add(color);
            _root.Add(Ui.Chips(ColorKeys.Select(k => L10n.Tr("innerColor.color." + k)).ToList(), -1, i =>
            {
                _color = Colors[i]; color.SetValueWithoutNotify(_color); Queue(ctx);
            }));
            _root.Add(Ui.Heading(L10n.Tr("innerColor.method")));
            _root.Add(Ui.Chips(ModeKeys.Select(k => L10n.Tr("innerColor.mode." + k)).ToList(), _mode, i =>
            {
                _mode = i; ctx.Rebuild(); Queue(ctx);
            }));
            if (_mode == 1)
                _root.Add(Ui.Slider(L10n.Tr("innerColor.height"), 0, 100, _height * 100, v => { _height = v / 100; Queue(ctx); }));
            else
                _root.Add(Ui.Slider(L10n.Tr("innerColor.thickness"), 1, 40, _thickness * 100, v => { _thickness = v / 100; Queue(ctx); }));
            _root.Add(Ui.Text(L10n.Tr(_mode == 1 ? "innerColor.heightHint" : "innerColor.thicknessHint"), "kn-hint"));
            _root.Add(Ui.Heading(L10n.Tr("innerColor.quality")));
            _root.Add(Ui.Chips(new List<string> { L10n.Tr("innerColor.size512"), L10n.Tr("innerColor.size1024") }, _size == 512 ? 0 : 1,
                i => { _size = i == 0 ? 512 : 1024; Queue(ctx); }));
            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("innerColor.preview"), () => Preview(ctx)));
            buttons.Add(Ui.Button(L10n.Tr("innerColor.reset"), () => Reset(ctx)));
            _root.Add(buttons);
            _status = Ui.Text("", "kn-hint");
            _root.Add(_status);
            _root.Add(Ui.Text(L10n.Tr("innerColor.uvHint"), "kn-hint"));
            return _root;
        }

        public override void OnClosed() { CancelPending(); _preview = false; _root = null; }
        private void CancelPending() { if (_pending != null) _pending.Pause(); _pending = null; }
        private void Queue(FeatureContext ctx)
        {
            CancelPending();
            if (_preview && _root != null) _pending = _root.schedule.Execute(() => Preview(ctx)).StartingIn(300);
        }

        private static bool IsLil(Material m) { return m != null && m.HasProperty("_lilToonVersion"); }
        private static bool Supported(Material m)
        {
            return IsLil(m) && m.HasProperty(Use) && m.HasProperty(Tint) && m.HasProperty(Main) &&
                m.HasProperty(Mask) && m.HasProperty("_Main2ndTexBlendMode");
        }
        private static List<int> HairSlots(VRCAvatarDescriptor avatar, SkinnedMeshRenderer r)
        {
            var result = new List<int>();
            var body = AvatarFinder.FindBody(avatar);
            var mats = r.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
                if (mats[i] != null && PartGuesser.Guess(r, mats[i], body) == BodyPart.Hair) result.Add(i);
            return result;
        }
        private static List<SkinnedMeshRenderer> HairRenderers(VRCAvatarDescriptor avatar)
        {
            return avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && HairSlots(avatar, r).Count > 0).OrderBy(r => r.name).ToList();
        }

        private sealed class Surface
        {
            public SkinnedMeshRenderer renderer;
            public Vector3[] positions;
            public Vector2[] uv;
            public readonly Dictionary<int, int[]> triangles = new Dictionary<int, int[]>();
            public readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
            public float[] values;
        }

        private List<Surface> Bake(VRCAvatarDescriptor avatar)
        {
            var head = AvatarMeasure.Bone(avatar, HumanBodyBones.Head);
            if (head == null) throw new InvalidOperationException(L10n.Tr("innerColor.noHead"));
            var surfaces = new List<Surface>();
            foreach (var r in HairRenderers(avatar).Where(r => !_excluded.Contains(r)))
            {
                var slots = HairSlots(avatar, r).Where(i => Supported(r.sharedMaterials[i])).ToList();
                if (slots.Count == 0) continue;
                var mesh = new Mesh();
                try
                {
                    // useScale=false: 頂点は renderer のローカル座標。TransformPoint でスケールを一度だけ適用。
                    r.BakeMesh(mesh, false);
                    var s = new Surface { renderer = r, positions = mesh.vertices, uv = mesh.uv };
                    if (s.positions.Length == 0 || s.uv.Length != s.positions.Length)
                        throw new InvalidOperationException(L10n.Tr("innerColor.noUV", r.name));
                    // 頭の中心から測り、向きはアバターを基準にする（Head の骨の軸は商品ごとに異なる）。
                    var rotation = Quaternion.Inverse(avatar.transform.rotation);
                    for (var i = 0; i < s.positions.Length; i++)
                        s.positions[i] = rotation * (r.transform.TransformPoint(s.positions[i]) - head.position);
                    foreach (var slot in slots)
                    {
                        if (slot >= mesh.subMeshCount) continue;
                        s.triangles.Add(slot, mesh.GetTriangles(slot));
                        s.materials.Add(slot, r.sharedMaterials[slot]);
                    }
                    if (s.triangles.Count > 0) surfaces.Add(s);
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            if (surfaces.Count == 0) return surfaces;
            InnerColorMask.Classify(surfaces.Select(s => s.positions).ToList(),
                surfaces.Select(s => s.triangles.Values.SelectMany(t => t).Distinct().ToArray()).ToList(),
                _mode, _thickness, _height, (i, values) => surfaces[i].values = values);
            return surfaces;
        }

        private void Preview(FeatureContext ctx)
        {
            CancelPending();
            if (ctx.Avatar == null || ctx.Avatar != _avatar || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var group = -1;
            try
            {
                var surfaces = Bake(ctx.Avatar);
                if (surfaces.Count == 0)
                {
                    Reset(ctx);
                    _status.text = L10n.Tr("innerColor.noneSelected");
                    return;
                }
                var materials = surfaces.SelectMany(s => s.materials.Values).Distinct().ToList();
                var state = InnerColorState.Load(ctx.Avatar);
                var conflicts = materials.Where(m => m.GetFloat(Use) > 0.5f &&
                    (state == null || !state.data.entries.Any(e => e.materialPath == AssetDatabase.GetAssetPath(m)))).ToList();
                if (conflicts.Count > 0 && !EditorUtility.DisplayDialog(L10n.Tr("feature.innerColor.title"),
                    L10n.Tr("innerColor.overwrite", string.Join(", ", conflicts.Select(m => m.name).ToArray())),
                    L10n.Tr("innerColor.overwriteYes"), L10n.Tr("innerColor.cancel"))) return;

                // 全画像を完成させてから見た目を変える。画像は不変にし、Undo/バックアップの参照先を壊さない。
                var masks = new Dictionary<Material, Texture2D>();
                foreach (var material in materials)
                {
                    var pixels = new float[_size * _size];
                    var scale = material.GetTextureScale("_MainTex");
                    var offset = material.GetTextureOffset("_MainTex");
                    foreach (var s in surfaces)
                        foreach (var slot in s.materials.Where(p => p.Value == material).Select(p => p.Key))
                            InnerColorMask.Paint(pixels, _size, s.uv, s.values, s.triangles[slot], scale, offset);
                    masks.Add(material, InnerColorMask.Save(ctx.Avatar, material.name, pixels, _size));
                }
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                var undoName = L10n.Tr("undo.innerColor");
                Undo.SetCurrentGroupName(undoName);
                state = InnerColorState.Load(ctx.Avatar, true);
                Undo.RecordObject(state, undoName);
                var active = new HashSet<Material>();
                foreach (var original in materials)
                {
                    var owned = OwnSelected(ctx.Avatar, original, surfaces, undoName);
                    active.Add(owned);
                    var path = AssetDatabase.GetAssetPath(owned);
                    if (!state.data.entries.Any(e => e.materialPath == path))
                    {
                        var previous = state.data.entries.FirstOrDefault(e => e.materialPath == AssetDatabase.GetAssetPath(original));
                        var baseline = previous == null ? null : AssetDatabase.LoadAssetAtPath<Material>(previous.originalPath);
                        var before = Object.Instantiate(baseline != null ? baseline : owned);
                        before.name = owned.name;
                        var beforeFolder = WorkFolders.Ensure(WorkFolders.Materials(ctx.Avatar.gameObject));
                        var beforePath = AssetDatabase.GenerateUniqueAssetPath(beforeFolder + "/" +
                            WorkFolders.Sanitize(owned.name) + "_BeforeInnerColor.mat");
                        AssetDatabase.CreateAsset(before, beforePath);
                        state.data.entries.Add(new InnerColorState.Entry { materialPath = path, originalPath = beforePath });
                    }
                    Undo.RecordObject(owned, undoName);
                    for (var i = 0; i < Floats.Length; i++)
                        if (owned.HasProperty(Floats[i])) owned.SetFloat(Floats[i], Floats[i] == Use || Floats[i] == "_Main2ndEnableLighting" ? 1 : 0);
                    for (var i = 0; i < Vectors.Length; i++)
                        if (owned.HasProperty(Vectors[i])) owned.SetVector(Vectors[i], VectorDefaults[i]);
                    owned.SetColor(Tint, _color);
                    owned.SetTexture(Main, null); // shader の既定の白。元の 2nd 画像の影響を残さない。
                    owned.SetTextureScale(Main, new Vector2(1, 1));
                    owned.SetTextureOffset(Main, new Vector2(0, 0));
                    owned.SetTexture(Mask, masks[original]);
                    SyncMultiKeywords(owned);
                    EditorUtility.SetDirty(owned);
                }
                // チェックを外した髪は、前回のプレビュー前の値へ戻す。
                foreach (var entry in state.data.entries)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(entry.materialPath);
                    if (material != null && !active.Contains(material)) RestoreSecond(ctx.Avatar, entry, undoName);
                }
                EditorUtility.SetDirty(state);
                Undo.CollapseUndoOperations(group); group = -1;
                AssetDatabase.SaveAssets();
                _preview = true;
                _status.text = L10n.Tr("innerColor.done");
                SceneView.RepaintAll();
            }
            catch (Exception e)
            {
                if (group >= 0) Undo.RevertAllDownToGroup(group);
                _status.text = L10n.Tr("innerColor.failed", e.Message);
                Debug.LogException(e);
            }
        }

        private static Material OwnSelected(VRCAvatarDescriptor avatar, Material original, List<Surface> surfaces, string undoName)
        {
            var selected = new Dictionary<SkinnedMeshRenderer, HashSet<int>>();
            foreach (var s in surfaces)
            {
                var slots = s.materials.Where(p => p.Value == original).Select(p => p.Key).ToList();
                if (slots.Count > 0) selected[s.renderer] = new HashSet<int>(slots);
            }
            var sharedOutside = false;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                HashSet<int> slots = null;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) selected.TryGetValue(smr, out slots);
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                    if (mats[i] == original && (slots == null || !slots.Contains(i))) sharedOutside = true;
            }
            if (!sharedOutside) return MaterialCopier.EnsureOwned(avatar, original, undoName);

            // EnsureOwned の全参照差し替えを避けるため、一時複製を渡して所有アセットを作り、選んだ枠だけへ割り当てる。
            var temporary = Object.Instantiate(original);
            temporary.name = original.name;
            Material owned;
            try { owned = MaterialCopier.EnsureOwned(avatar, temporary, undoName); }
            finally { Object.DestroyImmediate(temporary); }
            foreach (var pair in selected)
            {
                Undo.RecordObject(pair.Key, undoName);
                var mats = pair.Key.sharedMaterials;
                foreach (var slot in pair.Value) mats[slot] = owned;
                pair.Key.sharedMaterials = mats;
            }
            return owned;
        }

        private void Reset(FeatureContext ctx)
        {
            CancelPending();
            var state = InnerColorState.Load(ctx.Avatar);
            if (state == null || state.data.entries.Count == 0) return;
            ctx.BeginEdit();
            var undoName = L10n.Tr("undo.innerColor");
            UndoHelper.Separate(); UndoHelper.Begin("innerColor-reset", undoName);
            Undo.RecordObject(state, undoName);
            foreach (var entry in state.data.entries) RestoreSecond(ctx.Avatar, entry, undoName);
            state.data = new InnerColorState.Data();
            EditorUtility.SetDirty(state);
            UndoHelper.End(); UndoHelper.Separate();
            AssetDatabase.SaveAssets();
            _preview = false;
            _status.text = L10n.Tr("innerColor.restored");
            SceneView.RepaintAll();
        }

        private static void RestoreSecond(VRCAvatarDescriptor avatar, InnerColorState.Entry entry, string undoName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(entry.materialPath);
            var before = AssetDatabase.LoadAssetAtPath<Material>(entry.originalPath);
            if (material == null || before == null || !WorkFolders.IsOwnedBy(material, avatar.gameObject)) return;
            Undo.RecordObject(material, undoName);
            foreach (var property in Floats)
                if (material.HasProperty(property) && before.HasProperty(property)) material.SetFloat(property, before.GetFloat(property));
            foreach (var property in Vectors)
                if (material.HasProperty(property) && before.HasProperty(property)) material.SetVector(property, before.GetVector(property));
            if (material.HasProperty(Tint) && before.HasProperty(Tint)) material.SetColor(Tint, before.GetColor(Tint));
            foreach (var property in new[] { Main, Mask })
            {
                if (!material.HasProperty(property) || !before.HasProperty(property)) continue;
                material.SetTexture(property, before.GetTexture(property));
                material.SetTextureScale(property, before.GetTextureScale(property));
                material.SetTextureOffset(property, before.GetTextureOffset(property));
            }
            SyncMultiKeywords(material);
            EditorUtility.SetDirty(material);
        }

        // Multi 版はプロパティだけでなくキーワードも必要。2nd/3rd 共用のものは 3rd の状態を保つ。
        // https://github.com/lilxyzw/lilToon/blob/master/Assets/lilToon/Editor/lilMaterialUtils.cs (SetupMultiMaterial)
        // https://github.com/lilxyzw/lilToon/blob/master/Assets/lilToon/Editor/lilShaderUtils.cs (IsMultiShaderName)
        private static void SyncMultiKeywords(Material material)
        {
            if (material.shader == null) return;
            var name = material.shader.name;
            var separator = name.LastIndexOf('/');
            if (separator < 0 || name.IndexOf("Multi", separator + 1, StringComparison.Ordinal) < 0) return;
            var second = material.GetFloat(Use) > 0.5f;
            var third = material.HasProperty("_UseMain3rdTex") && material.GetFloat("_UseMain3rdTex") > 0.5f;
            Keyword(material, "_COLORADDSUBDIFF_ON", second);
            Keyword(material, "_SUNDISK_NONE", (second && AnimatedDecal(material, "_Main2ndTexDecalAnimation")) ||
                (third && AnimatedDecal(material, "_Main3rdTexDecalAnimation")));
            Keyword(material, "GEOM_TYPE_FROND", (second && material.HasProperty("_Main2ndDissolveParams") &&
                material.GetVector("_Main2ndDissolveParams").x != 0) ||
                (third && material.HasProperty("_Main3rdDissolveParams") && material.GetVector("_Main3rdDissolveParams").x != 0));
        }
        private static bool AnimatedDecal(Material material, string property)
        {
            if (!material.HasProperty(property)) return false;
            var v = material.GetVector(property);
            return v.x != 1 || v.y != 1 || v.z != 1 || v.w != 30;
        }
        private static void Keyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
        }
    }
}
