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
                new ColorFeature(),
                new ToggleFeature(),
                new TransformFeature(),
                new PlannedFeature("skin", 3, "#E8A87C"),
                new PlannedFeature("shape", 4, "#C38D9E"),
                new PlannedFeature("height", 5, "#41B3A3"),
                new PlannedFeature("view", 7, "#5DA9E9"),
                new PlannedFeature("pink", 8, "#F06292"),
                new PlannedFeature("light", 9, "#F2C14E"),
                new PlannedFeature("thumbnail", 10, "#8E7DBE"),
            };
        }
    }
}
