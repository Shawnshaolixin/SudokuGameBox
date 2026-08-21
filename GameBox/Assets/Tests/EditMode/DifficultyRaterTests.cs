using NUnit.Framework;
using Sudoku.Core;

namespace Sudoku.Core.Tests
{
    [TestFixture]
    public class DifficultyRaterTests
    {
        [Test]
        public void MapToDifficulty_SinglesOnly_HighGivens_IsBeginner()
        {
            Assert.AreEqual(Difficulty.Beginner, DifficultyRater.MapToDifficulty(Technique.HiddenSingle, 50));
        }

        [Test]
        public void MapToDifficulty_SinglesOnly_MediumGivens_IsEasy()
        {
            Assert.AreEqual(Difficulty.Easy, DifficultyRater.MapToDifficulty(Technique.NakedSingle, 40));
        }

        [Test]
        public void MapToDifficulty_Backtracking_LowGivens_IsMaster()
        {
            Assert.AreEqual(Difficulty.Master, DifficultyRater.MapToDifficulty(Technique.Backtracking, 20));
        }

        [Test]
        public void MapToDifficulty_Backtracking_MidGivens_IsExpert()
        {
            Assert.AreEqual(Difficulty.Expert, DifficultyRater.MapToDifficulty(Technique.Backtracking, 28));
        }

        [Test]
        public void Rate_EmptyBoard_IsMaster()
        {
            Assert.AreEqual(Difficulty.Master, DifficultyRater.Rate(new SudokuBoard()));
        }

        [Test]
        public void Rate_SolvedBoard_IsBeginner()
        {
            Assert.AreEqual(Difficulty.Beginner, DifficultyRater.Rate(Fixtures.ClassicSolution));
        }

        [Test]
        public void Rate_ClassicPuzzle_IsAtLeastMedium()
        {
            var d = DifficultyRater.Rate(Fixtures.ClassicPuzzle);
            Assert.GreaterOrEqual((int)d, (int)Difficulty.Medium);
        }
    }
}
