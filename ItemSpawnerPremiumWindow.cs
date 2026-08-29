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
        /// 回滚标记：本次 MenuWindow.Open() 中 OnOpen 失败并已反射 Close() 回滚。
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
        /// <summary>
        /// 手柄下打开面板时的默认选中项：第一个分类按钮，而不是搜索框。
        ///
        /// MenuWindow.Open 在手柄方案下会真的 EventSystem.SetSelectedGameObject(此对象)
        /// （键鼠下 UIInputHandler.SetSelectedObject 是空操作），而 TMP_InputField 被 Select 后
        /// 会因 shouldActivateOnSelect 自动 ActivateInputField，激活后 m_AllowInput 为 true，
        /// 其 OnMove 实现是 `if (!m_AllowInput) base.OnMove(...)` —— 输入框吞掉全部方向导航，
        /// 手柄玩家进了搜索框就摇不出来，只能打字（面板仍能用 UI 取消键关掉，不是死锁，
        /// 但等于只剩一个功能可用）。
        /// 键鼠下这个返回值无关紧要（SetSelectedObject 空操作），焦点由 UiEnhancer.FocusSearch 处理。
        /// </summary>
        public override Selectable objectToSelectOnOpen
        {
            get { return UiEnhancer.IsGamepadScheme() ? UiEnhancer.GetGamepadStartingSelectable() : (Selectable)searchInput; }
        }
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
        /// 每帧校验窗口状态不变量，命中非法组合就自愈。
        ///
        /// 为什么需要无条件自愈、而不只是消费 <see cref="_openRolledBack"/>：
        /// MenuWindow.Open() 的步骤序列是
        ///   isOpen=true → AllActiveWindows.Add → Show() → Initialize() → OnOpen()
        ///   → if (selectOnOpen) SelectStartingElement() → SetInputActive(true)
        /// 我们只能包住 OnOpen（见其注释）。而 SelectStartingElement 是 MenuWindow 的 **private**
        /// 方法（反编译 MenuWindow.cs:173-176），无法 override、无法包 try；它调
        /// UIInputHandler.SetSelectedObject，后者在手柄方案下执行
        /// `EventSystem.current.SetSelectedGameObject(obj)` 而**对 EventSystem.current 不判空**
        /// （UIInputHandler.cs:69-75）。本窗口 selectOnOpen 为 true，主动走了这条分支。
        ///
        /// 若那一行抛出，异常在 SetInputActive(true) **之前**冒泡出 Open()，被 ToggleWindow 的
        /// catch 吞掉，留下最坏的半开状态：
        ///   isOpen==true 且仍在 AllActiveWindows → blocksPlayerInput 使
        ///   GUIManager.UpdateWindowStatus 每帧置 windowBlockingInput=true → Character.CanDoInput()
        ///   恒 false，角色完全不能动；
        ///   同时 inputActive==false 使 TestCloseViaInput 整个方法体被 `if (inputActive)` 挡住
        ///   （MenuWindow.cs:59-74）→ Esc / UI 取消都关不掉窗口。
        /// 此时 _openRolledBack 为 false（根本没进 OnOpen 的 catch），旧实现不会做任何补救，
        /// 玩家只能靠 F5 或重启。
        ///
        /// 「isOpen && !inputActive」是一个 MenuWindow 正常运行时不该出现的组合：Open() 结尾必置
        /// inputActive=true，Close() 会同时清 isOpen 与 inputActive。因此把它当作"Open 中途断裂"
        /// 的信号并强制 Close()，对任何断裂点都有效，不需要知道具体断在哪一步。
        /// 反向的「!isOpen && inputActive」则是 OnOpen 回滚后 Open() 继续执行 SetInputActive(true)
        /// 造成的（详见 OnOpen 的时序注释），必须在 base.Update()（内含 TestCloseViaInput）之前
        /// 复位，否则本帧按键仍会被吞。
        /// </summary>
        protected override void Update()
        {
            if (isOpen && !inputActive)
            {
                // Open() 中途断裂：窗口正锁着玩家输入却又关不掉，强制走完整的 Close 解锁。
                // 只记一条 Error（Close 成功后 isOpen 变 false，下一帧不再命中，不会刷屏）。
                Plugin.Log.LogError("ItemSpawnerPremium: 检测到窗口处于半开状态"
                    + "（isOpen 为真但输入未激活，说明 MenuWindow.Open 中途抛异常），已强制关闭以解除输入锁定");
                CloseWindow(this);
            }
            else if (_openRolledBack)
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
            Character local = Character.localCharacter;
            if (!PhotonNetwork.InRoom || local == null
                || local.refs == null
                || local.refs.items == null)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 无法生成 " + item.gameObject.name + "（未进入房间或本地角色不存在）");
                return false;
            }
            // player 必须判空：房主端 Item.Interact 第一行就是 `interactor.player.HasEmptySlot(itemID)`
            // （反编译 Item.cs:549-551），而 Character.player 是
            // `PlayerHandler.GetPlayer(view.Owner)` → `m_playerLookup.GetValueOrDefault(ActorNumber)`
            // （Character.cs:201 / PlayerHandler.cs:206-209），**可以返回 null**。
            // 窗口期是"角色已 spawn 但 PlayerHandler 尚未注册 / 已注销"。
            // 该 NRE 发生在游戏侧 ExecuteRpc 的 methodInfo.Invoke 里（无 try/catch），
            // 本插件下方的 catch 只覆盖本地 Invoke，捕不到，所以只能在发 RPC 之前拦住。
            //
            // 注意这里只拦"会让房主端抛异常"的情况。**不检查背包是否已满、角色是否死亡/昏迷** ——
            // 那些是游戏自身的机制（手上拿满了新物品就掉地上、尸体也能拾取），不是本模组的缺陷，
            // 替玩家拦下来反而是剥夺操作自由。
            if (local.player == null)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 无法生成 " + item.gameObject.name + "（玩家数据尚未就绪）");
                return false;
            }
            if (SpawnItemInHandMethod == null)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 反射获取 CharacterItems.SpawnItemInHand 失败，无法生成物品");
                return false;
            }
            // prefab 可加载性预检。房主端的 RPC_SpawnItemInHandMaster 是
            //   PhotonNetwork.Instantiate("0_Items/" + objName, ...).GetComponent<Item>().Interact(character)
            // （CharacterItems.cs:1112-1116）—— **对 Instantiate 的返回值不判空**。
            // 而 PhotonNetwork.Instantiate 有两条返回 null 的路径（PhotonNetwork.cs:1733-1751）：
            // prefabPool.Instantiate 失败（DefaultPool 走 Resources.Load，失败只记 Error 返回 null，
            // DefaultPool.cs:13-20）、或 prefab 没有 PhotonView。任一命中都会让 .GetComponent<Item>()
            // 在游戏侧 RPC 里 NRE，本插件捕不到。
            //
            // 纯 vanilla 下不可达：实测 ItemDatabase.Objects 的 194 项全部有对应的
            // Resources 条目 "0_Items/<gameObject.name>"，且全部带 PhotonView。
            // 但物品模组可以通过 DatabaseAsset.AddRuntimeEntry（Zorro.Core\DatabaseAsset.cs:11-14）
            // 或 ItemDatabase.Add（[ConsoleCommand]）在运行时往库里追加条目，那些 prefab 通常来自
            // AssetBundle、**不在 Resources/0_Items 下**。此时本模组目录里会出现一个可点击条目，
            // 点一下就在房主端炸一个不可捕获的 NRE。
            // Resources.Load 自身带缓存（且 DefaultPool.ResourceCache 后续也会命中），
            // 把这个游戏侧不可捕获的崩溃降级成本插件的一条 Warning，代价可以接受。
            string prefabPath = "0_Items/" + item.gameObject.name;
            bool loadable;
            try
            {
                loadable = Resources.Load<GameObject>(prefabPath) != null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("ItemSpawnerPremium: prefab 可加载性检查失败，跳过该前置判定: " + ex.Message);
                loadable = true;
            }
            if (!loadable)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 无法生成 " + item.gameObject.name
                    + "（Resources 中找不到 " + prefabPath + "，可能是未随 Resources 打包的模组物品）");
                return false;
            }
            try
            {
                SpawnItemInHandMethod.Invoke(local.refs.items, new object[] { item.gameObject.name });
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
