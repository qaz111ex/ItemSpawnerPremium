using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// <see cref="InputKeys.IsTextInput"/> 的全域穷举测试。
    ///
    /// 为什么这个文件值得写得这么长：2.2.0 发布过一个 F5 死锁回归 ——「防止搜索框聚焦时
    /// 误吞文本按键」被实现成了「屏蔽全部按键」，配合"打开面板即聚焦搜索框"，玩家按 F5
    /// 打开面板后就再也关不掉。它落在三处架构盲区（MonoBehaviour 生命周期 / TMP 运行时状态 /
    /// 输入焦点）之一，单元测试当时完全触及不到。
    ///
    /// 2.3.0 把判定体提纯成 InputKeys.IsTextInput(int)（参数是 KeyCode 的底层数值，
    /// 因此不需要引用 UnityEngine.CoreModule），这是那三处盲区里唯一能做到**全域穷举**的一块：
    /// KeyCode 共 339 个成员、最大值 678，可以对 0..678 每个整数逐一断言。
    ///
    /// KeyCode 底层数值取自 UnityEngine.CoreModule 的枚举定义。刻意不引用别名成员
    /// （LeftApple/LeftCommand/LeftMeta 等多个名字共享同一数值），本文件一律用数值说话。
    /// </summary>
    [TestFixture]
    public class InputKeysTests
    {
        /// <summary>KeyCode 的最大数值（F24 = 678）。全域扫描的上界。</summary>
        private const int MaxKeyCode = 678;

        /// <summary>KeyCode.F5 —— 默认 ToggleKey，2.2.0 死锁回归的当事按键。</summary>
        private const int F5 = 286;

        // ---------- 1. 功能键必须全部放行（漏列一个就复现 F5 死锁）----------

        [Test]
        public void FunctionKeysF1ToF15_AreNotTextInput()
        {
            // F1=282 .. F15=296
            for (int k = 282; k <= 296; k++)
            {
                Assert.That(InputKeys.IsTextInput(k), Is.False,
                    "F" + (k - 281) + "(" + k + ") 被当成文本输入键 —— 会导致搜索框聚焦时该功能键失效");
            }
        }

        [Test]
        public void FunctionKeysF16ToF24_AreNotTextInput()
        {
            // F16..F24 的数值不与 F1..F15 连续（670..678），是最容易被区间判断漏掉的一段
            for (int k = 670; k <= MaxKeyCode; k++)
            {
                Assert.That(InputKeys.IsTextInput(k), Is.False,
                    "F" + (k - 654) + "(" + k + ") 被当成文本输入键");
            }
        }

        [Test]
        public void ToggleKeyDefaultF5_IsNeverSwallowedAsTextInput_RegressionGuardFor220Deadlock()
        {
            // ★ F5 死锁回归的精确守卫 ★
            // 2.2.0 已发布版本里，搜索框聚焦时 ToggleKey 的轮询被无条件跳过，而面板一打开就
            // 自动聚焦搜索框 —— 结果按 F5 打开后永远收不到关闭输入，玩家只能杀进程。
            // 若有人往 IsTextInput 的白名单里补上 case 286（或写出覆盖 282..296 的区间判断），
            // 这条断言会立刻失败。
            Assert.That(InputKeys.IsTextInput(F5), Is.False,
                "KeyCode.F5(286) 被判为文本输入键 —— 这将复现 2.2.0 已发布的 F5 死锁："
                + "面板打开即聚焦搜索框，F5 被让给输入框后面板再也无法关闭");
        }

        [Test]
        public void EveryPlausibleToggleKeyDefaultIsUsable()
        {
            // ToggleKey 是玩家可配置项，但常见取值必须都能在面板打开（搜索框聚焦）时收到。
            // F1-F12 / Escape / Pause / Insert / Home / End / PageUp / PageDown / Menu 全部放行；
            // 唯一需要注意的是 Delete —— 它会改输入框内容，按设计会被吞，因此不适合配成 ToggleKey。
            int[] usable = new int[]
            {
                282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, // F1..F12
                27,   // Escape
                19,   // Pause
                277, 278, 279, 280, 281, // Insert/Home/End/PageUp/PageDown
                319,  // Menu
            };
            for (int i = 0; i < usable.Length; i++)
            {
                Assert.That(InputKeys.IsTextInput(usable[i]), Is.False,
                    "键 " + usable[i] + " 作为 ToggleKey 时会被搜索框吞掉");
            }
            // 反面：Delete 确实在屏蔽集里，这是有意为之而非漏网
            Assert.That(InputKeys.IsTextInput(127), Is.True,
                "Delete(127) 会改输入框内容，按设计应被吞");
        }

        [Test]
        public void ControlAndNavigationKeys_AreNotTextInput()
        {
            // 逐组列出，失败信息能直接指出是哪一类被误纳入白名单
            AssertAllFalse("None", 0, 0);
            AssertAllFalse("Clear", 12, 12);
            AssertAllFalse("Pause", 19, 19);
            AssertAllFalse("Escape", 27, 27);
            AssertAllFalse("方向键", 273, 276);
            AssertAllFalse("Insert/Home/End/PageUp/PageDown", 277, 281);
            AssertAllFalse("Numlock/CapsLock/ScrollLock/Shift/Control/Alt/Meta/Windows/AltGr", 300, 313);
            AssertAllFalse("Help/Print/SysReq/Break/Menu", 315, 319);
        }

        [Test]
        public void MouseButtons_AreNotTextInput()
        {
            // Mouse0=323 .. Mouse6=329。鼠标滚轮（321/322）一并检查。
            AssertAllFalse("WheelUp/WheelDown", 321, 322);
            AssertAllFalse("Mouse0..Mouse6", 323, 329);
        }

        [Test]
        public void JoystickButtons_AreNotTextInput()
        {
            // JoystickButton0=330 起，一直到 669（670 开始是 F16..F24）。
            // 这是数量最多的一段（340 个值）；把它们误纳入白名单会让手柄玩家完全无法用 ToggleKey。
            AssertAllFalse("手柄按钮", 330, 669);
        }

        // ---------- 2. 会产生字符的键必须全部返回 true ----------

        [Test]
        public void Letters_AreTextInput()
        {
            // A=97 .. Z=122（KeyCode 只有小写字母成员，数值等于 ASCII 小写码）
            AssertAllTrue("字母 a-z", 97, 122);
        }

        [Test]
        public void MainKeyboardDigits_AreTextInput()
        {
            AssertAllTrue("Alpha0..Alpha9", 48, 57);
        }

        [Test]
        public void KeypadKeys_AreTextInput()
        {
            // Keypad0=256 .. Keypad9=265、KeypadPeriod=266、Divide/Multiply/Minus/Plus=267..270、
            // KeypadEnter=271、KeypadEquals=272。全部会改变输入框内容。
            AssertAllTrue("Keypad0..KeypadEquals", 256, 272);
        }

        [Test]
        public void PunctuationAndSymbols_AreTextInput()
        {
            // KeyCode 在 ASCII 可打印区的数值就是 ASCII 码，因此 32..64 与 91..126 连续成段。
            AssertAllTrue("Space 与标点(32..47)", 32, 47);
            AssertAllTrue("Colon..At(58..64)", 58, 64);
            AssertAllTrue("LeftBracket..BackQuote(91..96)", 91, 96);
            AssertAllTrue("LeftCurlyBracket..Tilde(123..126)", 123, 126);
        }

        [Test]
        public void EditingKeysThatChangeContent_AreTextInput()
        {
            // Backspace/Delete/Return/KeypadEnter/Tab 会改变输入框内容或提交/移交焦点，
            // 必须让给输入框。刻意**不含** Home/End/方向键/PageUp/PageDown ——
            // TMP_InputField 也消费它们，但那只移动光标、不改内容，纳入屏蔽集只会扩大误伤功能键的风险面。
            Assert.That(InputKeys.IsTextInput(8), Is.True, "Backspace(8)");
            Assert.That(InputKeys.IsTextInput(9), Is.True, "Tab(9)");
            Assert.That(InputKeys.IsTextInput(13), Is.True, "Return(13)");
            Assert.That(InputKeys.IsTextInput(127), Is.True, "Delete(127)");
            Assert.That(InputKeys.IsTextInput(271), Is.True, "KeypadEnter(271)");
        }

        [Test]
        public void CursorMovementKeysAreDeliberatelyNotInTheSwallowSet()
        {
            // 固化这条产品决策，避免有人"补全" TMP_InputField 消费的按键集合时把它们加进来 ——
            // 加进来不会立刻出错，但会让方向键/Home/End 无法用作 ToggleKey，
            // 且与 IsTextInput 注释里"宁可放行也不要误屏蔽"的取向相矛盾。
            int[] cursorKeys = new int[] { 273, 274, 275, 276, 278, 279, 280, 281 };
            for (int i = 0; i < cursorKeys.Length; i++)
            {
                Assert.That(InputKeys.IsTextInput(cursorKeys[i]), Is.False,
                    "键 " + cursorKeys[i] + " 只移动光标、不改内容，按设计不应纳入屏蔽集");
            }
        }

        // ---------- 3. 全域覆盖：0..678 的返回 true 集合必须与预期集合完全相等 ----------

        [Test]
        public void ExhaustiveScan_TrueSetEqualsExactlyTheExpectedCharacterProducingKeys()
        {
            // 用**集合相等**而不是逐个比较：只要有人往白名单里加一个功能键（或删掉一个字符键），
            // 差集会直接把它列出来。这是本文件的核心断言，前面那些分组断言只是为了让失败信息更好读。
            HashSet<int> expected = ExpectedTextInputKeys();

            HashSet<int> actual = new HashSet<int>();
            for (int k = 0; k <= MaxKeyCode; k++)
            {
                if (InputKeys.IsTextInput(k))
                {
                    actual.Add(k);
                }
            }

            List<int> unexpectedlyTrue = new List<int>();
            foreach (int k in actual)
            {
                if (!expected.Contains(k)) { unexpectedlyTrue.Add(k); }
            }
            List<int> unexpectedlyFalse = new List<int>();
            foreach (int k in expected)
            {
                if (!actual.Contains(k)) { unexpectedlyFalse.Add(k); }
            }
            unexpectedlyTrue.Sort();
            unexpectedlyFalse.Sort();

            Assert.That(unexpectedlyTrue, Is.Empty,
                "以下按键被新纳入屏蔽集（若其中含功能键，搜索框聚焦时该键将失效，F5 死锁即此类）: "
                + Join(unexpectedlyTrue));
            Assert.That(unexpectedlyFalse, Is.Empty,
                "以下会产生字符的按键不再被屏蔽（打字会误触 ToggleKey）: " + Join(unexpectedlyFalse));
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
        }

        [Test]
        public void ExhaustiveScan_IsTotalAndDeterministicOverTheWholeRange()
        {
            // 全域不抛异常 + 同一输入两次调用结果一致（纯函数，无隐藏状态）。
            // 前者保证不会有越界/空引用；后者防止有人引入缓存或静态可变状态。
            for (int k = 0; k <= MaxKeyCode; k++)
            {
                int captured = k;
                bool first = false;
                Assert.DoesNotThrow(delegate { first = InputKeys.IsTextInput(captured); },
                    "IsTextInput(" + captured + ") 抛异常");
                Assert.That(InputKeys.IsTextInput(captured), Is.EqualTo(first),
                    "IsTextInput(" + captured + ") 两次调用结果不一致（存在隐藏状态）");
            }
        }

        [Test]
        public void OutOfRangeValues_AreNotTextInput()
        {
            // 越界/非法数值（负数、远超 F24 的值）走 default 返回 false —— 与"宁可放行"一致。
            int[] outOfRange = new int[] { -1, -1000, 679, 1000, int.MaxValue, int.MinValue };
            for (int i = 0; i < outOfRange.Length; i++)
            {
                Assert.That(InputKeys.IsTextInput(outOfRange[i]), Is.False,
                    "越界值 " + outOfRange[i] + " 应返回 false");
            }
        }

        [Test]
        public void SwallowSetIsAStrictMinorityOfTheKeyCodeSpace()
        {
            // 量级保护：会产生字符的键只有 90 个左右，占 0..678 全域的一小部分。
            // 若有人把判定写成「除了少数几个键之外全部屏蔽」（这正是 2.2.0 回归的形态），
            // 这条断言会先于其他测试给出一个非常直观的失败信息。
            int trueCount = 0;
            for (int k = 0; k <= MaxKeyCode; k++)
            {
                if (InputKeys.IsTextInput(k)) { trueCount++; }
            }
            Assert.That(trueCount, Is.EqualTo(ExpectedTextInputKeys().Count));
            Assert.That(trueCount, Is.LessThan((MaxKeyCode + 1) / 4),
                "屏蔽集占了 KeyCode 全域的四分之一以上，判定很可能被写反成黑名单");
        }

        // ---------- 辅助 ----------

        /// <summary>
        /// 预期「会产生字符、应让给输入框」的 KeyCode 数值集合。
        ///
        /// 刻意用区间列举而不是复制 InputKeys 的实现结构（那样会变成同义反复）：
        /// KeyCode 在 ASCII 可打印区的数值就等于 ASCII 码，所以 32..64（Space、标点、数字、
        /// 冒号到 At）与 91..127（括号、字母、花括号、Tilde、Delete）天然连续成段，
        /// 加上小键盘区 256..272 与三个编辑键 8/9/13。
        /// </summary>
        private static HashSet<int> ExpectedTextInputKeys()
        {
            HashSet<int> set = new HashSet<int>();
            set.Add(8);   // Backspace
            set.Add(9);   // Tab
            set.Add(13);  // Return
            AddRange(set, 32, 64);    // Space..At（含 Alpha0..Alpha9）
            AddRange(set, 91, 127);   // LeftBracket..Delete（含 a..z）
            AddRange(set, 256, 272);  // Keypad0..KeypadEquals（含 KeypadEnter）
            return set;
        }

        private static void AddRange(HashSet<int> set, int from, int to)
        {
            for (int k = from; k <= to; k++)
            {
                set.Add(k);
            }
        }

        private static void AssertAllFalse(string groupName, int from, int to)
        {
            for (int k = from; k <= to; k++)
            {
                Assert.That(InputKeys.IsTextInput(k), Is.False,
                    groupName + " 组的键 " + k + " 被误判为文本输入键（该功能键将在搜索框聚焦时失效）");
            }
        }

        private static void AssertAllTrue(string groupName, int from, int to)
        {
            for (int k = from; k <= to; k++)
            {
                Assert.That(InputKeys.IsTextInput(k), Is.True,
                    groupName + " 组的键 " + k + " 未被判为文本输入键（打字时会误触 ToggleKey）");
            }
        }

        private static string Join(List<int> values)
        {
            string[] parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                parts[i] = values[i].ToString();
            }
            return string.Join(", ", parts);
        }
    }
}
