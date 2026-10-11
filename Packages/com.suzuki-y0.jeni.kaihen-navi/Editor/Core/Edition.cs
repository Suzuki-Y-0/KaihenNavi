namespace Jeni.KaihenNavi
{
    /// <summary>体験版か製品版か。製品版はスクリプト定義シンボル KAIHENNAVI_FULL を付けてビルドする。</summary>
    internal static class Edition
    {
#if KAIHENNAVI_FULL
        public static readonly bool IsTrial = false;
#else
        public static readonly bool IsTrial = true;
#endif

        public const string Version = "0.1.0";
        public const string BoothUrl = "https://booth.pm/";
    }
}
