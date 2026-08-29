using System;
using System.Collections.Generic;
using ItemSpawnerEnhancement;
using NUnit.Framework;

namespace ItemSpawnerPremium.Tests
{
    /// <summary>
    /// <see cref="Loc"/> 门面与 <see cref="ItemCatalog.GetMajorLabel"/> 的测试。
    ///
    /// 这两处此前是 0 覆盖：<c>Loc.Get</c> / <c>LanguageCodeProvider</c> / <c>WarningLogger</c> /
    /// <c>ResetForTesting</c> 在测试里一次都没被引用 —— 其中 <c>ResetForTesting</c> 是**为测试而
    /// 存在却无人调用**的。<c>GetMajorLabel</c> 的 7 个分支加 <c>default → "?"</c> 也全未测，
    /// 而它只依赖纯逻辑的 <c>Loc.Get</c>，完全可测。
    ///
    /// Loc 是**静态类**，NUnit 默认同一程序集内测试串行但顺序不定，因此这里每条测试都在
    /// TearDown 里把 _catalog / LanguageCodeProvider / WarningLogger 三个静态槽位复位，
    /// 避免污染其他测试（例如让 LocalizationTests 看到一个被换掉的 catalog）。
    /// </summary>
    [TestFixture]
    public class LocTests
    {
        [TearDown]
        public void ResetStaticLocState()
        {
            // 顺序无关紧要，但三者都必须清：注入的 provider 会影响后续任何 Loc.Get，
            // 而换入的 catalog 会让"从内嵌资源加载"这条路径被跳过。
            Loc.LanguageCodeProvider = null;
            Loc.WarningLogger = null;
            Loc.ResetForTesting(null);
        }

        // ---------- Loc.Get ----------

        [Test]
        public void Get_WithoutLanguageProvider_FallsBackToEnglish()
        {
            // 运行时 Plugin.Awake 才注入 provider；在它之前（或测试环境里）Loc.Get 必须
            // 静默用 "en"，而不是 NRE。这条路径真实存在：Warmup / 早期日志会先于 Awake 完成。
            Loc.LanguageCodeProvider = null;
            Loc.ResetForTesting(null);

            Assert.That(Loc.Get("catProps"), Is.EqualTo("Misc"),
                "未注入 LanguageCodeProvider 时应回退英文");
        }

        [Test]
        public void Get_UsesInjectedLanguageCode()
        {
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "zh-Hans"; };
            Assert.That(Loc.Get("catProps"), Is.EqualTo("杂项"));

            // provider 是每次 Get 都调用的（不缓存语言码），所以游戏内切换语言无需重建 catalog
            Loc.LanguageCodeProvider = delegate { return "ja"; };
            Assert.That(Loc.Get("catProps"), Is.EqualTo("その他"),
                "语言码被缓存了 —— 游戏内切换语言后文案不会更新");
        }

        [Test]
        public void Get_UnknownLanguageCodeFallsBackToEnglish()
        {
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "xx-YY"; };
            Assert.That(Loc.Get("catProps"), Is.EqualTo("Misc"));

            Loc.LanguageCodeProvider = delegate { return null; };
            Assert.That(Loc.Get("catProps"), Is.EqualTo("Misc"), "provider 返回 null 也应回退英文");
        }

        [Test]
        public void Get_UnknownKeyReturnsTheKeyItself()
        {
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "en"; };
            Assert.That(Loc.Get("catNonexistent"), Is.EqualTo("catNonexistent"));
        }

        [Test]
        public void Get_ProviderThrowing_PropagatesTheException_DocumentingCurrentBehaviour()
        {
            // 现状记录，**不是**在主张这是最佳设计：Loc.Get 直接 `LanguageCodeProvider()`，
            // 没有 try/catch，所以 provider 抛出的异常会原样冒泡到调用方。
            //
            // 运行时注入的是 GameLanguage.CurrentCode（读 LocalizedText.Language 的枚举转换），
            // 理论上不抛；真抛了也应当让它冒泡到 UiEnhancer/ItemListView 的调用点被记录下来，
            // 而不是在这里静默吞掉换成英文 —— 后者会表现为"所有文案莫名变英文"且无任何日志。
            // 这条断言的价值是：如果将来有人给它加了 catch，测试会失败并迫使他解释为什么。
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { throw new InvalidOperationException("provider boom"); };

            Assert.That(delegate { Loc.Get("catProps"); },
                Throws.TypeOf<InvalidOperationException>(),
                "LanguageCodeProvider 的异常当前按设计冒泡；若被吞掉会导致文案静默变英文且无日志");
        }

        // ---------- Loc.ResetForTesting / WarningLogger ----------

        [Test]
        public void ResetForTesting_SwapsInTheGivenCatalog()
        {
            // ResetForTesting 存在的唯一目的就是这个：换入一个自建 catalog。
            // 用一个"什么都没加载"的 catalog（typeof(object).Assembly 里没有 .Localization.* 资源），
            // 它对任何 key 都返回 key 本身 —— 与真实 catalog 的结果截然不同，因此能证明换入生效。
            Loc.LanguageCodeProvider = delegate { return "en"; };

            Loc.ResetForTesting(new LocalizationCatalog(typeof(object).Assembly));
            Assert.That(Loc.Get("catProps"), Is.EqualTo("catProps"),
                "换入的空 catalog 未生效，Loc 仍在用旧实例");

            // 传 null 让下一次 Get 重新从本程序集的内嵌资源构建
            Loc.ResetForTesting(null);
            Assert.That(Loc.Get("catProps"), Is.EqualTo("Misc"),
                "ResetForTesting(null) 后应重新从内嵌资源构建 catalog");
        }

        [Test]
        public void WarningLogger_IsPassedToTheLazilyBuiltCatalog()
        {
            // WarningLogger 只在 Loc 首次惰性构建 catalog 时被传进去。这里用一个没有任何
            // 本地化资源的场景无法走 Loc 的惰性路径（它固定用 typeof(Loc).Assembly），
            // 而本测试程序集资源是齐的、只会为被白名单挡下的假语言 klingon.json 告警一条 ——
            // 正好可以用来验证注入的 logger 真的被接上了。
            List<string> warnings = new List<string>();
            Loc.WarningLogger = delegate (string m) { warnings.Add(m); };
            Loc.LanguageCodeProvider = delegate { return "en"; };
            Loc.ResetForTesting(null);   // 强制下一次 Get 重建 catalog

            Loc.Get("catProps");

            Assert.That(warnings.Count, Is.GreaterThan(0),
                "注入的 WarningLogger 未被传给惰性构建的 LocalizationCatalog");
            bool mentionedKlingon = false;
            for (int i = 0; i < warnings.Count; i++)
            {
                if (warnings[i].IndexOf("klingon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mentionedKlingon = true;
                }
            }
            Assert.That(mentionedKlingon, Is.True,
                "告警内容不是预期的白名单过滤提示，实际: " + string.Join(" / ", warnings.ToArray()));
        }

        [Test]
        public void CatalogIsBuiltOnceAndCached()
        {
            // Loc.Get 在每次 Rebuild / 每个分类按钮上都会被调用；若每次都 new LocalizationCatalog
            // （要枚举资源清单、反序列化 15 个 JSON）会是明显的性能问题。
            // 用 WarningLogger 的调用次数间接观察构建次数：构建一次告警一条（klingon 被过滤）。
            List<string> warnings = new List<string>();
            Loc.WarningLogger = delegate (string m) { warnings.Add(m); };
            Loc.LanguageCodeProvider = delegate { return "en"; };
            Loc.ResetForTesting(null);

            for (int i = 0; i < 20; i++)
            {
                Loc.Get("catProps");
            }
            Assert.That(warnings.Count, Is.EqualTo(1),
                "catalog 被重复构建了 " + warnings.Count + " 次（每次 Get 都重建会枚举资源清单并反序列化 15 个 JSON）");
        }

        // ---------- ItemCatalog.GetMajorLabel ----------

        [Test]
        public void GetMajorLabel_EveryDeclaredMajorCategoryHasANonPlaceholderLabel()
        {
            // 7 个分支逐一走到。GetMajorLabel 内部调 Loc.Get，所以先建立可控环境。
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "en"; };

            foreach (MajorCategory major in Enum.GetValues(typeof(MajorCategory)))
            {
                string label = ItemCatalog.GetMajorLabel(major);
                Assert.That(label, Is.Not.Null.And.Not.Empty, major + " 的标签为空");
                Assert.That(label, Is.Not.EqualTo("?"),
                    major + " 落进了 default 分支（新增枚举成员后忘记加 case）");
                // 也不该返回原始 key —— 那意味着本地化表缺 key
                Assert.That(label, Does.Not.StartWith("cat"),
                    major + " 返回了原始 key '" + label + "'，说明本地化表缺该 key");
            }
        }

        [Test]
        public void GetMajorLabel_MapsEachCategoryToItsOwnLocalisationKey()
        {
            // 只断言"非空且不是 ?"不足以发现串线（例如把 Food 映到 catTools）。
            // 这里用英文文案逐一锁定映射关系。
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "en"; };

            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.All), Is.EqualTo("All"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Tools), Is.EqualTo("Tools"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Food), Is.EqualTo("Food"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Mystical), Is.EqualTo("Mystical"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Equipment), Is.EqualTo("Equipment"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Consumables), Is.EqualTo("Consumables"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Props), Is.EqualTo("Misc"));
        }

        [Test]
        public void GetMajorLabel_FollowsTheInjectedLanguage()
        {
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "zh-Hans"; };
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Props), Is.EqualTo("杂项"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Consumables), Is.EqualTo("消耗品"));

            Loc.LanguageCodeProvider = delegate { return "ko"; };
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Props), Is.EqualTo("기타"));
        }

        [Test]
        public void GetMajorLabel_IllegalCastValuesReturnQuestionMark()
        {
            // UiEnhancer 用 (MajorCategory)(-1) 作"无按钮高亮"哨兵。GetMajorLabel 不会被喂这个值，
            // 但 default 分支返回 "?" 而不是抛异常/空串是刻意的：真出现时界面上会看到一个显眼的
            // 问号按钮，比空白按钮容易发现。
            Loc.ResetForTesting(null);
            Loc.LanguageCodeProvider = delegate { return "en"; };

            Assert.That(ItemCatalog.GetMajorLabel((MajorCategory)(-1)), Is.EqualTo("?"));
            Assert.That(ItemCatalog.GetMajorLabel((MajorCategory)7), Is.EqualTo("?"),
                "7 是紧邻 Props(6) 的下一个值；若枚举新增成员而 GetMajorLabel 忘记加 case，会命中这里");
            Assert.That(ItemCatalog.GetMajorLabel((MajorCategory)99), Is.EqualTo("?"));
        }

        [Test]
        public void GetMajorLabel_DoesNotThrowWhenNoLocalisationIsAvailable()
        {
            // 极端降级：catalog 一个语言都没加载（内嵌资源缺失）。此时应返回原始 key，
            // 而不是抛异常把整个分类条的构建带崩。
            Loc.LanguageCodeProvider = delegate { return "en"; };
            Loc.ResetForTesting(new LocalizationCatalog(typeof(object).Assembly));

            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.All), Is.EqualTo("catAll"));
            Assert.That(ItemCatalog.GetMajorLabel(MajorCategory.Props), Is.EqualTo("catProps"));
        }
    }
}
