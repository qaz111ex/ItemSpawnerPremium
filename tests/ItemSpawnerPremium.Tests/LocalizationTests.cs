using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ItemSpawnerEnhancement;
using Newtonsoft.Json;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// 本地化资源的回归测试。
    ///
    /// 测试工程以与发行 DLL 相同的资源名格式（*.Localization.&lt;code&gt;.json）内嵌全部 15 个
    /// 语言文件，因此这里验证的是**真实的** marker 解析、白名单过滤与英文回退链，
    /// 而不是一个手写的假 catalog。
    /// 同时对 Localization\ 下的源文件做文件级校验（键集一致、无 BOM、可解析）。
    /// </summary>
    [TestFixture]
    public class LocalizationTests
    {
        /// <summary>代码中实际用到的全部本地化 key（UiEnhancer + ItemCatalogMethods）。</summary>
        private static readonly string[] RequiredKeys = new string[]
        {
            "catAll", "catTools", "catFood", "catMystical", "catEquipment",
            "catConsumables", "catProps", "catFavorite", "searchPlaceholder", "rightClickHint"
        };

        private static LocalizationCatalog _catalog;

        [OneTimeSetUp]
        public void LoadCatalog()
        {
            _catalog = new LocalizationCatalog(Assembly.GetExecutingAssembly());
        }

        // ---------- 内嵌资源加载 ----------

        [Test]
        public void AllFifteenLanguagesAreLoadedFromEmbeddedResources()
        {
            Assert.That(_catalog.LoadedLanguageCount, Is.EqualTo(15),
                "内嵌本地化资源未全部加载 —— 检查 EmbeddedResource 的 LogicalName 是否含 '.Localization.' marker");
        }

        [Test]
        public void EveryKnownLanguageCodeResolvesEveryRequiredKey()
        {
            for (int i = 0; i < LocalizationCatalog.KnownLanguageCodes.Length; i++)
            {
                string code = LocalizationCatalog.KnownLanguageCodes[i];
                for (int k = 0; k < RequiredKeys.Length; k++)
                {
                    string key = RequiredKeys[k];
                    string value = _catalog.Get(code, key);
                    Assert.That(value, Is.Not.Null.And.Not.Empty, code + " / " + key);
                    Assert.That(value, Is.Not.EqualTo(key),
                        code + " 缺少 key '" + key + "'（返回了 key 本身，说明连英文回退都没命中）");
                }
            }
        }

        [Test]
        public void UnknownLanguageCodeFallsBackToEnglish()
        {
            string english = _catalog.Get("en", "catAll");
            Assert.That(_catalog.Get("xx-YY", "catAll"), Is.EqualTo(english));
            Assert.That(_catalog.Get(null, "catAll"), Is.EqualTo(english));
        }

        [Test]
        public void UnknownKeyReturnsTheKeyItself()
        {
            // 这是刻意的降级：显示 "catNonexistent" 比抛异常或显示空白更容易被发现
            Assert.That(_catalog.Get("en", "catNonexistent"), Is.EqualTo("catNonexistent"));
        }

        [Test]
        public void LanguageCodeLookupIsCaseInsensitive()
        {
            string expected = _catalog.Get("zh-Hans", "catProps");
            Assert.That(_catalog.Get("zh-hans", "catProps"), Is.EqualTo(expected));
            Assert.That(_catalog.Get("ZH-HANS", "catProps"), Is.EqualTo(expected));
        }

        [Test]
        public void UnregisteredLanguageCodeIsRejectedByWhitelist()
        {
            // 白名单是防「RootNamespace 改名后 marker 失配、加载到无关资源」的闸门
            Assert.That(LocalizationCatalog.IsKnownLanguageCode("en"), Is.True);
            Assert.That(LocalizationCatalog.IsKnownLanguageCode("zh-Hans"), Is.True);
            Assert.That(LocalizationCatalog.IsKnownLanguageCode("klingon"), Is.False);
            Assert.That(LocalizationCatalog.IsKnownLanguageCode(""), Is.False);
        }

        [Test]
        public void EmptyAssemblyProducesCatalogThatDegradesToKeys()
        {
            // 内嵌资源整体缺失时不能抛异常，只能退化为显示 key
            LocalizationCatalog empty = null;
            Assert.DoesNotThrow(delegate
            {
                empty = new LocalizationCatalog(typeof(object).Assembly);
            });
            Assert.That(empty.LoadedLanguageCount, Is.EqualTo(0));
            Assert.That(empty.Get("en", "catAll"), Is.EqualTo("catAll"));
        }

        [Test]
        public void WarningLoggerIsInvokedWhenNothingLoads()
        {
            List<string> warnings = new List<string>();
            LocalizationCatalog empty = new LocalizationCatalog(
                typeof(object).Assembly,
                delegate (string m) { warnings.Add(m); });

            Assert.That(empty.LoadedLanguageCount, Is.EqualTo(0));
            Assert.That(warnings.Count, Is.GreaterThan(0), "全部加载失败时必须告警，否则无从诊断");
        }

        // ---------- 语言代码集合一致性 ----------

        [Test]
        public void KnownLanguageCodesHasFifteenUniqueEntries()
        {
            Assert.That(LocalizationCatalog.KnownLanguageCodes.Length, Is.EqualTo(15));
            HashSet<string> unique = new HashSet<string>(
                LocalizationCatalog.KnownLanguageCodes, StringComparer.OrdinalIgnoreCase);
            Assert.That(unique.Count, Is.EqualTo(15), "白名单有重复项");
        }

        [Test]
        public void EveryWhitelistedCodeHasACorrespondingJsonFile()
        {
            string dir = LocalizationDirectory();
            for (int i = 0; i < LocalizationCatalog.KnownLanguageCodes.Length; i++)
            {
                string path = Path.Combine(dir, LocalizationCatalog.KnownLanguageCodes[i] + ".json");
                Assert.That(File.Exists(path), Is.True, "白名单登记了但没有对应文件: " + path);
            }
        }

        [Test]
        public void EveryJsonFileHasACorrespondingWhitelistEntry()
        {
            // 反向检查：新增语言文件却忘记登记白名单会被静默丢弃
            string[] files = Directory.GetFiles(LocalizationDirectory(), "*.json");
            Assert.That(files.Length, Is.EqualTo(15));
            for (int i = 0; i < files.Length; i++)
            {
                string code = Path.GetFileNameWithoutExtension(files[i]);
                Assert.That(LocalizationCatalog.IsKnownLanguageCode(code), Is.True,
                    code + ".json 存在但未登记到 KnownLanguageCodes，会被白名单静默过滤");
            }
        }

        // ---------- 源文件级校验 ----------

        [Test]
        public void EveryJsonFileIsUtf8WithoutBom()
        {
            // 带 BOM 时 StreamReader 能处理，但 key 会被 BOM 污染的风险不值得承担；
            // 统一无 BOM 也让 git diff 干净
            string[] files = Directory.GetFiles(LocalizationDirectory(), "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                byte[] bytes = File.ReadAllBytes(files[i]);
                bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                Assert.That(hasBom, Is.False, Path.GetFileName(files[i]) + " 带 UTF-8 BOM");
            }
        }

        [Test]
        public void EveryJsonFileParsesAndHasExactlyTheRequiredKeys()
        {
            string[] files = Directory.GetFiles(LocalizationDirectory(), "*.json");
            HashSet<string> required = new HashSet<string>(RequiredKeys, StringComparer.Ordinal);

            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                Dictionary<string, string> map = null;
                Assert.DoesNotThrow(delegate
                {
                    map = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                        File.ReadAllText(files[i]));
                }, name + " 不是合法 JSON");

                Assert.That(map, Is.Not.Null, name);
                Assert.That(map.Count, Is.EqualTo(RequiredKeys.Length), name + " key 数量不符");

                foreach (string key in required)
                {
                    Assert.That(map.ContainsKey(key), Is.True, name + " 缺少 key: " + key);
                    Assert.That(map[key], Is.Not.Null.And.Not.Empty, name + " 的 " + key + " 为空");
                    Assert.That(map[key].Trim(), Is.EqualTo(map[key]), name + " 的 " + key + " 含首尾空白");
                }
                foreach (KeyValuePair<string, string> kv in map)
                {
                    Assert.That(required.Contains(kv.Key), Is.True, name + " 含多余 key: " + kv.Key);
                }
            }
        }

        [Test]
        public void EnglishIsTheFallbackSoItMustBeComplete()
        {
            // Get() 的回退链最后一站是英文；英文缺 key 会导致所有语言都显示原始 key
            string path = Path.Combine(LocalizationDirectory(), "en.json");
            Dictionary<string, string> en = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                File.ReadAllText(path));
            for (int i = 0; i < RequiredKeys.Length; i++)
            {
                Assert.That(en.ContainsKey(RequiredKeys[i]), Is.True, "en.json 缺少 " + RequiredKeys[i]);
            }
        }

        // ---------- 分类名跨语言一致性（用户拍板的结论）----------

        [Test]
        public void CatPropsUsesMiscSemanticsInEveryLanguage()
        {
            // 用户拍板：分类「杂项」在 en/ja/ko 下统一为 Misc / その他 / 기타，
            // 与其余 12 种语言的「其他」语义一致（此前 en=Props、ja=小道具、ko=소품 是语义分裂）
            Assert.That(_catalog.Get("en", "catProps"), Is.EqualTo("Misc"));
            Assert.That(_catalog.Get("ja", "catProps"), Is.EqualTo("その他"));
            Assert.That(_catalog.Get("ko", "catProps"), Is.EqualTo("기타"));
            Assert.That(_catalog.Get("zh-Hans", "catProps"), Is.EqualTo("杂项"));
            Assert.That(_catalog.Get("zh-Hant", "catProps"), Is.EqualTo("雜項"));
        }

        [Test]
        public void ChineseCategoryLabelsStayShortAsRequested()
        {
            // 用户要求中文类别名保持短标签（基本都是两个字）。唯一例外是「消耗品」——
            // 三个字，是既有且已发布的文案，缩成两字会丢失语义，因此这里允许 2~3 字并
            // 显式列出例外，防止有人无意间把其他标签也加长。
            Dictionary<string, int> expectedLength = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "catAll", 2 },
                { "catTools", 2 },
                { "catFood", 2 },
                { "catMystical", 2 },
                { "catEquipment", 2 },
                { "catConsumables", 3 },   // 唯一例外：消耗品 / 消耗品
                { "catProps", 2 },
                { "catFavorite", 2 },
            };
            string[] codes = new string[] { "zh-Hans", "zh-Hant" };
            for (int c = 0; c < codes.Length; c++)
            {
                foreach (KeyValuePair<string, int> kv in expectedLength)
                {
                    string label = _catalog.Get(codes[c], kv.Key);
                    Assert.That(label.Length, Is.EqualTo(kv.Value),
                        codes[c] + " 的 " + kv.Key + " = '" + label + "'，长度应为 " + kv.Value);
                }
            }
        }

        [Test]
        public void MysticalLabelIsNotRenamedBackToLongForm()
        {
            // 历史上曾误改为「神秘物品」/「Mystical Item」后回退，这里锁定结论
            Assert.That(_catalog.Get("zh-Hans", "catMystical"), Is.EqualTo("神秘"));
            Assert.That(_catalog.Get("en", "catMystical"), Is.EqualTo("Mystical"));
        }

        // ---------- 文案长度（配合分类按钮 autoSizing 的宽度约束）----------

        [Test]
        public void CategoryLabelsAreShortEnoughToBeUsableInButtons
            ()
        {
            // 分类条要塞 8 个按钮均分面板宽度；过长的标签即使有 Ellipsis 也会不可读。
            // 20 字符是经验上限（德语 Verbrauchsobjekte 是 17）。超限说明需要缩写而不是靠 UI 补救。
            string[] keys = new string[]
            {
                "catAll", "catTools", "catFood", "catMystical",
                "catEquipment", "catConsumables", "catProps", "catFavorite"
            };
            for (int i = 0; i < LocalizationCatalog.KnownLanguageCodes.Length; i++)
            {
                string code = LocalizationCatalog.KnownLanguageCodes[i];
                for (int k = 0; k < keys.Length; k++)
                {
                    string label = _catalog.Get(code, keys[k]);
                    Assert.That(label.Length, Is.LessThanOrEqualTo(20),
                        code + " 的 " + keys[k] + " 过长(" + label.Length + "): " + label);
                }
            }
        }

        /// <summary>定位仓库里的 Localization 目录（测试工程把源 JSON 拷到输出目录的 LocalizationFiles 下）。</summary>
        private static string LocalizationDirectory()
        {
            string dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "LocalizationFiles");
            Assert.That(Directory.Exists(dir), Is.True,
                "找不到 LocalizationFiles 输出目录，检查 csproj 的 None/CopyToOutputDirectory 配置");
            return dir;
        }
    }
}
