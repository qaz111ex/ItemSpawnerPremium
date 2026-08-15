using System;

namespace ItemSpawnerEnhancement
{
    public static partial class ItemCatalog
    {
        /// <summary>判断物品 prefab 是否应隐藏（装饰物、变体、未使用等）。</summary>
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

        /// <summary>一级分类标签（按当前语言返回中/英文）。</summary>
        public static string GetMajorLabel(MajorCategory major)
        {
            bool zh = ItemListView.IsChineseLanguage();
            switch (major)
            {
                case MajorCategory.All: return zh ? "全部" : "All";
                case MajorCategory.Food: return zh ? "食物" : "Food";
                case MajorCategory.Tools: return zh ? "工具" : "Tools";
                case MajorCategory.Weapon: return zh ? "武器" : "Weapon";
                case MajorCategory.Mystical: return zh ? "神秘" : "Mystical";
                case MajorCategory.Equipment: return zh ? "装备" : "Equipment";
                case MajorCategory.Consumables: return zh ? "消耗品" : "Consumables";
                case MajorCategory.Misc: return zh ? "其他" : "Misc";
                default: return "?";
            }
        }
    }
}
