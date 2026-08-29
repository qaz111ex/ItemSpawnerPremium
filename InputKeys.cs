namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 按键分类判定（纯逻辑，零 Unity 依赖）。
    ///
    /// 为什么参数是 int 而不是 KeyCode：KeyCode 定义在 UnityEngine.CoreModule，
    /// 测试工程不能引用它（会拖进整个 Unity 运行时）。把判定体搬到这里并以底层数值为参数，
    /// tests\ 下就能对 0..678 全域穷举断言 —— 而这正是 2.2.0 那个 F5 死锁回归的所在位置：
    /// 当时把「防误吞文本键」写成了「屏蔽全部按键」，配合"打开即聚焦搜索框"导致 F5 永远收不到。
    /// 调用方 <see cref="Plugin"/> 只做 `(int)keyCode` 强转。
    ///
    /// KeyCode 的底层数值来自 UnityEngine.CoreModule 的枚举定义，稳定且已逐一核实：
    ///   Backspace=8  Tab=9  Return=13  Escape=27  Space=32
    ///   Alpha0=48 .. Alpha9=57      A=97 .. Z=122
    ///   Delete=127   Keypad0=256 .. KeypadEquals=272
    ///   UpArrow=273 DownArrow=274 RightArrow=275 LeftArrow=276
    ///   Insert=277 Home=278 End=279 PageUp=280 PageDown=281
    ///   F1=282 .. F15=296          F16=670 .. F24=678
    ///   修饰键 300..313，鼠标键 323..329，手柄按钮 330+
    /// </summary>
    internal static class InputKeys
    {
        // 字母 A-Z
        private const int A = 97;
        private const int Z = 122;
        // 主键盘数字 0-9
        private const int Alpha0 = 48;
        private const int Alpha9 = 57;
        // 小键盘数字与运算符（Keypad0 .. KeypadEquals，均会输入字符）
        private const int Keypad0 = 256;
        private const int KeypadEquals = 272;

        /// <summary>
        /// 该按键在文本输入框聚焦时是否会被当作文本吃掉（即需要让给输入框的按键）。
        ///
        /// 只有这类按键才需要在搜索框聚焦时屏蔽 ToggleKey 轮询；F5/F1-F24、方向键、
        /// 修饰键、Escape 等功能键不参与文本输入，必须放行，否则默认 F5 会因为
        /// 「打开即聚焦搜索框」而永远无法关闭面板。
        ///
        /// 判定用白名单（列出会产生字符的键）而非黑名单：KeyCode 有 300+ 个值（含手柄按钮、
        /// 鼠标键），漏列一个功能键就会复现上面的死锁，而漏列一个字符键只是恢复到"打字误关面板"
        /// 这一较轻的问题。宁可放行也不要误屏蔽。
        /// </summary>
        public static bool IsTextInput(int keyCode)
        {
            if (keyCode >= A && keyCode <= Z)
            {
                return true;
            }
            if (keyCode >= Alpha0 && keyCode <= Alpha9)
            {
                return true;
            }
            if (keyCode >= Keypad0 && keyCode <= KeypadEquals)
            {
                return true;
            }
            switch (keyCode)
            {
                // 标点与符号键（ASCII 可打印区，按 KeyCode 的数值即 ASCII 码）
                case 32:  // Space
                case 33:  // Exclaim
                case 34:  // DoubleQuote
                case 35:  // Hash
                case 36:  // Dollar
                case 37:  // Percent
                case 38:  // Ampersand
                case 39:  // Quote
                case 40:  // LeftParen
                case 41:  // RightParen
                case 42:  // Asterisk
                case 43:  // Plus
                case 44:  // Comma
                case 45:  // Minus
                case 46:  // Period
                case 47:  // Slash
                case 58:  // Colon
                case 59:  // Semicolon
                case 60:  // Less
                case 61:  // Equals
                case 62:  // Greater
                case 63:  // Question
                case 64:  // At
                case 91:  // LeftBracket
                case 92:  // Backslash
                case 93:  // RightBracket
                case 94:  // Caret
                case 95:  // Underscore
                case 96:  // BackQuote
                case 123: // LeftCurlyBracket
                case 124: // Pipe
                case 125: // RightCurlyBracket
                case 126: // Tilde
                // 编辑键：会改变输入框内容，同样应让给输入框。
                // 注意刻意**不含** Home/End/方向键/PageUp/PageDown —— TMP_InputField 确实也会
                // 消费它们（KeyPressed 的 switch 里 return EditState.Continue），但那只移动光标/选区、
                // 不改内容；而把它们纳入屏蔽集会扩大"误屏蔽功能键"的风险面，
                // 与上面白名单取向（宁可放行）一致。
                case 8:   // Backspace
                case 127: // Delete
                case 13:  // Return
                case 271: // KeypadEnter
                case 9:   // Tab
                    return true;
                default:
                    return false;
            }
        }
    }
}
