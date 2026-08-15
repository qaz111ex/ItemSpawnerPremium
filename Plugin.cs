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
        /// 在 ItemSpawner 的 ItemSpawnerWindow.Initialize 完成后接管 UI：
        /// 搜索框移到顶部居中放大、增加分类按钮条、本地化名称与中文/拼音搜索。
        /// </summary>
        [HarmonyPatch(typeof(ItemSpawner.ItemSpawnerWindow), nameof(ItemSpawner.ItemSpawnerWindow.Initialize))]
        private static class Patch_Initialize
        {
            private static void Postfix(ItemSpawner.ItemSpawnerWindow __instance)
            {
                try
                {
                    UiEnhancer.Setup(__instance);
                }
                catch (System.Exception ex)
                {
                    Log.LogError("ItemSpawner Enhancement failed during Initialize postfix: " + ex);
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
                    // 等效清理 2：调用基类 MonoBehaviourPunCallbacks.OnDisable
                    MethodInfo baseOnDisable = typeof(MonoBehaviourPunCallbacks).GetMethod("OnDisable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (baseOnDisable != null)
                    {
                        baseOnDisable.Invoke(__instance, null);
                    }
                    else
                    {
                        Log.LogError("ItemSpawnerPlus: MonoBehaviourPunCallbacks.OnDisable 反射失败，基类清理被跳过");
                    }
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
