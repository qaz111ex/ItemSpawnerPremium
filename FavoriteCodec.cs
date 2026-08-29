using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ItemSpawnerEnhancement
{
    /// <summary>
    /// 收藏集合的 JSON 编解码（纯逻辑，无 BepInEx / Unity 依赖）。
    ///
    /// 从 FavoriteStore 抽出：原实现的 Serialize/TryDeserialize 是 private static，
    /// 且 TryDeserialize 内部直接调 Plugin.Log（BepInEx），导致这段「用户数据不能丢」的关键逻辑
    /// 无法单元测试。这里只做纯粹的编解码并通过 out 参数把错误信息交回调用方记日志。
    ///
    /// 比较口径：集合一律 <see cref="StringComparer.OrdinalIgnoreCase"/>，与 ItemCatalog 的查表语义一致
    /// （prefab 名在本插件里全程按「忽略大小写」处理）。而序列化排序用 Ordinal —— 见 Serialize 注释。
    /// </summary>
    internal static class FavoriteCodec
    {
        /// <summary>损坏配置的修复值（空 JSON 数组）。</summary>
        public const string EmptyJson = "[]";

        /// <summary>新建一个与本编解码口径一致的空集合。</summary>
        public static HashSet<string> NewSet()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>基于已有集合创建副本（write-ahead 用：先改副本、写盘成功后再替换内存集合）。</summary>
        public static HashSet<string> CopySet(IEnumerable<string> source)
        {
            return new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 序列化为 JSON 数组。剔除空白项；排序用 Ordinal 使输出确定（配置文件 diff 稳定、便于人工核对）。
        /// 这里刻意不用 OrdinalIgnoreCase：它对仅大小写不同的两项排序结果不确定，反而削弱确定性。
        /// </summary>
        public static string Serialize(IEnumerable<string> names)
        {
            List<string> list = new List<string>();
            if (names != null)
            {
                foreach (string n in names)
                {
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        list.Add(n);
                    }
                }
            }
            list.Sort(StringComparer.Ordinal);
            return JsonConvert.SerializeObject(list);
        }

        /// <summary>
        /// 反序列化。返回 false 表示配置损坏，调用方应回写 <see cref="EmptyJson"/> 修复。
        /// 注意「合法 JSON 但不是数组」（如 "null"、空串）也判为损坏 —— 否则会得到 null 集合，
        /// 后续 IsFavorite 直接 NRE。
        /// </summary>
        /// <param name="error">损坏原因（供调用方记日志）；成功时为 null。</param>
        public static bool TryDeserialize(string serialized, out HashSet<string> set, out string error)
        {
            error = null;
            try
            {
                string[] arr = JsonConvert.DeserializeObject<string[]>(serialized);
                if (arr == null)
                {
                    set = null;
                    error = "收藏配置不是 JSON 数组";
                    return false;
                }
                set = NewSet();
                for (int i = 0; i < arr.Length; i++)
                {
                    string n = arr[i];
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        set.Add(n);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                set = null;
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 找出不在有效集合中的收藏名（脏数据），按 Ordinal 排序返回；无脏数据返回 null。
        /// **只报告不删除** —— 调用方传入的「全集」只是某一时刻 ItemDatabase 的快照，
        /// 物品模组可能在本插件之后加载，或通过 DatabaseAsset.AddRuntimeEntry /
        /// ItemDatabase.Add([ConsoleCommand]) 在运行时追加条目，据此删除会造成不可逆的用户数据丢失。
        /// </summary>
        public static List<string> FindStale(IEnumerable<string> favorites, IEnumerable<string> validNames)
        {
            if (favorites == null || validNames == null)
            {
                return null;
            }
            HashSet<string> valid = CopySet(validNames);
            if (valid.Count == 0)
            {
                return null; // 空集合视为「全集未知」，连报告都没有意义
            }
            List<string> stale = null;
            foreach (string name in favorites)
            {
                if (!valid.Contains(name))
                {
                    if (stale == null)
                    {
                        stale = new List<string>();
                    }
                    stale.Add(name);
                }
            }
            if (stale != null)
            {
                stale.Sort(StringComparer.Ordinal); // 日志顺序确定，便于跨次运行比对
            }
            return stale;
        }
    }
}
