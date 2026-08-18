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

        // 配色方案：暖"卡纸"手绘贴纸风。保留 PEAK 户外暖色基调（米棕 → 奶油 → 浅暖黄），不采用冷色或纯白刺眼。
        // 浅底必须配深字：三态文字统一走深暖棕，保证高对比可读。
        private static readonly Color ColorIdle = new Color(0.82f, 0.75f, 0.63f, 1f);        // 默认填充：浅暖米棕（卡纸基色）
        private static readonly Color ColorHover = new Color(0.90f, 0.84f, 0.74f, 1f);       // 悬停：更亮的奶油米黄（比默认更亮，明显抬升）
        private static readonly Color ColorSelected = new Color(0.96f, 0.91f, 0.81f, 1f);    // 选中：最浅的暖黄高亮（三态中最亮，突出选中）
        private static readonly Color ColorTextIdle = new Color(0.28f, 0.21f, 0.14f, 1f);    // 默认/悬停文字：深暖棕（浅色底上高对比）
        private static readonly Color ColorTextSelected = new Color(0.22f, 0.16f, 0.10f, 1f); // 选中文字：更深的暖棕（最浅选中底上更稳）
        private static readonly Color ColorHint = new Color(0.50f, 0.42f, 0.32f, 1f);          // 右键收藏提示：柔和暖棕（比正文略淡，示意辅助提示）

        // 面板底色（暖卡纸）：比分类按钮略深一档，衬托按钮与条目（用户喜欢的牛皮纸基调，保留）。
        private static readonly Color PanelBackground = new Color(0.76f, 0.69f, 0.56f, 1f);

        // 手绘勾线色：深咖啡棕墨水描边（所有元素统一的"勾线"色，层级靠粗细区分）。
        private static readonly Color ColorInkOutline = new Color(0.36f, 0.25f, 0.15f, 1f);
        // 内层浅色描边：暖奶油白（贴在墨水线内侧，制造"贴纸白边 + 双层勾线"的手绘层次）。
        private static readonly Color ColorInnerHighlight = new Color(0.99f, 0.94f, 0.85f, 1f);
        // 卡片底色：比面板略亮的奶油卡纸（卡片从面板上"浮"起来，建立层次）。
        private static readonly Color ColorCardFill = new Color(0.89f, 0.83f, 0.73f, 1f);
        // 面板投影：深暖棕半透明（面板下方"纸张投影"，制造浮起深度）。
        private static readonly Color ColorPanelShadow = new Color(0.33f, 0.24f, 0.15f, 0.35f);
        // 搜索框底色：略深于面板的暖卡其（下凹"输入槽"感）。
        private static readonly Color ColorSearchFill = new Color(0.70f, 0.62f, 0.50f, 1f);
        // 滚动条轨道底色：略深的暖棕（半透明，贴合面板）。
        private static readonly Color ColorScrollbarBg = new Color(0.58f, 0.48f, 0.36f, 0.6f);
        // 滚动条 handle：较浅的暖棕滑块（在轨道上更明显）。
        private static readonly Color ColorScrollbarHandle = new Color(0.80f, 0.71f, 0.58f, 1f);

        /// <summary>构建入口：接收 ItemSpawnerPlusWindow，创建完整 UI 树并挂载 ItemListView。</summary>
        public static void Setup(ItemSpawnerPlusWindow window)
        {
            if (window == null)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: Setup abort, window is null.");
                return;
            }
            // 幂等：该窗口已成功 Setup（ItemListView.Initialized）或正在增量构建（Building），避免重复创建分类条/条目。
            ItemListView existing = window.GetComponent<ItemListView>();
            if (existing != null && (existing.Initialized || existing.Building))
            {
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
                Plugin.Log.LogError("ItemSpawnerPlus: ItemListView.Init 失败，已回滚: " + ex);
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
        }

        private static void EnsureCanvas(ItemSpawnerPlusWindow window)
        {
            Canvas canvas = window.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = window.gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;

            CanvasScaler scaler = window.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = window.gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (window.GetComponent<GraphicRaycaster>() == null)
            {
                window.gameObject.AddComponent<GraphicRaycaster>();
            }

            // 根 RectTransform 拉伸到全屏
            RectTransform root = window.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
        }

        private static void Build(ItemSpawnerPlusWindow window)
        {
            EnsureSprites();
            RectTransform root = window.GetComponent<RectTransform>();

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

        private static TMP_FontAsset ResolveFont()
        {
            return ItemListView.NeedsCjkFont() ? ItemListView.GetGameBaseFont() : ItemListView.FindFont("DarumaDropOne-Regular SDF");
        }

        private static RectTransform CreatePanel(RectTransform root)
        {
            // 纸张投影：面板下方略大、略深的暖棕半透明圆角片，制造"贴纸/卡纸浮起"的层次
            GameObject shadowGo = new GameObject("PanelShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform shadowRt = (RectTransform)shadowGo.transform;
            shadowRt.SetParent(root, false);
            shadowRt.anchorMin = new Vector2(0.18f, 0.18f);
            shadowRt.anchorMax = new Vector2(0.82f, 0.82f);
            shadowRt.offsetMin = new Vector2(-8f, -16f);   // 比面板略大一圈，向下偏移模拟顶部光源
            shadowRt.offsetMax = new Vector2(8f, 0f);
            Image shadowImg = shadowGo.GetComponent<Image>();
            ApplySprite(shadowImg, _shadowSprite);
            shadowImg.raycastTarget = false;

            GameObject go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0.18f, 0.18f);
            rt.anchorMax = new Vector2(0.82f, 0.82f);
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
            rt.anchorMin = new Vector2(0.07f, 1f);
            rt.anchorMax = new Vector2(0.93f, 1f);
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
            placeholder.font = font;
            placeholder.fontSize = 24f;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.45f, 0.37f, 0.28f, 0.6f);
            placeholder.text = Loc.Get("searchPlaceholder");
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.raycastTarget = false;

            // 输入文本
            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.SetParent(areaRt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = font;
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
            viewportRt.offsetMax = Vector2.zero;
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
            scroll.scrollSensitivity = 40f;

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
            ApplySprite(scrollbarImg, _scrollbarBgSprite);
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
            ApplySprite(handleImg, _scrollbarHandleSprite);
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
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;
            nameRt.anchoredPosition = new Vector2(0f, 4f);
            nameRt.sizeDelta = new Vector2(-10f, 36f);
            TextMeshProUGUI name = nameGo.GetComponent<TextMeshProUGUI>();
            name.font = font;
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
            const float outlineHalf = 1.75f;  // 勾线描边半宽（像素），整圈描边约 3.5px

            Color fill = new Color(0.86f, 0.32f, 0.34f, 1f); // 暖红填充（与原 favImg.color 一致）
            Color outline = ColorInkOutline;                  // 深暖棕勾线（与全局"勾线"色一致，手绘统一）

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "ItemSpawnerPlus Heart";
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
                            // 隐式心形方程：内部 f<0，边界 f=0，外部 f>0
                            float f = u * u * u - nx * nx * ny * ny * ny;
                            // 解析梯度 ∇f，用于把 f 归一化为近似带符号距离
                            float gx = 6f * nx * u * u - 2f * nx * ny * ny * ny;
                            float gy = 6f * ny * u * u - 3f * nx * nx * ny * ny;
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
            GetCardSprite(ref _panelSprite, "ItemSpawnerPlus Panel", 16f, 2.5f, ColorInkOutline, ColorInnerHighlight, PanelBackground);
            GetCardSprite(ref _cardSprite, "ItemSpawnerPlus Card", 9f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorCardFill);
            GetCardSprite(ref _searchSprite, "ItemSpawnerPlus Search", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorSearchFill);
            GetCardSprite(ref _btnIdleSprite, "ItemSpawnerPlus BtnIdle", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorIdle);
            GetCardSprite(ref _btnHoverSprite, "ItemSpawnerPlus BtnHover", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorHover);
            GetCardSprite(ref _btnSelectedSprite, "ItemSpawnerPlus BtnSelected", 10f, 1.5f, ColorInkOutline, ColorInnerHighlight, ColorSelected);
            GetCardSprite(ref _scrollbarBgSprite, "ItemSpawnerPlus ScrollBg", 6f, 1f, ColorInkOutline, ColorInnerHighlight, ColorScrollbarBg);
            GetCardSprite(ref _scrollbarHandleSprite, "ItemSpawnerPlus ScrollHandle", 6f, 1f, ColorInkOutline, ColorInnerHighlight, ColorScrollbarHandle);
            GetCardSprite(ref _shadowSprite, "ItemSpawnerPlus Shadow", 16f, 0f, Color.clear, Color.clear, ColorPanelShadow);
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
        private static void ApplySprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        /// <summary>用 SDF 生成带描边/白边/填充的 9-slice 卡片 Sprite（64×64，4×4 超采样抗锯齿）。</summary>
        private static Sprite CreateCardSprite(string name, float radius, float outlineWidth, Color ink, Color inner, Color fill)
        {
            const int size = 64;
            const int samplesPerAxis = 4;
            float innerWidth = outlineWidth * 0.6f; // 内描边宽度（约外描边 0.6）
            // 外描边留白：ink 外描边位于盒外 sd∈[0,outlineWidth]，必须在纹理四周留出该空间，
            // 否则直边外描边落在纹理外被裁掉（只圆角处可见），造成"圆角深棕、直边无分层"的突兀观感。
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

            float border = pad + radius + outlineWidth + innerWidth + 1f; // 9-slice 边框覆盖留白 + 圆角 + 内外描边，保证四角完整
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

        /// <summary>语言切换时刷新分类按钮的文字与字体（按钮 label 在创建时按当时语言固化）。</summary>
        internal static void RefreshButtonLabels()
        {
            TMP_FontAsset font = ItemListView.NeedsCjkFont() ? ItemListView.GetGameBaseFont() : ItemListView.FindFont("DarumaDropOne-Regular SDF");
            for (int i = 0; i < _categoryButtonLabels.Count; i++)
            {
                TextMeshProUGUI label = _categoryButtonLabels[i];
                if (label == null)
                {
                    continue;
                }
                label.text = ItemCatalog.GetMajorLabel((MajorCategory)i);
                if (font != null)
                {
                    label.font = font;
                }
            }
            // 收藏按钮文字/字体随语言刷新
            if (_favoriteButtonLabel != null)
            {
                _favoriteButtonLabel.text = Loc.Get("catFavorite");
                if (font != null)
                {
                    _favoriteButtonLabel.font = font;
                }
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

            TMP_FontAsset font = ItemListView.NeedsCjkFont() ? ItemListView.GetGameBaseFont() : ItemListView.FindFont("DarumaDropOne-Regular SDF");

            _categoryButtons.Clear();
            _categoryButtonLabels.Clear();
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
            text.font = font;
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
            text.font = font;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 22f;
            text.fontSize = 22f;
            text.fontStyle = FontStyles.UpperCase;
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
            enter.callback.AddListener(delegate { if ((MajorCategory)_categoryButtons.IndexOf(button) != _currentMajor) image.sprite = _btnHoverSprite; });
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
            text.font = font;
            // 清晰锐利方案：不再用 Bold + 黑色 SDF 描边（二者会把字形边缘做软/膨胀，是模糊主因）。
            // 改用「深暖棕文字 × 浅米棕底」的高对比 + 游戏原生的全大写 + 自适应字号（长英文标签自动缩小到 16，不裁剪）。
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = 22f;
            text.fontSize = 22f;
            text.fontStyle = FontStyles.UpperCase; // 游戏按钮/标签为全大写（中文无大小写，不受影响）
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; // 单行标签，避免长词折行
            text.color = ColorTextIdle;
            text.raycastTarget = false; // 文字不拦截点击，保证整块按钮区域可点
            text.outlineWidth = 0f;     // 移除黑色细描边（SDF 描边是文字模糊主因）
            text.text = ItemCatalog.GetMajorLabel(major);

            _categoryButtons.Add(button);
            _categoryButtonLabels.Add(text);
        }

        private static MajorCategory _currentMajor = MajorCategory.All;

        private static void RefreshButtonColor(int index)
        {
            if (index < 0 || index >= _categoryButtons.Count)
            {
                return;
            }
            bool selected = (MajorCategory)index == _currentMajor;
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
