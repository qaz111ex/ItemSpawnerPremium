using System;

namespace ItemSpawnerEnhancement
{
    /// <summary>本地化门面：延迟初始化 LocalizationCatalog，按当前游戏语言查询文案。</summary>
    internal static class Loc
    {
        private static LocalizationCatalog _catalog;

        public static string Get(string key)
        {
            if (_catalog == null)
            {
                _catalog = new LocalizationCatalog(typeof(Loc).Assembly);
            }
            return _catalog.Get(GameLanguage.CurrentCode, key);
        }
    }
}
