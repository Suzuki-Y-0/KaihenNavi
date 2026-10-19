using System.Collections.Generic;

namespace Jeni.KaihenNavi
{
    /// <summary>カードに並べる「やりたいこと」の一覧。並び順がそのまま画面の順番になる。</summary>
    internal static class FeatureCatalog
    {
        public static List<FeatureBase> Create()
        {
            return new List<FeatureBase>
            {
                new OutfitFeature(),
                new TransformFeature(),
                new ColorFeature(),
                new ToggleFeature(),
                new BodyFeature(),
                new SkinFeature(),
                new ViewFeature(),
                new PinkFeature(),
                new PlannedFeature("light", 9, "#F2C14E"),
                new ThumbnailFeature(),
            };
        }
    }
}
