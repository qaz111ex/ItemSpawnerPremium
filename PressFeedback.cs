using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 按下反馈组件（挂在物品条目根，Button 旁）：
    /// 卡片填充色已烘进 Sprite（Image.color 默认白色），按下时临时把 Image.color 压暗，松开/移出瞬间恢复，
    /// 产生"按下变色、松开还原"的点击反馈。与左键生成（Button.onClick）、右键收藏（ItemFavoriteTrigger.OnPointerClick）互不干扰。
    /// </summary>
    internal sealed class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        // 按下压暗系数：白 → 灰白，整体把烘进 Sprite 的暖色压暗（≈ 0.85 倍亮度）
        private static readonly Color PressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);

        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_image != null)
            {
                _image.color = PressedColor;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_image != null)
            {
                _image.color = Color.white;
            }
        }

        // 按住后移出卡片也还原，避免卡片卡在压暗态
        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image != null)
            {
                _image.color = Color.white;
            }
        }

        // 兜底：按下期间条目被 Rebuild 隐藏（SetActive(false)）时收不到 OnPointerUp/OnPointerExit，
        // 此处复位避免颜色残留在压暗态并随对象池复用"传染"到其他物品。
        private void OnDisable()
        {
            if (_image != null)
            {
                _image.color = Color.white;
            }
        }
    }
}
