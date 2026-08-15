using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// UI 增强：
    /// 1. 搜索框移到模组菜单顶部居中并扩大；
    /// 2. 搜索框下方新增横向分类按钮条（全部/食物/工具/武器/神秘/装备/消耗品/其他）；
    /// 3. 挂载 ItemListView（本地化显示名 + 中文/拼音搜索 + 分类过滤与排序）。
    /// </summary>
    public static class UiEnhancer
    {
        private static readonly List<Button> _categoryButtons = new List<Button>();
        private static readonly List<RectTransform> _categoryButtonRects = new List<RectTransform>();
        private static ItemListView _view;

        private static readonly Color ColorIdle = new Color(0.10f, 0.10f, 0.10f, 0.96f);
        private static readonly Color ColorSelected = new Color(0.88f, 0.64f, 0.18f, 1f);
        private static readonly Color ColorHover = new Color(0.22f, 0.22f, 0.22f, 0.96f);

        public static void Setup(ItemSpawner.ItemSpawnerWindow window)
        {
            if (window == null)
            {
                return;
            }
            Transform canvas = window.panel.transform;

            Transform searchGo = canvas.FindChildRecursive("SearchBar");
            Transform scrollViewGo = canvas.FindChildRecursive("Scroll View");
            Transform contentGo = canvas.FindChildRecursive("Content");
            Transform template = canvas.FindChildRecursive("ItemEntry");
            if (searchGo == null || scrollViewGo == null || contentGo == null || template == null)
            {
                Plugin.Log.LogWarning("ItemSpawner Enhancement: UI nodes not found, abort setup.");
                return;
            }
            TMP_InputField searchInput = searchGo.GetComponent<TMP_InputField>();

            // 1. 移除原 SearchScript 并清空搜索框上的旧监听（原逻辑只做英文前缀匹配）
            ItemSpawner.SearchScript oldSearch = window.GetComponent<ItemSpawner.SearchScript>();
            if (oldSearch != null)
            {
                UnityEngine.Object.Destroy(oldSearch);
            }
            if (searchInput != null)
            {
                searchInput.onValueChanged.RemoveAllListeners();
            }

            // 2. 搜索框：顶部居中、加宽加高
            RectTransform panelRt = scrollViewGo.parent as RectTransform; // Panel
            RectTransform sbRt = searchGo as RectTransform;
            sbRt.anchorMin = new Vector2(0.5f, 1f);
            sbRt.anchorMax = new Vector2(0.5f, 1f);
            sbRt.pivot = new Vector2(0.5f, 1f);
            sbRt.anchoredPosition = new Vector2(0f, -12f);
            float panelWidth = (panelRt != null) ? panelRt.rect.width : 900f;
            sbRt.sizeDelta = new Vector2(panelWidth * 0.86f, 36f);

            // 3. Scroll View 下移并收窄高度，为顶部搜索框 + 分类条让位。
            //    顶部缩进 = 34 + 122/2 = 95px（避开 SearchBar 12~48px 与 CategoryBar 56~90px 区域）。
            RectTransform svRt = scrollViewGo as RectTransform;
            svRt.anchoredPosition = new Vector2(0f, -33f);
            svRt.sizeDelta = new Vector2(-26f, -122f);

            // 4. 分类按钮条（位于搜索框与滚动列表之间）
            Transform bar = CreateCategoryBar(panelRt, sbRt);

            // 5. 挂载列表视图（若窗口重开则复用）
            if (_view != null)
            {
                _view.Stop();
            }
            _view = window.gameObject.GetComponent<ItemListView>();
            if (_view == null)
            {
                _view = window.gameObject.AddComponent<ItemListView>();
            }
            _view.Init(contentGo, template, searchInput, OnMajorSelected);

            // 6. 刷新分类按钮选中态
            OnMajorSelected(MajorCategory.All);
        }

        private static Transform CreateCategoryBar(RectTransform panel, RectTransform searchBar)
        {
            GameObject barGo = new GameObject("CategoryBar", typeof(RectTransform));
            RectTransform barRt = barGo.GetComponent<RectTransform>();
            barRt.SetParent(panel, false);
            barRt.anchorMin = new Vector2(0.5f, 1f);
            barRt.anchorMax = new Vector2(0.5f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = new Vector2(0f, -56f);
            barRt.sizeDelta = new Vector2(panel.rect.width * 0.86f, 34f);
            // 置于同级最上层（SetAsLastSibling），确保分类按钮不被 Scroll View 遮挡、可点击
            barRt.SetAsLastSibling();

            if (barGo.GetComponent<CanvasRenderer>() == null)
            {
                barGo.AddComponent<CanvasRenderer>();
            }

            HorizontalLayoutGroup layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 6f;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            TMP_FontAsset font = ItemListView.IsChineseLanguage() ? ItemListView.GetGameBaseFont() : ItemListView.FindFont("DarumaDropOne-Regular SDF");

            _categoryButtons.Clear();
            _categoryButtonRects.Clear();
            MajorCategory[] majors = (MajorCategory[])Enum.GetValues(typeof(MajorCategory));
            for (int i = 0; i < majors.Length; i++)
            {
                CreateCategoryButton(barRt, majors[i], font);
            }
            return barRt;
        }

        private static void CreateCategoryButton(RectTransform parent, MajorCategory major, TMP_FontAsset font)
        {
            GameObject go = new GameObject("CatButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(96f, 30f);

            Image image = go.GetComponent<Image>();
            Sprite sprite = FindSprite("UISprite");
            if (sprite != null)
            {
                image.sprite = sprite;
            }
            image.color = ColorIdle;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            MajorCategory captured = major;
            button.onClick.AddListener(() => OnMajorSelected(captured));

            // 悬停反馈
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;

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
            text.fontSize = 17f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.text = ItemCatalog.GetMajorLabel(major);

            _categoryButtons.Add(button);
            _categoryButtonRects.Add(rt);
        }

        private static void OnMajorSelected(MajorCategory major)
        {
            if (_view != null)
            {
                _view.SetMajor(major);
            }
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                Image image = _categoryButtons[i].targetGraphic as Image;
                if (image != null)
                {
                    image.color = ((MajorCategory)i == major) ? ColorSelected : ColorIdle;
                }
            }
        }

        private static Sprite FindSprite(string name)
        {
            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
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
    }
}
