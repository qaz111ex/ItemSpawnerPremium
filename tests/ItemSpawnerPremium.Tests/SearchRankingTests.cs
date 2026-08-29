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

        // ---------- ordinal 折叠（土耳其语 i/I 问题）----------

        [Test]
        public void ComparisonsAreOrdinalNotCultureSensitive()
        {
            // 归一化用 ToLowerInvariant（I→i），比较必须同为 ordinal，否则在 tr-TR 区域下会漏匹配。
            // 这里直接把线程区域切到 tr-TR 验证结果不随区域漂移。
            System.Globalization.CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("tr-TR");

                string display = SearchText.StripNonAlnum("Idol");   // → "idol"
                string query = SearchText.StripNonAlnum("Id");       // → "id"
                int s = SearchRanking.Score(display, null, null, null, null, query, query, false);
                Assert.That(s, Is.EqualTo(900), "tr-TR 区域下前缀匹配失效，说明比较未用 Ordinal");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void ScoreIsStableAcrossCultures()
        {
            string[] cultures = new string[] { "en-US", "tr-TR", "ru-RU", "zh-CN", "de-DE" };
            List<int> results = new List<int>();
            System.Globalization.CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                for (int i = 0; i < cultures.Length; i++)
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture =
                        new System.Globalization.CultureInfo(cultures[i]);
                    results.Add(Score("golden idol", "golden idol", "goldenidol", "", "", "idol", false));
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
            for (int i = 1; i < results.Count; i++)
            {
                Assert.That(results[i], Is.EqualTo(results[0]),
                    "区域 " + cultures[i] + " 下打分与 en-US 不一致");
            }
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
