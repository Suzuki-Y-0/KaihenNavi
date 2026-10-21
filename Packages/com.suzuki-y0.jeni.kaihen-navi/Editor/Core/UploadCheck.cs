using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// VRChat SDK がアップロードを止める問題のうち、モデルの読み込み設定が原因のものを先に見つけて直す。
    /// 判定は SDK（VRCSdkControlPanelAvatarBuilder の CheckAvatarMeshesForMeshReadWriteSetting /
    /// CheckAvatarMeshesForLegacyBlendShapesSetting）と同じにしてある。直し方も SDK の Auto Fix と同じで、
    /// モデルの .meta の設定だけを変え、モデルそのものは変えない。
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
            /// <summary>シェイプキーの法線の設定を確認できなかった（Unity の内部の名前が変わったときなど）。</summary>
            BlendShapeNormalsUnknown,
        }

        /// <summary>1 つのモデルファイル（FBX など）について見つかった問題。</summary>
        public sealed class Issue
        {
            public string ModelPath;
            public bool CanFix = true;
            public readonly HashSet<Kind> Kinds = new HashSet<Kind>();
        }

        /// <summary>SDK と同じく、ModelImporter の非公開プロパティをリフレクションで読む。</summary>
        private static readonly PropertyInfo LegacyBlendShapeNormals = typeof(ModelImporter).GetProperty(
            "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>アバターの中で使われているメッシュの読み込み設定を調べる。</summary>
        public static List<Issue> Find(VRCAvatarDescriptor avatar)
        {
            var result = new Dictionary<string, Issue>();
            if (avatar == null) return new List<Issue>();

            foreach (var mesh in Meshes(avatar.gameObject))
            {
                if (!AssetDatabase.Contains(mesh)) continue;
                var path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                var kinds = new List<Kind>();
                if (!importer.isReadable) kinds.Add(Kind.ReadWrite);
                if (importer.importBlendShapeNormals == ModelImporterNormals.Calculate)
                {
                    if (LegacyBlendShapeNormals == null) kinds.Add(Kind.BlendShapeNormalsUnknown);
                    else if (!(bool)LegacyBlendShapeNormals.GetValue(importer, null)) kinds.Add(Kind.BlendShapeNormals);
                }

                if (kinds.Count == 0) continue;

                Issue issue;
                if (!result.TryGetValue(path, out issue))
                {
                    issue = new Issue { ModelPath = path };
                    result.Add(path, issue);
                }

                foreach (var k in kinds) issue.Kinds.Add(k);
                if (issue.Kinds.Contains(Kind.BlendShapeNormalsUnknown)) issue.CanFix = false;
            }

            return result.Values.OrderBy(i => i.ModelPath).ToList();
        }

        /// <summary>SDK と同じ範囲のメッシュ（スキンメッシュ・メッシュ・パーティクル。EditorOnly の下は除く）。</summary>
        private static HashSet<Mesh> Meshes(GameObject root)
        {
            var meshes = new HashSet<Mesh>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr != null && smr.sharedMesh != null && !IsEditorOnly(smr.transform, root.transform)) meshes.Add(smr.sharedMesh);
            }

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf != null && mf.sharedMesh != null && !IsEditorOnly(mf.transform, root.transform)) meshes.Add(mf.sharedMesh);
            }

            foreach (var psr in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (psr == null || IsEditorOnly(psr.transform, root.transform)) continue;
                var list = new Mesh[psr.meshCount];
                psr.GetMeshes(list);
                foreach (var m in list)
                {
                    if (m != null) meshes.Add(m);
                }
            }

            return meshes;
        }

        /// <summary>自分か親（アバターまで）に EditorOnly タグが付いていればアップロードされない。</summary>
        private static bool IsEditorOnly(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (t.gameObject.tag == "EditorOnly") return true;
                if (t == root) break;
            }

            return false;
        }

        /// <summary>見つかった問題を直す（読み込み設定を変えて読み込み直す）。直したファイルの数を返す。</summary>
        public static int Fix(IEnumerable<Issue> issues)
        {
            var count = 0;
            foreach (var issue in issues)
            {
                if (!issue.CanFix) continue;
                var importer = AssetImporter.GetAtPath(issue.ModelPath) as ModelImporter;
                if (importer == null) continue;

                if (issue.Kinds.Contains(Kind.ReadWrite)) importer.isReadable = true;
                if (issue.Kinds.Contains(Kind.BlendShapeNormals) && LegacyBlendShapeNormals != null)
                {
                    LegacyBlendShapeNormals.SetValue(importer, true, null);
                }

                importer.SaveAndReimport();
                count++;
            }

            return count;
        }

        /// <summary>画面と確認ダイアログに出す一覧（「Assets/」を省いたファイル名と、問題の内容）。</summary>
        public static string Describe(IEnumerable<Issue> issues)
        {
            var lines = new List<string>();
            foreach (var issue in issues)
            {
                // 長いパスでも「・」とファイル名が離れないよう、ファイル名と置き場所を分けて書く
                var path = issue.ModelPath.StartsWith("Assets/") ? issue.ModelPath.Substring(7) : issue.ModelPath;
                var slash = path.LastIndexOf('/');
                var name = slash < 0 ? path : path.Substring(slash + 1);
                var folder = slash < 0 ? "" : path.Substring(0, slash);
                var what = string.Join(" / ", issue.Kinds.OrderBy(k => k).Select(k =>
                    k == Kind.ReadWrite ? L10n.Tr("check.kind.readWrite") :
                    k == Kind.BlendShapeNormals ? L10n.Tr("check.kind.normals") :
                    L10n.Tr("check.kind.normalsUnknown")).ToArray());
                lines.Add("・" + name + "：" + what);
                if (folder.Length > 0) lines.Add("　　" + L10n.Tr("check.folder", folder));
            }

            return string.Join("\n", lines.ToArray());
        }
    }
}
