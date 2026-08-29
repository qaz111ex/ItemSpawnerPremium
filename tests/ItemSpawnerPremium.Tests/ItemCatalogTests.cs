using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// 物品分类与隐藏规则的回归测试（ItemCatalog / ItemCatalogMethods，纯静态数据 + 纯函数）。
    /// 这里守护的核心不变量：隐藏规则与静态分类表必须精确互补 ——
    /// 「未被隐藏的物品」恰好等于「静态表里有的物品」，缺失 0、多余 0。
    /// 该不变量一旦被破坏（例如有人重跑生成脚本覆盖了人工修订），测试立即失败。
    /// </summary>
    [TestFixture]
    public class ItemCatalogTests
    {
        // ---------- IsHidden ----------

        [Test]
        public void IsHidden_NullOrEmpty_IsHidden()
        {
            // 空 prefab 名无法生成，视为隐藏比放进列表更安全
            Assert.That(ItemCatalog.IsHidden(null), Is.True);
            Assert.That(ItemCatalog.IsHidden(""), Is.True);
        }

        [Test]
        public void IsHidden_ExactMatches()
        {
            Assert.That(ItemCatalog.IsHidden("BaseObject"), Is.True);
            Assert.That(ItemCatalog.IsHidden("foodTest"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Portable Speaker"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Propeller"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Skull"), Is.True);
        }

        [Test]
        public void IsHidden_ExactMatchIsCaseInsensitive()
        {
            // HiddenExact 用 OrdinalIgnoreCase，与 ItemTagMap 口径一致
            Assert.That(ItemCatalog.IsHidden("baseobject"), Is.True);
            Assert.That(ItemCatalog.IsHidden("FOODTEST"), Is.True);
        }

        [Test]
        public void IsHidden_ChessPiecePrefixes()
        {
            string[] pieces = new string[] { "C_Bishop", "C_King", "C_Knight", "C_Pawn", "C_Queen", "C_Rook" };
            for (int i = 0; i < pieces.Length; i++)
            {
                Assert.That(ItemCatalog.IsHidden(pieces[i]), Is.True, pieces[i]);
                Assert.That(ItemCatalog.IsHidden(pieces[i] + " Variant"), Is.True, pieces[i] + " Variant");
                Assert.That(ItemCatalog.IsHidden(pieces[i].ToLowerInvariant()), Is.True, "大小写不敏感");
            }
        }

        [Test]
        public void IsHidden_GuidebookPagePrefixAlsoHidesScrollVariant()
        {
            // 这是**有意为之的产品决策**（用户拍板）：GuidebookPageScroll Variant 与书页一起隐藏。
            // 该测试固化决策，避免后续有人把它当 bug"修复"。
            Assert.That(ItemCatalog.IsHidden("GuidebookPage_1_Mushrooms"), Is.True);
            Assert.That(ItemCatalog.IsHidden("GuidebookPageScroll Variant"), Is.True,
                "按产品决策，Scroll 变体与书页一起隐藏");
        }

        [Test]
        public void IsHidden_Substrings()
        {
            // 这些名字在 2.2.0 时同时出现在 HiddenExact 与子串规则里（冗余）；2.3.0 删掉了
            // HiddenExact 那一份，改由 "_Prop" / "_UNUSED" / "_Hidden" 三条子串规则独占命中。
            // 这里刻意只断言 IsHidden 的**行为**、不断言它由哪张表命中 ——
            // 关心的是"这个物品不出现在目录里"，命中路径是实现细节。
            Assert.That(ItemCatalog.IsHidden("Binoculars_Prop"), Is.True);
            Assert.That(ItemCatalog.IsHidden("BingBong_Prop Variant"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Bugle_Prop Variant"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Lollipop_Prop"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Something_TEMP"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Clusterberry_UNUSED"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Mandrake_Hidden"), Is.True);
            // 子串匹配不限位置
            Assert.That(ItemCatalog.IsHidden("A_Prop_B"), Is.True);
        }

        [Test]
        public void HiddenExact_ContainsNoEntryAlreadyCoveredByPrefixOrSubstringRules()
        {
            // 2.3.0 从 HiddenExact 删掉 6 条冗余项的理由，固化成不变量。
            //
            // 冗余不只是啰嗦：当 "Binoculars_Prop" 同时躺在 HiddenExact 里时，"_Prop" 这条子串
            // 规则在当前数据下没有任何独占命中，于是整条删掉也不会改变 62/151 切分 ——
            // verify_catalog.py 和单元测试全部照过，规则实际失效而无人可知，直到游戏新增一个
            // Xxx_Prop 装饰物出现在目录里。
            //
            // 这条断言让"往 HiddenExact 里加一条已被子串/前缀覆盖的项"立刻失败。
            List<string> redundant = new List<string>();
            foreach (string exact in ItemCatalog.HiddenExact)
            {
                if (MatchedByPrefixOrSubstring(exact))
                {
                    redundant.Add(exact);
                }
            }
            Assert.That(redundant, Is.Empty,
                "HiddenExact 含已被前缀/子串规则覆盖的冗余项（会让那条规则失去独占命中而静默失效）: "
                + string.Join(", ", redundant.ToArray()));
        }

        private static bool MatchedByPrefixOrSubstring(string prefab)
        {
            for (int i = 0; i < ItemCatalog.HiddenPrefixes.Length; i++)
            {
                if (prefab.StartsWith(ItemCatalog.HiddenPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            for (int j = 0; j < ItemCatalog.HiddenSubstrings.Length; j++)
            {
                if (prefab.IndexOf(ItemCatalog.HiddenSubstrings[j], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void IsHidden_NormalItemsAreVisible()
        {
            string[] visible = new string[]
            {
                "RopeSpool", "Torch", "Lantern", "Candle", "Binoculars", "Compass",
                "Bugle", "Matchbook", "Parachute", "Rocketpack", "Basketball", "Stone",
                "Snowball", "Warpsketball", "Frisbee", "ScoutCookies", "Egg", "EggRaven"
            };
            for (int i = 0; i < visible.Length; i++)
            {
                Assert.That(ItemCatalog.IsHidden(visible[i]), Is.False, visible[i] + " 不应被隐藏");
            }
        }

        [Test]
        public void IsHidden_DoesNotOverreachOnSimilarNames()
        {
            // "Propeller" 在 HiddenExact 里；但 "Prop" 作为子串规则是 "_Prop"（带下划线），
            // 因此普通含 prop 的名字不应被误伤
            Assert.That(ItemCatalog.IsHidden("Proper Item"), Is.False);
            Assert.That(ItemCatalog.IsHidden("Propane"), Is.False);
        }

        // ---------- 核心不变量：隐藏规则与静态表互补 ----------

        [Test]
        public void EveryItemTagMapKeyIsVisible()
        {
            // 静态表里不应存在会被隐藏的 key（否则是死数据，永远查不到）
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, ItemCategory> kv in ItemCatalog.ItemTagMap)
            {
                if (ItemCatalog.IsHidden(kv.Key))
                {
                    dead.Add(kv.Key);
                }
            }
            Assert.That(dead, Is.Empty, "静态分类表含被隐藏的死键: " + string.Join(", ", dead.ToArray()));
        }

        [Test]
        public void ItemTagMapHasExpectedEntryCount()
        {
            // 151 = 真值 213 个物品减去隐藏的 62 个。数量变化必须是有意识的改动。
            Assert.That(ItemCatalog.ItemTagMap.Count, Is.EqualTo(151),
                "静态分类表条目数变化。若确实新增/删除了物品，请同步更新本断言与 verify_catalog.py");
        }

        [Test]
        public void NoItemTagMapEntryHasNoneCategory()
        {
            foreach (KeyValuePair<string, ItemCategory> kv in ItemCatalog.ItemTagMap)
            {
                Assert.That(kv.Value, Is.Not.EqualTo(ItemCategory.None), kv.Key + " 的分类为 None");
            }
        }

        [Test]
        public void NoItemTagMapEntryIsBothFoodAndConsumable()
        {
            // 「食物」与「消耗品」在玩家视角是互斥的：能吃的东西一律只归食物，
            // 即使它有使用次数（童军饼干 4 次）。在消耗品分类里翻到饼干只会让人以为分类坏了。
            // 这条断言防止后来者按「totalUses > 0 → Consumables」的机制口径重算本表时
            // 把食物又叠回消耗品 —— 那正是 2.2.0 及之前童军饼干混进消耗品的原因。
            List<string> both = new List<string>();
            foreach (KeyValuePair<string, ItemCategory> kv in ItemCatalog.ItemTagMap)
            {
                if ((kv.Value & ItemCategory.Food) != 0 && (kv.Value & ItemCategory.Consumables) != 0)
                {
                    both.Add(kv.Key);
                }
            }
            Assert.That(both, Is.Empty,
                "以下物品同时被归入食物与消耗品（两者互斥，食物优先）: " + string.Join(", ", both.ToArray()));
        }

        [Test]
        public void ItemsWithoutAnyRealUseAreHidden()
        {
            // 「能生成但生成了没意义」的物件不进目录，与棋子/撕下的书页同类。
            // 攀岩粉：只有 Action_ApplyAffliction 但没有可用效果，游戏本地化表里连名字都没有；
            // 童军队长之魂：只有 Breakable，砸开什么也不给。
            Assert.That(ItemCatalog.IsHidden("ClimbingChalk"), Is.True);
            Assert.That(ItemCatalog.IsHidden("ScoutmasterSoul"), Is.True);
            // 既然隐藏了，就不该再出现在分类表里（否则是永远用不到的死键，
            // verify_catalog.py 的 extra 断言也会失败）
            Assert.That(ItemCatalog.ItemTagMap.ContainsKey("ClimbingChalk"), Is.False);
            Assert.That(ItemCatalog.ItemTagMap.ContainsKey("ScoutmasterSoul"), Is.False);
        }

        [Test]
        public void ItemTagMapUsesCaseInsensitiveLookup()
        {
            // prefab 名在全插件按忽略大小写查表；若这里退回 Ordinal，收藏/分类会静默失配
            Assert.That(ItemCatalog.ItemTagMap.ContainsKey("ropespool"), Is.True);
            Assert.That(ItemCatalog.ItemTagMap.ContainsKey("ROPESPOOL"), Is.True);
        }

        // ---------- 用户拍板的分类例外（回归锁定）----------

        [Test]
        public void UserDecidedCategoryExceptions_AreLockedIn()
        {
            // 以下每一条都是用户明确拍板的结论，不是可自由调整的实现细节。
            AssertTags("BounceShroom", ItemCategory.Tools);
            AssertTags("ShelfShroom", ItemCategory.Tools);
            AssertTags("CloudFungus", ItemCategory.Tools);
            // WarpFungus 是平台菌类里唯一带魔法机制的，因此额外挂 Mystical
            AssertTags("WarpFungus", ItemCategory.Mystical | ItemCategory.Tools);

            AssertTags("Beehive", ItemCategory.Props);
            AssertTags("Snowball", ItemCategory.Props);
            AssertTags("Warpsketball", ItemCategory.Props);

            AssertTags("Matchbook", ItemCategory.Equipment);
            AssertTags("Megaphone", ItemCategory.Equipment);

            AssertTags("Torch", ItemCategory.Tools | ItemCategory.Consumables);
            AssertTags("Lantern", ItemCategory.Tools | ItemCategory.Consumables);
            AssertTags("Candle", ItemCategory.Tools | ItemCategory.Consumables);

            AssertTags("HealingPuffShroom", ItemCategory.Consumables | ItemCategory.Tools);
            AssertTags("Parachute", ItemCategory.Equipment | ItemCategory.Consumables);
            AssertTags("Rocketpack", ItemCategory.Equipment | ItemCategory.Consumables);
            AssertTags("Item_Coconut", ItemCategory.Food | ItemCategory.Props);
            // 童军饼干有 4 次用量（Action_ReduceUses），但「食物」与「消耗品」是互斥的：
            // 玩家在消耗品分类里看到饼干会觉得分类错了。次数只是它的使用方式，不改变它是食物这件事。
            AssertTags("ScoutCookies", ItemCategory.Food);
            AssertTags("ScoutCookies_Vanilla", ItemCategory.Food);

            // 以下 4 条与 scripts/gen_category_multi.py 的输出**冲突**，是纯人工修订。
            // 生成器会把它们改回去（Darkberry 丢掉 Mystical、PandorasBox 归 Mystical 单标签等），
            // 所以它们比上面那些更需要锁定：一旦有人重跑生成器覆盖 ItemCatalog.cs，这里立刻失败。
            AssertTags("Darkberry", ItemCategory.Food | ItemCategory.Mystical);
            AssertTags("PandorasBox", ItemCategory.Consumables | ItemCategory.Mystical);
            AssertTags("Mandrake", ItemCategory.Food);
            AssertTags("Shell Big", ItemCategory.Props);
        }

        private static void AssertTags(string prefab, ItemCategory expected)
        {
            ItemCategory actual;
            Assert.That(ItemCatalog.ItemTagMap.TryGetValue(prefab, out actual), Is.True,
                prefab + " 不在静态分类表中");
            Assert.That(actual, Is.EqualTo(expected), prefab + " 分类被改动");
        }

        // ---------- IsInMajor ----------

        [Test]
        public void IsInMajor_AllMatchesAnyNonEmptyTagSet()
        {
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Tools, MajorCategory.All), Is.True);
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Props, MajorCategory.All), Is.True);
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.None, MajorCategory.All), Is.False);
        }

        [Test]
        public void IsInMajor_SingleTagMatchesOnlyItsOwnCategory()
        {
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Tools, MajorCategory.Tools), Is.True);
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Tools, MajorCategory.Food), Is.False);
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Tools, MajorCategory.Props), Is.False);
        }

        [Test]
        public void IsInMajor_MultiTagMatchesEveryTagItHas()
        {
            ItemCategory torch = ItemCategory.Tools | ItemCategory.Consumables;
            Assert.That(ItemCatalog.IsInMajor(torch, MajorCategory.Tools), Is.True);
            Assert.That(ItemCatalog.IsInMajor(torch, MajorCategory.Consumables), Is.True);
            Assert.That(ItemCatalog.IsInMajor(torch, MajorCategory.Food), Is.False);
            Assert.That(ItemCatalog.IsInMajor(torch, MajorCategory.All), Is.True);
        }

        [Test]
        public void IsInMajor_UnknownSentinelValueMatchesNothing()
        {
            // UiEnhancer 用 (MajorCategory)(-1) 作为「无按钮高亮」哨兵。
            // default 返回 false（空列表，立刻可见）比 true（静默全通过）更容易发现错误。
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Tools, (MajorCategory)(-1)), Is.False);
            Assert.That(ItemCatalog.IsInMajor(ItemCategory.Tools, (MajorCategory)99), Is.False);
        }

        [Test]
        public void IsInMajor_CoversEveryDeclaredMajorCategory()
        {
            // 保证枚举新增成员时不会静默落进 default（会被上一条测试的语义捕获为"永不匹配"）
            ItemCategory all = ItemCategory.Tools | ItemCategory.Food | ItemCategory.Mystical
                | ItemCategory.Equipment | ItemCategory.Consumables | ItemCategory.Props;
            foreach (MajorCategory major in Enum.GetValues(typeof(MajorCategory)))
            {
                Assert.That(ItemCatalog.IsInMajor(all, major), Is.True,
                    major + " 未被 IsInMajor 处理（落进了 default）");
            }
        }

        // ---------- PrimaryOfTags ----------

        [Test]
        public void PrimaryOfTags_PicksLowestBitValueCategory()
        {
            // 位值顺序 Tools(1) < Food(2) < Mystical(4) < Equipment(8) < Consumables(16) < Props(32)
            Assert.That(ItemCatalog.PrimaryOfTags(ItemCategory.Tools | ItemCategory.Consumables),
                Is.EqualTo(ItemCategory.Tools));
            Assert.That(ItemCatalog.PrimaryOfTags(ItemCategory.Food | ItemCategory.Props),
                Is.EqualTo(ItemCategory.Food));
            Assert.That(ItemCatalog.PrimaryOfTags(ItemCategory.Equipment | ItemCategory.Consumables),
                Is.EqualTo(ItemCategory.Equipment));
            Assert.That(ItemCatalog.PrimaryOfTags(ItemCategory.Consumables | ItemCategory.Props),
                Is.EqualTo(ItemCategory.Consumables));
        }

        [Test]
        public void PrimaryOfTags_NoneFallsBackToProps()
        {
            Assert.That(ItemCatalog.PrimaryOfTags(ItemCategory.None), Is.EqualTo(ItemCategory.Props));
        }

        [Test]
        public void PrimaryOfTags_SingleTagReturnsItself()
        {
            ItemCategory[] singles = new ItemCategory[]
            {
                ItemCategory.Tools, ItemCategory.Food, ItemCategory.Mystical,
                ItemCategory.Equipment, ItemCategory.Consumables, ItemCategory.Props
            };
            for (int i = 0; i < singles.Length; i++)
            {
                Assert.That(ItemCatalog.PrimaryOfTags(singles[i]), Is.EqualTo(singles[i]));
            }
        }

        [Test]
        public void PrimaryOfTags_ResultIsAlwaysASingleFlag()
        {
            foreach (KeyValuePair<string, ItemCategory> kv in ItemCatalog.ItemTagMap)
            {
                ItemCategory primary = ItemCatalog.PrimaryOfTags(kv.Value);
                int bits = 0;
                int v = (int)primary;
                while (v != 0)
                {
                    bits += v & 1;
                    v >>= 1;
                }
                Assert.That(bits, Is.EqualTo(1), kv.Key + " 的主分类不是单一 flag: " + primary);
                Assert.That((kv.Value & primary), Is.Not.EqualTo(ItemCategory.None),
                    kv.Key + " 的主分类不属于它自己的标签集合");
            }
        }

        // ---------- ExtraNameKeys / ExtraCustomNames ----------

        [Test]
        public void ExtraNameKeys_HaveNameKeyFormat()
        {
            foreach (KeyValuePair<string, string> kv in ItemCatalog.ExtraNameKeys)
            {
                Assert.That(kv.Value, Does.StartWith("NAME_"),
                    kv.Key + " 的本地化 key 不是 NAME_ 前缀: " + kv.Value);
                Assert.That(kv.Value.Trim(), Is.EqualTo(kv.Value), kv.Key + " 的 key 含首尾空白");
            }
        }

        [Test]
        public void ExtraNameKeys_ExactMappingsAreLockedIn()
        {
            // 只断言 "NAME_" 前缀太弱：把 EggRaven 的 key 写成 NAME_BIRD 也照过，
            // 而那会让渡鸦蛋显示成"鸟"。这 6 条已核实在游戏本地化表里都存在且与
            // `"NAME_" + UIData.itemName.ToUpperInvariant()` 的默认解析结果一致，
            // 因此可以逐条锁定 —— 值被改动即失败。
            Dictionary<string, string> expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Bugfix", "NAME_TICK" },
                { "EggRaven", "NAME_EGG" },
                { "EggTurkey", "NAME_BIRD" },
                { "NestEgg", "NAME_BIG EGG" },
                { "NestEgg_Raven", "NAME_SMALL EGG" },
                { "Parachute", "NAME_AUTOPARACHUTE" },
            };
            Assert.That(ItemCatalog.ExtraNameKeys.Count, Is.EqualTo(expected.Count),
                "ExtraNameKeys 条目数变化，请同步本断言");
            foreach (KeyValuePair<string, string> kv in expected)
            {
                string actual;
                Assert.That(ItemCatalog.ExtraNameKeys.TryGetValue(kv.Key, out actual), Is.True,
                    kv.Key + " 不在 ExtraNameKeys 中");
                Assert.That(actual, Is.EqualTo(kv.Value), kv.Key + " 的本地化 key 被改动");
            }
        }

        [Test]
        public void ExtraCustomNames_CoverEveryKnownLanguageCodeExactly()
        {
            // 2.3.0 把值从「[中文, 英文] 两元组」改成「语言码 -> 显示名」字典。
            // 旧断言（ExtraCustomNames_HaveChineseAndOptionalEnglish）只检查 value[0] 非空、
            // value[1] 非 null，那在新结构下连编译都过不了；更重要的是它本来也不能捕获
            // "少给了某个语言的译名"——而这正是旧两元组结构的实际缺陷：13 种语言下这些物品
            // 显示英文名，在一屏母语物品里格外突兀。
            //
            // 因此这里断言**恰好覆盖**白名单的 15 个语言码：漏一个就失败（否则那个语言静默
            // 回退英文，没有任何信号），多一个也失败（写错语言码等同于漏写，同样静默回退）。
            Assert.That(ItemCatalog.ExtraCustomNames.Count, Is.GreaterThan(0));
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in ItemCatalog.ExtraCustomNames)
            {
                Dictionary<string, string> byLanguage = kv.Value;
                Assert.That(byLanguage, Is.Not.Null, kv.Key + " 的语言字典为 null");

                HashSet<string> expected = new HashSet<string>(
                    LocalizationCatalog.KnownLanguageCodes, StringComparer.OrdinalIgnoreCase);
                HashSet<string> actual = new HashSet<string>(
                    byLanguage.Keys, StringComparer.OrdinalIgnoreCase);

                List<string> missing = new List<string>();
                foreach (string code in expected)
                {
                    if (!actual.Contains(code)) { missing.Add(code); }
                }
                List<string> extra = new List<string>();
                foreach (string code in actual)
                {
                    if (!expected.Contains(code)) { extra.Add(code); }
                }
                Assert.That(missing, Is.Empty,
                    kv.Key + " 缺少语言译名（该语言会静默回退英文）: " + string.Join(", ", missing.ToArray()));
                Assert.That(extra, Is.Empty,
                    kv.Key + " 含未登记的语言码（等同于漏写，运行时永不命中）: " + string.Join(", ", extra.ToArray()));

                foreach (KeyValuePair<string, string> name in byLanguage)
                {
                    Assert.That(name.Value, Is.Not.Null.And.Not.Empty,
                        kv.Key + " / " + name.Key + " 的译名为空");
                    Assert.That(name.Value.Trim(), Is.EqualTo(name.Value),
                        kv.Key + " / " + name.Key + " 的译名含首尾空白");
                }
            }
        }

        [Test]
        public void ExtraCustomNames_LanguageLookupIsCaseInsensitive()
        {
            // ItemListView.ResolveDisplayName 用 GameLanguage.CurrentCode 直接查这张表。
            // 若内层字典退回默认（Ordinal）比较器，游戏返回 "ZH-HANS" 这类大小写不同的码时
            // 会整表失配、静默回退英文。查 "EN" 能命中 "en" 就说明比较器是 OrdinalIgnoreCase。
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in ItemCatalog.ExtraCustomNames)
            {
                Assert.That(kv.Value.ContainsKey("EN"), Is.True,
                    kv.Key + " 的语言字典不是 OrdinalIgnoreCase（查 'EN' 未命中 'en'）");
                Assert.That(kv.Value.ContainsKey("ZH-hans"), Is.True,
                    kv.Key + " 的语言字典不是 OrdinalIgnoreCase（查 'ZH-hans' 未命中 'zh-Hans'）");
            }
            // 外层（prefab 名）同样必须忽略大小写，与 ItemTagMap 口径一致
            Assert.That(ItemCatalog.ExtraCustomNames.ContainsKey("climbingchalk"), Is.True,
                "ExtraCustomNames 外层字典不是 OrdinalIgnoreCase");
        }

        [Test]
        public void ExtraNameKeysAndCustomNamesDoNotOverlap()
        {
            // 两者在 ResolveDisplayName 中是有序 fallback；同一 prefab 同时出现在两表意味着后者永不生效
            foreach (KeyValuePair<string, string> kv in ItemCatalog.ExtraNameKeys)
            {
                Assert.That(ItemCatalog.ExtraCustomNames.ContainsKey(kv.Key), Is.False,
                    kv.Key + " 同时出现在 ExtraNameKeys 与 ExtraCustomNames");
            }
        }

        [Test]
        public void HiddenRuleTablesAreNonEmptyAndTrimmed()
        {
            Assert.That(ItemCatalog.HiddenExact.Count, Is.GreaterThan(0));
            Assert.That(ItemCatalog.HiddenPrefixes.Length, Is.GreaterThan(0));
            Assert.That(ItemCatalog.HiddenSubstrings.Length, Is.GreaterThan(0));

            foreach (string s in ItemCatalog.HiddenExact)
            {
                Assert.That(s, Is.Not.Empty);
                Assert.That(s.Trim(), Is.EqualTo(s), "HiddenExact 项含首尾空白: [" + s + "]");
            }
            for (int i = 0; i < ItemCatalog.HiddenPrefixes.Length; i++)
            {
                Assert.That(ItemCatalog.HiddenPrefixes[i], Is.Not.Empty);
            }
            for (int i = 0; i < ItemCatalog.HiddenSubstrings.Length; i++)
            {
                Assert.That(ItemCatalog.HiddenSubstrings[i], Is.Not.Empty);
            }
        }
    }
}
