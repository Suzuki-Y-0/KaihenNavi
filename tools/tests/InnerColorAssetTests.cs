// ファイルの再利用・安全な削除・Undo の復元を、ディスクと動作する API スタブで検証する。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    internal static class InnerColorAssetTests
    {
        private static int checks;
        private static readonly Dictionary<string, Object> assets = new Dictionary<string, Object>();
        private static readonly Dictionary<string, Object> deleted = new Dictionary<string, Object>();
        private static readonly Dictionary<string, AssetImporter> importers = new Dictionary<string, AssetImporter>();
        private static readonly Dictionary<Object, string> paths = new Dictionary<Object, string>();
        private static readonly Dictionary<string, string> guids = new Dictionary<string, string>();
        private static readonly Dictionary<string, object> json = new Dictionary<string, object>();
        private static readonly Dictionary<string, string[]> dependencies = new Dictionary<string, string[]>();
        private static readonly List<Object> transient = new List<Object>();
        private static readonly List<Renderer> renderers = new List<Renderer>();
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        private static object Clone(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType) return value;
            var array = value as Array;
            if (array != null)
            {
                var copy = Array.CreateInstance(value.GetType().GetElementType(), array.Length);
                for (var i = 0; i < array.Length; i++) copy.SetValue(Clone(array.GetValue(i)), i);
                return copy;
            }
            var list = value as IList;
            var result = Activator.CreateInstance(value.GetType(), true);
            if (list != null) foreach (var item in list) ((IList)result).Add(Clone(item));
            else foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(result, Clone(field.GetValue(value)));
            return result;
        }
        private static void Add(Object asset, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!guids.ContainsKey(path)) guids[path] = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, "asset:" + asset.name);
            File.WriteAllText(path + ".meta", guids[path]);
            assets[path] = asset; paths[asset] = path;
            var backup = asset as BackupSnapshot;
            if (backup != null)
                dependencies[path] = backup.renderers.SelectMany(r => r.materials.Select(AssetDatabase.GetAssetPath)
                    .Concat(new[] { AssetDatabase.GetAssetPath(r.mesh) })).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            importers[path] = new TextureImporter { assetPath = path };
        }
        private static void Import(string path)
        {
            if (assets.ContainsKey(path)) return;
            Object previous;
            if (deleted.TryGetValue(path, out previous))
            {
                var material = previous as Material;
                var restored = material != null ? new Material(material) : previous is Texture2D ?
                    (Object)new Texture2D(1, 1, TextureFormat.RGBA32, false) : previous is Mesh ? CopyMesh((Mesh)previous) : Activator.CreateInstance(previous.GetType(), true) as Object;
                assets[path] = restored; paths[restored] = path;
            }
            else
            {
                assets[path] = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                paths[assets[path]] = path;
            }
            if (!File.Exists(path + ".meta"))
            {
                guids[path] = Guid.NewGuid().ToString("N"); File.WriteAllText(path + ".meta", guids[path]);
            }
            guids[path] = File.ReadAllText(path + ".meta");
            if (!importers.ContainsKey(path)) importers[path] = new TextureImporter { assetPath = path };
        }
        private static IEnumerable<string> Dependencies(string path, HashSet<string> visited)
        {
            if (!visited.Add(path)) yield break;
            yield return path;
            string[] links;
            if (!dependencies.TryGetValue(path, out links)) links = new string[0];
            Object asset;
            var material = assets.TryGetValue(path, out asset) ? asset as Material : null;
            if (material != null) links = links.Concat(material.GetTexturePropertyNames()
                .Select(p => AssetDatabase.GetAssetPath(material.GetTexture(p)))).ToArray();
            foreach (var link in links.Where(p => !string.IsNullOrEmpty(p)))
                foreach (var child in Dependencies(link, visited)) yield return child;
        }
        private static void Setup()
        {
            Resources.FindForTests = type => renderers.Cast<Object>().Concat(transient).Where(type.IsInstanceOfType).ToArray();
            Object.InstantiateForTests = original => original is Mesh ? (Object)CopyMesh((Mesh)original) : new Material((Material)original);
            ScriptableObject.CreateForTests = type =>
            {
                var value = (ScriptableObject)Activator.CreateInstance(type, true); transient.Add(value); return value;
            };
            EditorUtility.CopyForTests = (source, target) =>
            {
                ((Material)target).values = new Dictionary<string, object>(((Material)source).values);
                ((Material)target).keywords = new HashSet<string>(((Material)source).keywords);
            };
            JsonUtility.SerializeForTests = value =>
            {
                var key = Guid.NewGuid().ToString(); json[key] = Clone(value); return key;
            };
            JsonUtility.DeserializeForTests = (key, type) => Clone(json[key]);
            AssetImporter.LoadForTests = path => path != null && File.Exists(path) && importers.ContainsKey(path) ? importers[path] : null;
            AssetDatabase.PathForTests = asset => asset != null && paths.ContainsKey(asset) ? paths[asset] : "";
            AssetDatabase.LoadForTests = path => path != null && assets.ContainsKey(path) && File.Exists(path) ? assets[path] : null;
            AssetDatabase.LoadAllForTests = path => AssetDatabase.LoadForTests(path) == null ? new Object[0] : new[] { assets[path] };
            AssetDatabase.LocalIdForTests = asset => 1;
            AssetDatabase.PathsForTests = () => assets.Keys.Where(File.Exists).ToArray();
            AssetDatabase.GuidForTests = path => path != null && guids.ContainsKey(path) ? guids[path] : "";
            AssetDatabase.PathFromGuidForTests = guid => guids.First(p => p.Value == guid).Key;
            AssetDatabase.FolderForTests = Directory.Exists;
            EditorUtility.PersistentForTests = asset => paths.ContainsKey(asset);
            AssetDatabase.FindForTests = (filter, folders) =>
            {
                if (filter == "t:Prefab t:ScriptableObject t:Material")
                    Check(folders.SequenceEqual(new[] { WorkFolders.Root }), "Reference search must stay within the tool root");
                return assets.Where(pair => File.Exists(pair.Key) && folders.Any(f => pair.Key.StartsWith(f + "/", StringComparison.Ordinal)) &&
                    (filter == "" || pair.Key.EndsWith(".prefab", StringComparison.Ordinal) ||
                        pair.Value is ScriptableObject || pair.Value is Material) &&
                    (filter != "t:BackupSnapshot" || pair.Value is BackupSnapshot))
                    .Select(pair => guids[pair.Key]).ToArray();
            };
            AssetDatabase.CreateForTests = Add;
            AssetDatabase.ImportForTests = Import;
            AssetDatabase.DeleteForTests = path =>
            {
                if (assets.ContainsKey(path)) { deleted[path] = assets[path]; assets.Remove(path); }
                File.Delete(path); File.Delete(path + ".meta");
            };
            AssetDatabase.DependenciesForTests = roots => roots.SelectMany(p => Dependencies(p, new HashSet<string>())).Distinct().ToArray();
            AssetDatabase.MoveForTests = (from, to) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Move(from, to); File.Move(from + ".meta", to + ".meta");
                assets[to] = assets[from]; paths[assets[to]] = to; assets.Remove(from);
                importers[to] = importers[from]; importers.Remove(from); guids[to] = guids[from]; guids.Remove(from);
            };
        }
        private static void Restore(VRCAvatarDescriptor avatar, InnerColorState.Entry entry)
        {
            typeof(InnerColorFeature).GetMethod("RestoreSecond", Private).Invoke(null, new object[] { avatar, entry, "test" });
        }
        private static Mesh CopyMesh(Mesh source)
        {
            var copy = new Mesh { name = source.name, vertices = source.vertices.ToArray(), uv = source.uv,
                normals = source.normals, blendShapeCount = source.blendShapeCount, bindposes = source.bindposes,
                subMeshCount = source.subMeshCount };
            for (var i = 0; i < source.subMeshCount; i++) copy.SetTriangles(source.GetTriangles(i).ToArray(), i);
            return copy;
        }
        private static void RestoreMeshes(VRCAvatarDescriptor avatar, InnerColorState state)
        {
            typeof(InnerColorFeature).GetMethod("RestoreAll", Private).Invoke(null, new object[] { avatar, state, "test" });
        }
        private static void Replay(InnerColorAssetUndo journal, bool applied)
        {
            typeof(InnerColorAssetUndo).GetField("applied", Private).SetValue(journal, applied);
            Undo.ReplayForTests();
        }
        internal static void Run()
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            var temp = Path.Combine(Path.GetTempPath(), "kn-inner-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp); Directory.SetCurrentDirectory(temp);
            try { Setup(); RunChecks(); }
            finally { Directory.SetCurrentDirectory(originalDirectory); Directory.Delete(temp, true); }
            Console.WriteLine("InnerColor asset tests passed (" + checks + " checks).");
        }
        private static void RunChecks()
        {
            Check(!WorkFolders.IsToolPath("Assets/KaihenNaviOther/file.mat"), "Similar root must not be owned");
            Check(!WorkFolders.IsToolPath("Assets/KaihenNavi/../Bought.mat"), "Traversal must not be owned");
            var roots = InnerColorAssets.ReferenceRoots(new[] {
                WorkFolders.Root + "/Bases/Base.prefab", WorkFolders.Root + "/Bases/Base.prefab",
                WorkFolders.Root + "/Avatars/Other/Backups/backup.asset", WorkFolders.Root + "/Avatars/Other/Materials/Hair.mat",
                WorkFolders.Root + "/candidate.mat", "Assets/Purchased/Hair.mat", null
            }, new HashSet<string> { WorkFolders.Root + "/candidate.mat" });
            Check(roots.Length == 3, "Reference roots must exclude candidates and purchased assets and deduplicate");
            var go = new GameObject("TestAvatar") { scene = new UnityEngine.SceneManagement.Scene { validForTests = true, isLoaded = true } };
            var avatar = go.AddComponent<VRCAvatarDescriptor>();
            var hair = go.AddComponent<SkinnedMeshRenderer>();
            var other = go.AddComponent<SkinnedMeshRenderer>();
            renderers.Add(hair); renderers.Add(other);
            var source = new Material(new Shader { name = "lilToonMulti" }) { name = "Hair" };
            source.SetFloat("_lilToonVersion", 1);
            source.SetColor("_Color", new Color(0.3f, 0.4f, 0.5f, 0.4f));
            source.SetVector("_MainTexHSVG", new Vector4(0.2f, 0.8f, 0.7f, 1.2f));
            foreach (var name in InnerColorFeature.Floats) source.SetFloat(name, 0.25f);
            foreach (var name in InnerColorFeature.Vectors) source.SetVector(name, new Vector4(1, 2, 3, 4));
            source.SetColor("_Color2nd", new Color(0.1f, 0.2f, 0.3f));
            var oldTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            Add(oldTexture, "Assets/Purchased/old.png");
            source.SetTexture("_Main2ndTex", oldTexture); source.SetTexture("_Main2ndBlendMask", oldTexture);
            source.SetTextureScale("_Main2ndTex", new Vector2(2, 3));
            source.SetTextureOffset("_Main2ndTex", new Vector2(0.4f, 0.5f));
            source.EnableKeyword("_SUNDISK_NONE");
            Add(source, "Assets/Purchased/Hair.mat");
            var originalMesh = new Mesh { name = "Hair", subMeshCount = 2, blendShapeCount = 3,
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.forward, Vector3.one }, bindposes = new Matrix4x4[4] };
            originalMesh.SetTriangles(new[] { 0, 1, 2, 1, 2, 3 }, 0);
            originalMesh.SetTriangles(new[] { 0, 2, 3 }, 1);
            Add(originalMesh, "Assets/Purchased/Hair.asset");
            var purchasedBytes = File.ReadAllBytes("Assets/Purchased/Hair.asset");
            var purchasedMatBytes = File.ReadAllBytes("Assets/Purchased/Hair.mat");
            hair.sharedMesh = originalMesh; other.sharedMesh = originalMesh;
            hair.sharedMaterials = new[] { source, source }; other.sharedMaterials = new[] { source, source };
            var folder = WorkFolders.ForAvatar(go);
            Directory.CreateDirectory(WorkFolders.Backups(go));
            Directory.CreateDirectory(WorkFolders.Materials(go));
            var feature = new InnerColorFeature();
            typeof(InnerColorFeature).GetField("_status", Private).SetValue(feature, new Label());
            var context = new FeatureContext { Avatar = avatar, Backup = new BackupService(), FeatureId = "innerColor" };
            var journal = InnerColorAssetUndo.Begin(avatar);
            var state = InnerColorState.Load(avatar, true);
            Check(AssetDatabase.GetAssetPath(state) == folder + "/Backups/InnerColorState.asset", "State must live in the avatar Backups folder");
            var surface = new InnerColorFeature.Surface { renderer = hair, original = originalMesh,
                materials = new[] { source, source }, slot = 0,
                split = new InnerColorTriangles.Split { outer = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, inner = new[] { 1, 2, 3 } } };
            feature.ApplySurface(avatar, surface, state, journal, "test");
            var first = state.data.meshes.Single();
            var firstMesh = hair.sharedMesh;
            var firstMaterial = hair.sharedMaterials[2];
            Check(first.originalMesh.Load<Mesh>() == originalMesh && first.originalMaterials.Length == 2, "State must retain original mesh and all material slots");
            Check(firstMesh != originalMesh && firstMesh.subMeshCount == 3 && hair.sharedMaterials.Length == 3, "Applying must add exactly one submesh and material slot");
            Check(firstMesh.GetTriangles(0).SequenceEqual(new[] { 0, 1, 2 }) && firstMesh.GetTriangles(1).SequenceEqual(originalMesh.GetTriangles(1)) &&
                firstMesh.GetTriangles(2).SequenceEqual(new[] { 1, 2, 3 }), "Only selected triangles may move to the appended submesh");
            Check(firstMesh.blendShapeCount == 3 && firstMesh.bindposes.Length == 4 && firstMesh.vertices.SequenceEqual(originalMesh.vertices), "Cloning must retain the mesh data used for skinning and blend shapes");
            Check(other.sharedMesh == originalMesh && other.sharedMaterials.Length == 2 && hair.sharedMaterials[0] == source, "Other hair and outer material assignments must remain unchanged");
            Check(firstMaterial.GetVector("_MainTexHSVG").y == 0 && firstMaterial.GetVector("_MainTexHSVG").z == 1 && firstMaterial.GetVector("_MainTexHSVG").w == 1,
                "lilToon must remove texture saturation and keep shading");
            Check(firstMaterial.GetColor("_Color").r == 1 && firstMaterial.GetColor("_Color").a == 0.4f && firstMaterial.IsKeywordEnabled("EFFECT_HUE_VARIATION"),
                "Tint must preserve transparency and enable tone correction on Multi");
            Check(!Directory.Exists(folder + "/Textures"), "Face mode must not generate mask textures");
            // 分類の接続部分も確認する。焼いた頂点は変わっていても、三角形は元メッシュから読む。
            var animator = go.AddComponent<Animator>(); animator.isHuman = true;
            animator.BoneForTests = bone => new Transform { position = new Vector3(0, 1, 0) };
            go.activeInHierarchy = true; go.transform.up = Vector3.up; hair.enabled = true;
            EditorApplication.timeSinceStartup = 10; AvatarMeasure.Forget();
            var usedScale = true;
            SkinnedMeshRenderer.BakeForTests = (renderer, mesh, scale) =>
            {
                usedScale &= scale;
                mesh.vertices = renderer.sharedMesh.vertices.Select(v => v + new Vector3(-10, 0, 0)).ToArray();
            };
            var baked = (List<InnerColorFeature.Surface>)typeof(InnerColorFeature).GetMethod("Bake", Private)
                .Invoke(feature, new object[] { avatar, state });
            Check(usedScale && baked.Count == 1 && baked[0].original == originalMesh && baked[0].materials.Length == 2,
                "Bake must use the current pose with scale while resolving the pre-split mesh and materials");
            Check(baked[0].split.inner.SequenceEqual(new[] { 0, 1, 2 }) && baked[0].split.outer.Length == 2 &&
                baked[0].split.inner.Length + baked[0].split.outer.Sum(t => t.Length) == 9 && originalMesh.uv == null,
                "Current baked positions must determine each original face exactly once without requiring UVs");
            SkinnedMeshRenderer.BakeForTests = null;
            journal.Commit("test");
            var firstGuid = AssetDatabase.AssetPathToGUID(first.meshPath);
            var nextJournal = InnerColorAssetUndo.Begin(avatar);
            RestoreMeshes(avatar, state);
            Check(hair.sharedMesh == originalMesh && hair.sharedMaterials.SequenceEqual(new[] { source, source }), "Reset must restore the exact original assignments");
            state.data = new InnerColorState.Data();
            feature.ApplySurface(avatar, surface, state, nextJournal, "test");
            var second = state.data.meshes.Single();
            Check(second.meshPath != first.meshPath && second.materialPath != first.materialPath && hair.sharedMesh.subMeshCount == 3,
                "Repeated application must start at the original mesh and preserve previously saved assets");
            Check(firstMesh.GetTriangles(2).SequenceEqual(new[] { 1, 2, 3 }) && firstMaterial.GetColor("_Color").r == 1,
                "Previously generated mesh and material must not be overwritten");
            nextJournal.Commit("test");
            Replay(nextJournal, false);
            Check(hair.sharedMesh == firstMesh && hair.sharedMaterials[2] == firstMaterial && state.data.meshes.Single().meshPath == first.meshPath,
                "Undo must restore mesh, materials and state together");
            Replay(nextJournal, true);
            Check(AssetDatabase.GetAssetPath(hair.sharedMesh) == second.meshPath && state.data.meshes.Single().meshPath == second.meshPath,
                "Redo must restore the adjusted mesh and matching state");
            // 履歴はメッシュを直接参照し、内容の上書きなしで過去の面を復元する。
            context.Backup.BeginEdit(avatar, "innerColor");
            context.Backup.CompletePending();
            var backup = assets.Values.OfType<BackupSnapshot>().Last();
            Check(backup.renderers[0].hasMesh && backup.renderers[0].mesh == hair.sharedMesh && backup.renderers[1].rendererIndex == 1,
                "History must save mesh references and distinguish renderers on the same object");
            var savedMesh = hair.sharedMesh;
            hair.sharedMesh = originalMesh;
            typeof(BackupService).GetMethod("Apply", Private).Invoke(null, new object[] { avatar, backup });
            Check(hair.sharedMesh == savedMesh, "History restore must apply mesh even when materials have not changed");
            // 別の未保存アバター、保存済み Prefab とバックアップが使うメッシュを消さない。
            other.sharedMesh = firstMesh;
            var protectedJournal = InnerColorAssetUndo.Begin(avatar);
            InnerColorAssets.Cleanup(new HashSet<string> { first.meshPath }, protectedJournal);
            Check(File.Exists(first.meshPath), "Another scene renderer must protect a generated mesh");
            other.sharedMesh = originalMesh;
            var otherPrefab = folder + "/Other.prefab";
            Add(new GameObject("SavedAvatar"), otherPrefab);
            dependencies[otherPrefab] = new[] { first.meshPath, first.materialPath };
            InnerColorAssets.Cleanup(new HashSet<string> { first.meshPath, first.materialPath }, protectedJournal);
            Check(File.Exists(first.meshPath) && File.Exists(first.materialPath), "Saved avatar dependencies must protect mesh and material");
            dependencies.Remove(otherPrefab);
            // 実際のカードの「元に戻す」と、削除ファイルを再読み込みする Undo / Redo。
            typeof(InnerColorFeature).GetMethod("Reset", Private).Invoke(feature, new object[] { context });
            var resetJournal = transient.OfType<InnerColorAssetUndo>().Last();
            Check(hair.sharedMesh == originalMesh && hair.sharedMaterials.Length == 2 && File.Exists(second.meshPath) && File.Exists(second.materialPath),
                "Card reset must restore originals and protect files needed by saved history");
            var refreshedAfterImport = false;
            Action refresh = () => refreshedAfterImport = AssetDatabase.GetAssetPath(hair.sharedMesh) == second.meshPath &&
                InnerColorState.Load(avatar).data.meshes.Count == 1;
            InnerColorAssetUndo.Replayed += refresh;
            Replay(resetJournal, false);
            InnerColorAssetUndo.Replayed -= refresh;
            Check(refreshedAfterImport, "UI refresh must be notified after file import and state/renderer references have been restored");
            Check(AssetDatabase.GetAssetPath(hair.sharedMesh) == second.meshPath && hair.sharedMaterials.Length == 3 &&
                hair.sharedMaterials[2] != null && File.Exists(second.meshPath) && InnerColorState.Load(avatar).data.meshes.Count == 1,
                "Undo reset must reconnect freshly imported mesh and material with state");
            Check(AssetDatabase.AssetPathToGUID(first.meshPath) == firstGuid && hair.sharedMesh.GetTriangles(2).SequenceEqual(new[] { 1, 2, 3 }),
                "Undo must retain GUID and split triangles after import");
            Replay(resetJournal, true);
            Check(hair.sharedMesh == originalMesh && hair.sharedMaterials.Length == 2, "Redo reset must restore original assignments again");
            // 履歴にもシーンにも参照がないファイルは消し、Undo で GUID と内容をつなぎ直す。
            dependencies.Clear();
            var unusedJournal = InnerColorAssetUndo.Begin(avatar);
            InnerColorAssets.Cleanup(new HashSet<string> { second.meshPath, second.materialPath }, unusedJournal);
            unusedJournal.Commit("test");
            Check(!File.Exists(second.meshPath) && !File.Exists(second.materialPath), "Unused generated mesh and material must be deleted");
            Replay(unusedJournal, false);
            Check(File.Exists(second.meshPath) && File.Exists(second.materialPath) &&
                AssetDatabase.LoadAssetAtPath<Mesh>(second.meshPath).GetTriangles(2).SequenceEqual(new[] { 1, 2, 3 }),
                "Undo deletion must restore the generated files and triangles");
            Replay(unusedJournal, true);
            Check(!File.Exists(second.meshPath), "Redo deletion must remove an unreferenced generated mesh");
            // 色の明るさは暗い髪を補正し、非 lilToon では色だけにとどめる。
            var bright = new Material(source);
            InnerColorFeature.ConfigureMaterial(bright, Color.white, 4);
            Check(bright.GetVector("_MainTexHSVG").w == 0.25f, "Increasing brightness must lower gamma for dark textures");
            InnerColorFeature.ConfigureMaterial(bright, new Color(0, 0, 0), 0.25f);
            Check(bright.GetVector("_MainTexHSVG").w == 4 && bright.GetColor("_Color").r == 0, "Darkening and black presets must remain available");
            var generic = new Material((Shader)null); generic.SetColor("_Color", new Color(1, 1, 1, 0.7f));
            generic.SetVector("_MainTexHSVG", new Vector4(1, 2, 3, 4));
            InnerColorFeature.ConfigureMaterial(generic, new Color(0.2f, 0.3f, 0.4f), 4);
            Check(generic.GetVector("_MainTexHSVG").y == 2 && generic.GetColor("_Color").r == 0.2f && generic.GetColor("_Color").a == 0.7f,
                "Other shaders must receive only the selected color with original alpha");
            // 旧方式の値・画像・キーワード・元の割り当ては引き続き復元する。
            AssetDatabase.DeleteAsset(InnerColorState.PathFor(avatar));
            var oldState = new InnerColorState(); Add(oldState, folder + "/InnerColorState.asset");
            var oldCopy = new Material(source); var legacy = folder + "/Materials/Before.mat";
            Add(oldCopy, folder + "/Materials/Legacy.mat"); Add(new Material(source), legacy);
            oldCopy.SetFloat("_UseMain2ndTex", 1); oldCopy.DisableKeyword("_SUNDISK_NONE");
            oldCopy.SetTexture("_Main2ndTex", null);
            hair.sharedMaterials = new[] { oldCopy, source };
            oldState.data.entries.Add(new InnerColorState.Entry { materialPath = folder + "/Materials/Legacy.mat", originalPath = legacy,
                assignments = new List<InnerColorState.Assignment> { new InnerColorState.Assignment { rendererPath = "", rendererIndex = 0,
                    slot = 0, original = InnerColorState.AssetRef.Of(source) } } });
            oldState.data.meshes = null;
            var migrated = InnerColorState.Load(avatar);
            Check(migrated.data.meshes != null && migrated.data.entries[0].before != null && File.Exists(folder + "/InnerColorState.asset") && !File.Exists(InnerColorState.PathFor(avatar)),
                "Reading legacy state must preserve its file location and normalize missing fields");
            Restore(avatar, migrated.data.entries[0]);
            Check(hair.sharedMaterials[0] == source && oldCopy.GetFloat("_UseMain2ndTex") == 0.25f && oldCopy.IsKeywordEnabled("_SUNDISK_NONE") &&
                oldCopy.GetTexture("_Main2ndTex") == oldTexture && oldCopy.GetTextureScale("_Main2ndTex").y == 3,
                "Legacy reset must restore original slots and exact second-color properties");
            Check(File.ReadAllBytes("Assets/Purchased/Hair.asset").SequenceEqual(purchasedBytes) && originalMesh.subMeshCount == 2 &&
                originalMesh.GetTriangles(0).Length == 6 && File.ReadAllBytes("Assets/Purchased/Hair.mat").SequenceEqual(purchasedMatBytes) &&
                source.GetVector("_MainTexHSVG").y == 0.8f && source.GetColor("_Color").r == 0.3f,
                "Purchased mesh and material must remain unchanged in memory and on disk");
        }
    }
}
