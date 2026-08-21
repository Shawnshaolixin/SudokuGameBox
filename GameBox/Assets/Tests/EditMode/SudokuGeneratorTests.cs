using NUnit.Framework;
using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    [TestFixture]
    public class SudokuGeneratorTests
    {
        [Test]
        public void GenerateSolvedBoard_IsSolved()
        {
            var g = new SudokuGenerator(12345);
            for (int i = 0; i < 20; i++)
                Assert.IsTrue(g.GenerateSolvedBoard().IsSolved());
        }

        [TestCase((int)Difficulty.Easy)]
        [TestCase((int)Difficulty.Medium)]
        [TestCase((int)Difficulty.Hard)]
        public void Generate_ProducesValidUniquePuzzle(int difficultyInt)
        {
            var difficulty = (Difficulty)difficultyInt;
            var g = new SudokuGenerator(42);

            for (int i = 0; i < 3; i++)
            {
                var p = g.Generate(difficulty);

                // 唯一解
                Assert.IsTrue(SudokuSolver.HasUniqueSolution(p.Puzzle));

                // 解与返回的解一致
                Assert.IsTrue(SudokuSolver.Solve(p.Puzzle, out var solved));
                Assert.AreEqual(p.Solution, solved);

                // 谜题是解的子集(每个给定数都等于解)
                for (int idx = 0; idx < SudokuBoard.CellCount; idx++)
                    if (p.Puzzle[idx] != 0)
                        Assert.AreEqual(p.Solution[idx], p.Puzzle[idx]);

                // 提示数落在合理区间(阶段 A 的难度即由提示数档位决定)
                Assert.GreaterOrEqual(p.ClueCount, 22);
                Assert.LessOrEqual(p.ClueCount, 46);
            }
        }

        [Test]
        public void Generate_EasyHasMoreCluesThanHard()
        {
            var g = new SudokuGenerator(7);
            var easy = g.Generate(Difficulty.Easy);
            var hard = g.Generate(Difficulty.Hard);
            Assert.Greater(easy.ClueCount, hard.ClueCount);
        }

        [Test]
        public void Generate_SameSeed_SamePuzzle()
        {
            var a = new SudokuGenerator(99).Generate(Difficulty.Medium);
            var b = new SudokuGenerator(99).Generate(Difficulty.Medium);
            Assert.AreEqual(a.Puzzle, b.Puzzle);
            Assert.AreEqual(a.Solution, b.Solution);
        }
    }
}
