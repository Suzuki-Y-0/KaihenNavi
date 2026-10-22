using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    internal static class InnerColorAssets
    {
        internal const string Tag = "KaihenNavi.InnerColor:";

        internal static string Hash(string key)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
        }

        // 同名の別商品や別の選択範囲は区別する。未知の既存ファイルは上書きしない。
        internal static string StablePath(string folder, string name, string suffix, string key)
        {
            var stem = folder + "/" + WorkFolders.Sanitize(name);
            var path = stem + suffix;
            if (Available(path, key)) return path;
            var hash = Hash(key);
            for (var length = 10; length <= hash.Length; length += 2)
            {
                path = stem + "_" + hash.Substring(0, length) + suffix;
                if (Available(path, key)) return path;
            }
            throw new InvalidOperationException("InnerColor asset identity collision");
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
            return paths.Where(p => WorkFolders.IsToolPath(p) && !candidates.Contains(p))
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
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        references.Add(AssetDatabase.GetAssetPath(material));
                        foreach (var property in material.GetTexturePropertyNames())
                            references.Add(AssetDatabase.GetAssetPath(material.GetTexture(property)));
                    }
                }
                // 購入アセットは直接変更しないため、ツール外のアセットがツール製のマスクやコピーを
                // 参照することはない。全プロジェクトを走査せず、ツール内の参照元だけを調べる。
                // 改変アバター・ベース・別バージョン・自動バックアップと、直接参照し得る .mat が対象。
                EditorUtility.DisplayProgressBar(title, message, 0.2f);
                var roots = AssetDatabase.IsValidFolder(WorkFolders.Root)
                    ? ReferenceRoots(AssetDatabase.FindAssets("t:Prefab t:ScriptableObject t:Material", new[] { WorkFolders.Root })
                        .Select(AssetDatabase.GUIDToAssetPath), candidates)
                    : new string[0];
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
