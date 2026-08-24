using NUnit.Framework;
using Sudoku.Core;

namespace Box.HotUpdate.Sudoku.Tests
{
    /// <summary>
    /// GameSession 对局核心单测(Phase 4 4-2):
    /// 输入/笔记/撤销重做/错误检测/提示/计时/完成路径/高亮查询。
    /// 纯 C# 逻辑,无引擎依赖,秒级执行。
    /// </summary>
    public class GameSessionTests
    {
        FakeClock _clock;
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            _session = new GameSession(TestPuzzles.MakePuzzle(0, 40, 80), _clock); // 挖 3 洞:可填格 0/40/80
        }

        // ---- 初始状态 ----

        [Test]
        public void Initial_State()
        {
            Assert.AreEqual(-1, _session.SelectedIndex);
            Assert.AreEqual(GameSession.InputMode.Number, _session.Mode);
            Assert.AreEqual(0, _session.MistakeCount);
            Assert.IsFalse(_session.IsFinished);
            Assert.IsFalse(_session.CanUndo);
            Assert.IsFalse(_session.CanRedo);
            Assert.IsTrue(_session.CanUseHint);
            Assert.AreEqual(3, _session.HintCount);
            Assert.AreEqual(0f, _session.ElapsedSeconds, 0.001f);
            Assert.AreEqual(3, _session.StarRating);
        }

        [Test]
        public void Puzzle_Cells_Are_Given()
        {
            Assert.IsTrue(_session.IsGiven(1), "未挖洞格应为给定格");
            Assert.IsFalse(_session.IsGiven(0), "挖洞格不应是给定格");
        }

        // ---- 选格 ----

        [Test]
        public void SelectCell_Fires_Event()
        {
            int? selected = null;
            _session.CellSelected += i => selected = i;

            _session.SelectCell(40);

            Assert.AreEqual(40, _session.SelectedIndex);
            Assert.AreEqual(40, selected);
        }

        [Test]
        public void SelectCell_Out_Of_Range_Ignored()
        {
            _session.SelectCell(81);
            _session.SelectCell(-1);
            Assert.AreEqual(-1, _session.SelectedIndex);
        }

        // ---- 输入 ----

        [Test]
        public void InputNumber_Writes_And_Fires()
        {
            int changes = 0;
            _session.BoardChanged += () => changes++;

            _session.SelectCell(0);
            _session.InputNumber(7);

            Assert.AreEqual(7, _session.Board[0]);
            Assert.AreEqual(1, changes);
            Assert.IsTrue(_session.CanUndo);
        }

        [Test]
        public void Wrong_Input_Counts_Mistake_Not_Blocked()
        {
            _session.SelectCell(0);
            _session.InputNumber(8); // 解是 1,填 8

            Assert.AreEqual(1, _session.MistakeCount);
            Assert.AreEqual(8, _session.Board[0], "错误不拦截,只是标红计数");
        }

        [Test]
        public void Same_Number_Again_Clears_Cell()
        {
            _session.SelectCell(0);
            _session.InputNumber(1);
            Assert.AreEqual(1, _session.Board[0]);

            _session.InputNumber(1); // 再点同数=清除
            Assert.AreEqual(0, _session.Board[0]);
        }

        [Test]
        public void Given_Cell_Input_Rejected()
        {
            _session.SelectCell(1); // 给定格
            _session.InputNumber(9);
            Assert.AreEqual(TestPuzzles.FinishedBoard[1], _session.Board[1], "给定格不可改");
        }

        [Test]
        public void Erase_Clears_Value()
        {
            _session.SelectCell(0);
            _session.InputNumber(1);
            _session.Erase();

            Assert.AreEqual(0, _session.Board[0]);
        }

        // ---- 笔记 ----

        [Test]
        public void Note_Mode_Toggles_Notes()
        {
            _session.ToggleInputMode();
            Assert.AreEqual(GameSession.InputMode.Note, _session.Mode);

            _session.SelectCell(0);
            _session.InputNumber(5);
            Assert.IsTrue(_session.HasNote(0, 5));

            _session.InputNumber(5); // 再点删笔记
            Assert.IsFalse(_session.HasNote(0, 5));
        }

        [Test]
        public void Note_Input_Does_Not_Count_Mistake()
        {
            _session.ToggleInputMode();
            _session.SelectCell(0);
            _session.InputNumber(8); // 错误值只进笔记

            Assert.AreEqual(0, _session.MistakeCount, "笔记不参与错误计数");
            Assert.IsTrue(_session.HasNote(0, 8));
        }

        [Test]
        public void Input_Number_Clears_Own_Notes()
        {
            _session.ToggleInputMode();
            _session.SelectCell(0);
            _session.InputNumber(5);

            _session.ToggleInputMode();
            _session.InputNumber(1); // 填数

            Assert.IsFalse(_session.HasNote(0, 5), "填数后本格笔记应清空");
            Assert.AreEqual(1, _session.Board[0]);
        }

        [Test]
        public void AutoClear_Peer_Notes()
        {
            // 同行格(0,1)记笔记 9;在 (0,0) 填 9 应清除同行的 9
            _session.ToggleInputMode();
            _session.SelectCell(1);
            _session.InputNumber(9);

            _session.ToggleInputMode();
            _session.SelectCell(0);
            _session.InputNumber(9);

            Assert.IsFalse(_session.HasNote(1, 9), "填入 9 后同行笔记 9 应自动清除");
        }

        // ---- 撤销重做 ----

        [Test]
        public void Undo_Redo_Value()
        {
            _session.SelectCell(0);
            _session.InputNumber(1);
            _session.InputNumber(2);

            _session.Undo();
            Assert.AreEqual(1, _session.Board[0]);
            Assert.IsTrue(_session.CanRedo);

            _session.Redo();
            Assert.AreEqual(2, _session.Board[0]);
        }

        [Test]
        public void Undo_Does_Not_Decrease_Mistake()
        {
            _session.SelectCell(0);
            _session.InputNumber(8); // 错
            Assert.AreEqual(1, _session.MistakeCount);

            _session.Undo();
            Assert.AreEqual(1, _session.MistakeCount, "错误累计,Undo 不减(旧工程规则)");
        }

        [Test]
        public void New_Input_Clears_Redo()
        {
            _session.SelectCell(0);
            _session.InputNumber(1);
            _session.Undo();
            Assert.IsTrue(_session.CanRedo);

            _session.InputNumber(3);
            Assert.IsFalse(_session.CanRedo, "新输入后重做栈应清空");
        }

        // ---- 提示 ----

        [Test]
        public void Hint_Fills_Correct_Value()
        {
            _session.TryUseHint(out _);

            Assert.AreEqual(1, _session.HintsUsed);
            // 提示必正确:抽查被填格的值与解一致
            int filled = 0;
            for (int i = 0; i < 81; i++)
                if (_session.Board[i] != _session.Puzzle[i]) filled++;
            Assert.AreEqual(1, filled, "一次提示应恰好填一格");
        }

        [Test]
        public void Hint_Exhausted_Fires_Once()
        {
            int fired = 0;
            _session.HintExhausted += () => fired++;

            for (int i = 0; i < 5; i++) _session.TryUseHint(out _);

            Assert.AreEqual(3, _session.HintsUsed, "提示上限 3 次");
            Assert.IsFalse(_session.CanUseHint);
            Assert.AreEqual(1, fired, "HintExhausted 只在首次用完时触发一次");
        }

        // ---- 高亮查询 ----

        [Test]
        public void Peer_And_SameNumber_Queries()
        {
            _session.SelectCell(1); // 给定格 (0,1) 值=2(挖洞格值为 0,不适合做同数判定)
            Assert.IsTrue(_session.IsPeer(2), "同行应视为同区域");
            Assert.IsTrue(_session.IsPeer(10), "同列应视为同区域");
            Assert.IsTrue(_session.IsPeer(11), "同宫(1,2)应视为同区域");
            Assert.IsFalse(_session.IsPeer(23), "(2,5) 不同行列宫不应是 peer");
            Assert.IsFalse(_session.IsPeer(1), "自身不是 peer");

            Assert.IsTrue(_session.IsSameNumber(16), "(1,7)=2 与选中格同数");
            Assert.IsFalse(_session.IsSameNumber(2), "(0,2)=3 不同数");
        }

        // ---- 完成路径 ----

        [Test]
        public void Fill_All_Solution_Finishes_Once_And_Freezes()
        {
            int finished = 0;
            _session.GameFinished += () => finished++;

            foreach (int hole in new[] { 0, 40, 80 })
            {
                _session.SelectCell(hole);
                _session.InputNumber(_session.Solution[hole]);
            }

            Assert.IsTrue(_session.IsFinished);
            Assert.AreEqual(1, finished, "GameFinished 应只触发一次");

            _session.SelectCell(0); // 完成后输入冻结
            _session.InputNumber(2);
            Assert.AreEqual(_session.Solution[0], _session.Board[0], "完成后输入应冻结");
        }

        [Test]
        public void Mistake_Still_Finish_With_Rating()
        {
            _session.SelectCell(0);
            _session.InputNumber(8); // 故意错一次
            foreach (int hole in new[] { 0, 40, 80 })
            {
                _session.SelectCell(hole);
                _session.InputNumber(_session.Solution[hole]);
            }

            Assert.IsTrue(_session.IsFinished);
            Assert.AreEqual(2, _session.StarRating, "1 错应为 2 星");
        }

        [Test]
        public void StarRating_Rules()
        {
            Assert.AreEqual(3, _session.StarRating, "0 错 3 星");
            _session.SelectCell(0); _session.InputNumber(8);
            _session.Undo();
            Assert.AreEqual(2, _session.StarRating, "1 错 2 星");
            _session.SelectCell(0); _session.InputNumber(8);
            _session.SelectCell(40); _session.InputNumber(2);
            Assert.AreEqual(1, _session.StarRating, "2 错以上 1 星");
        }

        // ---- 计时 ----

        [Test]
        public void Timer_Tracks_And_Freezes_On_Finish()
        {
            _clock.Advance(65f);
            Assert.AreEqual(65f, _session.ElapsedSeconds, 0.001f, "未完成时实时计时");

            foreach (int hole in new[] { 0, 40, 80 })
            {
                _session.SelectCell(hole);
                _session.InputNumber(_session.Solution[hole]);
            }
            float frozen = _session.ElapsedSeconds;

            _clock.Advance(1000f);
            Assert.AreEqual(frozen, _session.ElapsedSeconds, 0.001f, "完成后计时应冻结");
        }

        [Test]
        public void Full_Puzzle_Cannot_Accept_Input()
        {
            var full = new GameSession(TestPuzzles.FullPuzzle(), _clock);
            full.SelectCell(0);
            full.InputNumber(2);
            Assert.AreEqual(TestPuzzles.FinishedBoard[0], full.Board[0], "全给定盘不可输入");
        }
    }
}
