using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ItemSpawnerEnhancement
{
    [BepInPlugin("com.itemspawnerplus.ItemSpawnerPlus", "ItemSpawnerPlus", "1.0.0")]
    [BepInDependency("com.quackandcheese.ItemSpawner")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony("com.itemspawnerplus.ItemSpawnerPlus");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.LogInfo("ItemSpawner Enhancement loaded!");
        }

        /// <summary>
        /// 接管 ItemSpawner 的 ItemSpawnerWindow.Initialize：
        /// Prefix 在原始 body 之前完成 UI 接管——原始 Initialize 方法体内先调用 RefreshEntries，
        /// 其 Prefix 检查 UiEnhancer.SetupSucceeded；若放在 Postfix 才 Setup，首窗口总会先跑一遍原逻辑
        /// （双重填充），且静态标志跨窗口陈旧。
        /// Postfix 仅销毁原始 body 新增的 SearchScript（接管成功后原搜索逻辑已冗余）。
        /// </summary>
        [HarmonyPatch(typeof(ItemSpawner.ItemSpawnerWindow), nameof(ItemSpawner.ItemSpawnerWindow.Initialize))]
        private static class Patch_Initialize
        {
            private static void Prefix(ItemSpawner.ItemSpawnerWindow __instance)
            {
                try
                {
                    UiEnhancer.Setup(__instance);
                }
                catch (System.Exception ex)
                {
                    Log.LogError("ItemSpawner Enhancement failed during Initialize prefix: " + ex);
                }
            }

            private static void Postfix(ItemSpawner.ItemSpawnerWindow __instance)
            {
                // 仅当接管成功才销毁原 SearchScript；Setup 失败时保留原搜索逻辑作为回退。
                if (!UiEnhancer.SetupSucceeded)
                {
                    return;
                }
                ItemSpawner.SearchScript old = __instance.GetComponent<ItemSpawner.SearchScript>();
                if (old != null)
                {
                    UnityEngine.Object.Destroy(old);
                }
            }
        }

        /// <summary>
        /// 接管 RefreshEntries：仅当 UiEnhancer.Setup 成功接管 UI 后才禁用原逻辑，
        /// 否则回退到原模组的填充逻辑，避免 Setup 失败时窗口永久空白。
        /// </summary>
        [HarmonyPatch(typeof(ItemSpawner.ItemSpawnerWindow), nameof(ItemSpawner.ItemSpawnerWindow.RefreshEntries))]
        private static class Patch_RefreshEntries
        {
            private static bool Prefix()
            {
                return !UiEnhancer.SetupSucceeded;
            }
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
