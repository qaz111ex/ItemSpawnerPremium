using System;
using UnityEngine;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 面板预热驱动器：在游戏加载阶段主动提前执行一次 UiEnhancer.Setup，
    /// 把 BuildCatalog（本地化/分类/拼音计算）与 Rebuild（Instantiate 条目克隆 + 纹理上传 + 字体加载）
    /// 从首次按 F5 打开面板的时刻挪到加载阶段，消除首次 F5 的明显卡顿。
    /// 满足全部就绪条件后执行一次 Setup，随后销毁自身停止轮询（避免每帧 FindObjectOfType 浪费）。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;

        private void Update()
        {
            // 每 0.5 秒检查一次（unscaledTime 不受时间缩放/暂停影响）
            if (Time.unscaledTime < _nextCheckTime)
            {
                return;
            }
            _nextCheckTime = Time.unscaledTime + 0.5f;
            TryWarmup();
        }

        private void TryWarmup()
        {
            // 已预热则直接停止（正常情况不会走到，双保险）
            if (UiEnhancer.SetupSucceeded)
            {
                Destroy(gameObject);
                return;
            }

            // 1. 窗口实例存在（由原模组在 GUIManager.Start 时创建）
#pragma warning disable CS0618 // FindObjectOfType 在本游戏运行时仍受支持，且为任务指定 API
            ItemSpawner.ItemSpawnerWindow window = UnityEngine.Object.FindObjectOfType<ItemSpawner.ItemSpawnerWindow>();
#pragma warning restore CS0618
            if (window == null)
            {
                return;
            }

            // 2. 物品数据库已加载
            ItemDatabase db = SingletonAsset<ItemDatabase>.Instance;
            if (db == null || db.Objects == null || db.Objects.Count == 0)
            {
                return;
            }

            // 3. 游戏字体系统已就绪（确保预热时字体选择正确）
            if (FontFallbackSwapper.instance == null)
            {
                return;
            }

            try
            {
                UiEnhancer.Setup(window);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: Warmup setup failed: " + ex);
            }

            // 无论成功失败都停止自身，避免每帧 FindObjectOfType 浪费
            Destroy(gameObject);
        }
    }
}
