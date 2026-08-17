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
            if (eventData.button == PointerEventData.InputButton.Right && _onFavorite != null)
            {
                _onFavorite();
            }
        }
    }
}
