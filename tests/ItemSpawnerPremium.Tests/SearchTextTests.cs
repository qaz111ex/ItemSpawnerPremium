using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// 搜索文本归一化（SearchText）与拼音（TinyPinyin）的回归测试。
    /// 覆盖审查报告列出的缺口：归一化口径一致性、拼音转换正确性、边界与非法输入。
    /// </summary>
    [TestFixture]
    public class SearchTextTests
    {
        // ---------- FilterAlnumLower / StripNonAlnum ----------

        [Test]
        public void StripNonAlnum_NullOrEmpty_ReturnsEmptyString()
        {
            // 返回 "" 而非 null 很关键：Score 里对 queryNoSpace 直接取 .Length
            Assert.That(SearchText.StripNonAlnum(null), Is.EqualTo(""));
            Assert.That(SearchText.StripNonAlnum(""), Is.EqualTo(""));
        }

        [Test]
        public void StripNonAlnum_RemovesSpacesPunctuationAndSymbols()
        {
            Assert.That(SearchText.StripNonAlnum("Anti-Rope Spool"), Is.EqualTo("antiropespool"));
            Assert.That(SearchText.StripNonAlnum("  Hot   Dog  "), Is.EqualTo("hotdog"));
            Assert.That(SearchText.StripNonAlnum("Cure-Some!"), Is.EqualTo("curesome"));
            Assert.That(SearchText.StripNonAlnum("Berrynana Peel Blue Variant"),
                Is.EqualTo("berrynanapeelbluevariant"));
        }

        [Test]
        public void StripNonAlnum_KeepsDigits()
        {
            Assert.That(SearchText.StripNonAlnum("Cheat Compass 1"), Is.EqualTo("cheatcompass1"));
        }

        [Test]
        public void StripNonAlnum_LowercasesWithInvariantCulture()
        {
            Assert.That(SearchText.StripNonAlnum("MYSTICAL"), Is.EqualTo("mystical"));

            // 旧断言只在默认区域下断言 StripNonAlnum("I") == "i"，注释虽然写着「必须走 Invariant，
            // 不能随线程区域漂移成 ı」，但**没有切换区域** —— 默认区域下 char.ToLower 与
            // char.ToLowerInvariant 同值，断言毫无区分力。实测：把 SearchText.cs 的
            // char.ToLowerInvariant 改成 char.ToLower 后全部测试仍然全绿。
            //
            // 土耳其/阿塞拜疆区域下 char.ToLower('I') == 'ı' (U+0131)，与 Invariant 的 'i' (U+0069)
            // 不同。归一化一旦漂移，displayName 与 query 会走不同的折叠规则，土耳其玩家搜含 I 的
            // 物品会漏匹配。所以必须在 tr-TR 下断言，并在 finally 里还原区域（测试须机器/区域无关）。
            System.Globalization.CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("tr-TR");

                // 先固化前提：本区域下 char.ToLower 确实与 Invariant 不同（否则本测试又退化成重言式，
                // 例如某些精简 ICU 环境不带土耳其规则时应当明确失败而不是假绿）。
                Assert.That(char.ToLower('I'), Is.EqualTo('\u0131'),
                    "当前运行环境的 tr-TR 大小写规则不生效，本测试无法验证 Invariant 归一化");
                Assert.That(char.ToLowerInvariant('I'), Is.EqualTo('i'));

                Assert.That(SearchText.StripNonAlnum("I"), Is.EqualTo("i"),
                    "归一化未用 Invariant：tr-TR 下 I 被折叠成 ı，与 query 口径不一致会导致漏匹配");
                Assert.That(SearchText.StripNonAlnum("IDOL"), Is.EqualTo("idol"));
                // 拼音归一化走同一个 FilterAlnumLower，同样不能漂移
                Assert.That(SearchText.ToPinyin("IDOL"), Is.EqualTo("idol"));
                Assert.That(SearchText.ToPinyinInitials("IDOL"), Is.EqualTo("idol"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void StripNonAlnum_KeepsCjkBecauseIsLetterOrDigitIsTrueForHan()
        {
            // 这是刻意保留的行为：中文 displayName 走同一归一化，若剔除汉字则显示名匹配档会失效。
            // 副作用是中文 query 永远匹配不到纯 ASCII 的 pinyin 字段 —— 无害，因为显示名档会先命中。
            Assert.That(SearchText.StripNonAlnum("热狗肠"), Is.EqualTo("热狗肠"));
        }

        [Test]
        public void StripNonAlnum_KeepsCyrillicAndLatinExtended()
        {
            Assert.That(SearchText.StripNonAlnum("Все"), Is.EqualTo("все"));
            Assert.That(SearchText.StripNonAlnum("Ausrüstung"), Is.EqualTo("ausrüstung"));
            Assert.That(SearchText.StripNonAlnum("Wyposażenie"), Is.EqualTo("wyposażenie"));
        }

        [Test]
        public void StripNonAlnum_DropsEmDashUsedInRightClickHint()
        {
            // pl.json 的 rightClickHint 含 U+2014，必须被剔除（它不是 LetterOrDigit）
            Assert.That(SearchText.StripNonAlnum("Prawy — dodaj"), Is.EqualTo("prawydodaj"));
        }

        // ---------- ToPinyin ----------

        [Test]
        public void ToPinyin_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.That(SearchText.ToPinyin(null), Is.EqualTo(""));
            Assert.That(SearchText.ToPinyin(""), Is.EqualTo(""));
        }

        [Test]
        public void ToPinyin_ConvertsHanToFullSpellingLowercaseWithoutSeparator()
        {
            Assert.That(SearchText.ToPinyin("绳索枪"), Is.EqualTo("shengsuoqiang"));
            Assert.That(SearchText.ToPinyin("热狗肠"), Is.EqualTo("regouchang"));
            Assert.That(SearchText.ToPinyin("杂项"), Is.EqualTo("zaxiang"));
        }

        [Test]
        public void ToPinyin_KeepsAsciiLettersAndDigits()
        {
            Assert.That(SearchText.ToPinyin("AK"), Is.EqualTo("ak"));
            Assert.That(SearchText.ToPinyin("罗盘 1"), Is.EqualTo("luopan1"));
        }

        [Test]
        public void ToPinyin_MixedHanAndLatin()
        {
            Assert.That(SearchText.ToPinyin("太空Basketball"), Is.EqualTo("taikongbasketball"));
        }

        [Test]
        public void ToPinyin_PipeCharacterDoesNotThrow()
        {
            // Engine 内部用 "|" 作分隔符实现首字母，输入自带 "|" 曾导致上游 Substring 越界（issue #5）
            Assert.DoesNotThrow(delegate { SearchText.ToPinyin("热|狗"); });
            Assert.DoesNotThrow(delegate { SearchText.ToPinyinInitials("热|狗"); });
        }

        [Test]
        public void ToPinyin_HandlesSurrogatePairsWithoutThrowing()
        {
            // 扩展区汉字是代理对，逐 char 处理时每一半都不在 BMP 汉字区间，应原样丢弃而不抛异常
            string emoji = "\U0001F600";
            Assert.DoesNotThrow(delegate { SearchText.ToPinyin(emoji); });
            Assert.That(SearchText.ToPinyin(emoji), Is.EqualTo(""));
        }

        [Test]
        public void ToPinyin_LingCharacterUsesSpecialCasePath()
        {
            // 〇 (U+3007) 不在 MIN_VALUE..MAX_VALUE 区间内，Engine 用 CHAR_12295 特例处理
            Assert.That(SearchText.ToPinyin("〇"), Is.EqualTo("ling"));
        }

        [Test]
        public void ToPinyin_NonChineseTextIsUnchangedApartFromNormalisation()
        {
            Assert.That(SearchText.ToPinyin("Rescue Claw"), Is.EqualTo("rescueclaw"));
            Assert.That(SearchText.ToPinyin("Все"), Is.EqualTo("все"));
        }

        // ---------- ToPinyinInitials ----------

        [Test]
        public void ToPinyinInitials_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.That(SearchText.ToPinyinInitials(null), Is.EqualTo(""));
            Assert.That(SearchText.ToPinyinInitials(""), Is.EqualTo(""));
        }

        [Test]
        public void ToPinyinInitials_TakesFirstLetterOfEachSyllable()
        {
            Assert.That(SearchText.ToPinyinInitials("绳索枪"), Is.EqualTo("ssq"));
            Assert.That(SearchText.ToPinyinInitials("热狗肠"), Is.EqualTo("rgc"));
            Assert.That(SearchText.ToPinyinInitials("杂项"), Is.EqualTo("zx"));
        }

        [Test]
        public void ToPinyinInitials_NonHanCharactersSurviveAsThemselves()
        {
            // 非汉字不是"音节"，GetPinyinInitials 对单字符段取首字符 = 原字符
            Assert.That(SearchText.ToPinyinInitials("AK"), Is.EqualTo("ak"));
        }

        [Test]
        public void ToPinyinInitials_IsPrefixConsistentWithToPinyinForPureHan()
        {
            // 不变量：首字母序列的每一位都是全拼中对应音节的首字母
            string full = SearchText.ToPinyin("绳索枪");
            string initials = SearchText.ToPinyinInitials("绳索枪");
            Assert.That(initials.Length, Is.EqualTo(3));
            Assert.That(full.StartsWith(initials.Substring(0, 1), StringComparison.Ordinal), Is.True);
        }

        // ---------- 归一化口径一致性（三者必须同规则）----------

        [Test]
        public void AllNormalisersShareTheSameCharacterClassRule()
        {
            // 若三者规则不一致，query 与 pinyin 字段就对不上，拼音搜索会静默漏匹配
            string raw = "热 狗-肠 AK 1!";
            string viaStrip = SearchText.StripNonAlnum(raw);
            Assert.That(viaStrip, Is.EqualTo("热狗肠ak1"));

            string pinyin = SearchText.ToPinyin(raw);
            string initials = SearchText.ToPinyinInitials(raw);
            foreach (char c in pinyin)
            {
                Assert.That(char.IsLetterOrDigit(c), Is.True, "全拼含非字母数字字符: " + c);
                Assert.That(char.IsUpper(c), Is.False, "全拼含大写字符: " + c);
            }
            foreach (char c in initials)
            {
                Assert.That(char.IsLetterOrDigit(c), Is.True, "首字母含非字母数字字符: " + c);
                Assert.That(char.IsUpper(c), Is.False, "首字母含大写字符: " + c);
            }
        }

        // ---------- TinyPinyin 数据表不变量 ----------

        [Test]
        public void PinyinDataTables_HaveExpectedShape()
        {
            Assert.That(TinyPinyin.Data.PinyinCode1.PINYIN_CODE.Length, Is.EqualTo(7000));
            Assert.That(TinyPinyin.Data.PinyinCode2.PINYIN_CODE.Length, Is.EqualTo(7000));
            Assert.That(TinyPinyin.Data.PinyinCode3.PINYIN_CODE.Length, Is.EqualTo(7000));
            Assert.That(TinyPinyin.Data.PinyinCode1.PINYIN_CODE_PADDING.Length, Is.EqualTo(875));
            Assert.That(TinyPinyin.Data.PinyinCode2.PINYIN_CODE_PADDING.Length, Is.EqualTo(875));
            Assert.That(TinyPinyin.Data.PinyinCode3.PINYIN_CODE_PADDING.Length, Is.EqualTo(875));
            // 875 == ceil(7000/8)：padding 是 1 bit/字的高位标记
            Assert.That(875, Is.EqualTo((7000 + 7) / 8));
        }

        [Test]
        public void PinyinCodeTables_CoverTheWholeBmpHanRange()
        {
            int span = TinyPinyin.Data.PinyinData.MAX_VALUE - TinyPinyin.Data.PinyinData.MIN_VALUE + 1;
            Assert.That(span, Is.LessThanOrEqualTo(7000 * 3), "三张表容量不足以覆盖 MIN..MAX 区间");
        }

        [Test]
        public void EveryCharacterInHanRangeDecodesWithoutThrowingOrGoingOutOfRange()
        {
            // 这是 Engine.GetPinyinByChar 上界防御的回归测试：全域扫描，任何一个字都不能抛
            int tableLength = TinyPinyin.Data.PinyinData.PINYIN_TABLE.Length;
            int converted = 0;
            for (char c = TinyPinyin.Data.PinyinData.MIN_VALUE; c <= TinyPinyin.Data.PinyinData.MAX_VALUE; c++)
            {
                string s = null;
                char captured = c;
                Assert.DoesNotThrow(delegate { s = TinyPinyin.Engine.GetPinyinByChar(captured); },
                    "U+" + ((int)captured).ToString("X4") + " 解码抛异常");
                Assert.That(s, Is.Not.Null);
                Assert.That(s.Length, Is.GreaterThan(0));
                if (TinyPinyin.Engine.IsChinese(c))
                {
                    converted++;
                }
            }
            Assert.That(tableLength, Is.GreaterThan(0));
            // 区间内绝大多数是有拼音的汉字；这里只做量级保护，防止数据表被整体清空还静默通过
            Assert.That(converted, Is.GreaterThan(15000), "可转换汉字数量异常偏少，数据表可能损坏");
        }

        [Test]
        public void IsChinese_RejectsLatinCyrillicAndPunctuation()
        {
            Assert.That(TinyPinyin.Engine.IsChinese('A'), Is.False);
            Assert.That(TinyPinyin.Engine.IsChinese('1'), Is.False);
            Assert.That(TinyPinyin.Engine.IsChinese('Ж'), Is.False);
            Assert.That(TinyPinyin.Engine.IsChinese('—'), Is.False);
            Assert.That(TinyPinyin.Engine.IsChinese('热'), Is.True);
        }

        [Test]
        public void GetPinyinByChar_ReturnsUppercaseFullSpellingForHan()
        {
            // Engine 层输出大写，小写化是 SearchText 的职责 —— 固化这个分工，避免有人在 Engine 里加 ToLower
            Assert.That(TinyPinyin.Engine.GetPinyinByChar('热'), Is.EqualTo("RE"));
        }

        // ---------- PinyinHelper 门面层（此前 0 覆盖）----------

        [Test]
        public void PinyinHelper_IsChinese_DelegatesToEngine()
        {
            // PinyinHelper 是 TinyPinyin 的公开门面；SearchText 只用到 GetPinyin(string, sep)
            // 与 GetPinyinInitials，另两个重载此前完全没有测试。它们是薄委托，但"薄"是当前实现的
            // 性质而非契约 —— 有人改成"顺手做点归一化"就会与 Engine 分叉，而 SearchText 的
            // 归一化口径一致性依赖它们与 Engine 同结论。
            Assert.That(TinyPinyin.PinyinHelper.IsChinese('热'), Is.True);
            Assert.That(TinyPinyin.PinyinHelper.IsChinese('A'), Is.False);
            Assert.That(TinyPinyin.PinyinHelper.IsChinese('〇'), Is.True, "〇 走 CHAR_12295 特例");
            Assert.That(TinyPinyin.PinyinHelper.IsChinese('热'),
                Is.EqualTo(TinyPinyin.Engine.IsChinese('热')));
        }

        [Test]
        public void PinyinHelper_GetPinyinChar_ReturnsUppercaseOrTheCharItself()
        {
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin('热'), Is.EqualTo("RE"));
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin('〇'), Is.EqualTo("LING"));
            // 非汉字原样返回单字符串（不是空串）—— ToPinyin 依赖这一点来保留 ASCII
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin('A'), Is.EqualTo("A"));
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin(' '), Is.EqualTo(" "));
        }

        [Test]
        public void PinyinHelper_GetPinyinString_DefaultSeparatorIsSingleSpace()
        {
            // 默认参数 separator = " " 这条路径没有任何调用方（SearchText 一律显式传 ""），
            // 因此改动它不会被现有测试发现。这里锁定它，因为它是 TinyPinyin 的公开 API 契约。
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin("绳索枪"), Is.EqualTo("SHENG SUO QIANG"));
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin("绳索枪", ""), Is.EqualTo("SHENGSUOQIANG"));
            Assert.That(TinyPinyin.PinyinHelper.GetPinyin("绳索枪", "-"), Is.EqualTo("SHENG-SUO-QIANG"));
        }

        [Test]
        public void PinyinHelper_GetPinyinInitials_HonoursNonEmptySeparator()
        {
            // 默认 separator = ""（SearchText 用的就是默认值），非空分隔符路径此前无覆盖
            Assert.That(TinyPinyin.PinyinHelper.GetPinyinInitials("绳索枪"), Is.EqualTo("SSQ"));
            Assert.That(TinyPinyin.PinyinHelper.GetPinyinInitials("绳索枪", "-"), Is.EqualTo("S-S-Q"));
            Assert.That(TinyPinyin.PinyinHelper.GetPinyinInitials("绳索枪", " "), Is.EqualTo("S S Q"));
            // null/空串按原样返回（注意：返回的是入参本身，不是 ""；SearchText 在上层已挡掉 null）
            Assert.That(TinyPinyin.PinyinHelper.GetPinyinInitials(null), Is.Null);
            Assert.That(TinyPinyin.PinyinHelper.GetPinyinInitials(""), Is.EqualTo(""));
        }

        [Test]
        public void Engine_ToPinyin_DoesNotAppendSeparatorAfterLastCharacter()
        {
            // Engine.ToPinyin 的循环里 `if (i != inputStr.Length - 1)` 是唯一的边界判断。
            // 若写成无条件 Append，结果会带尾随分隔符 —— SearchText 用的是空分隔符所以看不出来，
            // 但 GetPinyinInitials 传的是 "|"，尾随 "|" 会多出一个空音节段。
            Assert.That(TinyPinyin.Engine.ToPinyin("绳索枪", "-"), Is.EqualTo("SHENG-SUO-QIANG"));
            Assert.That(TinyPinyin.Engine.ToPinyin("热", "-"), Is.EqualTo("RE"), "单字符不应带分隔符");
            Assert.That(TinyPinyin.Engine.ToPinyin("绳索枪", "-"), Does.Not.EndWith("-"));
            // null/空串直接原样返回（不抛、不变成 ""）
            Assert.That(TinyPinyin.Engine.ToPinyin(null, "-"), Is.Null);
            Assert.That(TinyPinyin.Engine.ToPinyin("", "-"), Is.EqualTo(""));
        }

        [Test]
        public void ItemNameTable_AllVisibleChineseNamesConvertToNonEmptyPinyin()
        {
            // 用一组真实中文物品名做冒烟：任何一项转不出拼音都意味着中文搜索对该物品失效
            string[] names = new string[]
            {
                "绳索枪", "抓钩", "望远镜", "指南针", "热狗肠", "煎蛋", "雪球", "石头",
                "太空篮球", "风之杖", "童军饼干", "疗愈蘑菇", "整颗椰子", "火柴盒", "降落伞"
            };
            for (int i = 0; i < names.Length; i++)
            {
                string full = SearchText.ToPinyin(names[i]);
                string initials = SearchText.ToPinyinInitials(names[i]);
                Assert.That(full, Is.Not.Empty, names[i] + " 全拼为空");
                Assert.That(initials, Is.Not.Empty, names[i] + " 首字母为空");
                Assert.That(initials.Length, Is.LessThanOrEqualTo(full.Length));
            }
        }
    }
}
