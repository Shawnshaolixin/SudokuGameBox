using System;
using Box.ModuleFramework;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序主视图(全屏弹层;Layer=Popup 让 Android 返回键可关)。三面板内部切换,不压 Router 视图:
    /// 选关 → 对局 → 结算。
    ///
    /// 挂载纪律(20 文档 §4):热更组件不进场景直挂、不序列化进 prefab —— prefab 根挂 AOT HotViewBinder
    /// (viewTypeFullName 指向本类动态 AddComponent);prefab 由 M1.3 WaterSortViewSetup 生成,本类只写行为。
    ///
    /// Prefab 节点契约(M1.3 生成器严格按此命名;缺失节点一律空安全降级):
    ///   SelectPanel/Title                    TMP 标题
    ///   SelectPanel/DailyButton             每日挑战入口(选中「SelectPanel/ItemTemplate 同级,标题下」)
    ///   SelectPanel/ItemTemplate            选关按钮模板:BoxButton + Label(TMP)(只渲染可玩关,无锁定态)
    ///   SelectPanel/LevelScroll/Viewport/Content  选关网格容器(克隆 ItemTemplate 进 Content,代码铺 5 列)
    ///   SelectPanel/HubButton               回大厅弹窗(MoreGames;按钮 = Pop 本视图 → OnHide 退模块)
    ///   GamePanel/TopBar/BackButton         返回(常规=选关 / 每日=每日主页,放弃本局进度)
    ///   GamePanel/TopBar/GameTitle          关卡标题(常规「第 N 关 · 难度」/ 每日「每日挑战」)
    ///   GamePanel/TopBar/CoinLabel          TMP 金币余额(M1.4 双通道后随变更刷新)
    ///   GamePanel/StepText                  TMP 步数(每次盘面刷新同步)
    ///   GamePanel/TubeArea                  试管容器(本视图 AddComponent WaterSortTubeRack 代码绘制+点击)
    ///   GamePanel/BottomBar/UndoButton | RestartButton   免费操作(本版已接)
    ///   GamePanel/BottomBar/HintButton | ExtraTubeButton 金币直购提示/加空瓶(M1.4 已接;激励视频分支 M3 复用同钮)
    ///   SettlePanel/Title | ResultText | RewardText | NextButton | RetryButton | HubButton(RewardText 仅首通结算显示)
    ///   DailyPanel/Title | StateText | StreakText | PlayButton | BackButton  每日主页(M2.3;完成态/Streak/重玩)
    ///
    /// 每日挑战(M2.3)模式语义:视图内模式 = 会话标记(WaterSortSession.IsDaily);选关页「每日挑战」
    /// 入口经 Session 实例整体换新(旧实例退订销毁,见 SwitchSession),不弹 Router、不退出模块;
    /// 模块级入口 args="daily"(WaterSortModule.OnEnter)落同一主页。每日主页 Back 回常规选关 =
    /// 再换回常规会话。对局/结算共用面板,差异全部收敛在:标题文案、完成落盘(每日 → dailyDoneSeeds,
    /// 常规 → firstWinLevels+发币)、Next 放行(每日恒禁)。
    ///
    /// 退出纪律(与 WaterSortModule.OnExit 配合):本视图是模块压入 Router 的唯一自属视图,M1 内模块
    /// 不会在其上再压 Router 弹窗(结算/选关都是面板切换),故 OnHide 只会在「真的被 Pop」时触发
    /// (主动 HubButton 或返回键)——即用户离开模块的唯一信号 → ExitAsync 复位模块状态(防卡 Active)。
    /// ⚠️ M2 若需在本视图之上压 Router 弹窗(如每日奖励),须按 DifficultySelectView 的
    /// StackCount!=0 被盖守卫模式复检,防被盖时误退。
    /// </summary>
    public sealed class WaterSortView : UIView
    {
        /// <summary>面板枚举:Select=选关 / Daily=每日主页 / Game=对局 / Settle=结算(同视图内切换,不压 Router 栈)。</summary>
        enum Panel { Select, Daily, Game, Settle }

        WaterSortSession _session;   // 本视图会话快照(OnShow 取 Instance;OnDestroy 退订后不再引用)
        bool _leaving;               // 退模块流程已启动(防 OnHide 重入重复 ExitAsync)
        WaterSortLevelPack _pack;    // 本次会话题库缓存(选关渲染成功后赋值;选关/下一关同源取关)

        Transform _selectPanel, _dailyPanel, _gamePanel, _settlePanel;
        Transform _itemTemplate, _content;
        TextMeshProUGUI _resultText, _stepText;
        BoxButton _nextButton, _undoButton, _restartButton;
        BoxButton _hintButton, _extraTubeButton; // 金币消费点(见 OnHint/OnAddExtraTube)
        TextMeshProUGUI _rewardText;             // 结算首通奖励行(仅首通过关时 SetActive + 文案)
        TextMeshProUGUI _coinLabel;              // 对局顶栏金币余额(随消费/发奖就近刷新)
        TextMeshProUGUI _dailyStateText, _dailyStreakText; // 每日主页:今日状态 + 连续天数
        BoxButton _dailyPlayButton;              // 每日主页:开始/再玩今日挑战(题库就绪才可点)
        WaterSortDailyPack _dailyPack;           // 本次会话每日题库缓存(主页/开局同源取关)
        int _dailySeed;                          // 本局每日挑战归属日期种子(开局取;结算按此落完成,防跨零点错记)
        WaterSortTubeRack _rack;     // 试管区渲染与点击(OnCreate 挂到 TubeArea;随视图销毁)
        int _totalLevels;            // 题库总量(结算面板「下一关」放行判定:仅推进且有后关才可点)

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 返回键可关(关闭 = 退模块,见类头退出纪律)
            base.Awake();
        }

        protected override UniTask OnCreate()
        {
            // 面板与按钮句柄一次取齐;缺失节点静默降级(行为与布局解耦,prefab 缺失不崩)
            _selectPanel = FindInCard("SelectPanel");
            _dailyPanel = FindInCard("DailyPanel");
            _gamePanel = FindInCard("GamePanel");
            _settlePanel = FindInCard("SettlePanel");
            _content = FindInCard("SelectPanel/LevelScroll/Viewport/Content");
            _itemTemplate = FindInCard("SelectPanel/ItemTemplate");
            _resultText = FindInCard("SettlePanel/ResultText")?.GetComponent<TextMeshProUGUI>();
            _stepText = FindInCard("GamePanel/StepText")?.GetComponent<TextMeshProUGUI>();
            _nextButton = FindInCard("SettlePanel/NextButton")?.GetComponent<BoxButton>();
            _undoButton = FindInCard("GamePanel/BottomBar/UndoButton")?.GetComponent<BoxButton>();
            _restartButton = FindInCard("GamePanel/BottomBar/RestartButton")?.GetComponent<BoxButton>();
            _hintButton = FindInCard("GamePanel/BottomBar/HintButton")?.GetComponent<BoxButton>();
            _extraTubeButton = FindInCard("GamePanel/BottomBar/ExtraTubeButton")?.GetComponent<BoxButton>();
            _rewardText = FindInCard("SettlePanel/RewardText")?.GetComponent<TextMeshProUGUI>();
            _coinLabel = FindInCard("GamePanel/TopBar/CoinLabel")?.GetComponent<TextMeshProUGUI>();
            _dailyStateText = FindInCard("DailyPanel/StateText")?.GetComponent<TextMeshProUGUI>();
            _dailyStreakText = FindInCard("DailyPanel/StreakText")?.GetComponent<TextMeshProUGUI>();
            _dailyPlayButton = FindInCard("DailyPanel/PlayButton")?.GetComponent<BoxButton>();

            // 试管架:运行期挂到 TubeArea(热更组件不进 prefab 序列化,20 文档 §4 纪律同源)
            var tubeArea = FindInCard("GamePanel/TubeArea");
            if (tubeArea != null && tubeArea.GetComponent<WaterSortTubeRack>() == null)
                _rack = tubeArea.gameObject.AddComponent<WaterSortTubeRack>();
            if (_rack != null) _rack.PourRequested += OnPourRequested;

            Bind("SelectPanel/HubButton", LeaveToHub);
            Bind("SelectPanel/DailyButton", OnOpenDailyHome); // 每日入口(仅常规选关态可达)
            Bind("DailyPanel/BackButton", OnDailyBackToSelect);
            Bind("GamePanel/TopBar/BackButton", OnBackToSelect);
            Bind("SettlePanel/HubButton", LeaveToHub);
            Bind("SettlePanel/RetryButton", OnRetry);
            if (_nextButton != null) _nextButton.OnClick(OnNextLevel);
            if (_undoButton != null) _undoButton.OnClick(OnUndo);
            if (_restartButton != null) _restartButton.OnClick(OnRestart);
            if (_hintButton != null) _hintButton.OnClick(OnHint);
            if (_extraTubeButton != null) _extraTubeButton.OnClick(OnAddExtraTube);
            if (_dailyPlayButton != null) _dailyPlayButton.OnClick(OnDailyPlay);
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            _session = WaterSortSession.Instance; // 模块 OnEnter 先建会话再推本视图,恒非空
            _pack = null;                          // 新会话旧题库失效(重新走缓存加载,代价近零)
            SubscribeSession();
            if (_rack != null) _rack.SetSession(_session); // 试管架与会话绑定(本视图生命周期内不变)
            ApplyLanguage();
            if (_session != null && _session.IsDaily)
            {
                // 模块级每日入口(args="daily",WaterSortModule.OnEnter):直进每日主页
                // (题库缺失时主页内提示并回落常规选关,见 OpenDailyHome)
                _dailyPack = null;
                ShowPanel(Panel.Daily);
                await RenderDailyHomeAsync();
                return;
            }
            ShowPanel(Panel.Select);
            await RenderLevelSelect(); // 题库异步加载失败在方法内 toast,保持空列表可重进
        }

        protected override UniTask OnHide()
        {
            if (!_leaving)
            {
                // OnHide 仅由「本视图被 Pop」触发(见类头退出纪律):关闭即离开模块 → 复位模块状态,
                // 否则 Loader 认为模块仍在运行,玩家无法再次进入(watersort 卡 Active)。
                _leaving = true;
                if (_session != null && _session.IsInLevel) LogLevelAbandon(); // 对局中直接离开:埋点弃局
                ModuleLoader.Instance?.ExitAsync(WaterSortModule.ModuleId).Forget();
            }
            return UniTask.CompletedTask;
        }

        private new void OnDestroy()
        {
            UnsubscribeSession(); // 先退订会话事件,防旧会话实例残留引用(模块每次进入新建会话)
        }

        // ---- 面板切换 ---- //

        void ShowPanel(Panel p)
        {
            if (_selectPanel != null) _selectPanel.gameObject.SetActive(p == Panel.Select);
            if (_dailyPanel != null) _dailyPanel.gameObject.SetActive(p == Panel.Daily);
            if (_gamePanel != null) _gamePanel.gameObject.SetActive(p == Panel.Game);
            if (_settlePanel != null) _settlePanel.gameObject.SetActive(p == Panel.Settle);
        }

        // ---- 选关面板 ---- //

        /// <summary>
        /// 渲染选关列表:可点关号 ≤ 解锁数 + 1(前沿关;全清后不再扩展);关号与题库 id 一一对应
        /// (生成约定升序连续)。模板/容器缺失(编辑期)仅记警告,保持空列表不崩。
        /// </summary>
        async UniTask RenderLevelSelect()
        {
            ClearItems();
            _pack = await WaterSortLevelStore.LoadPackAsync();
            // 等待期间视图可能已被 Pop 销毁/退模块(异步续体访问已销毁节点会抛 MissingReference)
            if (this == null || _leaving) return;
            if (_pack == null || _pack.levels == null || _pack.levels.Count == 0)
            {
                ShowToast("watersort.toast.noLevels"); // 题库缺失属构建期错误,运行时不该出现
                return;
            }
            _totalLevels = _pack.levels.Count;
            if (_itemTemplate == null || _content == null) return; // prefab 未建阶段空安全
            _itemTemplate.gameObject.SetActive(false);             // 模板隐藏,仅作克隆源

            int unlocked = WaterSortProgressStore.UnlockedCount(WaterSortProgressStore.Load());
            int selectable = Mathf.Min(unlocked + 1, _totalLevels);
            const int cols = 5;
            const float gap = 12f;
            var tplRt = (RectTransform)_itemTemplate;
            float w = tplRt.rect.width, h = tplRt.rect.height;
            var contentRt = (RectTransform)_content;
            for (int i = 0; i < selectable; i++)
            {
                int levelNo = i + 1; // 局部拷贝,防闭包共享循环变量
                var item = Instantiate(_itemTemplate.gameObject, _content);
                item.name = "Item" + levelNo;
                item.SetActive(true);
                var rt = (RectTransform)item.transform;
                int col = i % cols, row = i / cols;
                rt.anchoredPosition = new Vector2(
                    (col - (cols - 1) * 0.5f) * (w + gap),
                    -(row * (h + gap) + h * 0.5f));
                var label = item.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null) label.text = levelNo.ToString();
                var btn = item.GetComponent<BoxButton>();
                if (btn != null) btn.OnClick(() => OnPickLevel(levelNo));
            }
            // 内容高度按行撑开(ScrollRect 滚动范围);宽度沿用容器
            contentRt.sizeDelta = new Vector2(contentRt.sizeDelta.x,
                Mathf.CeilToInt(selectable / (float)cols) * (h + gap) + gap);
        }

        void ClearItems()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }

        void OnPickLevel(int levelNo)
        {
            var level = _pack != null ? WaterSortLevelStore.FindById(_pack, levelNo) : null;
            StartGame(level); // 找不到(异常)在 StartGame 内 toast 兜底
        }

        // ---- 每日挑战(M2.3,WS-09) ---- //

        /// <summary>
        /// 会话整体换新(常规 ↔ 每日):旧会话退订销毁(模块每次进入新建的纪律同源),
        /// 静态 Instance 指向新会话后重订阅,试管架同步换绑。视图内完成,不弹 Router/不退模块。
        /// </summary>
        void SwitchSession(WaterSortSession next)
        {
            if (_session == next) return;
            UnsubscribeSession(); // 先摘旧会话事件
            _session = next;
            WaterSortSession.Instance = next; // 模块内其他取 Instance 的路径(理论无)也拿到新会话
            SubscribeSession();
            if (_rack != null) _rack.SetSession(_session);
        }

        /// <summary>选关页「每日挑战」入口:切每日会话 → 每日主页(异步拉题库后落状态文案)。</summary>
        void OnOpenDailyHome()
        {
            if (_session == null || _session.IsDaily) return; // 已在每日态(防重入)
            SwitchSession(new WaterSortSession(true));
            _dailyPack = null;
            ShowPanel(Panel.Daily);
            RenderDailyHomeAsync().Forget();
        }

        /// <summary>每日主页「返回」:切回常规会话 → 常规选关(网格重渲染幂等)。</summary>
        void OnDailyBackToSelect()
        {
            if (_session == null || !_session.IsDaily) return; // 仅每日态可回(常规态退出走 Hub)
            SwitchSession(new WaterSortSession(false));
            _dailyPack = null;
            ShowPanel(Panel.Select);
            RenderLevelSelect().Forget();
        }

        /// <summary>
        /// 每日主页渲染(进主页/题库首次就绪各一次):拉每日题库(缓存后常驻,后续近零开销),
        /// 拉取期间禁点开始钮(防空局);题库缺失/损坏 → toast 并回落常规选关(防空屏)。
        /// 就绪后按「今日状态/连续天数/按钮文案」落文案(L10n 语言感知)。
        /// </summary>
        async UniTask RenderDailyHomeAsync()
        {
            if (_dailyPlayButton != null) _dailyPlayButton.SetInteractable(false); // 题库就绪前禁点
            _dailyPack = await WaterSortDailyLevelStore.LoadPackAsync();
            if (this == null || _leaving) return; // 等待期视图被 Pop/退模块:续体勿触已销毁节点
            if (_dailyPack == null || _dailyPack.levels.Count == 0)
            {
                ShowToast("watersort.toast.noLevels"); // 每日题库缺失(构建期错误/资产损坏)
                if (_session != null && _session.IsDaily)
                {
                    SwitchSession(new WaterSortSession(false));
                    ShowPanel(Panel.Select);
                    RenderLevelSelect().Forget(); // 回落常规(每日功能受损但常规可玩)
                }
                return;
            }
            ApplyDailyTexts();
        }

        /// <summary>落每日主页文案:今日是否完成/连续 N 天/开始或再玩(与状态联动)。</summary>
        void ApplyDailyTexts()
        {
            if (_session == null || !_session.IsDaily) return;
            var now = WaterSortDailyStore.UtcNow();
            bool done = WaterSortDailyStore.IsDone(WaterSortDailySeed.SeedOf(now));
            if (_dailyStateText != null)
                _dailyStateText.text = L10n.Get(done ? "watersort.daily.state.done" : "watersort.daily.state.new");
            if (_dailyStreakText != null)
                _dailyStreakText.text = L10n.Format("watersort.daily.streak",
                    WaterSortDailyStore.Streak(WaterSortDailyStore.Load(), now));
            if (_dailyPlayButton != null)
                _dailyPlayButton.SetInteractable(true);
            SetLabel("DailyPanel/PlayButton",
                L10n.Get(done ? "watersort.daily.replay" : "watersort.daily.play"));
        }

        /// <summary>开始/再玩今日挑战:按当前 UTC 日期取关(缺失兜底备用池,GetForSeed 内确定性取)。</summary>
        void OnDailyPlay()
        {
            if (_session == null || !_session.IsDaily) return;
            if (_dailyPack == null)
            {
                RenderDailyHomeAsync().Forget(); // 极端路径(题库缓存被清):重拉一次后由主页状态引导
                return;
            }
            int seed = WaterSortDailySeed.SeedOf(WaterSortDailyStore.UtcNow());
            var level = WaterSortDailyLevelStore.GetForSeed(_dailyPack, seed, out _);
            if (level == null)
            {
                ShowToast("watersort.toast.noLevels");
                return;
            }
            _dailySeed = seed; // 结算按本局归属日期落完成(跨零点对局不错记)
            StartGame(level);  // 开局成功切对局;结算/返回语义由会话 IsDaily 收敛
        }

        // ---- 对局面板 ---- //

        /// <summary>进对局:开局成功切面板;失败(题库越界/损坏)toast 并留在选关。</summary>
        void StartGame(WaterSortLevelData level)
        {
            if (_session == null || !_session.StartLevel(level))
            {
                ShowToast("watersort.toast.badLevel");
                return;
            }
            ShowPanel(Panel.Game);
            ApplyLanguage(); // 标题切「第 N 关」文案
            RefreshTubeArea();
        }

        void OnBackToSelect()
        {
            // 放弃本局进度回前层(模块仍 Active,与 LeaveToHub 退出模块是两条独立路径):
            // 每日对局 → 每日主页(保留每日会话;ApplyDailyTexts 同步刚结算/放弃后的完成态与按钮文案);
            // 常规对局 → 选关
            if (_session != null && _session.IsDaily)
            {
                ShowPanel(Panel.Daily);
                ApplyDailyTexts();
                return;
            }
            ShowPanel(Panel.Select);
        }

        void OnUndo()
        {
            if (_session != null && _session.Undo()) RefreshTubeArea();
        }

        void OnRestart()
        {
            if (_session == null || !_session.IsInLevel) return;
            _session.Restart();
            RefreshTubeArea();
        }

        /// <summary>
        /// 试管区刷新唯一接缝:倒水/撤销/重开/换关后调用——
        /// 先摘选中再按盘面重建试管,同步步数文本(非法倒水抖动路径不经此方法,选中保留可再试)。
        /// </summary>
        void RefreshTubeArea()
        {
            if (_rack == null) return; // 无试管架(prefab 缺 TubeArea)时静默跳过,行为不崩
            _rack.ClearSelection();    // 提交型重建统一摘选中(盘面已变,残留高亮无意义)
            _rack.Refresh();
            if (_stepText != null && _session != null)
                _stepText.text = L10n.Format("watersort.step", _session.MoveCount);
            UpdateCoinLabel();       // 玩法内无全局金币事件(盒内暂无钱包组件),随每次盘面刷新就近同步
            SyncConsumeButtons();    // 上限/余额变化驱动提示与加管按钮禁用态(花钱点位不允许再点)
        }

        /// <summary>试管点击裁决:合法→会话推进(BoardChanged 驱动本区刷新);非法→源管抖动,选中保留可再试目标。</summary>
        void OnPourRequested(int src, int dst)
        {
            if (_session == null || !_session.IsInLevel) return; // 面板切换竞态兜底
            if (!_session.TryPour(src, dst)) _rack?.ShakeTube(src);
        }

        /// <summary>
        /// 提示(金币直购,WS-06):单价/每关上限走 WaterSortConfig。成功才扣币 —— 会话求解失败
        /// (玩家走入无解死角/超时)返回 false 时不收费,仅 toast 引导撤销。扣币后余额/按钮态立即刷新。
        /// </summary>
        void OnHint()
        {
            if (_session == null || !_session.IsInLevel) return; // 结算/选关面板按钮不可达,双保险
            if (_session.HintsUsed >= WaterSortConfig.HintLimitPerLevel) return;
            if ((ServiceLocator.Save?.Coins ?? 0) < WaterSortConfig.HintPriceCoins)
            {
                ShowToast("watersort.toast.noCoins");
                return;
            }
            if (!_session.TryHint())
            {
                ShowToast("watersort.toast.hintFail"); // 无可解续路:诚实提示引导撤销,不向死局收币
                return;
            }
            TrySpendCoins("hint", WaterSortConfig.HintPriceCoins);
        }

        /// <summary>额外空瓶(金币直购,WS-06/13):+1 支空管(每关上限走配置);同上成功才扣币。</summary>
        void OnAddExtraTube()
        {
            if (_session == null || !_session.IsInLevel) return;
            if (_session.ExtraTubesUsed >= WaterSortConfig.ExtraTubeLimitPerLevel) return;
            if ((ServiceLocator.Save?.Coins ?? 0) < WaterSortConfig.ExtraTubePriceCoins)
            {
                ShowToast("watersort.toast.noCoins");
                return;
            }
            if (!_session.TryAddExtraTube()) return; // 上限内恒成功;失败静默(防御,理论不可达)
            TrySpendCoins("extra_tube", WaterSortConfig.ExtraTubePriceCoins);
        }

        // ---- 结算面板 ---- //

        void OnLevelSolved()
        {
            if (_session == null) return;
            if (_session.IsDaily)
            {
                // 每日挑战(WS-09):不发首通奖、不推进常规解锁,只落「今日完成」(Streak 由 WaterSortDailyStore
                // 从 doneSeeds 推导)——归属按开局种子 _dailySeed(开局时取 UTC 日),跨零点对局不错记。
                if (_dailySeed > 0) WaterSortDailyStore.MarkDone(_dailySeed);
                ShowPanel(Panel.Settle);
                if (_resultText != null)
                    _resultText.text = L10n.Format("watersort.settle.result",
                        _session.MoveCount, DifficultyText(_session.Difficulty));
                if (_rewardText != null) _rewardText.gameObject.SetActive(false); // 每日挑战无首通奖励行
                UpdateCoinLabel();
                if (_nextButton != null) _nextButton.SetInteractable(false); // 每日仅一关:不提供「下一关」
                return;
            }
            // 首通推进(WS-04:解锁只认首通;重玩不推进)+ 首通发奖(WS-08:奖励曲线在 WaterSortConfig,
            // 仅首通入账 box.coins,重玩/已解锁关卡不发——解耦 RecordFirstWin 返回值复用为推进与发奖的共同信号)。
            bool firstWin = WaterSortProgressStore.RecordFirstWin(_session.LevelId);
            int reward = 0;
            if (firstWin)
            {
                reward = WaterSortConfig.FirstWinReward(_session.LevelId);
                if (reward > 0) GrantCoins(reward);
            }
            ShowPanel(Panel.Settle);
            if (_resultText != null)
                _resultText.text = L10n.Format("watersort.settle.result",
                    _session.MoveCount, DifficultyText(_session.Difficulty));
            if (_rewardText != null)
            {
                // 首通显示奖励行;重玩整行隐藏(发奖理由清晰,防误导性重复奖励展示)
                _rewardText.text = reward > 0 ? L10n.Format("watersort.settle.reward", reward) : "";
                _rewardText.gameObject.SetActive(reward > 0);
            }
            UpdateCoinLabel(); // 发奖即刷新(顶栏在结算面板不可见,回对局时已是最新)
            bool nextUnlocked = firstWin && _session.LevelId < _totalLevels; // 有后关且本次推进才可点
            if (_nextButton != null) _nextButton.SetInteractable(nextUnlocked);
        }

        void OnNextLevel()
        {
            var level = _pack != null ? WaterSortLevelStore.FindById(_pack, _session.LevelId + 1) : null;
            if (level != null)
            {
                if (_session.StartLevel(level)) // 换关:旧会话历史/过关标记随 StartLevel 复位
                {
                    ShowPanel(Panel.Game);
                    ApplyLanguage();
                    RefreshTubeArea();
                }
            }
            if (level == null || !_session.IsInLevel) ShowToast("watersort.toast.noLevels"); // 题库尾部兜底
        }

        void OnRetry()
        {
            if (_session == null || !_session.IsInLevel) return;
            _session.Restart(); // 重玩本关
            ShowPanel(Panel.Game);
            RefreshTubeArea();
        }

        // ---- 退出模块(唯一出口:Pop 本视图,OnHide 收口 ExitAsync) ---- //

        void LeaveToHub()
        {
            if (_leaving) return;
            var router = UIService.Instance?.Router;
            if (router != null) router.PopAsync().Forget();
        }

        // ---- 会话订阅/文案 ---- //

        void SubscribeSession()
        {
            if (_session == null) return;
            _session.BoardChanged += OnBoardChanged;
            _session.LevelSolved += OnLevelSolved;
        }

        void UnsubscribeSession()
        {
            if (_session == null) return;
            _session.BoardChanged -= OnBoardChanged;
            _session.LevelSolved -= OnLevelSolved;
        }

        void OnBoardChanged() => RefreshTubeArea();

        void ApplyLanguage()
        {
            SetText("SelectPanel/Title", L10n.Get("watersort.select.title"));
            SetLabel("SelectPanel/DailyButton", L10n.Get("watersort.daily.title")); // 每日入口按钮(常规选关页)
            // 每日主页顶栏(进模块必经 ApplyLanguage;语种模块内不切换,一次到位)
            SetText("DailyPanel/Title", L10n.Get("watersort.daily.title"));
            SetLabel("DailyPanel/BackButton", L10n.Get("game.back"));
            // 对局标题:仅对局中显示,StartGame/换关均先经 ApplyLanguage 到位;
            // 每日挑战显示专名「每日挑战」(当日仅一关、无序号),常规显示「第 N 关 · 难度」
            if (_session != null && _session.IsInLevel)
                SetText("GamePanel/TopBar/GameTitle", _session.IsDaily
                    ? L10n.Get("watersort.daily.title")
                    : L10n.Format("watersort.level.title",
                        _session.LevelId, DifficultyText(_session.Difficulty)));
            UpdateCoinLabel(); // M1.4:入场/换语言先刷一次(消费点各自刷新,见 TrySpendCoins/GrantCoins)
            SetText("SettlePanel/Title", L10n.Get("watersort.settle.title"));
            SetLabel("SelectPanel/HubButton", L10n.Get("game.back")); // 复用既有键(返回)
            SetLabel("GamePanel/TopBar/BackButton", L10n.Get("game.back"));
            SetLabel("GamePanel/BottomBar/UndoButton", L10n.Get("game.undo"));
            SetLabel("GamePanel/BottomBar/RestartButton", L10n.Get("watersort.btn.restart"));
            SetLabel("GamePanel/BottomBar/HintButton", L10n.Get("watersort.btn.hint"));
            SetLabel("GamePanel/BottomBar/ExtraTubeButton", L10n.Get("watersort.btn.tube"));
            SetLabel("SettlePanel/RetryButton", L10n.Get("watersort.btn.retry"));
            SetLabel("SettlePanel/HubButton", L10n.Get("settlement.home")); // 复用既有键(返回菜单)
            SetLabel("SettlePanel/NextButton", L10n.Get("watersort.btn.next"));
        }

        string CoinText()
        {
            long coins = ServiceLocator.Save != null ? ServiceLocator.Save.Coins : 0;
            return L10n.Format("watersort.coins", coins);
        }

        void UpdateCoinLabel()
        {
            if (_coinLabel != null) _coinLabel.text = CoinText();
        }

        /// <summary>
        /// 金币扣减(盒内唯一账本 box.coins,ISaveService.Coins;余额充足判定在调用点完成)。
        /// 玩法内暂无钱包组件与全局余额事件 → 扣完立即刷标签与按钮态;save 未就绪(测试/异常上下文)回 false。
        /// </summary>
        bool TrySpendCoins(string reason, int price)
        {
            var save = ServiceLocator.Save;
            if (save == null || save.Coins < price) return false;
            save.Coins -= price;
            save.Save(); // box.* 变更需显式落盘(接口契约:仅 SetModule 自动落盘)
            ServiceLocator.Analytics?.LogEvent("watersort.coin_spend", reason, price); // 埋点 source=玩法+动作
            UpdateCoinLabel();
            SyncConsumeButtons();
            return true;
        }

        /// <summary>金币入账(首通奖励 WS-08);amount ≤ 0 或服务未就绪跳过。入账后余额即刷。</summary>
        void GrantCoins(int amount)
        {
            var save = ServiceLocator.Save;
            if (save == null || amount <= 0) return;
            save.Coins += amount;
            save.Save();
            ServiceLocator.Analytics?.LogEvent("watersort.coin_reward", "amount", amount);
            UpdateCoinLabel();
        }

        /// <summary>
        /// 提示/加管按钮可用态:局内 + 未达每关上限 + 余额充足。
        /// 余额只在消费点变化(盒内无其他货币来源/消耗),上限只在 StartLevel/Undo(加管)变化,
        /// 因此 RefreshTubeArea(盘面变更统一接缝)与本类扣币点调用即覆盖全部变化路径。
        /// </summary>
        void SyncConsumeButtons()
        {
            bool inLevel = _session != null && _session.IsInLevel;
            long coins = ServiceLocator.Save != null ? ServiceLocator.Save.Coins : 0;
            if (_hintButton != null)
                _hintButton.SetInteractable(inLevel
                    && _session.HintsUsed < WaterSortConfig.HintLimitPerLevel
                    && coins >= WaterSortConfig.HintPriceCoins);
            if (_extraTubeButton != null)
                _extraTubeButton.SetInteractable(inLevel
                    && _session.ExtraTubesUsed < WaterSortConfig.ExtraTubeLimitPerLevel
                    && coins >= WaterSortConfig.ExtraTubePriceCoins);
        }

        string DifficultyText(WaterSortDifficulty d)
        {
            return d switch
            {
                WaterSortDifficulty.Easy => L10n.Get("diff.easy"),
                WaterSortDifficulty.Medium => L10n.Get("diff.medium"),
                _ => L10n.Get("diff.hard"),
            };
        }

        void ShowToast(string key)
        {
            BoxToast.Show(L10n.Get(key)); // 全局 toast(Toast 层不入 Router 栈);无实例时内部自建
        }

        void SetText(string path, string text)
        {
            var t = FindInCard(path)?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }

        void SetLabel(string path, string text)
        {
            var t = FindInCard(path + "/Label")?.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }

        void Bind(string path, Action handler)
        {
            var btn = FindInCard(path)?.GetComponent<BoxButton>();
            if (btn != null) btn.OnClick(handler);
        }

        void LogLevelAbandon() { } // M1 埋点占位:弃局(watersort.level_abandon)字典随 M1.4 落地
    }
}
