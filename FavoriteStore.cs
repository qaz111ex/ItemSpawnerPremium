using System;
using System.Collections.Generic;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 收藏集合管理 + Config 持久化。以 prefab 名（Item.gameObject.name）为 key，
    /// 用 JSON 数组存 ConfigEntry&lt;string&gt;。
    /// 纯粹的编解码与脏数据比对在 <see cref="FavoriteCodec"/>（零依赖、可单元测试）；
    /// 本类只负责与 BepInEx ConfigEntry 的交互、write-ahead 顺序与日志。
    ///
    /// 比较口径：集合一律 <see cref="StringComparer.OrdinalIgnoreCase"/>，
    /// 与 ItemCatalog.ItemTagMap / ExtraNameKeys / HiddenExact 及 IsHidden 的前缀/子串比较保持一致
    /// （prefab 名在本插件里全程按「忽略大小写」语义查表）。若用 Ordinal，用户手工编辑配置文件
    /// 出现大小写差异时，IsFavorite 会失配（心形与收藏筛选静默失效）。
    /// </summary>
    internal sealed class FavoriteStore
    {
        private readonly BepInEx.Configuration.ConfigEntry<string> _entry;
        private HashSet<string> _itemNames;

        /// <summary>上次报告过的脏数据清单（换行连接），用于抑制重复日志。仅内存态，不持久化。</summary>
        private string _lastStaleReport;

        public FavoriteStore(BepInEx.Configuration.ConfigEntry<string> entry)
        {
            _entry = entry;
            HashSet<string> names;
            string error;
            if (!FavoriteCodec.TryDeserialize(entry.Value, out names, out error))
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 收藏配置无效，已重置: " + error);
                _itemNames = FavoriteCodec.NewSet();
                try
                {
                    _entry.Value = FavoriteCodec.EmptyJson;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 修复收藏配置失败: " + ex.Message);
                }
            }
            else
            {
                _itemNames = names;
            }
        }

        public bool IsFavorite(string itemName)
        {
            return _itemNames.Contains(itemName);
        }

        public bool TryToggle(string itemName, out bool isFavorite)
        {
            HashSet<string> updated = FavoriteCodec.CopySet(_itemNames);
            isFavorite = !updated.Remove(itemName);
            if (isFavorite)
            {
                updated.Add(itemName);
            }
            string serialized = FavoriteCodec.Serialize(updated);
            try
            {
                // write-ahead：先写盘成功再替换内存集合，避免写盘失败时内存已改、磁盘未改的不一致
                // （那会表现为「取消收藏后重启又复活」）。
                _entry.Value = serialized;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ItemSpawnerPremium: 保存收藏失败: " + ex.Message);
                isFavorite = _itemNames.Contains(itemName);
                return false;
            }
            _itemNames = updated;
            return true;
        }

        /// <summary>
        /// 报告不在有效物品集合中的收藏名（脏数据）。**只记日志，不删除、不写盘。**
        ///
        /// 为什么不再自动删除：调用方传入的"全集"只是本插件 BuildCatalog 时刻 ItemDatabase 的快照，
        /// 无法证明它是真正的全集 —— 物品模组可能在本插件之后加载，或通过
        /// DatabaseAsset.AddRuntimeEntry / ItemDatabase.Add([ConsoleCommand]) 在运行时追加条目。
        /// 旧实现只防了 valid.Count==0，「部分全集」照样删，且因 Config.SaveOnConfigSet=true 会
        /// 立即同步写盘，用户对模组物品的收藏被静默、不可恢复地删除。
        ///
        /// 权衡：脏 key 的唯一代价是配置里多几十字节，且 IsFavorite 查不到它时没有任何副作用
        /// （不显示、不参与筛选）；而误删是不可逆的用户数据丢失。因此宁可留脏数据，只在日志里
        /// 提示玩家/开发者，让人工决定是否清理。本方法完全不触碰 _entry.Value 与 _itemNames，
        /// 天然没有一致性风险。方法名保留 Prune 是为了不改调用点签名。
        /// </summary>
        public void Prune(IEnumerable<string> validNames)
        {
            if (validNames == null || _itemNames.Count == 0)
            {
                return;
            }
            List<string> stale = FavoriteCodec.FindStale(_itemNames, validNames);
            if (stale == null || stale.Count == 0)
            {
                return;
            }
            string signature = string.Join("\n", stale.ToArray());
            if (signature == _lastStaleReport)
            {
                return; // 同一批脏数据只报一次：Prune 会在每次 BuildCatalog（F5、切隐藏开关）时重跑
            }
            _lastStaleReport = signature;
            // 用 LogWarning 而非 LogInfo：这是"配置里有对不上物品的收藏"的异常状态，
            // 玩家看到后可自行判断是模组未加载（正常，忽略即可）还是物品已被移除（可手工清理）。
            Plugin.Log.LogWarning("ItemSpawnerPremium: 收藏中有 " + stale.Count
                + " 项未在当前物品数据库中找到（可能是未加载的模组物品，已保留不删除）: "
                + string.Join(", ", stale.ToArray()));
        }
    }
}
