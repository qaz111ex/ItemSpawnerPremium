using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 收藏集合管理 + Config 持久化。以 prefab 名（Item.gameObject.name）为 key，
    /// 用 JSON 数组存 ConfigEntry&lt;string&gt;。
    /// </summary>
    internal sealed class FavoriteStore
    {
        private readonly BepInEx.Configuration.ConfigEntry<string> _entry;
        private HashSet<string> _itemNames;

        public FavoriteStore(BepInEx.Configuration.ConfigEntry<string> entry)
        {
            _entry = entry;
            HashSet<string> names;
            if (!TryDeserialize(entry.Value, out names))
            {
                _itemNames = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    _entry.Value = "[]";
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
            var updated = new HashSet<string>(_itemNames, StringComparer.Ordinal);
            isFavorite = !updated.Remove(itemName);
            if (isFavorite)
            {
                updated.Add(itemName);
            }
            string serialized = Serialize(updated);
            try
            {
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

        /// <summary>移除不在有效物品集合中的收藏名（脏数据清理），有变化时持久化。</summary>
        public void Prune(IEnumerable<string> validNames)
        {
            if (validNames == null)
            {
                return;
            }
            var valid = new HashSet<string>(validNames, StringComparer.Ordinal);
            int before = _itemNames.Count;
            _itemNames.RemoveWhere(n => !valid.Contains(n));
            if (_itemNames.Count != before)
            {
                try
                {
                    _entry.Value = Serialize(_itemNames);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("ItemSpawnerPremium: 清理收藏配置失败: " + ex.Message);
                }
            }
        }

        private static string Serialize(IEnumerable<string> names)
        {
            var list = new List<string>();
            foreach (string n in names)
            {
                if (!string.IsNullOrWhiteSpace(n))
                {
                    list.Add(n);
                }
            }
            list.Sort(StringComparer.Ordinal);
            return JsonConvert.SerializeObject(list);
        }

        private static bool TryDeserialize(string serialized, out HashSet<string> set)
        {
            try
            {
                string[] arr = JsonConvert.DeserializeObject<string[]>(serialized);
                set = new HashSet<string>(StringComparer.Ordinal);
                if (arr != null)
                {
                    foreach (string n in arr)
                    {
                        if (!string.IsNullOrWhiteSpace(n))
                        {
                            set.Add(n);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPremium: 收藏配置无效，已忽略: " + ex.Message);
                set = null;
                return false;
            }
        }
    }
}
