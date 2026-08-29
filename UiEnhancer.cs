using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// UI 构建器（纯代码，不依赖 AssetBundle）：
    /// 1. 根 Canvas + 暖卡纸面板；
    /// 2. 顶部居中放大的搜索框；
    /// 3. 搜索框下方横向分类按钮条（全部/工具/食物/神秘/装备/消耗品/场景），浅色暖卡纸风；
    /// 4. 滚动列表（GridLayoutGroup 正方形网格）+ 条目模板（Button + ItemIcon + ItemName + Favorite）；
    /// 5. 挂载 ItemListView（本地化显示名 + 中文/拼音搜索 + 多标签分类过滤与排序）。
    /// </summary>
    public static class UiEnhancer
    {
        private static readonly List<Button> _categoryButtons = new List<Button>();
        private static readonly List<TextMeshProUGUI> _categoryButtonLabels = new List<TextMeshProUGUI>();
        // 与 _categoryButtons 平行：记录每个按钮对应的分类枚举值。
        // 不用「List 索引强转 MajorCategory」，否则一旦枚举插入新值或指定非连续数值，
        // 按钮高亮与悬停判定会整体错位。
        private static readonly List<MajorCategory> _categoryButtonMajors = new List<MajorCategory>();
        private static ItemListView _view;
        private static Button _favoriteButton;
        private static TextMeshProUGUI _favoriteButtonLabel;
        private static TextMeshProUGUI _rightClickHint;
        private static Texture2D _heartTexture;
        // 烘焙好的 Sprite 缓存（描边/白边/填充全部烘进纹理，0 层 Outline）
        private static Sprite _panelSprite;
        private static Sprite _cardSprite;
        private static Sprite _searchSprite;
        private static Sprite _btnIdleSprite;
        private static Sprite _btnHoverSprite;
        private static Sprite _btnSelectedSprite;
        private static Sprite _scrollbarBgSprite;
        private static Sprite _scrollbarHandleSprite;
        private static Sprite _shadowSprite;

        // ── 字体解析缓存 ────────────────────────────────────────────────
        // ResolveFont 在 Setup / Build / CreateCategoryBar / RefreshButtonLabels 各调一次，
        // 而 ItemListView.FindFont 内部是 Resources.FindObjectsOfTypeAll<TMP_FontAsset>() 全量遍历
        // （PEAK 的已加载对象量级下单次可达数十毫秒），故装饰字体的查询结果必须静态缓存。
        private static TMP_FontAsset _decorativeFont;
        // 负缓存：仅当"确实遍历过但没找到"时置位，避免每次 ResolveFont 都重跑全量遍历。
        // 注意 TMP_FontAsset 继承 UnityEngine.Object，其 == null 也会把"已销毁/已卸载"的对象判为 null：
        // 因此若字体曾找到、之后资源被卸载，_decorativeFont 变 null 而本标志仍为 false，
        // 下次会重查一次（而非永久放弃装饰字体），重查仍失败才真正转入负缓存。
        private static bool _decorativeFontMissing;

        // 装饰字体的「字符覆盖探测」结果缓存（见 DecorativeFontCoversUiText）。
        // 按「当前语言 + 字体实例 ID」为键自失效：语言切换（RefreshButtonLabels 路径）与字体资源被换
        // 都会让键不匹配从而重新探测，无需在别处手动清缓存 —— 也覆盖了「面板尚未构建时玩家已切过语言」
        // 这种不经过 RefreshButtonLabels 的路径。
        private static bool _coverageProbed;
        private static bool _coverageResult;
        private static LocalizedText.Language _coverageLanguage;
        private static int _coverageFontId;

        /// <summary>
        /// 本模组全部 UI 文案 key，作为字体能力探测的取样来源（与 Localization/*.json 的 key 集合一致）。
        /// 只取这 10 条界面文案、不取物品名：物品名有 150+ 条且依赖 ItemDatabase 就绪，
        /// 而 ResolveFont 在 Setup 最早期就会被调用，此时目录还没建起来。
        /// </summary>
        private static readonly string[] UiTextKeys =
        {
            "catAll", "catTools", "catFood", "catMystical", "catEquipment",
            "catConsumables", "catProps", "catFavorite", "searchPlaceholder", "rightClickHint",
        };

        /// <summary>一套完整 UI 配色（按样式切换）。颜色通过下方 getter 属性按当前样式取值，所有引用点无需改动。</summary>
        private sealed class Palette
        {
            public Color Idle;              // 按钮默认填充
            public Color Hover;             // 按钮悬停
            public Color Selected;          // 按钮选中
            public Color TextIdle;          // 默认文字
            public Color TextSelected;      // 选中文字
            public Color Hint;              // 右键提示文字
            public Color PanelBackground;   // 面板底
            public Color InkOutline;        // 深墨描边（勾线色）
            public Color InnerHighlight;    // 奶油白内描边
            public Color CardFill;          // 卡片底
            public Color PanelShadow;       // 面板投影
            public Color SearchFill;        // 搜索框底
            public Color ScrollbarBg;       // 滚动条轨道
            public Color ScrollbarHandle;   // 滚动条滑块
            public Color Placeholder;        // 搜索框占位符
        }

        // 手绘风调色板（默认）：暖"卡纸"手绘贴纸风，实色。保留 PEAK 户外暖色基调（米棕 → 奶油 → 浅暖黄），不采用冷色或纯白刺眼。
        // 浅底必须配深字：三态文字统一走深暖棕，保证高对比可读。
        private static readonly Palette HandDrawnPalette = new Palette
        {
            Idle = new Color(0.82f, 0.75f, 0.63f, 1f),            // 默认填充：浅暖米棕（卡纸基色）
            Hover = new Color(0.90f, 0.84f, 0.74f, 1f),           // 悬停：更亮的奶油米黄（比默认更亮，明显抬升）
            Selected = new Color(0.96f, 0.91f, 0.81f, 1f),        // 选中：最浅的暖黄高亮（三态中最亮，突出选中）
            TextIdle = new Color(0.28f, 0.21f, 0.14f, 1f),        // 默认/悬停文字：深暖棕（浅色底上高对比）
            TextSelected = new Color(0.22f, 0.16f, 0.10f, 1f),    // 选中文字：更深的暖棕（最浅选中底上更稳）
            Hint = new Color(0.50f, 0.42f, 0.32f, 1f),            // 右键收藏提示：柔和暖棕（比正文略淡，示意辅助提示）
            PanelBackground = new Color(0.76f, 0.69f, 0.56f, 1f), // 面板底色：暖卡纸（比分类按钮略深一档）
            InkOutline = new Color(0.36f, 0.25f, 0.15f, 1f),      // 手绘勾线色：深咖啡棕墨水描边
            InnerHighlight = new Color(0.99f, 0.94f, 0.85f, 1f),  // 内层浅色描边：暖奶油白（贴纸白边 + 双层勾线）
            CardFill = new Color(0.89f, 0.83f, 0.73f, 1f),        // 卡片底色：比面板略亮的奶油卡纸
            PanelShadow = new Color(0.33f, 0.24f, 0.15f, 0.35f),  // 面板投影：深暖棕半透明
            SearchFill = new Color(0.70f, 0.62f, 0.50f, 1f),      // 搜索框底色：略深于面板的暖卡其（下凹输入槽感）
            ScrollbarBg = new Color(0.58f, 0.48f, 0.36f, 0.6f),   // 滚动条轨道底色：略深暖棕（半透明）
            ScrollbarHandle = new Color(0.80f, 0.71f, 0.58f, 1f), // 滚动条 handle：较浅暖棕滑块
            Placeholder = new Color(0.45f, 0.37f, 0.28f, 0.6f),
        };

        // 透明风调色板：精确照搬原版 ItemSpawner 的真实调色（从 itemspawnerui AssetBundle 解析，1.2.0 实际显示色）。
        // 核心：统一 alpha≈0.392 的深灰黑半透明（Panel 0.160 / ScrollView 0.387 / ItemEntry 纯黑），
        // 搜索框例外为不透明深灰（0.196），文字纯白，无描边、无投影（简洁）。
        private static readonly Palette TransparentPalette = new Palette
        {
            Idle = new Color(0.16f, 0.16f, 0.16f, 0.392f), // 按钮默认：同面板深灰（0.160 / 0.392）
            Hover = new Color(0.28f, 0.28f, 0.28f, 0.45f), // 按钮悬停：略亮一档
            Selected = new Color(0.42f, 0.42f, 0.42f, 0.5f), // 按钮选中：三态中最亮（明显选中）
            TextIdle = new Color(1f, 1f, 1f, 1f), // 默认文字：纯白（原版）
            TextSelected = new Color(1f, 1f, 1f, 1f), // 选中文字：纯白（原版）
            Hint = new Color(0.7f, 0.7f, 0.7f, 1f), // 提示文字：浅灰稍淡
            PanelBackground = new Color(0.160f, 0.160f, 0.160f, 0.392f), // 面板：原版 Panel（0.160 / 0.392）
            InkOutline = new Color(0f, 0f, 0f, 0f), // 描边：透明（原版无描边）
            InnerHighlight = new Color(0f, 0f, 0f, 0f), // 内描边：透明（原版无描边）
            CardFill = new Color(0f, 0f, 0f, 0.392f), // 卡片：原版 ItemEntry（纯黑 0.392）
            PanelShadow = new Color(0f, 0f, 0f, 0f), // 投影：透明（原版无投影）
            SearchFill = new Color(0.196f, 0.196f, 0.196f, 1f), // 搜索框：原版 SearchBar（不透明深灰）
            ScrollbarBg = new Color(0.387f, 0.387f, 0.387f, 0.392f), // 滚动条轨道：原版 ScrollView 背景（0.387 / 0.392）
            ScrollbarHandle = new Color(1f, 1f, 1f, 0.5f), // 滚动条滑块：半透明白（原版白色 Handle，比轨道亮可辨识）
            Placeholder = new Color(0.6f, 0.6f, 0.6f, 0.6f),
        };

        /// <summary>当前样式调色板：Plugin.UiStyle == "Transparent" 时走透明风，否则手绘风（默认）。</summary>
        private static Palette CurrentPalette
        {
            get { return (Plugin.UiStyle == "Transparent") ? TransparentPalette : HandDrawnPalette; }
        }

        // 以下颜色 getter 属性指向当前调色板：所有 ColorIdle/ColorHover/... 引用点代码保持不变，自动走当前样式。
        private static Color ColorIdle { get { return CurrentPalette.Idle; } }
        private static Color ColorHover { get { return CurrentPalette.Hover; } }
        private static Color ColorSelected { get { return CurrentPalette.Selected; } }
        private static Color ColorTextIdle { get { return CurrentPalette.TextIdle; } }
        private static Color ColorTextSelected { get { return CurrentPalette.TextSelected; } }
        private static Color ColorHint { get { return CurrentPalette.Hint; } }
        private static Color PanelBackground { get { return CurrentPalette.PanelBackground; } }
        private static Color ColorInkOutline { get { return CurrentPalette.InkOutline; } }
        private static Color ColorInnerHighlight { get { return CurrentPalette.InnerHighlight; } }
        private static Color ColorCardFill { get { return CurrentPalette.CardFill; } }
        private static Color ColorPanelShadow { get { return CurrentPalette.PanelShadow; } }
        private static Color ColorSearchFill { get { return CurrentPalette.SearchFill; } }
        private static Color ColorScrollbarBg { get { return CurrentPalette.ScrollbarBg; } }
        private static Color ColorScrollbarHandle { get { return CurrentPalette.ScrollbarHandle; } }
        private static Color ColorPlaceholder { get { return CurrentPalette.Placeholder; } }

        /// <summary>构建入口：接收 ItemSpawnerPremiumWindow，创建完整 UI 树并挂载 ItemListView。</summary>
        public static void Setup(ItemSpawnerPremiumWindow window)
        {
            if (window == null)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: Setup abort, window is null.");
                return;
            }
            // 幂等：该窗口已成功 Setup（ItemListView.Initialized）或正在增量构建（Building），避免重复创建分类条/条目。
            ItemListView existing = window.GetComponent<ItemListView>();
            if (existing != null && (existing.Initialized || existing.Building))
            {
                // 但「打开即聚焦搜索框」必须每次打开都做：Setup 由 OnOpen 调用，第二次起会走到这个提前 return。
                // 若只在 Setup 末尾激活，就退化成"仅第一次打开自动聚焦"。故在幂等 return 之前先激活。
                FocusSearch(window);
                return;
            }
            // 字体前置检查（必须在 EnsureCanvas/Build 之前，但放在幂等守卫之后 ——
            // 已建好的面板不该因为字体查询暂时失败而被挡在门外）：
            // ResolveFont 的兜底链末端是 ItemListView.FindFont("LiberationSans SDF")，它可以返回 null。
            // Warmup 路径有 FontFallbackSwapper.instance != null 前置检查，但 F5 兜底路径
            // （OnOpen → Setup）没有。一旦 font 为 null 而 Build 照常执行，各处 `.font = font`
            // 会把 TMP_Text.font 置空，TMP 随后在 GenerateTextMesh 内解引用 m_FontAsset 抛 NRE ——
            // 那是每帧渲染路径，结果是日志刷屏 + 面板不可用。
            // 故此处直接放弃本次 Setup：不创建任何节点、不挂 ItemListView，Initialized 保持 false，
            // 下一次 F5 或 Warmup 轮询会重试（届时字体可能已加载好）。
            // ResolveFont 内含 Resources.FindObjectsOfTypeAll，但装饰字体查询与字符覆盖探测都已静态缓存，
            // 本次检查之后 Build / CreateCategoryBar 的两次调用都命中缓存。
            if (ResolveFont() == null)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 未找到任何可用 TMP 字体，本次跳过 UI 构建（稍后重试）");
                return;
            }

            // 1. 根 Canvas / CanvasScaler / GraphicRaycaster（幂等）
            EnsureCanvas(window);

            // 2. 构建 UI 树（panelRect 已存在则复用，不重复构建）
            if (window.panelRect == null)
            {
                Build(window);
            }

            // 3. 挂载列表视图（复用未 Initialized 的残留 view 而非无条件 AddComponent）
            _view = existing;
            if (_view == null)
            {
                _view = window.gameObject.AddComponent<ItemListView>();
            }
            try
            {
                _view.Init(window.content, window.template, window.searchInput);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: ItemListView.Init 失败，已回滚: " + ex);
                if (_view != null)
                {
                    _view.Stop();  // 退订搜索监听 + 语言事件
                    UnityEngine.Object.Destroy(_view);
                    _view = null;
                }
                return;
            }

            // 4. 刷新分类按钮选中态
            OnMajorSelected(MajorCategory.All);

            // 5. 打开即聚焦搜索框（放最后：此时 UI 树已完整、searchInput 已回填）
            FocusSearch(window);
        }

        /// <summary>
        /// 激活搜索框输入焦点（"打开即搜索"）。
        ///
        /// 为什么需要它：窗口的 selectOnOpen/objectToSelectOnOpen 走的是
        /// MenuWindow.SelectStartingElement → UIInputHandler.SetSelectedObject，而后者
        /// （反编译 UIInputHandler.cs:69-75）**只在 InputHandler.GetCurrentUsedInputScheme() == Gamepad**
        /// 时才 EventSystem.SetSelectedGameObject，键鼠方案下是彻底的空操作；
        /// 且 TMP_InputField 即使被 Select 也仍需 ActivateInputField() 才会接收键入。
        /// 结果就是键鼠玩家每次打开面板都得多点一次搜索框。
        ///
        /// 时序：Setup 由 OnOpen 调用，此刻 MenuWindow.Open 还没执行 SelectStartingElement()。
        /// 但如上所述键鼠下它是空操作，不会把焦点抢走；Gamepad 下它会 SetSelectedGameObject(searchInput)，
        /// 而 TMP_InputField.ActivateInputFieldInternal 本身也会把自己设为 selected（TMP_InputField.cs:3805），
        /// 目标一致，不冲突。
        ///
        /// ActivateInputField 内部要求 IsActive() && IsInteractable()（TMP_InputField.cs:3788），
        /// 且真正激活发生在下一次 LateUpdate（m_ShouldActivateNextUpdate）。故 Warmup 预热路径
        /// （面板 inactive）调用它是安全的空操作，这里仍加 isOpen 守卫避免无谓调用。
        /// </summary>
        private static void FocusSearch(ItemSpawnerPremiumWindow window)
        {
            if (window == null || !window.isOpen || window.searchInput == null)
            {
                return;
            }
            window.searchInput.ActivateInputField();
        }

        private static void EnsureCanvas(ItemSpawnerPremiumWindow window)
        {
            // Canvas/CanvasScaler/GraphicRaycaster 已在 ItemSpawnerPremiumWindow.Awake 创建（挂子物体 canvasObject），
            // 这里只做幂等配置（窗口根保持 active，Canvas 作为子物体）。
            GameObject canvasGo = window.canvasObject;
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 子 Canvas RectTransform 拉伸到全屏（Awake 已设，这里幂等）
            RectTransform root = canvasGo.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
        }

        private static void Build(ItemSpawnerPremiumWindow window)
        {
            EnsureSprites();
            // UI 树挂在子 Canvas（canvasObject）下，而不是窗口根
            RectTransform root = window.canvasObject.GetComponent<RectTransform>();

            RectTransform panelRt = CreatePanel(root);
            window.panelRect = panelRt;

            TMP_FontAsset font = ResolveFont();

            TMP_InputField searchInput = CreateSearchInput(panelRt, font);
            window.searchInput = searchInput;

            // 先建滚动列表（含条目模板），再建分类条（分类条 SetAsLastSibling 置于最上层，保证可点击）
            RectTransform content;
            CreateScrollView(panelRt, out content);
            window.content = content;

            Transform template = CreateItemEntryTemplate(content, font);
            window.template = template;

            // 右键收藏提示（分类条下方、滚动列表上方一行居中；置于分类条之前创建，保持分类条始终为最上层兄弟）
            CreateRightClickHint(panelRt, font);

            CreateCategoryBar(panelRt, (RectTransform)searchInput.transform);
        }

        /// <summary>
        /// 解析当前语言应使用的字体。**这是全模组唯一的字体决策入口**，
        /// UiEnhancer 构建的节点与 ItemListView 动态刷新的节点（物品名、搜索框文字/占位符）
        /// 必须都走它，否则两边会得出不同结论 —— 例如波兰语下 UiEnhancer 已降级到主字体，
        /// 而 ItemListView 仍按自己缓存的装饰字体给物品名赋值，物品名照样显示方块。
        /// </summary>
        internal static TMP_FontAsset ResolveFont()
        {
            // CJK/西里尔语言直接走游戏主字体（含 fallback 链）：装饰字体连基本字形都没有，无需探测。
            // 注意 ItemListView.NeedsCjkFont() 只覆盖 简/繁/日/韩/俄/乌，剩下 9 种拉丁语言里
            // pl/tr/de/fr/it/es/pt-BR 的文案含大量 Latin-1 Supplement / Latin Extended-A 字符
            // （ę ż ü ğ ş ı É í ñ）以及 U+2014 em dash，DarumaDropOne 这类日系手绘装饰字体
            // 的拉丁子集未必覆盖 —— 2.1.0 已因同样原因把俄/乌划归主字体。
            // 由于不能修改 ItemListView.NeedsCjkFont()，这里改为「用当前语言的真实文案去探测装饰字体」，
            // 覆盖不全就降级到主字体，从而对未来新增语言也自动生效。
            //
            // 注意 Loc 依赖注入：BuildUiProbeText 走 Loc.Get，而 Loc 的语言代码提供者由
            // Plugin.Awake 注入。若在注入之前调用，Loc 会回退英文文案 —— 那会让非英文语言
            // 探测到"英文文案全覆盖"而错误保留装饰字体。实际调用链上 Setup/Build/RefreshButtonLabels
            // 都发生在 Plugin.Awake 之后（窗口由 GUIManager.Start 的 Postfix 创建），故安全；
            // 但覆盖探测结果是按语言缓存的，若将来有更早的调用点，必须同时清 _coverageProbed。
            if (ItemListView.NeedsCjkFont())
            {
                return ItemListView.GetGameBaseFont();
            }
            TMP_FontAsset decorative = GetDecorativeFont();
            if (decorative != null && DecorativeFontCoversUiText(decorative))
            {
                return decorative;
            }
            return ItemListView.GetGameBaseFont();
        }

        /// <summary>
        /// 取装饰字体（含负缓存）：避免每次 ResolveFont 都跑一遍
        /// ItemListView.FindFont → Resources.FindObjectsOfTypeAll&lt;TMP_FontAsset&gt;() 全量遍历。
        /// </summary>
        private static TMP_FontAsset GetDecorativeFont()
        {
            // TMP_FontAsset 是 UnityEngine.Object，== null 同时覆盖「从未查到」与「查到后资源被卸载/销毁」，
            // 后者必须重查（字体资源可能随场景卸载后又被重新加载）。
            if (_decorativeFont != null)
            {
                return _decorativeFont;
            }
            if (_decorativeFontMissing)
            {
                return null; // 上次遍历确认不存在，不再重复全量遍历
            }
            _decorativeFont = ItemListView.FindFont("DarumaDropOne-Regular SDF");
            if (_decorativeFont == null)
            {
                // 只有在「字体系统确实已就绪」时才转入负缓存。
                // Setup 可能在 TMP 字体资源加载完成之前就被 F5 路径触发（Warmup 路径有
                // FontFallbackSwapper.instance != null 前置检查，F5 路径没有），此时 FindFont
                // 找不到装饰字体只是"还没加载"，若无条件记成"不存在"，之后即使字体加载好了
                // 也永远走不回装饰字体 —— 英文玩家会永久失去手绘风。
                // 用「游戏主字体是否已能取到」作为就绪判据：取不到说明整个字体系统都没起来，
                // 本次不记负缓存，下次重查。
                _decorativeFontMissing = ItemListView.GetGameBaseFont() != null;
            }
            return _decorativeFont;
        }

        /// <summary>
        /// 探测装饰字体是否覆盖「当前语言实际要显示的 UI 文案字符集」。
        /// 结果按「语言 + 字体实例」缓存：语言切换或字体资源被替换后键不匹配即自动重探，
        /// 无需在别处手动清缓存。
        /// </summary>
        private static bool DecorativeFontCoversUiText(TMP_FontAsset font)
        {
            LocalizedText.Language language = LocalizedText.CURRENT_LANGUAGE;
            int fontId = font.GetInstanceID();
            if (_coverageProbed && _coverageLanguage == language && _coverageFontId == fontId)
            {
                return _coverageResult;
            }

            string probe = BuildUiProbeText();
            bool covered;
            uint[] missing = null;
            try
            {
                // 关键：searchFallbacks 与 tryAddCharacter 都必须传 false。
                // 1) tryAddCharacter=true 时，TMP_FontAsset.HasCharacters（反编译 TMP_FontAsset.cs:1108）
                //    会对 atlasPopulationMode == Dynamic / DynamicOS 的字体现场 TryAddCharacterInternal
                //    并返回 true —— 那是"能动态栅格化"，不代表这个装饰字体本身有该字形，
                //    探测会假阳性（而且会真的往图集里塞字，产生副作用）。传 false 只查 characterLookupTable。
                // 2) searchFallbacks=true 会连 fallbackFontAssetTable / TMP_Settings 一起算"覆盖"，
                //    结果是渲染时逐字混用主字体的字形，标签变成两种字体拼接，观感比统一用主字体更差。
                //    故这里要求装饰字体自身完整覆盖，否则整体降级 —— 排版一致优先。
                covered = font.HasCharacters(probe, out missing, false, false);
            }
            catch (Exception ex)
            {
                // 探测本身失败（字体资源损坏、characterLookupTable 构建异常等）：保守判为不覆盖，
                // 降级到游戏主字体。宁可失去手绘装饰风，也不能显示方块。
                Plugin.Log.LogWarning("ItemSpawnerPremium: 装饰字体字符探测失败，改用游戏主字体: " + ex.Message);
                covered = false;
            }

            _coverageProbed = true;
            _coverageLanguage = language;
            _coverageFontId = fontId;
            _coverageResult = covered;
            if (!covered)
            {
                // 每种语言只会记一次（探测结果已缓存），不会刷屏
                Plugin.Log.LogInfo("ItemSpawnerPremium: 装饰字体缺少当前语言字形（"
                    + DescribeMissing(missing) + "），界面改用游戏主字体");
            }
            return covered;
        }

        /// <summary>
        /// 拼出探测用字符串：当前语言的 10 条 UI 文案。
        /// 不探测物品名：一是 150+ 条开销大，二是 ResolveFont 在 Setup 最早期就会被调用，
        /// 此时 ItemListView 目录还没建起来，取不到显示名。
        /// 支持全大写的语言额外追加大写形态：分类/收藏按钮 label 用 FontStyles.UpperCase 渲染，
        /// TMP 在 GenerateTextMesh 里逐字 char.ToUpper（反编译 TMP_Text.cs:3586），
        /// 真正需要的字形是大写形态（fr 的 é→É、pl 的 ż→Ż）。
        /// 同时取当前区域与不变区域两种大写结果：char.ToUpper 走 CurrentCulture，
        /// 土耳其区域下 i→İ(U+0130) 与不变区域的 i→I 不同，两者都要覆盖才算安全。
        /// </summary>
        private static string BuildUiProbeText()
        {
            bool allCaps = LocalizedText.languageSupportsAllCaps;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
            for (int i = 0; i < UiTextKeys.Length; i++)
            {
                string value = Loc.Get(UiTextKeys[i]);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }
                sb.Append(value);
                if (allCaps)
                {
                    sb.Append(value.ToUpperInvariant());
                    sb.Append(value.ToUpper());
                }
            }
            sb.Append(GetLanguageDiacritics());
            return sb.ToString();
        }

        /// <summary>
        /// 当前语言的完整变音字母集（大小写成对，含物品名会用到但 UI 文案里没出现的字符）。
        ///
        /// 为什么需要它：探测只取 10 条 UI 文案，但游戏侧物品名同样含扩展拉丁字符，
        /// 且用到的字母不一定出现在 UI 文案里 —— 例如波兰语 "ZWÓJ ANTYSZNURA" 的 Ó、
        /// 土耳其语 "ANTİ-HALAT MAKARASI" 的 İ、"KIRMIZI ÇITIRYEMİŞ" 的 Ç，
        /// pl.json / tr.json 里都没有。逐条探测 150+ 物品名开销大且时机不对
        /// （ResolveFont 在 Setup 最早期调用，此时目录还没建），
        /// 改为按语言补一个固定的字母表常量：字符集封闭、零查询开销，且能覆盖物品名。
        /// 只列该语言正字法真正使用的字母，不搞"全 Latin-1 全都要"——
        /// 那会让装饰字体因为缺一个用不到的字形而被无谓地整体降级。
        /// </summary>
        private static string GetLanguageDiacritics()
        {
            switch (LocalizedText.CURRENT_LANGUAGE)
            {
                case LocalizedText.Language.Polish:
                    return "ąĄćĆęĘłŁńŃóÓśŚźŹżŻ";
                case LocalizedText.Language.Turkish:
                    // 土耳其语特有的点/无点 i 对（ı U+0131 / İ U+0130）最容易缺字
                    return "çÇğĞıİöÖşŞüÜ";
                case LocalizedText.Language.German:
                    return "äÄöÖüÜß";
                case LocalizedText.Language.French:
                    return "àÀâÂæÆçÇéÉèÈêÊëËîÎïÏôÔœŒùÙûÛüÜÿŸ";
                case LocalizedText.Language.Italian:
                    return "àÀèÈéÉìÌîÎòÒóÓùÙ";
                case LocalizedText.Language.SpanishSpain:
                case LocalizedText.Language.SpanishLatam:
                    return "áÁéÉíÍñÑóÓúÚüÜ¡¿";
                case LocalizedText.Language.BRPortuguese:
                    return "áÁàÀâÂãÃçÇéÉêÊíÍóÓôÔõÕúÚ";
                default:
                    // 英语（以及未来新增的未知语言）：不补充。
                    // 未知语言若真有扩展字符，UI 文案本身的探测仍会兜住。
                    return string.Empty;
            }
        }

        /// <summary>
        /// 把缺失码点列表格式化成日志文本（去重后最多列 8 个，避免长串刷日志）。
        ///
        /// 必须去重：探测串刻意包含「原文 + ToUpperInvariant + ToUpper」三份（见 BuildUiProbeText），
        /// 同一个缺失字符会在 HasCharacters 的 missing 数组里出现多次。
        /// 实测土耳其语原始输出是 "U+011F U+011E U+011E U+015F U+015E U+015E U+011F U+0131 …共 17 个"
        /// —— 8 个位置里有 3 个是重复的，既浪费展示位又让人误判缺失字符数量。
        /// </summary>
        private static string DescribeMissing(uint[] missing)
        {
            if (missing == null || missing.Length == 0)
            {
                return "字符表不可用";
            }
            List<uint> unique = new List<uint>(missing.Length);
            for (int i = 0; i < missing.Length; i++)
            {
                if (!unique.Contains(missing[i]))
                {
                    unique.Add(missing[i]);
                }
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
            int shown = unique.Count < 8 ? unique.Count : 8;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append("U+").Append(unique[i].ToString("X4"));
            }
            if (unique.Count > shown)
            {
                sb.Append(" …共 ").Append(unique.Count).Append(" 个");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 给 TMP 文本套字体，font 为 null 时保留组件默认字体。
        /// Setup 开头已做过 ResolveFont() 非 null 的前置检查，这里是第二道保险：
        /// TMP_Text.font = null 会让 TMP 在 GenerateTextMesh 里解引用 m_FontAsset 抛 NRE，
        /// 而那是每帧渲染路径（日志刷屏 + 面板不可用）。做法与 ItemListView.RefreshFonts 的
        /// `if (font == null) return;` 一致。
        /// </summary>
        private static void ApplyFont(TMP_Text text, TMP_FontAsset font)
        {
            if (text == null || font == null)
            {
                return;
            }
            text.font = font;
        }

        private static RectTransform CreatePanel(RectTransform root)
        {
            // 纸张投影：面板下方略大、略深的暖棕半透明圆角片，制造"贴纸/卡纸浮起"的层次
            GameObject shadowGo = new GameObject("PanelShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform shadowRt = (RectTransform)shadowGo.transform;
            shadowRt.SetParent(root, false);
            shadowRt.anchorMin = new Vector2(0.1834f, 0.1068f);
            shadowRt.anchorMax = new Vector2(0.8188f, 0.8955f);
            shadowRt.offsetMin = new Vector2(-8f, -16f);   // 比面板略大一圈，向下偏移模拟顶部光源
            shadowRt.offsetMax = new Vector2(8f, 0f);
            Image shadowImg = shadowGo.GetComponent<Image>();
            ApplySprite(shadowImg, _shadowSprite);
            shadowImg.raycastTarget = false;

            GameObject go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0.1834f, 0.1068f);
            rt.anchorMax = new Vector2(0.8188f, 0.8955f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = go.GetComponent<Image>();
            ApplySprite(bg, _panelSprite); // 圆角牛皮纸面板（描边已烘进 Sprite）
            bg.raycastTarget = true;
            return rt;
        }

        /// <summary>搜索框：顶部居中、加宽加高（高 50，占 12~62px）。</summary>
        private static TMP_InputField CreateSearchInput(RectTransform panel, TMP_FontAsset font)
        {
            GameObject go = new GameObject("SearchInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            // 与分类条（CreateCategoryBar 的 0.05/0.95）用同一组水平锚点：
            // 原值 0.07/0.93 使搜索框左右各比分类条内缩 2% 面板宽，两者边缘不齐，
            // 视觉上像是"搜索框被无意缩了一圈"。统一到分类条的锚点（搜索框变宽）。
            rt.anchorMin = new Vector2(0.05f, 1f);
            rt.anchorMax = new Vector2(0.95f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -12f);
            rt.sizeDelta = new Vector2(0f, 50f);

            Image bg = go.GetComponent<Image>();
            ApplySprite(bg, _searchSprite); // 圆角暖卡其输入槽（描边已烘进 Sprite）
            bg.raycastTarget = true;

            // 文本显示区（RectMask2D 裁剪超长输入）
            GameObject areaGo = new GameObject("Text Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(RectMask2D));
            RectTransform areaRt = (RectTransform)areaGo.transform;
            areaRt.SetParent(rt, false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(14f, 5f);
            areaRt.offsetMax = new Vector2(-14f, -5f);

            // 占位符
            GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform phRt = (RectTransform)phGo.transform;
            phRt.SetParent(areaRt, false);
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            TextMeshProUGUI placeholder = phGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(placeholder, font);
            placeholder.fontSize = 24f;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = ColorPlaceholder;
            placeholder.text = Loc.Get("searchPlaceholder");
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.raycastTarget = false;

            // 输入文本
            // 命名为 SearchText 而非通用的 "Text"：OnStyleChanged 按 gameObject 名重设文字颜色，
            // 通用名容易与其它来源的同名组件冲突。
            GameObject textGo = new GameObject("SearchText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.SetParent(areaRt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(text, font);
            text.fontSize = 24f;
            text.color = ColorTextIdle;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            TMP_InputField input = go.GetComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }

        /// <summary>滚动列表：ScrollRect + Viewport + Content（GridLayoutGroup 正方形网格自增高）。</summary>
        private static void CreateScrollView(RectTransform panel, out RectTransform content)
        {
            GameObject scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect));
            RectTransform scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(panel, false);
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.pivot = new Vector2(0.5f, 0.5f);
            // 顶部缩进 150px，为顶部搜索框（12~62px）+ 分类条（68~118px）+ 右键提示（123~145px）让位
            scrollRt.offsetMin = new Vector2(20f, 20f);
            scrollRt.offsetMax = new Vector2(-20f, -150f);

            GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            RectTransform viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.SetParent(scrollRt, false);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            // 右侧让出 16px（14px 滚动条 + 2px 余量）：滚动条是 scrollRt 的子节点、位置相对 scrollRt 靠右，
            // 且比 Viewport 后创建（在上层）+ raycastTarget=true；而 verticalScrollbarVisibility=Permanent
            // **不会**自动收缩 viewport（只有 AutoHideAndExpandViewport 会）。
            // 若 Viewport 仍占满，Grid 右 padding 只有 8px < 14px，某些分辨率下最右列卡片的右缘会被
            // 滚动条压住，该区域的左键生成/右键收藏都被滚动条吞掉。
            // 收缩 Viewport 后 Content（anchor 0,1 → 1,1）随之变窄，Grid 列数可能相应减少一列 —— 这是期望行为；
            // 滚动条自身位置不受影响（它挂在 scrollRt 上，不是 viewport 的子节点）。
            viewportRt.offsetMax = new Vector2(-16f, 0f);
            Image viewportImg = viewportGo.GetComponent<Image>();
            viewportImg.sprite = null;
            viewportImg.color = new Color(1f, 1f, 1f, 0.01f);
            Mask mask = viewportGo.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            RectTransform contentRt = (RectTransform)contentGo.transform;
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            GridLayoutGroup grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(120f, 120f);    // 正方形格子（120×120）
            grid.spacing = new Vector2(12f, 12f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.Flexible;   // 自动换行
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;

            ContentSizeFitter csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewportRt;
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            // 垂直滚动条（Unity 标准结构：Scrollbar → Sliding Area → Handle）
            GameObject scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            RectTransform scrollbarRt = (RectTransform)scrollbarGo.transform;
            scrollbarRt.SetParent(scrollRt, false);
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 0.5f);
            scrollbarRt.anchoredPosition = Vector2.zero;
            scrollbarRt.sizeDelta = new Vector2(14f, 0f);
            Image scrollbarImg = scrollbarGo.GetComponent<Image>();
            ApplySprite(scrollbarImg, _scrollbarBgSprite, Image.Type.Simple);
            scrollbarImg.raycastTarget = true;

            GameObject slidingAreaGo = new GameObject("Sliding Area", typeof(RectTransform));
            RectTransform slidingAreaRt = (RectTransform)slidingAreaGo.transform;
            slidingAreaRt.SetParent(scrollbarRt, false);
            slidingAreaRt.anchorMin = Vector2.zero;
            slidingAreaRt.anchorMax = Vector2.one;
            slidingAreaRt.offsetMin = new Vector2(4f, 4f);
            slidingAreaRt.offsetMax = new Vector2(-4f, -4f);

            GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform handleRt = (RectTransform)handleGo.transform;
            handleRt.SetParent(slidingAreaRt, false);
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            Image handleImg = handleGo.GetComponent<Image>();
            ApplySprite(handleImg, _scrollbarHandleSprite, Image.Type.Simple);
            handleImg.raycastTarget = true;

            Scrollbar sb = scrollbarGo.GetComponent<Scrollbar>();
            sb.handleRect = handleRt;
            sb.targetGraphic = handleImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            content = contentRt;
        }

        /// <summary>
        /// 条目模板：根有 Button，子节点命名必须为 "ItemName"（TextMeshProUGUI）与 "ItemIcon"（RawImage），
        /// 这是 ItemListView.Rebuild 依赖的结构。
        /// </summary>
        private static Transform CreateItemEntryTemplate(Transform content, TMP_FontAsset font)
        {
            GameObject go = new GameObject("ItemEntry", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(content, false);
            rt.sizeDelta = new Vector2(120f, 120f);

            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = 120f;
            layout.preferredHeight = 120f;
            layout.flexibleWidth = 0f;

            Image bg = go.GetComponent<Image>();
            ApplySprite(bg, _cardSprite); // 圆角奶油卡纸卡片（描边已烘进 Sprite）
            bg.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;

            // 按下反馈（白 → 压暗灰白）：挂在条目根，随模板被 Instantiate 克隆到每个条目；与 onClick / 右键收藏共存
            go.AddComponent<PressFeedback>();

            // 图标（顶部居中，72×72，放大后与底部文字仍留 2px 间隙不重叠）
            GameObject iconGo = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform iconRt = (RectTransform)iconGo.transform;
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -6f);
            iconRt.sizeDelta = new Vector2(72f, 72f);
            RawImage icon = iconGo.GetComponent<RawImage>();
            icon.raycastTarget = false;

            // 文字（底部居中，字号 14 仍可读，最多 2 行，超出省略号）
            GameObject nameGo = new GameObject("ItemName", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform nameRt = (RectTransform)nameGo.transform;
            nameRt.SetParent(rt, false);
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0f, 4f);
            nameRt.sizeDelta = new Vector2(-10f, 36f);
            TextMeshProUGUI name = nameGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(name, font);
            name.fontSize = 14f;
            name.color = ColorTextIdle;
            name.alignment = TextAlignmentOptions.Center;   // 居中
            name.textWrappingMode = TextWrappingModes.Normal; // 换行（enableWordWrapping 已弃用）
            name.overflowMode = TextOverflowModes.Ellipsis;  // 超出省略号
            name.maxVisibleLines = 2;                        // 最多 2 行
            name.raycastTarget = false;

            // 心形标记（右上角，收藏时显示，适配 120×120 卡）
            GameObject favGo = new GameObject("Favorite", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform favRt = (RectTransform)favGo.transform;
            favRt.SetParent(rt, false);
            favRt.anchorMin = new Vector2(1f, 1f);
            favRt.anchorMax = new Vector2(1f, 1f);
            favRt.pivot = new Vector2(1f, 1f);
            favRt.anchoredPosition = new Vector2(-4f, -4f);
            favRt.sizeDelta = new Vector2(26f, 26f);
            RawImage favImg = favGo.GetComponent<RawImage>();
            favImg.texture = GetHeartTexture();
            favImg.color = Color.white; // 红色填充 + 深暖棕描边已烘进纹理，color 置白避免二次染色
            favImg.raycastTarget = false;
            favGo.SetActive(false); // 默认隐藏

            // 模板返回前必须置 inactive。
            // ItemListView.CreateEntry 用 Instantiate(_template, _content) 克隆，clone 继承模板的 active 状态；
            // 而 _template.SetActive(false) 与首次 Rebuild() 都在增量构建循环**结束之后**才执行
            // （ItemListView.BuildAllEntriesIncremental）。模板若是 active：
            //   1) 首次打开面板时约 ceil(153/24)≈7 帧内，条目按数据库原始顺序逐批"闪入"（含本该被
            //      HideUnused 过滤掉的项），最后一帧才因 Rebuild 突然全量重排；
            //   2) 模板自身作为一张空白卡参与 Grid 布局，且带 PressFeedback，能被玩家按压变色。
            // 置 inactive 后 clone 生成即隐藏，由 Rebuild 统一 SetActive(true)；模板不在 _all 里，
            // 不会被 Rebuild 显示出来，与循环后的 _template.SetActive(false) 行为一致。
            go.SetActive(false);

            return rt;
        }

        /// <summary>
        /// 程序化生成 64x64 手绘风心形纹理（静态缓存）：红色填充 + 深暖棕勾线描边。
        /// 用隐式心形方程 f=(x²+y²−1)³ − x²·y³ 的 SDF 值做阈值分层（内部红填充 → 边缘深暖棕勾线 → 外部透明），
        /// 4×4 超采样抗锯齿；描边色直接烘进纹理，0 额外组件。
        /// </summary>
        private static Texture2D GetHeartTexture()
        {
            if (_heartTexture != null)
            {
                return _heartTexture;
            }
            const int size = 64;
            const int samplesPerAxis = 4;
            const float cx = 32f;             // 心形中心 x（归一化坐标原点对应的像素）
            const float cy = 29f;             // 心形中心 y（略低于几何中心，为底部尖角留白）
            const float scale = 22f;          // 心形缩放（归一化单位 → 像素）
            const float lobeStrength = 0.6f;  // 两瓣强度（=1 为标准心形 V 槽深；<1 更圆润饱满、V 槽更浅、两侧更平滑）

            Color fill = new Color(0.86f, 0.32f, 0.34f, 1f); // 暖红填充（与原 favImg.color 一致）
            Color outline = ColorInkOutline;                  // 深暖棕勾线（与全局"勾线"色一致，手绘统一）
            // 勾线描边半宽（像素），略收窄让描边更柔和自然。
            // 透明风的 InkOutline 是全透明色：此时若仍保留 1.4px 的描边圈，
            // 边界内侧一圈会被写成 alpha=0，等于把心形整体"腐蚀"掉 1.4px（视觉上更小且边缘发虚）。
            // 故描边色透明时把描边宽度归零，让实心区域直达边界 —— 与滚动条 sprite 的既有做法一致
            // （EnsureSprites 对滚动条传 outlineWidth=0 + Color.clear）。
            float outlineHalf = (outline.a <= 0f) ? 0f : 1.4f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "ItemSpawnerPremium Heart";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < samplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < samplesPerAxis; sx++)
                        {
                            float px = x + (sx + 0.5f) / samplesPerAxis;
                            float py = y + (sy + 0.5f) / samplesPerAxis;
                            float nx = (px - cx) / scale;
                            float ny = (py - cy) / scale;
                            float u = nx * nx + ny * ny - 1f;
                            // 隐式心形方程（lobeStrength 减弱两瓣强度 → 顶部 V 槽更浅、两侧更平滑、整体更圆润饱满）：
                            // 内部 f<0，边界 f=0，外部 f>0
                            float f = u * u * u - lobeStrength * nx * nx * ny * ny * ny;
                            // 解析梯度 ∇f（含 lobeStrength 系数），用于把 f 归一化为近似带符号距离
                            float gx = 6f * nx * u * u - 2f * lobeStrength * nx * ny * ny * ny;
                            float gy = 6f * ny * u * u - 3f * lobeStrength * nx * nx * ny * ny;
                            float glen = Mathf.Sqrt(gx * gx + gy * gy);
                            if (glen < 1e-3f)
                            {
                                glen = 1e-3f; // 尖点处梯度趋近 0，钳制防除零
                            }
                            float dist = f / glen * scale; // 带符号距离（像素，负 = 内部）
                            Color c = SampleHeart(dist, outlineHalf, fill, outline);
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }
                    int n = samplesPerAxis * samplesPerAxis;
                    pixels[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(r / n) * 255f),
                        (byte)(Mathf.Clamp01(g / n) * 255f),
                        (byte)(Mathf.Clamp01(b / n) * 255f),
                        (byte)(Mathf.Clamp01(a / n) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _heartTexture = texture;
            return _heartTexture;
        }

        /// <summary>心形 SDF 采样分层：深入内部 → 红填充；仅边界内侧一圈 → 深暖棕勾线；外部 → 透明。</summary>
        private static Color SampleHeart(float dist, float outlineHalf, Color fill, Color outline)
        {
            if (dist < -outlineHalf) { return fill; }   // 内部主体 → 红填充
            if (dist < 0f) { return outline; }          // 仅边界内侧 → 深暖棕勾线（外侧透明，消除尖点/两侧的溢出棕像素）
            return new Color(0f, 0f, 0f, 0f);            // 外部 → 透明
        }

        /// <summary>确保所有烘焙 Sprite 已生成（描边/白边/填充烘进纹理，0 层 Outline）。</summary>
        private static void EnsureSprites()
        {
            GetCardSprite(ref _panelSprite, "ItemSpawnerPremium Panel", 16f, 2.5f, ColorInkOutline, ColorInnerHighlight, PanelBackground);
            GetCardSprite(ref _cardSprite, "ItemSpawnerPremium Card", 9f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorCardFill);
            GetCardSprite(ref _searchSprite, "ItemSpawnerPremium Search", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorSearchFill);
            GetCardSprite(ref _btnIdleSprite, "ItemSpawnerPremium BtnIdle", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorIdle);
            GetCardSprite(ref _btnHoverSprite, "ItemSpawnerPremium BtnHover", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorHover);
            GetCardSprite(ref _btnSelectedSprite, "ItemSpawnerPremium BtnSelected", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorSelected);
            // 滚动条（轨道 14px 宽、滑块约 6px 宽）：不烘描边。
            // 64×64 纹理以 Simple 拉伸到细长条时，1px 的描边会被横向压到 0.2px 以下糊成竖线；
            // 而 9-slice border（≥8）又大于控件宽度导致 Sliced 退化。故只保留圆角纯色填充。
            GetCardSprite(ref _scrollbarBgSprite, "ItemSpawnerPremium ScrollBg", 6f, 0f, Color.clear, Color.clear, ColorScrollbarBg);
            GetCardSprite(ref _scrollbarHandleSprite, "ItemSpawnerPremium ScrollHandle", 6f, 0f, Color.clear, Color.clear, ColorScrollbarHandle);
            GetCardSprite(ref _shadowSprite, "ItemSpawnerPremium Shadow", 16f, 0f, Color.clear, Color.clear, ColorPanelShadow);
        }

        /// <summary>按字段惰性生成并缓存卡片 Sprite（描边/白边/填充全部烘进纹理）。</summary>
        private static Sprite GetCardSprite(ref Sprite field, string name, float radius, float outlineWidth, Color ink, Color inner, Color fill)
        {
            if (field == null)
            {
                field = CreateCardSprite(name, radius, outlineWidth, ink, inner, fill);
            }
            return field;
        }

        /// <summary>给 Image 套上烘焙好的 9-slice Sprite（描边已烘进纹理，不再叠 Outline 组件）。</summary>
        private static void ApplySprite(Image image, Sprite sprite, Image.Type type = Image.Type.Sliced)
        {
            image.sprite = sprite;
            image.type = type;
            image.color = Color.white;
        }

        /// <summary>销毁烘焙 Sprite 及其纹理（样式热重载时释放旧资源，避免 HideAndDontSave 累积泄漏）。</summary>
        private static void DestroySprite(ref Sprite field)
        {
            if (field == null)
            {
                return;
            }
            if (field.texture != null)
            {
                UnityEngine.Object.Destroy(field.texture);
            }
            UnityEngine.Object.Destroy(field);
            field = null;
        }

        /// <summary>样式热重载：清空 Sprite 缓存重新烘焙，并按 sprite 名遍历窗口所有 Image 重新套用。</summary>
        public static void OnStyleChanged()
        {
            bool hasWindow = Plugin.Window != null && Plugin.Window.canvasObject != null;

            // 1. 先快照「Image → 旧 sprite 名」。必须在销毁旧 Sprite 之前采集：
            //    虽然 Destroy 延迟到帧末、当帧仍可读 sprite.name，但依赖该延迟语义很脆弱；
            //    先快照后销毁可彻底消除"读取已销毁 Sprite"的时序风险。
            List<Image> spriteTargets = null;
            List<string> spriteNames = null;
            if (hasWindow)
            {
                Image[] images = Plugin.Window.canvasObject.GetComponentsInChildren<Image>(true);
                spriteTargets = new List<Image>(images.Length);
                spriteNames = new List<string>(images.Length);
                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null || img.sprite == null)
                    {
                        continue;
                    }
                    spriteTargets.Add(img);
                    spriteNames.Add(img.sprite.name);
                }
            }

            // 2. 清空缓存（EnsureSprites 惰性烘焙，字段为 null 会按新样式重新生成）
            if (_heartTexture != null)
            {
                UnityEngine.Object.Destroy(_heartTexture);
                _heartTexture = null;
            }
            DestroySprite(ref _panelSprite);
            DestroySprite(ref _cardSprite);
            DestroySprite(ref _searchSprite);
            DestroySprite(ref _btnIdleSprite);
            DestroySprite(ref _btnHoverSprite);
            DestroySprite(ref _btnSelectedSprite);
            DestroySprite(ref _scrollbarBgSprite);
            DestroySprite(ref _scrollbarHandleSprite);
            DestroySprite(ref _shadowSprite);
            EnsureSprites();

            if (!hasWindow)
            {
                return; // 窗口尚未创建，无需套用（下次 Setup 会用新 Sprite）
            }

            // 3. 按快照的旧 sprite 名套用新 Sprite（保持按钮当前三态/各元素角色不变）
            for (int i = 0; i < spriteTargets.Count; i++)
            {
                Image img = spriteTargets[i];
                if (img == null)
                {
                    continue;
                }
                Sprite s = FindSpriteByName(spriteNames[i]);
                if (s != null)
                {
                    // 滚动条用 Simple（9-slice border 大于控件尺寸会退化），其余保持 Sliced
                    ApplySprite(img, s, IsScrollbarSpriteName(spriteNames[i]) ? Image.Type.Simple : Image.Type.Sliced);
                }
            }

            // 心形纹理随样式重烤：重新生成后刷新所有 "Favorite" RawImage 引用（旧纹理已在上方 Destroy）
            Texture2D heart = GetHeartTexture();
            RawImage[] raws = Plugin.Window.canvasObject.GetComponentsInChildren<RawImage>(true);
            for (int i = 0; i < raws.Length; i++)
            {
                RawImage raw = raws[i];
                if (raw != null && raw.gameObject.name == "Favorite")
                {
                    raw.texture = heart;
                }
            }

            // 重设分类/收藏按钮 label 文字颜色（含选中态，三态文字颜色走新调色板）
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                RefreshButtonColor(i);
            }
            RefreshFavoriteButtonColor();

            // 遍历所有文字组件，按名字重设固定角色的文字颜色（按钮 label 已由上面 Refresh 处理，这里跳过 "Label"）
            TextMeshProUGUI[] texts = Plugin.Window.canvasObject.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TextMeshProUGUI t = texts[i];
                if (t == null)
                {
                    continue;
                }
                string goName = t.gameObject.name;
                if (goName == "ItemName" || goName == "SearchText")
                {
                    t.color = ColorTextIdle;
                }
                else if (goName == "RightClickHint")
                {
                    t.color = ColorHint;
                }
                else if (goName == "Placeholder")
                {
                    t.color = ColorPlaceholder;
                }
                // "Label" 由按钮 Refresh 处理，跳过
            }
        }

        /// <summary>按烘焙 Sprite 名返回对应缓存 Sprite（样式热重载时遍历套用）。</summary>
        private static Sprite FindSpriteByName(string name)
        {
            switch (name)
            {
                case "ItemSpawnerPremium Panel": return _panelSprite;
                case "ItemSpawnerPremium Card": return _cardSprite;
                case "ItemSpawnerPremium Search": return _searchSprite;
                case "ItemSpawnerPremium BtnIdle": return _btnIdleSprite;
                case "ItemSpawnerPremium BtnHover": return _btnHoverSprite;
                case "ItemSpawnerPremium BtnSelected": return _btnSelectedSprite;
                case "ItemSpawnerPremium ScrollBg": return _scrollbarBgSprite;
                case "ItemSpawnerPremium ScrollHandle": return _scrollbarHandleSprite;
                case "ItemSpawnerPremium Shadow": return _shadowSprite;
                default: return null;
            }
        }

        /// <summary>滚动条 Sprite 需用 Image.Type.Simple（9-slice border 大于控件宽度会退化）。</summary>
        private static bool IsScrollbarSpriteName(string name)
        {
            return name == "ItemSpawnerPremium ScrollBg" || name == "ItemSpawnerPremium ScrollHandle";
        }

        /// <summary>用 SDF 生成带描边/白边/填充的 9-slice 卡片 Sprite（64×64，4×4 超采样抗锯齿）。</summary>
        private static Sprite CreateCardSprite(string name, float radius, float outlineWidth, Color ink, Color inner, Color fill)
        {
            const int size = 64;
            const int samplesPerAxis = 4;
            // 透明风（TransparentPalette 的 InkOutline / InnerHighlight 都是 alpha=0）下必须把描边宽度归零。
            // 否则 SampleCard 会把 sd∈[-innerWidth,0) 与 sd∈[0,outlineWidth) 两圈写成全透明像素，
            // 等于把实心区域从 RectTransform 边界向内"腐蚀"掉 innerWidth+outlineWidth
            // （卡片 1.5+0.9=2.4px、面板 2.5+1.5=4.0px），观感是边缘发虚、面板比实际略小；
            // 同时 pad 与 border 仍按有描边计算，9-slice 边框会包住一圈纯透明像素。
            // 归零后两套样式的实心区域尺寸一致（仅余下方 pad 的 1px 抗锯齿边距，见 pad 注释）。
            // 做法与滚动条/投影 sprite 的既有处理一致（EnsureSprites 对它们直接传 outlineWidth=0 + Color.clear）。
            if (ink.a <= 0f && inner.a <= 0f)
            {
                outlineWidth = 0f;
            }
            float innerWidth = outlineWidth * 0.6f; // 内描边宽度（约外描边 0.6）
            // 外描边留白：ink 外描边位于盒外 sd∈[0,outlineWidth]，必须在纹理四周留出该空间，
            // 否则直边外描边落在纹理外被裁掉（只圆角处可见），造成"圆角深棕、直边无分层"的突兀观感。
            // +1f 的常数项即使在 outlineWidth==0（透明风/滚动条）时也保留：圆角边界需要至少 1px
            // 让 4×4 超采样把边缘渐变写完整，pad=0 会让圆角外侧的半透明采样被纹理边界截断成硬边。
            float pad = outlineWidth + 1f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            // 盒半宽（-radius 为圆角圆心偏移；-pad 为外描边留白）：盒不再占满整张纹理，直边外描边得以完整绘制。
            // 注意此处 half 已等价于标准圆角矩形 SDF 的 q = |p-center| - 盒半宽 + radius，勿再额外 +radius（会重复）。
            float half = size * 0.5f - pad - radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < samplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < samplesPerAxis; sx++)
                        {
                            Vector2 p = new Vector2(x + (sx + 0.5f) / samplesPerAxis, y + (sy + 0.5f) / samplesPerAxis);
                            Vector2 d = new Vector2(Mathf.Abs(p.x - center.x) - half, Mathf.Abs(p.y - center.y) - half);
                            Vector2 outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f));
                            float sd = outside.magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - radius;
                            Color c = SampleCard(sd, outlineWidth, innerWidth, ink, inner, fill);
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }
                    int n = samplesPerAxis * samplesPerAxis;
                    pixels[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(r / n) * 255f),
                        (byte)(Mathf.Clamp01(g / n) * 255f),
                        (byte)(Mathf.Clamp01(b / n) * 255f),
                        (byte)(Mathf.Clamp01(a / n) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            // 9-slice 边框覆盖留白 + 圆角 + 内外描边，保证四角完整。
            // 钳制到 size/2-1：Unity 要求左右 border 之和小于纹理宽度，否则 Sprite.Create 报错并产生退化 Sprite。
            float border = pad + radius + outlineWidth + innerWidth + 1f;
            float maxBorder = size * 0.5f - 1f;
            if (border > maxBorder)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: " + name + " 的 9-slice border " + border.ToString("0.##")
                    + " 超过纹理上限 " + maxBorder.ToString("0.##") + "，已钳制（如需更大圆角请同步增大纹理尺寸）");
                border = maxBorder;
            }
            Sprite result = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            result.name = texture.name;
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }

        /// <summary>SDF 采样：sd 负值在圆角矩形内部，正值在外部。中心填充 → 奶油内描边 → 深墨外描边 → 透明。</summary>
        private static Color SampleCard(float sd, float outlineWidth, float innerWidth, Color ink, Color inner, Color fill)
        {
            if (sd < -innerWidth) { return fill; }   // 深入中心 → 填充
            if (sd < 0f) { return inner; }           // 紧贴边界内侧 → 奶油白内描边
            if (sd < outlineWidth) { return ink; }   // 边界外侧 → 深墨外描边
            return new Color(0f, 0f, 0f, 0f);        // 更外 → 透明
        }

        /// <summary>语言切换时刷新分类按钮的文字、字体与大小写样式（按钮 label 在创建时按当时语言固化）。</summary>
        internal static void RefreshButtonLabels()
        {
            // 这里**不要**清 _coverageProbed：覆盖探测的缓存键已是「语言 + 字体实例 ID」，
            // 语言一变键就不匹配、自动重探，显式清除只会抵消缓存。
            // 曾经加过 `_coverageProbed = false;`，实测导致一次语言切换探测两遍 ——
            // ItemListView.OnLanguageChanged 的顺序是 RefreshFonts()（已探测并写入缓存）
            // → RefreshButtonLabels()（把刚写入的缓存清掉，ResolveFont 再探一次），
            // 结果是每次切语言多跑一遍 HasCharacters（探测串含 10 条文案 + 两种大写形态 + 变音字母表，
            // 数百字符）并多刷一条"改用游戏主字体"日志（土耳其语实测连续两条）。
            TMP_FontAsset font = ResolveFont();
            // 大小写样式随语言变化（俄/乌不全大写，见 CreateCategoryButton 注释）。
            // 必须在这里同步更新：否则从英文切到俄语后 label 仍停留在创建时固化的 UpperCase。
            FontStyles labelStyle = LocalizedText.languageSupportsAllCaps ? FontStyles.UpperCase : FontStyles.Normal;
            for (int i = 0; i < _categoryButtonLabels.Count; i++)
            {
                TextMeshProUGUI label = _categoryButtonLabels[i];
                if (label == null)
                {
                    continue;
                }
                // 用平行列表取分类值，不用索引强转（防枚举值变更后标签错位）
                if (i < _categoryButtonMajors.Count)
                {
                    label.text = ItemCatalog.GetMajorLabel(_categoryButtonMajors[i]);
                }
                if (font != null)
                {
                    label.font = font;
                }
                label.fontStyle = labelStyle;
            }
            // 收藏按钮文字/字体/大小写随语言刷新
            if (_favoriteButtonLabel != null)
            {
                _favoriteButtonLabel.text = Loc.Get("catFavorite");
                if (font != null)
                {
                    _favoriteButtonLabel.font = font;
                }
                _favoriteButtonLabel.fontStyle = labelStyle;
            }
            // 右键收藏提示文字/字体随语言刷新
            if (_rightClickHint != null)
            {
                _rightClickHint.text = Loc.Get("rightClickHint");
                if (font != null)
                {
                    _rightClickHint.font = font;
                }
            }
        }

        private static Transform CreateCategoryBar(RectTransform panel, RectTransform searchBar)
        {
            GameObject barGo = new GameObject("CategoryBar", typeof(RectTransform));
            RectTransform barRt = barGo.GetComponent<RectTransform>();
            barRt.SetParent(panel, false);
            barRt.anchorMin = new Vector2(0.05f, 1f);
            barRt.anchorMax = new Vector2(0.95f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = new Vector2(0f, -68f);
            barRt.sizeDelta = new Vector2(0f, 50f);
            // 置于同级最上层（SetAsLastSibling），确保分类按钮不被 Scroll View 遮挡、可点击
            barRt.SetAsLastSibling();

            if (barGo.GetComponent<CanvasRenderer>() == null)
            {
                barGo.AddComponent<CanvasRenderer>();
            }

            HorizontalLayoutGroup layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 6f;
            layout.childControlWidth = true;
            // 关键：childControlHeight 必须为 false！
            // true 时布局会用 LayoutUtility.GetPreferredHeight（无 LayoutElement 时≈0）覆盖按钮实际高度，
            // 导致按钮与 Image 被塌缩成一小片，可点击范围与 sizeDelta 设置完全无关。
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TMP_FontAsset font = ResolveFont();

            _categoryButtons.Clear();
            _categoryButtonLabels.Clear();
            _categoryButtonMajors.Clear();
            MajorCategory[] majors = (MajorCategory[])Enum.GetValues(typeof(MajorCategory));
            for (int i = 0; i < majors.Length; i++)
            {
                CreateCategoryButton(barRt, majors[i], font);
            }
            // 收藏筛选按钮：独立于分类单选，可叠加
            CreateFavoriteButton(barRt, font);
            return barRt;
        }

        /// <summary>右键收藏提示：分类条下方、滚动列表上方一行居中文字（暖卡纸柔和提示色，不拦截点击）。</summary>
        private static void CreateRightClickHint(RectTransform panel, TMP_FontAsset font)
        {
            GameObject go = new GameObject("RightClickHint", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // 位于分类条底部(-118)与滚动列表顶部(-150)之间，垂直居中（-123~-145）
            rt.anchoredPosition = new Vector2(0f, -123f);
            rt.sizeDelta = new Vector2(-24f, 22f);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            ApplyFont(text, font);
            text.fontSize = 17f;
            text.fontStyle = FontStyles.Italic; // 斜体示意辅助提示（与搜索占位符一致）
            text.color = ColorHint;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = Loc.Get("rightClickHint");
            text.raycastTarget = false; // 纯提示，不拦截点击

            _rightClickHint = text;
        }

        /// <summary>收藏筛选按钮（分类条末尾，独立 toggle，暖卡纸配色）。</summary>
        private static void CreateFavoriteButton(RectTransform parent, TMP_FontAsset font)
        {
            GameObject go = new GameObject("FavButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 50f);

            Image image = go.GetComponent<Image>();
            ApplySprite(image, _btnIdleSprite); // 圆角暖米棕填充（描边已烘进 Sprite）
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;

            button.onClick.AddListener(OnFavoriteToggled);

            // 悬停反馈（与分类按钮一致，仅非选中态提亮）
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate { if (_view == null || !_view.FavoritesOnly) image.sprite = _btnHoverSprite; });
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(delegate { RefreshFavoriteButtonColor(); });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);

            // 标签
            GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(text, font);
            text.enableAutoSizing = true;
            // fontSizeMin 从 16 降到 11：8 个按钮由 childForceExpandWidth 均分「面板宽 × 0.9」，
            // 1280×1024 下单按钮仅约 113px，而德语 "VERBRAUCHSOBJEKTE"(17 字符) @16pt 需约 150px。
            // 给 autoSizing 更大的收缩空间，让它优先缩小字号而不是走省略号。
            text.fontSizeMin = 11f;
            text.fontSizeMax = 22f;
            text.fontSize = 22f;
            // 必须显式设 Ellipsis：TMP 默认 overflowMode 是 Overflow，配合 NoWrap 时
            // 缩到 fontSizeMin 仍装不下的标签会直接溢出画到相邻按钮上（德/波/土/意最长标签会重叠）。
            text.overflowMode = TextOverflowModes.Ellipsis;
            // 全大写只对「游戏认为支持全大写」的语言启用：反编译 LocalizedText.cs:129-139 的
            // languageSupportsAllCaps 对 俄/乌/简中/繁中/日/韩 返回 false。CJK 无大小写本就不受影响，
            // 但西里尔会被真的大写（Все → ВСЕ），与游戏其余 UI 的排版规范冲突。
            text.fontStyle = LocalizedText.languageSupportsAllCaps ? FontStyles.UpperCase : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = ColorTextIdle;
            text.raycastTarget = false;
            text.outlineWidth = 0f;
            text.text = Loc.Get("catFavorite");

            _favoriteButton = button;
            _favoriteButtonLabel = text;
        }

        private static void OnFavoriteToggled()
        {
            if (_view == null)
            {
                return;
            }
            // 以 ItemListView 为唯一数据源：读当前状态翻转后同步回去
            bool nowFavorite = !_view.FavoritesOnly;
            _view.SetFavoritesOnly(nowFavorite);
            if (nowFavorite)
            {
                // 收藏开启：分类视觉取消选中（哨兵值，7 个分类按钮都不高亮），数据层分类设为"全部"（不过滤分类，只看收藏）
                _currentMajor = (MajorCategory)(-1);
                _view.SetMajor(MajorCategory.All);
            }
            else
            {
                // 收藏关闭：回到"全部"分类
                _currentMajor = MajorCategory.All;
            }
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                RefreshButtonColor(i);
            }
            RefreshFavoriteButtonColor();
        }

        private static void RefreshFavoriteButtonColor()
        {
            bool fav = (_view != null) && _view.FavoritesOnly;
            if (_favoriteButton == null)
            {
                return;
            }
            Image image = _favoriteButton.targetGraphic as Image;
            if (image != null)
            {
                image.sprite = fav ? _btnSelectedSprite : _btnIdleSprite;
            }
            if (_favoriteButtonLabel != null)
            {
                _favoriteButtonLabel.color = fav ? ColorTextSelected : ColorTextIdle;
            }
        }

        private static void CreateCategoryButton(RectTransform parent, MajorCategory major, TMP_FontAsset font)
        {
            GameObject go = new GameObject("CatButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 50f); // 宽度由布局均分；高度由自身 sizeDelta 决定（childControlHeight=false），
                                                 // Image 覆盖整个 RectTransform → 可点击范围 = 按钮大小

            Image image = go.GetComponent<Image>();
            ApplySprite(image, _btnIdleSprite); // 圆角暖米棕按钮（描边已烘进 Sprite）
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None; // 状态由代码统一换 Sprite

            MajorCategory captured = major;
            button.onClick.AddListener(() => OnMajorSelected(captured));

            // 悬停反馈：PointerEnter 换 hover Sprite，PointerExit 恢复（仅非选中按钮，选中态不被打断）
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate { if (captured != _currentMajor) image.sprite = _btnHoverSprite; });
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(delegate { RefreshButtonColor(_categoryButtons.IndexOf(button)); });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);

            // 标签（深暖棕文字、无描边、无加粗，浅色底上高对比 = 锐利）
            GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(text, font);
            // 清晰锐利方案：不再用 Bold + 黑色 SDF 描边（二者会把字形边缘做软/膨胀，是模糊主因）。
            // 改用「深暖棕文字 × 浅米棕底」的高对比 + 游戏原生的全大写 + 自适应字号（长英文标签自动缩小，不裁剪）。
            text.enableAutoSizing = true;
            // fontSizeMin 从 16 降到 11：8 个按钮由 childForceExpandWidth 均分「面板宽 × 0.9」（spacing 6），
            // 1280×1024（scale≈0.795）下单按钮仅约 113px，而德语 "VERBRAUCHSOBJEKTE"(17 字符) @16pt 需约 150px；
            // 波兰 "WYPOSAŻENIE"、土耳其 "TÜKETİLEBİLİR"、意大利 "EQUIPAGGIAMENTO" 同样超宽。
            // 降低下限让 autoSizing 优先缩小字号，把省略号当最后手段。
            text.fontSizeMin = 11f;
            text.fontSizeMax = 22f;
            text.fontSize = 22f;
            // 必须显式设 Ellipsis：TMP 默认 overflowMode 是 Overflow，配合 NoWrap 时
            // 缩到 fontSizeMin 仍装不下的标签会溢出到相邻按钮上，形成文字互相重叠。
            text.overflowMode = TextOverflowModes.Ellipsis;
            // 全大写只对「游戏认为支持全大写」的语言启用：反编译 LocalizedText.cs:129-139 的
            // languageSupportsAllCaps 对 俄/乌/简中/繁中/日/韩 返回 false。CJK 无大小写本就不受影响，
            // 但西里尔会被真的大写（Все → ВСЕ），与游戏其余 UI 的排版规范冲突。
            text.fontStyle = LocalizedText.languageSupportsAllCaps ? FontStyles.UpperCase : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; // 单行标签，避免长词折行
            text.color = ColorTextIdle;
            text.raycastTarget = false; // 文字不拦截点击，保证整块按钮区域可点
            text.outlineWidth = 0f;     // 移除黑色细描边（SDF 描边是文字模糊主因）
            text.text = ItemCatalog.GetMajorLabel(major);

            _categoryButtons.Add(button);
            _categoryButtonLabels.Add(text);
            _categoryButtonMajors.Add(major);
        }

        private static MajorCategory _currentMajor = MajorCategory.All;

        private static void RefreshButtonColor(int index)
        {
            if (index < 0 || index >= _categoryButtons.Count)
            {
                return;
            }
            // 用平行列表取分类值，不用索引强转（防枚举值变更后错位）
            bool selected = index < _categoryButtonMajors.Count && _categoryButtonMajors[index] == _currentMajor;
            Image image = _categoryButtons[index].targetGraphic as Image;
            if (image != null)
            {
                image.sprite = selected ? _btnSelectedSprite : _btnIdleSprite;
            }
            // 三态文字均为深暖棕，保证浅色底上清晰可读；选中文字略深一档，配合最浅的选中底更稳
            if (index < _categoryButtonLabels.Count)
            {
                TextMeshProUGUI label = _categoryButtonLabels[index];
                if (label != null)
                {
                    label.color = selected ? ColorTextSelected : ColorTextIdle;
                }
            }
        }

        private static void OnMajorSelected(MajorCategory major)
        {
            _currentMajor = major;
            if (_view != null)
            {
                _view.SetMajor(major);
                _view.SetFavoritesOnly(false);   // 选中分类时取消收藏（互斥）
            }
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                RefreshButtonColor(i);
            }
            RefreshFavoriteButtonColor();
        }
    }
}
