using System.IO;
using Box.ModuleFramework;
using Box.UI;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;
using WaterSort.Core;

/// <summary>
/// 水排序玩法资源生成器(M1.3):主视图 prefab + 独立 Addressables 组 Game_WaterSort + 首批关卡 JSON。
/// CLI 无头执行:unity run GameBox -- -executeMethod WaterSortViewSetup.Build(12 关 demo 包)
/// 或 -executeMethod WaterSortViewSetup.BuildFull100(100 关正式题库,M1.5)
/// 幂等:prefab/条目/关卡 JSON 已存在且参数一致 → 仅自愈(挂桥/地址校准),可反复执行。
///
/// ① 新建 Modules/WaterSort/Prefabs/WaterSortView.prefab —— 热更视图不进 prefab 序列化(20 文档 §4):
///    根挂 AOT HotViewBinder(viewTypeFullName = WaterSortView),运行期动态 AddComponent(UIView 由桥挂回)。
///    节点树严格按 WaterSortView 类头契约命名(对局三栏/每日主页/激励确认面板;选关页与结算页
///    2026-09-06 收口已下线);按钮为方块皮肤图承载(底栏四钮 190 方块、两处返回钮 150 方块),
///    试管区与液块由运行期 WaterSortTubeRack 代码绘制,prefab 只提供容器(几何 1080x1920 中心锚布局)。
///    文本不预赋字体:工程 TMP Settings 默认 MiSans 动态字体(FontSetup 后所有新建 TMP 免赋)。
/// ② 关卡 JSON(Assets/Modules/WaterSort/Data/regular_levels.json):按关号 1..N 调 WaterSortGenDefaults.
///    SpecForIndex 取规格 + WaterSortLevelGen.Generate(固定种子)生成 → WaterSortLevelCodec.Encode 落盘,
///    与运行期加载结构 WaterSortLevelPack 同源(JsonUtility 反序列化直读)。
/// ③ 全部入新建组 Game_WaterSort(PRD WS-20 组名;prefab 地址 UI/WaterSortView = WaterSortModule.
///    MainViewAddress;JSON 地址 WaterSort/Levels/regular_levels.json = WaterSortLevelStore.LevelsAddress)。
/// ④ 幂等迁移与几何校准(2026-09-06 收口:删选关/结算子树,底栏四钮与两处返回钮迁方块皮肤图几何,
///    重复执行零写入)+ 模块清单接入(id=watersort → WaterSortModule,Phase45ModuleSetup.AddEntry
///    幂等,13 文档步骤 3)。
/// </summary>
public static class WaterSortViewSetup
{
    // 资源落点(与 Modules/Sudoku 同级布局;Data = 预生成关卡 JSON)
    const string PrefabDir = "Assets/Modules/WaterSort/Prefabs";
    const string DataDir = "Assets/Modules/WaterSort/Data";
    const string PrefabPath = PrefabDir + "/WaterSortView.prefab";
    const string LevelsJsonPath = DataDir + "/regular_levels.json";

    // Addressables:组名按 WS-20;地址与运行时常量一一对应(见类头)
    const string GroupGameWaterSort = "Game_WaterSort";
    const string PrefabAddress = "UI/WaterSortView";
    const string LevelsAddress = "WaterSort/Levels/regular_levels.json";

    const int ReverifyTimeLimitMs = 2000; // 逐关复证限时:离线批处理无 UX 约束,2s 吸收生成/复证
                                           // 两时点的墙钟抖动(400ms 复证曾现 #50 假超时,见 VerifyLevelsJson)

    const int DemoLevelCount = 12;   // M1.3 demo 题库(开发期冒烟用)
    const int FullLevelCount = 100;  // M1.5 首批正式题库(≥100 关,WS-03;数量即内容形态,切换即重写)
    const int SeedBase = 0x5757;     // 关号种子基(稳定复现;demo 包与最终题库同管线同种子域)

    // 每日题库(M2.3,WS-09):生成工具 WaterSortDailyGenSetup 与运行时 WaterSortDailyLevelStore 共用落点;
    // 地址须与 WaterSortDailyLevelStore.DailyLevelsAddress 保持一字不差(运行期加载常量,双处同步)
    internal const string DailyLevelsPath = DataDir + "/daily_levels.json";
    internal const string DailyLevelsAddress = "WaterSort/Levels/daily_levels.json";

    // 占位配色(与运行时 WaterSortTubeRack 色板无关;文本/背景用,表现后置 AIGC 替换)
    static readonly Color Backdrop = new Color(0.05f, 0.08f, 0.11f, 1f); // 全屏深蓝灰(玩法底色)
    static readonly Color Accent = new Color(0.20f, 0.55f, 0.90f);       // 主按钮蓝(同 MoreGames)

    [MenuItem("Box/WaterSort/Build View Prefab + Levels(12 demo)")]
    public static void Build() => BuildInternal(DemoLevelCount);

    /// <summary>M1.5 全量入口:整批重生成 N 关(数量变化即覆盖重写,同种子可复现)。</summary>
    public static void BuildFull(int count) => BuildInternal(count);

    /// <summary>首批正式题库 100 关(CLI -executeMethod 无参入口;统计见日志,分布可复核)。</summary>
    [MenuItem("Box/WaterSort/Build View Prefab + Levels(100 full)")]
    public static void BuildFull100() => BuildInternal(FullLevelCount);

    /// <summary>
    /// M2.2 题库重生成入口:档位表 M2.1 定版后强制重生成 100 关(数量未变幂等判定会跳过,故走 force),
    /// 同种子域(0x5757)可复现;日志含吞吐/尝试统计(M2.2 验收证据,见 docs/19 附录 A.4)。
    /// </summary>
    [MenuItem("Box/WaterSort/Build Levels(100 full, M2.2 强制重生成)")]
    public static void BuildFull100Regen() => BuildInternal(FullLevelCount, forceLevels: true);

    /// <summary>
    /// M2.1 难度代理校准数据任务(WS-03 AC「出包前题库采样校准」正式跑数;CLI 无参入口):
    /// 按色数 3~10 采样「散射首块可测实解板」的实测深度——3 色取 IDA* 精确最优、≥4 色取 SolveAny
    /// 首解深度(代理),每色独立种子(0xC4A1 + 色数*7919),同参数可复现;
    /// 口径(M2.1):只统计可测实解(1 ≤ 步数 &lt; AnyBoundCap=100),预解/封顶漫游解/超时换题重散并
    /// 单独计数(与生成器窗口过滤等价)→ 每色最多 MaxAttempts=40 次散射/样本;
    /// 输出 min/P10/P50/P90/max/avg + 命中率表供对照 SpecForIndex 步窗定档(纯计算,零资产写入)。
    /// 样本量:3~6 色 200 全量;7~10 色命中率 ~3% 且超时占比随色数升,200 样本任务时长失控,
    /// 由 CalibrateDifficultyTail 降档批次承接(本方法跑 3~6 即可)。
    /// </summary>
    [MenuItem("Box/WaterSort/Calibrate Difficulty Proxy(M2 采样)")]
    public static void CalibrateDifficultyM2()
    {
        const int samples = 200;       // 全量色数样本量(3~6 色用;高色数走 Tail 批次)
        const int seedBase = 0xC4A1;
        Debug.Log("[WaterSortCalib] ===== 难度代理校准采样开始(3~6 色 200 样本,实解口径,同种子可复现) =====");
        for (int colors = 3; colors <= 6; colors++)
        {
            // 3 色用精确最优(≤3 色 IDA* 实时,Spike 实测);≥4 色用 SolveAny 首解深度代理
            var r = colors <= 3
                ? WaterSortCalib.SampleOptimalSteps(colors, seedBase + colors * 7919, samples)
                : WaterSortCalib.SampleProxyDepth(colors, seedBase + colors * 7919, samples);
            LogCalibRow(r, colors <= 3 ? "最优" : "代理");
        }
        Debug.Log("[WaterSortCalib] ===== 主批次采样完成,高色数判读见 Tail 批次 =====");
    }

    /// <summary>
    /// M2.1 难度代理校准·高色数尾部批次(7~10 色):口径与主批次全同,仅样本量按色数降档——
    /// 高色数单散射实解命中率 ~3% 上下、超时(400ms 快筛)占比随色数上升,200 样本全量预算失控
    /// (3~6 色 200 已由主批次定稿)。40~100 实解样本的分位误差 ≪ 步窗宽度(15~30),足够定档。
    /// 每色行独立种子、逐行落日志即留档,可断点续跑(单行前缀与主批次同种子同口径可衔接判读)。
    /// </summary>
    [MenuItem("Box/WaterSort/Calibrate Difficulty Proxy(Tail 7-10)")]
    public static void CalibrateDifficultyTail()
    {
        // 样本量表:索引=色数(3~6 由主批次承接,本方法只跑 7~10)
        int[] samplesByColor = { 0, 0, 0, 200, 200, 200, 200, 100, 80, 60, 50 };
        const int seedBase = 0xC4A1;
        Debug.Log("[WaterSortCalib] ===== 尾部批次采样开始(7~10 色,样本降档 100/80/60/50,同种子可复现) =====");
        for (int colors = 7; colors <= 10; colors++)
            LogCalibRow(WaterSortCalib.SampleProxyDepth(colors, seedBase + colors * 7919, samplesByColor[colors]), "代理");
        Debug.Log("[WaterSortCalib] ===== 尾部批次采样完成,与主批次行合并判读定档 =====");
    }

    /// <summary>校准结果单色行日志(主/尾批次共用格式,逐行留档可断点续跑)。</summary>
    static void LogCalibRow(WaterSortCalibResult r, string modeLabel)
    {
        Debug.Log($"[WaterSortCalib] {r.Colors}色 {modeLabel} " +
                  $"实解={r.Solved}/{r.Samples} 未解={r.Unsolved} 试散={r.ScattersTried} " +
                  $"封顶={r.CapHits} 预解={r.PreSolved} 超时={r.Timeouts} 命中率={r.RealHitRate:P1} " +
                  $"min={r.MinSteps} p10={r.P10} p25={r.P25} p50={r.P50} p75={r.P75} p90={r.P90} " +
                  $"max={r.MaxSteps} avg={r.AvgSteps:F1} 耗时={r.TotalMs}ms");
    }

    static void BuildInternal(int levelCount, bool forceLevels = false)
    {
        EnsureFolders();
        BuildPrefab();
        EnsureGroup();
        RegisterPrefabEntry();
        GenerateLevelsJson(levelCount, forceLevels);
        RegisterLevelsEntry();
        EnsureModuleEntry(); // 清单接入(幂等):大厅 More Games 入口新增「水排序」
        AssetDatabase.SaveAssets();
        Debug.Log($"[WaterSortSetup] 资源就绪: prefab + Game_WaterSort 组 + {levelCount} 关题库");
    }

    // ---- ① prefab(幂等:存在即加载校准,不重复重建) ----

    static void BuildPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var root = BuildRootTree();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[WaterSortSetup] 已新建 prefab: " + PrefabPath);
        }
        EnsureBinder(); // 新建后首次运行也会走自愈路径(此时必命中,保持行为单一路径)
        TrimRemovedPanels(); // 2026-09-06:旧树删选关/结算子树(新树不建,零写盘)
        EnsureDailyNodes(); // M2.3:每日主页(缺才建;SelectPanel 随删,DailyButton 入口钮下线)
        EnsureIconButtons(); // 图标钮几何校准:底栏四钮 190 方块 + 两处返回钮 150 方块(幂等)
        EnsureAdPanelNodes(); // M3.1:激励确认面板(缺才建;结算翻倍钮已随结算页下线)
    }

    /// <summary>
    /// 2026-09-06 UI 收口迁移入口(旧 prefab 原地升级;新装/重跑 Build 走 BuildPrefab 自动包含):
    /// 删选关/结算子树 → 每日/图标钮几何校准 → 皮肤图扫描入库。幂等,可重复执行。
    /// </summary>
    [MenuItem("Box/WaterSort/Migrate UI 2026-09(删选关/结算 + 图标钮几何)")]
    public static void Migrate2026Ui()
    {
        BuildPrefab();                 // 迁移链(删子树 + 几何校准;含缺树新建分支)
        WaterSortSkinImporter.Sweep(); // 新皮肤图入库(地址 = WaterSort/UI/&lt;名&gt;,归口 SkinImporter)
        AssetDatabase.SaveAssets();
        Debug.Log("[WaterSortSetup] 2026-09 UI 收口迁移完成(选关/结算已删,按钮迁方块图标几何)");
    }

    /// <summary>根挂/校准 HotViewBinder(幂等):LoadPrefabContents 原地改,保 GUID。</summary>
    static void EnsureBinder()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            const string typeName = "Box.HotUpdate.WaterSort.WaterSortView, Box.HotUpdate.WaterSort";
            var binder = root.GetComponent<HotViewBinder>();
            if (binder != null && binder.ViewTypeFullName == typeName) return; // 已配好,零写入
            if (binder == null)
                binder = root.AddComponent<HotViewBinder>();
            binder.ViewTypeFullName = typeName;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[WaterSortSetup] HotViewBinder 挂载校准完成");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>新建完整节点树(空 prefab 首建用;契约见 WaterSortView 类头注释)。</summary>
    static GameObject BuildRootTree()
    {
        var root = new GameObject("WaterSortView", typeof(RectTransform), typeof(CanvasGroup),
            typeof(HotViewBinder), typeof(Image));
        var rt = (RectTransform)root.transform;
        Stretch(rt);
        root.GetComponent<Image>().color = Backdrop;

        // ---- 对局面板(顶栏/步数/试管区/底栏;试管本体由运行期代码绘制) ----
        var game = NewPanel(root.transform, "GamePanel");
        var topBar = NewNode(game, "TopBar", new Vector2(0, 845), new Vector2(1080, 130), new Color(0, 0, 0, 0));
        NewButton(topBar, "BackButton", "", new Vector2(-400, 0), BackIconSize, true);
        NewText(topBar, "GameTitle", "", new Vector2(0, 0), new Vector2(640, 90), 44, true);
        NewText(topBar, "CoinLabel", "", new Vector2(395, 0), new Vector2(300, 80), 36, false);
        NewText(game, "StepText", "", new Vector2(0, 715), new Vector2(560, 84), 42, false);
        NewNode(game, "TubeArea", new Vector2(0, -60), new Vector2(1040, 1380), new Color(0, 0, 0, 0));
        var bottomBar = NewNode(game, "BottomBar", new Vector2(0, -845), new Vector2(1080, 130), new Color(0, 0, 0, 0));
        foreach (var b in BarButtons) NewButton(bottomBar, b.Name, "", b.Pos, b.Size, true);

        // ---- 每日主页面板(M2.3;标题/返回/今日状态/连续天数/开始钮,ShowPanel 与对局互斥) ----
        var daily = NewPanel(root.transform, "DailyPanel");
        NewButton(daily, "BackButton", "", new Vector2(-400, 830), BackIconSize, true);
        NewText(daily, "Title", "", new Vector2(0, 830), new Vector2(640, 90), 48, true);
        NewText(daily, "StateText", "", new Vector2(0, 200), new Vector2(900, 110), 64, true);
        NewText(daily, "StreakText", "", new Vector2(0, 20), new Vector2(700, 80), 44, false);
        NewButton(daily, "PlayButton", "", new Vector2(0, -260), new Vector2(520, 128), true);

        // ---- 内嵌激励确认面板(M3.1;根下最后 = 恒盖其它面板,见 EnsureAdPanelNodes) ----
        BuildAdPanelTree(root.transform);

        // 初始面板:对局可见(其余关闭,ShowPanel 由视图切换)
        game.gameObject.SetActive(true);
        daily.gameObject.SetActive(false);
        return root;
    }

    // ---- ①b 节点迁移与几何校准(幂等:缺则建、几何漂移才校准;重复执行零写入) ----

    // 2026-09-06 收口几何(与 WaterSortView 类头契约同步):底栏四操作钮 = 方块皮肤图承载(去文字化),
    // 顶栏与每日主页返回钮同为方块 —— 旧 pill 几何(206x96 / 220x88)由 EnsureIconButtons 一次性迁移
    static readonly Vector2 IconSize = new Vector2(190, 190);    // 底栏操作钮(边缘间隙 80,屏边留白 40)
    static readonly Vector2 BackIconSize = new Vector2(150, 150); // 返回钮

    struct BarButtonSpec
    {
        public string Name;
        public Vector2 Pos;
        public Vector2 Size;
    }

    static readonly BarButtonSpec[] BarButtons =
    {
        new BarButtonSpec { Name = "UndoButton",      Pos = new Vector2(-405, 0), Size = IconSize },
        new BarButtonSpec { Name = "HintButton",      Pos = new Vector2(-135, 0), Size = IconSize },
        new BarButtonSpec { Name = "ExtraTubeButton", Pos = new Vector2(135, 0),  Size = IconSize },
        new BarButtonSpec { Name = "RestartButton",   Pos = new Vector2(405, 0),  Size = IconSize },
    };

    /// <summary>
    /// 2026-09-06 收口迁移:删除已下线的选关页(SelectPanel)与结算页(SettlePanel)整棵子树
    /// (选关滚动列表/结算三钮/翻倍行随子树一并删除)。旧树(2026-09-05 及更早)才有这两棵,
    /// 新建树不建、旧树删后重跑零写盘;视图侧对应改造见 WaterSortView 类头(过关 = 胜利图自动跳关)。
    /// </summary>
    static void TrimRemovedPanels()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        bool dirty = false;
        try
        {
            foreach (var name in new[] { "SelectPanel", "SettlePanel" })
            {
                var gone = root.transform.Find(name);
                if (gone != null)
                {
                    Object.DestroyImmediate(gone.gameObject);
                    dirty = true;
                }
            }
            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[WaterSortSetup] 已删除下线路口面板子树:SelectPanel/SettlePanel(2026-09-06 收口)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 图标钮几何校准(2026-09-06 收口):底栏四钮 190 方块 + 顶栏/每日主页返回钮 150 方块,
    /// 缺失补建、漂移校准(Label 随钮缩放留兜底文字);重复执行比对一致即零写盘。
    /// 热更视图不序列化进 prefab,本区域只补纯 AOT 节点容器(与 BuildRootTree 同构),行为全在 WaterSortView。
    /// </summary>
    static void EnsureIconButtons()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        bool dirty = false;
        try
        {
            var bottom = root.transform.Find("GamePanel/BottomBar");
            if (bottom != null)
            {
                foreach (var b in BarButtons)
                {
                    var t = bottom.Find(b.Name);
                    if (t == null)
                    {
                        // 极旧树缺钮(M1.3 两钮时代):按当前契约整钮补建(Accent + BoxButton + Label)
                        NewButton(bottom, b.Name, "", b.Pos, b.Size, true);
                        dirty = true;
                        continue;
                    }
                    dirty |= Calibrate((RectTransform)t, b.Pos, b.Size);
                }
            }
            // 对局顶栏返回钮(左位方块 150)
            var topBackParent = root.transform.Find("GamePanel/TopBar");
            if (topBackParent != null)
            {
                var back = topBackParent.Find("BackButton");
                if (back == null)
                {
                    NewButton(topBackParent, "BackButton", "", new Vector2(-400, 0), BackIconSize, true);
                    dirty = true;
                }
                else dirty |= Calibrate((RectTransform)back, new Vector2(-400, 0), BackIconSize);
            }
            // 每日主页返回钮(同级根坐标;M2.3 老树为 220x88 pill,随本版迁移为 150 方块)
            var dailyParent = root.transform.Find("DailyPanel");
            if (dailyParent != null)
            {
                var back = dailyParent.Find("BackButton");
                if (back == null)
                {
                    NewButton(dailyParent, "BackButton", "", new Vector2(-400, 830), BackIconSize, true);
                    dirty = true;
                }
                else dirty |= Calibrate((RectTransform)back, new Vector2(-400, 830), BackIconSize);
            }
            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[WaterSortSetup] 图标钮几何校准完成(底栏 190 方块 × 4 + 返回钮 150 方块 × 2)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>按钮几何校准:pos/size 与契约不符才写,Label 随钮缩放(返回是否有变更)。</summary>
    static bool Calibrate(RectTransform rt, Vector2 pos, Vector2 size)
    {
        bool dirty = false;
        if (NotSame(rt.anchoredPosition, pos)) { rt.anchoredPosition = pos; dirty = true; }
        if (NotSame(rt.sizeDelta, size)) { rt.sizeDelta = size; dirty = true; }
        var label = rt.Find("Label");
        var labelSize = size - new Vector2(24, 16); // NewButton 的文案留边口径
        if (label != null && NotSame(((RectTransform)label).sizeDelta, labelSize))
        {
            ((RectTransform)label).sizeDelta = labelSize;
            dirty = true;
        }
        return dirty;
    }

    /// <summary>
    /// M2.3 幂等补建每日主页面板(WS-09;契约见 WaterSortView 类头):
    /// DailyPanel 整棵子树 —— 标题/返回/今日状态/连续天数/开始钮(与 GamePanel 并列,ShowPanel 双面板互斥)。
    /// 2026-09-06:SelectPanel 已下线,原挂其下的选关页 DailyButton 入口钮随子树删除,每日入口
    /// 只剩模块 args="daily"(隐藏入口,见 WaterSortView 类头)。全部子节点为纯 AOT 容器,
    /// 行为在 WaterSortView(Bind/FindInCard 路径);新建树由 BuildRootTree 直接生成,本方法只服务旧树迁移。
    /// </summary>
    static void EnsureDailyNodes()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        bool dirty = false;
        try
        {
            var daily = root.transform.Find("DailyPanel");
            if (daily == null)
            {
                var panel = NewPanel(root.transform, "DailyPanel"); // 全屏节区(同 GamePanel)
                daily = panel;
                // 顶栏:返回(左,方块 150,与对局顶栏返回钮同构)+ 标题(中,运行期 ApplyLanguage 落文案)
                NewButton(daily, "BackButton", "", new Vector2(-400, 830), BackIconSize, true);
                NewText(daily, "Title", "", new Vector2(0, 830), new Vector2(640, 90), 48, true);
                // 中部状态区:今日完成状态(大字)/ 连续天数(小字)/ 开始挑战钮(向下对齐)
                NewText(daily, "StateText", "", new Vector2(0, 200), new Vector2(900, 110), 64, true);
                NewText(daily, "StreakText", "", new Vector2(0, 20), new Vector2(700, 80), 44, false);
                NewButton(daily, "PlayButton", "", new Vector2(0, -260), new Vector2(520, 128), true);
                panel.gameObject.SetActive(false); // 初始隐藏(ShowPanel 由视图切换)
                dirty = true;
            }
            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[WaterSortSetup] DailyPanel 补建完成(M2.3 每日主页)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 激励确认面板补建(M3.1,WS-12/13;契约见 WaterSortView 类头):
    /// 玩法内嵌 AdPanel(全屏遮罩 + 卡片:消息 + 看广告/取消两钮)位于根下最后 —— 面板弹出即盖住
    /// 其它面板。刻意不进 Router 栈:UIRouter 覆盖下层会触发其 OnHide,而水排序 OnHide=退模块
    /// (防被盖误退,见 WaterSortView 类头退出纪律);内嵌面板零路由生命周期,遮罩拦点击,关闭只翻自身。
    /// 2026-09-06:结算页下线,SettlePanel/DoubleButton 翻倍行随之删除,本方法只剩 AdPanel;
    /// 新建树由 BuildRootTree → BuildAdPanelTree 直接生成,本方法只服务旧树补建。
    /// </summary>
    static void EnsureAdPanelNodes()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        bool dirty = false;
        try
        {
            if (root.transform.Find("AdPanel") == null)
            {
                BuildAdPanelTree(root.transform);
                dirty = true;
            }
            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[WaterSortSetup] AdPanel 补建完成(M3.1 激励确认面板)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>AdPanel 子树构建(新建树/旧树补建共用;返回 overlay 根,默认隐藏,ShowAdPanel 弹出)。</summary>
    static RectTransform BuildAdPanelTree(Transform parent)
    {
        var overlay = NewNode(parent, "AdPanel", Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
        Stretch(overlay);
        var img = overlay.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.55f);
        img.raycastTarget = true; // 遮罩拦截点击:面板弹出期间下层按钮不可达
        var card = NewNode(overlay, "Card", Vector2.zero, new Vector2(860, 440),
            new Color(0.10f, 0.14f, 0.18f, 0.98f));
        NewText(card, "MessageText", "", new Vector2(0, 70), new Vector2(740, 210), 42, false);
        NewButton(card, "ConfirmButton", "", new Vector2(-215, -155), new Vector2(360, 104), true);
        NewButton(card, "CancelButton", "", new Vector2(215, -155), new Vector2(360, 104), true);
        overlay.gameObject.SetActive(false); // 初始隐藏(ShowAdPanel 弹出)
        return overlay;
    }

    static bool NotSame(Vector2 a, Vector2 b)
    {
        // 0.05 单位容差:防重复执行对等效几何产生无谓写盘(prefab 内容漂移比对)
        return Mathf.Abs(a.x - b.x) > 0.05f || Mathf.Abs(a.y - b.y) > 0.05f;
    }

    /// <summary>
    /// 模块清单接入(13 文档步骤 3,幂等):入口 id="watersort" → WaterSortModule(弹窗模式,无场景)。
    /// 清单资产由 Phase4.5 生成并入库(Resources/Config/ModuleCatalog.asset);缺失时提示先行,不静默新建防双写。
    /// </summary>
    static void EnsureModuleEntry()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ModuleCatalog>("Assets/Resources/Config/ModuleCatalog.asset");
        if (catalog == null)
        {
            Debug.LogWarning("[WaterSortSetup] ModuleCatalog.asset 缺失:请先执行 Phase45ModuleSetup.Build");
            return;
        }
        // entryScene 为 v1.1 单场景化后废弃字段,传空串;大厅排序在数独(0)之后;
        // displayName = 本地化 key(module.watersort,2026-09-05 Bug 清单 5:游戏名默认英文)
        Phase45ModuleSetup.AddEntry(catalog, "watersort",
            "Box.HotUpdate.WaterSort.WaterSortModule", "", "module.watersort", 1);
    }

    // ---- ② 关卡 JSON(数量变化才重写;同种子确定性,重复执行内容一致) ----

    static void GenerateLevelsJson(int levelCount, bool force = false)
    {
        // 幂等判定:文件缺失或关数与目标不一致 → 重生成(数量 = 内容形态,见 M1.3/M1.5 交接);
        // force(M2.2)绕过:档位表定版后数量未变但规格窗已改,旧题需按新窗重落档
        if (!force && File.Exists(LevelsJsonPath) && LoadLevelsCount() == levelCount) return;

        var pack = new WaterSortLevelPack();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long attemptsTotal = 0;
        int maxAttempts = 0;
        for (int i = 1; i <= levelCount; i++)
        {
            var level = GenerateOne(i, out int attempts);
            attemptsTotal += attempts;
            maxAttempts = System.Math.Max(maxAttempts, attempts);
            if (level == null)
            {
                Debug.LogError($"[WaterSortSetup] 第 {i} 关生成失败(50 次种子平移全不中档),终止");
                return;
            }
            pack.levels.Add(level);
        }
        sw.Stop();
        // 吞吐统计(M2.2 NFR 实测记录;关/分 = 100 / (总耗时分))
        double totalSec = sw.ElapsedMilliseconds / 1000.0;
        Debug.Log($"[WaterSortSetup] 题库生成吞吐: {levelCount} 关 / {totalSec:F1}s = " +
                  $"{levelCount / (totalSec / 60.0):F1} 关/分;均 {sw.ElapsedMilliseconds / (double)levelCount:F0}ms/关; " +
                  $"总散射尝试 {attemptsTotal}(均 {attemptsTotal / (double)levelCount:F0}/关,单关最多 {maxAttempts})");
        // UTF-8 无 BOM(JsonUtility 原生字段名,与运行时反序列化一一对应)
        File.WriteAllText(LevelsJsonPath, JsonUtility.ToJson(pack, true));
        AssetDatabase.ImportAsset(LevelsJsonPath); // 生成为 TextAsset 资产(Addressables 可入库)
        Debug.Log($"[WaterSortSetup] 题库已{(File.Exists(LevelsJsonPath) ? "重" : "")}生成: {levelCount} 关 → {LevelsJsonPath}\n"
            + BuildPackSummary(pack));
    }

    /// <summary>
    /// 题库统计行(验收留档/难度抽查参考;每关步数 = 落档实测值:≤3 色 IDA* 精确最优 / ≥4 色 SolveAny 首解深度)。
    /// 按难度分段汇总数量与步数窗(分段编排见 WaterSortGenDefaults.SpecForIndex)。
    /// </summary>
    static string BuildPackSummary(WaterSortLevelPack pack)
    {
        if (pack == null || pack.levels == null || pack.levels.Count == 0) return "(空题库)";
        var sb = new System.Text.StringBuilder();
        for (int d = 0; d < 3; d++)
        {
            int count = 0, min = int.MaxValue, max = 0;
            long sum = 0;
            foreach (var l in pack.levels)
            {
                if ((int)l.difficulty != d) continue;
                count++;
                if (l.measuredSteps < min) min = l.measuredSteps;
                if (l.measuredSteps > max) max = l.measuredSteps;
                sum += l.measuredSteps;
            }
            if (count == 0) continue;
            var name = ((WaterSortDifficulty)d).ToString();
            sb.AppendLine($"  [{name}] {count} 关,步数 {min}~{max}(均值 {sum / (double)count:0.0})");
        }
        return "  === 题库分布 ===\n" + sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 按关号生成一关:默认规格 + 固定种子;种子失败则就近平移重试(最多 49 次)。
    /// attempts 输出实际散射尝试次数(M2.2 吞吐留档:r.Succeeded 为 false 时 = 50 全耗)。
    /// </summary>
    static WaterSortLevelData GenerateOne(int levelNo, out int attempts)
    {
        var spec = WaterSortGenDefaults.SpecForIndex(levelNo);
        for (int k = 0; k < 50; k++)
        {
            var r = WaterSortLevelGen.Generate(spec, SeedBase + levelNo * 7919 + k);
            attempts = k + 1;
            if (!r.Succeeded) continue;
            return WaterSortLevelCodec.Encode(r.Board, levelNo, spec.Difficulty, r.MeasuredSteps);
        }
        attempts = 50;
        return null;
    }

    /// <summary>读现有 JSON 关数(损坏/空 → -1 触发重生成)。</summary>
    static int LoadLevelsCount()
    {
        try
        {
            var text = File.ReadAllText(LevelsJsonPath);
            var pack = JsonUtility.FromJson<WaterSortLevelPack>(text);
            return pack?.levels != null ? pack.levels.Count : -1;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[WaterSortSetup] 现有题库读取失败,将重生成: " + e.Message);
            return -1;
        }
    }

    /// <summary>
    /// M2.2 逐关验收工具(CLI 无参入口):JSON 反序列化 → 逐关 TryDecode → SolveAny 复证可解
    /// (限时 2s,见 ReverifyTimeLimitMs——400ms 对墙钟抖动敏感曾现假超时)
    /// → 断言 measuredSteps/colors 落在 SpecForIndex 档窗内(档位表 M2.1 定版后数据一致性背向校验)。
    /// 输出按难度分段的步数统计 + 失败清单;任一关失败即 LogError(CLI 批处理日志可抓),全绿则
    /// Debug.Log 输出验收摘要(供 M2 验收留档:100 关全可解 + 无档窗越界)。
    /// </summary>
    [MenuItem("Box/WaterSort/Verify Levels Pack(逐关验证)")]
    public static void VerifyLevelsJson()
    {
        if (!File.Exists(LevelsJsonPath))
        {
            Debug.LogError("[WaterSortSetup] 题库 JSON 缺失,请先生成: " + LevelsJsonPath);
            return;
        }
        var pack = JsonUtility.FromJson<WaterSortLevelPack>(File.ReadAllText(LevelsJsonPath));
        if (pack == null || pack.levels == null || pack.levels.Count == 0)
        {
            Debug.LogError("[WaterSortSetup] 题库 JSON 为空或损坏,无法验收");
            return;
        }
        var sb = new System.Text.StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int[] count = new int[3], windowFail = new int[3], solveFail = new int[3], colorFail = new int[3];
        int[] min = { int.MaxValue, int.MaxValue, int.MaxValue };
        int[] max = new int[3];
        long[] sum = new long[3];
        var failures = new System.Collections.Generic.List<string>();
        long solveMsTotal = 0;
        foreach (var l in pack.levels)
        {
            int d = (int)l.difficulty;
            var spec = WaterSortGenDefaults.SpecForIndex(l.id);
            // 档窗自洽:难度标签必须与 SpecForIndex 一致(标签由规格决定,漂移即生成管线断裂)
            if (spec.Difficulty != l.difficulty)
            {
                failures.Add($"#{l.id} 难度标签 {l.difficulty} 与档窗 {spec.Difficulty} 不一致");
                continue;
            }
            if (l.colors < spec.MinColors || l.colors > spec.MaxColors)
            {
                colorFail[d]++;
                failures.Add($"#{l.id} 色数 {l.colors} 越档窗 [{spec.MinColors},{spec.MaxColors}]");
                continue;
            }
            if (l.measuredSteps < spec.MinSteps || l.measuredSteps > spec.MaxSteps)
            {
                windowFail[d]++;
                failures.Add($"#{l.id} 落档步数 {l.measuredSteps} 越档窗 [{spec.MinSteps},{spec.MaxSteps}]");
                continue;
            }
            count[d]++;
            min[d] = System.Math.Min(min[d], l.measuredSteps);
            max[d] = System.Math.Max(max[d], l.measuredSteps);
            sum[d] += l.measuredSteps;
            // 复证可解:同引擎重解——DFS 遍历序确定性,但 400ms 截断对墙钟抖动敏感(#50 首跑即现
            // TimedOut 假失败:生成期限内完成、复证期同限时越线),故放宽至 2s(离线批处理无 UX 时限);
            // 仍超时 = 真异常(窗内 ≤34 步的题 2s 找不出解,判数据损坏)。
            if (!WaterSortLevelCodec.TryDecode(l, out var board))
            {
                solveFail[d]++;
                failures.Add($"#{l.id} 解码失败(tubes 形状不符)");
                continue;
            }
            var solveSw = System.Diagnostics.Stopwatch.StartNew();
            var res = WaterSortSolver.SolveAny(board, ReverifyTimeLimitMs);
            solveSw.Stop();
            solveMsTotal += solveSw.ElapsedMilliseconds;
            if (!res.Solved || res.TimedOut)
            {
                solveFail[d]++;
                failures.Add($"#{l.id} 复证不可解(TimedOut={res.TimedOut},Solved={res.Solved})");
            }
        }
        sw.Stop();
        sb.AppendLine("=== M2.2 逐关验收 ===");
        for (int d = 0; d < 3; d++)
        {
            if (count[d] == 0) continue;
            var name = ((WaterSortDifficulty)d).ToString();
            sb.AppendLine($"  [{name}] {count[d]} 关全过解码+复证可解,步数 {min[d]}~{max[d]}" +
                          $"(均值 {sum[d] / (double)count[d]:0.0});越窗 {windowFail[d]} 色数越档 {colorFail[d]}");
        }
        if (failures.Count == 0)
        {
            Debug.Log($"[WaterSortSetup] 验收通过: {pack.levels.Count} 关逐关解码 + 复证可解 + 档窗自洽,全绿" +
                      $"(复证耗时 {solveMsTotal}ms,验收总耗时 {sw.ElapsedMilliseconds}ms)\n" + sb.ToString().TrimEnd());
        }
        else
        {
            Debug.LogError($"[WaterSortSetup] 验收失败 {failures.Count} 项:\n{string.Join("\n", failures)}");
        }
    }

    // ---- ③ Addressables 组与条目(幂等;地址/组不符即自愈) ----

    static void EnsureGroup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[WaterSortSetup] Addressables 未初始化,跳过分组(先执行 Phase6AddressablesSetup.EnsureSetup)");
            return;
        }
        if (settings.FindGroup(GroupGameWaterSort) != null) return;
        var schemas = new System.Collections.Generic.List<AddressableAssetGroupSchema>(settings.DefaultGroup.Schemas);
        var group = settings.CreateGroup(GroupGameWaterSort, false, false, false, schemas);
        if (group == null)
            Debug.LogError("[WaterSortSetup] 分组创建失败: " + GroupGameWaterSort);
        else
            Debug.Log("[WaterSortSetup] 分组已创建: " + GroupGameWaterSort);
    }

    /// <summary>条目入库自愈:缺则建,组错/地址错则改(幂等)。</summary>
    internal static void EnsureEntry(string assetPath, string address)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings?.FindGroup(GroupGameWaterSort);
        if (group == null || !File.Exists(assetPath)) return;
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return;
        var entry = settings.FindAssetEntry(guid);
        if (entry == null)
            entry = settings.CreateOrMoveEntry(guid, group, false);
        if (entry == null) return;
        bool dirty = false;
        if (entry.parentGroup != group) { settings.MoveEntry(entry, group, false); dirty = true; }
        if (entry.address != address) { entry.address = address; dirty = true; }
        if (dirty) { EditorUtility.SetDirty(settings); }
    }

    static void RegisterPrefabEntry() => EnsureEntry(PrefabPath, PrefabAddress);

    static void RegisterLevelsEntry() => EnsureEntry(LevelsJsonPath, LevelsAddress);

    /// <summary>每日题库条目入库(M2.3;由 WaterSortDailyGenSetup 生成后调用,幂等自愈)。</summary>
    internal static void RegisterDailyLevelsEntry() => EnsureEntry(DailyLevelsPath, DailyLevelsAddress);

    // ---- 节点构建助手(与 Phase4/MoreGames 生成器同构) ----

    /// <summary>目录自愈(幂等;WaterSortDailyGenSetup 独立跑日题时同用,故 internal)。</summary>
    internal static void EnsureFolders()
    {
        EnsureFolder("Assets/Modules");
        EnsureFolder("Assets/Modules/WaterSort");
        EnsureFolder(PrefabDir);
        EnsureFolder(DataDir);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        if (string.IsNullOrEmpty(parent))
            AssetDatabase.CreateFolder("Assets", name);
        else
            AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>全屏节区面板:拉伸填满根(与 SafeAreaFitter 语义兼容,中心锚内容坐标不变)。</summary>
    static RectTransform NewPanel(Transform parent, string name)
    {
        var rt = NewNode(parent, name, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
        Stretch(rt);
        return rt;
    }

    /// <summary>中心锚节点(sizeDelta 定尺寸,pos 定位置;可选底色)。</summary>
    static RectTransform NewNode(Transform parent, string name, Vector2 pos, Vector2 size, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        if (bg.a > 0) { var img = go.AddComponent<Image>(); img.color = bg; img.raycastTarget = false; }
        return rt;
    }

    static RectTransform NewText(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        return rt;
    }

    /// <summary>主色按钮(Accent 蓝,同 MoreGames)。</summary>
    static RectTransform NewButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, bool active)
        => NewButton(parent, name, label, pos, size, active, Accent);

    static RectTransform NewButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, bool active, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = bg;
        // targetGraphic + ColorTint:SetInteractable(false) 时按钮底色变灰(结算「下一关」禁用态可见);
        // 按压手感由 BoxButton 自带缩放动画承担,ColorTint 仅跟随禁用态
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.transition = Selectable.Transition.ColorTint;
        var labelRt = NewText(go.transform, "Label", label, Vector2.zero, size, 40, false);
        labelRt.sizeDelta = new Vector2(size.x - 24, size.y - 16); // 文案留边,防溢出按钮底
        go.SetActive(active);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
