using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ItemSpawnerEnhancement
{
    [BepInPlugin("com.itemspawnerpremium.ItemSpawnerPremium", "ItemSpawnerPremium", "2.3.1")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }

        /// <summary>窗口静态引用，供 Update（F5 轮询）/ Warmup 使用。</summary>
        internal static ItemSpawnerPremiumWindow Window { get; private set; }

        /// <summary>收藏集合（Config 持久化），供 ItemListView/UiEnhancer 使用。</summary>
        internal static FavoriteStore Favorites { get; private set; }

        /// <summary>UI 样式：HandDrawn=手绘风；Transparent=透明风。实时读 Config，改配置立即生效（热重载）。</summary>
        internal static string UiStyle
        {
            get { return (_styleEntry != null) ? _styleEntry.Value : "HandDrawn"; }
        }

        /// <summary>是否隐藏装饰/测试/无用物品（棋子、撕下的书页等）。实时读 Config，改配置立即生效。</summary>
        internal static bool HideUnused
        {
            get { return _hideUnused == null || _hideUnused.Value; }
        }

        /// <summary>
        /// 样式热重载请求标志：由 Config 的 SettingChanged 回调置位，Warmup.Update（主线程）消费。
        /// 放在 Plugin 而非 Warmup：置位方是 Plugin 持有的 ConfigEntry 回调，
        /// 且 Warmup 实例不是全局可达的单例引用（只有 GameObject 持有），静态标志放这里最省事且不需要判空。
        /// volatile：回调可能来自非主线程（见下方 SettingChanged 注释），保证主线程能立即观察到写入。
        /// </summary>
        internal static volatile bool StyleChangeRequested;

        /// <summary>
        /// 物品隐藏开关变更请求标志。与 <see cref="StyleChangeRequested"/> 同理由，
        /// 由 Config 回调置位、Warmup.Update（主线程）消费。
        ///
        /// 为什么连 GetComponent 都不能放在回调里：BepInEx 的 SettingChanged 在**调用方线程**
        /// 同步派发（ConfigEntry.Value setter → ConfigFile.OnSettingChanged，同栈帧遍历
        /// invocation list 直接调用 handler）。`Window != null` 是 UnityEngine.Object 的重载
        /// 运算符（进入原生 IsNativeObjectAlive）、`GetComponent<T>()` 是纯原生调用 ——
        /// 两者都是主线程专属。旧实现的注释写着"只置标志"，实际却做了这两次 Unity 调用，
        /// 与它自己声明的安全口径不符。现在两个回调都只写一个 volatile bool，口径统一。
        /// </summary>
        internal static volatile bool HideUnusedChangeRequested;


        private static ConfigEntry<KeyCode> _toggleKey;
        private static ConfigEntry<string> _styleEntry;
        private static ConfigEntry<bool> _hideUnused;

        private void Awake()
        {
            Log = Logger;

            // 注入本地化门面的依赖：Loc/LocalizationCatalog 刻意不引用游戏程序集与 BepInEx
            // （才能被 tests\ 下的单元测试直接加载并验证内嵌资源与英文回退链），
            // 因此「当前语言代码」与「告警输出」由这里注入。必须早于任何 Loc.Get 调用。
            Loc.LanguageCodeProvider = delegate { return GameLanguage.CurrentCode; };
            Loc.WarningLogger = delegate (string message) { Log.LogWarning("ItemSpawnerPremium: " + message); };

            // 显式声明依赖：收藏在 FavoriteStore 中通过 ConfigEntry.Value 赋值持久化，
            // 依赖 SaveOnConfigSet 立即写盘（BepInEx 5 默认即 true，此处显式写出以防默认值变更）。
            Config.SaveOnConfigSet = true;

            _toggleKey = Config.Bind<KeyCode>(
                "General",
                "ToggleKey",
                KeyCode.F5,
                "打开/关闭物品生成器窗口的按键（UnityEngine.KeyCode 枚举值）。");

            _styleEntry = Config.Bind<string>(
                "General",
                "Style",
                "HandDrawn",
                new ConfigDescription(
                    "UI 样式：HandDrawn=手绘风（默认，实色暖卡纸）；Transparent=透明风（面板半透明，搜索框不透明）。",
                    new AcceptableValueList<string>("HandDrawn", "Transparent")));
            // 热重载：PEAKLib.ModConfig 改样式时触发 SettingChanged，重新烘焙 Sprite 并套用到所有 Image。
            // 只置标志、不直接调 UiEnhancer.OnStyleChanged()：BepInEx 的 SettingChanged 在调用方线程
            // 同步派发（配置界面 / 文件监视线程都可能是调用方），而 OnStyleChanged 内部全是主线程专属
            // API（Object.Destroy / new Texture2D / SetPixels32 / Apply / Sprite.Create /
            // GetComponentsInChildren），在非主线程调用会抛。改由 Warmup.Update（DontDestroyOnLoad
            // 常驻组件，保证主线程每帧运行）消费——与下方 HideUnused 回调的处理方式一致。
            _styleEntry.SettingChanged += delegate
            {
                StyleChangeRequested = true;
            };

            _hideUnused = Config.Bind<bool>(
                "General",
                "HideUnused",
                true,
                "隐藏装饰/测试/无用物品（棋子、撕下的书页等）。设为 false 显示全部物品。");
            _hideUnused.SettingChanged += delegate
            {
                // 只写一个 volatile bool，绝不触碰任何 Unity API（连 GetComponent 都不行，
                // 理由见 HideUnusedChangeRequested 的注释）。由 Warmup.Update 在主线程消费。
                HideUnusedChangeRequested = true;
            };

            Favorites = new FavoriteStore(Config.Bind<string>("Favorites", "ItemNames", "[]",
                new ConfigDescription(
                    "收藏物品的 prefab 名（JSON 数组），在物品生成器 UI 中右键物品管理。",
                    null,
                    "Hidden")));

            // 面板预热：常驻轮询驱动器（跨场景复用，消除首次 F5 卡顿）。
            // 必须先于 Harmony patch 创建：Warmup 除了预热，还负责消费样式热重载标志
            // （StyleChangeRequested）与在 Setup 未完成时重试；即使下面的 patch 全部失败
            //（窗口不会被创建），Warmup 也应该存在并空转，而不是连带缺失。
            GameObject warmupGo = new GameObject("ItemSpawnerPremiumWarmup");
            UnityEngine.Object.DontDestroyOnLoad(warmupGo);
            warmupGo.AddComponent<Warmup>();

            // 逐个 patch 而非 harmony.PatchAll：PatchAll 遇到任一 patch 目标签名变化（游戏更新）
            // 会抛出并中断整个循环，未处理的 patch 类被连带跳过（顺序还依赖反射返回的类型顺序，不可控）。
            // CreateClassProcessor 逐类处理 + 各自 try/catch，做到故障隔离：
            // 例如 Peak.WarpOnThrow 被游戏重构后，窗口创建 patch 仍然生效。
            // （已确认 0Harmony 2.9.0.0 有 Harmony.CreateClassProcessor(Type) → PatchClassProcessor.Patch()）
            Harmony harmony = new Harmony("com.itemspawnerpremium.ItemSpawnerPremium");
            ApplyPatch(harmony, typeof(Patch_GUIManager_Start));
            ApplyPatch(harmony, typeof(Patch_WarpOnThrow_OnDisable));
            Log.LogInfo("ItemSpawnerPremium loaded!");
        }

        /// <summary>单个 patch 类的隔离应用：失败只记 Error，不影响其他 patch。</summary>
        private static void ApplyPatch(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
            }
            catch (Exception ex)
            {
                Log.LogError("ItemSpawnerPremium: 应用补丁 " + patchClass.Name + " 失败: " + ex);
            }
        }

        /// <summary>每帧轮询 ToggleKey，触发窗口显隐切换。</summary>
        private void Update()
        {
            // 整个方法体包一层 try/catch：这是本模组进入 Unity 每帧调用栈的边界之一。
            // 若这里抛出，Unity 会每帧记一条**不带本模组前缀**的异常，且 Update 每帧在同一行断掉
            // → F5 从此彻底失效，而玩家/排障者很难把日志归因到本插件。
            // 捕获后置位 _updateFaulted 停止后续轮询，只留一条带前缀的 Error，避免刷屏。
            if (_updateFaulted)
            {
                return;
            }
            try
            {
                PollToggleKey();
            }
            catch (Exception ex)
            {
                _updateFaulted = true;
                Log.LogError("ItemSpawnerPremium: 按键轮询异常，已停止轮询（重启游戏可恢复）: " + ex);
            }
        }

        /// <summary>Update 抛异常后置位，停止后续轮询避免每帧刷屏。</summary>
        private bool _updateFaulted;

        private void PollToggleKey()
        {
            if (Window == null || _toggleKey == null || _toggleKey.Value == KeyCode.None)
            {
                return;
            }
            // 加载屏期间不响应开关：加载中生成会被 Spawn 拒绝，而 Warmup 每 0.5 秒轮询一次
            // 也会把加载屏期间打开的面板强行收起 —— 玩家看到的是"按了 F5 面板闪一下又没了"，
            // 像是 F5 失灵。把"加载中不开面板"收敛到这一个决策点，比开了再被另一个组件收回更干净。
            if (LoadingScreenHandler.loading)
            {
                return;
            }
            // 搜索框聚焦时，只跳过「会被输入框当文本吃掉」的按键：
            // ToggleKey 若配成字母/数字等可打印键（如 KeyCode.G），TMP_InputField 会把它作为文本输入，
            // 同时这里的 Input.GetKeyDown 也会命中并切换窗口，玩家打一个 "g" 就等于关面板。
            //
            // 但**绝不能对所有按键生效**：面板打开时会自动激活搜索框（UiEnhancer.FocusSearch，
            // 实现"打开即搜索"），若无条件跳过，默认的 F5 就永远收不到 —— 表现为"打开后不与面板交互
            // 就再也关不掉，必须先点一下按钮或拖一下滚动条让输入框失焦"。这是 2.2.0 引入的回归，
            // 根因是把「防误吞文本键」写成了「屏蔽全部按键」。F5 等功能键不参与文本输入，必须放行。
            if (Window.isOpen
                && InputKeys.IsTextInput((int)_toggleKey.Value)
                && Window.searchInput != null
                && Window.searchInput.isFocused)
            {
                return;
            }
            // 游戏启用 legacy Input（Active Input Handling = Both，游戏自身大量使用 Input.GetKeyDown，见 BingBongPhysics/CinemaCamera 等），
            // 直接用 Input.GetKeyDown 轮询 KeyCode，覆盖全部 KeyCode 值（含 Alpha1/LeftControl/CapsLock 等），无 KeyCode→InputSystem.Key 名称映射问题。
            if (Input.GetKeyDown(_toggleKey.Value))
            {
                ItemSpawnerPremiumWindow.ToggleWindow(Window);
            }
        }

        /// <summary>
        /// 在 GUIManager.Start 之后创建独立窗口（DontDestroyOnLoad，跨场景复用，仅创建一次）。
        /// </summary>
        [HarmonyPatch(typeof(GUIManager), "Start")]
        private static class Patch_GUIManager_Start
        {
            private static void Postfix()
            {
                if (Window != null)
                {
                    return;
                }
                try
                {
                    CreateWindow();
                }
                catch (Exception ex)
                {
                    Log.LogError("ItemSpawnerPremium: 创建窗口失败: " + ex);
                }
            }
        }

        private static void CreateWindow()
        {
            GameObject go = new GameObject("ItemSpawnerPremium", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            Window = go.AddComponent<ItemSpawnerPremiumWindow>();
        }

        /// <summary>
        /// 防御性修复游戏 bug：Peak.WarpOnThrow.OnDisable 直接调用 trailFX.Stop() 但未判空，
        /// 当 trailFX 未赋值（如 Warpsketball 太空篮球实例）时抛 NullReferenceException。
        /// 在生成/销毁太空篮球时触发。这里在 trailFX 为空时手动完成等效清理并跳过原逻辑。
        /// </summary>
        [HarmonyPatch(typeof(Peak.WarpOnThrow), nameof(Peak.WarpOnThrow.OnDisable))]
        private static class Patch_WarpOnThrow_OnDisable
        {
            /// <summary>
            /// Peak.WarpOnThrow.OnItemThrown（private void OnItemThrown(Item)）的缓存 MethodInfo：
            /// 每次物品禁用都反射查找太浪费，提到静态字段一次解析（做法与
            /// ItemSpawnerPremiumWindow 的 Open/Close/SpawnItemInHand 缓存一致）。
            /// 显式指定参数类型避免未来重载导致 AmbiguousMatchException；GetMethod 找不到只返回 null 不抛，
            /// 因此静态初始化不会升级为 TypeInitializationException。
            /// </summary>
            private static readonly MethodInfo OnItemThrownMethod =
                typeof(Peak.WarpOnThrow).GetMethod("OnItemThrown",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Item) }, null);

            private static bool Prefix(Peak.WarpOnThrow __instance)
            {
                if (__instance.trailFX != null)
                {
                    return true; // 正常情况，走原逻辑
                }
                // 反射失败（游戏改名/改签名）时仍然 return false 走本分支，绝不 return true：
                // 走到这里说明 trailFX == null，原逻辑的 trailFX.Stop() 必定 NRE。
                // 相比"委托没退订"（后果是 GlobalEvents.OnItemThrown 持有已销毁实例，
                // OnItemThrown 里有 obj == item 与 trailFX 判空，最坏是无效调用与轻微泄漏），
                // 每次禁用都抛 NRE 会打断 Unity 的 OnDisable 派发链，后果严重得多。
                try
                {
                    // 等效清理 1：移除 GlobalEvents.OnItemThrown 委托（原逻辑的一部分）
                    if (OnItemThrownMethod != null)
                    {
                        Action<Item> d = (Action<Item>)Delegate.CreateDelegate(typeof(Action<Item>), __instance, OnItemThrownMethod);
                        GlobalEvents.OnItemThrown -= d;
                    }
                    else
                    {
                        Log.LogError("ItemSpawnerPremium: WarpOnThrow.OnItemThrown 反射失败，委托未移除，可能泄漏");
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning("ItemSpawnerPremium: WarpOnThrow.OnDisable 退订委托异常: " + ex.Message);
                }
                try
                {
                    // 等效清理 2：调用与基类 OnDisable 方法体完全等效的静态方法。
                    // 独立 try（不与上面的退订共用）：原逻辑里 base.OnDisable() 是第一步、无条件执行，
                    // 我们 return false 跳过原逻辑后有义务无条件补上；若与退订共用一个 try，
                    // 退订抛异常就会连带跳过 RemoveCallbackTarget，同时泄漏 Photon 回调目标和静态委托。
                    //
                    // 绝不要用 MethodInfo.Invoke 调用自身已被 Harmony patch 的虚方法：
                    // MonoBehaviourPunCallbacks.OnDisable 是 virtual，MethodInfo.Invoke 做虚分派
                    // 会分派到已被 patch 的 WarpOnThrow.OnDisable override，导致
                    // Prefix -> Invoke -> OnDisable -> Prefix 无限递归、栈溢出硬崩溃。
                    // PhotonNetwork.RemoveCallbackTarget 是 public static（参数 object），
                    // 与基类 OnDisable 方法体完全等效，无虚分派风险。
                    PhotonNetwork.RemoveCallbackTarget(__instance);
                }
                catch (Exception ex)
                {
                    Log.LogWarning("ItemSpawnerPremium: WarpOnThrow.OnDisable 移除 Photon 回调目标异常: " + ex.Message);
                }
                return false; // 跳过原逻辑（避免 trailFX.Stop() 空引用崩溃）
            }
        }
    }
}
