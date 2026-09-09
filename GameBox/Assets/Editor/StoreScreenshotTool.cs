using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Box.Gameplay;
using Box.HotUpdate.Sudoku;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Google Play 商店截图自动产出工具(2026-09-09,发布清单资源):
/// 一键进入 Play → 脚本驱动真实 UI 流程,在固定 1080×1920(画布参考分辨率,
/// 满足 Play Console 9:16/PNG/≤8MB 要求)依次定格 5 个画面并落盘:
///   01 主菜单 → 02 难度选择 → 03 对局·输入/高亮态 → 04 对局·笔记模式 → 05 结算三星
/// 输出:Build/StoreScreenshots/phone_0X_*.png(Assets 外,不污染版本库)。
/// 用法:菜单 Box ▸ Store ▸ Capture Phone Screenshots;播放过程可见,跑完自动退出 Play,
/// 可反复重拍(对局为随机题,画面略有差异)。注意会把 Game 视图分辨率切到 1080×1920,
/// 并预置 HasShownTutorial=1 屏蔽首局引导遮罩(需复测引导时用 Box/Dev/Reset 清键)。
/// 域重载适配:进出 Play 默认重载程序集域,菜单点击后创建的对象会被清空(曾致停在主界面
/// 无人驱动)→ 菜单只把"待跑"意图写进 EditorPrefs(跨域不丢),运行实例改在
/// EnteredPlayMode(新域)里创建;playModeStateChanged 订阅挂在 [InitializeOnLoadMethod]
/// 上常驻注册,任何 Play 方式(含域重载开关)下都可靠触发。
/// 驱动走强类型(Box.Editor 引用热更程序集);session 字段沿用冒烟测试同款反射单点。
/// </summary>
public static class StoreScreenshotTool
{
    const string TutorialPrefsKey = "HasShownTutorial"; // 屏蔽首局引导(同 FirstRunGuide.PrefsKey)
    const string PendingPrefsKey = "StoreShot.Pending"; // 意图交接键(EditorPrefs 落注册表,域重载不丢)

    static readonly string s_OutDir = Path.GetFullPath(
        Path.Combine(Application.dataPath, "..", "Build", "StoreScreenshots"));

    static StoreCapture _runner; // 单次运行实例(仅 Play 域内存活;回编辑态即清)

    /// <summary>每次程序集域加载都会执行(含进出 Play 的域重载)→ 订阅常驻,不依赖菜单点击时刻所在域的存活。</summary>
    [InitializeOnLoadMethod]
    static void HookPlayMode()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            if (!EditorPrefs.GetBool(PendingPrefsKey, false)) return; // 非工具触发的普通 Play,不打扰
            EditorPrefs.DeleteKey(PendingPrefsKey); // 消费意图:工具每次只跑一遍
            try
            {
                _runner = new StoreCapture();
                _runner.Begin();
            }
            catch (Exception e)
            {
                // 启动失败必须复位,防残留意图导致下次普通 Play 被误触发
                _runner = null;
                Debug.LogError("[StoreShot] 流程启动失败(详情见下),可再次点击菜单重试。\n" + e);
            }
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            // 回到编辑态统一收尾:正常跑完 / 截图超时中止 / 手动按 Stop,都会经过这里
            if (_runner != null) { _runner.CleanupNow(); _runner = null; }
        }
    }

    /// <summary>一键重拍:预置引导已看 → 进 Play → 驱动 5 画面 → 自动退出。</summary>
    [MenuItem("Box/Store/Capture Phone Screenshots (1080x1920)")]
    public static void CapturePhoneScreenshots()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[StoreShot] 请先退出 Play 再执行(避免与运行态冲突)");
            return;
        }
        Directory.CreateDirectory(s_OutDir);
        PlayerPrefs.SetInt(TutorialPrefsKey, 1); // 预置"已引导":截图中不出现首局引导遮罩
        PlayerPrefs.Save();
        EditorPrefs.SetBool(PendingPrefsKey, true); // 只登记意图,实例等 EnteredPlayMode 再建(见类头域重载注释)
        Debug.Log($"[StoreShot] 输出目录:{s_OutDir}(重拍同名覆盖)。即将进入 Play 并自动执行…");
        EditorApplication.EnterPlaymode();
    }

    /// <summary>一次完整截图流程:线性阶段状态机,EditorApplication.update 每帧推进。</summary>
    class StoreCapture
    {
        enum Stage { Home, OpenDifficulty, EnterGameplay, StageInputs, StageNotes, CompleteBoard }

        Stage _stage = Stage.Home;
        float _stageEnterTime;   // 当前阶段进入时刻(阶段内等待基准)
        float _readyAt = -1f;    // 本阶段目标对象就绪时刻(就绪后再稳等,防慢加载提前拍)
        float _deadline;         // 全流程兜底预算,防工具卡死编辑器
        bool _homeLoadIssued;
        int _shotIndex;
        MainMenuView _menu;
        GameplayView _gameplay;
        GameSession _session;

        // 截图"飞行中":CaptureScreenshot 帧末异步写盘;落盘前状态机停步、每帧只 Poll 文件
        // (曾用 Thread.Sleep 轮询阻塞主线程 → 当前帧渲染不完 → 永不写盘 → 卡死,故改为逐帧检查)
        string _shotName, _shotPath;
        float _shotDeadline;
        Action _shotDone; // 确认落盘后执行(点击推进 / 摆拍 / 退出 Play)

        public void Begin()
        {
            // 固定游戏画面分辨率 = 设计参考分辨率(1080×1920),画布 1:1 无缩放;
            // 编辑器 Game 视图随之一并切换(退出后如需其他尺寸自行改回)
            Screen.SetResolution(1080, 1920, FullScreenMode.Windowed);
            _deadline = Time.realtimeSinceStartup + 180f;
            _stageEnterTime = Time.realtimeSinceStartup;
            Log($"Play 就绪({Screen.width}x{Screen.height}),开始自动截图流程…");
            EditorApplication.update += Tick;
        }

        /// <summary>收尾(退订每帧驱动);playModeStateChanged 由外层静态钩子常驻订阅,不在实例内。</summary>
        public void CleanupNow() => EditorApplication.update -= Tick;

        void Tick()
        {
            if (!EditorApplication.isPlaying) return; // 退出 Play(ExitPlaymode/手动 Stop)由 EnteredEditMode 收尾
            if (Time.realtimeSinceStartup > _deadline) { Fail("超过 180s 总预算(某步等待超时)"); return; }

            if (_shotName != null) { PollShotWrite(); return; } // 上一张未落盘:只等它,不推进状态机

            switch (_stage)
            {
                case Stage.Home: HomeStage(); break;
                case Stage.OpenDifficulty: DifficultyStage(); break;
                case Stage.EnterGameplay: EnterGameplayStage(); break;
                case Stage.StageInputs: WaitAndShoot("03_gameplay_input", Stage.StageNotes, 0.5f, StageNotesState); break;
                case Stage.StageNotes: WaitAndShoot("04_gameplay_notes", Stage.CompleteBoard, 0.5f, CompleteBoardState); break;
                case Stage.CompleteBoard: SettlementStage(); break;
            }
        }

        // ---- 线性推进(每帧重入;条件满足 → 拍 → 落盘回调里再推进) ----

        void Next(Stage s)
        {
            _stage = s;
            _stageEnterTime = Time.realtimeSinceStartup;
            _readyAt = -1f; // 新阶段重新等就绪
            Log($"→ 进入阶段 {s}");
        }

        /// <summary>目标对象就绪稳定 seconds 秒(首次调用记就绪时刻,防慢加载后就拍)。</summary>
        bool ReadySince(float seconds)
        {
            if (_readyAt < 0f) _readyAt = Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - _readyAt >= seconds;
        }

        bool ElapsedFromStage(float seconds) => Time.realtimeSinceStartup - _stageEnterTime >= seconds;

        void HomeStage()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu")
            {
                if (!_homeLoadIssued) { _homeLoadIssued = true; SceneManager.LoadScene("MainMenu"); } // 只发起一次
                return;
            }
            if (_menu == null) { _menu = UnityEngine.Object.FindObjectOfType<MainMenuView>(); return; }
            if (!ReadySince(1.2f)) return; // 菜单装配/淡入动效收尾
            StartShot("01_home_menu", () =>
            {
                var start = FindButton(_menu.transform, "StartButton");
                if (start == null) { Fail("主菜单缺少 StartButton"); return; }
                start.onClick.Invoke(); // 开难度选择(AppBootstrap 已自举服务,模块清单入口可用)
                Next(Stage.OpenDifficulty);
            });
        }

        void DifficultyStage()
        {
            var popup = UnityEngine.Object.FindObjectOfType<DifficultySelectView>();
            if (popup == null) return; // 等弹窗(Addressables 加载 + 路由入栈)
            if (!ReadySince(0.6f)) return;
            StartShot("02_select_difficulty", () =>
            {
                // 弹窗内容在 Card 子节点;回退根直查(同冒烟测试约定)
                var easy = FindButton(popup.transform, "Card/EasyButton", "EasyButton");
                if (easy == null) { Fail("难度弹窗缺少 EasyButton"); return; }
                easy.onClick.Invoke();
                Next(Stage.EnterGameplay);
            });
        }

        void EnterGameplayStage()
        {
            if (SceneManager.GetActiveScene().name != "Gameplay") return; // 等切场景
            if (_gameplay == null) { _gameplay = UnityEngine.Object.FindObjectOfType<GameplayView>(); return; }
            if (!ReadySince(1.6f)) return; // 数字入场弹跳(≤0.55s)与淡入收尾
            if (!TryGetSession()) { Fail("无法取得 GameSession"); return; }
            StageBoardState(); // 摆输入/高亮态
            Next(Stage.StageInputs);
        }

        /// <summary>等 settle 秒稳定 → 拍一张,落盘回调里摆下一状态 → 进 next。</summary>
        void WaitAndShoot(string shot, Stage next, float settle, Action setup)
        {
            if (!ElapsedFromStage(settle)) return;
            StartShot(shot, () => { setup(); Next(next); });
        }

        void SettlementStage()
        {
            if (UnityEngine.Object.FindObjectOfType<SettlementPopupView>() == null) return; // 等结算弹窗
            if (!ReadySince(1.0f)) return; // 庆祝粒子/弹窗入场稳定
            StartShot("05_level_complete", () =>
            {
                Log($"全部完成:共 {_shotIndex} 张 → {s_OutDir}");
                EditorApplication.ExitPlaymode(); // 回编辑态由 EnteredEditMode 统一收尾
            });
        }

        // ---- 摆拍:对局状态编排(直接驱动 session,事件链带动视图刷新,同冒烟测试) ----

        /// <summary>均匀散布填 7 格正确值(输入橙字/给定深棕对比),再选中一个已填格作高亮锚点:
        /// 绿底选中 + 同行列宫浅杏 + 全盘同数黄橙,信息密度对商店图最友好。</summary>
        void StageBoardState()
        {
            var editable = EditableIndices();
            for (int k = 0; k < 7; k++)
            {
                int c = editable[editable.Count * k / 7];
                _session.SelectCell(c);
                _session.InputNumber(_session.Solution[c]);
            }
            _session.SelectCell(editable[editable.Count / 7]);
        }

        /// <summary>切笔记模式,空格落 2/4/7 三枚笔记(小号灰字,区别于正文数字)。</summary>
        void StageNotesState()
        {
            _session.ToggleInputMode(); // Number → Note(视图经 CellSelected 刷新按钮标签)
            int c = EditableIndices().Find(i => _session.GetNotes(i) == 0);
            _session.SelectCell(c);
            _session.InputNumber(2);
            _session.InputNumber(4);
            _session.InputNumber(7);
        }

        /// <summary>回数字模式,逐格填 solution(事件链驱动整盘刷新),末格触发结算。</summary>
        void CompleteBoardState()
        {
            if (_session.Mode != GameSession.InputMode.Number) _session.ToggleInputMode();
            foreach (int c in EditableIndices())
            {
                _session.SelectCell(c);
                _session.InputNumber(_session.Solution[c]);
            }
        }

        List<int> EditableIndices()
        {
            var list = new List<int>(81);
            for (int i = 0; i < 81; i++)
                if (_session.GetValue(i) == 0) list.Add(i);
            return list;
        }

        bool TryGetSession()
        {
            // 沿用冒烟测试同款反射:GameplayView._session 无公开访问器(热更程序集内部态)
            var f = typeof(GameplayView).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
            _session = f?.GetValue(_gameplay) as GameSession;
            return _session != null;
        }

        // ---- 落盘(CaptureScreenshot 帧末异步写盘 → 逐帧 Poll,绝不阻塞主线程) ----

        /// <summary>发出截图请求并进入"飞行中";文件确认落盘后回调 onWritten。</summary>
        void StartShot(string name, Action onWritten)
        {
            string path = Path.Combine(s_OutDir, $"phone_{name}.png");
            // 先删旧图:同名重拍若残留,File.Exists 会把上一张旧图误判为本次落盘
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            _shotName = name;
            _shotPath = path;
            _shotDeadline = Time.realtimeSinceStartup + 8f;
            _shotDone = onWritten;
            Log($"发起截图 {_shotIndex + 1}/5:{name} …");
        }

        void PollShotWrite()
        {
            if (File.Exists(_shotPath))
            {
                _shotIndex++;
                Log($"已截取 {_shotIndex}/5:{_shotName}.png({new FileInfo(_shotPath).Length / 1024}KB)");
                var cb = _shotDone;
                _shotName = null;
                _shotDone = null;
                cb?.Invoke(); // 推进下一阶段(点击/摆拍/退出)
            }
            else if (Time.realtimeSinceStartup >= _shotDeadline)
            {
                string name = _shotName;
                _shotName = null;
                _shotDone = null;
                Fail($"截图写盘超时:{name}(确认 Game 视图可见且处于播放状态后重试)");
            }
            // 未到死线且文件未出现:继续等(每帧重入)
        }

        void Fail(string reason)
        {
            LogError($"中止:{reason}(输出目录已有截图保留)");
            _shotName = null;
            _shotDone = null;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); // 回编辑态由 EnteredEditMode 收尾
            else { CleanupNow(); _runner = null; } // 已在编辑态(极端情形):直接清理防残留
        }

        static Button FindButton(Transform root, params string[] paths)
        {
            foreach (var p in paths)
            {
                var t = root.Find(p);
                if (t != null && t.TryGetComponent(out Button b)) return b;
            }
            return null;
        }

        static void Log(string msg) => Debug.Log("[StoreShot] " + msg);
        static void LogError(string msg) => Debug.LogError("[StoreShot] " + msg);
    }
}
