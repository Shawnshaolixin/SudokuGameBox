using System;
using System.Diagnostics;
using NUnit.Framework;
using Sudoku.Core;
using UnityEngine;

namespace Box.HotUpdate.Sudoku.Tests
{
    /// <summary>
    /// 每日挑战(Phase 4 4-4 完整版):日期种子确定性/完成标记/最佳成绩。
    /// PlayerPrefs 用例用唯一 seed + TearDown 清理,避免污染。
    /// </summary>
    public class DailyChallengeTests
    {
        const int UniqueSeed = 20260822; // 固定唯一 seed,用完即清

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey("sudoku.daily.done." + UniqueSeed);
            PlayerPrefs.DeleteKey("sudoku.daily.best." + UniqueSeed);
        }

        // ---- 日期种子 ----

        [Test]
        public void SeedFor_Same_Date_Same_Seed()
        {
            Assert.AreEqual(DailyChallengeStore.SeedFor(new DateTime(2026, 8, 22)),
                DailyChallengeStore.SeedFor(new DateTime(2026, 8, 22)));
        }

        [Test]
        public void SeedFor_Different_Dates_Differ()
        {
            Assert.AreNotEqual(DailyChallengeStore.SeedFor(new DateTime(2026, 8, 22)),
                DailyChallengeStore.SeedFor(new DateTime(2026, 8, 23)));
            Assert.AreEqual(20260822, DailyChallengeStore.SeedFor(new DateTime(2026, 8, 22)), "yyyyMMdd 格式");
        }

        // ---- 生成确定性 ----

        [Test]
        public void CreateDaily_Same_Seed_Same_Puzzle()
        {
            var a = PuzzleFactory.CreateDaily(UniqueSeed);
            var b = PuzzleFactory.CreateDaily(UniqueSeed);

            CollectionAssert.AreEqual(a.Puzzle.ToArray(), b.Puzzle.ToArray(), "同种子每日题必须完全一致");
            Assert.AreEqual(Difficulty.Medium, a.Difficulty);
            Assert.IsTrue(a.Solution.IsSolved());
        }

        [Test]
        public void CreateDaily_Unique_Solution()
        {
            var puzzle = PuzzleFactory.CreateDaily(UniqueSeed);
            Assert.IsTrue(SudokuSolver.HasUniqueSolution(puzzle.Puzzle), "每日题必须唯一解");
        }

        // ---- 完成标记/最佳成绩(临时 PlayerPrefs) ----

        [Test]
        public void Completion_Flag_RoundTrip()
        {
            Assert.IsFalse(DailyChallengeStore.IsCompleted(UniqueSeed), "默认未完成");
            DailyChallengeStore.MarkCompleted(UniqueSeed);
            Assert.IsTrue(DailyChallengeStore.IsCompleted(UniqueSeed));
        }

        [Test]
        public void BestSeconds_Only_Updates_Better()
        {
            Assert.AreEqual(0, DailyChallengeStore.GetBestSeconds(UniqueSeed), "默认 0=未完成过");

            DailyChallengeStore.SetBestSeconds(UniqueSeed, 300);
            Assert.AreEqual(300, DailyChallengeStore.GetBestSeconds(UniqueSeed));

            DailyChallengeStore.SetBestSeconds(UniqueSeed, 500); // 更差:不更新
            Assert.AreEqual(300, DailyChallengeStore.GetBestSeconds(UniqueSeed));

            DailyChallengeStore.SetBestSeconds(UniqueSeed, 240); // 更优:更新
            Assert.AreEqual(240, DailyChallengeStore.GetBestSeconds(UniqueSeed));
        }

        // ---- 性能验收:生成 <200ms(全难度) ----

        [Test]
        public void Generation_Under_200ms_All_Difficulties()
        {
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
            {
                var sw = Stopwatch.StartNew();
                var puzzle = PuzzleFactory.Create(d);
                sw.Stop();

                Assert.Less(sw.ElapsedMilliseconds, 200,
                    $"{d} 生成耗时 {sw.ElapsedMilliseconds}ms 超预算(10 文档验收 <200ms)");
                Assert.IsTrue(puzzle.Solution.IsSolved(), $"{d} 解必须合法");
                Assert.IsTrue(SudokuSolver.HasUniqueSolution(puzzle.Puzzle), $"{d} 谜题必须唯一解");
            }
        }
    }
}
