using System;
using NUnit.Framework;
using UnityEngine;
using WaterSort.Core;

namespace WaterSort.Core.Tests
{
    /// <summary>
    /// 生成器落档与关卡数据编解码(新增用例,M1.1 正式化部分)。
    /// 落档断言只验证机制自洽(区间含界/色数开关),不锁具体数值——档位数值属运营配置,由 M2 校准回写。
    /// </summary>
    [TestFixture]
    public class WaterSortLevelGenTests
    {
        private static WaterSortGenSpec Spec(WaterSortDifficulty d, int minColors, int maxColors, int minSteps, int maxSteps)
            => new WaterSortGenSpec { Difficulty = d, MinColors = minColors, MaxColors = maxColors, MinSteps = minSteps, MaxSteps = maxSteps };

        [Test]
        public void Generate_SameSeed_Reproducible()
        {
            // 资产管线要求同种子完全复现(同题同实测步数),才能支持"生成→校验→固化"的分步流水
            var spec = Spec(WaterSortDifficulty.Medium, 3, 5, 5, 100);
            var a = WaterSortLevelGen.Generate(spec, seed: 424242);
            var b = WaterSortLevelGen.Generate(spec, seed: 424242);
            Assert.IsTrue(a.Succeeded);
            Assert.AreEqual(a.Board.EncodeKey(), b.Board.EncodeKey(), "同种子应产出同题面");
            Assert.AreEqual(a.MeasuredSteps, b.MeasuredSteps);
            Assert.AreEqual(a.Colors, b.Colors);
            Assert.AreEqual(a.Attempts, b.Attempts);
        }

        [Test]
        public void Generate_Window_AcceptanceBounds()
        {
            // ≤3 色精确最优:命中结果必须落在规格区间内且标记为精确
            var spec = Spec(WaterSortDifficulty.Easy, 3, 3, 6, 8);
            int accepted = 0;
            for (int s = 0; s < 12; s++)
            {
                var g = WaterSortLevelGen.Generate(spec, seed: 7000 + s);
                if (!g.Succeeded) continue;
                accepted++;
                Assert.IsTrue(g.MeasuredOptimal, "3 色落档必须用 IDA* 精确最优");
                Assert.That(g.MeasuredSteps, Is.InRange(6, 8), "精确步数须落在规格区间(含界)");
            }
            Assert.Greater(accepted, 0, "区间 [6,8] 在 3 色下不应全数落空(12 种子×40 重洗)");
        }

        [Test]
        public void Generate_UnreachableWindow_NeverAccepts()
        {
            // 步数恒 ≥1,上界 0 不可达 → 确定性地全部拒绝(否定路径的落档过滤验证)
            var spec = Spec(WaterSortDifficulty.Easy, 3, 3, 0, 0);
            for (int s = 0; s < 3; s++)
                Assert.IsFalse(WaterSortLevelGen.Generate(spec, seed: 7100 + s).Succeeded);
        }

        [Test]
        public void Generate_ColorRange_TagsMeasurementByColor()
        {
            // 色数跨 3/4 开关:同一规格内 ≤3 色必须精确、≥4 色必须代理;两种分支都应能命中(多种子累计)
            var spec = Spec(WaterSortDifficulty.Medium, 3, 5, 5, 100);
            bool sawExact = false, sawProxy = false;
            for (int s = 0; s < 12; s++)
            {
                var g = WaterSortLevelGen.Generate(spec, seed: 7200 + s);
                Assert.IsTrue(g.Succeeded);
                Assert.That(g.Colors, Is.InRange(3, 5));
                if (g.Colors <= 3)
                {
                    sawExact = true;
                    Assert.IsTrue(g.MeasuredOptimal, "≤3 色实测须为精确最优");
                }
                else
                {
                    sawProxy = true;
                    Assert.IsFalse(g.MeasuredOptimal, "≥4 色实测须为 SolveAny 代理深度");
                }
                Assert.That(g.MeasuredSteps, Is.InRange(5, 100));
            }
            Assert.IsTrue(sawExact && sawProxy, $"色数开关两分支都应命中(样本 12 种子×40 重洗),exact={sawExact} proxy={sawProxy}");
        }

        [Test]
        public void InvalidSpec_Throws()
        {
            // 生成规格防御:色数 <3 或区间倒挂应直接报错而非静默产出非法题
            Assert.Throws<ArgumentException>(() => WaterSortLevelGen.Generate(new WaterSortGenSpec { MinColors = 2, MaxColors = 2 }, 1));
            Assert.Throws<ArgumentException>(() => WaterSortLevelGen.Generate(new WaterSortGenSpec { MinColors = 6, MaxColors = 4 }, 1));
            Assert.Throws<ArgumentException>(() => WaterSortLevelGen.Generate(null, 1));
        }

        [Test]
        public void Codec_RoundTrip_BoardPreserved()
        {
            // 编码 → 解码应还原同题面;id/难度/实测步数原样透传(关卡固化管线的核心契约)
            var g = WaterSortLevelGen.Generate(Spec(WaterSortDifficulty.Hard, 10, 10, 5, 100), seed: 7300);
            Assert.IsTrue(g.Succeeded);
            var data = WaterSortLevelCodec.Encode(g.Board, id: 77, WaterSortDifficulty.Hard, g.MeasuredSteps);
            Assert.IsTrue(WaterSortLevelCodec.TryDecode(data, out var back));
            Assert.AreEqual(g.Board.EncodeKey(), back.EncodeKey(), "编解码须还原同题面");
            Assert.AreEqual(77, data.id);
            Assert.AreEqual(WaterSortDifficulty.Hard, data.difficulty);
            Assert.AreEqual(g.MeasuredSteps, data.measuredSteps);
        }

        [Test]
        public void Codec_JsonUtility_Serializable()
        {
            // 关卡数据最终以 JSON TextAsset 入库(JsonUtility 序列化),须真实往返无损
            var g = WaterSortLevelGen.Generate(Spec(WaterSortDifficulty.Medium, 4, 4, 5, 100), seed: 7400);
            Assert.IsTrue(g.Succeeded);
            var data = WaterSortLevelCodec.Encode(g.Board, id: 3, WaterSortDifficulty.Medium, g.MeasuredSteps);
            string json = JsonUtility.ToJson(data);
            Assert.IsFalse(string.IsNullOrEmpty(json));
            var back = JsonUtility.FromJson<WaterSortLevelData>(json);
            Assert.IsTrue(WaterSortLevelCodec.TryDecode(back, out var board));
            Assert.AreEqual(g.Board.EncodeKey(), board.EncodeKey());
            Assert.AreEqual(3, back.id);
            Assert.AreEqual(WaterSortDifficulty.Medium, back.difficulty);
            Assert.AreEqual(g.MeasuredSteps, back.measuredSteps);
            CollectionAssert.AreEqual(data.tubes, back.tubes);
        }

        [Test]
        public void Codec_CorruptedData_Rejected()
        {
            // 每日挑战兜底路径判定"损坏条目"用 TryDecode:长度不符/滴值越界/全空引用都应返回 false 而非抛异常
            var data = new WaterSortLevelData { id = 1, colors = 3, tubes = new[] { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3 } }; // 少一滴
            Assert.IsFalse(WaterSortLevelCodec.TryDecode(data, out _));
            data.tubes = new[] { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 9 }; // 滴值越界
            Assert.IsFalse(WaterSortLevelCodec.TryDecode(data, out _));
            data.tubes = null;
            Assert.IsFalse(WaterSortLevelCodec.TryDecode(data, out _));
            Assert.IsFalse(WaterSortLevelCodec.TryDecode(null, out _));
            data.colors = 0;
            data.tubes = new int[0];
            Assert.IsFalse(WaterSortLevelCodec.TryDecode(data, out _));
        }

        [Test]
        public void Codec_NonStandardShape_EncodeRejects()
        {
            // 编码只收"生成器产物形态"(满管在前的混合盘),非标准构造直接抛错,防止脏数据混入题库
            var nonFull = new WaterSortBoard(3, 2, new[] { 1, 1 }, new[] { 2, 2 }, new[] { 3, 3 });
            Assert.Throws<ArgumentException>(() => WaterSortLevelCodec.Encode(nonFull, 1, WaterSortDifficulty.Easy, 5));
            var filled = new WaterSortBoard(3, 2, new[] { 1, 1, 1, 1 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 }, new[] { 1 });
            Assert.Throws<ArgumentException>(() => WaterSortLevelCodec.Encode(filled, 1, WaterSortDifficulty.Easy, 5));
        }
    }
}
