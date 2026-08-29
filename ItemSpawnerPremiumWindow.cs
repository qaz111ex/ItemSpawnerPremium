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
    public class ItemSpawnerPremiumWindow : MenuWindow
    {
        private static readonly MethodInfo OpenMethod = AccessTools.Method(typeof(MenuWindow), "Open");
        private static readonly MethodInfo CloseMethod = AccessTools.Method(typeof(MenuWindow), "Close");

        /// <summary>
        /// 物品生成入口：CharacterItems.SpawnItemInHand 在反编译后为 internal，
        /// 跨程序集无法直接调用，此处用反射缓存（行为等价于直接调用）。
        /// 显式指定参数类型：避免未来游戏新增同名重载时抛 AmbiguousMatchException，
        /// 该异常发生在静态字段初始化中会升级为 TypeInitializationException 使整个窗口类型不可用。
        /// </summary>
        private static readonly MethodInfo SpawnItemInHandMethod =
            typeof(CharacterItems).GetMethod("SpawnItemInHand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

        /// <summary>
        /// 生成节流时间戳（unscaledTime）：上一次成功派发 SpawnItemInHand RPC 的时刻。
        /// 静态而非实例字段：窗口是 DontDestroyOnLoad 单例，静态即全局唯一，且与 Spawn 的静态签名一致。
        /// </summary>
        private static float _lastSpawnTime = float.NegativeInfinity;

        /// <summary>
        /// 生成最小间隔（秒）。取 0.25 对齐 Item.Interact 的门槛：
        /// Item.Interact 开头有 `interactor.refs.items.lastEquippedSlotTime + 0.25f > Time.time` 直接 return，
        /// 即 0.25 秒内重复生成的物品根本不会被拾取——房主端却已经 PhotonNetwork.Instantiate 出实体，
        /// 结果是"连点 N 次只拿到 1 个，地上/内存里多出 N-1 个网络对象"。故在客户端就掐住这个频率。
        /// </summary>
        private const float SpawnCooldown = 0.25f;

        /// <summary>
        /// P0-1 回滚标记：本次 MenuWindow.Open() 中 OnOpen 失败并已反射 Close() 回滚。
        /// 由 <see cref="Update"/> 消费（复位 inputActive），理由见 OnOpen / Update 的注释。
        /// </summary>
        private bool _openRolledBack;

        // UI 节点引用（UiEnhancer 构建后回填）
        internal TMP_InputField searchInput;
        internal Transform content;
        internal Transform template;
        internal RectTransform panelRect;

        /// <summary>子 Canvas GameObject。窗口根必须保持 active（MonoBehaviour 协程才能运行），
        /// 因此 Canvas 作为独立子物体，panel 指向它；StartClosed 只隐藏子 Canvas，窗口根仍 active。</summary>
        internal GameObject canvasObject;

        private void Awake()
        {
            // 窗口根 RectTransform 拉伸全屏（作为子 Canvas 的父级坐标系）
            RectTransform rootRt = GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // Canvas 作为独立子物体（挂在窗口根下）：窗口根保持 active，增量构建协程才能运行；
            // MenuWindow.StartClosed 会对 panel（子 Canvas）SetActive(false)，不影响窗口根。
            canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            RectTransform canvasRt = canvasObject.GetComponent<RectTransform>();
            canvasRt.anchorMin = Vector2.zero;
            canvasRt.anchorMax = Vector2.one;
            canvasRt.offsetMin = Vector2.zero;
            canvasRt.offsetMax = Vector2.zero;
        }

        public override bool openOnStart => false;
        public override bool closeOnPause => true;
        public override bool closeOnUICancel => true;
        public override bool blocksPlayerInput => true;
        public override bool selectOnOpen => true;
        public override Selectable objectToSelectOnOpen => searchInput;
        public override bool autoHideOnClose => true;
        public override GameObject panel => canvasObject;

        protected override void OnOpen()
        {
            base.OnOpen();
            // Setup 放在 OnOpen（每次 Open 都调用）而非 Initialize（仅首次 Open 调用）：
            // MenuWindow.Open 首次调用 Initialize 后置 initialized=true，若首次 Setup 因
            // ItemDatabase 未就绪失败（BuildCatalog 抛异常被 Setup 吞掉），后续 Open 不会重调
            // Initialize，F5 兜底会失效。OnOpen 每次都调用，配合 Setup 的幂等守卫
            // （existing.Initialized），实现真正的 F5 兜底。
            //
            // 必须包 try/catch：UiEnhancer.Setup 只对 _view.Init 做了内部保护，EnsureCanvas/Build/
            // OnMajorSelected 会向外抛。异常若冒泡出 OnOpen，MenuWindow.Open 的剩余步骤
            // （SelectStartingElement / SetInputActive(true)）被跳过，留下最坏的半开状态：
            //   isOpen==true 且仍在 AllActiveWindows → blocksPlayerInput 使
            //   GUIManager.UpdateWindowStatus 每帧置 windowBlockingInput=true → Character.CanDoInput()
            //   恒 false，角色完全不能动；
            //   同时 inputActive==false 使 TestCloseViaInput 失效 → Esc/暂停键都关不掉窗口。
            // 若玩家把 ToggleKey 配成 KeyCode.None，就只能重启游戏。
            try
            {
                UiEnhancer.Setup(this);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 构建 UI 失败，已回滚到关闭态: " + ex);
                // 立即回滚到关闭态：反射调 internal Close()，它会置 isOpen=false、从 AllActiveWindows
                // 移除（解除输入锁死）、调 OnClose()、SetInputActive(false)、Hide()（隐藏半成品面板）——
                // 全部都是这里需要的行为。回滚本身再包一层 try/catch，防止二次抛出又冒泡出 OnOpen。
                try
                {
                    if (CloseMethod != null)
                    {
                        CloseMethod.Invoke(this, null);
                    }
                    else
                    {
                        Plugin.Log.LogError("ItemSpawnerPremium: 反射获取 MenuWindow.Close 失败，无法回滚窗口状态");
                    }
                }
                catch (Exception rollbackEx)
                {
                    Plugin.Log.LogError("ItemSpawnerPremium: 回滚窗口状态失败: " + rollbackEx);
                }
                // 关键时序：此刻仍在 MenuWindow.Open() 调用栈中间，Close() 返回后 Open() 会继续执行
                // SelectStartingElement() 与 SetInputActive(true)，把 inputActive 重新置回 true，
                // 留下「isOpen==false 但 inputActive==true」的不一致。后果不是"关不掉"（窗口已关），
                // 而是 TestCloseViaInput 仍会响应并吞掉按键：closeOnPause 分支会把
                // Character.localCharacter.input.pauseWasPressed 置 false，导致玩家按 Esc 打不开暂停菜单
                // （GUIManager.UpdatePaused 读的是同一个 pauseWasPressed）。
                // 故用实例标志记录本次 Open 已失败，由下一帧的 Update 在 base.Update()
                //（内含 TestCloseViaInput）之前复位 inputActive，确保按键一次都不会被吞。
                _openRolledBack = true;
            }
        }

        /// <summary>
        /// 复位 OnOpen 失败回滚遗留的 inputActive（详见 OnOpen 中的时序注释），
        /// 必须在 base.Update()（内含 TestCloseViaInput）之前执行，否则本帧按键仍会被吞。
        /// </summary>
        protected override void Update()
        {
            if (_openRolledBack)
            {
                _openRolledBack = false;
                if (!isOpen)
                {
                    // 仅在仍处于关闭态时复位：若玩家在这一帧之前又成功 Open 了一次，
                    // isOpen 为 true，此时置 inputActive=false 反而会让新打开的窗口关不掉。
                    SetInputActive(false);
                }
            }
            base.Update();
        }

        /// <summary>用反射切换窗口显隐（MenuWindow.Open/Close 为 internal）。</summary>
        internal static void ToggleWindow(ItemSpawnerPremiumWindow w)
        {
            if (w == null)
            {
                return;
            }
            if (OpenMethod == null || CloseMethod == null)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 反射获取 MenuWindow.Open/Close 失败，无法切换窗口");
                return;
            }
            try
            {
                (w.isOpen ? CloseMethod : OpenMethod).Invoke(w, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 切换窗口失败: " + ex);
            }
        }

        /// <summary>
        /// 关闭窗口（反射 internal Close）。供 Warmup 在加载屏期间强制收起面板使用；
        /// 已关闭时直接返回，避免多余的 Close()（它内部会 Debug.Log 刷屏）。
        /// </summary>
        internal static void CloseWindow(ItemSpawnerPremiumWindow w)
        {
            if (w == null || !w.isOpen)
            {
                return;
            }
            if (CloseMethod == null)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 反射获取 MenuWindow.Close 失败，无法关闭窗口");
                return;
            }
            try
            {
                CloseMethod.Invoke(w, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 关闭窗口失败: " + ex);
            }
        }

        /// <summary>生成物品到本地角色手中（前置 Photon/角色检查后调 SpawnItemInHand）。</summary>
        internal static bool Spawn(Item item)
        {
            if (item == null)
            {
                return false;
            }
            // 加载屏期间拒绝生成：此时 Photon 消息队列可能被 LoadingScreenHandler.LoadingRoutine 关闭
            // （IsMessageQueueRunning=false），且场景/角色正在切换，RPC 到达房主端时
            // RPC_SpawnItemInHandMaster 里的 PhotonNetwork.Instantiate 落在错误场景或对着已销毁的角色
            // 调 Interact。游戏自身在同类操作前也一律先检查它（RunManager.cs:77、RunStarter.cs:9、
            // GUIManager.UpdatePaused）。LoadingScreenHandler.loading 是 public static 属性，
            // 读它不会触发 RetrievableResourceSingleton.Instance 的 Resources.Load。
            if (LoadingScreenHandler.loading)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 无法生成 " + item.gameObject.name + "（正在加载中）");
                return false;
            }
            // 用 InRoom 而非 IsConnected：后者在 OfflineMode 或仅连上 master server 未进房时也为 true，
            // 此时 SpawnItemInHand 的 RPC 会被 Photon 静默丢弃（只记 Warning），玩家看到"点了没反应"。
            if (!PhotonNetwork.InRoom || Character.localCharacter == null
                || Character.localCharacter.refs == null
                || Character.localCharacter.refs.items == null)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 无法生成 " + item.gameObject.name + "（未进入房间或本地角色不存在）");
                return false;
            }
            if (SpawnItemInHandMethod == null)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 反射获取 CharacterItems.SpawnItemInHand 失败，无法生成物品");
                return false;
            }
            // 连点节流：用 unscaledTime 而非 time，因为打开面板期间游戏可能被暂停（timeScale=0），
            // 那样 Time.time 不前进会把闸门永久卡住。被节流时不发 RPC，只记 LogDebug——
            // 连点是正常玩家操作，用 Warning 会刷屏日志。
            if (Time.unscaledTime - _lastSpawnTime < SpawnCooldown)
            {
                Plugin.Log.LogDebug("ItemSpawnerPremium: 生成过快，已忽略本次点击 " + item.gameObject.name);
                return false;
            }
            try
            {
                SpawnItemInHandMethod.Invoke(Character.localCharacter.refs.items, new object[] { item.gameObject.name });
                // 只在 RPC 真正派发成功后推进时间戳：若 Invoke 抛异常（RPC 未发出），
                // 不应因此让下一次合法点击也被节流掉。
                _lastSpawnTime = Time.unscaledTime;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 生成物品失败: " + ex);
                return false;
            }
        }
    }
}
