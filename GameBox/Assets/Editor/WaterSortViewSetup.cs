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
/// CLI 无头执行:unity run GameBox -- -executeMethod WaterSortViewSetup.Build(默认 12 关 demo 包)
/// 幂等:prefab/条目/关卡 JSON 已存在且参数一致 → 仅自愈(挂桥/地址校准),可反复执行。
///
/// ① 新建 Modules/WaterSort/Prefabs/WaterSortView.prefab —— 热更视图不进 prefab 序列化(20 文档 §4):
///    根挂 AOT HotViewBinder(viewTypeFullName = WaterSortView),运行期动态 AddComponent(UIView 由桥挂回)。
///    节点树严格按 WaterSortView 类头契约命名(标题/五列选关网格/对局三栏/结算列);
///    试管区与液块由运行期 WaterSortTubeRack 代码绘制,prefab 只提供容器(几何 1080x1920 中心锚布局)。
///    文本不预赋字体:工程 TMP Settings 默认 MiSans 动态字体(FontSetup 后所有新建 TMP 免赋)。
/// ② 关卡 JSON(Assets/Modules/WaterSort/Data/regular_levels.json):按关号 1..N 调 WaterSortGenDefaults.
///    SpecForIndex 取规格 + WaterSortLevelGen.Generate(固定种子)生成 → WaterSortLevelCodec.Encode 落盘,
///    与运行期加载结构 WaterSortLevelPack 同源(JsonUtility 反序列化直读)。
/// ③ 全部入新建组 Game_WaterSort(PRD WS-20 组名;prefab 地址 UI/WaterSortView = WaterSortModule.
///    MainViewAddress;JSON 地址 WaterSort/Levels/regular_levels.json = WaterSortLevelStore.LevelsAddress)。
/// ④ 幂等补建 M1.4 节点(底栏四钮布局/结算奖励行,几何校准重复执行零写入)+ 模块清单接入
///    (id=watersort → WaterSortModule,Phase45ModuleSetup.AddEntry 幂等,13 文档步骤 3)。
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

    const int DemoLevelCount = 12;   // M1.3 demo 题库(M1.5 以 Build100 整批重生成)
    const int SeedBase = 0x5757;     // 关号种子基(稳定复现;demo 包与最终题库同管线同种子域)

    // 占位配色(与运行时 WaterSortTubeRack 色板无关;文本/背景用,表现后置 AIGC 替换)
    static readonly Color Backdrop = new Color(0.05f, 0.08f, 0.11f, 1f); // 全屏深蓝灰(玩法底色)
    static readonly Color Accent = new Color(0.20f, 0.55f, 0.90f);       // 主按钮蓝(同 MoreGames)
    static readonly Color PanelTint = new Color(1f, 1f, 1f, 0.06f);      // 容器微亮底(选关网格/栏目)

    [MenuItem("Box/WaterSort/Build View Prefab + Levels(12 demo)")]
    public static void Build() => BuildInternal(DemoLevelCount);

    /// <summary>M1.5 全量入口:整批重生成 N 关(数量变化即覆盖重写,同种子可复现)。</summary>
    public static void BuildFull(int count) => BuildInternal(count);

    static void BuildInternal(int levelCount)
    {
        EnsureFolders();
        BuildPrefab();
        EnsureGroup();
        RegisterPrefabEntry();
        GenerateLevelsJson(levelCount);
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
        EnsureM14Nodes(); // M1.4:增量节点补建 + 几何校准(幂等,见下)
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

        // ---- 选关面板(全屏节区,下含标题/五列网格/回大厅) ----
        var select = NewPanel(root.transform, "SelectPanel");
        NewText(select, "Title", "", new Vector2(0, 830), new Vector2(700, 90), 60, true);

        var scroll = NewNode(select, "LevelScroll", new Vector2(0, -60), new Vector2(1000, 1460), PanelTint);
        scroll.gameObject.AddComponent<ScrollRect>().vertical = true; // 滚动体:见下字段接线
        // Viewport 拉伸填满滚动区;Mask 裁剪列表越界。需显式 Image(Mask 依赖 Graphic)——
        // 透明底 + raycastTarget=true:空白区拖拽也命中(事件沿层级冒泡到 ScrollRect)
        var viewport = NewNode(scroll, "Viewport", Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
        Stretch(viewport);
        var vImg = viewport.gameObject.AddComponent<Image>();
        vImg.color = Color.clear;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var content = NewNode(viewport, "Content", Vector2.zero, new Vector2(0, 0), new Color(0, 0, 0, 0));
        content.anchorMin = new Vector2(0, 1);  // 顶锚:运行时按行撑高,ScrollRect 纵滚
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        var sr = scroll.GetComponent<ScrollRect>();
        sr.viewport = viewport;
        sr.content = content;
        sr.movementType = ScrollRect.MovementType.Elastic;

        // 选关项模板:BoxButton + Label(TMP),只渲染可玩关(无锁定态);运行期克隆进 Content 排 5 列
        NewButton(select, "ItemTemplate", "1", new Vector2(0, 0), new Vector2(150, 150), false, PanelTint);
        NewButton(select, "HubButton", "", new Vector2(0, -800), new Vector2(400, 96), true);

        // ---- 对局面板(顶栏/步数/试管区/底栏;试管本体由运行期代码绘制) ----
        var game = NewPanel(root.transform, "GamePanel");
        var topBar = NewNode(game, "TopBar", new Vector2(0, 845), new Vector2(1080, 130), new Color(0, 0, 0, 0));
        NewButton(topBar, "BackButton", "", new Vector2(-400, 0), new Vector2(220, 88), true);
        NewText(topBar, "GameTitle", "", new Vector2(0, 0), new Vector2(640, 90), 44, true);
        NewText(topBar, "CoinLabel", "", new Vector2(395, 0), new Vector2(300, 80), 36, false);
        NewText(game, "StepText", "", new Vector2(0, 715), new Vector2(560, 84), 42, false);
        NewNode(game, "TubeArea", new Vector2(0, -60), new Vector2(1040, 1380), new Color(0, 0, 0, 0));
        var bottomBar = NewNode(game, "BottomBar", new Vector2(0, -845), new Vector2(1080, 130), new Color(0, 0, 0, 0));
        NewButton(bottomBar, "UndoButton", "", new Vector2(-150, 0), new Vector2(280, 96), true);
        NewButton(bottomBar, "RestartButton", "", new Vector2(150, 0), new Vector2(280, 96), true);

        // ---- 结算面板(标题/结果/三个动作) ----
        var settle = NewPanel(root.transform, "SettlePanel");
        NewText(settle, "Title", "", new Vector2(0, 330), new Vector2(700, 110), 64, true);
        NewText(settle, "ResultText", "", new Vector2(0, 120), new Vector2(820, 96), 56, false);
        NewButton(settle, "NextButton", "", new Vector2(0, -120), new Vector2(420, 104), true);
        NewButton(settle, "RetryButton", "", new Vector2(0, -300), new Vector2(420, 104), true);
        NewButton(settle, "HubButton", "", new Vector2(0, -480), new Vector2(420, 104), true);

        // 初始面板:选关可见(其余关闭,ShowPanel 由视图切换)
        select.gameObject.SetActive(true);
        game.gameObject.SetActive(false);
        settle.gameObject.SetActive(false);
        return root;
    }

    // ---- ①b M1.4 增量节点与清单接入(幂等:缺则建、几何漂移才校准;重复执行零写入) ----

    /// <summary>底栏按钮几何契约(M1.3 两钮 → M1.4 四钮;位置/宽度与 WaterSortView 类头契约同步)。</summary>
    struct BarButtonSpec
    {
        public string Name;
        public Vector2 Pos;
        public Vector2 Size;
    }

    static readonly BarButtonSpec[] BarButtons =
    {
        new BarButtonSpec { Name = "UndoButton",      Pos = new Vector2(-405, 0), Size = new Vector2(206, 96) },
        new BarButtonSpec { Name = "HintButton",      Pos = new Vector2(-135, 0), Size = new Vector2(206, 96) },
        new BarButtonSpec { Name = "ExtraTubeButton", Pos = new Vector2(135, 0),  Size = new Vector2(206, 96) },
        new BarButtonSpec { Name = "RestartButton",   Pos = new Vector2(405, 0),  Size = new Vector2(206, 96) },
    };

    /// <summary>
    /// M1.4 幂等补建:底栏「提示/空瓶」两钮 + 结算奖励行 RewardText;既有钮按几何契约校准
    /// (两钮 → 四钮布局一次性迁移,Label 随钮宽收缩;重复执行比对一致即零写盘)。
    /// 热更视图不序列化进 prefab,本区域只补纯 AOT 节点容器(与 BuildRootTree 同构),行为全在 WaterSortView。
    /// </summary>
    static void EnsureM14Nodes()
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
                        NewButton(bottom, b.Name, "", b.Pos, b.Size, true); // 与 M1.3 按钮同构(Accent + BoxButton + Label)
                        dirty = true;
                        continue;
                    }
                    // 已存在(M1.3 两钮 / 布局漂移):校准几何与 Label 留边
                    var rt = (RectTransform)t;
                    if (NotSame(rt.anchoredPosition, b.Pos)) { rt.anchoredPosition = b.Pos; dirty = true; }
                    if (NotSame(rt.sizeDelta, b.Size)) { rt.sizeDelta = b.Size; dirty = true; }
                    var label = t.Find("Label");
                    var labelSize = b.Size - new Vector2(24, 16); // NewButton 的文案留边口径
                    if (label != null && NotSame(((RectTransform)label).sizeDelta, labelSize))
                    {
                        ((RectTransform)label).sizeDelta = labelSize;
                        dirty = true;
                    }
                }
            }
            var settle = root.transform.Find("SettlePanel");
            if (settle != null && settle.Find("RewardText") == null)
            {
                // 奖励行夹在 ResultText 与 NextButton 间的留白带;运行时按首通 SetActive(见 WaterSortView.OnLevelSolved)
                NewText(settle, "RewardText", "", new Vector2(0, 2), new Vector2(760, 56), 44, false);
                dirty = true;
            }
            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[WaterSortSetup] M1.4 节点补建完成(底栏四钮 + 结算奖励行)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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
        // entryScene 为 v1.1 单场景化后废弃字段,传空串;大厅排序在数独(0)之后
        Phase45ModuleSetup.AddEntry(catalog, "watersort",
            "Box.HotUpdate.WaterSort.WaterSortModule", "", "水排序", 1);
    }

    // ---- ② 关卡 JSON(数量变化才重写;同种子确定性,重复执行内容一致) ----

    static void GenerateLevelsJson(int levelCount)
    {
        // 幂等判定:文件缺失或关数与目标不一致 → 重生成(数量 = 内容形态,见 M1.3/M1.5 交接)
        if (File.Exists(LevelsJsonPath) && LoadLevelsCount() == levelCount) return;

        var pack = new WaterSortLevelPack();
        for (int i = 1; i <= levelCount; i++)
        {
            var level = GenerateOne(i);
            if (level == null)
            {
                Debug.LogError($"[WaterSortSetup] 第 {i} 关生成失败(40 次重洗全不中档),终止");
                return;
            }
            pack.levels.Add(level);
        }
        // UTF-8 无 BOM(JsonUtility 原生字段名,与运行时反序列化一一对应)
        File.WriteAllText(LevelsJsonPath, JsonUtility.ToJson(pack, true));
        AssetDatabase.ImportAsset(LevelsJsonPath); // 生成为 TextAsset 资产(Addressables 可入库)
        Debug.Log($"[WaterSortSetup] 题库已{(File.Exists(LevelsJsonPath) ? "重" : "")}生成: {levelCount} 关 → {LevelsJsonPath}");
    }

    /// <summary>按关号生成一关:默认规格 + 固定种子;种子失败则就近平移重试(最多 49 次)。</summary>
    static WaterSortLevelData GenerateOne(int levelNo)
    {
        var spec = WaterSortGenDefaults.SpecForIndex(levelNo);
        for (int k = 0; k < 50; k++)
        {
            var r = WaterSortLevelGen.Generate(spec, SeedBase + levelNo * 7919 + k);
            if (!r.Succeeded) continue;
            return WaterSortLevelCodec.Encode(r.Board, levelNo, spec.Difficulty, r.MeasuredSteps);
        }
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
    static void EnsureEntry(string assetPath, string address)
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

    // ---- 节点构建助手(与 Phase4/MoreGames 生成器同构) ----

    static void EnsureFolders()
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
