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
    ///    消除首次 F5 打开面板时的卡顿。期间临时把 Canvas sortingOrder 降到极低值，
    ///    确保渲染发生在加载屏幕之下、不闪屏（当前架构 panel 是独立子 Canvas，不影响窗口根）。
    ///
    /// 常驻（不 Destroy）：<see cref="Update"/> 每 0.5 秒轮询一次；
    /// 窗口是 DontDestroyOnLoad 单例（全程只有一个实例），预热过一次后由 <see cref="_warmed"/> 标志跳过，
    /// 不会因切场景重建；<see cref="_primingRunning"/> 防止渲染 priming 协程并发。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _warmed;                                      // 已预热过（窗口是 DontDestroyOnLoad 单例，预热一次即可）
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

            // 渲染 priming：仅在加载屏幕显示期间执行（此时面板被加载屏幕遮挡，激活不闪烁）
            if (LoadingScreenHandler.loading)
            {
                _primingRunning = true;
                StartCoroutine(PrimingRoutine(window));
            }
        }

        private IEnumerator PrimingRoutine(ItemSpawnerPlusWindow window)
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

            // 临时把 Canvas sortingOrder 降到极低，确保面板渲染在加载屏幕之下、不闪屏
            Canvas canvas = window.canvasObject.GetComponent<Canvas>();
            int originalOrder = (canvas != null) ? canvas.sortingOrder : 250;
            if (canvas != null)
            {
                canvas.sortingOrder = -9999;
            }

            try
            {
                window.panel.SetActive(true);   // 激活子 Canvas（不影响窗口根/MenuWindow 状态）
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 渲染 priming 启动中断: " + ex.Message);
                try { window.panel.SetActive(false); } catch { }
                if (canvas != null) { canvas.sortingOrder = originalOrder; }
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
                Plugin.Log.LogInfo("ItemSpawnerPlus: 渲染 priming 完成");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 渲染 priming 关闭中断: " + ex.Message);
            }
            finally
            {
                if (canvas != null)
                {
                    canvas.sortingOrder = originalOrder;  // 恢复原 sortingOrder
                }
                _primingRunning = false;
            }
        }
    }
}
