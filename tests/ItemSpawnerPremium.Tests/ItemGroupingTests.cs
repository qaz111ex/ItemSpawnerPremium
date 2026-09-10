using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// <see cref="ItemGrouping.ComputeGroupNames"/> 的单元测试。
    ///
    /// 这个函数决定的是一屏物品的排列顺序：同族物品能不能挨在一起、整族落在哪个位置。
    /// 玩家对"顺序"最敏感（东西跑位了立刻就会发现），所以这里把语义逐条钉死。
    /// </summary>
    [TestFixture]
    public class ItemGroupingTests
    {
        [Test]
        public void FamilyMembersShareTheSmallestDisplayName()
        {
            string[] keys = new string[] { "berry", "berry", "berry" };
            string[] names = new string[] { "red berry", "black berry", "yellow berry" };
            string[] result = ItemGrouping.ComputeGroupNames(keys, names);
            for (int i = 0; i < result.Length; i++)
            {
                Assert.That(result[i], Is.EqualTo("black berry"),
                    "同族成员必须共用一个排序名，且取族内最小显示名");
            }
        }

        [Test]
        public void UngroupedItemsKeepTheirOwnDisplayName()
        {
            string[] keys = new string[] { null, "", "   ", "berry" };
            string[] names = new string[] { "alpha", "beta", "gamma", "red berry" };
            string[] result = ItemGrouping.ComputeGroupNames(keys, names);
            Assert.That(result[0], Is.EqualTo("alpha"), "未登记(null)应保持自身显示名");
            Assert.That(result[1], Is.EqualTo("beta"), "空串同样视为未登记");
            Assert.That(result[2], Is.EqualTo("gamma"), "纯空白同样视为未登记");
            // "berry" 只有一个成员：仍然按族处理（返回自身），行为与未登记一致
            Assert.That(result[3], Is.EqualTo("red berry"));
        }

        [Test]
        public void SingleMemberFamilyBehavesLikeUngrouped()
        {
            string[] keys = new string[] { "solo", "other" };
            string[] names = new string[] { "zzz", "aaa" };
            string[] result = ItemGrouping.ComputeGroupNames(keys, names);
            Assert.That(result[0], Is.EqualTo("zzz"));
            Assert.That(result[1], Is.EqualTo("aaa"));
        }

        [Test]
        public void FamilyKeyLookupIsCaseInsensitive()
        {
            // ItemFamily 用 OrdinalIgnoreCase 查表，算分组名时也必须同口径，
            // 否则仅大小写不同的 key 会被当成两个族。
            string[] keys = new string[] { "Berry", "berry" };
            string[] names = new string[] { "b", "a" };
            string[] result = ItemGrouping.ComputeGroupNames(keys, names);
            Assert.That(result[0], Is.EqualTo("a"));
            Assert.That(result[1], Is.EqualTo("a"));
        }

        [Test]
        public void MinComparisonMatchesTheSorterCaseInsensitively()
        {
            // 取最小必须用与 CompareCatalog 相同的 OrdinalIgnoreCase，
            // 否则"哪个成员最小"与"整族落位"会不一致。
            string[] keys = new string[] { "f", "f", "f" };
            string[] names = new string[] { "B", "a", "C" };
            string[] result = ItemGrouping.ComputeGroupNames(keys, names);
            Assert.That(result[0], Is.EqualTo("a"));
        }

        [Test]
        public void NullDisplayNameDoesNotThrow()
        {
            string[] keys = new string[] { "f", "f" };
            string[] names = new string[] { null, "x" };
            string[] result = null;
            Assert.DoesNotThrow(delegate { result = ItemGrouping.ComputeGroupNames(keys, names); });
            Assert.That(result[0], Is.EqualTo(""));
            Assert.That(result[1], Is.EqualTo(""));
        }

        [Test]
        public void MismatchedLengthsThrow()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                ItemGrouping.ComputeGroupNames(new string[] { "a" }, new string[] { "a", "b" });
            });
        }

        [Test]
        public void NullArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(delegate
            {
                ItemGrouping.ComputeGroupNames(null, new string[] { "a" });
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                ItemGrouping.ComputeGroupNames(new string[] { "a" }, null);
            });
        }

        [Test]
        public void EmptyInputReturnsEmpty()
        {
            string[] result = ItemGrouping.ComputeGroupNames(new string[0], new string[0]);
            Assert.That(result.Length, Is.EqualTo(0));
        }

        // ---------- 与真实数据结合：家族表本身的不变量 ----------

        [Test]
        public void EveryFamilyHasAtLeastTwoMembers()
        {
            // 只有一个成员的家族没有任何意义（它只是给该物品换了个排序名，
            // 而且换成了"族内最小显示名"＝它自己的显示名，等于没做）。
            // 真出现这种条目说明家族表写错了。
            Dictionary<string, List<string>> byFamily = GroupRealTable();
            List<string> singles = new List<string>();
            foreach (KeyValuePair<string, List<string>> kv in byFamily)
            {
                if (kv.Value.Count < 2)
                {
                    singles.Add(kv.Key + "(" + kv.Value[0] + ")");
                }
            }
            Assert.That(singles, Is.Empty, "以下家族只有一个成员: " + string.Join(", ", singles.ToArray()));
        }

        [Test]
        public void EveryFamilyMemberIsAVisibleItem()
        {
            // 家族表里的 prefab 名必须是分类表里的键：那样它既真实存在于游戏数据库
            // （分类表与可见集一一对应，verify_catalog.py 守着），也不会被隐藏规则挡掉。
            // 拼错一个字母、或登记了一个被隐藏的物品，都会在这里失败。
            List<string> bad = new List<string>();
            foreach (KeyValuePair<string, string> kv in ItemCatalog.ItemFamily)
            {
                if (!ItemCatalog.ItemTagMap.ContainsKey(kv.Key))
                {
                    bad.Add(kv.Key + " 不在分类表里（拼错？还是它已被隐藏？）");
                }
            }
            Assert.That(bad, Is.Empty, string.Join("; ", bad.ToArray()));
        }

        [Test]
        public void FamilyDoesNotSpanMultipleCategories()
        {
            // 主分类是排序的第一级，跨分类的家族成员永远不会相邻 —— 那样登记家族是白费功夫，
            // 而且暗示家族表里混进了本该分开的东西。真出现就报出来。
            Dictionary<string, List<string>> byFamily = GroupRealTable();
            List<string> cross = new List<string>();
            foreach (KeyValuePair<string, List<string>> kv in byFamily)
            {
                ItemCategory first = PrimaryOf(kv.Value[0]);
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    if (PrimaryOf(kv.Value[i]) != first)
                    {
                        cross.Add(kv.Key + "（" + kv.Value[0] + " vs " + kv.Value[i] + "）");
                        break;
                    }
                }
            }
            Assert.That(cross, Is.Empty,
                "以下家族横跨多个主分类，成员不会相邻: " + string.Join(", ", cross.ToArray()));
        }

        [Test]
        public void FamilyKeysAreNonEmptyAndTrimmed()
        {
            foreach (KeyValuePair<string, string> kv in ItemCatalog.ItemFamily)
            {
                Assert.That(kv.Value, Is.Not.Null.And.Not.Empty, kv.Key + " 的家族 key 为空");
                Assert.That(kv.Value.Trim(), Is.EqualTo(kv.Value), kv.Key + " 的家族 key 有首尾空白");
            }
        }

        [Test]
        public void KnownFamiliesAreGroupedAsExpected()
        {
            // 用户明确点名的三组：四种葚莓、四种脆莓、两种绳索炮。
            AssertFamilyMembers("clusterberry",
                "Clusterberry Black", "Clusterberry Red", "Clusterberry Yellow", "Clusterberry_UNUSED");
            AssertFamilyMembers("crispberry",
                "Apple Berry Green", "Apple Berry Red", "Apple Berry Yellow", "Apple Berry Weird");
            AssertFamilyContains("rope", "RopeShooter");
            AssertFamilyContains("rope", "RopeShooterAnti");
        }

        [Test]
        public void ClusterberryFamilyActuallySortsAdjacentInChinese()
        {
            // 端到端：把四种葚莓的中文名喂进去，排序后必须连续。
            // 这正是玩家反馈的场景（中文名共享后缀"葚莓"、前缀不同，码点序会把它们打散）。
            string[] prefabs = new string[]
            {
                "Clusterberry Black", "Clusterberry Red", "Clusterberry Yellow", "Clusterberry_UNUSED",
                "Napberry", "Granola Bar",          // 两个无关物品插在中间
            };
            string[] names = new string[] { "黑葚莓", "红葚莓", "黄葚莓", "青葚莓", "晚安莓", "燕麦棒" };
            string[] groups = ItemGrouping.ComputeGroupNames(FamilyKeysOf(prefabs), names);

            List<int> order = new List<int>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                order.Add(i);
            }
            order.Sort(delegate (int i, int j)
            {
                int r = SearchRanking.CompareCatalog(
                    ItemCategory.Food, groups[i], names[i], prefabs[i],
                    ItemCategory.Food, groups[j], names[j], prefabs[j]);
                return r != 0 ? r : i.CompareTo(j);
            });

            // 四个葚莓在结果里必须占据连续下标
            List<int> berryPositions = new List<int>();
            for (int pos = 0; pos < order.Count; pos++)
            {
                if (order[pos] <= 3)
                {
                    berryPositions.Add(pos);
                }
            }
            Assert.That(berryPositions.Count, Is.EqualTo(4));
            for (int i = 1; i < berryPositions.Count; i++)
            {
                Assert.That(berryPositions[i], Is.EqualTo(berryPositions[i - 1] + 1),
                    "四种葚莓排序后没有连续排列，位置: " + JoinInts(berryPositions));
            }
        }

        // ---------- 辅助 ----------

        private static string[] FamilyKeysOf(string[] prefabs)
        {
            string[] keys = new string[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                keys[i] = ItemCatalog.GetFamily(prefabs[i]);
            }
            return keys;
        }

        private static string JoinInts(List<int> values)
        {
            string[] parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                parts[i] = values[i].ToString();
            }
            return string.Join(",", parts);
        }

        private static Dictionary<string, List<string>> GroupRealTable()
        {
            Dictionary<string, List<string>> byFamily =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in ItemCatalog.ItemFamily)
            {
                List<string> list;
                if (!byFamily.TryGetValue(kv.Value, out list))
                {
                    list = new List<string>();
                    byFamily[kv.Value] = list;
                }
                list.Add(kv.Key);
            }
            return byFamily;
        }

        private static ItemCategory PrimaryOf(string prefab)
        {
            ItemCategory tags;
            Assert.That(ItemCatalog.ItemTagMap.TryGetValue(prefab, out tags), Is.True,
                prefab + " 不在分类表里");
            return ItemCatalog.PrimaryOfTags(tags);
        }

        private static void AssertFamilyMembers(string family, params string[] expectedPrefabs)
        {
            for (int i = 0; i < expectedPrefabs.Length; i++)
            {
                Assert.That(ItemCatalog.GetFamily(expectedPrefabs[i]), Is.EqualTo(family),
                    expectedPrefabs[i] + " 的家族应为 " + family);
            }
        }

        private static void AssertFamilyContains(string family, string prefab)
        {
            Assert.That(ItemCatalog.GetFamily(prefab), Is.EqualTo(family),
                prefab + " 的家族应为 " + family);
        }
    }
}
