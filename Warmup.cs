using System;
using UnityEngine;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 面板预热驱动器：常驻轮询，只做 CPU 预热（提前 Setup）。
    /// 主动提前执行一次 UiEnhancer.Setup，把 BuildCatalog（本地化/分类/拼音计算）、
    /// Rebuild（Instantiate 条目克隆 + 纹理上传 + 字体加载）提前到加载阶段，
    /// 消除首次 F5 打开面板时的明显卡顿。不在加载屏幕期间 SetActive 面板（避免闪屏）。
    ///
    /// 常驻（不 Destroy）：<see cref="Update"/> 每 0.5 秒轮询一次；
    /// 窗口是 DontDestroyOnLoad 单例（全程只有一个实例），预热过一次后由 <see cref="_warmed"/> 标志跳过，
    /// 不会因切场景重建。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _warmed;                                      // 已预热过（窗口是 DontDestroyOnLoad 单例，预热一次即可）

        private void Update()
        {
            // 每 0.5 秒检查一次（unscaledTime 不受时间缩放/暂停影响）
            if (Time.unscaledTime < _nextCheckTime)
            {
                return;
            }
            _nextCheckTime = Time.unscaledTime + 0.5f;
            TryStartWarmup();
        }

        private void TryStartWarmup()
        {
            if (_warmed)
            {
                return; // 已预热过（含已 Setup / 已失败，失败也不重试，F5 时由 Initialize 兜底）
            }

            // 窗口是 DontDestroyOnLoad 单例且默认 inactive，FindObjectOfType 找不到，直接取 Plugin.Window 静态引用
            ItemSpawnerPlusWindow window = Plugin.Window;
            if (window == null)
            {
                return; // 窗口尚未创建（GUIManager.Start 还没跑），等下一轮
            }

            ItemListView view = window.GetComponent<ItemListView>();
            if (view != null && view.Initialized)
            {
                // 已被别处（F5 兜底）Setup，标记为已处理即可
                _warmed = true;
                return;
            }

            ItemDatabase db = SingletonAsset<ItemDatabase>.Instance;
            if (db == null || db.Objects == null || db.Objects.Count == 0)
            {
                return;
            }

            if (FontFallbackSwapper.instance == null)
            {
                return;
            }

            // 先标记，失败也不重试（F5 时由 Initialize 兜底）
            _warmed = true;

            // CPU 预热：同步 Setup
            try
            {
                UiEnhancer.Setup(window);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: 预热 Setup 失败: " + ex);
                return;
            }
        }
    }
}
