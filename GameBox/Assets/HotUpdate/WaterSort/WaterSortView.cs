using System;
using System.Collections.Generic;
using Box.HotUpdate.Core.Onboarding;
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
    ///   GamePanel/BottomBar/HintButton | ExtraTubeButton 道具双通道钮(M1.4 金币直购;M3.1 加激励分支复用同钮)
    ///   SettlePanel/Title | ResultText | RewardText | DoubleButton | NextButton | RetryButton | HubButton
    ///                     (RewardText+DoubleButton 仅首通结算显示:奖励行左、翻倍钮右,同一行)
    ///   AdPanel/Card/MessageText | ConfirmButton | CancelButton  激励确认面板(M3.1 内嵌,见退出纪律)
    ///   DailyPanel/Title | StateText | StreakText | PlayButton | BackButton  每日主页(M2.3;完成态/Streak/重玩)
    ///
    /// 每日挑战(M2.3)模式语义:视图内模式 = 会话标记(WaterSortSession.IsDaily);选关页「每日挑战」
    /// 入口经 Session 实例整体换新(旧实例退订销毁,见 SwitchSession),不弹 Router、不退出模块;
    /// 模块级入口 args="daily"(WaterSortModule.OnEnter)落同一主页。每日主页 Back 回常规选关 =
    /// 再换回常规会话。对局/结算共用面板,差异全部收敛在:标题文案、完成落盘(每日 → dailyDoneSeeds,
    /// 常规 → firstWinLevels+发币)、Next 放行(每日恒禁)。
    ///
    /// 道具双通道(M3.1,WS-08/12):提示/空瓶按钮点击 → 金币充足 = 金币直购(M1 行为,成功才扣币);
    /// 金币不足 = 激励确认面板(消息说明用途/上限)→ 确认后去广告用户伪视频直发(IsAdsRemoved,
    /// WS-13)或看完整激励视频 → 免费发放。发放层共用 Session 方法(计数/盘面效果),扣币与否在调用点;
    /// 结算翻倍(第三点位)同样经确认面板,奖励再发一份(GrantCoins 同层入账)。
    ///
    /// 退出纪律(与 WaterSortModule.OnExit 配合):本视图是模块压入 Router 的唯一自属视图,对局/选关/
    /// 结算都是面板切换不压 Router 弹窗(M3.1 激励确认 = 玩法内嵌 AdPanel 子树,刻意不用 Router:
    /// PushAsync 覆盖下层会 HideAsync 触发本视图 OnHide = 退模块,故激励面板零路由生命周期,
    /// 遮罩拦点击、关闭只翻自身)。OnHide 只会在「真的被 Pop」时触发(主动 HubButton 或返回键)
    /// ——即用户离开模块的唯一信号 → ExitAsync 复位模块状态(防卡 Active)。
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
        BoxButton _hintButton, _extraTubeButton; // 双通道道具钮(金币直购 | 激励兜底,见 OnHint/OnAddExtraTube)
        BoxButton _doubleButton;                 // 结算翻倍钮(激励点位,见 OnDoubleReward)
        TextMeshProUGUI _rewardText;             // 结算首通奖励行(仅首通过关时 SetActive + 文案)
        TextMeshProUGUI _coinLabel;              // 对局顶栏金币余额(随消费/发奖就近刷新)
        int _rewardAmount;                       // 本次首通奖励额(M3.1 翻倍源;翻倍后按 2× 累计,非首通结算恒 0)
        Transform _adPanel;                      // 内嵌激励确认面板(WS-12;不进 Router,见类头退出纪律)
        TextMeshProUGUI _adMessage;              // 面板消息(点位文案 + 次数上限参数)
        Action _adGrant;                         // 确认后的发放动作(点位闭包注入;取消/面板关闭即置空)
        string _adPoint;                         // 当前点位名(激励完成埋点 placement,04 文档 §5)
        TextMeshProUGUI _dailyStateText, _dailyStreakText; // 每日主页:今日状态 + 连续天数
        BoxButton _dailyPlayButton;              // 每日主页:开始/再玩今日挑战(题库就绪才可点)
        WaterSortDailyPack _dailyPack;           // 本次会话每日题库缓存(主页/开局同源取关)
        int _dailySeed;                          // 本局每日挑战归属日期种子(开局取;结算按此落完成,防跨零点错记)
        WaterSortTubeRack _rack;     // 试管区渲染与点击(OnCreate 挂到 TubeArea;随视图销毁)
        int _totalLevels;            // 题库总量(选关渲染后赋值;结算 Next 放行判定见 NextLevelPlayable)
        TutorialFlow _tut;           // 新手引导流程(M3.3,WS-14;null=未开播/已收尾,见「新手引导」区)
        int _tutNonMergePours;       // S2 放行计数:无聚合演示对时连续普通倒水次数(见 TutorialS2GracePours)
        int _tutPairA = -1, _tutPairB = -1; // 当前盘「同色聚合」演示对(试管索引;-1=无,随盘面刷新重扫)

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

            // Bug 清单 6(2026-09-05,真机):选关内容不可见但盲点可触发。根因 = Viewport 挂经典 Mask,
            // 遮罩图为全透明(Image a=0)+ CanvasRenderer CullTransparentMesh 在设备上剔除透明 mesh →
            // stencil 空写 → 子级渲染裁剪全败;GraphicRaycaster 不受 stencil 限制 → 可点击(现象吻合)。
            // 运行期换 RectMask2D(纯矩形几何裁剪,不依赖遮罩图/stencil);生成器 WaterSortViewSetup
            // 已同步改型,此处兜底存量 bundle(免重打 prefab 资源即生效)。
            var viewport = FindInCard("SelectPanel/LevelScroll/Viewport");
            if (viewport != null)
            {
                var legacyMask = viewport.GetComponent<UnityEngine.UI.Mask>();
                if (legacyMask != null)
                {
                    legacyMask.enabled = false; // 先摘出 stencil 裁剪链(帧内即生效)
                    Destroy(legacyMask);        // 帧末移除组件(免残留状态)
                    viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
                }
            }
            _resultText = FindInCard("SettlePanel/ResultText")?.GetComponent<TextMeshProUGUI>();
            _stepText = FindInCard("GamePanel/StepText")?.GetComponent<TextMeshProUGUI>();
            _nextButton = FindInCard("SettlePanel/NextButton")?.GetComponent<BoxButton>();
            _undoButton = FindInCard("GamePanel/BottomBar/UndoButton")?.GetComponent<BoxButton>();
            _restartButton = FindInCard("GamePanel/BottomBar/RestartButton")?.GetComponent<BoxButton>();
            _hintButton = FindInCard("GamePanel/BottomBar/HintButton")?.GetComponent<BoxButton>();
            _extraTubeButton = FindInCard("GamePanel/BottomBar/ExtraTubeButton")?.GetComponent<BoxButton>();
            _doubleButton = FindInCard("SettlePanel/DoubleButton")?.GetComponent<BoxButton>();
            _rewardText = FindInCard("SettlePanel/RewardText")?.GetComponent<TextMeshProUGUI>();
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
            if (_rack != null) _rack.PourRequested += OnPourRequested;

            Bind("SelectPanel/HubButton", LeaveToHub);
            Bind("SelectPanel/DailyButton", OnOpenDailyHome); // 每日入口(仅常规选关态可达)
            Bind("DailyPanel/BackButton", OnDailyBackToSelect);
            Bind("GamePanel/TopBar/BackButton", OnBackToSelect);
            // 结算 Hub 与选关 Hub 分开绑定(M3.2):过关后回大厅是唯一插屏候选出口(见 LeaveToHubAfterSettle)
            Bind("SettlePanel/HubButton", LeaveToHubAfterSettle);
            Bind("SettlePanel/RetryButton", OnRetry);
            if (_nextButton != null) _nextButton.OnClick(OnNextLevel);
            if (_undoButton != null) _undoButton.OnClick(OnUndo);
            if (_restartButton != null) _restartButton.OnClick(OnRestart);
            if (_hintButton != null) _hintButton.OnClick(OnHint);
            if (_extraTubeButton != null) _extraTubeButton.OnClick(OnAddExtraTube);
            if (_doubleButton != null) _doubleButton.OnClick(OnDoubleReward);
            if (_dailyPlayButton != null) _dailyPlayButton.OnClick(OnDailyPlay);
            Bind("AdPanel/Card/ConfirmButton", OnAdConfirm); // 激励确认:关闭面板 → 伪视频直发/真激励
            Bind("AdPanel/Card/CancelButton", OnAdCancel);   // 取消:只关面板(不发放)
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            // 缓存视图复推(UIRouter 命中缓存直接 OnShow,见 Router.PushAsync):复位退模块标记。
            // 不复位则异步渲染续体(RenderLevelSelect/RenderDailyHomeAsync 的 _leaving 早退守卫)
            // 误判"正在退出"而返回 → 二次进入选关空列表/按钮禁点(Bug 清单 7 伴随根因)。
            _leaving = false;
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
            if (_selectPanel != null) _selectPanel.gameObject.SetActive(p == Panel.Select);
            if (_dailyPanel != null) _dailyPanel.gameObject.SetActive(p == Panel.Daily);
            if (_gamePanel != null) _gamePanel.gameObject.SetActive(p == Panel.Game);
            if (_settlePanel != null) _settlePanel.gameObject.SetActive(p == Panel.Settle);
            if (p != Panel.Game) CancelTutorial(); // 引导只在对局面板存在(离开 = 中断,状态保留下次续播)
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
            MaybeStartTutorial(); // 常规第 1 关首次/中断重进:开播新手引导(WS-14)
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
            TutorialAfterBoardRefresh(); // 引导 S2 聚合演示对随盘面漂移:刷新后重扫重定位(M3.3)
        }

        /// <summary>试管点击裁决:合法→会话推进(BoardChanged 驱动本区刷新);非法→源管抖动,选中保留可再试目标。
        /// 引导局(M3.3)额外上报「是否同色聚合倒水」供第 2 步步进(判据与 Session 规则同源:非空 dst 顶层同色)。</summary>
        void OnPourRequested(int src, int dst)
        {
            if (_session == null || !_session.IsInLevel) return; // 面板切换竞态兜底
            bool merging = false;
            var b = _session.Board;
            if (b != null && b.TopCount(src) > 0 && b.TopCount(dst) > 0
                && b.TopColor(src) == b.TopColor(dst)) merging = true;
            if (_session.TryPour(src, dst)) TutorialOnPourSucceeded(merging);
            else _rack?.ShakeTube(src);
        }

        /// <summary>
        /// 提示(双通道,WS-06/08/12):金币充足 → 金币直购(M1 行为,成功才扣币 —— 求解失败即玩家走入
        /// 无解死角,不向死局收币);金币不足 → 激励链路(确认面板 → 去广告直发/看完整视频 → 免费发放)。
        /// 发放层共用 Session.TryHint(落子+计数),扣币与否在调用点;每关上限读 WaterSortConfig,
        /// 金币与激励同额共限不另立字段(防刷)。
        /// </summary>
        void OnHint()
        {
            if (_session == null || !_session.IsInLevel) return; // 结算/选关面板按钮不可达,双保险
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
            if (_session == null || !_session.IsInLevel) return;
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

        // ---- 结算面板 ---- //

        void OnLevelSolved()
        {
            if (_session == null) return;
            // 过关计数(M3.2 全局频控,WS-12):常规/每日首解瞬间统一上报;展示只发生在结算 Hub 出口
            // (LeaveToHubAfterSettle)——连关/重开只计数不插屏,计数与展示解耦(数独侧同构)。
            // 引导期解关 = 已会玩:整段引导提前收尾(Done),本局不上报过关计数(WS-14 引导期间零广告)。
            if (_tut is { IsActive: true }) _tut.Finish();
            else ServiceLocator.Ads?.NotifyLevelCompleted();
            if (_session.IsDaily)
            {
                // 每日挑战(WS-09):不发首通奖、不推进常规解锁,只落「今日完成」(Streak 由 WaterSortDailyStore
                // 从 doneSeeds 推导)——归属按开局种子 _dailySeed(开局时取 UTC 日),跨零点对局不错记。
                if (_dailySeed > 0) WaterSortDailyStore.MarkDone(_dailySeed);
                ShowPanel(Panel.Settle);
                if (_resultText != null)
                    _resultText.text = L10n.Format("watersort.settle.result",
                        _session.MoveCount, DifficultyText(_session.Difficulty));
                HideRewardRow(); // 每日挑战无首通奖励行(翻倍钮随之隐藏,WS-12 翻倍只对首通奖)
                UpdateCoinLabel();
                if (_nextButton != null) _nextButton.SetInteractable(false); // 每日仅一关:不提供「下一关」
                return;
            }
            // 首通发奖(WS-08:奖励曲线在 WaterSortConfig,仅首通入账 box.coins,重玩/已解锁关卡不发;
            // RecordFirstWin 返回值 = 本次是否首通,复用作发奖/翻倍信号)。
            // 解锁推进仍只认首通(WS-04,RecordFirstWin 落盘):Next 放行 = 下一关当前可达(首通/重玩一致,
            // 见 NextLevelPlayable)——导航不推进解锁,不破坏解锁口径。
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
            _rewardAmount = reward; // M3.1 翻倍源(翻倍后按 2× 累计重设;重玩恒 0 → 整行隐藏)
            if (_rewardText != null)
            {
                // 首通显示奖励行;重玩整行隐藏(发奖理由清晰,防误导性重复奖励展示)
                _rewardText.text = reward > 0 ? L10n.Format("watersort.settle.reward", reward) : "";
                _rewardText.gameObject.SetActive(reward > 0);
            }
            if (_doubleButton != null) _doubleButton.gameObject.SetActive(reward > 0); // 翻倍钮与奖励行同显
            SetDoubleButtonState();
            UpdateCoinLabel(); // 发奖即刷新(顶栏在结算面板不可见,回对局时已是最新)
            if (_nextButton != null) _nextButton.SetInteractable(NextLevelPlayable());
        }

        /// <summary>首通奖励行整行隐藏(每日结算/重玩结算;翻倍钮随行隐藏)。</summary>
        void HideRewardRow()
        {
            _rewardAmount = 0;
            if (_rewardText != null) _rewardText.gameObject.SetActive(false);
            if (_doubleButton != null) _doubleButton.gameObject.SetActive(false);
        }

        /// <summary>
        /// 结算翻倍(WS-12 第三点位):看完激励视频后按本关首通奖励额再加发一份(合计 2×,GrantCoins 同层
        /// 入账;去广告用户伪视频直发)。每关上限读配置(StartLevel 复位);翻倍后按钮禁用+文案「已翻倍」,
        /// 奖励行文本按 2× 累计重设(玩家所见即所得)。
        /// </summary>
        void OnDoubleReward()
        {
            if (_session == null || _rewardAmount <= 0 || !_session.CanDoubleReward) return;
            ShowAdPanel("watersort.ad.double", WaterSortConfig.RewardDoubleLimitPerLevel, "double", () =>
            {
                if (!_session.TryMarkRewardDoubled()) return; // 达上限(理论不可达,按钮已禁):不重复发
                GrantCoins(_rewardAmount); // 再加发一份 → 合计 2× 首通额
                _rewardAmount *= 2;
                if (_rewardText != null)
                    _rewardText.text = L10n.Format("watersort.settle.reward", _rewardAmount);
                SetDoubleButtonState();
            });
        }

        /// <summary>翻倍钮可用态/文案:首通奖励可翻倍 → 「翻倍奖励」可点;已翻倍 → 「已翻倍」禁用。</summary>
        void SetDoubleButtonState()
        {
            if (_doubleButton == null) return;
            bool available = _session != null && _rewardAmount > 0 && _session.CanDoubleReward;
            _doubleButton.SetInteractable(available);
            SetLabel("SettlePanel/DoubleButton",
                available ? L10n.Get("watersort.btn.double") : L10n.Get("watersort.btn.doubled"));
        }

        /// <summary>
        /// 结算 Next 放行判定(2026-09-05 Bug 清单 7):存在下一关且「下一关当前可玩」即放行。
        /// 可玩 ⇔ 解锁数(自 1 起连续首通数)≥ 本关号 —— 选关页同样可达该关,Next 只是导航快捷,
        /// 首通/重玩一致放行:重玩通关后 Next 灰掉属误伤(旧规则 firstWin && 有后关,
        /// 重玩/重试通关必灰 = 真机"偶现不可点"的根因)。导航不推进解锁(推进仍只认 RecordFirstWin,
        /// WS-04),每日结算恒禁、尾关无后关恒禁由调用分支保证。
        /// </summary>
        bool NextLevelPlayable()
        {
            if (_session == null || _session.IsDaily) return false;
            if (_session.LevelId < 1 || _session.LevelId >= _totalLevels) return false;
            return WaterSortProgressStore.UnlockedCount(WaterSortProgressStore.Load()) >= _session.LevelId;
        }

        void OnNextLevel()
        {
            // 防双击/连点:首击即锁钮(下次结算按 NextLevelPlayable 重放行;失败兜底分支恢复),
            // 防第二击落到新对局试管上误倒水
            if (_nextButton != null) _nextButton.SetInteractable(false);
            var level = _pack != null ? WaterSortLevelStore.FindById(_pack, _session.LevelId + 1) : null;
            if (level != null && _session.StartLevel(level)) // 换关:旧会话历史/过关标记随 StartLevel 复位
            {
                ShowPanel(Panel.Game);
                ApplyLanguage();
                RefreshTubeArea();
                return;
            }
            if (_nextButton != null) _nextButton.SetInteractable(NextLevelPlayable()); // 失败兜底恢复(理论不可达)
            ShowToast("watersort.toast.noLevels"); // 题库尾部/加载缺失兜底
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

        /// <summary>
        /// 结算面板 → 回大厅(M3.2 过关后局间出口):插屏展示候选点。
        /// 过关计数已在 OnLevelSolved 上报(NotifyLevelCompleted),此处只做展示判定——
        /// 频控在 AdsService 内部(去广告零广告 / 前 3 局保护 / 局间隔 4~6 分钟)。
        /// 不展示插屏的路径:连关(Next)/重开(Retry)连续玩法、对局中返回(BackButton)、
        /// 选关页回大厅(未完成新对局,SelectPanel/HubButton 仍直连 LeaveToHub)。
        /// </summary>
        void LeaveToHubAfterSettle()
        {
            ServiceLocator.Ads?.ShowInterstitial();
            LeaveToHub();
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
            SetDoubleButtonState(); // 翻倍钮文案(翻倍前/已翻倍态;未进首通结算时行已隐藏,无碍)
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
