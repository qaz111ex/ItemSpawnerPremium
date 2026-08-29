using System.Text;
using TinyPinyin;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 搜索文本归一化（纯逻辑，零 Unity 依赖）。
    ///
    /// 单独成文件的理由：这几个函数是搜索正确性的核心，但原先内嵌在 ItemListView（MonoBehaviour）里，
    /// 导致无法在没有 Unity 运行时的环境下单元测试。抽出后 tests\ 下的 NUnit 工程可直接以源码链接方式
    /// 引用本文件（见 tests\ItemSpawnerPremium.Tests\*.csproj 的 Compile Include），实现真正的回归保护。
    ///
    /// 归一化口径（三者必须一致，否则拼音字段与 query 对不上导致漏匹配）：
    /// 只保留字母数字（含汉字，因为 char.IsLetterOrDigit 对汉字返回 true）并转小写（Invariant）。
    /// </summary>
    internal static class SearchText
    {
        /// <summary>过滤非字母数字字符并转小写（拼音全拼/首字母/搜索 query 共用的归一化逻辑）。</summary>
        public static string FilterAlnumLower(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }
            return sb.ToString();
        }

        /// <summary>去掉所有非字母数字字符（小写化后），用于与 pinyin/pinyinInitials 字段对齐匹配。</summary>
        public static string StripNonAlnum(string text)
        {
            return FilterAlnumLower(text);
        }

        /// <summary>将字符串中的汉字转成拼音全拼（其余字母数字保留，小写无空格），用于拼音搜索。</summary>
        public static string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            // 分隔符传空串 → 全拼直接相连；非汉字被 TinyPinyin 原样保留，再由 FilterAlnumLower 收敛
            return FilterAlnumLower(PinyinHelper.GetPinyin(text, ""));
        }

        /// <summary>将字符串中的汉字转成拼音首字母（其余字母数字保留，小写无空格），用于首字母搜索。</summary>
        public static string ToPinyinInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return FilterAlnumLower(PinyinHelper.GetPinyinInitials(text, ""));
        }
    }
}
