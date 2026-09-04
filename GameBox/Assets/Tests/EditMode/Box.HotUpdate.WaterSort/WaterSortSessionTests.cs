using System;
using NUnit.Framework;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort.Tests
{
    /// <summary>
    /// 会话状态机用例(M1.2 骨架 + M1.4 提示/额外空瓶/类型化撤销栈回归):
    /// 金币扣减在视图层(不入会话),本组只测局内语义——盘面推进/撤销反演/每关消耗计数与上限/
    /// 重开复位。全部走真实生成器产局(确定性种子),覆盖"生成 → 开局"的完整链。
    /// </summary>
    public class WaterSortSessionTests
    {
        const int SeedBase = 0x5757; // 与编辑器生成器同种子域(0x5757 + levelNo*7919 + k 平移重试)

        static WaterSortLevelData GenerateLevel(int levelNo)
        {
            var spec = WaterSortGenDefaults.SpecForIndex(levelNo);
            for (int k = 0; k < 30; k++)
            {
                var r = WaterSortLevelGen.Generate(spec, SeedBase + levelNo * 7919 + k);
                if (!r.Succeeded) continue;
                return WaterSortLevelCodec.Encode(r.Board, levelNo, spec.Difficulty, r.MeasuredSteps);
            }
            throw new InvalidOperationException($"种子域内未生成出可用关(levelNo={levelNo})");
        }

        /// <summary>开局一个真实生成关(默认第 1 关 Easy 段,种子确定可复现)。</summary>
        static WaterSortSession StartGenerated(int levelNo = 1)
        {
            var s = new WaterSortSession(false);
            Assert.IsTrue(s.StartLevel(GenerateLevel(levelNo)), "生成关开局失败(解码链断)");
            return s;
        }

        /// <summary>终态盘面(每色一管满)作为关卡源:不可走(空解),用于"已过关不可提示"用例。</summary>
        static WaterSortLevelData SolvedLevel()
        {
            var board = new WaterSortBoard(2, 2); // ctor 即终态:色管全满 + 2 空管
            return WaterSortLevelCodec.Encode(board, 1, WaterSortDifficulty.Easy, 0);
        }

        [Test]
        public void Pour_ThenUndo_RestoresBoardMoveCount()
        {
            var s = StartGenerated();
            var initial = s.Board;
            var move = initial.LegalMoves()[0];
            Assert.IsTrue(s.TryPour(move.Src, move.Dst), "合法倒水被拒");
            Assert.AreEqual(1, s.MoveCount);
            Assert.AreNotSame(initial, s.Board, "倒水后必须是新盘面(不可变)");
            Assert.IsTrue(s.Undo(), "撤销失败");
            Assert.AreSame(initial, s.Board, "撤销应回到操作前同一快照实例");
            Assert.AreEqual(0, s.MoveCount, "撤销倒水必须回退步数");
        }

        [Test]
        public void AddExtraTube_ExtendsBoard_UndoRemoves_ReAddAllowed()
        {
            var s = StartGenerated();
            int t0 = s.Board.TubeCount;
            Assert.IsTrue(s.TryAddExtraTube(), "加管被拒");
            Assert.AreEqual(t0 + 1, s.Board.TubeCount, "加管后应多 1 支管");
            Assert.AreEqual(0, s.Board.TopCount(s.Board.TubeCount - 1), "新管必须是空管(末支)");
            Assert.AreEqual(1, s.ExtraTubesUsed);
            Assert.AreEqual(0, s.MoveCount, "加管不计步");

            Assert.IsTrue(s.Undo(), "撤销加管失败");
            Assert.AreEqual(t0, s.Board.TubeCount, "撤销加管应移除空管回原始盘面");
            Assert.AreEqual(0, s.ExtraTubesUsed, "撤销加管应回退计数(管已移除,可再购)");

            Assert.IsTrue(s.TryAddExtraTube(), "计数回退后应可再次加管(付费换租语义)");
            Assert.AreEqual(1, s.ExtraTubesUsed);
        }

        [Test]
        public void ExtraTube_RespectsPerLevelLimit()
        {
            var s = StartGenerated();
            // 默认上限 2/关(WaterSortConfig;用例锁死当前默认,改配置表需同步改此处意图)
            Assert.IsTrue(s.TryAddExtraTube());
            Assert.IsTrue(s.TryAddExtraTube());
            Assert.IsFalse(s.TryAddExtraTube(), "第 3 次加管必须被上限拦截");
            Assert.AreEqual(2, s.ExtraTubesUsed);
        }

        [Test]
        public void Hint_AppliesOneSolutionMove_CountsUpToPerLevelLimit()
        {
            var s = StartGenerated();
            var initial = s.Board;
            // Easy 段 ≤10 关为 3~4 色 5~15 步(IDA* 精确),单次提示不可能直接解完,便于数满 3 次
            Assert.IsTrue(s.TryHint(), "可解盘面提示被拒");
            Assert.AreEqual(1, s.HintsUsed);
            Assert.AreEqual(1, s.MoveCount, "提示 = 自动走出第一步,必须计步");
            Assert.AreNotSame(initial, s.Board, "提示后盘面必须推进");

            Assert.IsTrue(s.TryHint());
            Assert.IsTrue(s.TryHint());
            Assert.AreEqual(3, s.HintsUsed, "默认上限 3/关(WaterSortConfig)");
            Assert.IsFalse(s.TryHint(), "超过每关上限后提示必须拒绝");
            Assert.AreEqual(3, s.HintsUsed);
            Assert.AreEqual(3, s.MoveCount, "被拒的提示不得推进盘面");
        }

        [Test]
        public void Hint_OnSolvedBoard_ReturnsFalse()
        {
            var s = new WaterSortSession(false);
            Assert.IsTrue(s.StartLevel(SolvedLevel()), "终态关开局失败");
            Assert.IsTrue(s.Board.IsSolved());
            Assert.IsFalse(s.TryHint(), "已过关盘面不得再提示(无解路径)");
            Assert.AreEqual(0, s.HintsUsed);
            Assert.AreEqual(0, s.MoveCount);
        }

        [Test]
        public void Restart_ResetsConsumptionCountersAndBoardShape()
        {
            var s = StartGenerated();
            int t0 = s.Board.TubeCount;
            Assert.IsTrue(s.TryAddExtraTube());
            Assert.IsTrue(s.TryHint());
            Assert.IsTrue(s.MoveCount > 0);

            s.Restart();
            Assert.AreEqual(t0, s.Board.TubeCount, "重开必须回到关卡原始形态(加管不写回 LevelData)");
            Assert.AreEqual(0, s.ExtraTubesUsed, "重开复位加管计数(已付金币不退,可再购)");
            Assert.AreEqual(0, s.HintsUsed);
            Assert.AreEqual(0, s.MoveCount);
        }

        [Test]
        public void StartLevel_RejectsMalformedData()
        {
            var s = new WaterSortSession(false);
            // 形状不符(colors=1 需 4 滴,给 3):TryDecode 拒收 → StartLevel 返回 false 不动状态
            var bad = new WaterSortLevelData { id = 99, colors = 1, tubes = new[] { 1, 1, 1 } };
            Assert.IsFalse(s.StartLevel(bad));
            Assert.IsFalse(s.IsInLevel);
            Assert.IsNull(s.Board);
        }
    }
}
