using NUnit.Framework;
using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    [TestFixture]
    public class LogicSolverTests
    {
        [Test]
        public void TryFindSingle_NakedSingle()
        {
            var b = Fixtures.FromString(
                "123456780",
                "000000000",
                "000000000",
                "000000000",
                "000000000",
                "000000000",
                "000000000",
                "000000000",
                "000000000");

            Assert.IsTrue(LogicSolver.TryFindSingle(b, out int index, out int value, out var technique));
            Assert.AreEqual(Technique.NakedSingle, technique);
            Assert.AreEqual(SudokuBoard.Index(0, 8), index);
            Assert.AreEqual(9, value);
        }

        [Test]
        public void TryFindSingle_HiddenSingle()
        {
            // 行 0 缺 7/8/9,其中 7 被 col7、col8 上的 7 挡住,
            // 只能落在 (0,6),而该格候选多于一个 => 隐性唯一而非显性唯一。
            var b = Fixtures.FromString(
                "123456000",
                "000000000",
                "000000000",
                "000000070",
                "000000000",
                "000000000",
                "000000007",
                "000000000",
                "000000000");

            Assert.IsTrue(LogicSolver.TryFindSingle(b, out int index, out int value, out var technique));
            Assert.AreEqual(Technique.HiddenSingle, technique);
            Assert.AreEqual(SudokuBoard.Index(0, 6), index);
            Assert.AreEqual(7, value);
        }

        [Test]
        public void TryFindSingle_SolvedBoard_ReturnsFalse()
        {
            Assert.IsFalse(LogicSolver.TryFindSingle(Fixtures.ClassicSolution, out _, out _, out _));
        }
    }
}
