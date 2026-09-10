// 分类体系基于游戏交互提示 / 组件 / 显示名与游戏机制设计（多标签）。
//
// 【本文件是人工维护的，不要重跑生成器】
// 文件最初由 scripts/gen_category_multi.py 生成，但此后经过大量人工语义修订：
// 与该脚本当前输出相差 84 条（脚本 198 条 vs 本表 153 条：仅脚本有 46 条、仅本表有 1 条、
// 共有键但标签集不同 37 条），且脚本输出的 hidden_prefixes 是空数组 ——
// 重跑生成器会把 30 个棋子与 16 个书页放进目录、并抹掉全部用户拍板的分类结论。
// src/verify_catalog.py 是防这件事的门禁（切分数一变就 exit 1，pack.ps1 因此拒绝打包），
// 但门禁只能阻止发布，救不回被覆盖的源码 —— 真要重新生成，先让生成器写到 extract/ 供人工 diff。
using System.Collections.Generic;

namespace ItemSpawnerEnhancement
{
    /// <summary>二级分类（Flags，一物可属多类）。</summary>
    [System.Flags]
    public enum ItemCategory : int
    {
        None = 0,
        Tools = 1,
        Food = 2,
        Mystical = 4,
        Equipment = 8,
        Consumables = 16,
        Props = 32,
    }

    /// <summary>一级分类（顶部横向按钮，全部 + 六类）。</summary>
    public enum MajorCategory : int
    {
        All = 0,
        Tools = 1,
        Food = 2,
        Mystical = 3,
        Equipment = 4,
        Consumables = 5,
        Props = 6,
    }

    public static partial class ItemCatalog
    {
        /// <summary>物品 prefab 名 -> 全部标签（Flags）。主分类由 ItemCatalog.PrimaryOfTags 运行时推导。</summary>
        public static readonly Dictionary<string, ItemCategory> ItemTagMap = new Dictionary<string, ItemCategory>(System.StringComparer.OrdinalIgnoreCase)
        {
            // 分类依据：游戏资源真值提取（item_truth.json）——itemTags / totalUses / 子树全部组件，
            // 再叠加人工语义判断。**本表是人工维护的语义结论，判定标准与
            // ItemListView.ResolveCategories 的机械组件兜底标准不同**，后续维护者不要按兜底规则重算本表。
            //
            // Consumables 的实际标准是「使用后物品本体消失」，覆盖三类，其中后两类无法从静态资源推出：
            //   a) 次数耗尽：totalUses>0 或 Action_ReduceUses（如 Sunscreen 3 次）；
            //   b) 燃料耗尽后 photonView.RPC("Consume", ...)：Torch/Lantern/Candle/RopeSpool/Anti-Rope Spool
            //      在真值里 totalUses=-1 且 actions 不含 Action_Consume，是运行时燃料逻辑触发的销毁；
            //   c) 投掷/触发后自毁：HealingPuffShroom（砸地碎裂）、Parachute（落地后 Consume）、
            //      Rocketpack（燃尽爆炸）、MagicBean（种下后本体销毁）等 ConsumeDelayed/自毁族。
            // 因此表中带 Consumables 的条目不一定满足 totalUses>0 || Action_Consume，反之亦然。
            //
            // **Food 与 Consumables 互斥**：能吃的东西一律只归「食物」，即使它有使用次数。
            // 这是玩家视角的分类而不是机制分类 —— 在「消耗品」里翻到童军饼干只会让人以为分类坏了。
            // 因此本表中不存在同时带 Food 与 Consumables 的条目（单元测试锁定了这一点）。
            { "Airplane Food", ItemCategory.Food },
            { "AK", ItemCategory.Props },
            { "AloeVera", ItemCategory.Consumables },
            { "Amulet_Clone", ItemCategory.Mystical },
            { "Amulet_Healing", ItemCategory.Mystical },
            { "Amulet_InfiniteStamina", ItemCategory.Mystical },
            { "Amulet_SuperJump", ItemCategory.Mystical },
            { "AncientIdol", ItemCategory.Mystical },
            { "Anti-Rope Spool", ItemCategory.Consumables | ItemCategory.Mystical | ItemCategory.Tools },  // 绳索燃料耗尽后 Consume
            { "Antidote", ItemCategory.Consumables },
            { "AntiZooka", ItemCategory.Consumables | ItemCategory.Mystical | ItemCategory.Tools },  // 3 次
            { "Apple Berry Green", ItemCategory.Food },
            { "Apple Berry Red", ItemCategory.Food },
            { "Apple Berry Weird", ItemCategory.Food },
            { "Apple Berry Yellow", ItemCategory.Food },
            { "Backpack", ItemCategory.Equipment },
            { "Balloon", ItemCategory.Consumables },
            { "BalloonBunch", ItemCategory.Consumables },
            { "Bandages", ItemCategory.Consumables },
            { "Basketball", ItemCategory.Props },
            { "Beehive", ItemCategory.Props },
            { "Berrynana Blue", ItemCategory.Food },
            { "Berrynana Brown", ItemCategory.Food },
            { "Berrynana Peel Blue Variant", ItemCategory.Props },
            { "Berrynana Peel Brown Variant", ItemCategory.Props },
            { "Berrynana Peel Pink Variant", ItemCategory.Props },
            { "Berrynana Peel Yellow", ItemCategory.Props },
            { "Berrynana Pink", ItemCategory.Food },
            { "Berrynana Yellow", ItemCategory.Food },
            { "BingBong", ItemCategory.Equipment | ItemCategory.Mystical },
            { "Binoculars", ItemCategory.Equipment },
            { "BookOfBones", ItemCategory.Consumables | ItemCategory.Mystical },  // 1 次
            { "BounceShroom", ItemCategory.Tools },
            { "Bugfix", ItemCategory.Food },
            { "Bugle", ItemCategory.Equipment },
            { "Bugle_Magic", ItemCategory.Equipment | ItemCategory.Mystical },
            { "Bugle_Scoutmaster Variant", ItemCategory.Consumables | ItemCategory.Mystical },  // 1 次
            { "CactusBall", ItemCategory.Props },
            { "Candle", ItemCategory.Consumables | ItemCategory.Tools },        // 光源：燃料耗尽
            { "ChainShooter", ItemCategory.Consumables | ItemCategory.Tools },  // 1 次
            { "Cheat Compass", ItemCategory.Consumables | ItemCategory.Mystical },     // 1 次
            { "Cheat Compass 1", ItemCategory.Consumables | ItemCategory.Mystical },   // 1 次
            { "ClimbingSpike", ItemCategory.Tools },
            { "CloudFungus", ItemCategory.Tools },
            { "Clusterberry Black", ItemCategory.Food },
            { "Clusterberry Red", ItemCategory.Food },
            // prefab 名带 _UNUSED 后缀，但它是货真价实的第四种葚莓（itemName=Green Clusterberry，
            // 中文"青葚莓"，tag=Berry，组件与上面三个兄弟逐项同构：Action_RestoreHunger +
            // Action_Consume + Action_ModifyStatus）。它在 ItemDatabase.Objects 里，游戏里也真的会刷。
            // 2.3.1 之前它被 HiddenSubstrings 的 "_UNUSED" 误伤，玩家在游戏里见过的青葚莓无法生成。
            // 见 HiddenSubstrings 的注释：那条规则已删除。
            { "Clusterberry_UNUSED", ItemCategory.Food },
            { "Clusterberry Yellow", ItemCategory.Food },
            { "Compass", ItemCategory.Equipment },
            { "Cure-All", ItemCategory.Consumables | ItemCategory.Mystical },   // 3 次
            { "Cure-Some", ItemCategory.Consumables | ItemCategory.Mystical },
            { "Cursed Skull", ItemCategory.Mystical },
            { "Darkberry", ItemCategory.Food | ItemCategory.Mystical },
            { "Dynamite", ItemCategory.Consumables },                           // 1 次
            { "EarlyWorm", ItemCategory.Food },
            { "Egg", ItemCategory.Food },
            { "EggRaven", ItemCategory.Food },
            { "EggTurkey", ItemCategory.Food },
            { "Energy Drink", ItemCategory.Food },
            { "Energy Elixir", ItemCategory.Consumables | ItemCategory.Mystical },
            { "Fannypack", ItemCategory.Equipment },
            { "FireWood", ItemCategory.Props },
            { "FirstAidKit", ItemCategory.Consumables },
            { "Flag_Plantable_Checkpoint", ItemCategory.Consumables | ItemCategory.Tools },
            { "Flare", ItemCategory.Consumables },                              // 1 次
            { "FortifiedMilk", ItemCategory.Food },
            { "Frisbee", ItemCategory.Props },
            { "Frog", ItemCategory.Food },
            { "FrogLegs", ItemCategory.Food },
            { "Glider", ItemCategory.Equipment },
            { "Glizzy", ItemCategory.Food },
            { "Glizzy_CattailVariant", ItemCategory.Food },
            { "Granola Bar", ItemCategory.Food },
            { "Guidebook", ItemCategory.Equipment },
            { "HealingDart Variant", ItemCategory.Consumables | ItemCategory.Tools },  // 1 次
            { "HealingPuffShroom", ItemCategory.Consumables | ItemCategory.Tools },  // 砸地碎裂放出绿烟清除负面状态（ShelfShroom 族，投出即自毁）
            { "Heat Pack", ItemCategory.Consumables },
            { "Item_Coconut", ItemCategory.Food | ItemCategory.Props },  // 整颗需砸开（Breakable+Bonkable），本身零交互投掷物
            { "Item_Coconut_half", ItemCategory.Food },
            { "Item_Honeycomb", ItemCategory.Food },
            { "Jetpack", ItemCategory.Equipment },
            { "Kingberry Green", ItemCategory.Food },
            { "Kingberry Purple", ItemCategory.Food },
            { "Kingberry Yellow", ItemCategory.Food },
            { "Lantern", ItemCategory.Consumables | ItemCategory.Tools },       // 光源：燃料耗尽
            { "Lantern_Faerie", ItemCategory.Consumables | ItemCategory.Mystical | ItemCategory.Tools },
            { "Lollipop", ItemCategory.Food },
            { "Lollipop_Evil", ItemCategory.Food | ItemCategory.Mystical },
            { "MagicBean", ItemCategory.Consumables | ItemCategory.Mystical | ItemCategory.Tools },  // 种下生成藤蔓后本体销毁
            { "Mandrake", ItemCategory.Food },
            { "Marshmallow", ItemCategory.Food },
            { "Matchbook", ItemCategory.Equipment },   // 实为查看叠加层（同望远镜），非光源
            { "MedicinalRoot", ItemCategory.Food },
            { "Megaphone", ItemCategory.Equipment },
            { "Mushroom Chubby", ItemCategory.Food },
            { "Mushroom Cluster", ItemCategory.Food },
            { "Mushroom Cluster Poison", ItemCategory.Food },
            { "Mushroom Glow", ItemCategory.Food },
            { "Mushroom Lace", ItemCategory.Food },
            { "Mushroom Lace Poison", ItemCategory.Food },
            { "Mushroom Normie", ItemCategory.Food },
            { "Mushroom Normie Poison", ItemCategory.Food },
            { "Napberry", ItemCategory.Food },
            { "NestEgg", ItemCategory.Food },
            { "NestEgg_Raven", ItemCategory.Food },
            { "Painkillers", ItemCategory.Consumables },
            { "PandorasBox", ItemCategory.Consumables | ItemCategory.Mystical },  // 3 次
            { "Parachute", ItemCategory.Consumables | ItemCategory.Equipment },  // 坠落自动触发，用后 Consume
            { "Parasol", ItemCategory.Equipment },
            { "Parasol_Roots Variant", ItemCategory.Equipment },
            { "Passport", ItemCategory.Equipment },
            { "Pepper Berry", ItemCategory.Food },
            { "Pirate Compass", ItemCategory.Equipment },
            { "PortableStovetopItem", ItemCategory.Consumables | ItemCategory.Tools },
            { "Prickleberry_Gold", ItemCategory.Food },
            { "Prickleberry_Red", ItemCategory.Food },
            { "RescueHook", ItemCategory.Consumables | ItemCategory.Tools },    // 3 次
            { "RescueHook_Infinite", ItemCategory.Tools },                      // 无限次
            { "RitualDagger", ItemCategory.Mystical },
            { "Rocketpack", ItemCategory.Consumables | ItemCategory.Equipment },  // 占背包位，燃尽爆炸自毁
            { "RopeShooter", ItemCategory.Consumables | ItemCategory.Tools },   // 1 次
            { "RopeShooterAnti", ItemCategory.Consumables | ItemCategory.Mystical | ItemCategory.Tools },
            { "RopeSpool", ItemCategory.Consumables | ItemCategory.Tools },  // 绳索燃料耗尽后 Consume
            { "Scorpion", ItemCategory.Food },
            { "ScoutCannonItem", ItemCategory.Consumables | ItemCategory.Tools },
            { "ScoutCookies", ItemCategory.Food },        // 有 4 次用量，但食物归食物：Consumables 与 Food 互斥，见本表开头的说明
            { "ScoutCookies_Vanilla", ItemCategory.Food }, // 同上
            { "ScoutEffigy", ItemCategory.Consumables | ItemCategory.Mystical },
            { "ScoutsHonor", ItemCategory.Mystical },
            { "ShelfShroom", ItemCategory.Tools },
            { "Shell Big", ItemCategory.Props },
            { "Shroomberry_Blue", ItemCategory.Food },
            { "Shroomberry_Green", ItemCategory.Food },
            { "Shroomberry_Purple", ItemCategory.Food },
            { "Shroomberry_Red", ItemCategory.Food },
            { "Shroomberry_Yellow", ItemCategory.Food },
            { "Skyberry", ItemCategory.Food },
            { "Snowball", ItemCategory.Props },
            { "Sports Drink", ItemCategory.Food },
            { "Stone", ItemCategory.Props },
            { "Strange Gem", ItemCategory.Mystical },
            { "Sunscreen", ItemCategory.Consumables },                          // 3 次
            { "Torch", ItemCategory.Consumables | ItemCategory.Tools },         // 光源：燃料耗尽
            { "TrailMix", ItemCategory.Food },
            { "Wand of Wind", ItemCategory.Consumables | ItemCategory.Mystical },  // 3 次
            { "Warp Compass", ItemCategory.Consumables | ItemCategory.Mystical },  // 3 次
            { "WarpFungus", ItemCategory.Mystical | ItemCategory.Tools },
            { "Warpsketball", ItemCategory.Props },
            { "Winterberry Orange", ItemCategory.Food },
            { "Winterberry Yellow", ItemCategory.Food },
            { "Wonderberry", ItemCategory.Food | ItemCategory.Mystical },
            { "Yuzu Berry", ItemCategory.Food },
        };

        /// <summary>
        /// 补充本地化 key 映射：prefab 名 -> 本地化 key（UIData.itemName 与游戏表不对应时使用）。
        /// 说明：当前游戏版本下这些 key 与 "NAME_" + UIData.itemName 的默认解析结果一致，
        /// 保留作为版本兼容兜底（若未来 UIData.itemName 改动，此表可锁定正确 key）。
        /// </summary>
        public static readonly Dictionary<string, string> ExtraNameKeys = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Bugfix", "NAME_TICK" },
            { "EggRaven", "NAME_EGG" },
            { "EggTurkey", "NAME_BIRD" },
            { "NestEgg", "NAME_BIG EGG" },
            { "NestEgg_Raven", "NAME_SMALL EGG" },
            { "Parachute", "NAME_AUTOPARACHUTE" },
        };

        /// <summary>
        /// 补充自定义名称：prefab 名 -> (语言代码 -> 显示名)。
        ///
        /// 仅用于「游戏本地化表完全没有该物品的 NAME_ key」的少数物品。判定方式是拿
        /// `"NAME_" + UIData.itemName.ToUpperInvariant()` 去查游戏表（Resources/Localization/
        /// SerializedTermsData，837 键 / 206 个 NAME_* 行），查不到才需要在这里补。
        ///
        /// 为什么从「[中文, 英文] 两元组」改成按语言码索引（2.3.0）：
        /// 旧结构只能区分"中文界面"与"其它"，于是这些物品在日/韩/俄/乌/德/法/意/西/葡/波/土
        /// 这 13 种语言下全部显示英文名，而周围所有物品都是母语 —— 玩家看到的是一两个突兀的英文条目。
        ///
        /// 同时删除了 11 条运行时永不命中的条目（AK / Apple Berry Weird / Cure-Some / Darkberry /
        /// Energy Elixir / Lollipop_Evil / Matchbook / Painkillers / Skyberry / Wand of Wind /
        /// Wonderberry）：它们的资源路径是 `0_items/unused/*`，未登记进 ItemDatabase.Objects
        /// （实测该列表 194 项），因此本模组的目录遍历永远取不到它们，译名再全也没人看得到。
        /// 注意 ItemTagMap 里对应的键**刻意保留**——那张表要与 item_truth.json 的可见集保持
        /// 精确一一对应（verify_catalog.py 的不变量），删键会破坏该断言，而留着的代价只是几十字节。
        ///
        /// ClimbingChalk 虽然已进 HiddenExact（没有实际作用），译名仍然保留：
        /// 玩家把 HideUnused 关掉时隐藏物品会全部显示，那时这条译名就会被用到。
        /// </summary>
        public static readonly Dictionary<string, Dictionary<string, string>> ExtraCustomNames =
            new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            // 攀岩粉：游戏表缺 NAME_CLIMBING CHALK。各语言取攀岩运动里的通用叫法
            // （多数语言用"碳酸镁"而非直译"粉笔"）。
            { "ClimbingChalk", new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    { "en", "Climbing Chalk" },
                    { "zh-Hans", "攀岩粉" },
                    { "zh-Hant", "攀岩粉" },
                    { "ja", "クライミングチョーク" },
                    { "ko", "클라이밍 초크" },
                    { "ru", "Магнезия" },
                    { "uk", "Магнезія" },
                    { "fr", "Magnésie" },
                    { "it", "Magnesite" },
                    { "de", "Kletterkreide" },
                    { "es-ES", "Magnesio" },
                    { "es-419", "Magnesio" },
                    { "pt-BR", "Magnésio" },
                    { "pl", "Magnezja" },
                    { "tr", "Tırmanma tozu" },
                }
            },
            // 太空篮球：游戏表缺 NAME_WARPSKETBALL。原文是 warp + basketball 的合成词，
            // 投出后落地会把投掷者传送过去（Peak.WarpOnThrow：碰撞满足条件时
            // RPC("WarpPlayerRPC") 再 PhotonNetwork.Destroy 自身）。
            // 中文从 2.2.0 的"太空篮球"改为"传送篮球"：前者是按 warp 的字面联想取的，
            // 说不出这东西干什么；后者直接告诉玩家它的作用。
            { "Warpsketball", new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    { "en", "Warpsketball" },
                    { "zh-Hans", "传送篮球" },
                    { "zh-Hant", "傳送籃球" },
                    { "ja", "ワープバスケットボール" },
                    { "ko", "워프 농구공" },
                    { "ru", "Телепорт-мяч" },
                    { "uk", "Телепорт-м'яч" },
                    { "fr", "Ballon de téléportation" },
                    { "it", "Palla di teletrasporto" },
                    { "de", "Teleportball" },
                    { "es-ES", "Balón de teletransporte" },
                    { "es-419", "Balón de teletransporte" },
                    { "pt-BR", "Bola de teletransporte" },
                    { "pl", "Piłka teleportacyjna" },
                    { "tr", "Işınlanma topu" },
                }
            },
        };

        /// <summary>显式隐藏的物品 prefab 名（装饰/测试物品等）。</summary>
        public static readonly HashSet<string> HiddenExact = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // 2.3.0 删除了 6 条被下方 HiddenSubstrings 完全覆盖的冗余项
            // （BingBong_Prop Variant / Binoculars_Prop / Bugle_Prop Variant / Lollipop_Prop
            //  / Clusterberry_UNUSED / Mandrake_Hidden）。
            // 冗余不只是啰嗦：它让 "_Prop" / "_UNUSED" / "_Hidden" 三条子串规则在当前数据下
            // 没有任何独占命中，于是整条删掉也不会改变 60/153 切分，verify_catalog.py 与单元测试
            // 全部照过 —— 规则实际失效而无人可知，直到游戏新增一个 Xxx_Prop 装饰物出现在目录里。
            // 去掉冗余后，子串规则成为唯一命中路径，任何误删都会立刻表现为切分数变化。
            //
            // 下面留下的 10 条都有独占命中（不含下划线或用空格分隔，子串规则抓不到）：
            "BaseConstructable Variant",
            "BaseObject",
            "Berrynana UNUSED",   // 空格而非下划线，"_UNUSED" 抓不到
            // 攀岩粉与童军队长之魂：游戏里没有实际作用（前者只有 Action_ApplyAffliction 但
            // 没有可用效果、游戏本地化表里连名字都没有；后者只有 Breakable，砸开什么也不给），
            // 属于"能生成但生成了没意义"的物件，与棋子/书页同类，不进目录。
            "ClimbingChalk",
            "ScoutmasterSoul",
            "Propeller",          // 含 "Prop" 但不含 "_Prop"
            "Portable Speaker",
            "Skull",
            "TestConstructable Variant",
            "foodTest",
        };


        /// <summary>
        /// 需要隐藏的前缀（棋子等装饰物）。
        /// 注意 "GuidebookPage" 会连带隐藏 "GuidebookPageScroll Variant"（itemName=Scroll，
        /// 带 Action_SpawnGuidebookPage、实际可用的道具）—— 这是**有意为之的产品决策**：
        /// 该道具与 Guidebook 功能重叠且属于收集流程内部物件，不进目录。
        /// 后续维护者请不要把它当 bug「修复」；若确需放出，应加白名单而非改前缀规则
        /// （改前缀会连带放出真正的装饰用 GuidebookPage* 资源）。
        /// </summary>
        public static readonly string[] HiddenPrefixes = new string[]
        {
            "C_Bishop",
            "C_King",
            "C_Knight",
            "C_Pawn",
            "C_Queen",
            "C_Rook",
            "GuidebookPage",
        };

        /// <summary>
        /// 需要隐藏的子串。
        ///
        /// "_Prop" / "_Hidden" 各有独占命中（前者 4 项、后者 1 项），且这些物品都有同名的可见代表
        /// （BingBong / Binoculars / Bugle / Lollipop / MedicinalRoot），隐藏的是重复品 ——
        /// 玩家仍能拿到同名的那个，网格里也不会出现两条一模一样的条目。
        ///
        /// "_TEMP" 在当前 213 项真值里 0 命中，是**有意保留的前瞻性规则**（游戏历史上用过该后缀）；
        /// verify_catalog.py 会把这类"当前无贡献"的规则列为 INFO 提示而不失败。
        ///
        /// **"_UNUSED" 已在 2.3.1 删除。** 名字带 UNUSED 不等于物品没用：它在运行时数据库里
        /// 只命中一个物品，而那个物品（Clusterberry_UNUSED，itemName=Green Clusterberry，
        /// 中文"青葚莓"）是真实的第四种葚莓，tag=Berry、带完整的食用组件、游戏里真的会刷。
        /// 玩家反馈"游戏里的青葚莓生成不出来"就是这个误伤造成的。
        /// 真正没用处的 prefab（如 Berrynana UNUSED）本来就不在 ItemDatabase.Objects 里，
        /// 而本模组的目录只遍历那个列表，所以它们根本走不到隐藏规则这一步 ——
        /// 这条规则唯一的效果就是误伤，删掉它才是对的。
        /// </summary>
        public static readonly string[] HiddenSubstrings = new string[]
        {
            "_Prop",
            "_TEMP",
            "_Hidden",
        };

    }
}
