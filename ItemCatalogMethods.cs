using System;

namespace ItemSpawnerEnhancement
{
    public static partial class ItemCatalog
    {
        /// <summary>判断物品 prefab 是否应隐藏（装饰物、变体、测试物品等）。</summary>
        public static bool IsHidden(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return true;
            }
            if (HiddenExact.Contains(prefab))
            {
                return true;
            }
            for (int i = 0; i < HiddenPrefixes.Length; i++)
            {
                if (prefab.StartsWith(HiddenPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            for (int j = 0; j < HiddenSubstrings.Length; j++)
            {
                if (prefab.IndexOf(HiddenSubstrings[j], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>一级分类按钮标签（按当前语言本地化）。</summary>
        public static string GetMajorLabel(MajorCategory major)
        {
            switch (major)
            {
                case MajorCategory.All: return Loc.Get("catAll");
                case MajorCategory.Tools: return Loc.Get("catTools");
                case MajorCategory.Food: return Loc.Get("catFood");
                case MajorCategory.Mystical: return Loc.Get("catMystical");
                case MajorCategory.Equipment: return Loc.Get("catEquipment");
                case MajorCategory.Consumables: return Loc.Get("catConsumables");
                case MajorCategory.Props: return Loc.Get("catProps");
                default: return "?";
            }
        }

        /// <summary>判断某个标签是否属于某个一级分类（All 恒真）。</summary>
        public static bool IsInMajor(ItemCategory tags, MajorCategory major)
        {
            switch (major)
            {
                case MajorCategory.All:
                    // 防御性判断：ResolveCategories 保证 tags 至少为 Props（末尾兜底），
                    // 静态表 153 条也没有 None，所以此处实际恒真；保留以防未来新增构造路径漏兜底。
                    return tags != ItemCategory.None;
                case MajorCategory.Tools:
                    return (tags & ItemCategory.Tools) != 0;
                case MajorCategory.Food:
                    return (tags & ItemCategory.Food) != 0;
                case MajorCategory.Mystical:
                    return (tags & ItemCategory.Mystical) != 0;
                case MajorCategory.Equipment:
                    return (tags & ItemCategory.Equipment) != 0;
                case MajorCategory.Consumables:
                    return (tags & ItemCategory.Consumables) != 0;
                case MajorCategory.Props:
                    return (tags & ItemCategory.Props) != 0;
                default:
                    // 未知/非法 MajorCategory（如强转的哨兵值）返回 false 而非 true：
                    // 「一个都不匹配」会立刻表现为空列表，比「全部通过」这种看似正常的静默错误更容易被发现。
                    // 现有调用点传入的都是 0..6 的合法枚举值，因此本分支不影响既有行为。
                    return false;
            }
        }

        /// <summary>
        /// 主分类推导顺序（位值从小到大，即排序优先级从高到低）。
        /// 提为静态只读字段：PrimaryOfTags 在目录构建/语言切换时对每个物品调用，
        /// 每次 new 数组是纯粹的无谓分配。
        /// </summary>
        private static readonly ItemCategory[] PrimaryOrder = new ItemCategory[]
        {
            ItemCategory.Tools, ItemCategory.Food, ItemCategory.Mystical,
            ItemCategory.Equipment, ItemCategory.Consumables, ItemCategory.Props,
        };

        /// <summary>从标签集合中取主分类（位值最小者，即排序优先级最高）。</summary>
        public static ItemCategory PrimaryOfTags(ItemCategory tags)
        {
            for (int i = 0; i < PrimaryOrder.Length; i++)
            {
                if ((tags & PrimaryOrder[i]) != 0)
                {
                    return PrimaryOrder[i];
                }
            }
            return ItemCategory.Props;
        }
    }
}
