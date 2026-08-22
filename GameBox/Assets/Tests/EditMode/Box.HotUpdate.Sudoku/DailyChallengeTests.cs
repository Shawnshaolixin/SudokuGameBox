using System;
using System.Diagnostics;
using Box.Services;
using NUnit.Framework;
using Sudoku.Core;
using UnityEngine;

namespace Box.HotUpdate.Sudoku.Tests
{
    /// <summary>
    /// 每日挑战(Phase 4 4-4 + Phase 5 5-1):日期种子确定性/完成标记/最佳成绩/v0→v1 惰性迁移。
    /// 玩法侧走 ISaveService 分区(Fake 内存实现,断言分区内容);PlayerPrefs 旧键用例用唯一 seed + TearDown 清理。
    /// 加密/备份/恢复等壳层职责由 Box.Gameplay.Tests 覆盖;此处只验证分区使用与迁移逻辑。
    /// </summary>
    public class DailyChallengeTests
    {
        const int UniqueSeed = 20260822; // 固定唯一 seed,用完即清
        FakeSaveService _save;

        [SetUp]
        public void SetUp()
        {
            _save = new FakeSaveService();
            ServiceLocator.Register(_save, new FakeSettingsService());
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Reset(); // 隔离:不污染其它测试
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

        // ---- 完成标记/最佳成绩(Phase 5:走 ISaveService 分区,D-7) ----

        [Test]
        public void Completion_Flag_RoundTrip()
        {
            Assert.IsFalse(DailyChallengeStore.IsCompleted(UniqueSeed), "默认未完成");
            DailyChallengeStore.MarkCompleted(UniqueSeed);
            Assert.IsTrue(DailyChallengeStore.IsCompleted(UniqueSeed));

            // 已落盘到模块分区(壳层测试覆盖加密/备份,此处断言分区内容,职责分离)
            var saved = _save.RawModuleJson("sudoku");
            Assert.IsNotNull(saved, "MarkCompleted 必须写入 modules.sudoku 分区");
            StringAssert.Contains("\"seed\":" + UniqueSeed, saved);
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

        // ---- Phase 5 v0→v1 惰性迁移 ----

        [Test]
        public void Migration_V0_Legacy_Keys_Into_Module_And_Kept_For_Rollback()
        {
            // 模拟 v0 遗留:旧 PlayerPrefs 键(老版本已完成 + 300s 最佳)
            PlayerPrefs.SetInt("sudoku.daily.done." + UniqueSeed, 1);
            PlayerPrefs.SetInt("sudoku.daily.best." + UniqueSeed, 300);

            // 首次访问即触发迁移(惰性按 seed)
            Assert.IsTrue(DailyChallengeStore.IsCompleted(UniqueSeed));
            Assert.AreEqual(300, DailyChallengeStore.GetBestSeconds(UniqueSeed));

            // 分区已有记录(迁移落盘)
            var data = _save.GetModule<SudokuModuleData>("sudoku");
            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.daily.Count, "该 seed 应恰好一条记录");
            Assert.AreEqual(UniqueSeed, data.daily[0].seed);
            Assert.IsTrue(data.daily[0].done);
            Assert.AreEqual(300, data.daily[0].bestSeconds);

            // §8.2:旧键保留一个版本回滚,不删除
            Assert.AreEqual(1, PlayerPrefs.GetInt("sudoku.daily.done." + UniqueSeed, -1));
            Assert.AreEqual(300, PlayerPrefs.GetInt("sudoku.daily.best." + UniqueSeed, -1));
        }

        [Test]
        public void Migration_Does_Not_Overwrite_Newer_Module_Data()
        {
            // 先由新版本写入更优成绩(200s)
            DailyChallengeStore.SetBestSeconds(UniqueSeed, 200);

            // 旧键虽然存在(更老的完成记录),但分区已有记录 → 不覆盖(新数据为权威)
            PlayerPrefs.SetInt("sudoku.daily.done." + UniqueSeed, 1);
            PlayerPrefs.SetInt("sudoku.daily.best." + UniqueSeed, 300);

            Assert.AreEqual(200, DailyChallengeStore.GetBestSeconds(UniqueSeed));
            var data = _save.GetModule<SudokuModuleData>("sudoku");
            Assert.AreEqual(1, data.daily.Count, "迁移不重复插入");
            Assert.AreEqual(200, data.daily[0].bestSeconds);
        }

        [Test]
        public void Unregistered_Services_NoThrow()
        {
            ServiceLocator.Reset(); // 服务未注册(异常上下文/旧版兼容)
            Assert.AreEqual(0, DailyChallengeStore.GetBestSeconds(UniqueSeed));
            DailyChallengeStore.MarkCompleted(UniqueSeed); // 不应抛
            Assert.IsFalse(DailyChallengeStore.IsCompleted(UniqueSeed));
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
