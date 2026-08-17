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
    /// 窗口是 DontDestroyOnLoad 单例（全程只有一个实例），预热过一次后由 <see cref="_warmed"/> 标志跳过，
    /// 不会因切场景重建；<see cref="_primingRunning"/> 防止渲染 priming 协程并发。
    /// 渲染 priming 门槛为 <see cref="LoadingScreenHandler.loading"/>（public static bool，覆盖
    /// "等角色生成 + 3 秒 extraYieldTime + 加载屏幕淡出"全程），此期间 panel 被加载屏幕遮挡、激活不闪烁。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _primingRunning;                              // 渲染 priming 协程运行中，防并发
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
            if (_primingRunning)
            {
                return; // 上次协程未结束
            }

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

        private IEnumerator PrimingRoutine(ItemSpawnerPlusWindow window)
        {
            try
            {
                window.panel.SetActive(true);
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 渲染 priming 启动中断: " + ex.Message);
                // 若 SetActive(true) 已成功而 ForceUpdateCanvases 抛异常，恢复 panel 隐藏，避免面板残留 active
                try { window.panel.SetActive(false); } catch { }
                _primingRunning = false;
                yield break;
            }

            yield return null;   // 渲染帧 → GPU 纹理上传 + TMP 图集
            yield return null;   // 再一帧，LayoutGroup 稳定

            try
            {
                // 若 priming 期间玩家恰好按 F5 打开了窗口（isOpen==true），不要硬隐藏，
                // 避免"逻辑已打开但面板不可见"的错位。
                if (!window.isOpen)
                {
                    window.panel.SetActive(false);
                }
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
