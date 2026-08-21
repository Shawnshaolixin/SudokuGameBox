using NUnit.Framework;
using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    [TestFixture]
    public class SudokuSolverTests
    {
        [Test]
        public void Solve_ClassicPuzzle_MatchesKnownSolution()
        {
            Assert.IsTrue(SudokuSolver.Solve(Fixtures.ClassicPuzzle, out var solution));
            Assert.AreEqual(Fixtures.ClassicSolution, solution);
        }

        [Test]
        public void Solve_EmptyBoard_ReturnsSolvedBoard()
        {
            Assert.IsTrue(SudokuSolver.Solve(new SudokuBoard(), out var solution));
            Assert.IsTrue(solution.IsSolved());
        }

        [Test]
        public void Solve_ConflictingBoard_ReturnsFalse()
        {
            var b = new SudokuBoard();
            b[0, 0] = 5;
            b[0, 1] = 5; // 行冲突,无解
            Assert.IsFalse(SudokuSolver.Solve(b, out _));
        }

        [Test]
        public void CountSolutions_ClassicPuzzle_IsOne()
        {
            Assert.AreEqual(1, SudokuSolver.CountSolutions(Fixtures.ClassicPuzzle, 2));
        }

        [Test]
        public void HasUniqueSolution_ClassicPuzzle_True()
        {
            Assert.IsTrue(SudokuSolver.HasUniqueSolution(Fixtures.ClassicPuzzle));
        }

        [Test]
        public void HasUniqueSolution_EmptyBoard_False()
        {
            Assert.IsFalse(SudokuSolver.HasUniqueSolution(new SudokuBoard()));
        }

        [Test]
        public void CountSolutions_EmptyBoard_HitsLimit()
        {
            Assert.AreEqual(2, SudokuSolver.CountSolutions(new SudokuBoard(), 2));
        }

        [Test]
        public void CountSolutions_ConflictingBoard_Zero()
        {
            var b = new SudokuBoard();
            b[0, 0] = 5;
            b[0, 1] = 5;
            Assert.AreEqual(0, SudokuSolver.CountSolutions(b, 2));
        }
    }
}
