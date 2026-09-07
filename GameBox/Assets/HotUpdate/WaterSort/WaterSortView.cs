using System;
using System.Collections.Generic;
using Box.HotUpdate.Core.Onboarding;
using Box.ModuleFramework;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序主视图(全屏弹层;Layer=Popup 让 Android 返回键可关)。两面板内部切换,不压 Router 视图:
    /// 对局(入口直达)→ 胜利弹层自动跳关。选关页/结算页已删(2026-09-06 改造):
    /// 进模块 = 续玩半局或前沿关;过关 = 弹 ws_win_title 图 3 秒后自动进下一关(尾关循环回第 1 关,
    /// 每日回主页),首通发币/每日完成落盘/过关计数在过关瞬间照常完成。
    /// 游戏页按钮全部换皮肤图(Assets/Modules/WaterSort/UI/ws_btn_*.png),原生文字下线(图缺失时文字兜底)。
    ///
    /// 挂载纪律(20 文档 §4):热更组件不进场景直挂、不序列化进 prefab —— prefab 根挂 AOT HotViewBinder
    /// (viewTypeFullName 指向本类动态 AddComponent);prefab 由 M1.3 WaterSortViewSetup 生成,本类只写行为。
    ///
    /// Prefab 节点契约(M1.3 生成器严格按此命名;缺失节点一律空安全降级):
    ///   GamePanel/TopBar/BackButton         返回(常规=退模块回大厅 / 每日=每日主页,放弃本局进度;皮肤图钮)
    ///   GamePanel/TopBar/GameTitle | CoinLabel   关卡标题 / 金币余额(动态文字,保留原生 TMP)
    ///   GamePanel/StepText                  步数(动态文字,保留原生 TMP)
    ///   GamePanel/TubeArea                  试管容器(本视图 AddComponent WaterSortTubeRack 代码绘制+点击)
    ///   GamePanel/BottomBar/UndoButton|RestartButton|HintButton|ExtraTubeButton
    ///                     操作四钮(皮肤方形图承载图标;Label 原生文字兜底 —— 皮肤图加载成功即隐藏)
    ///   DailyPanel/Title | StateText | StreakText | PlayButton | BackButton  每日主页(M2.3;完成态/Streak/重玩)
    ///   AdPanel/Card/MessageText | ConfirmButton | CancelButton  激励确认面板(M3.1 内嵌,见退出纪律)
    ///   WinOverlay                           运行期生成胜利弹层(遮罩 + ws_win_title 图;见 BuildWinOverlay)
    ///
    /// 选关页/结算页已删(2026-09-06 改造):每日挑战入口隐藏,仅模块 args="daily" 可达
    /// (WaterSortModule.OnEnter);过关不再出结算面板 —— 弹胜利图 3 秒后自动进下一关
    /// (常规:下一关,尾关循环回第 1 关 / 每日:回每日主页),首通发币与完成落盘在过关瞬间照常。
    /// 固定功能文字(返回/撤销/重开/提示/加管)由皮肤图承载(图缺失文字兜底);
    /// 动态文字(第 N 关标题/步数/金币余额/每日主页状态)无图可替,保留原生 TMP(深色底白字可读)。
    ///
    /// 每日挑战(M2.3)模式语义:视图内模式 = 会话标记(WaterSortSession.IsDaily);「每日 → 常规」
    /// 经 Session 实例整体换新(旧实例退订销毁,见 SwitchSession),不弹 Router、不退出模块;
    /// 模块级入口 args="daily" 直落每日主页。每日主页 Back 回常规对局 = 再换回常规会话。
    /// 对局共用面板,差异全部收敛在:标题文案、完成落盘(每日 → dailyDoneSeeds,
    /// 常规 → firstWinLevels+发币)、过关后去向(每日 → 主页 / 常规 → 下一关)。
    ///
    /// 道具双通道(M3.1,WS-08/12):提示/空瓶按钮点击 → 金币充足 = 金币直购(M1 行为,成功才扣币);
    /// 金币不足 = 激励确认面板(消息说明用途/上限)→ 确认后去广告用户伪视频直发(IsAdsRemoved,
    /// WS-13)或看完整激励视频 → 免费发放。发放层共用 Session 方法(计数/盘面效果),扣币与否在调用点。
    ///
    /// 退出纪律(与 WaterSortModule.OnExit 配合):本视图是模块压入 Router 的唯一自属视图,对局/
    /// 每日主页都是面板切换不压 Router 弹窗(M3.1 激励确认 = 玩法内嵌 AdPanel 子树,刻意不用 Router:
    /// PushAsync 覆盖下层会 HideAsync 触发本视图 OnHide = 退模块,故激励面板零路由生命周期,
    /// 遮罩拦点击、关闭只翻自身)。OnHide 只会在「真的被 Pop」时触发(主动 HubButton 或返回键)
    /// ——即用户离开模块的唯一信号 → ExitAsync 复位模块状态(防卡 Active)。
    /// </summary>
    public sealed class WaterSortView : UIView
    {
        /// <summary>面板枚举:Daily=每日主页 / Game=对局(同视图内切换,不压 Router 栈)。</summary>
        enum Panel { Daily, Game }

        WaterSortSession _session;   // 本视图会话快照(OnShow 取 Instance;OnDestroy 退订后不再引用)
        bool _leaving;               // 退模块流程已启动(防 OnHide 重入重复 ExitAsync)
        WaterSortLevelPack _pack;    // 本次会话题库缓存(直达/过关跳关同源取关)

        Transform _dailyPanel, _gamePanel;
        TextMeshProUGUI _stepText;
        BoxButton _undoButton, _restartButton;
        BoxButton _hintButton, _extraTubeButton; // 双通道道具钮(金币直购 | 激励兜底,见 OnHint/OnAddExtraTube)
        TextMeshProUGUI _coinLabel;              // 对局顶栏金币余额(随消费/发奖就近刷新)
        GameObject _winOverlay;                  // 过关胜利弹层(运行期代码生成:遮罩 + ws_win_title;见 BuildWinOverlay)
        Transform _winTitle;                     // 胜利标题节点(弹出动画对象)
        bool _winShowing;                        // 胜利弹层播放中(防重入;3 秒自动跳关)
        readonly Dictionary<string, Sprite> _skinSprites = new Dictionary<string, Sprite>(); // 皮肤图缓存(见 LoadSkin)
        Transform _adPanel;                      // 内嵌激励确认面板(WS-12;不进 Router,见类头退出纪律)
        TextMeshProUGUI _adMessage;              // 面板消息(点位文案 + 次数上限参数)
        Action _adGrant;                         // 确认后的发放动作(点位闭包注入;取消/面板关闭即置空)
        string _adPoint;                         // 当前点位名(激励完成埋点 placement,04 文档 §5)
        TextMeshProUGUI _dailyStateText, _dailyStreakText; // 每日主页:今日状态 + 连续天数
        BoxButton _dailyPlayButton;              // 每日主页:开始/再玩今日挑战(题库就绪才可点)
        WaterSortDailyPack _dailyPack;           // 本次会话每日题库缓存(主页/开局同源取关)
        int _dailySeed;                          // 本局每日挑战归属日期种子(开局取;完成按此落盘,防跨零点错记)
        bool _solvedPending;                     // 通关被倒水动画压住:PourCompleted 收尾再播胜利弹层(见 OnLevelSolved)
        WaterSortTubeRack _rack;     // 试管区渲染与点击(OnCreate 挂到 TubeArea;随视图销毁)
        TutorialFlow _tut;           // 新手引导流程(M3.3,WS-14;null=未开播/已收尾,见「新手引导」区)
        int _tutNonMergePours;       // S2 放行计数:无聚合演示对时连续普通倒水次数(见 TutorialS2GracePours)
        int _tutPairA = -1, _tutPairB = -1; // 当前盘「同色聚合」演示对(试管索引;-1=无,随盘面刷新重扫)

        // 皮肤资源地址(21 文档 §6.1 约定;WaterSortSkinImporter 扫描即入组自愈,与 ws_tube 同管线)
        const string SkinBack = "WaterSort/UI/ws_btn_back_flat";
        const string SkinUndo = "WaterSort/UI/ws_btn_undo_flat";
        const string SkinHint = "WaterSort/UI/ws_btn_hint_flat";
        const string SkinRestart = "WaterSort/UI/ws_btn_restart_flat";
        const string SkinExtra = "WaterSort/UI/ws_btn_extra_flat";
        const string SkinWinTitle = "WaterSort/UI/ws_win_title";

        const float WinPopSeconds = 0.35f; // 胜利标题弹入动画时长(EaseOutBack 回弹)
        const float WinHoldSeconds = 3f;   // 胜利弹层停留时长(需求:过 3 秒自动进下一关)

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 返回键可关(关闭 = 退模块,见类头退出纪律)
            base.Awake();
        }

        protected override UniTask OnCreate()
        {
            // 面板与按钮句柄一次取齐;缺失节点静默降级(行为与布局解耦,prefab 缺失不崩)
            _dailyPanel = FindInCard("DailyPanel");
            _gamePanel = FindInCard("GamePanel");
            _stepText = FindInCard("GamePanel/StepText")?.GetComponent<TextMeshProUGUI>();
            _undoButton = FindInCard("GamePanel/BottomBar/UndoButton")?.GetComponent<BoxButton>();
            _restartButton = FindInCard("GamePanel/BottomBar/RestartButton")?.GetComponent<BoxButton>();
            _hintButton = FindInCard("GamePanel/BottomBar/HintButton")?.GetComponent<BoxButton>();
            _extraTubeButton = FindInCard("GamePanel/BottomBar/ExtraTubeButton")?.GetComponent<BoxButton>();
            _coinLabel = FindInCard("GamePanel/TopBar/CoinLabel")?.GetComponent<TextMeshProUGUI>();
            _adPanel = FindInCard("AdPanel");
            _adMessage = FindInCard("AdPanel/Card/MessageText")?.GetComponent<TextMeshProUGUI>();
            _dailyStateText = FindInCard("DailyPanel/StateText")?.GetComponent<TextMeshProUGUI>();
            _dailyStreakText = FindInCard("DailyPanel/StreakText")?.GetComponent<TextMeshProUGUI>();
            _dailyPlayButton = FindInCard("DailyPanel/PlayButton")?.GetComponent<BoxButton>();

            // 试管架:运行期挂到 TubeArea(热更组件不进 prefab 序列化,20 文档 §4 纪律同源)
            var tubeArea = FindInCard("GamePanel/TubeArea");
            if (tubeArea != null && tubeArea.GetComponent<WaterSortTubeRack>() == null)
                _rack = tubeArea.gameObject.AddComponent<WaterSortTubeRack>();
            if (_rack != null)
            {
                _rack.PourRequested += OnPourRequested;
                _rack.PourCompleted += OnPourCompleted; // 倒水动画收尾(重建后)的 HUD/引导同步
            }

            Bind("DailyPanel/BackButton", OnDailyBackToRegular);
            Bind("GamePanel/TopBar/BackButton", OnTopBarBack); // 常规 = 退模块回大厅(选关/结算页已删,唯一显式出口)
            if (_undoButton != null) _undoButton.OnClick(OnUndo);
            if (_restartButton != null) _restartButton.OnClick(OnRestart);
            if (_hintButton != null) _hintButton.OnClick(OnHint);
            if (_extraTubeButton != null) _extraTubeButton.OnClick(OnAddExtraTube);
            if (_dailyPlayButton != null) _dailyPlayButton.OnClick(OnDailyPlay);
            Bind("AdPanel/Card/ConfirmButton", OnAdConfirm); // 激励确认:关闭面板 → 伪视频直发/真激励
            Bind("AdPanel/Card/CancelButton", OnAdCancel);   // 取消:只关面板(不发放)
            BuildGameSkinAsync().Forget(); // 皮肤接入:按钮换图 + 胜利标题预热(异步,图缺失保持文字兜底)
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            // 缓存视图复推(UIRouter 命中缓存直接 OnShow,见 Router.PushAsync):复位退模块标记。
            // 不复位则异步渲染续体(RenderLevelSelect/RenderDailyHomeAsync 的 _leaving 早退守卫)
            // 误判"正在退出"而返回 → 二次进入选关空列表/按钮禁点(Bug 清单 7 伴随根因)。
            _leaving = false;
            _solvedPending = false; // 新入口新会话:上一局压住标记不可能再有,防御性复位
            // 胜利弹层复位:视图缓存复用(退模块时弹层可能停在中途,见 PlayWinThenAdvanceAsync 早退守卫),
            // 重进必须清 _winShowing 与弹层显隐,否则输入锁/遮罩残留卡死新会话
            _winShowing = false;
            if (_winOverlay != null) _winOverlay.SetActive(false);
            if (_winTitle != null) _winTitle.localScale = Vector3.one;
            _session = WaterSortSession.Instance; // 模块 OnEnter 先建会话再推本视图,恒非空
            _pack = null;                          // 新会话旧题库失效(重新走缓存加载,代价近零)
            SubscribeSession();
            if (_rack != null) _rack.SetSession(_session); // 试管架与会话绑定(本视图生命周期内不变)
            ApplyLanguage();
            if (_session != null && _session.IsDaily)
            {
                // 模块级每日入口(args="daily",WaterSortModule.OnEnter):直进每日主页
                // (题库缺失时主页内提示并回落常规对局,见 RenderDailyHomeAsync)
                _dailyPack = null;
                ShowPanel(Panel.Daily);
                await RenderDailyHomeAsync();
                return;
            }
            // 入口直达(一期改造):常规进入不再落选关页 —— 续玩半局/进前沿关;
            // 题库缺失回落选关(方法内兜底),选关自此为对局内二级入口(顶栏返回可达)
            await EnterCurrentLevelAsync();
        }

        protected override UniTask OnHide()
        {
            if (!_leaving)
            {
                // OnHide 仅由「本视图被 Pop」触发(见类头退出纪律):关闭即离开模块 → 复位模块状态,
                // 否则 Loader 认为模块仍在运行,玩家无法再次进入(watersort 卡 Active)。
                _leaving = true;
                CancelTutorial(); // 退模块必摘引导(状态保留 InProgress,重进第 1 关续播)
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
            if (_dailyPanel != null) _dailyPanel.gameObject.SetActive(p == Panel.Daily);
            if (_gamePanel != null) _gamePanel.gameObject.SetActive(p == Panel.Game);
            if (p != Panel.Game) CancelTutorial(); // 引导只在对局面板存在(离开 = 中断,状态保留下次续播)
        }

        // ---- 入口直达 ---- //

        /// <summary>
        /// 入口直达:进模块/每日返回后落点 —— 有半局快照则续玩该关,否则进前沿关
        /// (解锁数+1,全清后压到最后一关)。题库缺失 = 构建期错误 → toast 后留空壳对局面板(返回键可退)。
        /// </summary>
        async UniTask EnterCurrentLevelAsync()
        {
            _pack = await WaterSortLevelStore.LoadPackAsync();
            // 等待期间视图可能已被 Pop 销毁/退模块(异步续体访问已销毁节点会抛 MissingReference)
            if (this == null || _leaving) return;
            if (_pack == null || _pack.levels == null || _pack.levels.Count == 0)
            {
                ShowToast("watersort.toast.noLevels");
                ShowPanel(Panel.Game);
                return;
            }
            var run = WaterSortProgressStore.LoadRun();
            int total = _pack.levels.Count;
            int target = run != null && WaterSortLevelStore.FindById(_pack, run.levelId) != null
                ? run.levelId // 有未完成的半局 → 回到上次玩的那关(StartGame 内自动续盘)
                : Mathf.Min(WaterSortProgressStore.UnlockedCount(WaterSortProgressStore.Load()) + 1, total);
            StartGame(WaterSortLevelStore.FindById(_pack, target));
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

        /// <summary>每日主页「返回」:切回常规会话 → 直达常规对局(续玩半局/前沿关,同模块入口规则)。</summary>
        void OnDailyBackToRegular()
        {
            if (_session == null || !_session.IsDaily) return; // 仅每日态可回(常规态退出走 Hub)
            SwitchSession(new WaterSortSession(false));
            _dailyPack = null;
            EnterCurrentLevelAsync().Forget();
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
                    EnterCurrentLevelAsync().Forget(); // 回落常规对局(每日功能受损但常规可玩)
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

        /// <summary>进对局:开局成功切面板;失败(题库越界/损坏)toast 并留在原面板。
        /// 半局恢复(二期):存在关号匹配的快照则续玩(ResumedFromSave),全新开局则清快照槽。</summary>
        void StartGame(WaterSortLevelData level)
        {
            // 每日挑战无半局语义,不吃快照(防常规局快照串进每日会话)
            var run = level != null && _session != null && !_session.IsDaily
                ? WaterSortProgressStore.LoadRun() : null;
            if (_session == null || !_session.StartLevel(level, run))
            {
                ShowToast("watersort.toast.badLevel");
                return;
            }
            // 每日开局不清常规半局槽(每日无半局语义,但槽里可能是常规局进度,不能误清)
            if (!_session.IsDaily && !_session.ResumedFromSave) WaterSortProgressStore.ClearRun();
            ShowPanel(Panel.Game);
            ApplyLanguage(); // 标题切「第 N 关」文案
            _solvedPending = false; // 防残挂:上一局若有未消费的压住标记,开局即清(正常不可达,防御路径)
            RefreshTubeArea();
            MaybeStartTutorial(); // 常规第 1 关首次/中断重进:开播新手引导(WS-14)
        }

        /// <summary>对局顶栏「返回」:每日对局 → 每日主页(完成态/按钮文案同步);
        /// 常规对局 → 退模块回大厅(选关/结算页已删,本钮即唯一显式出口;半局已随盘面落盘不丢进度)。</summary>
        void OnTopBarBack()
        {
            if (_winShowing) return; // 胜利跳关流程中:交给自动跳关,防提前退出/切面板打断
            if (_session != null && _session.IsDaily)
            {
                ShowPanel(Panel.Daily);
                ApplyDailyTexts();
                return;
            }
            LeaveToHub();
        }

        void OnUndo()
        {
            if (_winShowing) return; // 胜利弹层期输入锁(遮罩亦拦点击,双保险)
            if (_rack != null && _rack.IsAnimating) return; // 倒水动画中锁操作(防动画中途盘面再变)
            if (_session != null && _session.Undo()) RefreshTubeArea();
        }

        void OnRestart()
        {
            if (_winShowing) return; // 胜利弹层期输入锁
            if (_session == null || !_session.IsInLevel) return;
            if (_rack != null && _rack.IsAnimating) return;
            // 重开 = 放弃半局(先清快照,防重进复活旧盘;Restart 强制全新盘)。
            // 仅常规局动快照槽:每日对局重开不得误清常规半局(每日无半局语义,槽内可能是常规局进度)
            if (!_session.IsDaily) WaterSortProgressStore.ClearRun();
            _session.Restart();
            RefreshTubeArea();
        }

        /// <summary>
        /// 试管区刷新唯一接缝:倒水/撤销/重开/换关后调用——
        /// 先摘选中再按盘面重建试管,同步步数文本。倒水走动画管道:动画期间本方法被
        /// IsAnimating 挂起(盘面已落子,视觉由动画呈现),收尾经 PourCompleted 统一补同步。
        /// </summary>
        void RefreshTubeArea()
        {
            if (_rack == null) return; // 无试管架(prefab 缺 TubeArea)时静默跳过,行为不崩
            if (_rack.IsAnimating) return; // 倒水动画中:挂起,动画收尾经 OnPourCompleted 补同步
            _rack.ClearSelection();    // 提交型重建统一摘选中(盘面已变,残留高亮无意义)
            _rack.Refresh();
            SyncHudAfterBoardRefresh();
        }

        /// <summary>盘面变化后的 HUD/引导同步(试管区重建尾部与倒水动画收尾共用)。</summary>
        void SyncHudAfterBoardRefresh()
        {
            if (_stepText != null && _session != null)
                _stepText.text = L10n.Format("watersort.step", _session.MoveCount);
            UpdateCoinLabel();       // 玩法内无全局金币事件(盒内暂无钱包组件),随每次盘面刷新就近同步
            SyncConsumeButtons();    // 上限/余额变化驱动提示与加管按钮禁用态(花钱点位不允许再点)
            TutorialAfterBoardRefresh(); // 引导 S2 聚合演示对随盘面漂移:刷新后重扫重定位(M3.3)
        }

        /// <summary>倒水动画收尾(试管架已重建到真实盘面):补做被动画挂起的 HUD/引导同步;
        /// 若动画期间盘面已解(通关),此刻才弹结算 —— 结算必须等最后一个动作演完。</summary>
        void OnPourCompleted()
        {
            SyncHudAfterBoardRefresh();
            if (_solvedPending)
            {
                _solvedPending = false;
                OnLevelSolved();
            }
        }

        /// <summary>试管点击执行:试管架已按 LegalMoves 预判,请求恒为合法移动。流程 = 先上动画锁 →
        /// TryPour 落子(BoardChanged 刷新被锁挂起)→ 播四段式倒水动画,收尾重建 + HUD 同步。
        /// 引导局(M3.3)步进照旧即时上报。防御分支(TryPour 竞态失败,理论不可达):解锁并抖动源管。</summary>
        void OnPourRequested(int src, int dst)
        {
            if (_winShowing) return; // 胜利弹层期输入锁(遮罩亦拦点击,双保险)
            if (_session == null || !_session.IsInLevel) return; // 面板切换竞态兜底
            if (_rack != null && _rack.IsAnimating) return;      // 倒水动画中不接受新倒水(架子点击已锁,双保险)
            bool merging = false;
            var b = _session.Board;
            if (b != null && b.TopCount(src) > 0 && b.TopCount(dst) > 0
                && b.TopColor(src) == b.TopColor(dst)) merging = true;
            // 动画量 = 本次实际转移格数(引擎 LegalMoves 给出,与 TryPour 落子同源)
            int count = 0;
            if (b != null)
                foreach (var m in b.LegalMoves())
                    if (m.Src == src && m.Dst == dst) { count = m.Count; break; }
            if (count <= 0) return; // 理论不可达(试管架已预判合法)
            if (_rack == null) // 无试管架(prefab 异常):退化为无动画直落
            {
                if (_session.TryPour(src, dst)) TutorialOnPourSucceeded(merging);
                return;
            }
            _rack.BeginPour(); // 先锁:TryPour 的 BoardChanged 刷新被挂起到动画收尾
            _rack.ClearSelection(); // 倒水即摘选中:防收尾重建后源管残留拎起/蓝染(动画期 RefreshTubeArea 挂起)
            if (_session.TryPour(src, dst))
            {
                TutorialOnPourSucceeded(merging);
                _rack.PlayPourAsync(src, dst, count).Forget();
            }
            else
            {
                _rack.CancelPour(); // 竞态兜底:解锁
                _rack.ShakeTube(src);
            }
        }

        /// <summary>
        /// 提示(双通道,WS-06/08/12):金币充足 → 金币直购(M1 行为,成功才扣币 —— 求解失败即玩家走入
        /// 无解死角,不向死局收币);金币不足 → 激励链路(确认面板 → 去广告直发/看完整视频 → 免费发放)。
        /// 发放层共用 Session.TryHint(落子+计数),扣币与否在调用点;每关上限读 WaterSortConfig,
        /// 金币与激励同额共限不另立字段(防刷)。
        /// </summary>
        void OnHint()
        {
            if (_winShowing) return; // 胜利弹层期输入锁
            if (_session == null || !_session.IsInLevel) return; // 面板按钮不可达,双保险
            if (_rack != null && _rack.IsAnimating) return;      // 倒水动画中锁道具
            if (TutorialHintActive) { DoTutorialHintDemo(); return; } // 引导第 3 步:走免费演示(不扣币/不弹广告)
            if (_session.HintsUsed >= WaterSortConfig.HintLimitPerLevel) return;
            var save = ServiceLocator.Save;
            if (save != null && save.Coins >= WaterSortConfig.HintPriceCoins)
            {
                if (!_session.TryHint())
                {
                    ShowToast("watersort.toast.hintFail"); // 无可解续路:诚实提示引导撤销,不向死局收币
                    return;
                }
                TrySpendCoins("hint", WaterSortConfig.HintPriceCoins);
                return;
            }
            // 激励通道:弹确认面板前先预检可解(死局看广告 = 白看;CanHint 只解不落子,无副作用)
            if (!_session.CanHint())
            {
                ShowToast("watersort.toast.hintFail");
                return;
            }
            ShowAdPanel("watersort.ad.hint", WaterSortConfig.HintLimitPerLevel, "hint",
                () => GrantFreeAction(_session.TryHint));
        }

        /// <summary>
        /// 额外空瓶(双通道,WS-06/13):+1 支空管(每关上限走配置);金币直购同 M1 成功才扣币;
        /// 金币不足走激励链路。加管恒成功(上限内),无需预检。
        /// </summary>
        void OnAddExtraTube()
        {
            if (_winShowing) return; // 胜利弹层期输入锁
            if (_session == null || !_session.IsInLevel) return;
            if (_rack != null && _rack.IsAnimating) return; // 倒水动画中锁道具
            if (_session.ExtraTubesUsed >= WaterSortConfig.ExtraTubeLimitPerLevel) return;
            var save = ServiceLocator.Save;
            if (save != null && save.Coins >= WaterSortConfig.ExtraTubePriceCoins)
            {
                if (!_session.TryAddExtraTube()) return; // 上限内恒成功;失败静默(防御,理论不可达)
                TrySpendCoins("extra_tube", WaterSortConfig.ExtraTubePriceCoins);
                return;
            }
            ShowAdPanel("watersort.ad.tube", WaterSortConfig.ExtraTubeLimitPerLevel, "extra_tube",
                () => GrantFreeAction(_session.TryAddExtraTube));
        }

        /// <summary>激励免费发放(M3.1):执行发放动作(金币购同款 Session 方法,仅不扣币);失败即 toast(观看期
        /// 盘面不可变,失败仅超时边缘防御)。动作内部会经 BoardChanged 链刷新按钮态。</summary>
        void GrantFreeAction(Func<bool> grant)
        {
            if (grant != null && !grant()) ShowToast("watersort.toast.hintFail");
            SyncConsumeButtons(); // 防御性兜底:发放成功路径已由 RefreshTubeArea 链刷新,失败路径补一次
        }

        // ---- 激励确认面板(M3.1 内嵌 AdPanel;UIRouter 覆盖会触发本视图 OnHide=退模块,故不走 Router 栈) ----

        /// <summary>弹出激励确认面板:注入点位消息/上限/发放回调。发放只发生在确认(OnAdConfirm)后。</summary>
        void ShowAdPanel(string messageKey, int arg, string point, Action grant)
        {
            if (_adPanel == null) // prefab 缺 AdPanel(编辑期/资源异常):跳过确认直接发放,不卡流程
            {
                grant?.Invoke();
                return;
            }
            _adGrant = grant;
            _adPoint = point;
            if (_adMessage != null) _adMessage.text = L10n.Format(messageKey, arg);
            _adPanel.gameObject.SetActive(true); // 遮罩拦点击:面板弹出期间下层按钮不可达
        }

        void HideAdPanel()
        {
            if (_adPanel != null) _adPanel.gameObject.SetActive(false);
        }

        void OnAdConfirm()
        {
            HideAdPanel();
            var grant = _adGrant;
            var point = _adPoint;
            _adGrant = null; // 先清再发:发放动作可能再弹面板(理论不可达),防重入残留
            _adPoint = null;
            if (grant == null) return;
            var ads = ServiceLocator.Ads;
            if (ads == null) return;
            if (ads.IsAdsRemoved)
            {
                // 去广告(WS-13):激励直接发放,伪视频秒完成(不展示真广告)
                grant();
                LogAdReward(point);
                return;
            }
            ads.ShowRewardedAd(watched =>
            {
                if (!watched) return; // 中途关闭/未就绪:不发放
                LogAdReward(point);
                grant();               // 完整观看 → 发放(发放层 = 各点位 Session 方法,见调用点注释)
            });
        }

        void OnAdCancel()
        {
            HideAdPanel();
            _adGrant = null; // 取消不发放,防残留回调误触发
            _adPoint = null;
        }

        /// <summary>激励完成埋点(04 文档 §5 ad_reward 字典:placement 点位)。</summary>
        void LogAdReward(string placement)
        {
            ServiceLocator.Analytics?.LogEvent("watersort_ad_reward", "placement", placement);
        }

        // ---- 皮肤接入(2026-09-06:按钮文字 → ws_btn_*_flat 皮肤图;图缺失保留文字兜底) ----

        /// <summary>按钮皮肤换装(OnCreate 尾触发):并行预载图标图,图到即换(隐藏原生 Label 文字)。
        /// 图标缺失(未入组/构建期漏检)→ 保留 Accent 底 + Label 文字兜底,不阻塞玩法。</summary>
        async UniTask BuildGameSkinAsync()
        {
            await UniTask.WhenAll(
                ApplyButtonSkinAsync("GamePanel/TopBar/BackButton", SkinBack),
                ApplyButtonSkinAsync("DailyPanel/BackButton", SkinBack), // 每日主页返回钮共用同款图标
                ApplyButtonSkinAsync("GamePanel/BottomBar/UndoButton", SkinUndo),
                ApplyButtonSkinAsync("GamePanel/BottomBar/HintButton", SkinHint),
                ApplyButtonSkinAsync("GamePanel/BottomBar/RestartButton", SkinRestart),
                ApplyButtonSkinAsync("GamePanel/BottomBar/ExtraTubeButton", SkinExtra));
            await LoadSkinAsync(SkinWinTitle); // 胜利标题预热:首关速通时弹层不白帧(见 PlayWinThenAdvanceAsync)

        }

        /// <summary>单钮换图:图到 → 覆写按钮 Image(清生成器底色,图自带色)并隐藏 Label 文字。
        /// 等比完整呈现图标(源图带透明出血,preserveAspect 防拉伸变形);禁用态灰化仍由 Button
        /// ColorTint 作用于 Image,白色图标 × 灰乘数 = 语义清晰的禁用态。</summary>
        async UniTask ApplyButtonSkinAsync(string buttonPath, string address)
        {
            var sprite = await LoadSkinAsync(address);
            if (sprite == null || this == null) return; // 加载失败保持原样(文字兜底);视图已销毁不落节点
            var img = FindInCard(buttonPath)?.GetComponent<Image>();
            if (img == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = Color.white;
            // 图片即文案:原生 Label 下线(节点保留 —— 图缺失路径的文字兜底由「未隐藏」天然承接)
            var label = FindInCard(buttonPath + "/Label");
            if (label != null) label.gameObject.SetActive(false);
        }

        /// <summary>皮肤图加载(按地址缓存一次;失败缓存 null —— 缺失不再重复请求刷警告)。
        /// 回调式 IAssetService 包一层 UniTask(回调在主线程,无跨线程风险);服务未就绪不悬挂直返 null。</summary>
        async UniTask<Sprite> LoadSkinAsync(string address)
        {
            if (_skinSprites.TryGetValue(address, out var cached)) return cached;
            var svc = ServiceLocator.Assets;
            if (svc == null)
            {
                _skinSprites[address] = null;
                return null;
            }
            var tcs = new UniTaskCompletionSource<Sprite>();
            svc.LoadAsset<Sprite>(address, sp =>
            {
                if (sp == null)
                    Debug.LogWarning($"[WaterSortSkin] 皮肤图缺失: {address}(保持原生文字兜底)");
                _skinSprites[address] = sp; // 就绪/失败均入缓存
                tcs.TrySetResult(sp);
            });
            return await tcs.Task;
        }

        // ---- 胜利弹层与自动跳关(2026-09-06 改造:结算面板删除后唯一过关呈现) ----

        /// <summary>过关瞬间(LevelSolved 由 TryPour 同步发出,见 WaterSortSession):进度落盘先行 ——
        /// 清半局槽(常规)/首通发奖落盘/每日完成标记,随后播放胜利弹层 3 秒自动进下一关。
        /// 「先落盘后展示」的顺序保证:弹层期杀进程/退模块也不丢进度。
        /// 被倒水动画压住时挂起,等 PourCompleted 收尾再弹(见 OnPourCompleted:_solvedPending)。</summary>
        void OnLevelSolved()
        {
            if (_session == null) return;
            if (!_session.IsDaily) WaterSortProgressStore.ClearRun(); // 通关即清半局快照(常规局;每日不走该槽)
            // 通关由最后一手倒水触发时(LevelSolved 在 TryPour 落子瞬间同步发出),若动画仍在播,
            // 压住弹层等 PourCompleted 收尾再弹 —— 否则「最后一个动作刚做就通关」穿帮
            if (_rack != null && _rack.IsAnimating)
            {
                _solvedPending = true;
                return;
            }
            // 过关计数(M3.2 全局频控,WS-12):常规/每日首解瞬间统一上报(自动跳关只计数不插屏,
            // 插屏展示候选收敛在退模块路径 LeaveToHub,计数与展示解耦,数独侧同构)。
            // 引导期解关 = 已会玩:整段引导提前收尾(Done),本局不上报过关计数(WS-14 引导期间零广告)。
            if (_tut is { IsActive: true }) _tut.Finish();
            else ServiceLocator.Ads?.NotifyLevelCompleted();
            if (_session.IsDaily)
            {
                // 每日挑战(WS-09):不发首通奖、不推进常规解锁,只落「今日完成」(Streak 由
                // WaterSortDailyStore 从 doneSeeds 推导)——归属按开局种子 _dailySeed(开局取 UTC 日),
                // 跨零点对局不错记。
                if (_dailySeed > 0) WaterSortDailyStore.MarkDone(_dailySeed);
            }
            else
            {
                // 首通发奖(WS-08:奖励曲线在 WaterSortConfig,仅首通入账 box.coins;RecordFirstWin 返回值 =
                // 本次是否首通,落盘与发奖同源 —— 解锁推进仍只认首通,WS-04)。
                if (WaterSortProgressStore.RecordFirstWin(_session.LevelId))
                {
                    int reward = WaterSortConfig.FirstWinReward(_session.LevelId);
                    if (reward > 0) GrantCoins(reward);
                }
            }
            UpdateCoinLabel(); // 发奖即刷顶栏余额(弹层停留期玩家可见)
            PlayWinThenAdvanceAsync().Forget();
        }

        /// <summary>胜利呈现:遮罩淡入 + 标题回弹放大(「弹出来」),停留 WinHoldSeconds 后自动跳关/回主页。
        /// 输入锁 _winShowing 全程生效(各操作钮守卫 + 遮罩 raycast 拦截,双保险);视图销毁/退模块
        /// (缓存复用)时早退 —— 进度已随过关落盘,不跳关无损失,复位交 OnShow(见其弹层复位)。</summary>
        async UniTask PlayWinThenAdvanceAsync()
        {
            if (_winShowing) return; // 防重入(LevelSolved 每局只发一次,双保险)
            _winShowing = true;
            // 标题图就绪(预热失败/缺失 → 遮罩 + 自动跳仍走,仅标题位留空)
            var sprite = await LoadSkinAsync(SkinWinTitle);
            if (this == null || _leaving) return;
            var overlay = BuildWinOverlay();
            var titleImg = _winTitle != null ? _winTitle.GetComponent<Image>() : null;
            if (titleImg != null)
            {
                titleImg.sprite = sprite;
                titleImg.gameObject.SetActive(sprite != null); // 图缺失不显空 Image(纯白块穿帮)
                _winTitle.localScale = Vector3.one * 0.3f;     // 回弹起点
            }
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling(); // 恒盖所有面板(含 AdPanel;弹层期任何下层点击不可达)
            // 弹入:遮罩淡入与标题回弹并行(EaseOutBack 中段过冲 = 「弹」感)
            BoxTween.FadeTo(overlay, 0f, 1f, 0.25f).Forget();
            if (titleImg != null && sprite != null)
                BoxTween.ScalePulse(_winTitle, 0.3f, 1f, WinPopSeconds).Forget();
            // 停留展示(DeltaTime 计时:对局暂停/退后台冻结,回前台续走)
            await UniTask.Delay(Mathf.RoundToInt(WinHoldSeconds * 1000f), DelayType.DeltaTime);
            if (this == null || _leaving) return; // 退模块/销毁:复位交 OnShow(见其弹层复位注释)
            _winShowing = false;
            overlay.SetActive(false);
            if (titleImg != null) _winTitle.localScale = Vector3.one;
            AdvanceAfterWin();
        }

        /// <summary>胜利弹层节点懒建一次(此后复用;根下最后兄弟 = 恒盖各面板)。全屏遮罩压暗聚焦 +
        /// 标题位。纯运行期节点不进 prefab(与热更组件不进序列化的纪律同源)。</summary>
        GameObject BuildWinOverlay()
        {
            if (_winOverlay != null) return _winOverlay;
            var go = new GameObject("WinOverlay", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; // 全屏拉伸(与生成器面板同构)
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var mask = go.GetComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.35f); // 压暗背景聚焦标题;raycastTarget 默认 true 拦点击
            // 标题(ws_win_title 671×326,等比按 760 宽显示;居中略偏上 —— 遮罩下半露出试管区解局余韵)
            var title = new GameObject("WinTitle", typeof(RectTransform), typeof(Image));
            title.transform.SetParent(go.transform, false);
            var trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, 320f);
            trt.sizeDelta = new Vector2(760f, 369f); // 等比:760 / 671 × 326 ≈ 369
            var tImg = title.GetComponent<Image>();
            tImg.preserveAspect = true;
            tImg.raycastTarget = false; // 标题不拦点击(遮罩已拦)
            _winTitle = title.transform;
            _winOverlay = go;
            go.SetActive(false);
            return go;
        }

        /// <summary>弹层停留结束后的去向:每日 → 每日主页(完成态已由过关落盘,主页文案即刷);
        /// 常规 → 下一关(尾关循环回第 1 关;通关即清快照,StartGame 内续盘路径恒走全新开局)。</summary>
        void AdvanceAfterWin()
        {
            if (_session == null) return;
            if (_session.IsDaily)
            {
                _dailySeed = 0; // 本局归属已落盘,复位防旧种子串到下一局
                ShowPanel(Panel.Daily);
                ApplyDailyTexts();
                return;
            }
            int total = _pack != null && _pack.levels != null ? _pack.levels.Count : 0;
            int nextId = total > 0 && _session.LevelId >= total ? 1 : _session.LevelId + 1;
            var level = total > 0 ? WaterSortLevelStore.FindById(_pack, nextId) : null;
            if (level == null) // 题库缺失/越界(构建期错误):留对局面板可退(返回键/重开钮),不卡流程
            {
                ShowToast("watersort.toast.noLevels");
                return;
            }
            StartGame(level); // 开局成功切面板并刷新试管;LevelSolved 事件源已随 StartLevel 复位,可正常再收下一关
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

        void OnBoardChanged()
        {
            RefreshTubeArea();
            SaveRunIfNeeded(); // 半局快照随盘面即落(倒水/撤销/加管后杀进程均可续,二期)
        }

        /// <summary>半局快照落盘(仅常规对局):已解盘不存(通关路径即清);撤销回起点 = 等价全新盘,
        /// 清槽防复活旧盘;一步未走且无加管也清(空快照无意义)。每日挑战无半局语义不落本槽。</summary>
        void SaveRunIfNeeded()
        {
            if (_session == null || !_session.IsInLevel || _session.IsDaily) return;
            if (_session.Board == null || _session.Board.IsSolved()) return;
            if (_session.MoveCount <= 0 && _session.ExtraTubesUsed <= 0)
            {
                WaterSortProgressStore.ClearRun();
                return;
            }
            var run = _session.BuildRunSnapshot();
            if (run != null) WaterSortProgressStore.SaveRun(run);
        }

        void ApplyLanguage()
        {
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
            // 固定功能按钮:皮肤图承载图标后 Label 隐藏(此路径为图缺失时的文字兜底,见 ApplyButtonSkinAsync)
            SetLabel("GamePanel/TopBar/BackButton", L10n.Get("game.back"));
            SetLabel("GamePanel/BottomBar/UndoButton", L10n.Get("game.undo"));
            SetLabel("GamePanel/BottomBar/RestartButton", L10n.Get("watersort.btn.restart"));
            SetLabel("GamePanel/BottomBar/HintButton", L10n.Get("watersort.btn.hint"));
            SetLabel("GamePanel/BottomBar/ExtraTubeButton", L10n.Get("watersort.btn.tube"));
            // 激励确认面板按钮(M3.1):复用既有「看广告/取消」键(跨玩法通用文案,字库免新增)
            SetLabel("AdPanel/Card/ConfirmButton", L10n.Get("hint.ad.confirm"));
            SetLabel("AdPanel/Card/CancelButton", L10n.Get("hint.ad.cancel"));
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
            ServiceLocator.Analytics?.LogEvent("watersort_coin_spend", reason, price); // 埋点 source=玩法+动作
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
            ServiceLocator.Analytics?.LogEvent("watersort_coin_reward", "amount", amount);
            UpdateCoinLabel();
        }

        /// <summary>
        /// 提示/加管按钮可用态:局内 + 未达每关上限(M3.1 双通道起不再查余额 —— 余额不足也可点,
        /// 点击走激励链路免费发放;金币够则金币直购,分发在 OnHint/OnAddExtraTube 内)。
        /// 上限只在 StartLevel/Undo(加管)变化,因此 RefreshTubeArea(盘面变更统一接缝)与本类
        /// 扣币/发放点调用即覆盖全部变化路径。
        /// </summary>
        void SyncConsumeButtons()
        {
            bool inLevel = _session != null && _session.IsInLevel;
            if (_hintButton != null)
                _hintButton.SetInteractable(inLevel
                    && _session.HintsUsed < WaterSortConfig.HintLimitPerLevel);
            if (_extraTubeButton != null)
                _extraTubeButton.SetInteractable(inLevel
                    && _session.ExtraTubesUsed < WaterSortConfig.ExtraTubeLimitPerLevel);
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

        // ---- 新手引导(M3.3,WS-14;通用件在 Box.HotUpdate.Core.Onboarding,10 文档 §16.7 9.5 可复用) ----

        /// <summary>引导状态分区键 gameId(OnboardingStore → box.onboarding.watersort,盒级共享分区)。</summary>
        const string TutorialGameId = "watersort";
        /// <summary>S2(同色聚合)无聚合对可演示时,连续 N 次普通成功倒水即放行进下一步(防盘面无可示对时卡教学)。</summary>
        const int TutorialS2GracePours = 3;

        /// <summary>开局落点(StartGame 尾部):常规(非每日)第 1 关 + 引导未收尾 → 开播 ≤N 步;收尾(Done/Skipped)
        /// 或配置 0 步 → 不再播。中途离开对局面板/退模块时 CancelTutorial 保留 InProgress,重进第 1 关从头续播。
        /// 引导期零广告(WS-14):过关计数豁免见 OnLevelSolved,提示演示免费见 DoTutorialHintDemo。</summary>
        void MaybeStartTutorial()
        {
            if (_session == null || _session.IsDaily || _session.LevelId != 1
                || OnboardingStore.IsFinished(TutorialGameId)) return;
            var steps = new List<TutorialStepDef>
            {
                // S1 点击倒水:高亮整个试管区,任意一次成功倒水即过(TutorialOnPourSucceeded)
                new TutorialStepDef("watersort.tutorial.pour", TubeAreaScreenRect),
                // S2 同色聚合:动态目标 = 盘面当前可演示的「同色聚合」对包围盒(RescanTutorialPair 每刷重扫)
                new TutorialStepDef("watersort.tutorial.merge", TutorialMergeScreenRect),
                // S3 卡关求助:高亮提示按钮,点击走免费演示(DoTutorialHintDemo)
                new TutorialStepDef("watersort.tutorial.hint", HintButtonScreenRect),
            };
            int n = Mathf.Clamp(WaterSortConfig.OnboardingStepCount, 0, steps.Count);
            if (n <= 0) // 运营配置 0 步 = 不开引导:直接置 Done,避免每局白查
            {
                OnboardingStore.Set(TutorialGameId, OnboardingStatus.Done);
                return;
            }
            _tutNonMergePours = 0;
            _tut = TutorialFlow.Start(TutorialGameId, steps.GetRange(0, n), "watersort.tutorial.skip",
                OnTutorialStepShown, OnTutorialEnded);
            if (_tut != null) RescanTutorialPair(); // S1 步进后即切 S2,第一帧目标就要准
        }

        /// <summary>离开引导局(返回选关/每日主页/退模块):摘掩码,状态保留 InProgress —— 不弹「是否跳过」,
        /// 也不静默吞掉引导(重进第 1 关从头再播;完成/跳过不走此路径)。</summary>
        void CancelTutorial()
        {
            _tut?.Cancel();
            _tut = null;
        }

        /// <summary>引导局成功倒水驱动(S1/S2 步进,OnPourRequested 成功分支调用):
        /// S1 任意成功倒水 → 切 S2;S2 同色聚合倒水 = 规则实体演示 → 切 S3;
        /// 无聚合可演示时连续普通倒水达 TutorialS2GracePours 次也放行(防教学卡死)。</summary>
        void TutorialOnPourSucceeded(bool merging)
        {
            if (_tut == null || !_tut.IsActive) return;
            if (_tut.StepIndex == 0)
            {
                RescanTutorialPair(); // 新盘面先扫聚合对,S2 首帧高亮即正确
                _tut.Advance();
                return;
            }
            if (_tut.StepIndex == 1)
            {
                if (merging) _tut.Advance(); // 真聚合倒水 = 教学达成
                else if (++_tutNonMergePours >= TutorialS2GracePours) _tut.Advance();
            }
        }

        /// <summary>盘面刷新接缝(RefreshTubeArea 尾部):S2 在场时重扫「同色聚合」对并重定位孔洞
        /// (演示对随盘面漂移,静态矩形会指错;S1/S3 目标本身静态,无需刷新)。</summary>
        void TutorialAfterBoardRefresh()
        {
            if (_tut == null || !_tut.IsActive || _tut.StepIndex != 1) return;
            RescanTutorialPair();
            _tut.RefreshTarget();
        }

        /// <summary>扫当前盘任一同色聚合合法移动(判据与 Session 同源:源顶层色 = 非空目标顶层色)。
        /// 取第一对即可(演示语义,不追求最优);无对 → 双索引置 -1(S2 目标退回整个试管区)。</summary>
        void RescanTutorialPair()
        {
            _tutPairA = _tutPairB = -1;
            var b = _session?.Board;
            if (b == null || b.IsSolved()) return;
            foreach (var m in b.LegalMoves())
            {
                if (b.TopCount(m.Dst) > 0 && b.TopColor(m.Src) == b.TopColor(m.Dst))
                {
                    _tutPairA = m.Src;
                    _tutPairB = m.Dst;
                    return;
                }
            }
        }

        /// <summary>S2 高亮目标:聚合对两试管包围盒(屏幕像素矩形);对不存在/试管未重建 → 退回整个试管区
        /// (泛引导不指错:玩家任意倒水也能继续)。</summary>
        Rect TutorialMergeScreenRect()
        {
            var a = _rack != null && _tutPairA >= 0 ? _rack.Tube(_tutPairA) : null;
            var b = _rack != null && _tutPairB >= 0 ? _rack.Tube(_tutPairB) : null;
            if (a == null || b == null) return TubeAreaScreenRect();
            var ra = ScreenRectOf(a);
            var rb = ScreenRectOf(b);
            return ra.width > 0f && rb.width > 0f ? UnionRect(ra, rb) : TubeAreaScreenRect();
        }

        Rect TubeAreaScreenRect() => ScreenRectOf(FindInCard("GamePanel/TubeArea") as RectTransform);
        Rect HintButtonScreenRect() => ScreenRectOf(FindInCard("GamePanel/BottomBar/HintButton") as RectTransform);

        /// <summary>S3 在场判定:引导激活且处于第 3 步(HintButton 高亮中)→ 提示点击走免费演示。</summary>
        bool TutorialHintActive => _tut is { IsActive: true } && _tut.StepIndex == 2;

        /// <summary>
        /// 「卡关求助」演示(M3.3):真执行一次提示(落子后 BoardChanged 链自行刷新试管区,教学立即可见),
        /// 但免费 —— 不扣金币、不弹激励确认(引导期零广告,WS-14);占用一次本关提示配额(与正常提示同额,
        /// 引导一生只播一次,代价 = 首关提示余 2/3 次,见 WaterSortConfig.HintLimitPerLevel)。
        /// 死角无法演示(CanHint 失败)时不误导,直接收尾引导。
        /// </summary>
        void DoTutorialHintDemo()
        {
            if (_tut == null) return;
            if (_session == null || !_session.IsInLevel || !_session.CanHint())
            {
                _tut.Finish();
                return;
            }
            _session.TryHint(); // 演示期必有解(上一步已 CanHint 预检);失败静默(超时边缘防御,不可达)
            _tut.Finish();
        }

        /// <summary>步骤展示埋点(04 文档 §5 tutorial_step;step_index 从 1 起,运营侧直读)。
        /// 跳过事件在 OnTutorialEnded 补发(接口仅单键值对,拆分上报)。</summary>
        void OnTutorialStepShown(int stepIndex)
            => ServiceLocator.Analytics?.LogEvent("watersort_tutorial_step", "step_index", stepIndex + 1);

        /// <summary>引导收尾回调(完成/跳过都经此):跳过补 skipped 埋点;统一置空引用
        /// (Done/Skipped 落盘由 TutorialFlow 内部完成,视图只管退出引导态)。</summary>
        void OnTutorialEnded(bool finished)
        {
            if (!finished)
                ServiceLocator.Analytics?.LogEvent("watersort_tutorial_step", "skipped", 1);
            _tut = null;
        }

        /// <summary>控件 → 屏幕像素矩形(ScreenSpaceOverlay 下世界坐标即屏幕像素;TutorialMask.LocalizeRect 同系换算)。
        /// 节点缺失/未激活(画布外)返回空矩形 → 掩码不挖洞只显示气泡(空安全降级,见 TutorialMask.ShowStep)。</summary>
        static Rect ScreenRectOf(RectTransform rt)
        {
            if (rt == null) return default;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                if (corners[i].x < minX) minX = corners[i].x;
                if (corners[i].y < minY) minY = corners[i].y;
                if (corners[i].x > maxX) maxX = corners[i].x;
                if (corners[i].y > maxY) maxY = corners[i].y;
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>两矩形包围盒(合并;S2 演示对 → 覆盖两试管的单个高亮窗)。</summary>
        static Rect UnionRect(Rect a, Rect b)
            => new Rect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax) - Mathf.Min(a.xMin, b.xMin),
                Mathf.Max(a.yMax, b.yMax) - Mathf.Min(a.yMin, b.yMin));

        void LogLevelAbandon() { } // M1 埋点占位:弃局(watersort.level_abandon)字典随 M1.4 落地
    }
}
