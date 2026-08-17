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
            _itemNames = Deserialize(entry.Value);
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
                Plugin.Log.LogError("ItemSpawnerPlus: 保存收藏失败: " + ex.Message);
                isFavorite = _itemNames.Contains(itemName);
                return false;
            }
            _itemNames = updated;
            return true;
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

        private static HashSet<string> Deserialize(string serialized)
        {
            try
            {
                string[] arr = JsonConvert.DeserializeObject<string[]>(serialized);
                var set = new HashSet<string>(StringComparer.Ordinal);
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
                return set;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ItemSpawnerPlus: 收藏配置无效，已忽略: " + ex.Message);
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }
    }
}
