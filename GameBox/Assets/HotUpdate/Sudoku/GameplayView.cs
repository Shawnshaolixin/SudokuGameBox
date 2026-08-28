using System;
using System.Threading;
using Box.ModuleFramework;
using Box.Services;
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
            FxPool.Init(); // 特效池预热(幂等;跨场景常驻)
            BuildBoardCells();

            _titleText = transform.Find("TitleText")?.GetComponent<BoxText>();
            _timeText = transform.Find("TimeText")?.GetComponent<BoxText>();
            _hintText = transform.Find("HintCountText")?.GetComponent<BoxText>();

            BindButton("ModeButton", out _modeBtn, OnMode);
            BindButton("UndoButton", out _undoBtn, () => _session?.Undo());
            BindButton("RedoButton", out _redoBtn, () => _session?.Redo());
            BindButton("EraseButton", out _eraseBtn, () =>
            {
                if (_session != null && _session.Erase()) PlaySfx(AudioSfx.Erase); // 实际擦除才响
            });
            BindButton("HintButton", out _hintBtn, OnHint);
            BindButton("BackButton", out _, OnExitButton); // 左上角返回=退出对局(确认框);实体返回键仍=Undo
            // 2026-08-29:返回按钮只保留 icon,隐藏文字 Label(icon 已足够表达语义)
            transform.Find("BackButton/Label")?.gameObject.SetActive(false);

            for (int i = 1; i <= 9; i++)
            {
                int n = i; // 闭包捕获
                // 数字按钮在 NumberPanel 内由 HorizontalLayoutGroup 排布(Phase 8 换肤重构),Find 走完整路径
                var btn = transform.Find("NumberPanel/Num" + n)?.GetComponent<BoxButton>();
                if (btn != null) btn.OnClick(() =>
                {
                    if (_session != null && _session.InputNumber(n))
                    {
                        PlaySfx(AudioSfx.Place); // 实际输入(含清格/笔记)才响
                        FxPool.PlayBurst(FxPool.StarTex, CellWorldPos(_session.SelectedIndex), 12, 0.8f,
                            new Color(0.65f, 0.82f, 1f)); // 填数反馈:淡蓝小星爆发
                    }
                });
            }
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            StartGame(GameContext.IsDaily
                ? PuzzleFactory.CreateDaily(GameContext.DailySeed)
                : PuzzleFactory.Create(GameContext.Difficulty));
            _svc?.RegisterBackHandler(OnBackKey);
            L10n.LanguageChanged += OnLanguageChanged; // 语言切换刷新对局内文案(FR-17)
            ApplyLanguage(); // 打开即按当前语言刷新(prefab 初始英文文案)
            // 淡入(D-15):绑定 _timerCts,OnDestroy 取消防场景后协程访问已销毁对象
            await BoxTween.FadeTo(gameObject, 0f, 1f, 0.2f, _timerCts.Token);
        }

        // 隐藏 MonoBehaviour.OnDestroy(Unity 生命周期):场景卸载即清理,防残留闭包;
        // 注意 UIView 另有 UniTask OnDestroy()(Router 销毁流程),两者独立。
        // sealed 类用 private new 避免 CS0628。
        private new void OnDestroy()
        {
            L10n.LanguageChanged -= OnLanguageChanged; // 退订,防泄漏
            _timerCts?.Cancel();
            if (_svc != null) _svc.ClearBackHandler();
        }

        void OnLanguageChanged() => ApplyLanguage();

        /// <summary>按当前语言刷新对局内所有静态文案(标题/工具条/模式/按钮)。</summary>
        void ApplyLanguage()
        {
            RefreshTitle();
            SetButtonLabel("ModeButton", null); // 模式标签由 RefreshBoard 按输入模式定,这里只刷固定按钮
            SetButtonLabel("UndoButton", L10n.Get("game.undo"));
            SetButtonLabel("RedoButton", L10n.Get("game.redo"));
            SetButtonLabel("EraseButton", L10n.Get("game.erase"));
            SetButtonLabel("HintButton", L10n.Get("game.hint"));
            // BackButton 无文字(2026-08-29:只保留 icon,见 OnCreate 隐藏 Label)
            RefreshHintText();
        }

        void SetButtonLabel(string path, string text)
        {
            var t = transform.Find(path + "/Label")?.GetComponent<TextMeshProUGUI>();
            if (t != null && text != null) t.text = text;
        }

        void RefreshTitle()
        {
            if (_titleText == null) return;
            _titleText.Text = GameContext.IsDaily
                ? L10n.Get("game.title.daily")
                : L10n.Format("game.title.normal", DifficultyName(GameContext.Difficulty));
        }

        void RefreshHintText()
        {
            if (_hintText != null && _session != null)
                _hintText.Text = L10n.Format("game.hintcount", _session.HintsUsed, _session.HintCount);
        }

        // ---- 对局 ----

        void StartGame(GeneratedPuzzle puzzle)
        {
            _session = new GameSession(puzzle);
            _session.CellSelected += _ => RefreshBoard();
            _session.BoardChanged += RefreshBoard;
            _session.GameFinished += OnGameFinished;
            _session.HintExhausted += OnHintExhausted;

            RefreshTitle(); // 标题走 L10n(每日挑战/数独-难度)
            RefreshBoard();
            _svc?.Router.Analytics?.LogEvent("sudoku.level_start"); // §8.4 {module_id}.{action}

            _timerCts?.Cancel();
            _timerCts = new CancellationTokenSource();
            UpdateTimer().Forget();
        }

        void OnGameFinished()
        {
            RefreshBoard();
            PlaySfx(AudioSfx.Win); // TODO(试听):胜利音临时占位(switch 系列尾部),待正式 fanfare 替换
            if (_board != null) FxPool.Celebrate(_board.transform.position); // 胜利庆祝:棋盘中心双爆发
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

            // 局间插屏候选点(Phase 7 7-1):结算弹窗关闭后展示,不打断结算阅读。
            // 频控在 AdsService 内部(去广告零广告 / 前 3 局不弹 / 局间隔 4~6 分钟),桩实现同样生效。
            ServiceLocator.Ads?.ShowInterstitial();

            if (result.Action == SettlementAction.Next)
            {
                StartGame(GameContext.IsDaily
                    ? PuzzleFactory.CreateDaily(GameContext.DailySeed)
                    : PuzzleFactory.Create(GameContext.Difficulty));
            }
            else
            {
                await ExitToMainMenuAsync();
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
            dialog.SetTitle(L10n.Get("game.exit.title"));
            dialog.SetMessage(L10n.Get("game.exit.message"));
            dialog.OnCancel(() => _svc.Router.PopAsync().Forget()); // 取消:只关弹窗
            dialog.OnConfirm(async () => // 确认:先关弹窗再退模块(复位状态),防竞态(同 DifficultySelect)
            {
                await _svc.Router.PopAsync();
                await ExitToMainMenuAsync();
            });
        }

        // ---- 统一回大厅出口(复位模块状态,防二次进入被拒) ----
        // 必须走 ModuleLoader.ExitAsync:直接切场景会让 _states[sudoku] 永久卡 Active,
        // EnterAsync 的防重入守卫(GetState != Idle)会拒绝第二次进入。SceneManager 归 OnExit 管。

        async UniTask ExitToMainMenuAsync()
        {
            var loader = ModuleLoader.Instance;
            if (loader != null)
                await loader.ExitAsync("sudoku"); // SudokuModule.OnExit 内部 LoadSceneAsync("MainMenu")
            else
                await SceneManager.LoadSceneAsync("MainMenu"); // 兜底(无启动引导时)
        }

        void OnMode()
        {
            if (_session == null) return;
            _session.ToggleInputMode();
            RefreshBoard();
        }

        /// <summary>
        /// 提示按钮(Phase 7 7-1 商业化接线):
        /// 免费提示未用尽 → 直接用;用尽且未达广告提示上限 → 弹确认框 → 激励视频 → 回奖 1 次提示。
        /// 广告提示链路(04 文档「提示币耗尽点提示」核心激励点;每局上限 MaxAdsBonusHints 防刷)。
        /// </summary>
        void OnHint()
        {
            if (_session == null || _session.IsFinished) return;
            if (_session.CanUseHint)
            {
                UseHint();
                return;
            }
            if (!_session.CanRequestAdHint) return; // 已达广告提示上限:按钮置灰态(OnHintExhausted)
            AskAdHint().Forget();
        }

        async UniTaskVoid AskAdHint()
        {
            if (_svc == null) return;
            // 广告未就绪(无 GMS/加载失败)时直接提示,不再弹确认框空等(Phase 9 真机反馈)
            var ads = ServiceLocator.Ads;
            if (ads != null && !ads.IsRewardedReady)
            {
                BoxToast.Show(L10n.Get("hint.ad.unavailable"));
                return;
            }
            var dialog = await _svc.Router.PushAsync<BoxDialogView>("UI/Popups/AdHintConfirm");
            if (dialog == null) return; // 资源缺失:放弃(提示按钮保持不可点)
            dialog.SetTitle(L10n.Get("hint.ad.title"));
            dialog.SetMessage(L10n.Format("hint.ad.message", GameSession.MaxAdsBonusHints));
            dialog.OnCancel(() => _svc.Router.PopAsync().Forget());
            dialog.OnConfirm(async () =>
            {
                await _svc.Router.PopAsync(); // 先关确认框,再展示激励视频
                var ads = ServiceLocator.Ads;
                if (ads == null) return;
                ads.ShowRewardedAd(watched =>
                {
                    if (!watched || _session == null) return; // 中途关闭/未就绪:不发放
                    if (!_session.CanRequestAdHint) return;
                    _session.GrantAdHint(); // 回奖:提示数 +1,按钮恢复可点
                    UseHint();
                });
            });
        }

        void UseHint()
        {
            if (_session != null && _session.TryUseHint(out int hintIdx))
            {
                PlaySfx(AudioSfx.Hint); // 提示落子轻音
                FxPool.PlayBurst(FxPool.SparkTex, CellWorldPos(hintIdx), 16, 1f,
                    new Color(1f, 0.85f, 0.4f)); // 提示反馈:金色火花出现在被提示格
                _svc?.Router.Analytics?.LogEvent("sudoku.hint_used");
            }
            RefreshBoard(); // 回奖后同步按钮 interactable(HintExhausted 置灰 → 可点)
        }

        void OnHintExhausted()
        {
            if (_hintBtn != null) _hintBtn.SetInteractable(false);
        }

        // ---- 棋盘配色:对齐 docs/UIDesignSystem 设计 Token(浅色奶油棕系,原深灰板废止) ----
        // 宫间 8px 缝隙=Border 描边,格间 2px 缝隙=Background/Secondary,格底=Surface 两级
        // 2026-08-29 视觉修正:① 选中/错误区分(选中改草绿,错误加深红——原橙/红同属暖系难辨);
        // ② Peer 高亮独立(原与格缝隙 #FBE8A6 同值,选中后 9 格与边框同色);
        // ③ 内外边框加深一档,格线更明显
        static readonly Color s_BoardOuterColor = new Color(0.753f, 0.635f, 0.376f); // #C0A260 棋盘最外层边框(比宫分隔深一档)
        static readonly Color s_BoardGapColor = new Color(0.839f, 0.722f, 0.451f); // #D6B873 棋盘底/宫分隔(外边框,加深)
        static readonly Color s_CellGapColor = new Color(0.941f, 0.843f, 0.561f); // #F0D78F 宫底/格间缝隙(内边框,加深)
        static readonly Color s_CellBgDefault = new Color(1f, 0.976f, 0.914f); // Surface/Primary #FFF9E9(可编辑格)
        static readonly Color s_CellBgGiven = new Color(1f, 0.953f, 0.816f); // Surface/Secondary #FFF3D0(给定格)
        static readonly Color s_HlSelected = new Color(0.435f, 0.659f, 0.361f); // 柔和草绿(选中格,与错误红明确区分)
        static readonly Color s_HlSameNumber = new Color(0.965f, 0.847f, 0.459f); // Background/Primary #F6D875(同数高亮,黄橙)
        static readonly Color s_HlPeer = new Color(1f, 0.906f, 0.745f); // #FFE7BE(同行列宫弱高亮,浅杏橙,独立于边框色)
        static readonly Color s_HlMistake = new Color(0.851f, 0.353f, 0.267f); // #D95A44 错误红(加深,与选中绿对比清晰)
        static readonly Color s_TextGiven = new Color(0.227f, 0.165f, 0.102f); // Text/Primary #3A2A1A(给定数字)
        static readonly Color s_TextInput = new Color(0.914f, 0.471f, 0.196f); // Primary #E97832(输入数字)
        static readonly Color s_TextNote = new Color(0.502f, 0.424f, 0.302f); // Text/Secondary #806C4D(笔记)

        // ---- 棋盘渲染(81 格全量刷新,UI 轻量无性能问题) ----

        void BuildBoardCells()
        {
            if (_board == null || _cells != null) return;
            var boardRect = _board.GetComponent<RectTransform>();
            var outer = _board.GetComponent<GridLayoutGroup>();
            if (outer == null) outer = _board.AddComponent<GridLayoutGroup>();
            float boxGap = 8f, cellGap = 2f;
            float outerPad = 14f; // 棋盘最外层边框厚度(2026-08-29:GridLayoutGroup padding 外露容器底色)
            var rect = boardRect.rect;
            outer.padding = new RectOffset((int)outerPad, (int)outerPad, (int)outerPad, (int)outerPad);
            outer.cellSize = new Vector2((rect.width - 2 * boxGap - 2 * outerPad) / 3f, (rect.height - 2 * boxGap - 2 * outerPad) / 3f);
            outer.spacing = new Vector2(boxGap, boxGap);
            _board.GetComponent<Image>().color = s_BoardOuterColor; // 容器底色=外框色(宫缝用宫底色,见 Box 生成)
            outer.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            outer.constraintCount = 3;

            _cells = new GameObject[81];
            _cellTexts = new BoxText[81];
            for (int box = 0; box < 9; box++)
            {
                var boxGo = new GameObject("Box" + box, typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup));
                boxGo.transform.SetParent(_board.transform, false);
                var bg = boxGo.GetComponent<Image>();
                bg.color = s_CellGapColor; // 宫底色(格间 2px 缝隙透出,Background/Secondary)
                var glg = boxGo.GetComponent<GridLayoutGroup>();
                glg.cellSize = new Vector2((outer.cellSize.x - 2 * cellGap) / 3f, (outer.cellSize.y - 2 * cellGap) / 3f);
                glg.spacing = new Vector2(cellGap, cellGap);
                glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = 3;

                for (int k = 0; k < 9; k++)
                {
                    // 索引换算:视觉(宫优先 box/k) → 数据(行优先 row*9+col)。
                    // box=(vR/3)*3+(vC/3), k=(vR%3)*3+(vC%3) → vR*9+vC。
                    // 旧实现 box*9+k 与 SudokuBoard 行优先索引错位,
                    // 会把第(r+1)行数据渲染进第 r 行 → 视觉同列重复(数据合法,显示错位)。
                    int index = ((box / 3) * 3 + k / 3) * SudokuBoard.Size + ((box % 3) * 3 + k % 3);
                    // 父格:背景+点击(TMP 与 Image 同为 Graphic 互斥,不能同 GameObject)
                    var go = new GameObject("C" + index, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
                    go.transform.SetParent(boxGo.transform, false);
                    var img = go.GetComponent<Image>();
                    img.color = s_CellBgDefault; // 格底默认色(Surface/Primary)
                    var btn = go.GetComponent<Button>();
                    btn.transition = Selectable.Transition.None; // 格子不闪默认高亮
                    var boxBtn = go.GetComponent<BoxButton>();
                    boxBtn.PressFeedbackEnabled = false; // 密集网格不做缩放反馈(统一点击音也随之跳过,防格子+数字盘双响)
                    boxBtn.OnClick(() =>
                    {
                        if (_session != null) _session.SelectCell(index);
                        PlaySfx("click1"); // 选格轻点击音
                    });

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
                    // 选中(绿底)/错误(红底)实心高亮统一翻白字保对比;给定=深棕(Text/Primary),输入=主色橙(Primary)区分给定/自填
                    text.SetColor(_session.IsMistake(i) || i == _session.SelectedIndex ? Color.white : _session.IsGiven(i) ? s_TextGiven : s_TextInput);
                    text.SetVisible(true);
                }
                else if (_session.Mode == GameSession.InputMode.Note && _session.GetNotes(i) != 0)
                {
                    text.Text = NotesToString(_session.GetNotes(i));
                    text.SetFontSize(18);
                    text.SetColor(s_TextNote);
                    text.SetVisible(true);
                }
                else
                {
                    text.Text = string.Empty;
                    text.SetVisible(false);
                }

                // 高亮优先级:Mistake > Selected > SameNumber > Peer > 给定/默认
                // 配色(2026-08-29):选中=草绿(与错误红明确区分),同数=背景主黄橙,
                // 同行列宫=浅杏橙(独立色,不与边框混淆),给定=次级 Surface,错误=深红
                Color bg;
                if (_session.IsMistake(i)) bg = s_HlMistake;
                else if (i == _session.SelectedIndex) bg = s_HlSelected;
                else if (_session.IsSameNumber(i)) bg = s_HlSameNumber;
                else if (_session.IsPeer(i)) bg = s_HlPeer;
                else if (_session.IsGiven(i)) bg = s_CellBgGiven;
                else bg = s_CellBgDefault;
                img.color = bg;
            }

            if (_undoBtn != null) _undoBtn.SetInteractable(_session.CanUndo);
            if (_redoBtn != null) _redoBtn.SetInteractable(_session.CanRedo);
            // 提示按钮:免费额度可用或仍可请求广告回奖额度时保持可点;
            // 否则(两者皆耗尽)置灰——修复:免费用尽后仍应可点,点击走广告确认框(Phase 7)。
            if (_hintBtn != null) _hintBtn.SetInteractable(_session.CanUseHint || _session.CanRequestAdHint);
            RefreshHintText(); // L10n:提示 x/y
            var modeLabel = _modeBtn != null ? _modeBtn.transform.Find("Label")?.GetComponent<BoxText>() : null;
            if (modeLabel != null)
                modeLabel.Text = L10n.Get(_session.Mode == GameSession.InputMode.Note ? "game.mode.note" : "game.mode.input");
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
                    if (_timeText != null) _timeText.Text = L10n.Format("game.time", FormatTime(_session.ElapsedSeconds));
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { /* 场景切换/重开时取消 */ }
        }

        // ---- 工具 ----

        /// <summary>音效快捷方式(Phase 8 音频系统):经 IAudioService,短名常量见 AudioSfx。</summary>
        static void PlaySfx(string name) => ServiceLocator.Audio?.PlaySfx(name);

        /// <summary>格子 UI 世界坐标(overlay 下即屏幕位置;越界返回原点防空引用)。</summary>
        Vector3 CellWorldPos(int index)
        {
            if (_cells == null || index < 0 || index >= _cells.Length) return Vector3.zero;
            return _cells[index].transform.position;
        }

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
            Difficulty.Easy => L10n.Get("game.diff.easy"),
            Difficulty.Medium => L10n.Get("game.diff.medium"),
            Difficulty.Hard => L10n.Get("game.diff.hard"),
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
