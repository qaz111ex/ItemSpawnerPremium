using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ItemSpawnerEnhancement
{
    /// <summary>右键点击监听组件（挂在物品条目 clone 上），用于切换收藏。</summary>
    internal sealed class ItemFavoriteTrigger : MonoBehaviour, IPointerClickHandler
    {
        private Action _onFavorite;

        public void Configure(Action onFavorite)
        {
            _onFavorite = onFavorite;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            try
            {
                // 拖拽中不触发收藏。
                //
                // 实际上这层守卫在本作用的两个输入模块下都不会命中：
                // InputSystemUIInputModule.ProcessPointerButtonDrag 一旦发现
                // `pointerPress != pointerDrag` 就把 eligibleForClick 置 false，
                // 而条目卡片是 pointerPress（PressFeedback 实现 IPointerDownHandler）、
                // pointerDrag 会沿层级上溯到 ScrollRect（IDragHandler），二者必然不同 ——
                // 释放时 pointerClickHandler 根本不会被派发。StandaloneInputModule 同理。
                // 保留它是零成本的前瞻防御（换输入模块 / 未来 UI 结构变化时仍然正确），
                // 但不要据「EventSystem 拖拽后仍会派发 OnPointerClick」这个错误前提做别的推理。
                if (eventData.dragging)
                {
                    return;
                }
                if (eventData.button == PointerEventData.InputButton.Right && _onFavorite != null)
                {
                    _onFavorite();
                }
            }
            catch (Exception ex)
            {
                // UI 回调兜底：ExecuteEvents.Execute 虽有框架级 try/catch，但那条日志不带本模组前缀，
                // 排障时按前缀 grep 会漏掉。
                Plugin.Log.LogError("ItemSpawnerPremium: 右键切换收藏失败: " + ex);
            }
        }
    }
}
