namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 搜索排名打分（纯逻辑，零 Unity 依赖）。
    ///
    /// 从 ItemListView.Score 抽出：原实现同时承担「分类/收藏过滤」与「文本匹配打分」两件事，
    /// 且依赖 MonoBehaviour 实例字段（_major / _favoritesOnly），无法在没有 Unity 运行时的环境下测试。
    /// 这里只保留纯文本匹配部分；过滤仍留在 ItemListView（它需要读 Plugin.Favorites 与当前分类）。
    ///
    /// 分档设计（数值即优先级，越大越靠前）：
    ///   当前语言显示名 精确 1000 / 前缀 900 / 包含 600
    ///   拼音全拼（仅中文界面）前缀 850 / 包含 550
    ///   拼音首字母（仅中文界面）前缀 750 / 包含 500
    ///   英文名 精确 700 / 前缀 500 / 包含 300
    ///   prefab 名 精确 650 / 前缀 450 / 包含 250
    /// 拼音前缀（850）刻意高于英文名前缀（500）与显示名包含（600）：中文玩家打拼音时，
    /// 应当先看到「拼音开头就对上」的物品，而不是恰好含该字母序列的英文名。
    /// </summary>
    internal static class SearchRanking
    {
        /// <summary>空 query 时的基准分（表示「匹配，但无排序偏好」）。</summary>
        public const int MatchAllScore = 1;

        /// <summary>
        /// 对单个条目的文本字段打分。返回 0 表示不匹配。
        /// </summary>
        /// <param name="displayNameLower">当前语言显示名（已 ToLowerInvariant）</param>
        /// <param name="enNameLower">英文显示名（已 ToLowerInvariant）</param>
        /// <param name="prefabNameLower">prefab 名（已 ToLowerInvariant）</param>
        /// <param name="pinyin">显示名的拼音全拼（已归一化，小写无空格）</param>
        /// <param name="pinyinInitials">显示名的拼音首字母（已归一化，小写无空格）</param>
        /// <param name="query">搜索词（已 Trim + ToLowerInvariant）</param>
        /// <param name="queryNoSpace">搜索词的归一化形式（去掉非字母数字），与 pinyin 字段同口径</param>
        /// <param name="chinese">当前是否为简/繁中文界面（决定拼音档是否参与）</param>
        public static int Score(
            string displayNameLower,
            string enNameLower,
            string prefabNameLower,
            string pinyin,
            string pinyinInitials,
            string query,
            string queryNoSpace,
            bool chinese)
        {
            if (string.IsNullOrEmpty(query))
            {
                return MatchAllScore;
            }

            // 下面所有 StartsWith/IndexOf 一律显式指定 StringComparison.Ordinal：
            // .NET Framework 中单参 string.StartsWith(string) 默认是 CurrentCulture（走 CompareInfo.IsPrefix，
            // 比 ordinal 慢一个量级），而归一化用的是 ToLowerInvariant/StripNonAlnum ——
            // 两套规则不一致，土耳其语等区域下 i/I/İ/ı 的折叠差异会造成漏匹配。
            // 字符串相等运算符本身已是 ordinal，无需额外指定。

            // 1. 当前语言显示名（最高优先级）
            if (displayNameLower != null)
            {
                if (displayNameLower == query) { return 1000; }
                if (displayNameLower.StartsWith(query, System.StringComparison.Ordinal)) { return 900; }
                if (displayNameLower.IndexOf(query, System.StringComparison.Ordinal) >= 0) { return 600; }
            }

            // 2. 拼音（仅中文界面参与，避免英文查询被拼音噪声干扰）
            if (chinese && !string.IsNullOrEmpty(queryNoSpace))
            {
                if (pinyin != null)
                {
                    if (pinyin.StartsWith(queryNoSpace, System.StringComparison.Ordinal)) { return 850; }
                    if (pinyin.IndexOf(queryNoSpace, System.StringComparison.Ordinal) >= 0) { return 550; }
                }
                if (pinyinInitials != null)
                {
                    if (pinyinInitials.StartsWith(queryNoSpace, System.StringComparison.Ordinal)) { return 750; }
                    if (pinyinInitials.IndexOf(queryNoSpace, System.StringComparison.Ordinal) >= 0) { return 500; }
                }
            }

            // 3. 英文名
            if (enNameLower != null)
            {
                if (enNameLower == query) { return 700; }
                if (enNameLower.StartsWith(query, System.StringComparison.Ordinal)) { return 500; }
                if (enNameLower.IndexOf(query, System.StringComparison.Ordinal) >= 0) { return 300; }
            }

            // 4. prefab 名
            if (prefabNameLower != null)
            {
                if (prefabNameLower == query) { return 650; }
                if (prefabNameLower.StartsWith(query, System.StringComparison.Ordinal)) { return 450; }
                if (prefabNameLower.IndexOf(query, System.StringComparison.Ordinal) >= 0) { return 250; }
            }

            return 0;
        }

        /// <summary>
        /// 目录排序比较：主分类升序 → 分组名 → 显示名（忽略大小写）→ prefab 名（Ordinal）。
        ///
        /// 分组名（groupName）由 <see cref="ItemGrouping.ComputeGroupNames"/> 算好：
        /// 同族成员共用一个值（族内最小的显示名），未登记家族的条目等于自身显示名。
        /// 于是同族物品挤在一起，整族落在它最靠前那个成员本该在的位置；
        /// 未分组的物品行为与加分组前完全一致（第二级等于第三级，不产生额外区分）。
        ///
        /// prefab 名作为最终 tiebreaker 使比较器成为**全序**。这不是可选优化：可见物品里存在
        /// 多组同显示名的条目（救援抓钩×2、煎蛋×2、黄雪莓×2、莓蕉皮×4、传送罗盘×3、热狗肠×2、
        /// 阳伞×2、童军饼干×2、三组毒/非毒同名蘑菇），若比较器只到显示名一级，
        /// List.Sort（不稳定）会让这些同名条目相对顺序在每次排序后抖动，表现为切换语言时网格位置互换。
        /// </summary>
        public static int CompareCatalog(
            ItemCategory aPrimary, string aGroupName, string aDisplayName, string aPrefabName,
            ItemCategory bPrimary, string bGroupName, string bDisplayName, string bPrefabName)
        {
            int byPrimary = aPrimary.CompareTo(bPrimary);
            if (byPrimary != 0)
            {
                return byPrimary;
            }
            int byGroup = string.Compare(aGroupName, bGroupName, System.StringComparison.OrdinalIgnoreCase);
            if (byGroup != 0)
            {
                return byGroup;
            }
            int byDisplay = string.Compare(aDisplayName, bDisplayName, System.StringComparison.OrdinalIgnoreCase);
            if (byDisplay != 0)
            {
                return byDisplay;
            }
            return string.Compare(aPrefabName, bPrefabName, System.StringComparison.Ordinal);
        }
    }
}
