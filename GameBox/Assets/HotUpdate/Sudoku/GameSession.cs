using System;
using System.Collections.Generic;
using Sudoku.Core;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 对局会话(纯 C#,非 MonoBehaviour,Phase 4 4-2 核心):
    /// 选格/填数/笔记/撤销重做/错误检测/计时/胜利判定,全部可单测。
    /// 规则与旧工程对齐:错误=不拦截只计数标红(累计,Undo 不减);再点同数=清除;
    /// 提示上限 3 次;完成后输入冻结,GameFinished 仅触发一次。
    /// </summary>
    public sealed class GameSession
    {
        public enum InputMode { Number, Note }

        /// <summary>一次落子(值/笔记)变更记录,双栈撤销重做。</summary>
        public readonly struct Move
        {
            public readonly int Index;
            public readonly int OldValue;
            public readonly int NewValue;
            public readonly int OldNotes;
            public readonly int NewNotes;

            public Move(int index, int oldValue, int newValue, int oldNotes, int newNotes)
            {
                Index = index; OldValue = oldValue; NewValue = newValue;
                OldNotes = oldNotes; NewNotes = newNotes;
            }
        }

        // ---- 状态 ----

        /// <summary>当前棋盘(含玩家填入)。</summary>
        public SudokuBoard Board { get; }
        /// <summary>原始谜题(判定给定格 IsGiven)。</summary>
        public SudokuBoard Puzzle { get; }
        /// <summary>完整解(错误判定/提示来源)。</summary>
        public SudokuBoard Solution { get; }
        public Difficulty Difficulty { get; }
        public int SelectedIndex { get; private set; } = -1;
        public InputMode Mode { get; private set; } = InputMode.Number;
        public int MistakeCount { get; private set; }

        /// <summary>免费提示上限(04 文档:每局 3 次)。</summary>
        public int HintCount { get; } = 3;

        /// <summary>广告回奖提示数(Phase 7:提示用尽后看激励视频获得,每局上限 MaxAdsBonusHints)。</summary>
        public int AdsBonusHints { get; private set; }

        /// <summary>每局广告提示上限(防刷:激励视频按次计费,过度提供会压 eCPM)。</summary>
        public const int MaxAdsBonusHints = 2;

        public int HintsUsed { get; private set; }
        public bool IsFinished { get; private set; }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool CanUseHint => !IsFinished && HintsUsed < HintCount + AdsBonusHints;

        /// <summary>是否还可请求广告提示(未完成且未达上限;频控在 AdsService 内部)。</summary>
        public bool CanRequestAdHint => !IsFinished && AdsBonusHints < MaxAdsBonusHints;

        /// <summary>已用时长(未完成=实时,完成=冻结)。</summary>
        public float ElapsedSeconds => _finished ? _frozenSeconds : _clock.Now - _startTime;

        /// <summary>星级:0 错 3 星 / ≤2 错 2 星 / 否则 1 星(旧工程规则)。</summary>
        public int StarRating => MistakeCount == 0 ? 3 : MistakeCount <= 2 ? 2 : 1;

        // ---- 事件 ----

        public event Action BoardChanged;
        public event Action<int> CellSelected;
        public event Action GameFinished;
        public event Action HintExhausted;

        // ---- 内部 ----

        readonly int[] _notes = new int[SudokuBoard.CellCount]; // 位掩码 1<<d
        readonly Stack<Move> _undo = new();
        readonly Stack<Move> _redo = new();
        readonly IClock _clock;
        readonly float _startTime;
        float _frozenSeconds;
        bool _finished;

        public GameSession(GeneratedPuzzle puzzle, IClock clock = null)
        {
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
            Puzzle = puzzle.Puzzle;
            Solution = puzzle.Solution;
            Difficulty = puzzle.Difficulty;
            Board = Puzzle.Clone();
            _clock = clock ?? new UnityClock();
            _startTime = _clock.Now;
        }

        // ---- 输入 ----

        public void SelectCell(int index)
        {
            if (IsFinished || index < 0 || index >= SudokuBoard.CellCount) return;
            SelectedIndex = index;
            CellSelected?.Invoke(index);
        }

        public void ToggleInputMode()
        {
            if (IsFinished) return;
            Mode = Mode == InputMode.Number ? InputMode.Note : InputMode.Number;
        }

        /// <summary>数字/笔记模式输入;再点同数=清除(旧工程规则)。</summary>
        public void InputNumber(int number)
        {
            if (IsFinished || SelectedIndex < 0 || number < 1 || number > SudokuBoard.Size) return;
            int idx = SelectedIndex;
            if (IsGiven(idx)) return; // 给定格不可改

            if (Mode == InputMode.Note)
            {
                ToggleNote(idx, number);
                return;
            }

            int oldValue = Board[idx];
            if (oldValue == number)
            {
                SetValue(idx, 0); // 再点同数=清除
                return;
            }
            SetValue(idx, number);
        }

        /// <summary>擦除当前格的值(笔记保留,便于重填)。</summary>
        public void Erase()
        {
            if (IsFinished || SelectedIndex < 0 || IsGiven(SelectedIndex)) return;
            if (Board[SelectedIndex] == 0) return;
            SetValue(SelectedIndex, 0);
        }

        public void Undo()
        {
            if (IsFinished || _undo.Count == 0) return;
            var m = _undo.Pop();
            Board[m.Index] = m.OldValue;
            _notes[m.Index] = m.OldNotes;
            _redo.Push(m);
            BoardChanged?.Invoke();
        }

        public void Redo()
        {
            if (IsFinished || _redo.Count == 0) return;
            var m = _redo.Pop();
            Board[m.Index] = m.NewValue;
            _notes[m.Index] = m.NewNotes;
            _undo.Push(m);
            BoardChanged?.Invoke();
        }

        /// <summary>
        /// 广告看完回奖 1 次提示(Phase 7:提示用尽后的激励视频链路)。
        /// 调用方须先校验 CanRequestAdHint;回奖后 CanUseHint 恢复,视图刷新按钮即可用。
        /// </summary>
        public void GrantAdHint()
        {
            if (IsFinished || AdsBonusHints >= MaxAdsBonusHints) return; // 完成后/达上限不回奖
            AdsBonusHints++;
            BoardChanged?.Invoke();
        }

        /// <summary>提示一步(优先逻辑单步);提示不占撤销栈,错误数不受影响(提示值必然正确)。</summary>
        public bool TryUseHint()
        {
            if (!CanUseHint) return false;
            if (!HintEngine.GetHint(Board, out var hint)) return false;

            int idx = SudokuBoard.Index(hint.Row, hint.Col);
            _notes[idx] = 0;
            Board[idx] = hint.Value;
            AutoClearPeerNotes(idx, hint.Value);
            HintsUsed++;
            BoardChanged?.Invoke();
            CheckFinish();
            if (!CanUseHint) HintExhausted?.Invoke();
            return true;
        }

        // ---- 查询(视图高亮用) ----

        public int GetValue(int index) => Board[index];
        public bool IsGiven(int index) => Puzzle[index] != 0;
        public bool IsEmpty(int index) => Board[index] == 0;
        public int GetNotes(int index) => _notes[index];
        public bool HasNote(int index, int number) => (_notes[index] & (1 << number)) != 0;

        /// <summary>是否错误格(非给定且与解不符)。</summary>
        public bool IsMistake(int index) => !IsGiven(index) && Board[index] != 0 && Board[index] != Solution[index];

        /// <summary>是否与选中格同数(同行列宫高亮)。</summary>
        public bool IsSameNumber(int index)
        {
            if (SelectedIndex < 0 || index == SelectedIndex) return false;
            int v = Board[SelectedIndex];
            return v != 0 && Board[index] == v;
        }

        /// <summary>是否与选中格同行/列/宫(同区域高亮)。</summary>
        public bool IsPeer(int index)
        {
            if (SelectedIndex < 0 || index == SelectedIndex) return false;
            int sRow = SudokuBoard.RowOf(SelectedIndex), sCol = SudokuBoard.ColOf(SelectedIndex);
            int row = SudokuBoard.RowOf(index), col = SudokuBoard.ColOf(index);
            if (row == sRow || col == sCol) return true;
            return SudokuBoard.BoxOf(row, col) == SudokuBoard.BoxOf(sRow, sCol);
        }

        // ---- 内部 ----

        void SetValue(int idx, int value)
        {
            int oldNotes = _notes[idx];
            if (value != 0)
            {
                _notes[idx] = 0;
                AutoClearPeerNotes(idx, value);
            }
            var move = new Move(idx, Board[idx], value, oldNotes, _notes[idx]);
            Board[idx] = value;
            _undo.Push(move);
            _redo.Clear();
            if (value != 0 && value != Solution[idx]) MistakeCount++; // 错误累计,Undo 不减(旧工程规则)
            BoardChanged?.Invoke();
            CheckFinish();
        }

        void ToggleNote(int idx, int number)
        {
            int oldNotes = _notes[idx];
            _notes[idx] ^= 1 << number; // 位翻转:有则删,无则加
            if (_notes[idx] != oldNotes)
            {
                _undo.Push(new Move(idx, Board[idx], Board[idx], oldNotes, _notes[idx]));
                _redo.Clear();
                BoardChanged?.Invoke();
            }
        }

        void AutoClearPeerNotes(int idx, int value)
        {
            int bit = 1 << value;
            int row = SudokuBoard.RowOf(idx), col = SudokuBoard.ColOf(idx);
            for (int c = 0; c < SudokuBoard.Size; c++)
            {
                int i = row * SudokuBoard.Size + c;
                if (i != idx) _notes[i] &= ~bit;
            }
            for (int r = 0; r < SudokuBoard.Size; r++)
            {
                int i = r * SudokuBoard.Size + col;
                if (i != idx) _notes[i] &= ~bit;
            }
            int boxRow = (row / SudokuBoard.BoxSize) * SudokuBoard.BoxSize;
            int boxCol = (col / SudokuBoard.BoxSize) * SudokuBoard.BoxSize;
            for (int r = boxRow; r < boxRow + SudokuBoard.BoxSize; r++)
                for (int c = boxCol; c < boxCol + SudokuBoard.BoxSize; c++)
                {
                    int i = r * SudokuBoard.Size + c;
                    if (i != idx) _notes[i] &= ~bit;
                }
        }

        void CheckFinish()
        {
            if (_finished || !Board.IsSolved()) return;
            _finished = true;
            _frozenSeconds = _clock.Now - _startTime;
            IsFinished = true;
            GameFinished?.Invoke();
        }
    }
}
