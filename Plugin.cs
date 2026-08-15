using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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
    }
}
