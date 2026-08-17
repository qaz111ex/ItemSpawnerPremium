using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 独立物品生成器窗口：继承游戏 MenuWindow，纯代码构建 UI。
    /// MenuWindow.Open/Close 是 internal，跨程序集用反射调用（缓存 MethodInfo）。
    /// </summary>
    public class ItemSpawnerPlusWindow : MenuWindow
    {
        private static readonly MethodInfo OpenMethod = AccessTools.Method(typeof(MenuWindow), "Open");
        private static readonly MethodInfo CloseMethod = AccessTools.Method(typeof(MenuWindow), "Close");

        /// <summary>
        /// 物品生成入口：CharacterItems.SpawnItemInHand 在反编译后为 internal，
        /// 跨程序集无法直接调用，此处用反射缓存（行为等价于直接调用）。
        /// </summary>
        private static readonly MethodInfo SpawnItemInHandMethod =
            typeof(CharacterItems).GetMethod("SpawnItemInHand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // UI 节点引用（UiEnhancer 构建后回填）
        internal TMP_InputField searchInput;
        internal Transform content;
        internal Transform template;
        internal RectTransform panelRect;

        public override bool openOnStart => false;
        public override bool closeOnPause => true;
        public override bool closeOnUICancel => true;
        public override bool blocksPlayerInput => false;
        public override bool selectOnOpen => true;
        public override Selectable objectToSelectOnOpen => searchInput;
        public override bool autoHideOnClose => true;
        public override GameObject panel => gameObject;

        protected override void Initialize()
        {
            base.Initialize();
            UiEnhancer.Setup(this);
        }

        /// <summary>用反射切换窗口显隐（MenuWindow.Open/Close 为 internal）。</summary>
        internal static void ToggleWindow(ItemSpawnerPlusWindow w)
        {
            if (w == null)
            {
                return;
            }
            try
            {
                (w.isOpen ? CloseMethod : OpenMethod).Invoke(w, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: 切换窗口失败: " + ex);
            }
        }

        /// <summary>生成物品到本地角色手中（前置 Photon/角色检查后调 SpawnItemInHand）。</summary>
        internal static void Spawn(Item item)
        {
            if (item == null)
            {
                return;
            }
            if (!PhotonNetwork.IsConnected || Character.localCharacter == null
                || Character.localCharacter.refs == null
                || Character.localCharacter.refs.items == null)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 无法生成 " + item.gameObject.name + "（未连接到房间或本地角色不存在）");
                return;
            }
            if (SpawnItemInHandMethod == null)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: 反射获取 CharacterItems.SpawnItemInHand 失败，无法生成物品");
                return;
            }
            SpawnItemInHandMethod.Invoke(Character.localCharacter.refs.items, new object[] { item.gameObject.name });
        }
    }
}
