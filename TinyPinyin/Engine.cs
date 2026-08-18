using System;
using System.Text;
using TinyPinyin.Data;

namespace TinyPinyin
{
    public static class Engine
    {
        /// <summary>
        /// 获取文本拼音（仅按单字转换；多音字按字典默认读音输出，如“重”→ZHONG，
        /// 词级消歧（如“重庆”→CHONGQING）不受支持）。
        /// </summary>
        /// <param name="inputStr"></param>
        /// <param name="separator"></param>
        public static string ToPinyin(string inputStr, string separator)
        {
            if (inputStr == null || inputStr.Length == 0)
            {
                return inputStr;
            }

            var builder1 = new StringBuilder();
            for (int i = 0; i < inputStr.Length; i++)
            {
                builder1.Append(GetPinyinByChar(inputStr[i]));
                if (i != inputStr.Length - 1)
                {
                    builder1.Append(separator);
                }
            }
            return builder1.ToString();
        }

        private static int GetPinyinCode(char c)
        {
            int offset = c - PinyinData.MIN_VALUE;
            if (0 <= offset && offset < PinyinData.PINYIN_CODE_1_OFFSET)
            {
                // 前 7000 个汉字
                return decodeIndex(PinyinCode1.PINYIN_CODE_PADDING, PinyinCode1.PINYIN_CODE, offset);
            }
            else if (PinyinData.PINYIN_CODE_1_OFFSET <= offset && offset < PinyinData.PINYIN_CODE_2_OFFSET)
            {
                // 7000 ~ 14000 个汉字
                return decodeIndex(PinyinCode2.PINYIN_CODE_PADDING, PinyinCode2.PINYIN_CODE, offset - PinyinData.PINYIN_CODE_1_OFFSET);
            }
            else
            {
                // 14000 之后的汉字
                return decodeIndex(PinyinCode3.PINYIN_CODE_PADDING, PinyinCode3.PINYIN_CODE, offset - PinyinData.PINYIN_CODE_2_OFFSET);
            }
        }

        private static short decodeIndex(byte[] paddings, byte[] indexes, int offset)
        {
            //CHECKSTYLE:OFF
            int index1 = offset / 8;
            int index2 = offset % 8;
            short realIndex;
            realIndex = (short)(indexes[offset] & 0xff);
            //CHECKSTYLE:ON
            if ((paddings[index1] & PinyinData.BIT_MASKS[index2]) != 0)
            {
                realIndex = (short)(realIndex | PinyinData.PADDING_MASK);
            }
            return realIndex;
        }

        /// <summary>
        /// 判断是否是汉字
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public static bool IsChinese(char c)
        {
            return (PinyinData.MIN_VALUE <= c && c <= PinyinData.MAX_VALUE && GetPinyinCode(c) > 0) || PinyinData.CHAR_12295 == c;
        }

        /// <summary>
        /// 获取单个汉字的完整拼音
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public static string GetPinyinByChar(char c)
        {
            if (IsChinese(c))
            {
                if (c == PinyinData.CHAR_12295)
                {
                    return PinyinData.PINYIN_12295;
                }
                else
                {
                    return PinyinData.PINYIN_TABLE[GetPinyinCode(c)];
                }
            }
            else
            {
                return c.ToString();
            }
        }
    }
}
