using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// AssetDatabase の削除・PNG の上書きは Unity Undo の対象外なので、内容と .meta を記録する。
    /// Assets 内にごみ箱は作らない。Editor 内のオブジェクトだけで保持し、同じ GUID で戻す。
    /// Undo 後はインポートで失われた参照もパス・local ID からつなぎ直す。
    /// </summary>
    [InitializeOnLoad]
    internal sealed class InnerColorAssetUndo : ScriptableObject
    {
        [Serializable]
        internal sealed class FileState
        {
            public string path;
            public byte[] content, meta;
            internal static FileState Read(string path)
            {
                return new FileState
                {
                    path = path, content = File.Exists(path) ? File.ReadAllBytes(path) : null,
                    meta = File.Exists(path + ".meta") ? File.ReadAllBytes(path + ".meta") : null,
                };
            }
            internal void Write()
            {
                if (content == null || !WorkFolders.IsToolPath(path)) return;
                WorkFolders.Ensure(Path.GetDirectoryName(path).Replace('\\', '/'));
                File.WriteAllBytes(path, content);
                if (meta != null) File.WriteAllBytes(path + ".meta", meta);
            }
        }
        [Serializable]
        private sealed class RendererState
        {
            public Renderer renderer;
            public InnerColorState.AssetRef[] materials;
        }
        [Serializable]
        private sealed class MaterialState
        {
            public string path;
            public InnerColorState.SecondState second;
        }
        [Serializable]
        private sealed class Snapshot
        {
            public List<RendererState> renderers = new List<RendererState>();
            public List<MaterialState> materials = new List<MaterialState>();
            public string stateJson;
            internal static Snapshot Capture(VRCAvatarDescriptor avatar)
            {
                var result = new Snapshot();
                var state = InnerColorState.Load(avatar);
                result.stateJson = state == null ? null : JsonUtility.ToJson(state.data);
                foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
                    result.renderers.Add(new RendererState { renderer = renderer,
                        materials = renderer.sharedMaterials.Select(InnerColorState.AssetRef.Of).ToArray() });
                var paths = result.renderers.SelectMany(r => r.materials).Select(m => m.path);
                if (state != null) paths = paths.Concat(state.data.entries.Select(e => e.materialPath));
                foreach (var path in paths.Distinct().Where(WorkFolders.IsToolPath))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material != null) result.materials.Add(new MaterialState
                        { path = path, second = InnerColorState.SecondState.Capture(material) });
                }
                return result;
            }
            internal void Apply(VRCAvatarDescriptor avatar)
            {
                foreach (var saved in renderers)
                {
                    if (saved.renderer == null) continue;
                    saved.renderer.sharedMaterials = saved.materials.Select(m => m.Load<Material>()).ToArray();
                    EditorUtility.SetDirty(saved.renderer);
                }
                foreach (var saved in materials)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(saved.path);
                    if (material != null) saved.second.Apply(material);
                }
                var state = InnerColorState.Load(avatar, !string.IsNullOrEmpty(stateJson));
                if (state != null)
                {
                    state.data = string.IsNullOrEmpty(stateJson) ? new InnerColorState.Data() :
                        JsonUtility.FromJson<InnerColorState.Data>(stateJson);
                    state.Normalize();
                    EditorUtility.SetDirty(state);
                }
            }
        }

        [SerializeField] private VRCAvatarDescriptor avatar;
        [SerializeField] private Snapshot before, after;
        [SerializeField] private List<FileState> oldFiles = new List<FileState>();
        [SerializeField] private List<FileState> newFiles = new List<FileState>();
        [SerializeField] private bool applied;
        [SerializeField] private long order;
        [NonSerialized] private bool observed;

        static InnerColorAssetUndo() { Undo.undoRedoPerformed += Replay; }
        private void OnEnable() { observed = applied; }

        internal static InnerColorAssetUndo Begin(VRCAvatarDescriptor avatar)
        {
            var journal = CreateInstance<InnerColorAssetUndo>();
            journal.hideFlags = HideFlags.HideAndDontSave;
            journal.avatar = avatar;
            journal.before = Snapshot.Capture(avatar);
            journal.order = DateTime.UtcNow.Ticks;
            journal.Track(InnerColorState.PathFor(avatar));
            return journal;
        }

        internal void Track(string path)
        {
            if (!WorkFolders.IsToolPath(path) || oldFiles.Any(f => f.path == path)) return;
            oldFiles.Add(FileState.Read(path));
        }

        internal void Commit(string undoName)
        {
            AssetDatabase.SaveAssets();
            after = Snapshot.Capture(avatar);
            newFiles = oldFiles.Select(f => FileState.Read(f.path)).ToList();
            // 内容の記録を完成させてから、目印だけを Undo に載せる。
            Undo.RecordObject(this, undoName);
            applied = true;
            observed = true;
        }

        internal void Rollback()
        {
            Apply(before, oldFiles);
            DestroyImmediate(this);
        }

        private static void Replay()
        {
            var journals = Resources.FindObjectsOfTypeAll<InnerColorAssetUndo>().Where(j => j.applied != j.observed).ToList();
            foreach (var journal in journals.Where(j => !j.applied).OrderByDescending(j => j.order)
                .Concat(journals.Where(j => j.applied).OrderBy(j => j.order)))
            {
                journal.Apply(journal.applied ? journal.after : journal.before,
                    journal.applied ? journal.newFiles : journal.oldFiles);
                journal.observed = journal.applied;
            }
        }

        private void Apply(Snapshot snapshot, List<FileState> files)
        {
            if (avatar == null) return;
            // .meta を先に戻し、依存先を全て置いてからインポートする。
            foreach (var file in files) file.Write();
            foreach (var file in files.Where(f => f.content != null)) AssetDatabase.ImportAsset(file.path);
            snapshot.Apply(avatar);
            AssetDatabase.SaveAssets();
            MyAvatar.Save(avatar);
            var absent = new HashSet<string>(files.Where(f => f.content == null).Select(f => f.path));
            var referenced = InnerColorAssets.Referenced(absent);
            foreach (var path in absent)
                if (WorkFolders.IsToolPath(path) && !referenced.Contains(path)) AssetDatabase.DeleteAsset(path);
            SceneView.RepaintAll();
        }
    }
}
