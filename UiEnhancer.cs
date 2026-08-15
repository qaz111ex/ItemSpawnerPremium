using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// UI 增强：
    /// 1. 搜索框移到模组菜单顶部居中并扩大；
    /// 2. 搜索框下方新增横向分类按钮条（全部/工具/食物/神秘/装备/消耗品/场景），按钮加大并加粗描边；
    /// 3. 挂载 ItemListView（本地化显示名 + 中文/拼音搜索 + 多标签分类过滤与排序）。
    /// </summary>
    public static class UiEnhancer
    {
        private static readonly List<Button> _categoryButtons = new List<Button>();
        private static readonly List<RectTransform> _categoryButtonRects = new List<RectTransform>();
        private static ItemListView _view;

        private static readonly Color ColorIdle = new Color(1f, 1f, 1f, 0f);      // 透明背景，保持面板原本视觉
        private static readonly Color ColorHover = new Color(1f, 1f, 1f, 0.16f);   // 悬停轻微提亮
        private static readonly Color ColorSelected = new Color(0.92f, 0.70f, 0.25f, 0.90f); // 选中金色

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
                Plugin.Log.LogWarning("ItemSpawnerPlus: UI nodes not found, abort setup.");
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
            sbRt.sizeDelta = new Vector2(panelWidth * 0.86f, 38f);

            // 3. Scroll View 下移并收窄高度，为顶部搜索框 + 分类条让位。
            //    分类条高 44 位于 56~100px，Scroll View 顶部缩进 = 37 + 130/2 = 102px。
            RectTransform svRt = scrollViewGo as RectTransform;
            svRt.anchoredPosition = new Vector2(0f, -37f);
            svRt.sizeDelta = new Vector2(-26f, -130f);

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
            barRt.sizeDelta = new Vector2(panel.rect.width * 0.90f, 44f);
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
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
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
            rt.sizeDelta = new Vector2(0f, 44f); // 宽度由布局均分，高度加大保证可点击区域

            Image image = go.GetComponent<Image>();
            Sprite sprite = FindSprite("UISprite");
            if (sprite != null)
            {
                image.sprite = sprite;
            }
            image.color = ColorIdle; // 默认透明背景，保持面板原本视觉
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None; // 颜色由代码统一管理

            MajorCategory captured = major;
            button.onClick.AddListener(() => OnMajorSelected(captured));

            // 悬停反馈：PointerEnter 轻微提亮，PointerExit 恢复
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate { if ((MajorCategory)_categoryButtons.IndexOf(button) != _currentMajor) image.color = ColorHover; });
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(delegate { RefreshButtonColor(_categoryButtons.IndexOf(button)); });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);

            // 标签（加粗、无描边）
            GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 19f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false; // 文字不拦截点击，保证整块按钮区域可点
            text.outlineWidth = 0f;     // 无描边
            text.text = ItemCatalog.GetMajorLabel(major);

            _categoryButtons.Add(button);
            _categoryButtonRects.Add(rt);
        }

        private static MajorCategory _currentMajor = MajorCategory.All;

        private static void RefreshButtonColor(int index)
        {
            if (index < 0 || index >= _categoryButtons.Count)
            {
                return;
            }
            Image image = _categoryButtons[index].targetGraphic as Image;
            if (image != null)
            {
                image.color = ((MajorCategory)index == _currentMajor) ? ColorSelected : ColorIdle;
            }
        }

        private static void OnMajorSelected(MajorCategory major)
        {
            _currentMajor = major;
            if (_view != null)
            {
                _view.SetMajor(major);
            }
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                RefreshButtonColor(i);
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
