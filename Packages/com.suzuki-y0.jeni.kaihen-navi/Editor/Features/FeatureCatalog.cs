using System.Collections.Generic;

namespace Jeni.KaihenNavi
{
    /// <summary>ナビに並べる「やりたいこと」の一覧。並び順がそのまま画面の順番になる。</summary>
    internal static class FeatureCatalog
    {
        public static List<FeatureBase> Create()
        {
            return new List<FeatureBase>
            {
                new OutfitFeature(),
                new TransformFeature(),
                new ToggleFeature(),
                new SkinFeature(),
                new ColorFeature(),
                new InnerColorFeature(),
                new LightFeature(),
                new PinkFeature(),
                new BodyFeature(),
                new ViewFeature(),
                new WalkFeature(),
                new MenuFeature(),
                new ThumbnailFeature(),
            };
        }
    }
}
