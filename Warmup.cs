using System;
using System.Collections;
using UnityEngine;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 面板预热驱动器：常驻轮询 + 跨场景复用预热 + loading 门槛。
    /// 主动提前执行一次 UiEnhancer.Setup（CPU 预热），并在加载屏幕显示期间短暂激活 panel
    /// 真实渲染 1~2 帧再隐藏（渲染 priming），把 BuildCatalog（本地化/分类/拼音计算）、
    /// Rebuild（Instantiate 条目克隆 + 纹理上传 + 字体加载），以及首次 Show 激活 Canvas 后才发生的
    /// GPU 重活（图标纹理上传 + TMP 动态 SDF 字体图集栅格化 + Canvas 首次合批/mesh 构建）
    /// 全部提前到加载阶段，消除首次 F5 打开面板时的明显卡顿。
    ///
    /// 常驻（不 Destroy）：<see cref="Update"/> 每 0.5 秒轮询一次；
    /// <see cref="_lastWarmedWindow"/> 记录已处理（含已 Setup / 已失败）的窗口实例，
    /// 切场景后 <see cref="UnityEngine.Object.FindObjectOfType{T}"/> 返回新实例（Unity == 比较不同实例为 false）
    /// → 自动触发新窗口的预热；<see cref="_primingRunning"/> 防止渲染 priming 协程并发。
    /// 渲染 priming 门槛为 <see cref="LoadingScreenHandler.loading"/>（public static bool，覆盖
    /// "等角色生成 + 3 秒 extraYieldTime + 加载屏幕淡出"全程），此期间 panel 被加载屏幕遮挡、激活不闪烁。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _primingRunning;                              // 渲染 priming 协程运行中，防并发
        private ItemSpawner.ItemSpawnerWindow _lastWarmedWindow;   // 已处理过的窗口（跨场景去重）

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
            if (_primingRunning)
            {
                return; // 上次协程未结束
            }

#pragma warning disable CS0618 // FindObjectOfType 在本游戏运行时仍受支持，且为任务指定 API
            ItemSpawner.ItemSpawnerWindow window = UnityEngine.Object.FindObjectOfType<ItemSpawner.ItemSpawnerWindow>();
#pragma warning restore CS0618
            if (window == null)
            {
                return; // 场景切换中，等新窗口
            }

            if (window == _lastWarmedWindow)
            {
                return; // 该窗口已处理（含已 Setup/已失败），跨场景后新实例 != 旧实例会自动触发
            }

            ItemListView view = window.GetComponent<ItemListView>();
            if (view != null && view.Initialized)
            {
                // 已被别处（F5 兜底）Setup，标记为已处理即可
                _lastWarmedWindow = window;
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

            // 先标记，失败也不重试同一窗口（F5 时由 Initialize 兜底）
            _lastWarmedWindow = window;

            // (1) CPU 预热：同步 Setup
            try
            {
                UiEnhancer.Setup(window);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: 预热 Setup 失败: " + ex);
                return;
            }

            // (2) 渲染 priming：仅在加载屏幕显示期间（loading==true）执行
            if (LoadingScreenHandler.loading)
            {
                _primingRunning = true;
                StartCoroutine(PrimingRoutine(window));
            }
        }

        private IEnumerator PrimingRoutine(ItemSpawner.ItemSpawnerWindow window)
        {
            try
            {
                window.panel.SetActive(true);
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 渲染 priming 启动中断: " + ex.Message);
                _primingRunning = false;
                yield break;
            }

            yield return null;   // 渲染帧 → GPU 纹理上传 + TMP 图集
            yield return null;   // 再一帧，LayoutGroup 稳定

            try
            {
                window.panel.SetActive(false);
                Canvas.ForceUpdateCanvases();
                Plugin.Log.LogInfo("ItemSpawnerPlus: 渲染 priming 完成");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 渲染 priming 关闭中断: " + ex.Message);
            }
            finally
            {
                _primingRunning = false;
            }
        }
    }
}
