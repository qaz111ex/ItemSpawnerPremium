namespace ItemSpawnerEnhancement
{
    /// <summary>把游戏 <see cref="LocalizedText.Language"/> 枚举映射为本地化语言代码字符串。</summary>
    internal static class GameLanguage
    {
        public static string CurrentCode
        {
            get { return ToCode(LocalizedText.CURRENT_LANGUAGE); }
        }

        public static string ToCode(LocalizedText.Language language)
        {
            switch (language)
            {
                case LocalizedText.Language.French: return "fr";
                case LocalizedText.Language.Italian: return "it";
                case LocalizedText.Language.German: return "de";
                case LocalizedText.Language.SpanishSpain: return "es-ES";
                case LocalizedText.Language.SpanishLatam: return "es-419";
                case LocalizedText.Language.BRPortuguese: return "pt-BR";
                case LocalizedText.Language.Russian: return "ru";
                case LocalizedText.Language.Ukrainian: return "uk";
                case LocalizedText.Language.SimplifiedChinese: return "zh-Hans";
                case LocalizedText.Language.TraditionalChinese: return "zh-Hant";
                case LocalizedText.Language.Japanese: return "ja";
                case LocalizedText.Language.Korean: return "ko";
                case LocalizedText.Language.Polish: return "pl";
                case LocalizedText.Language.Turkish: return "tr";
                default: return "en";
            }
        }
    }
}
