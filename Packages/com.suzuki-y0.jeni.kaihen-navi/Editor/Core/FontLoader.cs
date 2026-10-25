using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 画面の文字を、言語ごとに 1 つのフォントでそろえる。
    /// Unity エディタの標準フォントには日本語が無く、足りない文字ごとに別のフォントへ切り替わるため、
    /// 漢字とかなで太さや形がばらばらに見える。それを避ける。
    /// 日本語と英語はパッケージに同梱した LINE Seed JP（OFL、Editor フォルダの中なのでアバターには入らない）を使う。
    /// LINE Seed JP に無い文字や、中国語・韓国語は Windows に最初から入っているフォントで補う。
    /// どれも見つからなければ何もしない（Unity の標準のまま）。
    /// </summary>
    internal static class FontLoader
    {
        private const string BoldAppliedClass = "kn-bold-font";

        // 言語ごとの候補（上から順に、見つかったものを使う）
        private static readonly Dictionary<string, string[]> Families = new Dictionary<string, string[]>
        {
            { "ja-system", new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic" } },
            { "zh-Hans", new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" } },
            { "ko", new[] { "Malgun Gothic", "Gulim" } },
            { "en", new[] { "Segoe UI" } },
        };

        // アバター名などに他の言語の文字が混ざっても形がそろうよう、後ろに付ける順番
        private static readonly string[] FallbackOrder = { "ja-system", "zh-Hans", "ko" };

        // 同梱フォントを使う言語
        private static readonly string[] BundledLanguages = { "ja", "en" };
        private const string BundledRegular = "/Fonts/LINESeedJP-Regular.ttf";
        private const string BundledBold = "/Fonts/LINESeedJP-Bold.ttf";

        private sealed class Pair
        {
            public FontAsset Regular;
            public FontAsset Bold;
        }

        private static readonly Dictionary<string, Pair> Loaded = new Dictionary<string, Pair>();
        private static readonly Dictionary<string, Pair> Combined = new Dictionary<string, Pair>();
        private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> Sweeps = new Dictionary<VisualElement, IVisualElementScheduledItem>();

        /// <summary>
        /// root 以下の文字にフォントを当てる。太字（USS の -unity-font-style: bold）は、
        /// 実行時に作ったフォントでは Unity が太らせてくれないので、太字の書体を直接当てる。
        /// 画面は作り直されることがあるので、開いている間はこまめに見直す。
        /// </summary>
        public static void Apply(VisualElement root, string language)
        {
            IVisualElementScheduledItem previous;
            if (Sweeps.TryGetValue(root, out previous))
            {
                previous.Pause();
                Sweeps.Remove(root);
            }

            var fonts = ForLanguage(language);
            if (fonts == null) return;

            root.style.unityFontDefinition = FontDefinition.FromSDFFont(fonts.Regular);
            if (fonts.Bold == null) return;

            var bold = FontDefinition.FromSDFFont(fonts.Bold);
            Sweeps[root] = root.schedule.Execute(() => ApplyBold(root, bold)).Every(100);
        }

        private static void ApplyBold(VisualElement root, FontDefinition bold)
        {
            root.Query<UnityEngine.UIElements.TextElement>().ForEach(e =>
            {
                if (e.ClassListContains(BoldAppliedClass)) return;
                var style = e.resolvedStyle.unityFontStyleAndWeight;
                if (style != FontStyle.Bold && style != FontStyle.BoldAndItalic) return;

                e.AddToClassList(BoldAppliedClass);
                e.style.unityFontDefinition = bold;
                // 太字の書体をさらに太らせないよう、指定は普通に戻す
                e.style.unityFontStyleAndWeight = style == FontStyle.Bold ? FontStyle.Normal : FontStyle.Italic;
            });
        }

        private static Pair ForLanguage(string language)
        {
            Pair result;
            if (Combined.TryGetValue(language, out result)) return result;

            // 代替の並びを言語ごとに変えるため、主フォントは言語ごとに別に作る
            result = CreateNew(language);
            if (result != null)
            {
                var regularFallbacks = new List<FontAsset>();
                var boldFallbacks = new List<FontAsset>();
                foreach (var code in FallbackOrder)
                {
                    if (code == language) continue;
                    var f = Find(code);
                    if (f == null) continue;
                    regularFallbacks.Add(f.Regular);
                    boldFallbacks.Add(f.Bold ?? f.Regular);
                }

                result.Regular.fallbackFontAssetTable = regularFallbacks;
                if (result.Bold != null) result.Bold.fallbackFontAssetTable = boldFallbacks;
            }

            Combined[language] = result;
            return result;
        }

        /// <summary>代替用（共有）。</summary>
        private static Pair Find(string language)
        {
            Pair result;
            if (Loaded.TryGetValue(language, out result)) return result;
            result = CreateNew(language);
            Loaded[language] = result;
            return result;
        }

        private static Pair CreateNew(string language)
        {
            if (System.Array.IndexOf(BundledLanguages, language) >= 0)
            {
                var regular = CreateBundled(BundledRegular);
                if (regular != null) return new Pair { Regular = regular, Bold = CreateBundled(BundledBold) };
                language = "ja-system";
            }

            string[] names;
            if (!Families.TryGetValue(language, out names)) return null;

            foreach (var name in names)
            {
                var regular = Create(name, "Regular");
                if (regular == null) continue;
                return new Pair { Regular = regular, Bold = Create(name, "Bold") };
            }

            return null;
        }

        private static FontAsset CreateBundled(string file)
        {
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(PackagePaths.Editor + file);
            if (font == null) return null;

            FontAsset asset;
            try
            {
                asset = FontAsset.CreateFontAsset(font);
            }
            catch (System.Exception)
            {
                asset = null;
            }

            if (asset != null) asset.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }

        private static FontAsset Create(string family, string style)
        {
            FontAsset asset;
            try
            {
                asset = FontAsset.CreateFontAsset(family, style, 90);
            }
            catch (System.Exception)
            {
                asset = null;
            }

            if (asset != null) asset.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }
    }
}
