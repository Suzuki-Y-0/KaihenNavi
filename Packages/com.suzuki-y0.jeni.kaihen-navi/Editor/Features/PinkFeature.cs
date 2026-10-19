using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 8：ピンク色を直したい（製品版）。
    /// ピンク（マゼンタ）になる原因はいろいろあるが、初心者に多い「シェーダーがプロジェクトに入っていない」に絞って扱う。
    /// マテリアルのファイルに残っているプロパティ名から、どのシェーダーを入れればよいかを見分けて案内する。
    /// </summary>
    internal sealed class PinkFeature : FeatureBase
    {
        /// <summary>シェーダーの見分け方と、入れ方の案内。</summary>
        private sealed class ShaderFamily
        {
            public string Key;
            public string[] Markers;
            public string Url;
        }

        private static readonly ShaderFamily[] Families =
        {
            new ShaderFamily { Key = "lilToon", Markers = new[] { "_lilToonVersion" }, Url = "https://lilxyzw.github.io/lilToon/ja_JP/" },
            new ShaderFamily { Key = "Poiyomi", Markers = new[] { "shader_master_label", "_ShaderOptimizerEnabled", "_PoiParallax", "footer_github" }, Url = "https://www.poiyomi.com/" },
            new ShaderFamily { Key = "UnityChanToonShader", Markers = new[] { "_utsVersion", "_utsTechnique" }, Url = "https://github.com/unity3d-jp/UnityChanToonShaderVer2_Project" },
            new ShaderFamily { Key = "Sunao", Markers = new[] { "_SunaoShaderType", "_VersionH" }, Url = "https://booth.pm/ja/search/Sunao%20Shader" },
        };

        private sealed class Problem
        {
            public Material Material;
            public List<Renderer> Renderers;
            public string Kind;
            public ShaderFamily Family;
        }

        public override string Id { get { return "pink"; } }
        public override int Number { get { return 8; } }
        public override Color Accent { get { return Ui.Hex("#F06292"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("pink.intro"), "info"));

            var problems = Find(ctx.Avatar);
            var missingSlots = MissingSlots(ctx.Avatar);
            if (problems.Count == 0 && missingSlots.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("pink.none"), "ok"));
                return root;
            }

            // シェーダーごとにまとめて案内する（同じシェーダーを入れれば全部直るため）
            foreach (var group in problems.GroupBy(p => p.Family == null ? p.Kind : p.Family.Key))
            {
                var first = group.First();
                var box = Ui.Box("kn-pink");
                var title = first.Family != null
                    ? L10n.Tr("pink.needShader", first.Family.Key)
                    : L10n.Tr("pink." + first.Kind);
                box.Add(Ui.Text(title, "kn-heading"));

                var list = Ui.Box("kn-list");
                foreach (var p in group)
                {
                    var row = Ui.Box("kn-list__row");
                    var texts = Ui.Box("kn-list__texts");
                    texts.Add(Ui.Text(p.Material.name, "kn-list__title"));
                    texts.Add(Ui.Text(string.Join(", ", p.Renderers.Select(r => r.name).Distinct().Take(3).ToArray()), "kn-list__sub"));
                    row.Add(texts);
                    var m = p.Material;
                    row.Add(Ui.Button(L10n.Tr("common.where"), () => EditorGUIUtility.PingObject(m), "kn-button--small", "kn-button--ghost"));
                    list.Add(row);
                }

                box.Add(list);

                if (first.Family != null)
                {
                    box.Add(Ui.Text(L10n.Tr("pink.how." + first.Family.Key), "kn-hint"));
                    var url = first.Family.Url;
                    box.Add(Ui.Button(L10n.Tr("pink.openSite", first.Family.Key), () => Application.OpenURL(url), "kn-button--primary"));
                }
                else
                {
                    box.Add(Ui.Text(L10n.Tr("pink.how." + first.Kind), "kn-hint"));
                }

                root.Add(box);
            }

            if (missingSlots.Count > 0)
            {
                root.Add(Ui.Heading(L10n.Tr("pink.missingMaterial")));
                var list = Ui.Box("kn-list");
                foreach (var r in missingSlots)
                {
                    var row = Ui.Box("kn-list__row");
                    row.Add(Ui.Text(r.name, "kn-list__title", "kn-grow"));
                    var target = r;
                    row.Add(Ui.Button(L10n.Tr("common.where"), () =>
                    {
                        EditorGUIUtility.PingObject(target.gameObject);
                        Selection.activeGameObject = target.gameObject;
                    }, "kn-button--small", "kn-button--ghost"));
                    list.Add(row);
                }

                root.Add(list);
                root.Add(Ui.Text(L10n.Tr("pink.missingMaterialHow"), "kn-hint"));
            }

            root.Add(Ui.Button(L10n.Tr("pink.recheck"), () => ctx.Rebuild(), "kn-button--ghost"));
            return root;
        }

        // ---------------------------------------------------------------- 調べる

        private static List<Problem> Find(VRCAvatarDescriptor avatar)
        {
            var result = new List<Problem>();
            foreach (var pair in MaterialCopier.Collect(avatar))
            {
                var material = pair.Key;
                var kind = Diagnose(material);
                if (kind == null) continue;
                result.Add(new Problem
                {
                    Material = material,
                    Renderers = pair.Value,
                    Kind = kind,
                    Family = kind == "missing" || kind == "locked" ? Identify(material) : null,
                });
            }

            return result;
        }

        /// <summary>ピンクになる理由。問題なければ null。</summary>
        private static string Diagnose(Material material)
        {
            var shader = material.shader;
            if (shader == null || shader.name == "Hidden/InternalErrorShader") return "missing";

            // Poiyomi などの「ロック」したシェーダーは、生成されたファイルが無いとピンクになる
            if (shader.name.StartsWith("Hidden/Locked/")) return "locked";

            if (!shader.isSupported) return "unsupported";
            if (ShaderUtil.ShaderHasError(shader)) return "error";
            return null;
        }

        /// <summary>マテリアルのファイルに残っているプロパティ名から、元のシェーダーを見分ける。</summary>
        private static ShaderFamily Identify(Material material)
        {
            var path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                return null;
            }

            foreach (var family in Families)
            {
                if (family.Markers.Any(m => Regex.IsMatch(text, @"\b" + Regex.Escape(m) + @"\b"))) return family;
            }

            return null;
        }

        /// <summary>マテリアルの枠が空のレンダラー（何も割り当てられていない所もピンクになる）。</summary>
        private static List<Renderer> MissingSlots(VRCAvatarDescriptor avatar)
        {
            var result = new List<Renderer>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if (r.sharedMaterials.Any(m => m == null)) result.Add(r);
            }

            return result;
        }
    }
}
