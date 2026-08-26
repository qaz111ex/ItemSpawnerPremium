using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            public GameObject go;           // 该条目对应的 GameObject（对象池缓存）
        }

        /// <summary>Init 是否已成功执行（成功末尾置 true）。判断"已成功 Setup"的可靠依据，不依赖 Destroy 延迟语义。</summary>
        [NonSerialized] public bool Initialized;

        /// <summary>增量构建条目是否进行中（防止构建期间 Warmup/F5 重复 Setup 导致重复构建）。</summary>
        public bool Building { get; private set; }

        private Transform _content;
        private Transform _template;
        private TMP_InputField _searchInput;
        private List<Entry> _all = new List<Entry>();
        private MajorCategory _major = MajorCategory.All;
        private string _query = "";
        private bool _favoritesOnly;

        private TMP_FontAsset _fontLatin;
        private TMP_FontAsset _fontCjk;
        private bool _subscribedInput;
        private bool _subscribedLanguage;
        private volatile bool _refreshRequested;   // 物品隐藏开关变化请求（可能由非主线程的 Config 回调置位）

        public void Init(Transform content, Transform template, TMP_InputField searchInput)
        {
            _content = content;
            _template = template;
            _searchInput = searchInput;

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
            // 同步置构建中标志：窗口 inactive 时协程体不会执行，必须在 Init 同步置位防止 Warmup/F5 重复 Setup
            Building = true;
            StartCoroutine(BuildAllEntriesIncremental());
        }

        /// <summary>
        /// 请求重建目录（物品隐藏开关变化时调用）。只置标志，由 <see cref="Update"/> 在主线程消费：
        /// BepInEx 的 SettingChanged 在调用方线程同步派发，直接做 Destroy/StartCoroutine 会在非主线程抛异常；
        /// 且构建中（Building）时立即处理会被丢弃，置标志可保证稍后补做而非静默丢失。
        /// </summary>
        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        private void Update()
        {
            if (!_refreshRequested)
            {
                return;
            }
            if (!Initialized || Building)
            {
                return; // 构建中/未就绪：保留请求，下一帧再试
            }
            _refreshRequested = false;
            RefreshCatalog();
        }

        private void RefreshCatalog()
        {
            // 旧条目先脱离 _content 再销毁：Destroy 延迟到帧末，若仍挂在 _content 下，
            // 同帧新建的条目会与之共存，污染 Rebuild 的 SetSiblingIndex 与 GridLayoutGroup 布局。
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Transform child = _content.GetChild(i);
                if (child != _template)
                {
                    child.gameObject.SetActive(false);
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
            }
            for (int i = 0; i < _all.Count; i++)
            {
                _all[i].go = null;  // 旧 clone 已销毁，断开引用避免 Rebuild 访问
            }
            try
            {
                BuildCatalog();
            }
            catch (Exception ex)
            {
                // 目录重建失败时必须复位 Initialized：否则 UiEnhancer.Setup 的守卫会因
                // Initialized==true 永久跳过，F5 兜底失效，窗口永久空列表直到重启。
                Initialized = false;
                Plugin.Log.LogError("ItemSpawnerPremium: 重建目录失败，已复位以便下次 F5 重试: " + ex);
                return;
            }
            Building = true;
            StartCoroutine(BuildAllEntriesIncremental());
        }

        private void OnDestroy()
        {
            Stop();  // 统一走 Stop()：退订语言事件与搜索监听，避免两处重复逻辑漂移
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
        }

        public void SetQuery(string value)
        {
            _query = value ?? "";
            Rebuild();
        }

        /// <summary>当前收藏筛选状态（唯一数据源，供 UiEnhancer 读取以刷新按钮颜色）。</summary>
        public bool FavoritesOnly { get { return _favoritesOnly; } }

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
            if (db == null || db.Objects == null || db.Objects.Count == 0)
            {
                // 主菜单早期 ItemDatabase 未就绪时抛异常，冒泡到 UiEnhancer.Setup 的 catch 回滚 view，
                // 下次 F5/Warmup 重新 Setup，避免目录被缓存为空后永久空白。
                throw new InvalidOperationException("ItemDatabase 未就绪（空），稍后重试");
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
                    if (string.IsNullOrEmpty(prefab) || (Plugin.HideUnused && ItemCatalog.IsHidden(prefab)))
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
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 跳过异常物品 " + (item != null ? item.gameObject.name : "<null>") + ": " + ex.Message);
                }
            }
            _all.Sort(CompareEntries);
            // 收藏清理只能在"目录代表全集"时进行：HideUnused=true 时 _all 已剔除隐藏物品，
            // 若据此 Prune 会把隐藏物品的收藏永久删除并立即写盘（SaveOnConfigSet），造成用户数据丢失。
            // 因此传入未经显示过滤的完整 prefab 集合，与显示过滤彻底解耦。
            Plugin.Favorites.Prune(EnumerateAllPrefabNames(db));
        }

        /// <summary>枚举数据库中所有物品的 prefab 名（不受隐藏开关影响），供收藏脏数据清理使用。</summary>
        private static IEnumerable<string> EnumerateAllPrefabNames(ItemDatabase db)
        {
            foreach (Item item in db.Objects)
            {
                if (item == null)
                {
                    continue;
                }
                string name = null;
                try
                {
                    name = item.gameObject.name;
                }
                catch
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(name))
                {
                    yield return name;
                }
            }
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
        /// 1) 静态表（基于游戏资源真值提取的已知物品，见 ItemCatalog.ItemTagMap）
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
                // 食物兜底只认"恢复饥饿"这一确定证据：ItemCooking/Action_Consume 在大量非食物道具上
                // 也存在（望远镜、指南书、绷带、炸药、绳索、防晒、篮球、雪球等 41 项），
                // 用它们判定食物会把模组新增的普通道具误分类为食物。
                if (item.GetComponent<Action_RestoreHunger>() != null)
                {
                    tags |= ItemCategory.Food;
                }
                // 有使用次数或一次性消耗 → 消耗品（食物除外，食物走上面的分支）
                if ((tags & ItemCategory.Food) == 0
                    && (item.totalUses > 0 || item.GetComponent<Action_Consume>() != null))
                {
                    tags |= ItemCategory.Consumables;
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

            // 兜底：未知物品归入"杂项"
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
            int c2 = string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
            if (c2 != 0)
            {
                return c2;
            }
            // prefab 名做最终 tiebreaker：使比较器成为全序，避免同名条目（如两种救援抓钩/煎蛋）
            // 在 List.Sort（不稳定排序）后相对顺序抖动，导致语言切换时网格位置互换。
            return string.Compare(a.prefabName, b.prefabName, StringComparison.Ordinal);
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

        /// <summary>创建单个条目并设置一次性内容（图标、名称初值、左键生成、右键收藏、心形初值）。</summary>
        private void CreateEntry(Entry entry)
        {
            Transform clone = Instantiate(_template, _content);
            clone.gameObject.name = entry.prefabName;
            entry.go = clone.gameObject;

            // 移除克隆条目上的 LocalizedText 组件，避免游戏语言刷新时覆盖我们设置的文本/字体
            LocalizedText[] inheritedLts = clone.GetComponentsInChildren<LocalizedText>(true);
            for (int li = 0; li < inheritedLts.Length; li++)
            {
                if (inheritedLts[li] != null)
                {
                    Destroy(inheritedLts[li]);
                }
            }

            // 图标（一次性）
            Transform iconTrans = clone.Find("ItemIcon");
            if (iconTrans != null)
            {
                RawImage icon = iconTrans.GetComponent<RawImage>();
                if (icon != null && entry.item.UIData != null)
                {
                    icon.texture = entry.item.UIData.icon;
                }
            }

            // 名称（一次性设初值，后续 Rebuild 按语言更新）
            Transform nameTrans = clone.Find("ItemName");
            if (nameTrans != null)
            {
                TextMeshProUGUI nameText = nameTrans.GetComponent<TextMeshProUGUI>();
                if (nameText != null)
                {
                    nameText.text = entry.displayName;
                }
            }

            // 左键生成（一次性绑定）
            Button button = clone.GetComponent<Button>();
            if (button != null)
            {
                Item captured = entry.item;
                button.onClick.AddListener(() => SpawnItem(captured));
            }

            // 右键收藏（一次性挂载）
            ItemFavoriteTrigger ft = clone.gameObject.AddComponent<ItemFavoriteTrigger>();
            string capturedPrefab = entry.prefabName;
            Transform capturedFav = clone.Find("Favorite");
            ft.Configure(() => ToggleFavorite(capturedPrefab, capturedFav));

            // 心形标记（初始状态）
            if (capturedFav != null)
            {
                capturedFav.gameObject.SetActive(Plugin.Favorites.IsFavorite(entry.prefabName));
            }
        }

        /// <summary>
        /// 增量构建：分批实例化条目（每批 24 个让出一帧），分散单帧 Instantiate 开销，消除初次打开卡顿。
        /// </summary>
        private IEnumerator BuildAllEntriesIncremental()
        {
            Building = true;
            try
            {
                const int batchSize = 24;
                for (int i = 0; i < _all.Count; i++)
                {
                    CreateEntry(_all[i]);
                    if ((i + 1) % batchSize == 0 && i + 1 < _all.Count)
                    {
                        yield return null;  // 每批让出一帧，分散 Instantiate 开销
                    }
                }
                // 模板放最后且 inactive（不参与布局），避免被误当条目
                _template.gameObject.SetActive(false);
                _template.SetAsLastSibling();
                Rebuild();
                Initialized = true;
            }
            finally
            {
                Building = false;
            }
        }

        private void Rebuild()
        {
            if (_content == null || _template == null)
            {
                return;
            }

            TMP_FontAsset font = NeedsCjkFont() ? _fontCjk : _fontLatin;
            string query = (_query == null) ? "" : _query.Trim().ToLowerInvariant();
            string queryNoSpace = StripNonAlnum(query); // 与 pinyin/pinyinInitials 同规则：去掉所有非字母数字
            bool chinese = IsChineseLanguage();          // 提到循环外算一次，避免每条目重复读静态字段

            // 智能排名：计算每个条目分数（0 表示不匹配），可见条目按分数降序（LINQ 稳定排序，同分保持原顺序）
            var scored = _all
                .Select(entry => new { Entry = entry, S = Score(entry, query, queryNoSpace, chinese) })
                .ToList();
            var visible = scored.Where(x => x.S > 0).OrderByDescending(x => x.S).ToList();

            // 对象池复用：先全部隐藏，再按排序顺序显示可见条目并设置 sibling 顺序
            for (int i = 0; i < scored.Count; i++)
            {
                GameObject go = scored[i].Entry.go;
                if (go != null)
                {
                    go.SetActive(false);
                }
            }

            for (int i = 0; i < visible.Count; i++)
            {
                Entry entry = visible[i].Entry;
                GameObject go = entry.go;
                if (go == null)
                {
                    continue;
                }
                go.SetActive(true);
                go.transform.SetSiblingIndex(i);

                // 更新名称文字与字体（语言切换后 displayName/font 会变）
                Transform nameTrans = go.transform.Find("ItemName");
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

                // 刷新心形显隐：收藏状态可能已变（如在收藏筛选界面取消收藏后切回其他分类），
                // Rebuild 必须同步心形标记，否则残留"已取消收藏却仍显示爱心"。
                Transform favTrans = go.transform.Find("Favorite");
                if (favTrans != null)
                {
                    favTrans.gameObject.SetActive(Plugin.Favorites.IsFavorite(entry.prefabName));
                }

                // 复位卡片按压反馈色：条目在按下期间被 Rebuild 隐藏时 PressFeedback 收不到
                // OnPointerUp/Exit，颜色会残留在压暗态并随对象池复用"传染"到其他物品。
                Image cardImg = go.GetComponent<Image>();
                if (cardImg != null)
                {
                    cardImg.color = Color.white;
                }
            }
        }

        /// <summary>
        /// 计算条目匹配分数：返回 0 表示不匹配，分数越高排名越靠前。
        /// 优先级：当前语言显示名 &gt; 拼音（仅中文）&gt; 英文名 &gt; prefab 名；
        /// 每档内再按「精确 == / 前缀 / 包含」细分。
        /// </summary>
        private int Score(Entry entry, string query, string queryNoSpace, bool chinese)
        {
            // query 已 trim + ToLowerInvariant；queryNoSpace 是去掉所有非字母数字后的 query
            if (!ItemCatalog.IsInMajor(entry.tags, _major)) { return 0; }
            if (_favoritesOnly && !Plugin.Favorites.IsFavorite(entry.prefabName)) { return 0; }
            if (query.Length == 0) { return 1; }

            string dn = entry.displayName == null ? null : entry.displayName.ToLowerInvariant();
            string en = entry.enName == null ? null : entry.enName.ToLowerInvariant();
            string pn = entry.prefabName == null ? null : entry.prefabName.ToLowerInvariant();

            // 1. 当前语言显示名（最高优先级）
            if (dn != null)
            {
                if (dn == query) { return 1000; }
                if (dn.StartsWith(query)) { return 900; }
                if (dn.Contains(query)) { return 600; }
            }

            // 2. 拼音（仅中文语言下参与；前缀匹配优先于英文子串，保证中文玩家打拼音时中文结果靠前）
            if (chinese)
            {
                if (entry.pinyin != null && queryNoSpace.Length > 0)
                {
                    if (entry.pinyin.StartsWith(queryNoSpace)) { return 850; }
                    if (entry.pinyin.Contains(queryNoSpace)) { return 550; }
                }
                if (entry.pinyinInitials != null && queryNoSpace.Length > 0)
                {
                    if (entry.pinyinInitials.StartsWith(queryNoSpace)) { return 750; }
                    if (entry.pinyinInitials.Contains(queryNoSpace)) { return 500; }
                }
            }

            // 3. 英文名
            if (en != null)
            {
                if (en == query) { return 700; }
                if (en.StartsWith(query)) { return 500; }
                if (en.Contains(query)) { return 300; }
            }
            // 4. prefab 名
            if (pn != null)
            {
                if (pn == query) { return 650; }
                if (pn.StartsWith(query)) { return 450; }
                if (pn.Contains(query)) { return 250; }
            }
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
            try
            {
                ItemSpawnerPremiumWindow.Spawn(item); // 内部已做前置检查并记录提示
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
            return FilterAlnumLower(text);
        }

        /// <summary>将字符串中的汉字转成拼音全拼（其余字母数字保留），用于拼音搜索。</summary>
        public static string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return FilterAlnumLower(PinyinHelper.GetPinyin(text, "")); // 全拼无空格，非汉字原样保留
        }

        /// <summary>将字符串中的汉字转成拼音首字母（其余字母数字保留，小写无空格），用于首字母搜索。</summary>
        public static string ToPinyinInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return FilterAlnumLower(PinyinHelper.GetPinyinInitials(text, "")); // 首字母，非汉字原样保留
        }

        /// <summary>过滤非字母数字字符并转小写（拼音全拼/首字母/搜索 query 共用的归一化逻辑）。</summary>
        private static string FilterAlnumLower(string raw)
        {
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

        /// <summary>
        /// 当前语言是否需要游戏主字体（而非拉丁装饰字体）。
        /// 除简/繁/日/韩（CJK 字形）外，俄语/乌克兰语（西里尔字母）也必须用主字体：
        /// DarumaDropOne 是日系手绘装饰字体，其拉丁子集不含西里尔字形，直接使用会渲染成方块。
        /// </summary>
        public static bool NeedsCjkFont()
        {
            // 简体/繁体/日文/韩文需 CJK 字形（游戏 SetLanguage 对中文切换 fallback）；
            // 俄语/乌克兰语需西里尔字形，两者都只有游戏主字体（含 fallback 链）能覆盖。
            LocalizedText.Language language = LocalizedText.CURRENT_LANGUAGE;
            return language == LocalizedText.Language.SimplifiedChinese
                || language == LocalizedText.Language.TraditionalChinese
                || language == LocalizedText.Language.Japanese
                || language == LocalizedText.Language.Korean
                || language == LocalizedText.Language.Russian
                || language == LocalizedText.Language.Ukrainian;
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
