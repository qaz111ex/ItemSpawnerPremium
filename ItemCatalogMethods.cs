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
                    return true;
            }
        }

        /// <summary>从标签集合中取主分类（位值最小者，即排序优先级最高）。</summary>
        public static ItemCategory PrimaryOfTags(ItemCategory tags)
        {
            ItemCategory[] order = new ItemCategory[]
            {
                ItemCategory.Tools, ItemCategory.Food, ItemCategory.Mystical,
                ItemCategory.Equipment, ItemCategory.Consumables, ItemCategory.Props,
            };
            for (int i = 0; i < order.Length; i++)
            {
                if ((tags & order[i]) != 0)
                {
                    return order[i];
                }
            }
            return ItemCategory.Props;
        }
    }
}
