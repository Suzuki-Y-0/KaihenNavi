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
        internal static readonly string[] Floats =
        {
            Use, "_Main2ndTexBlendMode", "_Main2ndTexAlphaMode", "_Main2ndTexAngle",
            "_Main2ndTex_UVMode", "_Main2ndTex_Cull", "_Main2ndTexIsDecal",
            "_Main2ndTexIsLeftOnly", "_Main2ndTexIsRightOnly", "_Main2ndTexShouldCopy",
            "_Main2ndTexShouldFlipMirror", "_Main2ndTexShouldFlipCopy", "_Main2ndTexIsMSDF",
            "_Main2ndEnableLighting",
        };
        internal static readonly string[] Vectors =
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
        private readonly HashSet<SkinnedMeshRenderer> _selectionInitialized = new HashSet<SkinnedMeshRenderer>();
        private readonly InnerColorOpenness.Cache _openness = new InnerColorOpenness.Cache();
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
                _selectionInitialized.Clear();
                _openness.Clear();
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
                if (_selectionInitialized.Add(r) && DefaultExcluded(r.name)) _excluded.Add(r);
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
            public InnerColorOpenness.Geometry geometry;
            public Vector3[] positions;
            public Vector2[] uv;
            public readonly Dictionary<int, int[]> triangles = new Dictionary<int, int[]>();
            public readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
            public float[] values;
        }

        internal static bool DefaultExcluded(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return PartGuesser.LooksLikeCloth(name) || new[]
            {
                "acc", "ribbon", "pin", "clip", "earring", "飾", "リボン", "ピン",
            }.Any(lower.Contains);
        }

        private static List<SkinnedMeshRenderer> Occluders(VRCAvatarDescriptor avatar, List<SkinnedMeshRenderer> hair)
        {
            var all = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).ToList();
            var body = AvatarFinder.FindBody(avatar);
            var result = new HashSet<SkinnedMeshRenderer>(hair); // チェックを外した髪・髪飾りも遮る。
            if (body != null && body.sharedMesh != null) result.Add(body);
            if (avatar.VisemeSkinnedMesh != null && avatar.VisemeSkinnedMesh.sharedMesh != null) result.Add(avatar.VisemeSkinnedMesh);
            // 顔と体が分かれた商品にも対応。服や髪を避けた中で一番大きなメッシュも素体の候補にする。
            var largest = all.Where(r => !hair.Contains(r) && !PartGuesser.LooksLikeCloth(r.name))
                .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            if (largest != null) result.Add(largest);
            foreach (var r in all)
                if (!PartGuesser.LooksLikeCloth(r.name) && (PartGuesser.Guess(r, null, body) == BodyPart.Skin ||
                    r.sharedMaterials.Any(m => m != null && PartGuesser.Guess(r, m, body) == BodyPart.Skin))) result.Add(r);
            return result.OrderBy(r => r.GetInstanceID()).ToList();
        }

        private List<Surface> Bake(VRCAvatarDescriptor avatar)
        {
            var head = AvatarMeasure.Bone(avatar, HumanBodyBones.Head);
            if (head == null) throw new InvalidOperationException(L10n.Tr("innerColor.noHead"));
            var hair = HairRenderers(avatar);
            var surfaces = new List<Surface>();
            var baked = new Dictionary<SkinnedMeshRenderer, InnerColorOpenness.Geometry>();
            foreach (var r in hair.Where(r => !_excluded.Contains(r)))
            {
                var slots = HairSlots(avatar, r).Where(i => Supported(r.sharedMaterials[i])).ToList();
                if (slots.Count == 0) continue;
                var geometry = InnerColorOpenness.Bake(r);
                baked.Add(r, geometry);
                var s = new Surface { renderer = r, geometry = geometry, uv = geometry.uv };
                if (geometry.positions.Length == 0 || s.uv.Length != geometry.positions.Length)
                    throw new InvalidOperationException(L10n.Tr("innerColor.noUV", r.name));
                // 下のほう・後頭部の重みだけ、頭の中心とアバターの向きを基準にする。
                var rotation = Quaternion.Inverse(avatar.transform.rotation);
                s.positions = geometry.positions.Select(p => rotation * (p - head.position)).ToArray();
                foreach (var slot in slots)
                {
                    if (slot >= geometry.triangles.Length) continue;
                    s.triangles.Add(slot, geometry.triangles[slot]);
                    s.materials.Add(slot, r.sharedMaterials[slot]);
                }
                geometry.used = s.triangles.Values.SelectMany(t => t).Distinct().OrderBy(i => i).ToArray();
                if (s.triangles.Count > 0) surfaces.Add(s);
            }
            if (surfaces.Count == 0) return surfaces;
            List<float[]> openness = null;
            if (_mode != 1)
            {
                foreach (var r in Occluders(avatar, hair))
                    if (!baked.ContainsKey(r)) baked.Add(r, InnerColorOpenness.Bake(r));
                var geometry = baked.OrderBy(p => p.Key.GetInstanceID()).Select(p => p.Value).ToList();
                var measured = _openness.Get(InnerColorOpenness.CacheKey(avatar.GetInstanceID(), geometry),
                    () => InnerColorOpenness.Measure(geometry));
                openness = surfaces.Select(s => measured[geometry.IndexOf(s.geometry)]).ToList();
            }
            InnerColorMask.Classify(surfaces.Select(s => s.positions).ToList(),
                surfaces.Select(s => s.geometry.used).ToList(), openness,
                _mode, _thickness, _height, (i, values) => surfaces[i].values = values);
            return surfaces;
        }

        private void Preview(FeatureContext ctx)
        {
            CancelPending();
            if (ctx.Avatar == null || ctx.Avatar != _avatar || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var group = -1;
            InnerColorAssetUndo journal = null;
            try
            {
                var surfaces = Bake(ctx.Avatar);
                if (surfaces.Count == 0)
                {
                    Reset(ctx);
                    _status.text = L10n.Tr("innerColor.noneSelected");
                    return;
                }
                var state = InnerColorState.Load(ctx.Avatar);
                // プレビュー中のコピーを、その変更前のマテリアルへ正規化する。
                foreach (var surface in surfaces)
                    foreach (var slot in surface.materials.Keys.ToList())
                    {
                        var current = surface.materials[slot];
                        var entry = state == null ? null : state.data.entries.FirstOrDefault(e =>
                            e.materialPath == AssetDatabase.GetAssetPath(current));
                        var source = entry == null || entry.source == null ? null : entry.source.Load<Material>();
                        if (source != null) surface.materials[slot] = source;
                    }
                var materials = surfaces.SelectMany(s => s.materials.Values).Distinct().ToList();
                var conflicts = materials.Where(m => m.GetFloat(Use) > 0.5f &&
                    (state == null || !state.data.entries.Any(e => e.materialPath == AssetDatabase.GetAssetPath(m) ||
                        (e.source != null && e.source.Load<Material>() == m)))).ToList();
                if (conflicts.Count > 0 && !EditorUtility.DisplayDialog(L10n.Tr("feature.innerColor.title"),
                    L10n.Tr("innerColor.overwrite", string.Join(", ", conflicts.Select(m => m.name).ToArray())),
                    L10n.Tr("innerColor.overwriteYes"), L10n.Tr("innerColor.cancel"))) return;

                // 数値計算が全部成功してから、ファイルや見た目を変える。
                var masks = new Dictionary<Material, float[]>();
                foreach (var material in materials)
                {
                    var pixels = new float[_size * _size];
                    var scale = material.GetTextureScale("_MainTex");
                    var offset = material.GetTextureOffset("_MainTex");
                    foreach (var surface in surfaces)
                        foreach (var slot in surface.materials.Where(p => p.Value == material).Select(p => p.Key))
                            InnerColorMask.Paint(pixels, _size, surface.uv, surface.values, surface.triangles[slot], scale, offset);
                    masks.Add(material, pixels);
                }
                ctx.BeginEdit();
                UndoHelper.Separate();
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                var undoName = L10n.Tr("undo.innerColor");
                Undo.SetCurrentGroupName(undoName);
                journal = InnerColorAssetUndo.Begin(ctx.Avatar);
                state = InnerColorState.Load(ctx.Avatar, true);
                Undo.RecordObject(state, undoName);
                // チェックを外した髪と、選択範囲を変える前のコピーは元の割り当てに戻す。
                foreach (var entry in state.data.entries) RestoreSecond(ctx.Avatar, entry, undoName);
                foreach (var original in materials)
                {
                    var entry = OwnSelected(ctx.Avatar, original, surfaces, state, journal, undoName);
                    var owned = AssetDatabase.LoadAssetAtPath<Material>(entry.materialPath);
                    journal.Track(entry.maskPath);
                    var mask = InnerColorMask.Save(entry.maskPath, entry.key, masks[original], _size);
                    Undo.RecordObject(owned, undoName);
                    for (var i = 0; i < Floats.Length; i++)
                        if (owned.HasProperty(Floats[i])) owned.SetFloat(Floats[i], Floats[i] == Use || Floats[i] == "_Main2ndEnableLighting" ? 1 : 0);
                    for (var i = 0; i < Vectors.Length; i++)
                        if (owned.HasProperty(Vectors[i])) owned.SetVector(Vectors[i], VectorDefaults[i]);
                    owned.SetColor(Tint, _color);
                    owned.SetTexture(Main, null);
                    owned.SetTextureScale(Main, new Vector2(1, 1));
                    owned.SetTextureOffset(Main, new Vector2(0, 0));
                    owned.SetTexture(Mask, mask);
                    SyncMultiKeywords(owned);
                    EditorUtility.SetDirty(owned);
                }
                EditorUtility.SetDirty(state);
                journal.Commit(undoName); journal = null;
                Undo.CollapseUndoOperations(group); group = -1;
                _preview = true;
                _status.text = L10n.Tr("innerColor.done");
                SceneView.RepaintAll();
            }
            catch (Exception e)
            {
                if (group >= 0) Undo.RevertAllDownToGroup(group);
                if (journal != null) journal.Rollback();
                _status.text = L10n.Tr("innerColor.failed", e.Message);
                Debug.LogException(e);
            }
        }

        private static InnerColorState.Entry OwnSelected(VRCAvatarDescriptor avatar, Material original,
            List<Surface> surfaces, InnerColorState state, InnerColorAssetUndo journal, string undoName)
        {
            var assignments = new List<InnerColorState.Assignment>();
            foreach (var surface in surfaces)
                foreach (var slot in surface.materials.Where(p => p.Value == original).Select(p => p.Key))
                    assignments.Add(new InnerColorState.Assignment
                    {
                        rendererPath = HierarchyPath.Get(avatar.transform, surface.renderer.transform),
                        rendererIndex = Array.IndexOf(surface.renderer.GetComponents<Renderer>(), surface.renderer),
                        slot = slot, original = InnerColorState.AssetRef.Of(original),
                    });
            var source = InnerColorState.AssetRef.Of(original);
            var key = source.Key + "|" + string.Join("|", assignments.Select(a =>
                a.rendererPath.Length + ":" + a.rendererPath + ":" + a.rendererIndex + ":" + a.slot)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray());
            var entry = state.data.entries.FirstOrDefault(e => e.key == key);
            if (entry == null)
            {
                var materials = WorkFolders.Ensure(WorkFolders.Materials(avatar.gameObject));
                var textures = WorkFolders.Ensure(WorkFolders.ForAvatar(avatar.gameObject) + "/Textures");
                entry = new InnerColorState.Entry
                {
                    key = key, source = source, assignments = assignments,
                    before = InnerColorState.SecondState.Capture(original),
                    materialPath = InnerColorAssets.StablePath(materials, original.name, "_InnerColor.mat", key),
                    maskPath = InnerColorAssets.StablePath(textures, original.name, "_InnerMask.png", key),
                };
                state.data.entries.Add(entry);
            }
            journal.Track(entry.materialPath);
            var owned = MaterialCopier.EnsureInnerColorCopy(original, entry.materialPath, key, undoName);
            foreach (var assignment in entry.assignments)
            {
                var renderer = InnerColorState.RendererFor(avatar, assignment);
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                if (assignment.slot >= mats.Length || mats[assignment.slot] != original) continue;
                Undo.RecordObject(renderer, undoName);
                mats[assignment.slot] = owned;
                renderer.sharedMaterials = mats;
            }
            return entry;
        }

        private void Reset(FeatureContext ctx)
        {
            CancelPending();
            if (ctx.Avatar == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var state = InnerColorState.Load(ctx.Avatar);
            var candidates = InnerColorAssets.Candidates(ctx.Avatar, state);
            if (state == null && candidates.Count == 0) return;
            ctx.BeginEdit();
            var undoName = L10n.Tr("undo.innerColor");
            UndoHelper.Separate(); UndoHelper.Begin("innerColor-reset", undoName);
            var journal = InnerColorAssetUndo.Begin(ctx.Avatar);
            try
            {
                // 変更前のファイルを保存する。削除後の Ctrl+Z でも GUID と内容を復元できる。
                foreach (var path in candidates) journal.Track(path);
                if (state != null)
                {
                    Undo.RecordObject(state, undoName);
                    foreach (var entry in state.data.entries) RestoreSecond(ctx.Avatar, entry, undoName);
                    state.data = new InnerColorState.Data();
                    EditorUtility.SetDirty(state);
                }
                AssetDatabase.SaveAssets();
                // 自分用 Prefab の古い割り当てが、片付けを妨げないよう先に保存する。
                MyAvatar.Save(ctx.Avatar);
                InnerColorAssets.Cleanup(candidates, journal);
                journal.Commit(undoName);
                UndoHelper.End(); UndoHelper.Separate();
            }
            catch (Exception e)
            {
                journal.Rollback();
                UndoHelper.End(); UndoHelper.Separate();
                _status.text = L10n.Tr("innerColor.failed", e.Message);
                Debug.LogException(e);
                return;
            }
            _preview = false;
            _status.text = L10n.Tr("innerColor.restored");
            SceneView.RepaintAll();
        }

        private static void RestoreSecond(VRCAvatarDescriptor avatar, InnerColorState.Entry entry, string undoName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(entry.materialPath);
            if (material == null || !WorkFolders.IsOwnedBy(material, avatar.gameObject)) return;
            // 旧版の控えは読み込めるが、新しい .mat は作らない。
            if (entry.before == null && !string.IsNullOrEmpty(entry.originalPath))
            {
                var before = AssetDatabase.LoadAssetAtPath<Material>(entry.originalPath);
                if (before != null) entry.before = InnerColorState.SecondState.Capture(before);
            }
            if (entry.before != null)
            {
                Undo.RecordObject(material, undoName);
                entry.before.Apply(material);
            }
            foreach (var assignment in entry.assignments)
            {
                var renderer = InnerColorState.RendererFor(avatar, assignment);
                var original = assignment.original.Load<Material>();
                if (renderer == null || original == null) continue;
                var mats = renderer.sharedMaterials;
                if (assignment.slot >= mats.Length || mats[assignment.slot] != material) continue;
                Undo.RecordObject(renderer, undoName);
                mats[assignment.slot] = original;
                renderer.sharedMaterials = mats;
            }
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
