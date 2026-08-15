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

        /// <summary>一级分类按钮标签（按当前语言返回中/英文）。</summary>
        public static string GetMajorLabel(MajorCategory major)
        {
            bool zh = ItemListView.IsChineseLanguage();
            switch (major)
            {
                case MajorCategory.All: return zh ? "全部" : "All";
                case MajorCategory.Tools: return zh ? "工具" : "Tools";
                case MajorCategory.Food: return zh ? "食物" : "Food";
                case MajorCategory.Mystical: return zh ? "神秘" : "Mystical";
                case MajorCategory.Equipment: return zh ? "装备" : "Equipment";
                case MajorCategory.Consumables: return zh ? "消耗品" : "Consumables";
                case MajorCategory.Props: return zh ? "场景" : "Props";
                default: return "?";
            }
        }

        /// <summary>二级标签 -> 一级分类（用于过滤按钮）。</summary>
        public static MajorCategory GetMajorOfTag(ItemCategory tag)
        {
            switch (tag)
            {
                case ItemCategory.Tools: return MajorCategory.Tools;
                case ItemCategory.Food: return MajorCategory.Food;
                case ItemCategory.Mystical: return MajorCategory.Mystical;
                case ItemCategory.Equipment: return MajorCategory.Equipment;
                case ItemCategory.Consumables: return MajorCategory.Consumables;
                case ItemCategory.Props: return MajorCategory.Props;
                default: return MajorCategory.All;
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
