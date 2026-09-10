using System;
using System.Collections.Generic;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 家族分组排序名计算（纯逻辑，零 Unity 依赖）。
    ///
    /// 为什么要分组：目录默认按显示名排序，而中文名常常是「共享后缀、前缀不同」
    /// —— 黑葚莓 / 红葚莓 / 黄葚莓 / 青葚莓 按码点排出来是 红 · 青 · 黄 · 黑，
    /// 中间会插进几十个别的物品。玩家想找"那种莓"得满屏翻。
    ///
    /// 做法：同族成员共用一个**排序名**（族内最小的显示名），于是整族挤在一起，
    /// 且整族落在"它最靠前那个成员"本该在的位置 —— 位置仍符合按名字找的直觉。
    /// 未登记家族的物品用自己的显示名，行为与分组前完全一致。
    ///
    /// 与 <see cref="SearchRanking.CompareCatalog"/> 的分工：本类只负责算出排序名，
    /// 比较逻辑在 CompareCatalog；两者都是纯函数，便于单元测试。
    /// </summary>
    internal static class ItemGrouping
    {
        /// <summary>
        /// 计算每个条目的分组排序名。
        /// </summary>
        /// <param name="familyKeys">
        /// 与 <paramref name="displayNames"/> 等长；未登记家族的条目传 null 或空串。
        /// </param>
        /// <param name="displayNames">当前语言的显示名（同一数组内可以有重复）。</param>
        /// <returns>与输入等长的排序名数组。</returns>
        public static string[] ComputeGroupNames(string[] familyKeys, string[] displayNames)
        {
            if (familyKeys == null)
            {
                throw new ArgumentNullException("familyKeys");
            }
            if (displayNames == null)
            {
                throw new ArgumentNullException("displayNames");
            }
            if (familyKeys.Length != displayNames.Length)
            {
                throw new ArgumentException(
                    "familyKeys 与 displayNames 长度必须一致（分组名按同一下标对齐）");
            }

            // 第一遍：族 -> 族内最小的显示名。用与排序器一致的 OrdinalIgnoreCase 比较，
            // 否则"哪个成员最小"与"整族的落位"可能不一致（例如仅大小写不同的两个名字）。
            Dictionary<string, string> familyMin = null;
            for (int i = 0; i < familyKeys.Length; i++)
            {
                string family = Normalize(familyKeys[i]);
                if (family == null)
                {
                    continue;
                }
                string name = displayNames[i] ?? "";
                if (familyMin == null)
                {
                    familyMin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                string current;
                if (!familyMin.TryGetValue(family, out current)
                    || string.Compare(name, current, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    familyMin[family] = name;
                }
            }

            // 第二遍：族内成员取族的最小名；未登记的用自己的显示名。
            string[] result = new string[displayNames.Length];
            for (int i = 0; i < displayNames.Length; i++)
            {
                string family = Normalize(familyKeys[i]);
                string min;
                if (family != null && familyMin != null && familyMin.TryGetValue(family, out min))
                {
                    result[i] = min;
                }
                else
                {
                    result[i] = displayNames[i] ?? "";
                }
            }
            return result;
        }

        /// <summary>家族 key 归一化：null / 空白视为"未登记"（返回 null）。</summary>
        private static string Normalize(string family)
        {
            return string.IsNullOrEmpty(family) || family.Trim().Length == 0 ? null : family;
        }
    }
}
