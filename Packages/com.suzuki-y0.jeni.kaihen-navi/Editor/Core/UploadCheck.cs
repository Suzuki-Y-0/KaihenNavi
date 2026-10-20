using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// VRChat SDK がアップロードを止める問題のうち、モデルの読み込み設定が原因のものを先に見つけて直す。
    /// SDK の Auto Fix と同じ変更（FBX の .meta の設定）だけを行い、モデルそのものは変えない。
    /// </summary>
    internal static class UploadCheck
    {
        /// <summary>見つかった問題の種類。</summary>
        public enum Kind
        {
            /// <summary>Read/Write がオフ。</summary>
            ReadWrite,
            /// <summary>シェイプキーの法線が Calculate なのに Legacy Blendshape Normals がオフ。</summary>
            BlendShapeNormals,
        }

        /// <summary>1 つのモデルファイル（FBX など）について見つかった問題。</summary>
        public sealed class Issue
        {
            public string ModelPath;
            public readonly HashSet<Kind> Kinds = new HashSet<Kind>();
            public readonly List<string> UsedBy = new List<string>();
        }

        /// <summary>SDK と同じ、Unity 内部の設定名。</summary>
        private const string LegacyBlendShapeNormals = "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes";

        /// <summary>アバターの中で使われているメッシュの読み込み設定を調べる。</summary>
        public static List<Issue> Find(VRCAvatarDescriptor avatar)
        {
            var result = new Dictionary<string, Issue>();
            if (avatar == null) return new List<Issue>();

            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                Inspect(smr.sharedMesh, smr.gameObject.name, smr.sharedMesh.blendShapeCount > 0, result);
            }

            foreach (var mf in avatar.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                Inspect(mf.sharedMesh, mf.gameObject.name, false, result);
            }

            return result.Values.OrderBy(i => i.ModelPath).ToList();
        }

        private static void Inspect(Mesh mesh, string user, bool hasBlendShapes, Dictionary<string, Issue> result)
        {
            var path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/") && !path.StartsWith("Packages/")) return;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            var kinds = new List<Kind>();
            if (!importer.isReadable) kinds.Add(Kind.ReadWrite);
            if (hasBlendShapes && NeedsLegacyNormals(importer)) kinds.Add(Kind.BlendShapeNormals);
            if (kinds.Count == 0) return;

            Issue issue;
            if (!result.TryGetValue(path, out issue))
            {
                issue = new Issue { ModelPath = path };
                result.Add(path, issue);
            }

            foreach (var k in kinds) issue.Kinds.Add(k);
            if (!issue.UsedBy.Contains(user)) issue.UsedBy.Add(user);
        }

        private static bool NeedsLegacyNormals(ModelImporter importer)
        {
            if (importer.importBlendShapeNormals != ModelImporterNormals.Calculate) return false;
            var property = new SerializedObject(importer).FindProperty(LegacyBlendShapeNormals);
            return property != null && !property.boolValue;
        }

        /// <summary>見つかった問題を直す（読み込み設定を変えて読み込み直す）。直したファイルの数を返す。</summary>
        public static int Fix(IEnumerable<Issue> issues)
        {
            var count = 0;
            foreach (var issue in issues)
            {
                var importer = AssetImporter.GetAtPath(issue.ModelPath) as ModelImporter;
                if (importer == null) continue;

                if (issue.Kinds.Contains(Kind.ReadWrite)) importer.isReadable = true;
                if (issue.Kinds.Contains(Kind.BlendShapeNormals))
                {
                    var so = new SerializedObject(importer);
                    var property = so.FindProperty(LegacyBlendShapeNormals);
                    if (property != null)
                    {
                        property.boolValue = true;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                importer.SaveAndReimport();
                count++;
            }

            return count;
        }

        /// <summary>確認画面に出す一覧（「Assets/」を省いたファイル名と、問題の内容）。</summary>
        public static string Describe(IEnumerable<Issue> issues)
        {
            var lines = new List<string>();
            foreach (var issue in issues)
            {
                var name = issue.ModelPath.StartsWith("Assets/") ? issue.ModelPath.Substring(7) : issue.ModelPath;
                var what = string.Join(" / ", issue.Kinds.OrderBy(k => k).Select(k => L10n.Tr("check.kind." + (k == Kind.ReadWrite ? "readWrite" : "normals"))).ToArray());
                lines.Add("・" + name + "（" + what + "）");
            }

            return string.Join("\n", lines.ToArray());
        }
    }
}
