using System;
using System.Threading;
using Box.UI;
using Cysharp.Threading.Tasks;
using Sudoku.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 对局视图(Phase 4 4-2):代码生成 81 格棋盘(3x3 宫嵌套 GridLayoutGroup)、
    /// 选格/高亮(Mistake&gt;Selected&gt;SameNumber&gt;Peer&gt;宫底色)、数字盘/工具条、
    /// 计时循环、返回键=Undo、胜利结算+每日挑战写入。
    /// prefab: Resources/UI/GameplayView.prefab(Phase4SceneSetup 生成)。
    /// </summary>
    public sealed class GameplayView : UIView
    {
        protected override async void Awake()
        {
            base.Awake();
            await InitSceneRoot(); // 场景直挂视图:自驱动 Create+Show(不走 Router 栈)
        }

        UIService _svc;
        GameSession _session;
        GameObject _board;
        GameObject[] _cells;
        BoxText[] _cellTexts;
        BoxText _titleText, _timeText, _hintText;
        BoxButton _modeBtn, _undoBtn, _redoBtn, _eraseBtn, _hintBtn;
        CancellationTokenSource _timerCts;

        protected override UniTask OnCreate()
        {
            _svc = UIService.Instance;
            _board = transform.Find("BoardPlaceholder")?.gameObject;
            BuildBoardCells();

            _titleText = transform.Find("TitleText")?.GetComponent<BoxText>();
            _timeText = transform.Find("TimeText")?.GetComponent<BoxText>();
            _hintText = transform.Find("HintCountText")?.GetComponent<BoxText>();

            BindButton("ModeButton", out _modeBtn, OnMode);
            BindButton("UndoButton", out _undoBtn, () => _session?.Undo());
            BindButton("RedoButton", out _redoBtn, () => _session?.Redo());
            BindButton("EraseButton", out _eraseBtn, () => _session?.Erase());
            BindButton("HintButton", out _hintBtn, OnHint);
            BindButton("BackButton", out _, OnExitButton); // 左上角返回=退出对局(确认框);实体返回键仍=Undo

            for (int i = 1; i <= 9; i++)
            {
                int n = i; // 闭包捕获
                var btn = transform.Find("Num" + n)?.GetComponent<BoxButton>();
                if (btn != null) btn.OnClick(() => _session?.InputNumber(n));
            }
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            StartGame(GameContext.IsDaily
                ? PuzzleFactory.CreateDaily(GameContext.DailySeed)
                : PuzzleFactory.Create(GameContext.Difficulty));
            _svc?.RegisterBackHandler(OnBackKey);
            // 淡入(D-15):绑定 _timerCts,OnDestroy 取消防场景切换后协程访问已销毁对象
            await BoxTween.FadeTo(gameObject, 0f, 1f, 0.2f, _timerCts.Token);
        }

        // 隐藏 MonoBehaviour.OnDestroy(Unity 生命周期):场景卸载即清理,防残留闭包;
        // 注意 UIView 另有 UniTask OnDestroy()(Router 销毁流程),两者独立。
        // sealed 类用 private new 避免 CS0628。
        private new void OnDestroy()
        {
            _timerCts?.Cancel();
            if (_svc != null) _svc.ClearBackHandler();
        }

        // ---- 对局 ----

        void StartGame(GeneratedPuzzle puzzle)
        {
            _session = new GameSession(puzzle);
            _session.CellSelected += _ => RefreshBoard();
            _session.BoardChanged += RefreshBoard;
            _session.GameFinished += OnGameFinished;
            _session.HintExhausted += OnHintExhausted;

            bool daily = GameContext.IsDaily;
            if (_titleText != null) _titleText.Text = daily ? "每日挑战" : "数独 - " + DifficultyName(GameContext.Difficulty);
            RefreshBoard();
            _svc?.Router.Analytics?.LogEvent("sudoku.level_start"); // §8.4 {module_id}.{action}

            _timerCts?.Cancel();
            _timerCts = new CancellationTokenSource();
            UpdateTimer().Forget();
        }

        void OnGameFinished()
        {
            RefreshBoard();
            _svc?.Router.Analytics?.LogEvent("sudoku.level_complete");

            var result = new SettlementResult
            {
                StarRating = _session.StarRating,
                MistakeCount = _session.MistakeCount,
                HintsUsed = _session.HintsUsed,
                TimeSec = _session.ElapsedSeconds,
                Difficulty = _session.Difficulty,
                IsDaily = GameContext.IsDaily,
            };
            if (GameContext.IsDaily) // 临时 PlayerPrefs,Phase 5 迁入正式存档
            {
                int seed = GameContext.DailySeed;
                DailyChallengeStore.MarkCompleted(seed);
                DailyChallengeStore.SetBestSeconds(seed, (int)_session.ElapsedSeconds);
                result.BestSec = DailyChallengeStore.GetBestSeconds(seed);
            }
            ShowSettlement(result).Forget();
        }

        async UniTaskVoid ShowSettlement(SettlementResult result)
        {
            if (_svc == null) return;
            while (result.Action == SettlementAction.None)
            {
                await _svc.Popup.ShowAsync("UI/Popups/SettlementPopup", result);
                if (result.Action == SettlementAction.None)
                    await UniTask.DelayFrame(1); // 防御:弹窗展示失败(资源缺失/路由占用)时 Action 不变,放行帧防同帧死循环(watchdog 断点)
            }

            if (result.Action == SettlementAction.Next)
            {
                StartGame(GameContext.IsDaily
                    ? PuzzleFactory.CreateDaily(GameContext.DailySeed)
                    : PuzzleFactory.Create(GameContext.Difficulty));
            }
            else
            {
                await SceneManager.LoadSceneAsync("MainMenu");
            }
        }

        // ---- 返回键(栈深>0 交还路由关弹窗,否则 Undo) ----

        UniTask<bool> OnBackKey()
        {
            if (_svc != null && _svc.Router.StackCount > 0) return UniTask.FromResult(false); // 弹窗打开:交还路由
            OnBack();
            return UniTask.FromResult(true);
        }

        void OnBack()
        {
            if (_session == null || _session.IsFinished) return;
            if (_svc != null && _svc.Router.StackCount > 0) return;
            if (_session.CanUndo) _session.Undo();
        }

        // ---- 左上角返回按钮:退出确认框(防误触;实体返回键=Undo 不受影响) ----

        async void OnExitButton()
        {
            if (_session == null || _session.IsFinished) return;
            if (_svc != null && _svc.Router.StackCount > 0) return;
            var dialog = await _svc.Router.PushAsync<BoxDialogView>("UI/Popups/ExitConfirm");
            if (dialog == null) return; // 资源缺失:放弃
            dialog.SetTitle("退出对局");
            dialog.SetMessage("当前进度将丢失,确定退出?");
            dialog.OnCancel(() => _svc.Router.PopAsync().Forget()); // 取消:只关弹窗
            dialog.OnConfirm(async () => // 确认:先关弹窗再切场景,防竞态(同 DifficultySelect)
            {
                await _svc.Router.PopAsync();
                await SceneManager.LoadSceneAsync("MainMenu");
            });
        }

        void OnMode()
        {
            if (_session == null) return;
            _session.ToggleInputMode();
            RefreshBoard();
        }

        void OnHint()
        {
            if (_session != null && _session.TryUseHint())
                _svc?.Router.Analytics?.LogEvent("sudoku.hint_used");
        }

        void OnHintExhausted()
        {
            if (_hintBtn != null) _hintBtn.SetInteractable(false);
        }

        // ---- 棋盘渲染(81 格全量刷新,UI 轻量无性能问题) ----

        void BuildBoardCells()
        {
            if (_board == null || _cells != null) return;
            var boardRect = _board.GetComponent<RectTransform>();
            var outer = _board.GetComponent<GridLayoutGroup>();
            if (outer == null) outer = _board.AddComponent<GridLayoutGroup>();
            float boxGap = 8f, cellGap = 2f;
            var rect = boardRect.rect;
            outer.cellSize = new Vector2((rect.width - 2 * boxGap) / 3f, (rect.height - 2 * boxGap) / 3f);
            outer.spacing = new Vector2(boxGap, boxGap);
            outer.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            outer.constraintCount = 3;

            _cells = new GameObject[81];
            _cellTexts = new BoxText[81];
            for (int box = 0; box < 9; box++)
            {
                var boxGo = new GameObject("Box" + box, typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup));
                boxGo.transform.SetParent(_board.transform, false);
                var bg = boxGo.GetComponent<Image>();
                bg.color = new Color(0.22f, 0.22f, 0.26f); // 宫底色
                var glg = boxGo.GetComponent<GridLayoutGroup>();
                glg.cellSize = new Vector2((outer.cellSize.x - 2 * cellGap) / 3f, (outer.cellSize.y - 2 * cellGap) / 3f);
                glg.spacing = new Vector2(cellGap, cellGap);
                glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = 3;

                for (int k = 0; k < 9; k++)
                {
                    int index = box * 9 + k;
                    // 父格:背景+点击(TMP 与 Image 同为 Graphic 互斥,不能同 GameObject)
                    var go = new GameObject("C" + index, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
                    go.transform.SetParent(boxGo.transform, false);
                    var img = go.GetComponent<Image>();
                    img.color = new Color(0.16f, 0.16f, 0.18f);
                    var btn = go.GetComponent<Button>();
                    btn.transition = Selectable.Transition.None; // 格子不闪默认高亮
                    var boxBtn = go.GetComponent<BoxButton>();
                    boxBtn.PressFeedbackEnabled = false; // 密集网格不做缩放反馈
                    boxBtn.OnClick(() => _session?.SelectCell(index));

                    // 数字子节点:TMP 组件 + BoxText 同节点(BoxText.Awake 于 AddComponent 时 GetComponent,
                    // 顺序 TextMeshProUGUI 在前);缺 TMP 时 BoxText._text 为 null → 数字静默不显示
                    var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(BoxText));
                    textGo.transform.SetParent(go.transform, false);
                    var trt = textGo.GetComponent<RectTransform>();
                    trt.anchorMin = Vector2.zero; // 铺满父格
                    trt.anchorMax = Vector2.one;
                    trt.sizeDelta = Vector2.zero;
                    textGo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center; // 数字居中
                    _cells[index] = go;
                    _cellTexts[index] = textGo.GetComponent<BoxText>();
                }
            }
        }

        void RefreshBoard()
        {
            if (_session == null || _cells == null) return;
            for (int i = 0; i < _cells.Length; i++)
            {
                var text = _cellTexts[i];
                var img = _cells[i].GetComponent<Image>();

                int value = _session.GetValue(i);
                if (value != 0)
                {
                    text.Text = value.ToString();
                    text.SetFontSize(40);
                    text.SetColor(_session.IsGiven(i) ? new Color(0.85f, 0.85f, 0.9f) : Color.white);
                    text.SetVisible(true);
                }
                else if (_session.Mode == GameSession.InputMode.Note && _session.GetNotes(i) != 0)
                {
                    text.Text = NotesToString(_session.GetNotes(i));
                    text.SetFontSize(18);
                    text.SetColor(new Color(0.6f, 0.6f, 0.7f));
                    text.SetVisible(true);
                }
                else
                {
                    text.Text = string.Empty;
                    text.SetVisible(false);
                }

                // 高亮优先级:Mistake > Selected > SameNumber > Peer > 给定/默认
                Color bg;
                if (_session.IsMistake(i)) bg = new Color(0.55f, 0.16f, 0.18f);
                else if (i == _session.SelectedIndex) bg = new Color(0.16f, 0.42f, 0.72f);
                else if (_session.IsSameNumber(i)) bg = new Color(0.24f, 0.40f, 0.62f);
                else if (_session.IsPeer(i)) bg = new Color(0.28f, 0.28f, 0.32f);
                else if (_session.IsGiven(i)) bg = new Color(0.22f, 0.22f, 0.24f);
                else bg = new Color(0.17f, 0.17f, 0.19f);
                img.color = bg;
            }

            if (_undoBtn != null) _undoBtn.SetInteractable(_session.CanUndo);
            if (_redoBtn != null) _redoBtn.SetInteractable(_session.CanRedo);
            if (_hintBtn != null) _hintBtn.SetInteractable(_session.CanUseHint);
            if (_hintText != null) _hintText.Text = $"提示 {_session.HintsUsed}/{_session.HintCount}";
            var modeLabel = _modeBtn != null ? _modeBtn.transform.Find("Label")?.GetComponent<BoxText>() : null;
            if (modeLabel != null)
                modeLabel.Text = _session.Mode == GameSession.InputMode.Note ? "数字" : "笔记";
        }

        // ---- 计时 ----

        async UniTaskVoid UpdateTimer()
        {
            var ct = _timerCts.Token;
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_timeText != null) _timeText.Text = "用时 " + FormatTime(_session.ElapsedSeconds);
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { /* 场景切换/重开时取消 */ }
        }

        // ---- 工具 ----

        void BindButton(string path, out BoxButton btn, Action callback)
        {
            btn = transform.Find(path)?.GetComponent<BoxButton>();
            if (btn != null) btn.OnClick(callback);
        }

        static string NotesToString(int mask)
        {
            var sb = new System.Text.StringBuilder(16);
            for (int d = 1; d <= 9; d++)
                if ((mask & (1 << d)) != 0) { if (sb.Length > 0) sb.Append(' '); sb.Append(d); }
            return sb.ToString();
        }

        static string DifficultyName(Difficulty d) => d switch
        {
            Difficulty.Easy => "简单",
            Difficulty.Medium => "中等",
            Difficulty.Hard => "困难",
            _ => d.ToString(),
        };

        /// <summary>mm:ss 格式(结算弹窗共用)。</summary>
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
