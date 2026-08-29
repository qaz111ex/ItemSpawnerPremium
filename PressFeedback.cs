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
            // 只对左键（=生成物品的那个键）给按下反馈：右键是收藏操作，
            // 不应该出现"卡片被按下"的压暗动画。
            // 注意 OnPointerUp / OnPointerExit / OnDisable 三条恢复路径都刻意不加按键判定：
            // 左键按下、右键松开这类边缘情况下，若恢复路径也过滤按键，卡片会永久卡在压暗态。
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
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
