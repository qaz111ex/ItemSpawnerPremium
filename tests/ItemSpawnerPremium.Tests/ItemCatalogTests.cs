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
            Assert.That(ItemCatalog.IsHidden("Binoculars_Prop"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Something_TEMP"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Clusterberry_UNUSED"), Is.True);
            Assert.That(ItemCatalog.IsHidden("Mandrake_Hidden"), Is.True);
            // 子串匹配不限位置
            Assert.That(ItemCatalog.IsHidden("A_Prop_B"), Is.True);
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
            // 153 = 真值 213 个物品减去隐藏的 60 个。数量变化必须是有意识的改动。
            Assert.That(ItemCatalog.ItemTagMap.Count, Is.EqualTo(153),
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
            AssertTags("ScoutmasterSoul", ItemCategory.Mystical);

            AssertTags("Torch", ItemCategory.Tools | ItemCategory.Consumables);
            AssertTags("Lantern", ItemCategory.Tools | ItemCategory.Consumables);
            AssertTags("Candle", ItemCategory.Tools | ItemCategory.Consumables);

            AssertTags("HealingPuffShroom", ItemCategory.Consumables | ItemCategory.Tools);
            AssertTags("Parachute", ItemCategory.Equipment | ItemCategory.Consumables);
            AssertTags("Rocketpack", ItemCategory.Equipment | ItemCategory.Consumables);
            AssertTags("Item_Coconut", ItemCategory.Food | ItemCategory.Props);
            AssertTags("ScoutCookies", ItemCategory.Consumables | ItemCategory.Food);
            AssertTags("ScoutCookies_Vanilla", ItemCategory.Consumables | ItemCategory.Food);
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
        public void ExtraCustomNames_HaveChineseAndOptionalEnglish()
        {
            foreach (KeyValuePair<string, string[]> kv in ItemCatalog.ExtraCustomNames)
            {
                Assert.That(kv.Value, Is.Not.Null, kv.Key);
                Assert.That(kv.Value.Length, Is.GreaterThanOrEqualTo(1), kv.Key + " 自定义名为空数组");
                Assert.That(kv.Value[0], Is.Not.Empty, kv.Key + " 中文名为空");
                if (kv.Value.Length >= 2)
                {
                    Assert.That(kv.Value[1], Is.Not.Null, kv.Key + " 英文名为 null（应省略或给值）");
                }
            }
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
