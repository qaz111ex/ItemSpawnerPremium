using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Photon.Pun;
using TinyPinyin;
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
            public string pinyinInitials; // 显示名的拼音首字母（小写无空格）
            public ItemCategory tags;       // 多标签（Flags）
            public ItemCategory primary;    // 主分类（用于排序）
        }

        /// <summary>Init 是否已成功执行（成功末尾置 true）。判断"已成功 Setup"的可靠依据，不依赖 Destroy 延迟语义。</summary>
        [NonSerialized] public bool Initialized;

        private Transform _content;
        private Transform _template;
        private TMP_InputField _searchInput;
        private List<Entry> _all = new List<Entry>();
        private MajorCategory _major = MajorCategory.All;
        private string _query = "";
        private bool _favoritesOnly;

        private TMP_FontAsset _fontLatin;
        private TMP_FontAsset _fontCjk;
        private Action<MajorCategory> _onMajorChanged;
        private bool _subscribedInput;
        private bool _subscribedLanguage;

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

            if (_searchInput != null && !_subscribedInput)
            {
                _searchInput.onValueChanged.AddListener(OnSearchChanged);
                _subscribedInput = true;
            }
            if (!_subscribedLanguage)
            {
                LocalizedText.OnLangugageChanged += OnLanguageChanged;
                _subscribedLanguage = true;
            }

            RefreshFonts();
            Rebuild();
            Initialized = true;
        }

        /// <summary>挂接搜索输入监听（无条件强制重挂接，幂等）；UiEnhancer.Setup 清空旧监听后调用以恢复。</summary>
        public void SubscribeSearchInput()
        {
            if (_searchInput != null)
            {
                _searchInput.onValueChanged.RemoveListener(OnSearchChanged);
                _searchInput.onValueChanged.AddListener(OnSearchChanged);
                _subscribedInput = true;
            }
        }

        private void OnDestroy()
        {
            LocalizedText.OnLangugageChanged -= OnLanguageChanged;
        }

        /// <summary>停止监听（组件复用前调用，保证幂等）。</summary>
        public void Stop()
        {
            if (_subscribedLanguage)
            {
                LocalizedText.OnLangugageChanged -= OnLanguageChanged;
                _subscribedLanguage = false;
            }
            if (_subscribedInput && _searchInput != null)
            {
                _searchInput.onValueChanged.RemoveListener(OnSearchChanged);
                _subscribedInput = false;
            }
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

        /// <summary>切换收藏筛选：仅显示已收藏物品（与分类/搜索叠加）。</summary>
        public void SetFavoritesOnly(bool value)
        {
            if (_favoritesOnly != value)
            {
                _favoritesOnly = value;
                Rebuild();
            }
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
                entry.pinyinInitials = ToPinyinInitials(entry.displayName);
            }
            _all.Sort(CompareEntries);
            RefreshFonts();
            UiEnhancer.RefreshButtonLabels(); // 分类按钮文字/字体随语言刷新
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
                try
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
                        pinyinInitials = ToPinyinInitials(display),
                        tags = tags,
                        primary = primary,
                    });
                }
                catch (Exception ex)
                {
                    // 单个物品异常不应摧毁整个目录（参考 ItemBrowser 的逐条隔离）
                    Plugin.Log.LogWarning("ItemSpawnerPlus: 跳过异常物品 " + (item != null ? item.gameObject.name : "<null>") + ": " + ex.Message);
                }
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
            if (item != null && item.UIData != null)
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
                string name = item.UIData.itemName;
                // 与 ResolveDisplayName 保持一致：剥离 "LOC:" 前缀（若 itemName 本身是本地化 key）
                if (name.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(4).Trim();
                }
                return name;
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

            // 1) 静态表（主分类由 PrimaryOfTags 运行时推导，避免双份维护漂移）
            if (ItemCatalog.ItemTagMap.TryGetValue(prefab, out tags))
            {
                primary = ItemCatalog.PrimaryOfTags(tags);
                return;
            }

            // 2) 运行时组件/标签兜底。
            //    注意：目录遍历的是 ItemDatabase.Objects（prefab 资产，Awake 不执行），
            //    序列化在食物 prefab 上的 ItemCooking 表示"可烹饪食物"，对目录分类有意义。
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
            TMP_FontAsset font = NeedsCjkFont() ? _fontCjk : _fontLatin;
            if (font == null)
            {
                return; // 字体未找到时保留模板原字体，避免赋 null
            }
            if (_searchInput != null)
            {
                if (_searchInput.textComponent != null)
                {
                    _searchInput.textComponent.font = font;
                    _searchInput.textComponent.fontSize = 24f; // 与搜索框新高度匹配
                }
                TMP_Text placeholder = _searchInput.placeholder as TMP_Text;
                if (placeholder != null)
                {
                    placeholder.font = font;
                    placeholder.fontSize = 24f; // 与输入文字字号一致
                    placeholder.text = Loc.Get("searchPlaceholder");
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

            TMP_FontAsset font = NeedsCjkFont() ? _fontCjk : _fontLatin;
            string query = (_query == null) ? "" : _query.Trim().ToLowerInvariant();
            string queryNoSpace = StripNonAlnum(query); // 与 pinyin/pinyinInitials 同规则：去掉所有非字母数字

            // 智能排名：先计算每个条目匹配分数（0 表示不匹配），按分数降序排列；
            // LINQ OrderByDescending 为稳定排序，同分保持原顺序（分类 + 显示名顺序）。
            var ordered = _all
                .Select(entry => new { Entry = entry, S = Score(entry, query, queryNoSpace) })
                .Where(x => x.S > 0)
                .OrderByDescending(x => x.S);

            foreach (var item in ordered)
            {
                Entry entry = item.Entry;
                Transform clone = Instantiate(_template, _content);
                clone.SetAsLastSibling();
                clone.gameObject.name = entry.prefabName;

                // 移除克隆条目上的 LocalizedText 组件，避免游戏语言刷新时覆盖我们设置的文本/字体
                LocalizedText[] inheritedLts = clone.GetComponentsInChildren<LocalizedText>(true);
                for (int li = 0; li < inheritedLts.Length; li++)
                {
                    if (inheritedLts[li] != null)
                    {
                        Destroy(inheritedLts[li]);
                    }
                }

                Transform nameTrans = clone.Find("ItemName");
                if (nameTrans != null)
                {
                    TextMeshProUGUI nameText = nameTrans.GetComponent<TextMeshProUGUI>();
                    if (nameText != null)
                    {
                        nameText.text = entry.displayName;
                        if (font != null)
                        {
                            nameText.font = font;
                        }
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

                // 右键收藏：挂 IPointerClickHandler 监听右键，切换收藏并重建
                ItemFavoriteTrigger ft = clone.gameObject.AddComponent<ItemFavoriteTrigger>();
                string capturedPrefab = entry.prefabName;
                Transform capturedFav = clone.Find("Favorite");
                ft.Configure(() => ToggleFavorite(capturedPrefab, capturedFav));
                ft.InteractionEnabled = true;

                // 心形标记：收藏时显示
                Transform fav = clone.Find("Favorite");
                if (fav != null)
                {
                    fav.gameObject.SetActive(Plugin.Favorites.IsFavorite(entry.prefabName));
                }

                clone.gameObject.SetActive(true);
            }

            // 隐藏模板本身（原模组把模板留在列表中，这里隐藏以获得干净列表）
            _template.gameObject.SetActive(false);
        }

        /// <summary>
        /// 计算条目匹配分数：返回 0 表示不匹配，分数越高排名越靠前。
        /// 优先级：当前语言显示名 &gt; 英文名 &gt; prefab 名 &gt; 拼音全拼 &gt; 拼音首字母；
        /// 每档内再按「精确 == / 前缀 / 包含」细分。
        /// </summary>
        private int Score(Entry entry, string query, string queryNoSpace)
        {
            // query 已 trim + ToLowerInvariant；queryNoSpace 是去掉空格后的 query
            if (!ItemCatalog.IsInMajor(entry.tags, _major))
            {
                return 0;
            }
            if (_favoritesOnly && !Plugin.Favorites.IsFavorite(entry.prefabName))
            {
                return 0; // 收藏筛选：非收藏物品不匹配
            }
            if (query.Length == 0)
            {
                return 1; // 空查询全部匹配，最低正分
            }

            string dn = entry.displayName == null ? null : entry.displayName.ToLowerInvariant();
            string en = entry.enName == null ? null : entry.enName.ToLowerInvariant();
            string pn = entry.prefabName == null ? null : entry.prefabName.ToLowerInvariant();

            // 当前语言显示名（最高优先级）
            if (dn != null)
            {
                if (dn == query) return 1000;
                if (dn.StartsWith(query)) return 800;
                if (dn.Contains(query)) return 500;
            }
            // 英文名
            if (en != null)
            {
                if (en == query) return 900;
                if (en.StartsWith(query)) return 700;
                if (en.Contains(query)) return 400;
            }
            // prefab 名
            if (pn != null)
            {
                if (pn == query) return 800;
                if (pn.StartsWith(query)) return 600;
                if (pn.Contains(query)) return 350;
            }
            // 拼音全拼
            if (entry.pinyin != null && entry.pinyin.Contains(queryNoSpace)) return 200;
            // 拼音首字母
            if (entry.pinyinInitials != null && entry.pinyinInitials.Contains(queryNoSpace)) return 100;

            return 0;
        }

        /// <summary>切换物品收藏（右键触发）。筛选关闭时仅局部更新心形，避免全量重建导致滚动位置重置。</summary>
        private void ToggleFavorite(string prefabName, Transform favoriteMarker)
        {
            bool isFav;
            if (Plugin.Favorites.TryToggle(prefabName, out isFav))
            {
                if (_favoritesOnly)
                {
                    // 收藏筛选开启时，列表成员会变，需全量重建
                    Rebuild();
                }
                else
                {
                    // 筛选关闭时，仅更新该条目心形，避免全量重建导致滚动位置重置
                    if (favoriteMarker != null)
                    {
                        favoriteMarker.gameObject.SetActive(isFav);
                    }
                }
            }
        }

        private void SpawnItem(Item item)
        {
            if (item == null)
            {
                return;
            }
            // 原模组 Spawn 在未连接/无本地角色时静默返回，这里给出明确提示
            if (!PhotonNetwork.IsConnected || Character.localCharacter == null)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 无法生成 " + item.gameObject.name + "（未连接到房间或本地角色不存在）");
                return;
            }
            try
            {
                ItemSpawnerPlusWindow.Spawn(item);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawner Enhancement failed to spawn " + item.gameObject.name + ": " + ex);
            }
        }

        /// <summary>去掉所有非字母数字字符（小写化后），用于与 pinyin/pinyinInitials 字段对齐匹配。</summary>
        public static string StripNonAlnum(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }
            return sb.ToString();
        }

        /// <summary>将字符串中的汉字转成拼音全拼（其余字母数字保留），用于拼音搜索。</summary>
        public static string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            string raw = PinyinHelper.GetPinyin(text, ""); // 全拼无空格，非汉字原样保留
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

        /// <summary>将字符串中的汉字转成拼音首字母（其余字母数字保留，小写无空格），用于首字母搜索。</summary>
        public static string ToPinyinInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            string raw = PinyinHelper.GetPinyinInitials(text, ""); // 首字母，非汉字原样保留
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

        /// <summary>当前语言是否需要 CJK 字体（简/繁/日/韩），用于字体选择。</summary>
        public static bool NeedsCjkFont()
        {
            // 简体/繁体/日文/韩文均需 CJK 字体（游戏 SetLanguage 对这些语言切换中文字体 fallback）
            LocalizedText.Language language = LocalizedText.CURRENT_LANGUAGE;
            return language == LocalizedText.Language.SimplifiedChinese
                || language == LocalizedText.Language.TraditionalChinese
                || language == LocalizedText.Language.Japanese
                || language == LocalizedText.Language.Korean;
        }

        /// <summary>当前语言是否为中文（仅简/繁），用于中文文案分支（按钮标签与 ExtraCustomNames 自定义名）。</summary>
        public static bool IsChineseLanguage()
        {
            // 仅简体/繁体返回 true；日/韩玩家使用英文文案，但字体仍需 CJK（见 NeedsCjkFont）
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

        /// <summary>获取游戏主 UI 字体（带中文 fallback，用于 CJK 显示）。</summary>
        public static TMP_FontAsset GetGameBaseFont()
        {
            if (FontFallbackSwapper.instance != null && FontFallbackSwapper.instance.mainBaseFont != null)
            {
                return FontFallbackSwapper.instance.mainBaseFont;
            }
            // TMP 字体资源名惯例带 " SDF" 后缀（如 "Muli SDF"），"Muli" 为 LocalizedText 的基名
            TMP_FontAsset muli = FindFont("Muli SDF");
            if (muli != null)
            {
                return muli;
            }
            muli = FindFont("Muli");
            if (muli != null)
            {
                return muli;
            }
            return FindFont("LiberationSans SDF");
        }
    }
}
