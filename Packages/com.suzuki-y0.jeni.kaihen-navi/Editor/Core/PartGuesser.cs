using System.Collections.Generic;
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
            foreach (var rule in Rules)
            {
                foreach (var keyword in rule.Value)
                {
                    if (lower.Contains(keyword))
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
