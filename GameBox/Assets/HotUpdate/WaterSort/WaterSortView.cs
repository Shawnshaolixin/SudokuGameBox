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
    ///   SelectPanel/ItemTemplate            选关按钮模板:BoxButton + Label(TMP)(只渲染可玩关,无锁定态)
    ///   SelectPanel/LevelScroll/Viewport/Content  选关网格容器(克隆 ItemTemplate 进 Content,代码铺 5 列)
    ///   SelectPanel/HubButton               回大厅弹窗(MoreGames;按钮 = Pop 本视图 → OnHide 退模块)
    ///   GamePanel/TopBar/BackButton         返回选关(放弃本局进度,会话仍可再开局)
    ///   GamePanel/TopBar/GameTitle          关卡标题「第 N 关 · 难度」(StartGame 置文案)
    ///   GamePanel/TopBar/CoinLabel          TMP 金币余额(M1.4 双通道后随变更刷新)
    ///   GamePanel/StepText                  TMP 步数(每次盘面刷新同步)
    ///   GamePanel/TubeArea                  试管容器(本视图 AddComponent WaterSortTubeRack 代码绘制+点击)
    ///   GamePanel/BottomBar/UndoButton | RestartButton   免费操作(本版已接)
    ///   GamePanel/BottomBar/HintButton | ExtraTubeButton 金币/激励点位(M1.4 接配置后启用)
    ///   SettlePanel/Title | ResultText | NextButton | RetryButton | HubButton
    ///
    /// 退出纪律(与 WaterSortModule.OnExit 配合):本视图是模块压入 Router 的唯一自属视图,M1 内模块
    /// 不会在其上再压 Router 弹窗(结算/选关都是面板切换),故 OnHide 只会在「真的被 Pop」时触发
    /// (主动 HubButton 或返回键)——即用户离开模块的唯一信号 → ExitAsync 复位模块状态(防卡 Active)。
    /// ⚠️ M2 若需在本视图之上压 Router 弹窗(如每日奖励),须按 DifficultySelectView 的
    /// StackCount!=0 被盖守卫模式复检,防被盖时误退。
    /// </summary>
    public sealed class WaterSortView : UIView
    {
        /// <summary>面板枚举:Select=选关 / Game=对局 / Settle=结算(同视图内切换,不压 Router 栈)。</summary>
        enum Panel { Select, Game, Settle }

        WaterSortSession _session;   // 本视图会话快照(OnShow 取 Instance;OnDestroy 退订后不再引用)
        bool _leaving;               // 退模块流程已启动(防 OnHide 重入重复 ExitAsync)
        WaterSortLevelPack _pack;    // 本次会话题库缓存(选关渲染成功后赋值;选关/下一关同源取关)

        Transform _selectPanel, _gamePanel, _settlePanel;
        Transform _itemTemplate, _content;
        TextMeshProUGUI _resultText, _stepText;
        BoxButton _nextButton, _undoButton, _restartButton;
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
            _gamePanel = FindInCard("GamePanel");
            _settlePanel = FindInCard("SettlePanel");
            _content = FindInCard("SelectPanel/LevelScroll/Viewport/Content");
            _itemTemplate = FindInCard("SelectPanel/ItemTemplate");
            _resultText = FindInCard("SettlePanel/ResultText")?.GetComponent<TextMeshProUGUI>();
            _stepText = FindInCard("GamePanel/StepText")?.GetComponent<TextMeshProUGUI>();
            _nextButton = FindInCard("SettlePanel/NextButton")?.GetComponent<BoxButton>();
            _undoButton = FindInCard("GamePanel/BottomBar/UndoButton")?.GetComponent<BoxButton>();
            _restartButton = FindInCard("GamePanel/BottomBar/RestartButton")?.GetComponent<BoxButton>();

            // 试管架:运行期挂到 TubeArea(热更组件不进 prefab 序列化,20 文档 §4 纪律同源)
            var tubeArea = FindInCard("GamePanel/TubeArea");
            if (tubeArea != null && tubeArea.GetComponent<WaterSortTubeRack>() == null)
                _rack = tubeArea.gameObject.AddComponent<WaterSortTubeRack>();
            if (_rack != null) _rack.PourRequested += OnPourRequested;

            Bind("SelectPanel/HubButton", LeaveToHub);
            Bind("GamePanel/TopBar/BackButton", OnBackToSelect);
            Bind("SettlePanel/HubButton", LeaveToHub);
            Bind("SettlePanel/RetryButton", OnRetry);
            if (_nextButton != null) _nextButton.OnClick(OnNextLevel);
            if (_undoButton != null) _undoButton.OnClick(OnUndo);
            if (_restartButton != null) _restartButton.OnClick(OnRestart);
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
                // 每日挑战入口预留(M2 按日期直取题库关开局);先回落常规选关,防空屏
                Debug.LogWarning("[WaterSort] 每日挑战 M2 接入,当前回落常规选关");
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
            // 放弃本局进度回选关(模块仍 Active,与 LeaveToHub 退出模块是两条独立路径)
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
        }

        /// <summary>试管点击裁决:合法→会话推进(BoardChanged 驱动本区刷新);非法→源管抖动,选中保留可再试目标。</summary>
        void OnPourRequested(int src, int dst)
        {
            if (_session == null || !_session.IsInLevel) return; // 面板切换竞态兜底
            if (!_session.TryPour(src, dst)) _rack?.ShakeTube(src);
        }

        // ---- 结算面板 ---- //

        void OnLevelSolved()
        {
            if (_session == null) return;
            // 首通推进(WS-04:解锁只认首通;重玩不推进)。金币发放 M1.4 接配置与 box.coins,此处仅解锁。
            bool firstWin = WaterSortProgressStore.RecordFirstWin(_session.LevelId);
            ShowPanel(Panel.Settle);
            if (_resultText != null)
                _resultText.text = L10n.Format("watersort.settle.result",
                    _session.MoveCount, DifficultyText(_session.Difficulty));
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
            // 对局标题「第 N 关 · 难度」:仅对局中显示,StartGame/换关均先经 ApplyLanguage 到位
            if (_session != null && _session.IsInLevel)
                SetText("GamePanel/TopBar/GameTitle", L10n.Format("watersort.level.title",
                    _session.LevelId, DifficultyText(_session.Difficulty)));
            SetText("GamePanel/TopBar/CoinLabel", CoinText()); // M1.4 金币变更订阅前,入场先刷一次
            SetText("SettlePanel/Title", L10n.Get("watersort.settle.title"));
            SetLabel("SelectPanel/HubButton", L10n.Get("game.back")); // 复用既有键(返回)
            SetLabel("GamePanel/TopBar/BackButton", L10n.Get("game.back"));
            SetLabel("GamePanel/BottomBar/UndoButton", L10n.Get("game.undo"));
            SetLabel("GamePanel/BottomBar/RestartButton", L10n.Get("watersort.btn.restart"));
            SetLabel("SettlePanel/RetryButton", L10n.Get("watersort.btn.retry"));
            SetLabel("SettlePanel/HubButton", L10n.Get("settlement.home")); // 复用既有键(返回菜单)
            SetLabel("SettlePanel/NextButton", L10n.Get("watersort.btn.next"));
        }

        string CoinText()
        {
            long coins = ServiceLocator.Save != null ? ServiceLocator.Save.Coins : 0;
            return L10n.Format("watersort.coins", coins);
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
