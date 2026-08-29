using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// 收藏数据编解码的回归测试（FavoriteCodec）。
    /// 这里守护的是「用户数据不能丢」：损坏输入必须可修复、往返必须无损、
    /// 脏数据比对必须只报告不删除。
    /// </summary>
    [TestFixture]
    public class FavoriteCodecTests
    {
        // ---------- 往返 ----------

        [Test]
        public void RoundTrip_PreservesAllNames()
        {
            HashSet<string> original = FavoriteCodec.NewSet();
            original.Add("RopeSpool");
            original.Add("Torch");
            original.Add("Warpsketball");

            string json = FavoriteCodec.Serialize(original);
            HashSet<string> restored;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize(json, out restored, out error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(restored.Count, Is.EqualTo(3));
            Assert.That(restored.Contains("RopeSpool"), Is.True);
            Assert.That(restored.Contains("Torch"), Is.True);
            Assert.That(restored.Contains("Warpsketball"), Is.True);
        }

        [Test]
        public void RoundTrip_EmptySet()
        {
            string json = FavoriteCodec.Serialize(FavoriteCodec.NewSet());
            Assert.That(json, Is.EqualTo("[]"));

            HashSet<string> restored;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize(json, out restored, out error), Is.True);
            Assert.That(restored, Is.Empty);
        }

        // ---------- Serialize ----------

        [Test]
        public void Serialize_OutputIsDeterministicRegardlessOfInsertionOrder()
        {
            // 配置文件 diff 稳定性：同一批收藏无论加入顺序如何，写出的 JSON 必须字节一致
            HashSet<string> a = FavoriteCodec.NewSet();
            a.Add("Torch"); a.Add("Candle"); a.Add("Lantern");

            HashSet<string> b = FavoriteCodec.NewSet();
            b.Add("Lantern"); b.Add("Torch"); b.Add("Candle");

            Assert.That(FavoriteCodec.Serialize(b), Is.EqualTo(FavoriteCodec.Serialize(a)));
        }

        [Test]
        public void Serialize_SortsWithOrdinal()
        {
            HashSet<string> set = FavoriteCodec.NewSet();
            set.Add("banana"); set.Add("Apple"); set.Add("Cherry");
            // Ordinal 下大写字母（U+0041..）排在小写（U+0061..）之前
            Assert.That(FavoriteCodec.Serialize(set), Is.EqualTo("[\"Apple\",\"Cherry\",\"banana\"]"));
        }

        [Test]
        public void Serialize_SkipsNullEmptyAndWhitespaceNames()
        {
            List<string> names = new List<string> { "Torch", null, "", "   ", "\t", "Candle" };
            string json = FavoriteCodec.Serialize(names);
            Assert.That(json, Is.EqualTo("[\"Candle\",\"Torch\"]"));
        }

        [Test]
        public void Serialize_NullInputYieldsEmptyArray()
        {
            Assert.That(FavoriteCodec.Serialize(null), Is.EqualTo("[]"));
        }

        [Test]
        public void Serialize_EscapesSpecialCharacters()
        {
            // prefab 名理论上不含引号，但配置文件是用户可编辑的，必须保证输出仍是合法 JSON
            List<string> names = new List<string> { "Weird\"Name", "Back\\slash" };
            string json = FavoriteCodec.Serialize(names);

            HashSet<string> restored;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize(json, out restored, out error), Is.True, error);
            Assert.That(restored.Contains("Weird\"Name"), Is.True);
            Assert.That(restored.Contains("Back\\slash"), Is.True);
        }

        // ---------- TryDeserialize：损坏输入 ----------

        [Test]
        public void TryDeserialize_ValidArray()
        {
            HashSet<string> set;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize("[\"Torch\",\"Candle\"]", out set, out error), Is.True);
            Assert.That(set.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryDeserialize_JsonNullIsTreatedAsCorrupt()
        {
            // "null" 是合法 JSON 但反序列化成 null 集合，后续 IsFavorite 会 NRE，
            // 因此必须判为损坏让调用方回写 "[]"
            HashSet<string> set;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize("null", out set, out error), Is.False);
            Assert.That(set, Is.Null);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void TryDeserialize_EmptyStringIsTreatedAsCorrupt()
        {
            HashSet<string> set;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize("", out set, out error), Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void TryDeserialize_MalformedJsonIsTreatedAsCorrupt()
        {
            string[] broken = new string[]
            {
                "[",
                "{\"a\":1}",
                "not json at all",
                "[\"unterminated",
                "42"
            };
            for (int i = 0; i < broken.Length; i++)
            {
                HashSet<string> set;
                string error;
                bool ok = FavoriteCodec.TryDeserialize(broken[i], out set, out error);
                Assert.That(ok, Is.False, "输入 [" + broken[i] + "] 应判为损坏");
                Assert.That(error, Is.Not.Null.And.Not.Empty, "损坏必须给出原因: " + broken[i]);
            }
        }

        [Test]
        public void TryDeserialize_NeverThrows()
        {
            // 配置文件内容完全由用户掌控，任何输入都不能让插件在 Awake 阶段崩溃
            string[] hostile = new string[]
            {
                null, "", "   ", "\0", "[[[[[[", "{}", "[1,2,3]",
                "[\"a\",null,\"b\"]", "[\"\",\"  \"]"
            };
            for (int i = 0; i < hostile.Length; i++)
            {
                HashSet<string> set;
                string error;
                string input = hostile[i];
                Assert.DoesNotThrow(delegate
                {
                    FavoriteCodec.TryDeserialize(input, out set, out error);
                }, "输入 [" + (input ?? "<null>") + "] 抛异常");
            }
        }

        [Test]
        public void TryDeserialize_SkipsNullAndWhitespaceElements()
        {
            HashSet<string> set;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize("[\"Torch\",null,\"\",\"  \",\"Candle\"]",
                out set, out error), Is.True);
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.Contains("Torch"), Is.True);
            Assert.That(set.Contains("Candle"), Is.True);
        }

        [Test]
        public void TryDeserialize_DeduplicatesCaseInsensitively()
        {
            HashSet<string> set;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize("[\"Torch\",\"torch\",\"TORCH\"]",
                out set, out error), Is.True);
            Assert.That(set.Count, Is.EqualTo(1), "集合口径应为 OrdinalIgnoreCase");
        }

        // ---------- 大小写口径 ----------

        [Test]
        public void Sets_UseOrdinalIgnoreCaseToMatchItemCatalogLookups()
        {
            // 与 ItemCatalog.ItemTagMap / HiddenExact 的 OrdinalIgnoreCase 口径一致：
            // 用户手工编辑配置写错大小写时，收藏仍应命中，而不是静默失效
            HashSet<string> set = FavoriteCodec.NewSet();
            set.Add("RopeSpool");
            Assert.That(set.Contains("ropespool"), Is.True);
            Assert.That(set.Contains("ROPESPOOL"), Is.True);
        }

        [Test]
        public void CopySet_PreservesComparer()
        {
            HashSet<string> source = FavoriteCodec.NewSet();
            source.Add("Torch");
            HashSet<string> copy = FavoriteCodec.CopySet(source);
            Assert.That(copy.Contains("torch"), Is.True, "副本丢失了 OrdinalIgnoreCase 口径");
        }

        [Test]
        public void CopySet_IsIndependentOfSource()
        {
            // write-ahead 语义的前提：改副本不能影响原集合
            HashSet<string> source = FavoriteCodec.NewSet();
            source.Add("Torch");
            HashSet<string> copy = FavoriteCodec.CopySet(source);
            copy.Add("Candle");
            copy.Remove("Torch");

            Assert.That(source.Count, Is.EqualTo(1));
            Assert.That(source.Contains("Torch"), Is.True);
            Assert.That(source.Contains("Candle"), Is.False);
        }

        // ---------- FindStale ----------

        [Test]
        public void FindStale_ReturnsNamesNotInValidSet()
        {
            HashSet<string> favorites = FavoriteCodec.NewSet();
            favorites.Add("Torch");
            favorites.Add("ModdedItem");

            List<string> stale = FavoriteCodec.FindStale(favorites, new string[] { "Torch", "Candle" });
            Assert.That(stale, Is.Not.Null);
            Assert.That(stale.Count, Is.EqualTo(1));
            Assert.That(stale[0], Is.EqualTo("ModdedItem"));
        }

        [Test]
        public void FindStale_ReturnsNullWhenEverythingIsValid()
        {
            HashSet<string> favorites = FavoriteCodec.NewSet();
            favorites.Add("Torch");
            Assert.That(FavoriteCodec.FindStale(favorites, new string[] { "Torch", "Candle" }), Is.Null);
        }

        [Test]
        public void FindStale_EmptyValidSetIsTreatedAsUnknownUniverse()
        {
            // 空全集意味着「数据库尚未就绪」，此时任何比对都无意义 —— 不能据此报告全部收藏为脏
            HashSet<string> favorites = FavoriteCodec.NewSet();
            favorites.Add("Torch");
            Assert.That(FavoriteCodec.FindStale(favorites, new string[0]), Is.Null);
        }

        [Test]
        public void FindStale_NullArgumentsReturnNull()
        {
            Assert.That(FavoriteCodec.FindStale(null, new string[] { "a" }), Is.Null);
            Assert.That(FavoriteCodec.FindStale(new string[] { "a" }, null), Is.Null);
        }

        [Test]
        public void FindStale_MatchesCaseInsensitively()
        {
            HashSet<string> favorites = FavoriteCodec.NewSet();
            favorites.Add("RopeSpool");
            // 全集里大小写不同，不应被误判为脏数据（否则旧实现会把它删掉）
            Assert.That(FavoriteCodec.FindStale(favorites, new string[] { "ropespool" }), Is.Null);
        }

        [Test]
        public void FindStale_ResultIsSortedForStableLogs()
        {
            HashSet<string> favorites = FavoriteCodec.NewSet();
            favorites.Add("Zebra");
            favorites.Add("Alpha");
            favorites.Add("Middle");

            List<string> stale = FavoriteCodec.FindStale(favorites, new string[] { "Other" });
            Assert.That(stale, Is.Not.Null);
            Assert.That(stale.Count, Is.EqualTo(3));
            Assert.That(stale[0], Is.EqualTo("Alpha"));
            Assert.That(stale[1], Is.EqualTo("Middle"));
            Assert.That(stale[2], Is.EqualTo("Zebra"));
        }

        [Test]
        public void FindStale_DoesNotMutateInputs()
        {
            // FindStale 是纯查询：Prune 的「只报告不删除」语义依赖它不动任何数据
            HashSet<string> favorites = FavoriteCodec.NewSet();
            favorites.Add("Torch");
            favorites.Add("ModdedItem");
            int before = favorites.Count;

            FavoriteCodec.FindStale(favorites, new string[] { "Torch" });

            Assert.That(favorites.Count, Is.EqualTo(before));
            Assert.That(favorites.Contains("ModdedItem"), Is.True,
                "脏数据必须保留 —— 误删是不可逆的用户数据丢失");
        }

        [Test]
        public void EmptyJsonConstantIsAValidEmptyArray()
        {
            HashSet<string> set;
            string error;
            Assert.That(FavoriteCodec.TryDeserialize(FavoriteCodec.EmptyJson, out set, out error), Is.True);
            Assert.That(set, Is.Empty);
        }
    }
}
