#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ItemCatalog.cs 不变量校验（发行前置门禁）。

对照 item_truth.json（游戏资源真值，213 个物品 prefab）校验三条不变量：

  1. 隐藏 / 可见的切分符合预期（默认 61 / 152）。切分规则复刻
     ItemCatalogMethods.cs 的 IsHidden：HiddenExact 精确匹配、HiddenPrefixes 前缀、
     HiddenSubstrings 子串，全部大小写不敏感。
  2. ItemTagMap 的键集合与「可见集合」精确一一对应：
     - missing：可见但表里没有（运行时会走兜底推导分类，分类质量不可控）
     - extra  ：表里有但被隐藏规则挡掉（表项永远用不到）
     - dead   ：表里有但游戏里根本没有这个 prefab（历史遗留死键）
     三者都必须为 0。
  3. ItemTagMap 无重复键、无「无分类」值（ItemCategory.None / (ItemCategory)0 /
     default(ItemCategory) / 裸 0 —— 它们在 C# 里全是同一个值，只写一种等于给
     绕过门禁留了后门）。
  4. 被**子串**隐藏规则命中的物品，必须有同 itemName 的可见物品可替代。
     子串规则按名字猜"没用"，名字会骗人：Clusterberry_UNUSED 的 itemName 是
     Green Clusterberry（中文"青葚莓"），是真实的莓，游戏里真的会刷。
     HiddenExact / HiddenPrefixes 是显式决定，不受此限。

另外输出一段 INFO 级的「隐藏规则贡献度」分析（不参与成败判定，见 report_rule_coverage）。

为什么需要它：ItemCatalog.cs 顶部仍带 auto-generated 头，但表内容早已经过人工语义
修订，与 scripts/gen_category_multi.py 的输出存在大量差异。任何人重跑生成脚本都会
把人工结论覆盖回去。本脚本作为 pack.ps1 的前置校验，让这类回退在发布前直接失败。

用法：
    python verify_catalog.py [--catalog ItemCatalog.cs] [--truth <item_truth.json>]
                             [--expect-hidden 61] [--expect-visible 152]
真值文件路径优先级：--truth > 环境变量 ITEMSPAWNER_TRUTH_JSON > 脚本内默认值。
（真值文件不在 git 仓库内，换机器时用环境变量比改脚本更不容易污染 diff。）
退出码：0 = 全部通过；1 = 存在违规；2 = 输入文件缺失/解析失败。
"""

import argparse
import json
import os
import re
import sys
from typing import NoReturn

DEFAULT_TRUTH = r"D:\zhuanban\youhua\item_truth.json"
TRUTH_ENV_VAR = "ITEMSPAWNER_TRUTH_JSON"

# ItemTagMap 里表示「没有分类」的所有等价写法。ItemCategory.None、(ItemCategory)0、
# default(ItemCategory) 与裸 0 编译后完全相同，只认第一种的话，把值改成后三种就能
# 静默塞进一条无分类表项（实测过：旧版脚本对 (ItemCategory)0 返回 exit=0）。
NONE_VALUE_RE = re.compile(
    r"\bItemCategory\s*\.\s*None\b"
    r"|\(\s*ItemCategory\s*\)\s*0\b"
    r"|\bdefault\s*\(\s*ItemCategory\s*\)"
    r"|\bdefault\b"
    r"|^\s*0\s*$"
)


def fail(msg, code=2):
    # type: (str, int) -> NoReturn
    print("[FAIL] " + msg)
    sys.exit(code)


def read_text(path):
    # utf-8-sig 对「有 BOM」和「无 BOM」两种 UTF-8 都能正确读取（有 BOM 时吃掉它，
    # 无 BOM 时行为等同 utf-8）。ItemCatalog.cs 的 BOM 状态历史上变动过，别写死 utf-8。
    with open(path, "r", encoding="utf-8-sig") as f:
        return f.read()


def strip_comments(text):
    """去掉 C# 的 // 行注释与 /* */ 块注释，保留字符串/字符字面量原样。

    为什么必须先保护字面量再去注释：prefab 名理论上可以含 "//"（资源路径风格的名字），
    朴素的 re.sub(r"//.*") 会把这样一条表项的后半截连同 '}' 一起吃掉，进而让解析
    静默漏掉条目。反之，不去注释同样危险 —— 把一条真实表项整行注释掉曾经可以通过
    门禁（parse_tag_map 不去注释，而 parse_string_set 去注释，两者标准不一致）。

    已知限制：不处理 C# 11 的原始字符串字面量（三引号形式），本代码库不用。
    """
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        # 逐字复制字符串字面量（含 @"" 逐字字符串与 $"" 内插字符串的外层引号）
        if c == '"':
            verbatim = i > 0 and text[i - 1] == "@"
            out.append(c)
            i += 1
            while i < n:
                ch = text[i]
                if verbatim:
                    if ch == '"':
                        if i + 1 < n and text[i + 1] == '"':  # "" 是转义的引号
                            out.append('""')
                            i += 2
                            continue
                        out.append(ch)
                        i += 1
                        break
                    out.append(ch)
                    i += 1
                else:
                    if ch == "\\" and i + 1 < n:
                        out.append(text[i:i + 2])
                        i += 2
                        continue
                    out.append(ch)
                    i += 1
                    if ch == '"':
                        break
                    if ch == "\n":  # 非逐字字符串不能跨行，遇换行按未闭合处理
                        break
            continue
        if c == "'":
            out.append(c)
            i += 1
            while i < n:
                ch = text[i]
                if ch == "\\" and i + 1 < n:
                    out.append(text[i:i + 2])
                    i += 2
                    continue
                out.append(ch)
                i += 1
                if ch == "'" or ch == "\n":
                    break
            continue
        if c == "/" and i + 1 < n:
            nxt = text[i + 1]
            if nxt == "/":
                j = text.find("\n", i)
                i = n if j < 0 else j  # 保留换行，行号/结构不错位
                continue
            if nxt == "*":
                j = text.find("*/", i + 2)
                # 用换行替换块注释，避免把上下两行粘成一行（会造出假的 { "x", y } 组合）
                chunk = text[i:(n if j < 0 else j + 2)]
                out.append("\n" * chunk.count("\n"))
                i = n if j < 0 else j + 2
                continue
        out.append(c)
        i += 1
    return "".join(out)


def slice_block(src, header_pattern, what):
    """截取 `header ... {  <body>  };` 的 body 部分。"""
    m = re.search(header_pattern, src)
    if not m:
        fail("无法在 ItemCatalog.cs 中定位 %s 的声明（文件结构已变？）" % what)
    start = src.index("{", m.end() - 1)
    depth = 0
    for i in range(start, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[start + 1:i]
    fail("%s 的初始化块括号不闭合" % what)


def parse_tag_map(src):
    body = slice_block(src, r"ItemTagMap\s*=\s*new\s+Dictionary<\s*string\s*,\s*ItemCategory\s*>[^{]*", "ItemTagMap")
    entries = re.findall(r'\{\s*"([^"]+)"\s*,\s*([^}]+?)\s*\}', body)
    return [(k, v.strip()) for k, v in entries]


def parse_string_set(src, field, what):
    body = slice_block(src, field + r"\s*=\s*new\s+(?:HashSet<\s*string\s*>|string\s*\[\s*\])[^{]*", what)
    return re.findall(r'"([^"]*)"', body)


def is_hidden(prefab, exact_lower, prefixes_lower, substrings_lower):
    """复刻 ItemCatalogMethods.IsHidden（OrdinalIgnoreCase）。"""
    if not prefab:
        return True
    low = prefab.lower()
    if low in exact_lower:
        return True
    for p in prefixes_lower:
        if low.startswith(p):
            return True
    for s in substrings_lower:
        if s in low:
            return True
    return False


def count_hidden(prefabs, exact_lower, prefixes_lower, substrings_lower):
    return sum(1 for p in prefabs
               if is_hidden(p, exact_lower, prefixes_lower, substrings_lower))


def hidden_by_substring_only(prefab, exact_lower, prefixes_lower, substrings_lower):
    """是否「只因为子串规则而被隐藏」（未被显式 HiddenExact、也未被前缀规则命中）。

    为什么要单独区分这一类：HiddenExact 与 HiddenPrefixes 是**显式决定**（逐个点名，或按
    C_* / GuidebookPage* 这种结构性前缀），而 HiddenSubstrings 是**按名字猜**"这物品没用"。
    名字会骗人，猜就会猜错。
    """
    if not prefab:
        return False
    low = prefab.lower()
    if low in exact_lower:
        return False
    for p in prefixes_lower:
        if low.startswith(p):
            return False
    return any(s in low for s in substrings_lower)


def find_substring_misfires(truth, map_keys, exact_lower, prefixes_lower, substrings_lower):
    """找出「被子串规则误伤」的物品：被隐藏，且没有任何同名可见物品可替代。

    真实案例（2.3.1 修的）：`Clusterberry_UNUSED` 的 itemName 是 `Green Clusterberry`
    （中文"青葚莓"），tag=Berry、带完整食用组件、在 ItemDatabase.Objects 里、游戏里真的会刷，
    只因为 prefab 名带 "_UNUSED" 就被隐藏 —— 玩家在游戏里见过的青葚莓生成不出来。

    判据用 ItemTagMap 中**真正可见**的键作为「可替代品」集合：如果同 itemName 的另一个
    prefab 在表里且没被隐藏，那玩家仍然能拿到这个物品（隐藏的只是重复品，网格里也不会
    出现两条一样的条目），这种隐藏是合理的。反之则这个物品从目录里彻底消失。

    注意必须排除「表里但已被隐藏规则挡掉」的键（即 extra 违规项）：那种键自己都进不了
    目录，不能拿来当别人的替代品 —— 否则这条检查会自我抵消（把 _UNUSED 加回去时，
    表里那条 Clusterberry_UNUSED 会给它自己当替代品，检查就永远不报）。
    """
    name_of = {}
    for it in truth:
        p = it.get("prefab")
        if p:
            name_of[p] = it.get("itemName", "")
    spawnable_names = set()
    for k in map_keys:
        if is_hidden(k, exact_lower, prefixes_lower, substrings_lower):
            continue
        spawnable_names.add(name_of.get(k, ""))

    misfires = []
    for p, nm in name_of.items():
        if not hidden_by_substring_only(p, exact_lower, prefixes_lower, substrings_lower):
            continue
        if nm not in spawnable_names:
            misfires.append((p, nm))
    return sorted(misfires)


def report_rule_coverage(prefabs, exact, prefixes, substrings, baseline_hidden):
    """逐条把隐藏规则拿掉，看隐藏数是否变化，以此判断这条规则有没有独占贡献。

    为什么只打 INFO 不失败：前瞻性防御规则是合法的（"_TEMP" 在当前 213 项真值里 0
    命中，但保留它能挡住日后新增的临时资源）。而「冗余」信号本身很有价值 ——
    HiddenExact 里被前缀/子串完全覆盖的项，一度让前缀/子串规则可以被整条删掉而
    不改变 60/153 切分，等于让切分数断言对这些规则失效。清完冗余项之后这里应该是
    0 项，可以当回归信号看。
    """
    exact_lower = set(s.lower() for s in exact)
    prefixes_lower = [s.lower() for s in prefixes if s]
    substrings_lower = [s.lower() for s in substrings if s]
    truth_lower = set(p.lower() for p in prefabs)

    contributing, idle = [], []
    for p in prefixes:
        rest = [x for x in prefixes_lower if x != p.lower()]
        n = count_hidden(prefabs, exact_lower, rest, substrings_lower)
        (contributing if n != baseline_hidden else idle).append("HiddenPrefixes:" + p)
    for s in substrings:
        rest = [x for x in substrings_lower if x != s.lower()]
        n = count_hidden(prefabs, exact_lower, prefixes_lower, rest)
        (contributing if n != baseline_hidden else idle).append("HiddenSubstrings:" + s)

    redundant_exact, absent_exact = [], []
    for e in exact:
        rest = set(x for x in exact_lower if x != e.lower())
        n = count_hidden(prefabs, rest, prefixes_lower, substrings_lower)
        if n != baseline_hidden:
            contributing.append("HiddenExact:" + e)
        elif e.lower() in truth_lower:
            redundant_exact.append(e)  # 真值里有这个 prefab，但已被前缀/子串挡住
        else:
            absent_exact.append(e)     # 真值里根本没有这个 prefab

    print("")
    print("隐藏规则贡献度（INFO，不影响退出码）")
    print("  有独占贡献             : %d 条" % len(contributing))
    if idle:
        print("  前缀/子串当前 0 命中   : %d 条 -> %s" % (len(idle), ", ".join(idle)))
        print("    （前瞻性防御规则，允许存在；但删掉它们不会被切分数断言发现）")
    else:
        print("  前缀/子串当前 0 命中   : 0 条")
    if redundant_exact:
        print("  HiddenExact 冗余项     : %d 项 -> %s" % (len(redundant_exact), ", ".join(redundant_exact)))
        print("    （已被前缀/子串完全覆盖，删掉不改变切分；建议清理）")
    else:
        print("  HiddenExact 冗余项     : 0 项")
    if absent_exact:
        print("  HiddenExact 真值中不存在: %d 项 -> %s" % (len(absent_exact), ", ".join(absent_exact)))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    env_truth = os.environ.get(TRUTH_ENV_VAR)
    ap = argparse.ArgumentParser()
    ap.add_argument("--catalog", default=os.path.join(here, "ItemCatalog.cs"))
    ap.add_argument("--truth", default=(env_truth if env_truth else DEFAULT_TRUTH))
    ap.add_argument("--expect-hidden", type=int, default=61)
    ap.add_argument("--expect-visible", type=int, default=152)
    args = ap.parse_args()

    if not os.path.isfile(args.catalog):
        fail("找不到 catalog 文件：%s" % args.catalog)
    if not os.path.isfile(args.truth):
        fail("找不到真值文件：%s\n"
             "  该文件不在 git 仓库内，换机器时按以下优先级指定：\n"
             "    1) 命令行 --truth <路径>\n"
             "    2) 环境变量 %s\n"
             "    3) 脚本内默认值 DEFAULT_TRUTH（%s）"
             % (args.truth, TRUTH_ENV_VAR, DEFAULT_TRUTH))

    # 先剥注释再解析：注释掉的表项不算表项，注释掉的隐藏规则也不算规则。
    src = strip_comments(read_text(args.catalog))
    try:
        truth = json.loads(read_text(args.truth))
    except ValueError as ex:
        fail("真值 JSON 解析失败：%s" % ex)

    truth_prefabs = [it["prefab"] for it in truth if it.get("prefab")]
    if not truth_prefabs:
        fail("真值文件里没有任何 prefab 字段")

    entries = parse_tag_map(src)
    exact = parse_string_set(src, "HiddenExact", "HiddenExact")
    prefixes = parse_string_set(src, "HiddenPrefixes", "HiddenPrefixes")
    substrings = parse_string_set(src, "HiddenSubstrings", "HiddenSubstrings")

    exact_lower = set(s.lower() for s in exact)
    prefixes_lower = [s.lower() for s in prefixes if s]
    substrings_lower = [s.lower() for s in substrings if s]

    hidden, visible = [], []
    for p in truth_prefabs:
        (hidden if is_hidden(p, exact_lower, prefixes_lower, substrings_lower) else visible).append(p)

    map_keys = [k for k, _ in entries]
    map_lower = {}
    dups = []
    for k in map_keys:
        if k.lower() in map_lower:
            dups.append(k)
        else:
            map_lower[k.lower()] = k

    visible_lower = set(p.lower() for p in visible)
    truth_lower = set(p.lower() for p in truth_prefabs)

    missing = sorted(p for p in visible if p.lower() not in map_lower)
    extra = sorted(map_lower[k] for k in map_lower if k in truth_lower and k not in visible_lower)
    dead = sorted(map_lower[k] for k in map_lower if k not in truth_lower)
    none_tags = sorted(k for k, v in entries if NONE_VALUE_RE.search(v))
    misfires = find_substring_misfires(truth, map_keys, exact_lower, prefixes_lower, substrings_lower)

    print("真值物品总数           : %d" % len(truth_prefabs))
    print("隐藏 / 可见            : %d / %d  (期望 %d / %d)"
          % (len(hidden), len(visible), args.expect_hidden, args.expect_visible))
    print("ItemTagMap 条目        : %d" % len(map_keys))
    print("HiddenExact/Prefix/Sub : %d / %d / %d" % (len(exact), len(prefixes), len(substrings)))

    problems = []
    if len(hidden) != args.expect_hidden:
        problems.append("隐藏数量 %d != 期望 %d" % (len(hidden), args.expect_hidden))
    if len(visible) != args.expect_visible:
        problems.append("可见数量 %d != 期望 %d" % (len(visible), args.expect_visible))
    if len(map_keys) != args.expect_visible:
        problems.append("ItemTagMap 条目 %d != 期望 %d" % (len(map_keys), args.expect_visible))
    if missing:
        problems.append("可见但 ItemTagMap 缺失 %d 项：%s" % (len(missing), ", ".join(missing)))
    if extra:
        problems.append("ItemTagMap 中已被隐藏规则挡掉 %d 项：%s" % (len(extra), ", ".join(extra)))
    if dead:
        problems.append("ItemTagMap 死键（游戏中无此 prefab）%d 项：%s" % (len(dead), ", ".join(dead)))
    if dups:
        problems.append("ItemTagMap 重复键 %d 项：%s" % (len(dups), ", ".join(dups)))
    if none_tags:
        problems.append("ItemTagMap 出现无分类值（None/(ItemCategory)0/default/0）%d 项：%s"
                        % (len(none_tags), ", ".join(none_tags)))
    if misfires:
        problems.append(
            "被子串规则误伤（被隐藏且无同名可见物品可替代，玩家彻底拿不到）%d 项：%s"
            % (len(misfires), ", ".join("%s(itemName=%s)" % (p, nm) for p, nm in misfires)))

    report_rule_coverage(truth_prefabs, exact, prefixes, substrings, len(hidden))

    if problems:
        print("")
        for p in problems:
            print("[FAIL] " + p)
        print("")
        print("ItemCatalog.cs 不变量校验失败。若刚跑过 scripts/gen_*.py，很可能是生成脚本")
        print("覆盖了人工维护的分类结论 —— 请对照 git diff 恢复。")
        return 1

    print("")
    print("[OK] ItemCatalog.cs 不变量全部成立（缺失 0 / 多余 0 / 死键 0）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
