using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    internal enum BodyPart
    {
        Hair,
        Cloth,
        Eye,
        Skin,
        Other,
    }

    /// <summary>
    /// オブジェクト名・マテリアル名のキーワードから「髪・服・目・肌」を推定する。
    /// 外れた場合は画面で選び直せる（Override）。
    /// </summary>
    internal static class PartGuesser
    {
        // 上から順に判定する（「eyelash」を「目」より先に「その他」へ振り分けるため）
        private static readonly KeyValuePair<BodyPart, string[]>[] Rules =
        {
            new KeyValuePair<BodyPart, string[]>(BodyPart.Other, new[]
            {
                "eyelash", "lash", "eyebrow", "brow", "matsuge", "mayu", "まつ毛", "まつげ", "睫毛", "眉",
                "eyeline", "highlight", "ハイライト", "tear", "涙", "cheek", "頬", "tooth", "teeth", "歯", "tongue", "舌",
            }),
            new KeyValuePair<BodyPart, string[]>(BodyPart.Eye, new[]
            {
                "eye", "iris", "pupil", "hitomi", "瞳", "目",
            }),
            new KeyValuePair<BodyPart, string[]>(BodyPart.Hair, new[]
            {
                "hair", "kami", "bang", "fringe", "ponytail", "twintail", "髪", "前髪", "後ろ髪", "横髪", "アホ毛",
            }),
            new KeyValuePair<BodyPart, string[]>(BodyPart.Skin, new[]
            {
                "body", "skin", "face", "head", "karada", "hada", "kao", "肌", "顔", "体", "素体",
            }),
            new KeyValuePair<BodyPart, string[]>(BodyPart.Cloth, new[]
            {
                "cloth", "costume", "outfit", "wear", "shirt", "skirt", "pants", "shorts", "jacket", "coat", "dress",
                "hoodie", "parka", "sweater", "tops", "bottoms", "shoes", "boots", "sock", "glove", "hat", "cap",
                "ribbon", "tie", "belt", "frill", "apron", "uniform", "swim", "inner", "bra", "服", "衣装", "靴",
                "帽子", "スカート", "パーカー", "リボン",
            }),
        };

        /// <summary>名前に服・小物の言葉が入っているか（「Cloth_HairRibbon」のように髪の言葉と両方あるときに服を優先するため）。</summary>
        public static bool LooksLikeCloth(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            var words = Words(name);
            foreach (var rule in Rules)
            {
                if (rule.Key != BodyPart.Cloth) continue;
                foreach (var word in rule.Value)
                {
                    if (Hit(word, lower, words)) return true;
                }
            }

            return false;
        }

        // 英字の語は「語の頭」で比べる。「brow」が「Brown」に、「head」が「Headphone」に当たらないよう、
        // 頭だけ一致しても別の語になるものは完全一致だけにする。
        private static readonly HashSet<string> WholeWordOnly = new HashSet<string> { "brow", "head" };

        // キャメルケースの切れ目と、英字の連なり（数字・記号・日本語は区切り）
        private static readonly Regex CamelBoundary = new Regex("(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant);
        private static readonly Regex AsciiWord = new Regex("[a-z]+", RegexOptions.CultureInvariant);

        /// <summary>「Hair_Brown」「hair01」「CamelCase」などを、小文字の英字の語に分ける。</summary>
        private static string[] Words(string name)
        {
            var spaced = CamelBoundary.Replace(name, " ").ToLowerInvariant();
            var matches = AsciiWord.Matches(spaced);
            var words = new string[matches.Count];
            for (var i = 0; i < words.Length; i++) words[i] = matches[i].Value;
            return words;
        }

        /// <summary>キーワードが名前に当たるか。英字は語単位（複数形と、長めの語の頭一致を含む）、日本語は部分一致。</summary>
        private static bool Hit(string keyword, string lower, string[] words)
        {
            foreach (var c in keyword)
            {
                if (c >= 128) return lower.Contains(keyword);
            }

            foreach (var word in words)
            {
                if (word == keyword || word == keyword + "s" || word == keyword + "es") return true;
                if (keyword.Length >= 4 && !WholeWordOnly.Contains(keyword) && word.StartsWith(keyword, System.StringComparison.Ordinal)) return true;
            }

            return false;
        }

        public static BodyPart Guess(Renderer renderer, Material material, Renderer body)
        {
            // マテリアル名を優先し、だめならオブジェクト名で判定する
            BodyPart part;
            if (material != null && TryMatch(material.name, out part)) return part;
            if (renderer != null && TryMatch(renderer.name, out part)) return part;
            if (material != null && material.HasProperty("_MainTex") && material.mainTexture != null &&
                TryMatch(material.mainTexture.name, out part)) return part;

            // 素体に乗っているマテリアルは肌、それ以外の追加メッシュは服とみなす
            if (renderer != null && renderer == body) return BodyPart.Skin;
            return BodyPart.Cloth;
        }

        private static bool TryMatch(string name, out BodyPart part)
        {
            var lower = name.ToLowerInvariant();
            var words = Words(name);
            foreach (var rule in Rules)
            {
                foreach (var keyword in rule.Value)
                {
                    if (Hit(keyword, lower, words))
                    {
                        part = rule.Key;
                        return true;
                    }
                }
            }

            part = BodyPart.Other;
            return false;
        }
    }
}
