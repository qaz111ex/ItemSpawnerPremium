using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Zorro.Core;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 面板预热驱动器：在游戏加载阶段主动提前执行一次 UiEnhancer.Setup（CPU 预热），
    /// 并短暂激活 panel 真实渲染 1~2 帧再隐藏（渲染 priming），
    /// 把 BuildCatalog（本地化/分类/拼音计算）、Rebuild（Instantiate 条目克隆 + 纹理上传 + 字体加载），
    /// 以及首次 Show 激活 Canvas 后才发生的 GPU 重活（约 198 个图标纹理上传 + 上百个中文/日韩字形的
    /// TMP 动态 SDF 字体图集栅格化 + Canvas 首次合批/mesh 构建）全部提前到加载阶段，
    /// 消除首次 F5 打开面板时的明显卡顿。
    /// 满足全部就绪条件后执行一次，随后销毁自身停止轮询（避免每帧 FindObjectOfType 浪费）。
    /// </summary>
    internal sealed class Warmup : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _started;

        private void Update()
        {
            // 已启动预热流程则不再重复检查
            if (_started)
            {
                return;
            }

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

            _started = true;
            StartCoroutine(WarmupRoutine(window));
        }

        private IEnumerator WarmupRoutine(ItemSpawner.ItemSpawnerWindow window)
        {
            // (1) CPU 预热：Setup
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                UiEnhancer.Setup(window);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPlus: 预热 Setup 失败: " + ex);
                Destroy(gameObject);
                yield break;
            }
            sw.Stop();
            Plugin.Log.LogInfo("ItemSpawnerPlus: 预热 Setup 完成, 耗时 " + sw.ElapsedMilliseconds + "ms");

            // (2) 渲染 priming：短暂激活 panel 真实渲染 1~2 帧再隐藏
            // 仅当角色尚未生成（加载阶段）时执行，避免游戏开始后可见闪烁
            if (Character.localCharacter == null)
            {
                Stopwatch sw2 = Stopwatch.StartNew();
                window.panel.SetActive(true);
                Canvas.ForceUpdateCanvases(); // 同步构建 mesh + TMP 图集
                yield return null;            // 本帧末尾真实渲染 → icon 纹理 GPU 上传
                yield return null;            // 再一帧，让 LayoutGroup 稳定
                window.panel.SetActive(false);
                Canvas.ForceUpdateCanvases();
                sw2.Stop();
                Plugin.Log.LogInfo("ItemSpawnerPlus: 渲染 priming 完成, 耗时 " + sw2.ElapsedMilliseconds + "ms");
            }
            else
            {
                Plugin.Log.LogInfo("ItemSpawnerPlus: 跳过渲染 priming（角色已生成，避免闪烁）");
            }

            Destroy(gameObject);
        }
    }
}
