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
    /// </summary>
    internal sealed class LocalizationCatalog
    {
        private readonly Dictionary<string, Dictionary<string, string>> _languages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public LocalizationCatalog(Assembly assembly)
        {
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
        }

        public string Get(string languageCode, string key)
        {
            Dictionary<string, string> language;
            if (_languages.TryGetValue(languageCode, out language))
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
                if (english.TryGetValue(key, out fallback))
                {
                    return fallback;
                }
            }
            return key;
        }
    }
}
