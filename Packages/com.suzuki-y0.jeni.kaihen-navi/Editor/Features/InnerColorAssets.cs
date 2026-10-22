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

        internal static HashSet<string> Referenced(HashSet<string> candidates)
        {
            var references = new HashSet<string>();
            // 非表示・別アバター・Prefab 編集中も含む。シーンにまだ保存していない参照も見る。
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    references.Add(AssetDatabase.GetAssetPath(material));
                    foreach (var property in material.GetTexturePropertyNames())
                        references.Add(AssetDatabase.GetAssetPath(material.GetTexture(property)));
                }
            // 未ロードの Prefab、別バージョン、自動バックアップなどの参照も保護する。
            var outside = AssetDatabase.GetAllAssetPaths().Where(p => !candidates.Contains(p) &&
                !AssetDatabase.IsValidFolder(p)).ToArray();
            foreach (var path in AssetDatabase.GetDependencies(outside, true)) references.Add(path);
            return references;
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
