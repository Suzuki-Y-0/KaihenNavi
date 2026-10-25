using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    /// <summary>Editor 内だけの復元台帳。プロパティ値と割り当てを 1 アバター 1 ファイルに保存する。</summary>
    internal sealed class InnerColorState : ScriptableObject
    {
        [Serializable]
        internal sealed class AssetRef
        {
            public string path;
            public long localId;
            public static AssetRef Of(Object value)
            {
                string guid; long id;
                id = 0;
                if (value != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out guid, out id);
                return new AssetRef { path = value == null ? "" : AssetDatabase.GetAssetPath(value), localId = id };
            }
            public T Load<T>() where T : Object
            {
                if (string.IsNullOrEmpty(path)) return null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    string guid; long id;
                    if (asset is T && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out id) && id == localId)
                        return (T)asset;
                }
                return localId == 0 ? AssetDatabase.LoadAssetAtPath<T>(path) : null;
            }
            public string Key { get { return AssetDatabase.AssetPathToGUID(path) + ":" + localId; } }
        }

        [Serializable] internal sealed class FloatValue { public string name; public float value; }
        [Serializable] internal sealed class VectorValue { public string name; public Vector4 value; }
        [Serializable] internal sealed class TextureValue
        {
            public string name;
            public AssetRef texture;
            public Vector2 scale, offset;
        }
        [Serializable] internal sealed class KeywordValue { public string name; public bool enabled; }

        [Serializable]
        internal sealed class SecondState
        {
            public List<FloatValue> floats = new List<FloatValue>();
            public List<VectorValue> vectors = new List<VectorValue>();
            public List<TextureValue> textures = new List<TextureValue>();
            public List<KeywordValue> keywords = new List<KeywordValue>();
            public Color color;

            public static SecondState Capture(Material material)
            {
                var result = new SecondState();
                if (material.HasProperty("_Color2nd")) result.color = material.GetColor("_Color2nd");
                foreach (var name in InnerColorFeature.Floats)
                    if (material.HasProperty(name)) result.floats.Add(new FloatValue { name = name, value = material.GetFloat(name) });
                foreach (var name in InnerColorFeature.Vectors)
                    if (material.HasProperty(name)) result.vectors.Add(new VectorValue { name = name, value = material.GetVector(name) });
                foreach (var name in new[] { "_Main2ndTex", "_Main2ndBlendMask" })
                    if (material.HasProperty(name)) result.textures.Add(new TextureValue
                    {
                        name = name, texture = AssetRef.Of(material.GetTexture(name)),
                        scale = material.GetTextureScale(name), offset = material.GetTextureOffset(name),
                    });
                foreach (var name in new[] { "_COLORADDSUBDIFF_ON", "_SUNDISK_NONE", "GEOM_TYPE_FROND" })
                    result.keywords.Add(new KeywordValue { name = name, enabled = material.IsKeywordEnabled(name) });
                return result;
            }

            public void Apply(Material material)
            {
                foreach (var p in floats) if (material.HasProperty(p.name)) material.SetFloat(p.name, p.value);
                foreach (var p in vectors) if (material.HasProperty(p.name)) material.SetVector(p.name, p.value);
                if (material.HasProperty("_Color2nd")) material.SetColor("_Color2nd", color);
                foreach (var p in textures)
                {
                    if (!material.HasProperty(p.name)) continue;
                    material.SetTexture(p.name, p.texture.Load<Texture>());
                    material.SetTextureScale(p.name, p.scale);
                    material.SetTextureOffset(p.name, p.offset);
                }
                foreach (var p in keywords)
                    if (p.enabled) material.EnableKeyword(p.name); else material.DisableKeyword(p.name);
                EditorUtility.SetDirty(material);
            }
        }

        [Serializable]
        internal sealed class Assignment
        {
            public string rendererPath;
            public int rendererIndex;
            public int slot = 0;
            public AssetRef original;
        }

        [Serializable]
        internal sealed class Entry
        {
            public string materialPath = null;
            // 旧版の .mat 控えを読むためだけに残す。新規の控えは作らない。
            public string originalPath = null;
            public string key = null;
            public string maskPath = null;
            public AssetRef source = null;
            public SecondState before;
            public List<Assignment> assignments = new List<Assignment>();
        }

        /// <summary>面方式の元メッシュと元の枠。旧方式の entries とは別に保存する。</summary>
        [Serializable]
        internal sealed class MeshEntry
        {
            public Assignment assignment;
            public AssetRef originalMesh;
            public AssetRef[] originalMaterials;
            public string meshPath;
            public string materialPath = null;
            public int sourceSlot;
        }

        [Serializable]
        internal sealed class Data
        {
            public List<Entry> entries = new List<Entry>();
            public List<MeshEntry> meshes = new List<MeshEntry>();
            public bool hasSettings;
            public Color color;
            public float range = 56.25f;
            public float height;
            public float brightness = 1f;
        }

        public Data data = new Data();

        internal void Normalize()
        {
            if (data == null) data = new Data();
            if (data.entries == null) data.entries = new List<Entry>();
            if (data.meshes == null) data.meshes = new List<MeshEntry>();
            foreach (var entry in data.entries)
            {
                if (entry.assignments == null) entry.assignments = new List<Assignment>();
                // 自動バックアップに JSON を渡す前に、旧版の .mat 控えも値へ移す。
                // 控えを片付けた後でも、そのバックアップから復元できるようにする。
                if (entry.before != null || string.IsNullOrEmpty(entry.originalPath)) continue;
                var before = AssetDatabase.LoadAssetAtPath<Material>(entry.originalPath);
                if (before == null) continue;
                entry.before = SecondState.Capture(before);
                EditorUtility.SetDirty(this);
            }
        }

        public static string PathFor(VRCAvatarDescriptor avatar)
        {
            return WorkFolders.Backups(avatar.gameObject) + "/InnerColorState.asset";
        }

        public static InnerColorState Load(VRCAvatarDescriptor avatar, bool create = false)
        {
            var path = PathFor(avatar);
            var state = AssetDatabase.LoadAssetAtPath<InnerColorState>(path);
            if (state == null)
            {
                var oldPath = WorkFolders.ForAvatar(avatar.gameObject) + "/InnerColorState.asset";
                state = AssetDatabase.LoadAssetAtPath<InnerColorState>(oldPath);
                // 旧版もその場所で読む。画面を開くだけでファイルを移動すると Undo の外で変更される。
            }
            if (state != null) state.Normalize();
            if (state != null || !create) return state;
            WorkFolders.Ensure(WorkFolders.Backups(avatar.gameObject));
            state = CreateInstance<InnerColorState>();
            AssetDatabase.CreateAsset(state, path);
            return state;
        }

        internal static Renderer RendererFor(VRCAvatarDescriptor avatar, Assignment assignment)
        {
            var transform = HierarchyPath.Find(avatar.transform, assignment.rendererPath);
            if (transform == null) return null;
            return transform.GetComponents<Renderer>().ElementAtOrDefault(assignment.rendererIndex);
        }
    }

    internal sealed class InnerColorBackup : IBackupParticipant
    {
        public string Key { get { return "inner-color"; } }
        public string Capture(VRCAvatarDescriptor avatar)
        {
            var state = InnerColorState.Load(avatar);
            return state == null ? null : JsonUtility.ToJson(state.data);
        }
        /// <summary>片付け済みの生成物を履歴が指していたら、その髪は元のメッシュと枠に戻す。</summary>
        internal static void RestoreMissingMeshes(VRCAvatarDescriptor avatar, BackupSnapshot snapshot, string undoName)
        {
            var saved = snapshot.extras.FirstOrDefault(e => e.key == "inner-color");
            if (saved == null || string.IsNullOrEmpty(saved.json)) return;
            var data = JsonUtility.FromJson<InnerColorState.Data>(saved.json);
            if (data == null || data.meshes == null) return;
            foreach (var entry in data.meshes)
            {
                var renderer = InnerColorState.RendererFor(avatar, entry.assignment) as SkinnedMeshRenderer;
                if (renderer == null) continue;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.meshPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(entry.materialPath);
                // 同じ名前で作り直した後も、古い GUID の Missing 参照は元の髪へ戻す。
                if (mesh != null && material != null && renderer.sharedMesh == mesh &&
                    renderer.sharedMaterials.Contains(material)) continue;
                Undo.RecordObject(renderer, undoName);
                renderer.sharedMesh = entry.originalMesh.Load<Mesh>();
                renderer.sharedMaterials = entry.originalMaterials.Select(m => m.Load<Material>()).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
                var state = InnerColorState.Load(avatar);
                if (state == null) continue;
                Undo.RecordObject(state, undoName);
                state.data.meshes.RemoveAll(e => e.assignment.rendererPath == entry.assignment.rendererPath &&
                    e.assignment.rendererIndex == entry.assignment.rendererIndex);
                if (state.data.meshes.Count == 0 && state.data.entries.Count == 0) state.data.hasSettings = false;
                EditorUtility.SetDirty(state);
            }
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
            var state = InnerColorState.Load(avatar, !string.IsNullOrEmpty(json));
            if (state == null) return;
            Undo.RecordObject(state, undoName);
            state.data = string.IsNullOrEmpty(json) ? new InnerColorState.Data() :
                JsonUtility.FromJson<InnerColorState.Data>(json) ?? new InnerColorState.Data();
            state.Normalize();
            EditorUtility.SetDirty(state);
            // 見た目は、この後 BackupService が保存済みのマテリアルから戻す。
        }
    }
}
