using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemSpawnerEnhancement
{
    [BepInPlugin("com.itemspawnerplus.ItemSpawnerPlus", "ItemSpawnerPlus", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }

        /// <summary>窗口静态引用，供 Update（F5 轮询）/ Warmup 使用。</summary>
        internal static ItemSpawnerPlusWindow Window { get; private set; }

        /// <summary>收藏集合（Config 持久化），供 ItemListView/UiEnhancer 使用。</summary>
        internal static FavoriteStore Favorites { get; private set; }

        /// <summary>UI 样式：HandDrawn=手绘风；Transparent=透明风。实时读 Config，改配置立即生效（热重载）。</summary>
        internal static string UiStyle
        {
            get { return (_styleEntry != null) ? _styleEntry.Value : "HandDrawn"; }
        }

        private static ConfigEntry<Key> _toggleKey;
        private static ConfigEntry<string> _styleEntry;

        private void Awake()
        {
            Log = Logger;

            // BepInEx 5.x 的 ConfigEntry.Value setter 不会自动写盘，开启后每次赋值立即持久化到磁盘
            Config.SaveOnConfigSet = true;

            _toggleKey = Config.Bind<Key>(
                "General",
                "ToggleKey",
                Key.F5,
                "打开/关闭物品生成器窗口的按键（Unity.InputSystem.Key 枚举值）。");

            _styleEntry = Config.Bind<string>(
                "General",
                "Style",
                "HandDrawn",
                new ConfigDescription(
                    "UI 样式：HandDrawn=手绘风（默认，实色暖卡纸）；Transparent=透明风（面板半透明，搜索框不透明）。",
                    new AcceptableValueList<string>("HandDrawn", "Transparent")));
            // 热重载：PEAKLib.ModConfig 改样式时触发 SettingChanged，重新烘焙 Sprite 并套用到所有 Image
            _styleEntry.SettingChanged += delegate { UiEnhancer.OnStyleChanged(); };

            Favorites = new FavoriteStore(Config.Bind<string>("Favorites", "ItemNames", "[]",
                new ConfigDescription(
                    "收藏物品的 prefab 名（JSON 数组），在物品生成器 UI 中右键物品管理。",
                    null,
                    "Hidden")));

            Harmony harmony = new Harmony("com.itemspawnerplus.ItemSpawnerPlus");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.LogInfo("ItemSpawnerPlus loaded!");

            // 面板预热：常驻轮询驱动器（跨场景复用，消除首次 F5 卡顿）。
            GameObject warmupGo = new GameObject("ItemSpawnerPlusWarmup");
            UnityEngine.Object.DontDestroyOnLoad(warmupGo);
            warmupGo.AddComponent<Warmup>();
        }

        /// <summary>每帧轮询 ToggleKey，触发窗口显隐切换。</summary>
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (Window == null || keyboard == null || _toggleKey == null || _toggleKey.Value == Key.None)
            {
                return;
            }
            if (keyboard[_toggleKey.Value].wasPressedThisFrame)
            {
                ItemSpawnerPlusWindow.ToggleWindow(Window);
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
                    Log.LogError("ItemSpawnerPlus: 创建窗口失败: " + ex);
                }
            }
        }

        private static void CreateWindow()
        {
            GameObject go = new GameObject("ItemSpawnerPlus", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            Window = go.AddComponent<ItemSpawnerPlusWindow>();
        }

        /// <summary>
        /// 防御性修复游戏 bug：Peak.WarpOnThrow.OnDisable 直接调用 trailFX.Stop() 但未判空，
        /// 当 trailFX 未赋值（如 Warpsketball 太空篮球实例）时抛 NullReferenceException。
        /// 在生成/销毁太空篮球时触发。这里在 trailFX 为空时手动完成等效清理并跳过原逻辑。
        /// </summary>
        [HarmonyPatch(typeof(Peak.WarpOnThrow), nameof(Peak.WarpOnThrow.OnDisable))]
        private static class Patch_WarpOnThrow_OnDisable
        {
            private static bool Prefix(Peak.WarpOnThrow __instance)
            {
                if (__instance.trailFX != null)
                {
                    return true; // 正常情况，走原逻辑
                }
                try
                {
                    // 等效清理 1：移除 GlobalEvents.OnItemThrown 委托（原逻辑的一部分）
                    MethodInfo onThrown = typeof(Peak.WarpOnThrow).GetMethod("OnItemThrown",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (onThrown != null)
                    {
                        Action<Item> d = (Action<Item>)Delegate.CreateDelegate(typeof(Action<Item>), __instance, onThrown);
                        GlobalEvents.OnItemThrown -= d;
                    }
                    else
                    {
                        Log.LogError("ItemSpawnerPlus: WarpOnThrow.OnItemThrown 反射失败，委托未移除，可能泄漏");
                    }
                    // 等效清理 2：调用与基类 OnDisable 方法体完全等效的静态方法。
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
                    Log.LogWarning("ItemSpawnerPlus: WarpOnThrow.OnDisable 防御修复异常: " + ex.Message);
                }
                return false; // 跳过原逻辑（避免 trailFX.Stop() 空引用崩溃）
            }
        }
    }
}
