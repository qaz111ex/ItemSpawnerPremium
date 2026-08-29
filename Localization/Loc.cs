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
                // 注意 WarningLogger 是**每次读取**而非构造期捕获：传进去的是一个转发委托，
                // 而不是 WarningLogger 当前的值。若直接传 WarningLogger，catalog 会永久固定住
                // 首次 Get 时刻的那个值 —— 一旦有代码在 Plugin.Awake 注入之前间接调到 Get，
                // logger 就被永久固定为 null，LocalizationCatalog 的三条诊断
                //（未登记语言码 / 单文件损坏 / 全部加载失败）从此永远静默，
                // 而那恰恰是最需要它们的场景。语言代码提供者本来就是每次调用时读，这里对齐它。
                _catalog = new LocalizationCatalog(typeof(Loc).Assembly, ForwardWarning);
            }
            string code = (LanguageCodeProvider != null) ? LanguageCodeProvider() : "en";
            return _catalog.Get(code, key);
        }

        /// <summary>把告警转发给当前的 <see cref="WarningLogger"/>（每次调用时读，见 Get 的注释）。</summary>
        private static void ForwardWarning(string message)
        {
            Action<string> logger = WarningLogger;
            if (logger != null)
            {
                logger(message);
            }
        }
    }
}
