using NUnit.Framework;
using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    [TestFixture]
    public class HintEngineTests
    {
        [Test]
        public void GetHint_OnClassicPuzzle_ReturnsLegalMove()
        {
            Assert.IsTrue(HintEngine.GetHint(Fixtures.ClassicPuzzle, out var hint));

            // 提示值应等于该格在已知解中的值
            Assert.AreEqual(Fixtures.ClassicSolution[hint.Row, hint.Col], hint.Value);

            // 该步在当前盘面应合法
            Assert.IsTrue(Fixtures.ClassicPuzzle.IsValidPlacement(hint.Row, hint.Col, hint.Value));
        }

        [Test]
        public void GetHint_OnSolvedBoard_ReturnsFalse()
        {
            Assert.IsFalse(HintEngine.GetHint(Fixtures.ClassicSolution, out _));
        }

        [Test]
        public void GetHint_OnNakedSingleBoard_ReturnsNakedSingle()
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

            Assert.IsTrue(HintEngine.GetHint(b, out var hint));
            Assert.AreEqual(Technique.NakedSingle, hint.Technique);
            Assert.AreEqual(0, hint.Row);
            Assert.AreEqual(8, hint.Col);
            Assert.AreEqual(9, hint.Value);
        }
    }
}
