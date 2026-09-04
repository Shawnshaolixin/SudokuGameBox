using System.Text;
using NUnit.Framework;
using WaterSort.Core;

namespace WaterSort.Core.Tests
{
    /// <summary>
    /// 难度代理校准工具冒烟(M1.1):验证采样管线通顺、汇总字段自洽,并输出小样本分布供人工判读。
    /// 注:替代 Spike 的 PerfTable 大数据记录测试(结论已定稿于 19 文档 §10,日常回归不再烧大样本;
    /// M2 以本工具跑正式校准数据任务,采样量/区间定档以任务产出为准)。
    /// </summary>
    [TestFixture]
    public class WaterSortCalibTests
    {
        [Test, Timeout(120000)]
        public void SampleProxyDepth_4Colors_Smoke()
        {
            // 4 色代理深度采样:至少命中 1 个可解样本,统计字段满足 min ≤ avg ≤ max 的自洽关系
            var r = WaterSortCalib.SampleProxyDepth(4, seedBase: 5500, samples: 6);
            Assert.Greater(r.Solved, 0, "4 色随机散射应几乎全部可解(快筛 400ms)");
            Assert.AreEqual(0, r.TimedOutOnSolve, "代理模式无非正式求解环节,不应有超时计数");
            if (r.Solved > 0)
            {
                Assert.GreaterOrEqual(r.MinSteps, 1);
                Assert.GreaterOrEqual(r.MaxSteps, r.MinSteps);
                Assert.That(r.AvgSteps, Is.InRange(r.MinSteps, r.MaxSteps));
            }
            TestContext.Progress.WriteLine($"Calib 4色代理: solved={r.Solved}/{r.Samples} 深度 min={r.MinSteps} avg={r.AvgSteps:F1} max={r.MaxSteps} 耗时={r.TotalMs}ms");
        }

        [Test, Timeout(120000)]
        public void SampleOptimal_3Colors_Smoke()
        {
            // 3 色精确最优采样冒烟(数据记录性质,耗时按 Spike 实测 3 色均值 162ms 量级,小样本即快)
            var r = WaterSortCalib.SampleOptimalSteps(3, seedBase: 5600, samples: 5);
            Assert.Greater(r.Solved, 0, "3 色随机散射应几乎全部可解且 IDA* 可精确测步");
            Assert.AreEqual(0, r.TimedOutOnSolve, "3 色 IDA* 限时 2s(Spike max 617ms),不应超时");
            if (r.Solved > 0)
            {
                Assert.That(r.AvgSteps, Is.InRange(r.MinSteps, r.MaxSteps));
                Assert.GreaterOrEqual(r.MinSteps, 1);
            }
            TestContext.Progress.WriteLine($"Calib 3色精确: solved={r.Solved}/{r.Samples} 步数 min={r.MinSteps} avg={r.AvgSteps:F1} max={r.MaxSteps} 耗时={r.TotalMs}ms");
        }

        [Test, Timeout(30000)]
        public void Sample_Reproducible_And_FieldSane()
        {
            // 校准采样同参数可复现(数据任务留档/复跑的前提),且每个样本独立重洗不共享序列
            var a = WaterSortCalib.SampleProxyDepth(5, seedBase: 5700, samples: 3);
            var b = WaterSortCalib.SampleProxyDepth(5, seedBase: 5700, samples: 3);
            Assert.AreEqual(a.Solved, b.Solved);
            Assert.AreEqual(a.MinSteps, b.MinSteps);
            Assert.AreEqual(a.MaxSteps, b.MaxSteps);
            Assert.AreEqual(a.AvgSteps, b.AvgSteps);
            Assert.AreEqual(0, a.TimedOutOnSolve);
            Assert.AreEqual(a.Samples, a.Solved + a.Unsolved, "样本账目:Solved+Unsolved 应等于 Samples");
        }
    }
}
