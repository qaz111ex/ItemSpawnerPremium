using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// 搜索排名（SearchRanking.Score）与目录排序（CompareCatalog）的回归测试。
    /// 覆盖审查报告列出的缺口：分档优先级、拼音仅中文生效、ordinal 折叠、全序性质。
    /// </summary>
    [TestFixture]
    public class SearchRankingTests
    {
        /// <summary>按真实调用约定构造一次打分（displayName/enName/prefabName 需已小写）。</summary>
        private static int Score(string display, string en, string prefab,
                                 string pinyin, string initials,
                                 string query, bool chinese)
        {
            return SearchRanking.Score(
                display, en, prefab, pinyin, initials,
                query, SearchText.StripNonAlnum(query), chinese);
        }

        // ---------- 空 query ----------

        [Test]
        public void EmptyQuery_MatchesEverythingWithBaselineScore()
        {
            Assert.That(Score("rope spool", "rope spool", "ropespool", "", "", "", false),
                Is.EqualTo(SearchRanking.MatchAllScore));
            Assert.That(SearchRanking.MatchAllScore, Is.GreaterThan(0), "基准分必须为正，否则空 query 会过滤掉全部条目");
        }

        [Test]
        public void NullQuery_TreatedAsEmpty()
        {
            Assert.That(SearchRanking.Score("a", "a", "a", "", "", null, "", false),
                Is.EqualTo(SearchRanking.MatchAllScore));
        }

        // ---------- 显示名分档 ----------

        [Test]
        public void DisplayName_ExactBeatsPrefixBeatsContains()
        {
            int exact = Score("rope spool", null, null, null, null, "rope spool", false);
            int prefix = Score("rope spool", null, null, null, null, "rope", false);
            int contains = Score("rope spool", null, null, null, null, "spool", false);

            Assert.That(exact, Is.EqualTo(1000));
            Assert.That(prefix, Is.EqualTo(900));
            Assert.That(contains, Is.EqualTo(600));
            Assert.That(exact, Is.GreaterThan(prefix));
            Assert.That(prefix, Is.GreaterThan(contains));
        }

        [Test]
        public void NoMatchAnywhere_ReturnsZero()
        {
            Assert.That(Score("rope spool", "rope spool", "ropespool", "", "", "zzzz", false), Is.EqualTo(0));
        }

        // ---------- 字段优先级 ----------

        [Test]
        public void DisplayNameOutranksEnglishNameWhichOutranksPrefabName()
        {
            int byDisplay = Score("绳索枪", "rope spool", "ropespool", "shengsuoqiang", "ssq", "绳索枪", true);
            int byEnglish = Score("绳索枪", "rope spool", "ropespool", "shengsuoqiang", "ssq", "rope spool", true);
            int byPrefab = Score("绳索枪", "rope spool", "ropespool", "shengsuoqiang", "ssq", "ropespool", true);

            Assert.That(byDisplay, Is.EqualTo(1000));
            Assert.That(byEnglish, Is.EqualTo(700));
            Assert.That(byPrefab, Is.EqualTo(650));
            Assert.That(byDisplay, Is.GreaterThan(byEnglish));
            Assert.That(byEnglish, Is.GreaterThan(byPrefab));
        }

        // ---------- 拼音档 ----------

        [Test]
        public void PinyinFullSpelling_OnlyCountsWhenChineseUi()
        {
            int chinese = Score("绳索枪", null, null, "shengsuoqiang", "ssq", "sheng", true);
            int english = Score("绳索枪", null, null, "shengsuoqiang", "ssq", "sheng", false);

            Assert.That(chinese, Is.EqualTo(850));
            Assert.That(english, Is.EqualTo(0), "非中文界面下拼音档必须完全不参与");
        }

        [Test]
        public void PinyinInitials_OnlyCountsWhenChineseUi()
        {
            Assert.That(Score("热狗肠", null, null, "regouchang", "rgc", "rgc", true), Is.EqualTo(750));
            Assert.That(Score("热狗肠", null, null, "regouchang", "rgc", "rgc", false), Is.EqualTo(0));
        }

        [Test]
        public void PinyinPrefixOutranksEnglishPrefixAndDisplayContains()
        {
            // 这是刻意的设计：中文玩家打拼音时，"拼音开头就对上"的结果必须排在
            // "恰好含该字母序列的英文名"与"显示名中段命中"之前。
            int pinyinPrefix = 850;
            int englishPrefix = 500;
            int displayContains = 600;
            Assert.That(pinyinPrefix, Is.GreaterThan(englishPrefix));
            Assert.That(pinyinPrefix, Is.GreaterThan(displayContains));

            int actual = Score("绳索枪", "shenglong rope", "ropespool", "shengsuoqiang", "ssq", "sheng", true);
            Assert.That(actual, Is.EqualTo(pinyinPrefix));
        }

        [Test]
        public void PinyinFullSpellingOutranksInitialsAtSameMatchKind()
        {
            int full = Score("热狗肠", null, null, "regouchang", "rgc", "regou", true);
            int initials = Score("热狗肠", null, null, "regouchang", "rgc", "rgc", true);
            Assert.That(full, Is.EqualTo(850));
            Assert.That(initials, Is.EqualTo(750));
            Assert.That(full, Is.GreaterThan(initials));
        }

        [Test]
        public void PinyinContainsRanksBelowAllPrefixTiers()
        {
            int pinyinContains = Score("热狗肠", null, null, "regouchang", "rgc", "gouchang", true);
            Assert.That(pinyinContains, Is.EqualTo(550));
            Assert.That(pinyinContains, Is.LessThan(750));
        }

        [Test]
        public void PinyinFieldsNull_DoesNotThrowAndFallsThrough()
        {
            Assert.DoesNotThrow(delegate
            {
                Score("绳索枪", "rope", "rope", null, null, "rope", true);
            });
            Assert.That(Score("绳索枪", "rope", "rope", null, null, "rope", true), Is.EqualTo(700));
        }

        [Test]
        public void ChineseQueryYieldsEmptyQueryNoSpaceForAsciiPinyinFields()
        {
            // 中文 query 归一化后仍是中文，与 ASCII 拼音字段永不相等 —— 这没问题，
            // 因为显示名档会先命中。这里固化该行为，防止有人"优化"出错误期望。
            int s = Score("绳索枪", "rope spool", "ropespool", "shengsuoqiang", "ssq", "索", true);
            Assert.That(s, Is.EqualTo(600), "中文 query 应由显示名『包含』档命中");
        }

        // ---------- ordinal 语义（区分力来自"可忽略字符"，不是土耳其语 i/I）----------

        /// <summary>软连字符 U+00AD。culture-sensitive 比较会**跳过**它，ordinal 不会。</summary>
        private const string SoftHyphen = "\u00AD";

        /// <summary>在指定区域下执行 body，结束后无条件还原（测试必须与区域无关）。</summary>
        private static void InCulture(string name, Action body)
        {
            System.Globalization.CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo(name);
                body();
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void ComparisonsAreOrdinalNotCultureSensitive()
        {
            // 旧断言（2.3.0 之前）是同义反复，必须说明清楚，否则后来者会以为下面这套
            // 软连字符输入是无谓的复杂化：
            //
            //   旧写法把 "Idol" / "Id" 过一遍 StripNonAlnum 再在 tr-TR 下比较。但 StripNonAlnum
            //   已经用 ToLowerInvariant 把两边都变成了纯 ASCII 小写，而土耳其语 i/I 的折叠差异发生在
            //   **大小写转换**阶段、不在 StartsWith/IndexOf 阶段 —— 到达比较时 CurrentCulture 与
            //   Ordinal 在纯 ASCII 小写串上结论完全一致。实测：把 SearchRanking.cs 里全部 11 处
            //   `, StringComparison.Ordinal)` 删掉退回 CurrentCulture 重载后，全部测试仍然通过。
            //
            // 真正能区分两者的是 Unicode「可忽略字符」：culture-sensitive 比较把它们视为不存在，
            // ordinal 按码点逐一比。软连字符 U+00AD 是最典型的一个，且它能通过 StripNonAlnum
            // （char.IsLetterOrDigit('\u00AD') == false，会被剔除）—— 所以这里直接构造已归一化的
            // 字段值，模拟"显示名里混入了不可见格式字符"的真实情形（游戏本地化表用过软连字符做断词提示）。
            //
            // 期望：ordinal 下 "i\u00ADdol" 既不等于 "idol"、不以它开头、也不包含它，前 4 档全不命中 → 0 分。
            // 若退回 CurrentCulture，StartsWith 会返回 true → 900 分，测试失败。
            // 注意软连字符要放在**词内部**而不是开头：放开头时 ordinal 的 IndexOf 仍能在偏移 1
            // 处找到 needle，"包含"档会命中 600，断言就失去了区分力。
            InCulture("tr-TR", delegate
            {
                int s = SearchRanking.Score(
                    "i" + SoftHyphen + "dol", null, null, null, null, "idol", "idol", false);
                Assert.That(s, Is.EqualTo(0),
                    "Ordinal 语义下软连字符必须参与比较；得到 " + s + " 说明比较退回了 culture-sensitive 重载");
            });
        }

        [Test]
        public void OrdinalSemanticsApplyToEveryScoredField()
        {
            // 逐档验证：11 处比较分布在 显示名 / 拼音全拼 / 拼音首字母 / 英文名 / prefab 名 五组。
            // 只测显示名那一档的话，其余 8 处退回 CurrentCulture 不会被发现。
            // 每个用例的软连字符都插在被查字段的**词内部**，使 ordinal 的 StartsWith 与 IndexOf
            // 双双落空（返回 0 分），而 culture-sensitive 比较会跳过它并命中相应档位。
            InCulture("tr-TR", delegate
            {
                // 显示名：前缀档（culture 下 900）
                Assert.That(SearchRanking.Score("ro" + SoftHyphen + "pespool", null, null, null, null,
                    "rope", "rope", false), Is.EqualTo(0), "显示名前缀档未用 Ordinal");
                // 显示名：包含档（culture 下 600）
                Assert.That(SearchRanking.Score("golden i" + SoftHyphen + "dol", null, null, null, null,
                    "idol", "idol", false), Is.EqualTo(0), "显示名包含档未用 Ordinal");
                // 拼音全拼：前缀 850 / 包含 550（displayName 传 null 以确保落到拼音档）
                Assert.That(SearchRanking.Score(null, null, null, "she" + SoftHyphen + "ngsuoqiang", null,
                    "sheng", "sheng", true), Is.EqualTo(0), "拼音全拼前缀档未用 Ordinal");
                Assert.That(SearchRanking.Score(null, null, null, "shengs" + SoftHyphen + "uoqiang", null,
                    "suo", "suo", true), Is.EqualTo(0), "拼音全拼包含档未用 Ordinal");
                // 拼音首字母：前缀 750 / 包含 500
                Assert.That(SearchRanking.Score(null, null, null, null, "r" + SoftHyphen + "gc",
                    "rg", "rg", true), Is.EqualTo(0), "拼音首字母前缀档未用 Ordinal");
                Assert.That(SearchRanking.Score(null, null, null, null, "rg" + SoftHyphen + "c",
                    "gc", "gc", true), Is.EqualTo(0), "拼音首字母包含档未用 Ordinal");
                // 英文名：前缀 500 / 包含 300
                Assert.That(SearchRanking.Score(null, "ro" + SoftHyphen + "pe spool", null, null, null,
                    "rope", "rope", false), Is.EqualTo(0), "英文名前缀档未用 Ordinal");
                Assert.That(SearchRanking.Score(null, "rope sp" + SoftHyphen + "ool", null, null, null,
                    "spool", "spool", false), Is.EqualTo(0), "英文名包含档未用 Ordinal");
                // prefab 名：前缀 450 / 包含 250
                Assert.That(SearchRanking.Score(null, null, "ro" + SoftHyphen + "pespool", null, null,
                    "rope", "rope", false), Is.EqualTo(0), "prefab 前缀档未用 Ordinal");
                Assert.That(SearchRanking.Score(null, null, "ropesp" + SoftHyphen + "ool", null, null,
                    "spool", "spool", false), Is.EqualTo(0), "prefab 包含档未用 Ordinal");
            });
        }

        [Test]
        public void ScoreIsStableAcrossCultures()
        {
            // 旧断言用的是 ("golden idol", query "idol")：纯 ASCII 小写串，任何区域下都是 600 分，
            // 于是它只在测「打分函数不读区域」这个平凡事实 —— 即使把全部比较退回 CurrentCulture
            // 重载它也照过（实测：删掉 SearchRanking.cs 的 11 处 Ordinal 说明符后全部测试仍通过）。
            //
            // 换成 ("chalk", query "c") 才有区分力：捷克语/斯洛伐克语把 "ch" 当作**一个字母**
            // 排在 h 之后，因此 culture-sensitive 比较下
            //   "chalk".StartsWith("c") == false 且 "chalk".IndexOf("c") == -1（实测 cs-CZ / sk-SK），
            // 而 en-US 等区域是 true/0。Ordinal 则恒为 true/0，与区域无关。
            // 一旦比较退回 CurrentCulture，本循环会在 cs-CZ 上得到 0 分而与 en-US 的 900 不一致。
            string[] cultures = new string[] { "en-US", "cs-CZ", "sk-SK", "tr-TR", "ru-RU", "zh-CN", "de-DE", "hu-HU" };
            List<int> results = new List<int>();
            for (int i = 0; i < cultures.Length; i++)
            {
                InCulture(cultures[i], delegate
                {
                    results.Add(Score("chalk", "chalk", "climbingchalk", "", "", "c", false));
                });
            }
            for (int i = 0; i < results.Count; i++)
            {
                Assert.That(results[i], Is.EqualTo(results[0]),
                    "区域 " + cultures[i] + " 下打分与 en-US 不一致，说明比较受 CurrentCulture 影响");
            }
            // 同时锁定具体数值，避免"一致地全错"（例如全部退化成 0）也算通过。
            Assert.That(results[0], Is.EqualTo(900),
                "Ordinal 下 'chalk' 对 query 'c' 应命中显示名前缀档(900)");
        }

        [Test]
        public void CompareCatalogUsesOrdinalForPrefabTiebreaker()
        {
            // CompareCatalog 的第三级 tiebreaker 也显式指定了 Ordinal。它的作用是让比较器成为
            // 全序，而 culture-sensitive 比较会把只差一个可忽略字符的两个 prefab 名判为**相等**
            // （实测 tr-TR 下 string.Compare("a", "\u00ADa") == 0），tiebreaker 随即失效 ——
            // 表现回归为"切换语言时同名条目网格位置互换"。
            InCulture("tr-TR", delegate
            {
                int r = SearchRanking.CompareCatalog(
                    ItemCategory.Tools, "rescue claw", "RescueHook",
                    ItemCategory.Tools, "rescue claw", SoftHyphen + "RescueHook");
                Assert.That(r, Is.Not.EqualTo(0),
                    "prefab tiebreaker 未用 Ordinal：仅差一个可忽略字符的两项被判为相等，比较器不再是全序");
            });
        }

        // ---------- CompareCatalog 全序性质 ----------

        private static int Cmp(ItemCategory ap, string ad, string apf, ItemCategory bp, string bd, string bpf)
        {
            return SearchRanking.CompareCatalog(ap, ad, apf, bp, bd, bpf);
        }

        [Test]
        public void CompareCatalog_PrimaryCategoryDominates()
        {
            Assert.That(Cmp(ItemCategory.Tools, "zzz", "zzz", ItemCategory.Food, "aaa", "aaa"),
                Is.LessThan(0), "Tools(1) 应排在 Food(2) 之前，与显示名无关");
        }

        [Test]
        public void CompareCatalog_FallsBackToDisplayNameIgnoringCase()
        {
            Assert.That(Cmp(ItemCategory.Food, "apple", "x", ItemCategory.Food, "Banana", "y"),
                Is.LessThan(0));
            Assert.That(Cmp(ItemCategory.Food, "APPLE", "x", ItemCategory.Food, "apple", "x"),
                Is.EqualTo(0), "同名同 prefab 应完全相等");
        }

        [Test]
        public void CompareCatalog_UsesPrefabNameAsFinalTiebreaker()
        {
            // 11 组同显示名条目之一：救援抓钩。没有这个 tiebreaker，List.Sort 会让它们位置抖动。
            int r = Cmp(ItemCategory.Tools, "rescue claw", "RescueHook",
                        ItemCategory.Tools, "rescue claw", "RescueHook_Infinite");
            Assert.That(r, Is.Not.EqualTo(0), "同显示名必须由 prefab 名分出先后");
            Assert.That(r, Is.LessThan(0));
        }

        [Test]
        public void CompareCatalog_IsAntisymmetric()
        {
            Assert.That(Cmp(ItemCategory.Tools, "a", "p1", ItemCategory.Food, "b", "p2"),
                Is.LessThan(0));
            Assert.That(Cmp(ItemCategory.Food, "b", "p2", ItemCategory.Tools, "a", "p1"),
                Is.GreaterThan(0));
        }

        [Test]
        public void CompareCatalog_IsTransitiveOnRealDuplicateNameGroups()
        {
            // 传送罗盘三兄弟同显示名，靠 prefab 名排序
            string[] prefabs = new string[] { "Cheat Compass", "Cheat Compass 1", "Warp Compass" };
            for (int i = 0; i < prefabs.Length; i++)
            {
                for (int j = 0; j < prefabs.Length; j++)
                {
                    for (int k = 0; k < prefabs.Length; k++)
                    {
                        int ij = Cmp(ItemCategory.Mystical, "warp compass", prefabs[i],
                                     ItemCategory.Mystical, "warp compass", prefabs[j]);
                        int jk = Cmp(ItemCategory.Mystical, "warp compass", prefabs[j],
                                     ItemCategory.Mystical, "warp compass", prefabs[k]);
                        int ik = Cmp(ItemCategory.Mystical, "warp compass", prefabs[i],
                                     ItemCategory.Mystical, "warp compass", prefabs[k]);
                        if (ij < 0 && jk < 0)
                        {
                            Assert.That(ik, Is.LessThan(0), "传递性被破坏");
                        }
                    }
                }
            }
        }

        [Test]
        public void SortingIsDeterministicRegardlessOfInitialOrder()
        {
            // 用 11 组重名里的真实数据构造，两种不同初始顺序排序后必须完全一致。
            // 这直接复现了 tiebreaker 修复前「语言切换后网格位置互换」的 bug 场景。
            List<string[]> items = new List<string[]>
            {
                new string[] { "rescue claw", "RescueHook" },
                new string[] { "rescue claw", "RescueHook_Infinite" },
                new string[] { "egg", "Egg" },
                new string[] { "egg", "EggRaven" },
                new string[] { "scout cookies", "ScoutCookies" },
                new string[] { "scout cookies", "ScoutCookies_Vanilla" },
                new string[] { "parasol", "Parasol" },
                new string[] { "parasol", "Parasol_Roots Variant" },
            };

            Comparison<string[]> cmp = delegate (string[] a, string[] b)
            {
                return SearchRanking.CompareCatalog(
                    ItemCategory.Tools, a[0], a[1],
                    ItemCategory.Tools, b[0], b[1]);
            };

            List<string[]> forward = new List<string[]>(items);
            List<string[]> reversed = new List<string[]>(items);
            reversed.Reverse();

            forward.Sort(cmp);
            reversed.Sort(cmp);

            Assert.That(forward.Count, Is.EqualTo(reversed.Count));
            for (int i = 0; i < forward.Count; i++)
            {
                Assert.That(reversed[i][1], Is.EqualTo(forward[i][1]),
                    "第 " + i + " 位排序结果依赖初始顺序，比较器不是全序");
            }
        }

        [Test]
        public void CompareCatalog_HandlesNullNamesWithoutThrowing()
        {
            // string.Compare 对 null 有定义（null 最小），不应抛异常
            Assert.DoesNotThrow(delegate
            {
                Cmp(ItemCategory.Props, null, null, ItemCategory.Props, "a", "b");
            });
            Assert.That(Cmp(ItemCategory.Props, null, null, ItemCategory.Props, "a", "b"),
                Is.LessThan(0));
        }
    }
}
