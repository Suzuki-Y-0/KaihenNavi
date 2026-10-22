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
                    (Object)new Texture2D(1, 1, TextureFormat.RGBA32, false) : Activator.CreateInstance(previous.GetType(), true) as Object;
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
            Object.InstantiateForTests = original => new Material((Material)original);
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
        private static InnerColorState.Entry Own(VRCAvatarDescriptor avatar, SkinnedMeshRenderer renderer,
            Material source, InnerColorState state, InnerColorAssetUndo journal)
        {
            var type = typeof(InnerColorFeature).GetNestedType("Surface", Private);
            var surface = Activator.CreateInstance(type, true);
            type.GetField("renderer").SetValue(surface, renderer);
            ((Dictionary<int, Material>)type.GetField("materials").GetValue(surface)).Add(0, source);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type)); list.Add(surface);
            return (InnerColorState.Entry)typeof(InnerColorFeature).GetMethod("OwnSelected", Private)
                .Invoke(null, new object[] { avatar, source, list, state, journal, "test" });
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
            Check(!WorkFolders.IsToolPath("Assets/Purchased/Hair.mat"), "Purchased assets must not be owned");
            var roots = InnerColorAssets.ReferenceRoots(new[] {
                WorkFolders.Root + "/Bases/Base.prefab", WorkFolders.Root + "/Bases/Base.prefab",
                WorkFolders.Root + "/Avatars/Other/Backups/backup.asset", WorkFolders.Root + "/Avatars/Other/Materials/Hair.mat",
                WorkFolders.Root + "/candidate.mat", "Assets/Purchased/Hair.mat", "Assets/KaihenNaviOther/Hair.mat",
                WorkFolders.Root + "/../Purchased/Hair.mat", "", null
            }, new HashSet<string> { WorkFolders.Root + "/candidate.mat" });
            Check(roots.Length == 3 && roots.Contains(WorkFolders.Root + "/Bases/Base.prefab") &&
                roots.Contains(WorkFolders.Root + "/Avatars/Other/Backups/backup.asset") &&
                roots.Contains(WorkFolders.Root + "/Avatars/Other/Materials/Hair.mat"),
                "Reference roots must exclude candidates, purchased assets and invalid paths, and deduplicate");
            var go = new GameObject { name = "TestAvatar", scene = new UnityEngine.SceneManagement.Scene { validForTests = true, isLoaded = true } };
            var root = new Transform { gameObject = go }; root.transform = root; go.transform = root;
            var avatar = new VRCAvatarDescriptor { gameObject = go, transform = root };
            var hair = new SkinnedMeshRenderer { gameObject = go, transform = root };
            var other = new SkinnedMeshRenderer { gameObject = go, transform = root };
            var components = new Component[] { root, avatar, hair, other };
            go.ComponentsForTests = type => components.Where(type.IsInstanceOfType).Cast<object>().ToArray();
            renderers.Add(hair); renderers.Add(other);
            var source = new Material(null) { name = "Hair" };
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
            var originalBytes = File.ReadAllBytes("Assets/Purchased/Hair.mat");
            hair.sharedMaterials = new[] { source }; other.sharedMaterials = new[] { source };
            var folder = WorkFolders.ForAvatar(go);
            Directory.CreateDirectory(WorkFolders.Backups(go));
            Directory.CreateDirectory(WorkFolders.Materials(go)); Directory.CreateDirectory(folder + "/Textures");
            var context = new FeatureContext { Avatar = avatar, Backup = new BackupService(), FeatureId = "innerColor" };
            context.BeginEdit(); // 最初のプレビュー前の自動バックアップ。
            var firstJournal = InnerColorAssetUndo.Begin(avatar);
            var state = InnerColorState.Load(avatar, true);
            Check(AssetDatabase.GetAssetPath(state) == folder + "/Backups/InnerColorState.asset", "One state file must live in Backups");
            var entry = Own(avatar, hair, source, state, firstJournal);
            var firstCopy = hair.sharedMaterials[0];
            Check(firstCopy != source && other.sharedMaterials[0] == source, "Only selected renderer must use the copy");
            firstJournal.Track(entry.maskPath);
            var mask = InnerColorMask.Save(entry.maskPath, entry.key, new float[16], 4);
            firstCopy.SetFloat("_UseMain2ndTex", 1); firstCopy.SetTexture("_Main2ndBlendMask", mask);
            firstCopy.DisableKeyword("_SUNDISK_NONE"); File.WriteAllBytes(entry.maskPath, new byte[] { 1, 2, 3 }); firstJournal.Commit("test");
            var maskGuid = AssetDatabase.AssetPathToGUID(entry.maskPath);
            var secondJournal = InnerColorAssetUndo.Begin(avatar);
            Restore(avatar, entry);
            Check(hair.sharedMaterials[0] == source, "Reset must restore original renderer assignment");
            Check(firstCopy.GetFloat("_UseMain2ndTex") == 0.25f && firstCopy.IsKeywordEnabled("_SUNDISK_NONE"), "Reset must restore exact floats and keywords");
            Check(firstCopy.GetTexture("_Main2ndTex") == oldTexture && firstCopy.GetTextureScale("_Main2ndTex").y == 3 &&
                firstCopy.GetTextureOffset("_Main2ndTex").x == 0.4f, "Texture references, scale and offset must round trip");
            var repeated = Own(avatar, hair, source, state, secondJournal);
            secondJournal.Track(entry.maskPath);
            InnerColorMask.Save(repeated.maskPath, repeated.key, Enumerable.Repeat(1f, 16).ToArray(), 4);
            Check(ReferenceEquals(hair.sharedMaterials[0], firstCopy) && state.data.entries.Count == 1, "Repeated preview must reuse one material and entry");
            Check(entry.maskPath == repeated.maskPath && AssetDatabase.AssetPathToGUID(entry.maskPath) == maskGuid, "Mask overwrite must retain path and GUID");
            File.WriteAllBytes(entry.maskPath, new byte[] { 4, 5, 6 });
            secondJournal.Commit("test");
            Replay(secondJournal, false);
            Check(File.ReadAllBytes(entry.maskPath).SequenceEqual(new byte[] { 1, 2, 3 }), "Undo overwrite must restore previous PNG content");
            Replay(secondJournal, true);
            Check(File.ReadAllBytes(entry.maskPath).SequenceEqual(new byte[] { 4, 5, 6 }), "Redo overwrite must restore replacement PNG content");
            var collision = new Material(null) { name = "Hair" }; Add(collision, "Assets/Other/Hair.mat");
            var stable = InnerColorAssets.StablePath(folder + "/Materials", "Hair", "_InnerColor.mat", "other-key");
            Check(stable != entry.materialPath && stable == InnerColorAssets.StablePath(folder + "/Materials", "Hair", "_InnerColor.mat", "other-key"), "Same-name sources must get deterministic collision paths");
            var subsetJournal = InnerColorAssetUndo.Begin(avatar);
            var otherEntry = Own(avatar, other, source, state, subsetJournal);
            Check(otherEntry.materialPath != entry.materialPath && otherEntry.maskPath != entry.maskPath, "Different selected renderer must get distinct material and mask paths");
            Restore(avatar, otherEntry); state.data.entries.Remove(otherEntry); subsetJournal.Commit("test");
            var orphan = folder + "/Textures/Hair_InnerMask 1.png";
            Add(new Texture2D(1, 1, TextureFormat.RGBA32, false), orphan);
            var legacy = folder + "/Materials/Hair_BeforeInnerColor.mat";
            Add(new Material(source), legacy);
            var feature = new InnerColorFeature();
            typeof(InnerColorFeature).GetField("_status", Private).SetValue(feature, new Label());
            typeof(InnerColorFeature).GetMethod("Reset", Private).Invoke(feature, new object[] { context });
            var resetJournal = transient.OfType<InnerColorAssetUndo>().Last();
            Check(hair.sharedMaterials[0] == source && other.sharedMaterials[0] == source, "Card reset must restore all original assignments");
            Check(!File.Exists(entry.materialPath) && !File.Exists(entry.maskPath) && !File.Exists(InnerColorState.PathFor(avatar)), "Card reset must delete unused generated copy, mask and state");
            Check(!File.Exists(orphan) && !File.Exists(legacy), "Card reset must clean legacy numbered masks and backups");
            Replay(resetJournal, false);
            var restoredCopy = hair.sharedMaterials[0];
            Check(restoredCopy != null && restoredCopy != source && !ReferenceEquals(restoredCopy, firstCopy), "Undo reset must reconnect a freshly imported copy");
            Check(File.Exists(entry.materialPath) && File.Exists(entry.maskPath) && InnerColorState.Load(avatar).data.entries.Count == 1, "Undo must restore files and state");
            Check(AssetDatabase.AssetPathToGUID(entry.maskPath) == maskGuid && restoredCopy.GetTexture("_Main2ndBlendMask") != null, "Undo must preserve mask GUID and reference");
            Replay(resetJournal, true);
            Check(hair.sharedMaterials[0] == source && !File.Exists(entry.materialPath), "Redo reset must restore source and remove copy again");
            var nextJournal = InnerColorAssetUndo.Begin(avatar);
            var nextState = InnerColorState.Load(avatar, true);
            var next = Own(avatar, hair, source, nextState, nextJournal);
            Check(next.materialPath == entry.materialPath && next.maskPath == entry.maskPath, "Preview after reset must use the same fixed paths");
            nextJournal.Track(next.maskPath); InnerColorMask.Save(next.maskPath, next.key, new float[16], 4);
            nextJournal.Commit("test");
            var external = new MeshRenderer { gameObject = go, sharedMaterials = new[] { hair.sharedMaterials[0] } }; renderers.Add(external);
            Restore(avatar, next); nextState.data.entries.Clear();
            external.sharedMaterials[0].SetTexture("_Main2ndBlendMask", AssetDatabase.LoadAssetAtPath<Texture2D>(next.maskPath));
            var protectedJournal = InnerColorAssetUndo.Begin(avatar);
            InnerColorAssets.Cleanup(new HashSet<string> { next.materialPath, next.maskPath, "Assets/Purchased/Hair.mat" }, protectedJournal);
            Check(File.Exists(next.materialPath) && File.Exists(next.maskPath), "A renderer outside the avatar must protect copy and mask");
            renderers.Remove(external);
            var persisted = new MeshRenderer { gameObject = go, sharedMaterials = external.sharedMaterials };
            Add(persisted, "Assets/Purchased/LoadedRenderer.asset"); renderers.Add(persisted);
            var detached = new MeshRenderer { gameObject = new GameObject(), sharedMaterials = external.sharedMaterials };
            renderers.Add(detached);
            var closed = new MeshRenderer { gameObject = new GameObject {
                scene = new UnityEngine.SceneManagement.Scene { validForTests = true, isLoaded = false }
            }, sharedMaterials = external.sharedMaterials }; renderers.Add(closed);
            var unreferenced = InnerColorAssets.Referenced(new HashSet<string> { next.materialPath, next.maskPath });
            Check(!unreferenced.Contains(next.materialPath) && !unreferenced.Contains(next.maskPath),
                "Persistent renderers and objects outside loaded scenes must not protect candidates");
            renderers.Remove(persisted); renderers.Remove(detached); renderers.Remove(closed);
            foreach (var path in new[] { WorkFolders.Root + "/Avatars/Other/Other.prefab",
                WorkFolders.Root + "/Bases/Base.prefab", WorkFolders.Root + "/Avatars/Other/Backups/backup.asset" })
            {
                Add(new ScriptableObject(), path); dependencies[path] = new[] { next.materialPath };
                InnerColorAssets.Cleanup(new HashSet<string> { next.materialPath, next.maskPath }, protectedJournal);
                Check(File.Exists(next.materialPath) && File.Exists(next.maskPath),
                    "Tool prefab, base and backup must protect copy and mask transitively: " + path);
                dependencies.Clear();
            }
            var keeper = new Material(source); keeper.SetTexture("_Main2ndBlendMask", AssetDatabase.LoadAssetAtPath<Texture2D>(next.maskPath));
            Add(keeper, WorkFolders.Root + "/Avatars/Other/Materials/Keeper.mat");
            Check(InnerColorAssets.Referenced(new HashSet<string> { next.materialPath, next.maskPath }).Contains(next.maskPath),
                "A tool material must protect a directly referenced mask");
            keeper.SetTexture("_Main2ndBlendMask", null);
            Add(new ScriptableObject(), "Assets/Purchased/Outside.prefab");
            dependencies["Assets/Purchased/Outside.prefab"] = new[] { next.materialPath };
            var dependencyReader = AssetDatabase.DependenciesForTests;
            var cleared = false;
            var progress = new List<float>();
            EditorUtility.ProgressForTests = value => progress.Add(value);
            EditorUtility.ClearProgressForTests = () => cleared = true;
            AssetDatabase.DependenciesForTests = pathsToRead =>
            {
                Check(pathsToRead.All(p => WorkFolders.IsToolPath(p) && p != next.materialPath && p != next.maskPath),
                    "Only tool reference roots outside the candidates may be queried");
                throw new InvalidOperationException("dependency read failed");
            };
            var failed = false;
            try { InnerColorAssets.Cleanup(new HashSet<string> { next.materialPath, next.maskPath }, protectedJournal); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && cleared && File.Exists(next.materialPath) && File.Exists(next.maskPath),
                "Failure must clear progress and leave cleanup candidates intact");
            AssetDatabase.DependenciesForTests = dependencyReader;
            cleared = false;
            InnerColorAssets.Cleanup(new HashSet<string> { next.materialPath, next.maskPath }, protectedJournal);
            Check(cleared && progress.First() == 0 && progress.Last() == 1, "Successful scan must report progress and clear it");
            EditorUtility.ProgressForTests = null; EditorUtility.ClearProgressForTests = null;
            dependencies.Clear();
            Check(!File.Exists(next.materialPath) && !File.Exists(next.maskPath), "Unreferenced tool assets must be removed");
            Check(File.ReadAllBytes("Assets/Purchased/Hair.mat").SequenceEqual(originalBytes) && source.GetFloat("_UseMain2ndTex") == 0.25f, "Purchased asset must remain byte-for-byte and property unchanged");
            // 旧版の台帳を移動し、.mat 控えから元の値を読み戻せること。
            AssetDatabase.DeleteAsset(InnerColorState.PathFor(avatar));
            var oldState = new InnerColorState(); Add(oldState, folder + "/InnerColorState.asset");
            var oldCopy = new Material(source); Add(oldCopy, folder + "/Materials/Legacy.mat");
            Add(new Material(source), legacy); oldCopy.SetFloat("_UseMain2ndTex", 1);
            oldState.data.entries.Add(new InnerColorState.Entry { materialPath = folder + "/Materials/Legacy.mat", originalPath = legacy, assignments = null });
            var migrated = InnerColorState.Load(avatar);
            Check(migrated.data.entries[0].before != null && migrated.data.entries[0].assignments != null,
                "Legacy properties must migrate before backup capture, including missing new fields");
            Restore(avatar, migrated.data.entries[0]);
            Check(ReferenceEquals(migrated, oldState) && !File.Exists(folder + "/InnerColorState.asset") && File.Exists(InnerColorState.PathFor(avatar)), "Legacy state must move into Backups without duplication");
            Check(oldCopy.GetFloat("_UseMain2ndTex") == 0.25f && oldCopy.IsKeywordEnabled("_SUNDISK_NONE"), "Legacy material backups must restore without errors");
        }
    }
}
