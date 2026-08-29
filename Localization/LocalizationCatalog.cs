using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 本地化目录：从程序集内嵌资源加载 Localization/*.json，按语言代码查询；
    /// 缺失时回退英文，再缺失返回 key 本身。
    ///
    /// 不引用 UnityEngine / BepInEx：日志通过构造函数注入的回调输出，
    /// 使本类可被 tests\ 下的 NUnit 工程直接实例化并验证（含白名单过滤与回退链）。
    /// </summary>
    internal sealed class LocalizationCatalog
    {
        /// <summary>
        /// 已知语言代码白名单。与 Localization\ 下的文件名一一对应，也与
        /// GameLanguage.ToCode 的返回值集合一致。
        /// </summary>
        internal static readonly string[] KnownLanguageCodes = new string[]
        {
            "en", "zh-Hans", "zh-Hant", "ja", "ko", "ru", "uk", "fr", "it", "de",
            "es-ES", "es-419", "pt-BR", "pl", "tr"
        };

        private readonly Dictionary<string, Dictionary<string, string>> _languages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _warn;

        /// <summary>已加载的语言数（供诊断与测试断言）。</summary>
        internal int LoadedLanguageCount { get { return _languages.Count; } }

        public LocalizationCatalog(Assembly assembly) : this(assembly, null)
        {
        }

        public LocalizationCatalog(Assembly assembly, Action<string> warningLogger)
        {
            _warn = warningLogger;
            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                const string marker = ".Localization.";
                int markerIndex = resourceName.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0 || !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string languageCode = resourceName.Substring(
                    markerIndex + marker.Length,
                    resourceName.Length - markerIndex - marker.Length - ".json".Length);
                // 已知语言代码白名单校验：避免 RootNamespace 改名后 marker 静默失配，加载到无关资源
                if (!IsKnownLanguageCode(languageCode))
                {
                    // 记一条 Debug 级提示：将来新增语言若忘记登记白名单，会静默回退英文，排查成本很高
                    Warn("跳过未登记的本地化资源（语言代码不在白名单中）: " + resourceName);
                    continue;
                }
                try
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            continue;
                        }
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            Dictionary<string, string> values =
                                JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
                            if (values != null)
                            {
                                _languages[languageCode] = values;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 单个语言文件损坏时跳过该文件，保证其余语言仍可用，避免整个 catalog 构造抛异常
                    Warn("加载本地化资源失败 " + resourceName + ": " + ex.Message);
                }
            }
            if (_languages.Count == 0)
            {
                // 全部语言都没加载上（内嵌资源缺失/命名变更）：UI 会退化为显示原始 key（如 "catAll"），
                // 这里明确报错便于诊断，而不是让玩家看到一堆英文 key。
                Warn("未加载到任何本地化资源，界面文字将显示为原始 key");
            }
        }

        internal static bool IsKnownLanguageCode(string languageCode)
        {
            for (int i = 0; i < KnownLanguageCodes.Length; i++)
            {
                if (string.Equals(languageCode, KnownLanguageCodes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void Warn(string message)
        {
            if (_warn != null)
            {
                _warn(message);
            }
        }

        public string Get(string languageCode, string key)
        {
            Dictionary<string, string> language;
            if (languageCode != null && _languages.TryGetValue(languageCode, out language))
            {
                string value;
                if (language.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            Dictionary<string, string> english;
            if (_languages.TryGetValue("en", out english))
            {
                string fallback;
                if (english.TryGetValue(key, out fallback) && !string.IsNullOrWhiteSpace(fallback))
                {
                    return fallback;
                }
            }
            return key;
        }
    }
}
