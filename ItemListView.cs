using System;
using System.Collections;
using System.Collections.Generic;
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

            // ---- 搜索用小写副本（预计算）----
            // Score 是按键热路径：每次按键对全部条目各调一次，若在 Score 内做 ToLowerInvariant，
            // N=153 时每次按键要产生约 3N 个临时字符串。这三个字段在条目创建与语言切换时更新一次即可。
            public string displayNameLower;
            public string enNameLower;
            public string prefabNameLower;

            // ---- 子节点组件缓存 ----
            // Rebuild 每次按键都要更新可见条目的文字/心形/卡片色，原先用 transform.Find + GetComponent
            // 现取一次存住：Find 是按名字线性遍历子节点、GetComponent 有托管↔原生往返开销，
            // 而条目子树在创建后结构固定（模板由 UiEnhancer 纯代码构建），缓存不会失效。
            public TextMeshProUGUI nameText;
            public Transform favoriteMarker;
            public Image cardImage;

            /// <summary>Rebuild 单次遍历的临时分数（0 = 本次不可见）。仅在 Rebuild 内有意义，不参与持久状态。</summary>
            public int scratchScore;

            /// <summary>Rebuild 单次遍历时该条目在 _all 中的下标，作为同分 tiebreaker 保证排序稳定。</summary>
            public int scratchOrder;

            /// <summary>刷新全部小写副本（条目创建时调用）。</summary>
            public void CacheLowerNames()
            {
                displayNameLower = (displayName == null) ? null : displayName.ToLowerInvariant();
                enNameLower = (enName == null) ? null : enName.ToLowerInvariant();
                prefabNameLower = (prefabName == null) ? null : prefabName.ToLowerInvariant();
            }

            /// <summary>仅刷新显示名的小写副本（语言切换时只有 displayName 会变）。</summary>
            public void CacheDisplayNameLower()
            {
                displayNameLower = (displayName == null) ? null : displayName.ToLowerInvariant();
            }

            /// <summary>
            /// 断开 GameObject 与其子组件缓存（clone 已销毁或即将销毁时调用）。
            /// 必须与 go 一起清空：只清 go 会留下指向已销毁组件的引用，
            /// 后续若有代码先判 nameText != null 再用就会踩到 "destroyed object" 异常。
            /// </summary>
            public void ClearGameObject()
            {
                go = null;
                nameText = null;
                favoriteMarker = null;
                cardImage = null;
            }
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

        private bool _subscribedInput;
        private bool _subscribedLanguage;
        private volatile bool _refreshRequested;   // 物品隐藏开关变化请求（可能由非主线程的 Config 回调置位）

        // Rebuild 热路径复用的缓冲区：每次按键都会重算，若每次新建 List 则 N=153 时按键即产生垃圾。
        // 分数与稳定排序所需的原始下标存在 Entry.scratchScore / scratchOrder 上，避免再开平行数组。
        private readonly List<Entry> _visible = new List<Entry>();

        /// <summary>
        /// 可见条目排序：分数降序，同分按 _all 中的原始下标升序。
        /// 原实现用 LINQ OrderByDescending（稳定排序）隐式保证同分维持 _all 顺序；
        /// 改用 List.Sort（不稳定）后必须显式加下标 tiebreaker，否则同分条目相对顺序会抖动，
        /// 表现为每次按键网格里的物品位置乱跳。委托提为静态字段，避免每次 Rebuild 新建闭包。
        /// </summary>
        private static readonly Comparison<Entry> VisibleComparison = delegate (Entry a, Entry b)
        {
            if (a.scratchScore != b.scratchScore)
            {
                return b.scratchScore - a.scratchScore;   // 分数降序
            }
            return a.scratchOrder - b.scratchOrder;       // 同分保持原顺序
        };

        public void Init(Transform content, Transform template, TMP_InputField searchInput)
        {
            _content = content;
            _template = template;
            _searchInput = searchInput;

            // 无条件清场：Init 不保证是首次调用。若上一轮增量构建中途抛异常，
            // Initialized 仍为 false、Building 已在 finally 复位，UiEnhancer.Setup 的守卫会放行再次 Init，
            // 而 BuildCatalog 的 _all.Clear() 会丢弃对上一批 clone 的全部引用 ——
            // 那些 clone 仍挂在 _content 下且 Button.onClick / ItemFavoriteTrigger 有效，
            // 不受 Rebuild（只遍历 _all）的任何过滤控制，形成"点了照样出物品"的孤儿条目，且每次失败叠加一层。
            // 此处在 _template 已赋值之后立即清理，保证 _content 下只剩模板。
            Initialized = false;   // 条目已被清空，状态必须诚实：真正建完由协程末尾重新置 true
            ClearEntries();

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

        /// <summary>
        /// 销毁 _content 下所有条目 clone（保留模板），并断开 _all 里对它们的引用。
        /// 被 Init（无条件清场，防上次构建失败留下的孤儿条目）与 RefreshCatalog（重建前清场）共用。
        /// </summary>
        private void ClearEntries()
        {
            if (_content != null)
            {
                // 旧条目先脱离 _content 再销毁：Destroy 延迟到帧末，若仍挂在 _content 下，
                // 同帧新建的条目会与之共存，污染 Rebuild 的 SetSiblingIndex 与 GridLayoutGroup 布局。
                for (int i = _content.childCount - 1; i >= 0; i--)
                {
                    Transform child = _content.GetChild(i);
                    if (child != _template)   // 模板是长期资产，只能隐藏不能销毁
                    {
                        child.gameObject.SetActive(false);
                        child.SetParent(null, false);
                        Destroy(child.gameObject);
                    }
                }
            }
            for (int i = 0; i < _all.Count; i++)
            {
                _all[i].ClearGameObject();  // 旧 clone 已销毁，断开 go 与子组件缓存避免 Rebuild 访问
            }
            _visible.Clear();  // 该缓冲持有上一轮 Entry 引用，清掉避免无谓保活（Rebuild 也会重填）
        }

        private void RefreshCatalog()
        {
            ClearEntries();
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
                entry.CacheDisplayNameLower(); // 搜索用小写副本必须同步，否则语言切换后按显示名搜不到
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
                    Entry entry = new Entry
                    {
                        item = item,
                        displayName = display,
                        enName = ResolveEnglishName(item, prefab),
                        prefabName = prefab,
                        pinyin = ToPinyin(display),
                        pinyinInitials = ToPinyinInitials(display),
                        tags = tags,
                        primary = primary,
                    };
                    entry.CacheLowerNames();   // 预计算搜索用小写副本（Score 热路径不再做 ToLowerInvariant）
                    _all.Add(entry);
                }
                catch (Exception ex)
                {
                    // 单个物品异常不应摧毁整个目录（参考 ItemBrowser 的逐条隔离）
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 跳过异常物品 " + (item != null ? item.gameObject.name : "<null>") + ": " + ex.Message);
                }
            }
            _all.Sort(CompareEntries);
            // 收藏脏数据检查只能在"目录代表全集"时进行：HideUnused=true 时 _all 已剔除隐藏物品，
            // 若据此判断会把隐藏物品的收藏当成脏数据。因此传入未经显示过滤的完整 prefab 集合，
            // 与显示过滤彻底解耦。（Prune 现在只报告不删除，见 FavoriteStore.Prune 的说明。）
            Plugin.Favorites.Prune(EnumerateAllPrefabNames(db));
        }

        /// <summary>枚举数据库中所有物品的 prefab 名（不受隐藏开关影响），供收藏脏数据检查使用。</summary>
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
                // 必须用默认的 printDebug: true。LocalizedText.GetText 的这个参数语义反直觉
                // （反编译 LocalizedText.cs:405-410）：key 缺失时 true 分支静默返回 "LOC: " + id，
                // false 分支反而会 Debug.LogError("Failed to load text: ...") 再返回 ""。
                // 这里是"试探性查表、查不到就走下一档兜底"的场景，不该刷 Unity Error：
                // BuildCatalog 与每次语言切换都会对 6 个 key 各调一次，游戏更新移除任一 key 就会刷屏。
                // 返回的 "LOC:" 前缀恰好被下面的 StartsWith 判定拦下，行为与原先等价。
                string text = LocalizedText.GetText(extraKey);
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
                // 食物 itemTags 除 PackagedFood/Berry/Mushroom 外，还必须认 Bird 与 GourmandRequirement：
                // 真值数据（item_truth.json）里这两个 tag 只出现在食物上 ——
                //   Bird 唯一持有者是 EggTurkey（火鸡蛋，itemName=Bird）；
                //   GourmandRequirement 的唯一持有者是 Egg / EggRaven / NestEgg / NestEgg_Raven /
                //   Item_Coconut_half / Item_Honeycomb（单独持有），以及 Winterberry Yellow / Yuzu Berry
                //   （与 Berry 同时持有）—— 全部是可食用物。
                // 这 9 项本身都在静态表里（静态表优先命中，分类结果不变），补这两个 tag 是为了让
                // 模组新增的同类食物（蛋类/椰子/蜂巢等只打 Bird 或 GourmandRequirement 的物品）不被漏判。
                if ((itags & Item.ItemTags.PackagedFood) != 0
                    || (itags & Item.ItemTags.Berry) != 0
                    || (itags & Item.ItemTags.Mushroom) != 0
                    || (itags & Item.ItemTags.Bird) != 0
                    || (itags & Item.ItemTags.GourmandRequirement) != 0)
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
            // 三级全序比较（主分类 → 显示名 → prefab 名）委托给 SearchRanking.CompareCatalog：
            // 纯逻辑无 Unity 依赖，由 tests\ 下的单元测试验证全序性质（反对称、传递、同名稳定）。
            return SearchRanking.CompareCatalog(
                a.primary, a.displayName, a.prefabName,
                b.primary, b.displayName, b.prefabName);
        }

        /// <summary>
        /// 取当前语言应使用的字体。统一委托给 <see cref="UiEnhancer.ResolveFont"/>，
        /// **不能自己用 `NeedsCjkFont() ? _fontCjk : _fontLatin` 判断**：
        /// UiEnhancer 在 2.2.0 增加了「装饰字体字符覆盖探测」，波兰语/土耳其语等拉丁扩展语言下
        /// 会降级到游戏主字体；若这里还按老逻辑直接取 _fontLatin（未经探测的装饰字体），
        /// 就会出现「按钮标签正常、物品名与搜索框仍是方块」的半修复状态。
        /// UiEnhancer 侧已对装饰字体查询与覆盖探测做了静态缓存，重复调用不产生额外开销。
        /// </summary>
        private static TMP_FontAsset ResolveCurrentFont()
        {
            return UiEnhancer.ResolveFont();
        }

        private void RefreshFonts()
        {
            TMP_FontAsset font = ResolveCurrentFont();
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

            // 模板由 UiEnhancer.CreateItemEntryTemplate 纯代码构建，组件清单固定为
            // RectTransform/CanvasRenderer/Image/Button/LayoutElement/PressFeedback + 子节点
            // RawImage(ItemIcon)/TextMeshProUGUI(ItemName)/RawImage(Favorite)，从不包含 LocalizedText，
            // 因此这里不需要清理 LocalizedText（旧版复用游戏 prefab 时才需要，那时游戏 prefab 自带
            // LocalizedText 会在语言刷新时覆盖我们设置的文本/字体）。
            // 若未来改回复用游戏 prefab，必须恢复 "GetComponentsInChildren<LocalizedText>(true) 全部 Destroy" 的逻辑。

            // 图标（一次性）
            Transform iconTrans = clone.Find("ItemIcon");
            if (iconTrans != null)
            {
                RawImage icon = iconTrans.GetComponent<RawImage>();
                if (icon != null && entry.item.UIData != null)
                {
                    icon.texture = ResolveIconTexture(entry.item.UIData);
                }
            }

            // 名称（一次性设初值，后续 Rebuild 按语言更新）；组件引用缓存进 Entry 供 Rebuild 复用
            Transform nameTrans = clone.Find("ItemName");
            if (nameTrans != null)
            {
                TextMeshProUGUI nameText = nameTrans.GetComponent<TextMeshProUGUI>();
                if (nameText != null)
                {
                    nameText.text = entry.displayName;
                    entry.nameText = nameText;
                }
            }

            // 卡片背景 Image（PressFeedback 压暗的目标，Rebuild 需复位其颜色）
            entry.cardImage = clone.GetComponent<Image>();

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
            entry.favoriteMarker = capturedFav;
            ft.Configure(() => ToggleFavorite(capturedPrefab, capturedFav));

            // 心形标记（初始状态）
            if (capturedFav != null)
            {
                capturedFav.gameObject.SetActive(Plugin.Favorites.IsFavorite(entry.prefabName));
            }

            // 与 UiEnhancer 的模板初始状态解耦：Instantiate 会继承模板的 active 状态，
            // 首次构建时模板还是 active 的（模板要到增量构建结束才 SetActive(false)），
            // 于是条目会逐批"闪入"，且未经过滤就先显示一瞬。
            // 这里无条件置 inactive，让 Rebuild 成为显隐的唯一决策者（可见条目由它 SetActive(true)），
            // 无论 UiEnhancer 那边模板是什么状态都不受影响。
            clone.gameObject.SetActive(false);
        }

        /// <summary>
        /// 取物品图标，优先走游戏自己的 UIData.GetIcon()（尊重"昆虫恐惧症"与色盲无障碍设置）。
        /// GetIcon() 内部（反编译 Item.cs:83-94）会无保护地解引用两个可能为 null 的单例：
        ///   1. GameHandler.Instance（Item.cs:85）—— GameHandler 在自身 Initialize() 里才赋 _instance，
        ///      且 SettingsHandler 是在 async Awake 中构造的（GameHandler.cs:154），
        ///      即"Instance 非 null 但 SettingsHandler 仍为 null"的窗口真实存在；
        ///      此外 GetSetting&lt;BugPhobiaSetting&gt;() 在找不到时返回 null（SettingsHandler.cs:84-94），
        ///      对其取 .Value 同样会 NRE。
        ///   2. GUIManager.instance（Item.cs:89）—— 静态字段在 GUIManager.Awake 才赋值（GUIManager.cs:317），
        ///      主菜单/早期场景下为 null。
        /// 本插件的目录构建可能发生在主菜单（Warmup 预热），因此必须包 try/catch，
        /// 失败时回退到原始 icon（与修复前行为一致，只是丢失无障碍图标替换）。
        /// </summary>
        private static Texture2D ResolveIconTexture(Item.ItemUIData uiData)
        {
            try
            {
                return uiData.GetIcon();
            }
            catch (Exception)
            {
                return uiData.icon;
            }
        }

        /// <summary>
        /// 丢弃创建失败的半成品条目：销毁其 clone 并断开 Entry 上的全部 GameObject/组件缓存。
        /// 与 ClearEntries 的差别是只处理单个条目（用于 CreateEntry 抛异常的补偿路径）。
        /// </summary>
        private void DiscardPartialEntry(Entry entry)
        {
            if (entry == null)
            {
                return;
            }
            try
            {
                GameObject go = entry.go;
                if (go != null)
                {
                    go.SetActive(false);
                    go.transform.SetParent(null, false);  // 与 ClearEntries 同理：先脱离父级再销毁
                    Destroy(go);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 清理半成品条目失败: " + ex.Message);
            }
            entry.ClearGameObject();
        }

        /// <summary>
        /// 增量构建：分批实例化条目（每批 24 个让出一帧），分散单帧 Instantiate 开销，消除初次打开卡顿。
        /// 异常处理受 C# 7.3 迭代器限制：yield return 不能出现在带 catch 子句的 try 块内，
        /// 只能出现在 try-finally 的 try 块内。因此外层保持 try/finally（只负责复位 Building），
        /// 把可能抛异常的两段（单条 CreateEntry、收尾）各自包进不含 yield 的内层 try/catch。
        /// </summary>
        private IEnumerator BuildAllEntriesIncremental()
        {
            Building = true;
            try
            {
                const int batchSize = 24;
                for (int i = 0; i < _all.Count; i++)
                {
                    // 单条隔离：坏条目跳过即可，不能让一个异常物品毁掉整个目录
                    // （与 BuildCatalog 的逐条隔离一致）。注意此 try 内不能有 yield return。
                    try
                    {
                        CreateEntry(_all[i]);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("ItemSpawnerPremium: 创建条目失败，已跳过 "
                            + (_all[i] != null ? _all[i].prefabName : "<null>") + ": " + ex.Message);
                        // CreateEntry 可能在 Instantiate 之后才抛（如缺子节点/AddComponent 失败），
                        // 此时半成品 clone 已挂在 _content 下。必须连带销毁并断开引用，
                        // 否则它既不被 Rebuild 管理（entry.go 若保留会被当正常条目显示）、
                        // 又可能带着未绑定完的交互留在界面上。
                        DiscardPartialEntry(_all[i]);
                    }
                    if ((i + 1) % batchSize == 0 && i + 1 < _all.Count)
                    {
                        yield return null;  // 每批让出一帧，分散 Instantiate 开销
                    }
                }
                // 收尾同样隔离：这里若抛异常（如 _template 已被销毁），原先会带着"已建 clone 留在
                // _content 且 Initialized 仍为 false"的状态退出，下次 F5 再 Init 时 BuildCatalog 的
                // _all.Clear() 会丢弃这些 clone 的引用 —— 它们脱离 Rebuild 的过滤但点击仍能生成物品
                // （孤儿条目），且每次失败叠加一层。因此失败时必须 ClearEntries() 清场，
                // 并保持 Initialized=false 让 F5 能干净重试。
                try
                {
                    // 模板放最后且 inactive（不参与布局），避免被误当条目
                    _template.gameObject.SetActive(false);
                    _template.SetAsLastSibling();
                    Rebuild();
                    Initialized = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("ItemSpawnerPremium: 条目构建收尾失败，已清场以便下次 F5 重试: " + ex);
                    try
                    {
                        ClearEntries();
                    }
                    catch (Exception clearEx)
                    {
                        Plugin.Log.LogError("ItemSpawnerPremium: 清场亦失败: " + clearEx.Message);
                    }
                }
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

            TMP_FontAsset font = ResolveCurrentFont();   // 与 UiEnhancer 同源，见 ResolveCurrentFont 注释
            string query = (_query == null) ? "" : _query.Trim().ToLowerInvariant();
            string queryNoSpace = StripNonAlnum(query); // 与 pinyin/pinyinInitials 同规则：去掉所有非字母数字
            bool chinese = IsChineseLanguage();          // 提到循环外算一次，避免每条目重复读静态字段

            // 智能排名：计算每个条目分数（0 表示不匹配），可见条目按分数降序。
            // 用复用的 _visible 缓冲 + Entry 上的 scratch 字段，取代原先每次按键 2N 个匿名类型堆对象
            // （_all.Select(new {..}).ToList() + Where().OrderByDescending().ToList()）。
            _visible.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                Entry entry = _all[i];
                // 对象池复用：先全部隐藏，可见者在下面的循环里再显示
                if (entry.go != null)
                {
                    entry.go.SetActive(false);
                }
                int score = Score(entry, query, queryNoSpace, chinese);
                if (score > 0)
                {
                    entry.scratchScore = score;
                    entry.scratchOrder = i;   // 同分 tiebreaker，保证排序稳定（见 VisibleComparison）
                    _visible.Add(entry);
                }
            }
            _visible.Sort(VisibleComparison);

            for (int i = 0; i < _visible.Count; i++)
            {
                Entry entry = _visible[i];
                GameObject go = entry.go;
                if (go == null)
                {
                    continue;
                }
                go.SetActive(true);
                // 仅在顺序真的变了时才动：SetSiblingIndex 会触发父级 hierarchy 变更事件与
                // GridLayoutGroup 的布局脏标记，且内部要搬移 child 数组（N 次调用是 O(N²)）。
                // 绝大多数 Rebuild（如切收藏状态、语言切换）可见顺序不变，这个比较能直接省掉整轮开销。
                Transform tr = go.transform;
                if (tr.GetSiblingIndex() != i)
                {
                    tr.SetSiblingIndex(i);
                }

                // 更新名称文字与字体（语言切换后 displayName/font 会变）；组件引用来自 CreateEntry 的缓存
                TextMeshProUGUI nameText = entry.nameText;
                if (nameText != null)
                {
                    nameText.text = entry.displayName;   // TMP 的 text setter 自带相等短路，无需手动比较
                    // font setter 没有相等短路，赋同一个值也会 SetAllDirty() 强制重建该文本 mesh，
                    // 因此必须自己判等：字体只在语言切换时变，按键搜索时这里恒为 false。
                    if (font != null && nameText.font != font)
                    {
                        nameText.font = font;
                    }
                }

                // 刷新心形显隐：收藏状态可能已变（如在收藏筛选界面取消收藏后切回其他分类），
                // Rebuild 必须同步心形标记，否则残留"已取消收藏却仍显示爱心"。
                Transform favTrans = entry.favoriteMarker;
                if (favTrans != null)
                {
                    favTrans.gameObject.SetActive(Plugin.Favorites.IsFavorite(entry.prefabName));
                }

                // 复位卡片按压反馈色：条目在按下期间被 Rebuild 隐藏时 PressFeedback 收不到
                // OnPointerUp/Exit，颜色会残留在压暗态并随对象池复用"传染"到其他物品。
                Image cardImg = entry.cardImage;
                if (cardImg != null)
                {
                    cardImg.color = Color.white;
                }
            }
        }

        /// <summary>
        /// 计算条目匹配分数：返回 0 表示不匹配，分数越高排名越靠前。
        /// 本方法只负责「视图状态过滤」（分类 + 收藏筛选），文本匹配打分委托给
        /// <see cref="SearchRanking.Score"/>（纯逻辑、零 Unity 依赖，由 tests\ 下的单元测试覆盖）。
        /// 拆分理由：过滤要读 MonoBehaviour 实例状态（_major/_favoritesOnly）与 Plugin.Favorites，
        /// 无法脱离 Unity 运行时；而分档打分是纯字符串比较，是最需要回归保护的部分。
        /// </summary>
        private int Score(Entry entry, string query, string queryNoSpace, bool chinese)
        {
            // query 已 trim + ToLowerInvariant；queryNoSpace 是去掉所有非字母数字后的 query
            if (!ItemCatalog.IsInMajor(entry.tags, _major)) { return 0; }
            if (_favoritesOnly && !Plugin.Favorites.IsFavorite(entry.prefabName)) { return 0; }

            return SearchRanking.Score(
                entry.displayNameLower,
                entry.enNameLower,
                entry.prefabNameLower,
                entry.pinyin,
                entry.pinyinInitials,
                query,
                queryNoSpace,
                chinese);
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
            return SearchText.StripNonAlnum(text);
        }

        /// <summary>将字符串中的汉字转成拼音全拼（其余字母数字保留），用于拼音搜索。</summary>
        public static string ToPinyin(string text)
        {
            return SearchText.ToPinyin(text);
        }

        /// <summary>将字符串中的汉字转成拼音首字母（其余字母数字保留，小写无空格），用于首字母搜索。</summary>
        public static string ToPinyinInitials(string text)
        {
            return SearchText.ToPinyinInitials(text);
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
