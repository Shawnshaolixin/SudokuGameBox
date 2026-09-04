using NUnit.Framework;
using WaterSort.Core;

namespace WaterSort.Core.Tests
{
    /// <summary>
    /// 难度代理校准工具冒烟(M1.1):验证采样管线通顺、汇总字段自洽,并输出小样本分布供人工判读。
    /// 口径(M2.1):只统计可测实解(1 ≤ 步数 &lt; AnyBoundCap),预解/封顶/超时换题重散并单独计数
    /// ——与生成器窗口过滤等价(生成器只收窗口内实解),分布字段都只覆盖实解样本。
    /// 注:替代 Spike 的 PerfTable 大数据记录测试(结论已定稿于 19 文档 §10,日常回归不再烧大样本;
    /// M2 以本工具跑正式校准数据任务,采样量/区间定档以任务产出为准)。
    /// </summary>
    [TestFixture]
    public class WaterSortCalibTests
    {
        [Test, Timeout(120000)]
        public void SampleProxyDepth_4Colors_Smoke()
        {
            // 4 色代理深度采样:至少命中 1 个可测实解,统计字段满足 min ≤ avg ≤ max 的自洽关系
            var r = WaterSortCalib.SampleProxyDepth(4, seedBase: 5500, samples: 6);
            Assert.Greater(r.Solved, 0, "4 色散射在重洗上限内应能命中可测实解(仅剔除封顶漫游解后)");
            Assert.GreaterOrEqual(r.ScattersTried, r.Solved, "散射总次数须不小于实解命中数");
            Assert.GreaterOrEqual(r.Solved + r.Unsolved + r.PreSolved + r.CapHits + r.Timeouts, 0); // 哨兵:计数不炸
            if (r.Solved > 0)
            {
                Assert.GreaterOrEqual(r.MinSteps, 1, "实解步数 ≥1(0 步废题已被预解计数剔除)");
                Assert.GreaterOrEqual(r.MaxSteps, r.MinSteps);
                Assert.That(r.AvgSteps, Is.InRange(r.MinSteps, r.MaxSteps));
            }
            // M2 百分位自洽:分位单调且落在 [min, max] 内(样本 6,4 色命中率高,取 ≥3 命中即验)
            if (r.Solved >= 3)
            {
                Assert.That(r.P10, Is.InRange(r.MinSteps, r.P50), "P10 应在 min 与 P50 之间");
                Assert.That(r.P50, Is.InRange(r.P10, r.P90), "P50 应在 P10 与 P90 之间");
                Assert.That(r.P90, Is.InRange(r.P50, r.MaxSteps), "P90 应在 P50 与 max 之间");
            }
            TestContext.Progress.WriteLine($"Calib 4色代理: 实解={r.Solved}/{r.Samples} 未解={r.Unsolved} " +
                $"试散={r.ScattersTried} 封顶={r.CapHits} 预解={r.PreSolved} 超时={r.Timeouts} " +
                $"命中率={r.RealHitRate:P1} 深度 min={r.MinSteps} p10={r.P10} p50={r.P50} p90={r.P90} " +
                $"avg={r.AvgSteps:F1} max={r.MaxSteps} 耗时={r.TotalMs}ms");
        }

        [Test, Timeout(120000)]
        public void SampleOptimal_3Colors_Smoke()
        {
            // 3 色精确最优采样冒烟(数据记录性质,耗时按 Spike 实测 3 色均值 162ms 量级,小样本即快)
            var r = WaterSortCalib.SampleOptimalSteps(3, seedBase: 5600, samples: 5);
            Assert.Greater(r.Solved, 0, "3 色随机散射应命中可测实解且 IDA* 可精确测步");
            Assert.AreEqual(0, r.Timeouts, "3 色 IDA* 限时 2s(Spike max 617ms),不应超时");
            if (r.Solved > 0)
            {
                Assert.That(r.AvgSteps, Is.InRange(r.MinSteps, r.MaxSteps));
                Assert.GreaterOrEqual(r.MinSteps, 1, "0 步废题已被预解计数剔除");
            }
            TestContext.Progress.WriteLine($"Calib 3色精确: 实解={r.Solved}/{r.Samples} 步数 min={r.MinSteps} " +
                $"avg={r.AvgSteps:F1} max={r.MaxSteps} 耗时={r.TotalMs}ms");
        }

        [Test, Timeout(30000)]
        public void Sample_Reproducible_And_FieldSane()
        {
            // 校准采样同参数可复现(数据任务留档/复跑的前提),且每个样本独立重洗不共享序列
            var a = WaterSortCalib.SampleProxyDepth(5, seedBase: 5700, samples: 3);
            var b = WaterSortCalib.SampleProxyDepth(5, seedBase: 5700, samples: 3);
            Assert.AreEqual(a.Solved, b.Solved);
            Assert.AreEqual(a.Unsolved, b.Unsolved);
            Assert.AreEqual(a.MinSteps, b.MinSteps);
            Assert.AreEqual(a.MaxSteps, b.MaxSteps);
            Assert.AreEqual(a.AvgSteps, b.AvgSteps);
            Assert.AreEqual(a.P50, b.P50, "分位必须随种子可复现(M2 校准数据任务留档前提)");
            Assert.AreEqual(a.ScattersTried, b.ScattersTried, "散射计数随种子可复现");
            Assert.AreEqual(a.CapHits, b.CapHits, "封顶计数随种子可复现(口径排除项也要留档一致)");
            Assert.AreEqual(a.PreSolved, b.PreSolved);
            Assert.AreEqual(a.Timeouts, b.Timeouts);
            Assert.AreEqual(a.Samples, a.Solved + a.Unsolved, "样本账目:Solved+Unsolved 应等于 Samples");
        }
    }
}
