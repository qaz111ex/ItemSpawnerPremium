#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ItemCatalog.cs 不变量校验（发行前置门禁）。

对照 item_truth.json（游戏资源真值，213 个物品 prefab）校验三条不变量：

  1. 隐藏 / 可见的切分符合预期（默认 60 / 153）。切分规则复刻
     ItemCatalogMethods.cs 的 IsHidden：HiddenExact 精确匹配、HiddenPrefixes 前缀、
     HiddenSubstrings 子串，全部大小写不敏感。
  2. ItemTagMap 的键集合与「可见集合」精确一一对应：
     - missing：可见但表里没有（运行时会走兜底推导分类，分类质量不可控）
     - extra  ：表里有但被隐藏规则挡掉（表项永远用不到）
     - dead   ：表里有但游戏里根本没有这个 prefab（历史遗留死键）
     三者都必须为 0。
  3. ItemTagMap 无重复键、无 ItemCategory.None。

为什么需要它：ItemCatalog.cs 顶部仍带 auto-generated 头，但表内容早已经过人工语义
修订，与 scripts/gen_category_multi.py 的输出存在大量差异。任何人重跑生成脚本都会
把人工结论覆盖回去。本脚本作为 pack.ps1 的前置校验，让这类回退在发布前直接失败。

用法：
    python verify_catalog.py [--catalog ItemCatalog.cs] [--truth <item_truth.json>]
                             [--expect-hidden 60] [--expect-visible 153]
退出码：0 = 全部通过；1 = 存在违规；2 = 输入文件缺失/解析失败。
"""

import argparse
import json
import os
import re
import sys
from typing import NoReturn

DEFAULT_TRUTH = r"D:\zhuanban\youhua\item_truth.json"


def fail(msg, code=2):
    # type: (str, int) -> NoReturn
    print("[FAIL] " + msg)
    sys.exit(code)


def read_text(path):
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


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
    body = re.sub(r"//[^\n]*", "", body)
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


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--catalog", default=os.path.join(here, "ItemCatalog.cs"))
    ap.add_argument("--truth", default=DEFAULT_TRUTH)
    ap.add_argument("--expect-hidden", type=int, default=60)
    ap.add_argument("--expect-visible", type=int, default=153)
    args = ap.parse_args()

    if not os.path.isfile(args.catalog):
        fail("找不到 catalog 文件：%s" % args.catalog)
    if not os.path.isfile(args.truth):
        fail("找不到真值文件：%s（用 --truth 指定路径）" % args.truth)

    src = read_text(args.catalog)
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
    none_tags = sorted(k for k, v in entries if re.search(r"\bItemCategory\.None\b", v))

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
        problems.append("ItemTagMap 出现 ItemCategory.None %d 项：%s" % (len(none_tags), ", ".join(none_tags)))

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
