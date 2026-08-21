using System;
using NUnit.Framework;
using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    [TestFixture]
    public class SudokuBoardTests
    {
        [Test]
        public void Index_MapsRowColToLinearIndex()
        {
            Assert.AreEqual(0, SudokuBoard.Index(0, 0));
            Assert.AreEqual(8, SudokuBoard.Index(0, 8));
            Assert.AreEqual(9, SudokuBoard.Index(1, 0));
            Assert.AreEqual(80, SudokuBoard.Index(8, 8));
        }

        [Test]
        public void RowColOf_AreInverseOfIndex()
        {
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                {
                    int i = SudokuBoard.Index(r, c);
                    Assert.AreEqual(r, SudokuBoard.RowOf(i));
                    Assert.AreEqual(c, SudokuBoard.ColOf(i));
                }
        }

        [Test]
        public void BoxOf_ReturnsCorrectBox()
        {
            Assert.AreEqual(0, SudokuBoard.BoxOf(0, 0));
            Assert.AreEqual(1, SudokuBoard.BoxOf(0, 3));
            Assert.AreEqual(4, SudokuBoard.BoxOf(4, 4));
            Assert.AreEqual(8, SudokuBoard.BoxOf(8, 8));
        }

        [Test]
        public void SetGet_RoundTrips()
        {
            var b = new SudokuBoard();
            b.Set(2, 5, 7);
            Assert.AreEqual(7, b.Get(2, 5));
            b.Clear(2, 5);
            Assert.AreEqual(0, b.Get(2, 5));
        }

        [Test]
        public void Constructor_ThrowsOnWrongLength()
        {
            Assert.Throws<ArgumentException>(() => new SudokuBoard(new int[80]));
            Assert.Throws<ArgumentException>(() => new SudokuBoard(new int[82]));
        }

        [Test]
        public void Constructor_ClonesInputArray()
        {
            var src = new int[81];
            src[0] = 5;
            var b = new SudokuBoard(src);
            src[0] = 9; // 修改源数组不应影响棋盘
            Assert.AreEqual(5, b[0]);
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var b = new SudokuBoard();
            b[0] = 3;
            var c = b.Clone();
            c[0] = 4;
            Assert.AreEqual(3, b[0]);
            Assert.AreEqual(4, c[0]);
        }

        [Test]
        public void CountGivens_CountsNonZero()
        {
            Assert.AreEqual(30, Fixtures.ClassicPuzzle.CountGivens());
        }

        [Test]
        public void IsValidPlacement_RejectsRowConflict()
        {
            var b = new SudokuBoard();
            b[0, 0] = 5;
            Assert.IsFalse(b.IsValidPlacement(0, 1, 5));
        }

        [Test]
        public void IsValidPlacement_RejectsColumnConflict()
        {
            var b = new SudokuBoard();
            b[0, 0] = 5;
            Assert.IsFalse(b.IsValidPlacement(1, 0, 5));
        }

        [Test]
        public void IsValidPlacement_RejectsBoxConflict()
        {
            var b = new SudokuBoard();
            b[0, 0] = 5;
            Assert.IsFalse(b.IsValidPlacement(1, 1, 5));
        }

        [Test]
        public void IsValidPlacement_AcceptsValid()
        {
            var b = new SudokuBoard();
            b[0, 1] = 5;
            Assert.IsTrue(b.IsValidPlacement(0, 0, 3));
        }

        [Test]
        public void IsSolved_EmptyBoardIsFalse()
        {
            Assert.IsFalse(new SudokuBoard().IsSolved());
        }

        [Test]
        public void IsSolved_FullValidBoardIsTrue()
        {
            Assert.IsTrue(Fixtures.ClassicSolution.IsSolved());
        }

        [Test]
        public void IsSolved_FullBoardWithConflictIsFalse()
        {
            var b = Fixtures.ClassicSolution.Clone();
            b[0, 1] = b[0, 0]; // 制造行冲突
            Assert.IsFalse(b.IsSolved());
        }

        [Test]
        public void HasConflicts_DetectsRowDuplicate()
        {
            var b = new SudokuBoard();
            b[0, 0] = 5;
            b[0, 3] = 5;
            Assert.IsTrue(b.HasConflicts());
        }

        [Test]
        public void GetCandidates_OnEmptyBoardIsAllDigits()
        {
            var c = new SudokuBoard().GetCandidates(0, 0);
            Assert.AreEqual(9, c.Count);
        }

        [Test]
        public void GetCandidates_ExcludesRowDigits()
        {
            var b = new SudokuBoard();
            for (int c = 0; c < 8; c++) b[0, c] = c + 1; // 行里已放 1~8
            var cands = b.GetCandidates(0, 8);
            CollectionAssert.AreEqual(new[] { 9 }, cands);
        }
    }
}
