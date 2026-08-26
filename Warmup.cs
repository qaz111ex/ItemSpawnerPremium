using System;
using System.Collections;
using UnityEngine;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 面板预热驱动器：常驻轮询，分两步预热。
    /// 1) CPU 预热（提前 Setup）：把 BuildCatalog（本地化/分类/拼音计算）与增量构建条目
    ///    Instantiate 提前到加载阶段。
    /// 2) 渲染 priming：加载屏幕显示期间短暂激活子 Canvas 渲染 1~2 帧，提前完成
    ///    GPU 重活（图标纹理上传 + TMP 字体图集栅格化 + Canvas 首次合批/mesh 构建），
    ///    消除首次 F5 打开面板时的卡顿。期间用 CanvasGroup.alpha=0 使其不可见
    ///    （alpha=0 仍会执行布局与渲染提交，GPU 预热有效），避免闪屏；
    ///    同时置 blocksRaycasts=false，防止不可见面板拦截点击。
    ///
    /// 常驻（不 Destroy）：<see cref="Update"/> 每 0.5 秒轮询一次；
    /// 窗口是 DontDestroyOnLoad 单例（全程只有一个实例），预热过一次后由 <see cref="_warmed"/> 标志跳过，
    /// 不会因切场景重建；<see cref="_primingRunning"/> 防止渲染 priming 协程并发。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _warmed;                                      // CPU 预热（Setup）已执行过（失败不重试，F5 兜底）
        private bool _primed;                                      // 渲染 priming 已成功完成（GPU 预热，失败下次 loading 重试）
        private bool _primingRunning;                              // 渲染 priming 协程运行中，防并发

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

            ItemSpawnerPremiumWindow window = Plugin.Window;
            if (window == null)
            {
                return; // 窗口尚未创建（GUIManager.Start 还没跑），等下一轮
            }

            // 1. CPU 预热（仅一次；失败不重试，F5 时由 Initialize 兜底）
            if (!_warmed)
            {
                ItemListView view = window.GetComponent<ItemListView>();
                if (view != null && view.Initialized)
                {
                    _warmed = true; // 已被别处（F5 兜底）Setup
                }
                else
                {
                    ItemDatabase db = SingletonAsset<ItemDatabase>.Instance;
                    if (db == null || db.Objects == null || db.Objects.Count == 0)
                    {
                        return;
                    }
                    if (FontFallbackSwapper.instance == null)
                    {
                        return;
                    }
                    try
                    {
                        UiEnhancer.Setup(window);
                        _warmed = true; // 成功后才标记，失败时保留重试机会（F5 兜底之外仍可自愈）
                    }
                    catch (Exception ex)
                    {
                        _warmed = true; // 抛异常说明环境异常，避免每 0.5 秒反复抛；由 F5（OnOpen→Setup）兜底
                        Plugin.Log.LogError("ItemSpawnerPremium: 预热 Setup 失败: " + ex);
                        return;
                    }
                }
            }

            // 2. 渲染 priming：在任一 loading 屏期间执行一次（_primed 成功后置位；失败/放弃则下次 loading 重试）
            if (!_primed && LoadingScreenHandler.loading)
            {
                _primingRunning = true;
                StartCoroutine(PrimingRoutine(window));
            }
        }

        private IEnumerator PrimingRoutine(ItemSpawnerPremiumWindow window)
        {
            // 等增量构建完成（Initialized 为 true 时条目才全部 Instantiate 完成）
            ItemListView view = window.GetComponent<ItemListView>();
            float deadline = Time.unscaledTime + 10f;
            while (view != null && !view.Initialized && Time.unscaledTime < deadline)
            {
                yield return null;
            }
            if (view == null || !view.Initialized)
            {
                _primingRunning = false;
                yield break;
            }

            // 激活前复检：加载屏若已结束则放弃本次 priming（配合 _primed 标志，下次 loading 重试）
            if (!LoadingScreenHandler.loading)
            {
                _primingRunning = false;
                yield break;
            }

            GameObject canvasGo = window.canvasObject;
            CanvasGroup cg = canvasGo.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                cg = canvasGo.AddComponent<CanvasGroup>();
            }
            float originalAlpha = cg.alpha;
            bool originalBlocks = cg.blocksRaycasts;
            cg.alpha = 0f;
            cg.blocksRaycasts = false;

            try
            {
                window.panel.SetActive(true);   // 激活子 Canvas（不影响窗口根/MenuWindow 状态）
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 渲染 priming 启动中断: " + ex.Message);
                try { window.panel.SetActive(false); } catch { }
                cg.alpha = originalAlpha;
                cg.blocksRaycasts = originalBlocks;
                _primingRunning = false;
                yield break;
            }

            yield return null;   // 渲染帧 → GPU 图标纹理上传 + TMP 图集栅格化
            yield return null;   // 再一帧，布局稳定

            try
            {
                if (!window.isOpen)
                {
                    window.panel.SetActive(false);  // 若 priming 期间玩家恰好 F5 打开窗口，则不硬隐藏
                }
                Canvas.ForceUpdateCanvases();
                // 只有完整收尾成功才算预热达成：若在此之前置位，收尾抛异常会留下"面板可见且 alpha 已恢复"的窗口。
                _primed = true;
                Plugin.Log.LogInfo("ItemSpawnerPremium: 渲染 priming 完成");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 渲染 priming 关闭中断: " + ex.Message);
                try { if (!window.isOpen) { window.panel.SetActive(false); } } catch { }
            }
            finally
            {
                cg.alpha = originalAlpha;
                cg.blocksRaycasts = originalBlocks;
                _primingRunning = false;
            }
        }
    }
}
