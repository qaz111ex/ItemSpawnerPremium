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
        private static Texture2D _heartTexture;

        // 配色方案：浅色系暖"卡纸"风（应用户反馈，把原深暖棕/深卡其整体调浅）。
        // 保留 PEAK 户外暖色基调（米棕 → 奶油 → 浅暖黄方向），不采用冷色或纯白刺眼。
        // 浅底必须配深字：三态文字统一走深暖棕，保证高对比可读。
        private static readonly Color ColorIdle = new Color(0.82f, 0.75f, 0.63f, 1f);        // 默认填充：浅暖米棕（卡纸基色）
        private static readonly Color ColorHover = new Color(0.90f, 0.84f, 0.74f, 1f);       // 悬停：更亮的奶油米黄（比默认更亮，明显抬升）
        private static readonly Color ColorSelected = new Color(0.96f, 0.91f, 0.81f, 1f);    // 选中：最浅的暖黄高亮（三态中最亮，突出选中）
        private static readonly Color ColorBorder = new Color(0.70f, 0.60f, 0.48f, 0.6f);    // 按钮描边：浅暖棕（比各填充略深以勾边，明显浅于原卡其）
        private static readonly Color ColorTextIdle = new Color(0.28f, 0.21f, 0.14f, 1f);    // 默认/悬停文字：深暖棕（浅色底上高对比）
        private static readonly Color ColorTextSelected = new Color(0.22f, 0.16f, 0.10f, 1f); // 选中文字：更深的暖棕（最浅选中底上更稳）

        // 面板底色（暖卡纸）：比分类按钮略深一档，衬托按钮与条目。
        private static readonly Color PanelBackground = new Color(0.76f, 0.69f, 0.56f, 1f);

        /// <summary>构建入口：接收 ItemSpawnerPlusWindow，创建完整 UI 树并挂载 ItemListView。</summary>
        public static void Setup(ItemSpawnerPlusWindow window)
        {
            if (window == null)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: Setup abort, window is null.");
                return;
            }
            // 幂等：该窗口已成功 Setup（ItemListView.Initialized），避免重复创建分类条/条目。
            ItemListView existing = window.GetComponent<ItemListView>();
            if (existing != null && existing.Initialized)
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
                _view.Init(window.content, window.template, window.searchInput, OnMajorSelected);
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

            CreateCategoryBar(panelRt, (RectTransform)searchInput.transform);
        }

        private static TMP_FontAsset ResolveFont()
        {
            return ItemListView.NeedsCjkFont() ? ItemListView.GetGameBaseFont() : ItemListView.FindFont("DarumaDropOne-Regular SDF");
        }

        private static RectTransform CreatePanel(RectTransform root)
        {
            GameObject go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0.18f, 0.18f);
            rt.anchorMax = new Vector2(0.82f, 0.82f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = go.GetComponent<Image>();
            bg.sprite = null;
            bg.color = PanelBackground;
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
            bg.sprite = null;
            bg.color = new Color(0.72f, 0.64f, 0.52f, 1f); // 略深于面板的暖卡其输入底色
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
            // 顶部缩进 130px，为顶部搜索框（12~62px）+ 分类条（68~118px）让位
            scrollRt.offsetMin = new Vector2(20f, 20f);
            scrollRt.offsetMax = new Vector2(-20f, -130f);

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
            grid.cellSize = new Vector2(150f, 150f);       // 正方形格子
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
            rt.sizeDelta = new Vector2(150f, 150f);

            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = 150f;
            layout.preferredHeight = 150f;
            layout.flexibleWidth = 0f;

            Image bg = go.GetComponent<Image>();
            bg.sprite = null;
            bg.color = new Color(1f, 1f, 1f, 0.06f); // 浅色面板上的微反衬条目底色
            bg.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;

            // 图标（顶部居中，约 90x90）
            GameObject iconGo = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform iconRt = (RectTransform)iconGo.transform;
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -8f);
            iconRt.sizeDelta = new Vector2(90f, 90f);
            RawImage icon = iconGo.GetComponent<RawImage>();
            icon.raycastTarget = false;

            // 文字（底部居中，最多 2 行，超出省略号）
            GameObject nameGo = new GameObject("ItemName", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform nameRt = (RectTransform)nameGo.transform;
            nameRt.SetParent(rt, false);
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;
            nameRt.anchoredPosition = new Vector2(0f, 4f);
            nameRt.sizeDelta = new Vector2(-10f, 44f);
            TextMeshProUGUI name = nameGo.GetComponent<TextMeshProUGUI>();
            name.font = font;
            name.fontSize = 19f;
            name.color = ColorTextIdle;
            name.alignment = TextAlignmentOptions.Center;   // 居中
            name.textWrappingMode = TextWrappingModes.Normal; // 换行（enableWordWrapping 已弃用）
            name.overflowMode = TextOverflowModes.Ellipsis;  // 超出省略号
            name.maxVisibleLines = 2;                        // 最多 2 行
            name.raycastTarget = false;

            // 心形标记（右上角，收藏时显示）
            GameObject favGo = new GameObject("Favorite", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform favRt = (RectTransform)favGo.transform;
            favRt.SetParent(rt, false);
            favRt.anchorMin = new Vector2(1f, 1f);
            favRt.anchorMax = new Vector2(1f, 1f);
            favRt.pivot = new Vector2(1f, 1f);
            favRt.anchoredPosition = new Vector2(-6f, -6f);
            favRt.sizeDelta = new Vector2(24f, 24f);
            RawImage favImg = favGo.GetComponent<RawImage>();
            favImg.texture = GetHeartTexture();
            favImg.color = new Color(0.86f, 0.32f, 0.34f, 1f); // 暖红心形
            favImg.raycastTarget = false;
            favGo.SetActive(false); // 默认隐藏

            return rt;
        }

        /// <summary>程序化生成 32x32 心形纹理（静态缓存，避免重复生成）。</summary>
        private static Texture2D GetHeartTexture()
        {
            if (_heartTexture != null)
            {
                return _heartTexture;
            }
            const int size = 32;
            const int samplesPerAxis = 4;
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
                    int inside = 0;
                    for (int sy = 0; sy < samplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < samplesPerAxis; sx++)
                        {
                            float px = x + (sx + 0.5f) / samplesPerAxis;
                            float py = y + (sy + 0.5f) / samplesPerAxis;
                            float nx = (px - 16f) / 11f;
                            float ny = (py - 14.5f) / 11f;
                            float sum = nx * nx + ny * ny - 1f;
                            if (sum * sum * sum - nx * nx * ny * ny * ny <= 0f)
                            {
                                inside++;
                            }
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samplesPerAxis * samplesPerAxis)));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _heartTexture = texture;
            return _heartTexture;
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

        /// <summary>收藏筛选按钮（分类条末尾，独立 toggle，暖卡纸配色）。</summary>
        private static void CreateFavoriteButton(RectTransform parent, TMP_FontAsset font)
        {
            GameObject go = new GameObject("FavButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 50f);

            Image image = go.GetComponent<Image>();
            image.sprite = null;
            image.color = ColorIdle;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = ColorBorder;
            outline.effectDistance = new Vector2(1f, 1f);

            button.onClick.AddListener(OnFavoriteToggled);

            // 悬停反馈（与分类按钮一致，仅非选中态提亮）
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate { if (_view == null || !_view.FavoritesOnly) image.color = ColorHover; });
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
                // 收藏开启时，取消分类选中（回到"全部"）
                _currentMajor = MajorCategory.All;
                _view.SetMajor(MajorCategory.All);
                for (int i = 0; i < _categoryButtons.Count; i++)
                {
                    RefreshButtonColor(i);
                }
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
                image.color = fav ? ColorSelected : ColorIdle;
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
            // 不使用 UISprite：该 sprite 为圆角且带投影纹理，不透明填充时暴露圆角与像素阴影。
            // 置空 sprite 后 Image 渲染为纯直角矩形，暖卡其描边由下方 Outline 组件提供。
            image.sprite = null;
            image.color = ColorIdle; // 默认浅暖米棕填充（悬停/选中由 EventTrigger 与 RefreshButtonColor 统一管理）
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None; // 颜色由代码统一管理

            // 浅暖棕细描边：浅色卡纸的勾边（比填充略深以界定边缘，替换原先的深卡其描边）
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = ColorBorder;
            outline.effectDistance = new Vector2(1f, 1f);

            MajorCategory captured = major;
            button.onClick.AddListener(() => OnMajorSelected(captured));

            // 悬停反馈：PointerEnter 提亮填充为暖棕，PointerExit 恢复（仅非选中按钮，选中态不被打断）
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate { if ((MajorCategory)_categoryButtons.IndexOf(button) != _currentMajor) image.color = ColorHover; });
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
                image.color = selected ? ColorSelected : ColorIdle;
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
