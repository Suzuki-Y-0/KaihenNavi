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
    /// <summary>今の姿勢で髪の面を選び、複製メッシュの内側用の枠へ移す。</summary>
    internal sealed class InnerColorFeature : FeatureBase
    {
        // 旧方式の台帳を「元に戻す」で読めるよう、2nd カラーの項目だけ残す。
        private const string Use = "_UseMain2ndTex";
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
        private static readonly string[] ColorKeys = { "pink", "blue", "purple", "silver", "black" };
        private static readonly Color[] Colors =
        {
            new Color(1f, 0.35f, 0.6f), new Color(0.25f, 0.65f, 1f), new Color(0.65f, 0.35f, 1f),
            new Color(0.85f, 0.88f, 0.95f), new Color(0.06f, 0.05f, 0.08f),
        };
        private VRCAvatarDescriptor _avatar;
        private FeatureContext _context;
        private readonly HashSet<SkinnedMeshRenderer> _excluded = new HashSet<SkinnedMeshRenderer>();
        private readonly HashSet<SkinnedMeshRenderer> _selectionInitialized = new HashSet<SkinnedMeshRenderer>();
        private readonly Dictionary<SkinnedMeshRenderer, int> _slots = new Dictionary<SkinnedMeshRenderer, int>();
        private Color _color = Colors[0];
        private float _range = 56.25f;
        private float _height;
        private float _brightness = 1f;
        private bool _preview;
        private bool _draggingSlider;
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
            _context = ctx;
            InnerColorAssetUndo.Replayed -= OnAssetsReplayed;
            InnerColorAssetUndo.Replayed += OnAssetsReplayed;
            _draggingSlider = false;
            if (_avatar != ctx.Avatar)
            {
                _avatar = ctx.Avatar;
                _excluded.Clear(); _selectionInitialized.Clear(); _slots.Clear();
                _color = Colors[0]; _range = 56.25f; _height = 0; _brightness = 1;
            }
            var state = InnerColorState.Load(ctx.Avatar);
            _preview = state != null && (state.data.meshes.Count > 0 || state.data.entries.Count > 0);
            if (state != null && state.data.hasSettings)
            {
                _color = state.data.color; _range = state.data.range;
                _height = state.data.height; _brightness = state.data.brightness;
            }
            _root = new VisualElement();
            _root.Add(Ui.Notice(L10n.Tr("innerColor.intro"), "info"));
            _root.Add(Ui.Heading(L10n.Tr("innerColor.which")));
            var targets = HairRenderers(ctx.Avatar, state);
            foreach (var renderer in targets)
            {
                var r = renderer;
                var saved = MeshEntry(ctx.Avatar, state, r);
                if (_selectionInitialized.Add(r))
                {
                    if (DefaultExcluded(r.name) && saved == null) _excluded.Add(r);
                }
                if (state != null && state.data.hasSettings)
                {
                    if (saved != null) _excluded.Remove(r); else _excluded.Add(r);
                }
                var materials = OriginalMaterials(ctx.Avatar, state, r);
                var slots = HairSlots(ctx.Avatar, r, materials);
                var row = Ui.Box("kn-list__row");
                var toggle = new Toggle(r.name) { value = !_excluded.Contains(r) };
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _excluded.Remove(r); else _excluded.Add(r);
                    Queue(ctx);
                });
                row.Add(toggle); _root.Add(row);
                var selected = saved == null ? (_slots.ContainsKey(r) ? _slots[r] : slots[0]) : saved.sourceSlot;
                if (!slots.Contains(selected)) selected = slots[0];
                _slots[r] = selected;
                // 同じマテリアルの枠はまとめて選ぶ。髪ごとに内側用の枠は 1 つだけ足す。
                if (slots.Count > 1)
                    _root.Add(Ui.Chips(slots.Select(i => materials[i].name).ToList(), slots.IndexOf(selected), i =>
                    {
                        _slots[r] = slots[i]; Queue(ctx);
                    }));
                if (slots.Any(i => !IsLil(materials[i])))
                    _root.Add(Ui.Text(L10n.Tr("innerColor.otherShader", r.name), "kn-hint"));
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
            _root.Add(DeferredSlider(ctx, "innerColor.brightness", 25, 400, _brightness * 100, v => _brightness = v / 100));
            _root.Add(Ui.Text(L10n.Tr("innerColor.brightnessHint"), "kn-hint"));
            _root.Add(DeferredSlider(ctx, "innerColor.thickness", 0, 100, _range, v => _range = v));
            _root.Add(Ui.Text(L10n.Tr("innerColor.thicknessHint"), "kn-hint"));
            _root.Add(DeferredSlider(ctx, "innerColor.height", 0, 100, _height * 100, v => _height = v / 100));
            _root.Add(Ui.Text(L10n.Tr("innerColor.heightHint"), "kn-hint"));
            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("innerColor.preview"), () => Preview(ctx)));
            buttons.Add(Ui.Button(L10n.Tr("innerColor.reset"), () => Reset(ctx)));
            _root.Add(buttons);
            _status = Ui.Text("", "kn-hint"); _root.Add(_status);
            _root.Add(Ui.Text(L10n.Tr("innerColor.materialHint"), "kn-hint"));
            return _root;
        }

        /// <summary>ドラッグの途中で止まっても反映を分けず、指を離した後に 1 手で反映する。</summary>
        private Slider DeferredSlider(FeatureContext ctx, string key, float min, float max, float value, Action<float> change)
        {
            var slider = Ui.Slider(L10n.Tr(key), min, max, value, v => { change(v); Queue(ctx); });
            slider.RegisterCallback<PointerDownEvent>(e => { _draggingSlider = true; CancelPending(); }, TrickleDown.TrickleDown);
            Action finish = () =>
            {
                if (!_draggingSlider) return;
                _draggingSlider = false; Queue(ctx);
            };
            slider.RegisterCallback<PointerUpEvent>(e => finish(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(e => finish());
            slider.RegisterCallback<FocusOutEvent>(e => finish());
            return slider;
        }

        public override void OnClosed()
        {
            CancelPending(); _draggingSlider = false; _preview = false; _root = null; _context = null;
            InnerColorAssetUndo.Replayed -= OnAssetsReplayed;
        }
        private void OnAssetsReplayed()
        {
            // ウィンドウの Undo 通知より後にファイルが戻る場合も、戻った台帳と選択を表示する。
            if (_root != null && _context != null && _context.Rebuild != null) _context.Rebuild();
        }
        private void CancelPending() { if (_pending != null) _pending.Pause(); _pending = null; }
        private void Queue(FeatureContext ctx)
        {
            CancelPending();
            if (_preview && !_draggingSlider && _root != null) _pending = _root.schedule.Execute(() => Preview(ctx)).StartingIn(300);
        }
        private static bool IsLil(Material m) { return m != null && m.HasProperty("_lilToonVersion"); }

        private static InnerColorState.MeshEntry MeshEntry(VRCAvatarDescriptor avatar, InnerColorState state, SkinnedMeshRenderer r)
        {
            if (state == null) return null;
            return state.data.meshes.FirstOrDefault(e => InnerColorState.RendererFor(avatar, e.assignment) == r &&
                AssetDatabase.GetAssetPath(r.sharedMesh) == e.meshPath);
        }

        /// <summary>新旧の複製を元の参照へ読み替える。計算中はシーンも元アセットも変えない。</summary>
        private static Material[] OriginalMaterials(VRCAvatarDescriptor avatar, InnerColorState state, SkinnedMeshRenderer r)
        {
            var saved = MeshEntry(avatar, state, r);
            var materials = saved == null ? r.sharedMaterials.ToArray() : saved.originalMaterials.Select(m => m.Load<Material>()).ToArray();
            if (state != null)
                foreach (var entry in state.data.entries)
                    foreach (var a in entry.assignments.Where(a => InnerColorState.RendererFor(avatar, a) == r))
                        if (a.slot < materials.Length && AssetDatabase.GetAssetPath(materials[a.slot]) == entry.materialPath)
                            materials[a.slot] = a.original.Load<Material>();
            return materials;
        }
        private static List<int> HairSlots(VRCAvatarDescriptor avatar, SkinnedMeshRenderer r, Material[] materials)
        {
            var body = AvatarFinder.FindBody(avatar);
            return Enumerable.Range(0, materials.Length).Where(i => materials[i] != null && materials[i].HasProperty("_Color") &&
                PartGuesser.Guess(r, materials[i], body) == BodyPart.Hair)
                .GroupBy(i => materials[i]).Select(g => g.First()).ToList();
        }
        private static List<SkinnedMeshRenderer> HairRenderers(VRCAvatarDescriptor avatar, InnerColorState state)
        {
            return avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && HairSlots(avatar, r, OriginalMaterials(avatar, state, r)).Count > 0)
                .OrderBy(r => r.name).ToList();
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
        private static bool Visible(Renderer renderer)
        {
            if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) return false;
            for (var t = renderer.transform; t != null; t = t.parent)
                if (t.gameObject.tag == "EditorOnly") return false;
            return true;
        }

        internal sealed class Surface
        {
            public SkinnedMeshRenderer renderer;
            public Mesh original;
            public Material[] materials;
            public int slot;
            public InnerColorTriangles.Split split;
        }

        private List<Surface> Bake(VRCAvatarDescriptor avatar, InnerColorState state)
        {
            var head = AvatarMeasure.Bone(avatar, HumanBodyBones.Head);
            if (head == null) throw new InvalidOperationException(L10n.Tr("innerColor.noHead"));
            var up = avatar.transform.up.normalized;
            var center = InnerColorTriangles.HeadCenter(head.position, up, AvatarMeasure.Height(avatar));
            var surfaces = new List<Surface>();
            foreach (var r in HairRenderers(avatar, state).Where(r => !_excluded.Contains(r) && Visible(r)))
            {
                var entry = MeshEntry(avatar, state, r);
                var original = entry == null ? r.sharedMesh : entry.originalMesh.Load<Mesh>();
                if (original == null) throw new InvalidOperationException(L10n.Tr("innerColor.missingMesh", r.name));
                var materials = OriginalMaterials(avatar, state, r);
                var slots = HairSlots(avatar, r, materials);
                var slot = _slots.ContainsKey(r) && slots.Contains(_slots[r]) ? _slots[r] : slots[0];
                // マテリアルがサブメッシュより多い髪（最後の面を影用などで重ねて描く）にも対応する（ApplySurface で最後の面を複製して残す）。
                // マテリアルが足りない髪は、描かれていない面があるので扱わない。
                if (materials.Length < original.subMeshCount)
                    throw new InvalidOperationException(L10n.Tr("innerColor.slotMismatch", r.name));
                var baked = new Mesh();
                try
                {
                    r.BakeMesh(baked, true);
                    var vertices = baked.vertices.Select(v => r.transform.position + r.transform.rotation * v).ToArray();
                    var triangles = Enumerable.Range(0, original.subMeshCount).Select(original.GetTriangles).ToArray();
                    var selected = Enumerable.Range(0, original.subMeshCount).Select(i => i < materials.Length && materials[i] == materials[slot]).ToArray();
                    surfaces.Add(new Surface { renderer = r, original = original, materials = materials, slot = slot,
                        split = InnerColorTriangles.Classify(vertices, triangles, selected, center, up,
                            InnerColorTriangles.Threshold(_range), _height) });
                }
                finally { Object.DestroyImmediate(baked); }
            }
            return surfaces;
        }

        private void Preview(FeatureContext ctx)
        {
            CancelPending();
            if (ctx.Avatar == null || ctx.Avatar != _avatar || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var group = -1;
            InnerColorAssetUndo journal = null;
            var started = EditorApplication.timeSinceStartup;
            try
            {
                var state = InnerColorState.Load(ctx.Avatar);
                var surfaces = Bake(ctx.Avatar, state);
                if (surfaces.Count == 0) { Reset(ctx); _status.text = L10n.Tr("innerColor.noneSelected"); return; }
                // 判定が全て成功してからバックアップと Undo を始める。
                ctx.BeginEdit(); UndoHelper.Separate();
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                var undoName = L10n.Tr("undo.innerColor"); Undo.SetCurrentGroupName(undoName);
                journal = InnerColorAssetUndo.Begin(ctx.Avatar);
                var candidates = InnerColorAssets.Candidates(ctx.Avatar, state);
                candidates.Remove(InnerColorState.PathFor(ctx.Avatar));
                if (state != null) candidates.Remove(AssetDatabase.GetAssetPath(state));
                state = InnerColorState.Load(ctx.Avatar, true);
                Undo.RecordObject(state, undoName);
                RestoreAll(ctx.Avatar, state, undoName);
                state.data = new InnerColorState.Data { hasSettings = true, color = _color,
                    range = _range, height = _height, brightness = _brightness };
                foreach (var surface in surfaces) ApplySurface(ctx.Avatar, surface, state, journal, undoName);
                EditorUtility.SetDirty(state);
                Finish(ctx, journal, candidates, undoName, group);
                journal = null;
                Undo.CollapseUndoOperations(group); group = -1;
                _preview = true;
                _status.text = L10n.Tr("innerColor.doneElapsed", (EditorApplication.timeSinceStartup - started).ToString("F1"));
                SceneView.RepaintAll();
            }
            catch (Exception e)
            {
                if (group >= 0) Undo.RevertAllDownToGroup(group);
                if (journal != null) journal.Rollback();
                _status.text = L10n.Tr("innerColor.failed", e.Message); Debug.LogException(e);
            }
        }

        /// <summary>保存済みの生成物は書き換えない。過去の履歴や別バージョンが参照する形と色を守る。</summary>
        internal void ApplySurface(VRCAvatarDescriptor avatar, Surface surface, InnerColorState state,
            InnerColorAssetUndo journal, string undoName)
        {
            var r = surface.renderer;
            var key = Guid.NewGuid().ToString("N");
            var meshFolder = WorkFolders.Ensure(WorkFolders.ForAvatar(avatar.gameObject) + "/Meshes");
            var materialFolder = WorkFolders.Ensure(WorkFolders.Materials(avatar.gameObject));
            var meshPath = InnerColorAssets.StablePath(meshFolder, r.name, "_InnerColor.asset", key);
            var source = surface.materials[surface.slot];
            var materialPath = InnerColorAssets.StablePath(materialFolder, source.name, "_InnerColor.mat", key);
            journal.Track(meshPath); journal.Track(materialPath);
            var mesh = Object.Instantiate(surface.original);
            mesh.name = surface.original.name + "_InnerColor";
            try
            {
                // baseVertex 付きの大きなメッシュも、絶対頂点番号で安全に保存する。
                if (mesh.vertexCount > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var count = surface.original.subMeshCount;
                // マテリアルがサブメッシュより多いとき、余ったマテリアルは「最後のサブメッシュ」を重ねて描いている（影用など）。
                // 内側の枠を足すと最後が変わってしまうので、元の最後のサブメッシュを丸ごと複製して、いちばん後ろに残す。
                var extras = surface.materials.Length - count;
                mesh.subMeshCount = count + 1 + (extras > 0 ? 1 : 0);
                for (var i = 0; i < surface.split.outer.Length; i++) mesh.SetTriangles(surface.split.outer[i], i);
                mesh.SetTriangles(surface.split.inner, count);
                if (extras > 0) mesh.SetTriangles(surface.original.GetTriangles(count - 1), count + 1);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            catch { if (!EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh); throw; }
            InnerColorAssets.Mark(meshPath, key);
            var material = Object.Instantiate(source);
            material.name = source.name + "_InnerColor";
            try
            {
                ConfigureMaterial(material, _color, _brightness);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            catch { if (!EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material); throw; }
            InnerColorAssets.Mark(materialPath, key);
            var assignment = new InnerColorState.Assignment
            {
                rendererPath = HierarchyPath.Get(avatar.transform, r.transform),
                rendererIndex = Array.IndexOf(r.GetComponents<Renderer>(), r),
            };
            state.data.meshes.Add(new InnerColorState.MeshEntry { assignment = assignment,
                originalMesh = InnerColorState.AssetRef.Of(surface.original), meshPath = meshPath, materialPath = materialPath,
                originalMaterials = surface.materials.Select(InnerColorState.AssetRef.Of).ToArray(), sourceSlot = surface.slot });
            Undo.RecordObject(r, undoName);
            r.sharedMesh = mesh;
            var main = surface.materials.Take(surface.original.subMeshCount);
            var overlays = surface.materials.Skip(surface.original.subMeshCount);
            r.sharedMaterials = main.Concat(new[] { material }).Concat(overlays).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            EditorUtility.SetDirty(r);
        }

        internal static void ConfigureMaterial(Material material, Color color, float brightness)
        {
            // lilToneCorrection は w 乗してから HSV を補正する。値を小さくすると暗い髪も明るくなる。
            // https://github.com/lilxyzw/lilToon/blob/master/Assets/lilToon/Shader/Includes/lil_common_functions.hlsl
            if (IsLil(material) && material.HasProperty("_MainTexHSVG"))
            {
                material.SetVector("_MainTexHSVG", new Vector4(0, 0, 1, 1 / Mathf.Max(0.25f, brightness)));
                // Multi 版は色調補正のキーワードも必要（lilMaterialUtils.SetupMultiMaterial）。
                if (material.shader != null && material.shader.name.Contains("Multi"))
                    material.EnableKeyword("EFFECT_HUE_VARIATION");
            }
            if (material.HasProperty("_Color"))
            {
                // 透け方は変えない。lilToon 以外は色だけを変える。
                color.a = material.GetColor("_Color").a;
                material.SetColor("_Color", color);
            }
            EditorUtility.SetDirty(material);
        }

        private static void Finish(FeatureContext ctx, InnerColorAssetUndo journal, HashSet<string> candidates, string undoName, int group)
        {
            // 片付けより先に履歴を保存し、操作前のメッシュへの参照も保護する。
            ctx.Backup.CompletePending(group);
            AssetDatabase.SaveAssets();
            Undo.FlushUndoRecordObjects();
            MyAvatar.Save(ctx.Avatar);
            InnerColorAssets.Cleanup(candidates, journal);
            journal.Commit(undoName);
        }

        private void Reset(FeatureContext ctx)
        {
            CancelPending();
            if (ctx.Avatar == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var state = InnerColorState.Load(ctx.Avatar);
            var candidates = InnerColorAssets.Candidates(ctx.Avatar, state);
            if (state == null && candidates.Count == 0) return;
            ctx.BeginEdit(); UndoHelper.Separate();
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            var undoName = L10n.Tr("undo.innerColor"); Undo.SetCurrentGroupName(undoName);
            var journal = InnerColorAssetUndo.Begin(ctx.Avatar);
            try
            {
                if (state != null)
                {
                    Undo.RecordObject(state, undoName); RestoreAll(ctx.Avatar, state, undoName);
                    state.data = new InnerColorState.Data(); EditorUtility.SetDirty(state);
                }
                Finish(ctx, journal, candidates, undoName, group);
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception e)
            {
                Undo.RevertAllDownToGroup(group); journal.Rollback();
                _status.text = L10n.Tr("innerColor.failed", e.Message); Debug.LogException(e); return;
            }
            _preview = false; _status.text = L10n.Tr("innerColor.restored"); SceneView.RepaintAll();
        }

        private static void RestoreAll(VRCAvatarDescriptor avatar, InnerColorState state, string undoName)
        {
            foreach (var entry in state.data.meshes)
            {
                var renderer = InnerColorState.RendererFor(avatar, entry.assignment) as SkinnedMeshRenderer;
                if (renderer == null || AssetDatabase.GetAssetPath(renderer.sharedMesh) != entry.meshPath) continue;
                var original = entry.originalMesh.Load<Mesh>();
                if (original == null) throw new InvalidOperationException(L10n.Tr("innerColor.missingMesh", renderer.name));
                Undo.RecordObject(renderer, undoName);
                renderer.sharedMesh = original;
                renderer.sharedMaterials = entry.originalMaterials.Select(m => m.Load<Material>()).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }
            foreach (var entry in state.data.entries) RestoreSecond(avatar, entry, undoName);
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

    }
}
