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
    /// 除预热外还兼任「主线程消费者」：Config 的 SettingChanged 在调用方线程同步派发，
    /// 样式热重载需要的 Unity API 全是主线程专属，故由本组件的 Update 消费
    /// <see cref="Plugin.StyleChangeRequested"/> 标志。
    ///
    /// 常驻（不 Destroy）：<see cref="Update"/> 每 0.5 秒轮询一次；
    /// 窗口是 DontDestroyOnLoad 单例（全程只有一个实例），预热过一次后由 <see cref="_warmed"/> 标志跳过，
    /// 不会因切场景重建；<see cref="_primingRunning"/> 防止渲染 priming 协程并发。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        /// <summary>CPU 预热（Setup）最大尝试次数：失败后允许有限次重试自愈，超出则彻底放弃（F5 兜底），避免每 0.5 秒无限刷日志。</summary>
        private const int MaxWarmupAttempts = 5;

        private float _nextCheckTime;
        private bool _warmed;                                      // CPU 预热（Setup）已完成或已放弃重试
        private int _warmupAttempts;                               // 已尝试 Setup 的次数（达到 MaxWarmupAttempts 后放弃）
        private bool _primed;                                      // 渲染 priming 已成功完成（GPU 预热，失败下次 loading 重试）
        private bool _primingRunning;                              // 渲染 priming 协程运行中，防并发

        private void Update()
        {
            // 样式热重载必须立即响应（玩家在配置界面切样式后应当当帧看到变化），
            // 因此放在 0.5 秒节流判断之前。
            ConsumeStyleChangeRequest();

            // 每 0.5 秒检查一次（unscaledTime 不受时间缩放/暂停影响）
            if (Time.unscaledTime < _nextCheckTime)
            {
                return;
            }
            _nextCheckTime = Time.unscaledTime + 0.5f;
            TryStartWarmup();
        }

        /// <summary>
        /// 在主线程消费样式热重载请求（标志由 Plugin 的 ConfigEntry.SettingChanged 回调置位）。
        /// 先清标志再执行：OnStyleChanged 抛异常时不重试。理由是它内部会先 Destroy 旧 Sprite 再重新烘焙，
        /// 中途失败后状态已被部分改写，反复重跑只会每帧重复抛同一个异常刷屏，而无法自愈；
        /// 真正的恢复手段是玩家再改一次配置（重新置位标志）。
        /// </summary>
        private void ConsumeStyleChangeRequest()
        {
            if (!Plugin.StyleChangeRequested)
            {
                return;
            }
            Plugin.StyleChangeRequested = false;
            try
            {
                UiEnhancer.OnStyleChanged();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 样式热重载失败: " + ex);
            }
        }

        private void TryStartWarmup()
        {
            ItemSpawnerPremiumWindow window = Plugin.Window;
            if (window == null)
            {
                return; // 窗口尚未创建（GUIManager.Start 还没跑），等下一轮
            }

            // 加载屏期间强制收起面板：加载中 Spawn 会被拒绝（Photon 消息队列可能已关闭、角色正在重建），
            // 面板留在屏幕上只会让玩家点了没反应；游戏自身也在换场景/结算时调 MenuWindow.CloseAllWindows()。
            // 放在最前面（先于 _primingRunning 守卫）：priming 协程运行期间玩家依然可能按 F5 打开面板。
            if (LoadingScreenHandler.loading && window.isOpen)
            {
                ItemSpawnerPremiumWindow.CloseWindow(window);
            }

            if (_primingRunning)
            {
                return; // 上次协程未结束
            }

            // 1. CPU 预热（有限次重试；全部失败后交给 F5 兜底）
            if (!_warmed)
            {
                ItemListView view = window.GetComponent<ItemListView>();
                if (view != null && (view.Initialized || view.Building))
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
                    _warmupAttempts++;
                    bool ok = false;
                    try
                    {
                        UiEnhancer.Setup(window);
                        // UiEnhancer.Setup 返回 void 且内部吞掉了 _view.Init 的异常，"抛没抛异常"无法区分成功与失败。
                        // 改为按结果判定：Setup 成功的标志是窗口上挂了 ItemListView 且
                        // Initialized（目录已建完）或 Building（增量构建协程已启动，也算成功启动）。
                        ItemListView built = window.GetComponent<ItemListView>();
                        ok = built != null && (built.Initialized || built.Building);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError("ItemSpawnerPremium: 预热 Setup 抛异常: " + ex);
                    }
                    if (ok)
                    {
                        _warmed = true;
                    }
                    else if (_warmupAttempts >= MaxWarmupAttempts)
                    {
                        // 必须有次数上限：否则每 0.5 秒重试一次会无限刷日志（Setup 内部失败也会各记一条 Error）。
                        _warmed = true;
                        Plugin.Log.LogError("ItemSpawnerPremium: 预热 Setup 连续 " + MaxWarmupAttempts
                            + " 次未成功，放弃预热（打开面板时会再次尝试构建）");
                    }
                    if (!_warmed)
                    {
                        return; // 本轮未成功，下一轮重试；不进入渲染 priming（条目还没建出来）
                    }
                }
            }

            // 2. 渲染 priming：在任一 loading 屏期间执行一次（_primed 成功后置位；失败/放弃则下次 loading 重试）
            // 追加 !window.isOpen：priming 会强制 alpha=0 + blocksRaycasts=false 两帧，
            // 若玩家已在加载屏期间按 F5 打开了面板，那两帧面板会变成透明且点不动。
            if (!_primed && LoadingScreenHandler.loading && !window.isOpen)
            {
                _primingRunning = true;
                StartCoroutine(PrimingRoutine(window));
            }
        }

        private IEnumerator PrimingRoutine(ItemSpawnerPremiumWindow window)
        {
            // 整个协程体包一层 try/finally，由 finally 统一复位 _primingRunning：
            // 原实现把 _primingRunning = false 散落在各个 return 点，任何未被保护的语句
            // （GetComponent / AddComponent / 属性赋值）抛异常都会终止协程并把标志永久卡在 true，
            // 之后 TryStartWarmup 每次都立刻 return，CPU/GPU 预热彻底失效。
            // 注意 C# 7.3 迭代器不允许 yield return 出现在带 catch 的 try 块内，但允许在 try-finally 内，
            // 故外层只能是 try-finally，含风险且不含 yield 的语句段各自再包内层 try/catch。
            try
            {
                // 等增量构建完成（Initialized 为 true 时条目才全部 Instantiate 完成）
                ItemListView view = null;
                try
                {
                    view = window.GetComponent<ItemListView>();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 渲染 priming 获取列表视图失败: " + ex.Message);
                    yield break;
                }
                float deadline = Time.unscaledTime + 10f;
                while (view != null && !view.Initialized && Time.unscaledTime < deadline)
                {
                    yield return null;
                }
                if (view == null || !view.Initialized)
                {
                    yield break;
                }

                // 激活前复检：加载屏若已结束则放弃本次 priming（配合 _primed 标志，下次 loading 重试）；
                // 同时复检 isOpen——上面的等待循环可能持续多帧，玩家有充足时间按 F5 打开面板，
                // 此时继续 priming 会把玩家可见的面板压成透明且不可点。
                if (!LoadingScreenHandler.loading || window.isOpen)
                {
                    yield break;
                }

                CanvasGroup cg = null;
                bool createdCanvasGroup = false;
                float originalAlpha = 1f;
                bool originalBlocks = true;
                try
                {
                    GameObject canvasGo = window.canvasObject;
                    cg = canvasGo.GetComponent<CanvasGroup>();
                    if (cg == null)
                    {
                        cg = canvasGo.AddComponent<CanvasGroup>();
                        createdCanvasGroup = true; // 由本次 priming 新建 → 收尾时销毁，不给窗口留下多余组件
                    }
                    originalAlpha = cg.alpha;
                    originalBlocks = cg.blocksRaycasts;
                    cg.alpha = 0f;
                    cg.blocksRaycasts = false;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 渲染 priming 准备 CanvasGroup 失败: " + ex.Message);
                    RestoreCanvasGroup(cg, originalAlpha, originalBlocks, createdCanvasGroup);
                    yield break;
                }

                try
                {
                    window.panel.SetActive(true);   // 激活子 Canvas（不影响窗口根/MenuWindow 状态）
                    Canvas.ForceUpdateCanvases();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 渲染 priming 启动中断: " + ex.Message);
                    try { window.panel.SetActive(false); } catch { }
                    RestoreCanvasGroup(cg, originalAlpha, originalBlocks, createdCanvasGroup);
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
                    RestoreCanvasGroup(cg, originalAlpha, originalBlocks, createdCanvasGroup);
                }
            }
            finally
            {
                _primingRunning = false;
            }
        }

        /// <summary>
        /// 恢复 CanvasGroup 原始状态；若该组件是本次 priming 新建的则一并销毁，避免每次 priming 都往窗口上留组件。
        /// 先恢复属性再 Destroy：Destroy 延迟到帧末生效，销毁前的这一帧仍会参与渲染/射线检测，
        /// 属性先恢复更安全（且顺序上不依赖 Destroy 的延迟语义）。
        /// </summary>
        private static void RestoreCanvasGroup(CanvasGroup cg, float alpha, bool blocksRaycasts, bool createdByPriming)
        {
            if (cg == null)
            {
                return;
            }
            try
            {
                cg.alpha = alpha;
                cg.blocksRaycasts = blocksRaycasts;
                if (createdByPriming)
                {
                    UnityEngine.Object.Destroy(cg);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 渲染 priming 恢复 CanvasGroup 失败: " + ex.Message);
            }
        }
    }
}
