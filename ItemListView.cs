using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 物品列表视图：物品条目收集、本地化显示名解析、中文/拼音搜索、多标签分类过滤与排序。
    /// </summary>
    public class ItemListView : MonoBehaviour
    {
        public class Entry
        {
            public Item item;
            public string displayName; // 当前语言的本地化显示名
            public string enName;      // 英文显示名（用于英文关键词搜索）
            public string prefabName;  // 英文 prefab 名
            public string pinyin;      // 显示名的拼音全拼（无空格，小写）
            public ItemCategory tags;       // 多标签（Flags）
            public ItemCategory primary;    // 主分类（用于排序）
        }

        private Transform _content;
        private Transform _template;
        private TMP_InputField _searchInput;
        private List<Entry> _all = new List<Entry>();
        private MajorCategory _major = MajorCategory.All;
        private string _query = "";

        private TMP_FontAsset _fontLatin;
        private TMP_FontAsset _fontCjk;
        private Action<MajorCategory> _onMajorChanged;

        public void Init(Transform content, Transform template, TMP_InputField searchInput,
            Action<MajorCategory> onMajorChanged)
        {
            _content = content;
            _template = template;
            _searchInput = searchInput;
            _onMajorChanged = onMajorChanged;

            _fontLatin = FindFont("DarumaDropOne-Regular SDF");
            _fontCjk = GetGameBaseFont();

            BuildCatalog();

            if (_searchInput != null)
            {
                _searchInput.onValueChanged.AddListener(OnSearchChanged);
            }
            LocalizedText.OnLangugageChanged += OnLanguageChanged;

            RefreshFonts();
            Rebuild();
        }

        private void OnDestroy()
        {
            LocalizedText.OnLangugageChanged -= OnLanguageChanged;
        }

        /// <summary>停止监听（组件复用前调用）。</summary>
        public void Stop()
        {
            LocalizedText.OnLangugageChanged -= OnLanguageChanged;
        }

        public void SetMajor(MajorCategory major)
        {
            if (_major == major)
            {
                return;
            }
            _major = major;
            Rebuild();
            if (_onMajorChanged != null)
            {
                _onMajorChanged(major);
            }
        }

        public void SetQuery(string value)
        {
            _query = value ?? "";
            Rebuild();
        }

        private void OnSearchChanged(string value)
        {
            SetQuery(value);
        }

        private void OnLanguageChanged()
        {
            // 语言切换：重新解析显示名与拼音，刷新字体与列表
            foreach (Entry entry in _all)
            {
                entry.displayName = ResolveDisplayName(entry.item, entry.prefabName);
                entry.pinyin = ToPinyin(entry.displayName);
            }
            _all.Sort(CompareEntries);
            RefreshFonts();
            Rebuild();
        }

        private void BuildCatalog()
        {
            _all.Clear();
            ItemDatabase db = SingletonAsset<ItemDatabase>.Instance;
            if (db == null || db.Objects == null)
            {
                return;
            }
            foreach (Item item in db.Objects)
            {
                if (item == null)
                {
                    continue;
                }
                string prefab = item.gameObject.name;
                if (string.IsNullOrEmpty(prefab) || ItemCatalog.IsHidden(prefab))
                {
                    continue;
                }
                string display = ResolveDisplayName(item, prefab);
                ItemCategory tags;
                ItemCategory primary;
                ResolveCategories(item, prefab, out tags, out primary);
                _all.Add(new Entry
                {
                    item = item,
                    displayName = display,
                    enName = ResolveEnglishName(item, prefab),
                    prefabName = prefab,
                    pinyin = ToPinyin(display),
                    tags = tags,
                    primary = primary,
                });
            }
            _all.Sort(CompareEntries);
        }

        private static string ResolveDisplayName(Item item, string prefab)
        {
            // 1) 补充本地化 key 映射（UIData.itemName 与游戏本地化表不对应时，如 FireWood->棍子）
            string extraKey;
            if (ItemCatalog.ExtraNameKeys.TryGetValue(prefab, out extraKey))
            {
                string text = LocalizedText.GetText(extraKey, false);
                if (!string.IsNullOrEmpty(text) && !text.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
            // 2) 补充自定义名称（游戏本地化表完全没有的物品，如太空篮球/风之杖）
            string[] custom;
            if (ItemCatalog.ExtraCustomNames.TryGetValue(prefab, out custom) && custom != null && custom.Length >= 1)
            {
                if (IsChineseLanguage())
                {
                    return custom[0];
                }
                return (custom.Length >= 2 && !string.IsNullOrEmpty(custom[1])) ? custom[1] : custom[0];
            }
            // 3) 游戏本地化名（基于 Item.UIData.itemName 的动态解析，兼容所有版本物品与语言）
            if (item != null)
            {
                string localized = item.GetName();
                if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
                {
                    return localized;
                }
            }
            // 4) UIData.itemName 兜底
            if (item != null && item.UIData != null && !string.IsNullOrEmpty(item.UIData.itemName))
            {
                return item.UIData.itemName;
            }
            // 5) prefab 名兜底
            return prefab;
        }

        private static string ResolveEnglishName(Item item, string prefab)
        {
            if (item != null && item.UIData != null && !string.IsNullOrEmpty(item.UIData.itemName))
            {
                return item.UIData.itemName;
            }
            return prefab;
        }

        /// <summary>
        /// 解析物品的多标签分类与主分类。
        /// 1) 静态表（基于游戏交互提示/组件/显示名设计的 148 个已知物品）
        /// 2) 运行时组件/ItemTags 兜底（兼容模组新增物品）
        /// 3) prefab 名特征
        /// </summary>
        private static void ResolveCategories(Item item, string prefab, out ItemCategory tags, out ItemCategory primary)
        {
            tags = ItemCategory.None;
            primary = ItemCategory.Props;

            // 1) 静态表
            if (ItemCatalog.ItemTagMap.TryGetValue(prefab, out tags))
            {
                if (!ItemCatalog.ItemPrimaryMap.TryGetValue(prefab, out primary))
                {
                    primary = ItemCatalog.PrimaryOfTags(tags);
                }
                return;
            }

            // 2) 运行时组件/标签兜底
            if (item != null)
            {
                Item.ItemTags itags = item.itemTags;
                if ((itags & Item.ItemTags.Mystical) != 0
                    || (itags & Item.ItemTags.GoldenIdol) != 0
                    || (itags & Item.ItemTags.BookOfBones) != 0
                    || (itags & Item.ItemTags.ScoutAmulet) != 0)
                {
                    tags |= ItemCategory.Mystical;
                }
                if ((itags & Item.ItemTags.PackagedFood) != 0
                    || (itags & Item.ItemTags.Berry) != 0
                    || (itags & Item.ItemTags.Mushroom) != 0)
                {
                    tags |= ItemCategory.Food;
                }
                if (item.GetComponent<ItemCooking>() != null || item.GetComponent<Action_Consume>() != null)
                {
                    tags |= ItemCategory.Food;
                }
            }

            // 3) prefab 名特征
            if (prefab.IndexOf("Shroom", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Mushroom", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Berry", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tags |= ItemCategory.Food;
            }
            if (prefab.IndexOf("Rope", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Spike", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Spool", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Cannon", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Shooter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tags |= ItemCategory.Tools;
            }

            // 兜底：未知物品归入"场景"
            if (tags == ItemCategory.None)
            {
                tags = ItemCategory.Props;
            }
            primary = ItemCatalog.PrimaryOfTags(tags);
        }

        private static int CompareEntries(Entry a, Entry b)
        {
            int c = a.primary.CompareTo(b.primary);
            if (c != 0)
            {
                return c;
            }
            return string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshFonts()
        {
            TMP_FontAsset font = IsChineseLanguage() ? _fontCjk : _fontLatin;
            if (_searchInput != null)
            {
                if (_searchInput.textComponent != null)
                {
                    _searchInput.textComponent.font = font;
                }
                TMP_Text placeholder = _searchInput.placeholder as TMP_Text;
                if (placeholder != null)
                {
                    placeholder.font = font;
                }
            }
        }

        private void Rebuild()
        {
            if (_content == null || _template == null)
            {
                return;
            }
            // 清理现有条目（保留模板）
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Transform child = _content.GetChild(i);
                if (child == _template)
                {
                    continue;
                }
                Destroy(child.gameObject);
            }

            TMP_FontAsset font = IsChineseLanguage() ? _fontCjk : _fontLatin;
            string query = (_query == null) ? "" : _query.Trim().ToLowerInvariant();
            string queryNoSpace = query.Replace(" ", "");

            foreach (Entry entry in _all)
            {
                if (!Matches(entry, query, queryNoSpace))
                {
                    continue;
                }
                Transform clone = Instantiate(_template, _content);
                clone.SetAsLastSibling();
                clone.gameObject.name = entry.prefabName;

                Transform nameTrans = clone.Find("ItemName");
                if (nameTrans != null)
                {
                    TextMeshProUGUI nameText = nameTrans.GetComponent<TextMeshProUGUI>();
                    if (nameText != null)
                    {
                        nameText.text = entry.displayName;
                        nameText.font = font;
                    }
                }
                Transform iconTrans = clone.Find("ItemIcon");
                if (iconTrans != null)
                {
                    RawImage icon = iconTrans.GetComponent<RawImage>();
                    if (icon != null && entry.item.UIData != null)
                    {
                        icon.texture = entry.item.UIData.icon;
                    }
                }
                Button button = clone.GetComponent<Button>();
                if (button != null)
                {
                    Item captured = entry.item;
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => SpawnItem(captured));
                }
                clone.gameObject.SetActive(true);
            }

            // 隐藏模板本身（原模组把模板留在列表中，这里隐藏以获得干净列表）
            _template.gameObject.SetActive(false);
        }

        private bool Matches(Entry entry, string query, string queryNoSpace)
        {
            if (!ItemCatalog.IsInMajor(entry.tags, _major))
            {
                return false;
            }
            if (query.Length == 0)
            {
                return true;
            }
            if (entry.displayName != null && entry.displayName.ToLowerInvariant().Contains(query))
            {
                return true;
            }
            if (entry.enName != null && entry.enName.ToLowerInvariant().Contains(query))
            {
                return true;
            }
            if (entry.prefabName != null && entry.prefabName.ToLowerInvariant().Contains(query))
            {
                return true;
            }
            if (entry.pinyin != null && entry.pinyin.Contains(queryNoSpace))
            {
                return true;
            }
            return false;
        }

        private void SpawnItem(Item item)
        {
            if (item == null)
            {
                return;
            }
            try
            {
                ItemSpawner.Plugin.Spawn(item);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawner Enhancement failed to spawn " + item.gameObject.name + ": " + ex);
            }
        }

        /// <summary>将字符串中的汉字转成拼音全拼（其余字母数字保留），用于拼音搜索。</summary>
        public static string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch >= 0x4e00 && ch <= 0x9fff)
                {
                    string pinyin;
                    if (ItemCatalog.PinyinMap.TryGetValue(ch, out pinyin))
                    {
                        sb.Append(pinyin);
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }
            return sb.ToString();
        }

        public static bool IsChineseLanguage()
        {
            LocalizedText.Language language = LocalizedText.CURRENT_LANGUAGE;
            return language == LocalizedText.Language.SimplifiedChinese
                || language == LocalizedText.Language.TraditionalChinese;
        }

        public static TMP_FontAsset FindFont(string name)
        {
            TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all == null)
            {
                return null;
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                {
                    return all[i];
                }
            }
            return null;
        }

        /// <summary>获取游戏主 UI 字体（带中文 fallback，用于中文显示）。</summary>
        public static TMP_FontAsset GetGameBaseFont()
        {
            if (FontFallbackSwapper.instance != null && FontFallbackSwapper.instance.mainBaseFont != null)
            {
                return FontFallbackSwapper.instance.mainBaseFont;
            }
            TMP_FontAsset muli = FindFont("Muli");
            if (muli != null)
            {
                return muli;
            }
            return FindFont("LiberationSans SDF");
        }
    }
}
