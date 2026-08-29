using System;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 本地化门面：延迟初始化 <see cref="LocalizationCatalog"/>，按当前游戏语言查询文案。
    ///
    /// 语言代码不直接向 GameLanguage/LocalizedText 取，而是通过可注入的
    /// <see cref="LanguageCodeProvider"/>：这样本类与 LocalizationCatalog 都不再引用游戏程序集，
    /// 可以被 tests\ 下的 NUnit 工程以源码链接方式直接测试（含内嵌资源加载与英文回退链）。
    /// 运行时由 Plugin.Awake 注入 <c>GameLanguage.CurrentCode</c>。
    /// </summary>
    internal static class Loc
    {
        private static LocalizationCatalog _catalog;

        /// <summary>
        /// 当前语言代码提供者。运行时由 Plugin.Awake 注入；未注入时回退英文
        /// （只会发生在极早期或测试环境，此时显示英文比抛异常合适）。
        /// 用属性而非字段：本文件也被 tests\ 工程源码链接，那里没有 Plugin 赋值点，
        /// 字段会触发 CS0649「从未赋值」警告，而本仓库要求 0 警告。
        /// </summary>
        internal static Func<string> LanguageCodeProvider { get; set; }

        /// <summary>
        /// 日志回调（可选）。运行时由 Plugin.Awake 注入 BepInEx logger；
        /// 未注入时静默 —— 本类可能在 Plugin.Awake 之前被间接调用，那时没有可用的 logger。
        /// </summary>
        internal static Action<string> WarningLogger { get; set; }

        /// <summary>供测试重置缓存（更换内嵌资源来源或语言提供者后调用）。</summary>
        internal static void ResetForTesting(LocalizationCatalog catalog)
        {
            _catalog = catalog;
        }

        public static string Get(string key)
        {
            if (_catalog == null)
            {
                _catalog = new LocalizationCatalog(typeof(Loc).Assembly, WarningLogger);
            }
            string code = (LanguageCodeProvider != null) ? LanguageCodeProvider() : "en";
            return _catalog.Get(code, key);
        }
    }
}
