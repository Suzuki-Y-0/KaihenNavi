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
            if (Directory.Exists(folder))
                foreach (var existing in Directory.GetFiles(folder, "*" + suffix).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var path = existing.Replace('\\', '/');
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer != null && importer.userData == Tag + key) return path;
                }
            var stem = folder + "/" + WorkFolders.Sanitize(name);
            for (var index = 0; ; index++)
            {
                var path = stem + (index == 0 ? "" : " " + (index + 1)) + suffix;
                if (Available(path, key)) return path;
            }
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

        internal static HashSet<string> Referenced(HashSet<string> candidates)
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
                    var skinned = renderer as SkinnedMeshRenderer;
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = skinned != null ? skinned.sharedMesh : filter == null ? null : filter.sharedMesh;
                    if (mesh != null) references.Add(AssetDatabase.GetAssetPath(mesh));
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        references.Add(AssetDatabase.GetAssetPath(material));
                        foreach (var property in material.GetTexturePropertyNames())
                            references.Add(AssetDatabase.GetAssetPath(material.GetTexture(property)));
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
                    foreach (var path in AssetDatabase.GetDependencies(batch, true)) references.Add(path);
                }
                EditorUtility.DisplayProgressBar(title, message, 1);
                return references;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        internal static void Cleanup(HashSet<string> candidates, InnerColorAssetUndo journal)
        {
            var referenced = Referenced(candidates);
            foreach (var path in candidates.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!WorkFolders.IsToolPath(path) || referenced.Contains(path) || !File.Exists(path)) continue;
                journal.Track(path);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
