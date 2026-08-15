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
            Harmony harmony = new Harmony("com.example.ItemSpawnerEnhancement");
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
        /// 完全接管 RefreshEntries：原逻辑用英文内部名填充条目且无分类排序。
        /// </summary>
        [HarmonyPatch(typeof(ItemSpawner.ItemSpawnerWindow), nameof(ItemSpawner.ItemSpawnerWindow.RefreshEntries))]
        private static class Patch_RefreshEntries
        {
            private static bool Prefix()
            {
                return false;
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
                    // 等效清理 2：调用基类 MonoBehaviourPunCallbacks.OnDisable
                    MethodInfo baseOnDisable = typeof(MonoBehaviourPunCallbacks).GetMethod("OnDisable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (baseOnDisable != null)
                    {
                        baseOnDisable.Invoke(__instance, null);
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
