using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    internal static class InnerColorAssets
    {
        internal const string Tag = "KaihenNavi.InnerColor:";

        /// <summary>同じ髪・元マテリアルには同じ名前を使う。同名の別の髪だけ番号で区別する。</summary>
        internal static string StablePath(string folder, string name, string suffix, string key)
        {
            // 先に所有者が一致するファイルを探す。別の髪の削除で若い番号が空いても作り直さない。
            var found = Find(folder, suffix, key);
            if (found != null) return found;
            var stem = folder + "/" + WorkFolders.Sanitize(name);
            for (var index = 0; ; index++)
            {
                var path = stem + (index == 0 ? "" : " " + (index + 1)) + suffix;
                if (Available(path, key)) return path;
            }
        }

        /// <summary>この印（所有者）のファイルがあれば、そのパスを返す。</summary>
        internal static string Find(string folder, string suffix, string key)
        {
            if (!Directory.Exists(folder)) return null;
            foreach (var existing in Directory.GetFiles(folder, "*" + suffix).OrderBy(p => p, StringComparer.Ordinal))
            {
                var path = existing.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && importer.userData == Tag + key) return path;
            }
            return null;
        }

        /// <summary>インナーカラーが作ったファイル（印の付いたメッシュ・マテリアル）か。色変えなどの対象から外すのに使う。</summary>
        internal static bool IsGenerated(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && (importer.userData ?? "").StartsWith(Tag, StringComparison.Ordinal);
        }

        private static bool Available(string path, string key)
        {
            var importer = AssetImporter.GetAtPath(path);
            return importer == null ? !File.Exists(path) : importer.userData == Tag + key;
        }

        internal static void Mark(string path, string key)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) return;
            importer.userData = Tag + key;
            importer.SaveAndReimport();
        }

        internal static HashSet<string> Candidates(VRCAvatarDescriptor avatar, InnerColorState state)
        {
            var folder = WorkFolders.ForAvatar(avatar.gameObject);
            var result = new HashSet<string>();
            if (state != null)
            {
                result.Add(AssetDatabase.GetAssetPath(state));
                foreach (var entry in state.data.meshes)
                {
                    result.Add(entry.meshPath);
                    result.Add(entry.materialPath);
                }
                foreach (var entry in state.data.entries)
                {
                    if (!string.IsNullOrEmpty(entry.key)) result.Add(entry.materialPath);
                    if (!string.IsNullOrEmpty(entry.maskPath)) result.Add(entry.maskPath);
                    if (!string.IsNullOrEmpty(entry.originalPath)) result.Add(entry.originalPath);
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && (importer.userData ?? "").StartsWith(Tag, StringComparison.Ordinal)) result.Add(path);
                if (path.StartsWith(folder + "/Materials/", StringComparison.Ordinal) &&
                    Regex.IsMatch(Path.GetFileName(path), @"_BeforeInnerColor(?: \d+)?\.mat$"))
                {
                    result.Add(path);
                }
                if (path.StartsWith(folder + "/Textures/", StringComparison.Ordinal) &&
                    Regex.IsMatch(Path.GetFileName(path), @"_InnerMask(?: \d+)?\.png$")) result.Add(path);
            }
            result.RemoveWhere(p => !WorkFolders.IsToolPath(p) || !p.StartsWith(folder + "/", StringComparison.Ordinal));
            return result;
        }

        // FindAssets で種類を絞った結果から、参照元にできるパスだけを残す（候補同士の参照は除外）。
        internal static string[] ReferenceRoots(IEnumerable<string> paths, HashSet<string> candidates)
        {
            // バックアップ内のサブマテリアルが検索に含まれても、履歴だけの参照では残さない。
            return paths.Where(p => !string.IsNullOrEmpty(p) && !candidates.Contains(p) &&
                (p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ||
                 (WorkFolders.IsToolPath(p) && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        /// <param name="owners">渡すと、候補を参照している Renderer・アセットを記録する（残った原因を調べるため）。</param>
        internal static HashSet<string> Referenced(HashSet<string> candidates, Dictionary<string, string> owners = null)
        {
            var references = new HashSet<string>();
            if (candidates.Count == 0) return references;
            try
            {
                var title = L10n.Tr("feature.innerColor.title");
                var message = L10n.Tr("innerColor.checkingReferences");
                EditorUtility.DisplayProgressBar(title, message, 0);
                // 非表示・別アバター・Prefab 編集中も含む。保存済みアセットの Renderer は除外し、
                // 開いているシーン（Prefab 編集用のシーンを含む）の未保存の参照を保護する。
                var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
                for (var i = 0; i < renderers.Length; i++)
                {
                    if (i % 32 == 0)
                        EditorUtility.DisplayProgressBar(title, message, 0.2f * i / Math.Max(1, renderers.Length));
                    var renderer = renderers[i];
                    if (EditorUtility.IsPersistent(renderer) || !renderer.gameObject.scene.IsValid() ||
                        !renderer.gameObject.scene.isLoaded) continue;
                    // MA（NDMF）のプレビュー用の複製など、保存されない Renderer は参照に数えない。
                    // 複製のメッシュは一時的なものだがマテリアルは元のままなので、.mat だけが残っていた。
                    if (Unsaved(renderer.hideFlags) || Unsaved(renderer.gameObject.hideFlags)) continue;
                    var skinned = renderer as SkinnedMeshRenderer;
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = skinned != null ? skinned.sharedMesh : filter == null ? null : filter.sharedMesh;
                    var owner = owners == null ? null : Describe(renderer);
                    if (mesh != null) Note(references, owners, candidates, AssetDatabase.GetAssetPath(mesh), owner);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Note(references, owners, candidates, AssetDatabase.GetAssetPath(material), owner);
                        foreach (var property in material.GetTexturePropertyNames())
                            Note(references, owners, candidates, AssetDatabase.GetAssetPath(material.GetTexture(property)), owner);
                    }
                }
                // 自分用 Prefab と、手作業で参照を追加されたマテリアルも保護する。
                // 履歴・復元台帳の参照は片付けを妨げない（消えた髪は履歴復元時に元へ戻す）。
                EditorUtility.DisplayProgressBar(title, message, 0.2f);
                var roots = ReferenceRoots(AssetDatabase.FindAssets("t:Prefab t:Material", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath), candidates);
                const int batchSize = 32;
                for (var i = 0; i < roots.Length; i += batchSize)
                {
                    EditorUtility.DisplayProgressBar(title, message, 0.2f + 0.8f * i / roots.Length);
                    var batch = roots.Skip(i).Take(batchSize).ToArray();
                    foreach (var path in AssetDatabase.GetDependencies(batch, true))
                    {
                        references.Add(path);
                        // 候補が見つかったまとまりだけ、どのアセットが参照しているかを 1 つずつ調べる
                        if (owners == null || !candidates.Contains(path) || owners.ContainsKey(path)) continue;
                        var root = batch.FirstOrDefault(b => AssetDatabase.GetDependencies(new[] { b }, true).Contains(path));
                        owners[path] = "asset " + (root ?? "?");
                    }
                }
                EditorUtility.DisplayProgressBar(title, message, 1);
                return references;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Note(HashSet<string> references, Dictionary<string, string> owners, HashSet<string> candidates,
            string path, string owner)
        {
            if (string.IsNullOrEmpty(path)) return;
            references.Add(path);
            if (owners != null && candidates.Contains(path) && !owners.ContainsKey(path)) owners[path] = owner;
        }

        private static string Describe(Renderer renderer)
        {
            var names = new List<string>();
            for (var t = renderer.transform; t != null; t = t.parent) names.Insert(0, t.name);
            return "renderer " + string.Join("/", names.ToArray()) + " (scene: " + renderer.gameObject.scene.name +
                ", hideFlags: " + renderer.hideFlags + " / " + renderer.gameObject.hideFlags + ")";
        }

        private static bool Tagged(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && (importer.userData ?? "").StartsWith(Tag, StringComparison.Ordinal);
        }

        internal static bool Unsaved(HideFlags flags)
        {
            return (flags & HideFlags.DontSaveInEditor) != 0;
        }

        /// <summary>参照されていない候補を消す。使用中で残した生成物（印の付いたファイル）を返す。</summary>
        internal static HashSet<string> Cleanup(HashSet<string> candidates, InnerColorAssetUndo journal)
        {
            var owners = new Dictionary<string, string>();
            var referenced = Referenced(candidates, owners);
            var kept = new HashSet<string>();
            foreach (var path in candidates.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!WorkFolders.IsToolPath(path) || !File.Exists(path)) continue;
                if (referenced.Contains(path))
                {
                    if (!Tagged(path)) continue;
                    // 「元に戻す」の後に .mat が 1 つ残る原因を、1 回の操作で確かめられるようにする
                    string owner;
                    owners.TryGetValue(path, out owner);
                    Debug.Log("[KaihenNavi] インナーカラー：使用中のため残したファイル " + path + " ← " + (owner ?? "不明"));
                    kept.Add(path);
                    continue;
                }
                journal.Track(path);
                AssetDatabase.DeleteAsset(path);
            }
            return kept;
        }

        /// <summary>
        /// 使用中で残した生成物を、少し後にもう一度片付ける（次の更新で作り直される MA・NDMF のプレビュー用の複製が
        /// 古い参照を一時的に持っている場合のため）。次の更新で 1 回、さらに約 1 秒後に 1 回だけ試す。
        /// 参照は毎回調べ直す。消すときは同じ Undo のまとまりにファイルの記録を足すので、Ctrl+Z 1 回で全部戻る。
        /// 別の操作が入った（Undo のまとまりが変わった）ら何もしない。
        /// </summary>
        internal static void RetryCleanup(VRCAvatarDescriptor avatar, HashSet<string> kept, int group, string undoName)
        {
            if (avatar == null || kept == null || kept.Count == 0) return;
            var remaining = new HashSet<string>(kept);
            EditorApplication.delayCall += () =>
            {
                if (RetryOnce(avatar, remaining, group, undoName)) return;
                var at = EditorApplication.timeSinceStartup + 1.0;
                EditorApplication.CallbackFunction later = null;
                later = () =>
                {
                    if (EditorApplication.timeSinceStartup < at) return;
                    EditorApplication.update -= later;
                    RetryOnce(avatar, remaining, group, undoName);
                };
                EditorApplication.update += later;
            };
        }

        /// <summary>1 回だけ片付け直す。もう試さなくてよければ true。</summary>
        private static bool RetryOnce(VRCAvatarDescriptor avatar, HashSet<string> remaining, int group, string undoName)
        {
            if (avatar == null || EditorApplication.isPlayingOrWillChangePlaymode || Undo.GetCurrentGroup() != group) return true;
            remaining.RemoveWhere(p => !File.Exists(p));
            if (remaining.Count == 0) return true;
            var journal = InnerColorAssetUndo.Begin(avatar);
            try
            {
                var kept = Cleanup(remaining, journal);
                // 消した分だけファイルの記録を Undo に載せる（何も消さなければ記録しない）。元の操作と同じまとまりにする。
                journal.Commit(undoName);
                journal = null;
                Undo.CollapseUndoOperations(group);
                remaining.IntersectWith(kept);
                return remaining.Count == 0;
            }
            catch (Exception e)
            {
                if (journal != null) journal.Rollback();
                Debug.LogException(e);
                return true;
            }
        }
    }
}
