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
            // 拖拽结束时不触发收藏：EventSystem 在拖拽后仍会派发 OnPointerClick（只要按下与松开在同一对象上），
            // 玩家按住右键滚动/拖动列表后松手，会被误判成一次"右键点击"而切换收藏状态。
            // PointerEventData.dragging 在拖拽进行中为 true（UnityEngine.UI 的 PointerEventData 属性，已确认存在）。
            if (eventData.dragging)
            {
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Right && _onFavorite != null)
            {
                _onFavorite();
            }
        }
    }
}
