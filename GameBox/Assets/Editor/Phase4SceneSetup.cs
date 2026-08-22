using Box.Gameplay;
using Box.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 4 场景框架增量生成器(10 文档 §9):主菜单/对局/结算 prefab 增补节点 + 难度选择弹窗。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod Phase4SceneSetup.Build
/// 幂等:已有节点跳过创建(Ensure 模式),文案无条件刷新(改英文,工程无中文字体)。
/// </summary>
public static class Phase4SceneSetup
{
    const string PrefabsDir = "Assets/Resources/UI";
    const string PopupDir = "Assets/Resources/UI/Popups";

    [MenuItem("Box/Phase4/Build Scene Framework")]
    public static void Build()
    {
        RepairMissingScripts(); // Phase 3 遗留:多类文件导致 BoxText 等序列化为 fileID:0
        UpgradeMainMenu();
        UpgradeGameplay();
        UpgradeSettlement();
        CreateDifficultySelect();
        CreateExitConfirm();
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase4] scene framework upgraded: MainMenu/Gameplay/Settlement + DifficultySelect + ExitConfirm");
    }

    /// <summary>
    /// 修复 Phase 3 遗留:BoxControls.cs 单文件多类违反 Unity 一文件一类约定,
    /// 非首类(BoxText 等)的 MonoScript 引用在 prefab 序列化时丢失(m_Script fileID:0,加载为 missing)。
    /// 类已拆为单类文件;此处清除 prefab 全部 missing 组件引用,并按组件形态补回(幂等,无 missing 时零操作)。
    /// SaveAsPrefabAsset 覆盖保存同路径 → prefab GUID 不变 → 场景引用不丢。
    /// </summary>
    static void RepairMissingScripts()
    {
        string[] paths =
        {
            PrefabsDir + "/MainMenuView.prefab",
            PrefabsDir + "/GameplayView.prefab",
            PopupDir + "/SettlementPopup.prefab",
            PopupDir + "/DifficultySelect.prefab",
        };
        foreach (var path in paths)
        {
            var go = LoadInstance(path);
            if (go == null) continue; // 缺失(DifficultySelect 首次 Build 前)跳过

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            // 按组件形态补回:按钮补 BoxButton,文本节点补 BoxText(有则跳过,幂等)
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Button>() != null && t.GetComponent<BoxButton>() == null)
                    t.gameObject.AddComponent<BoxButton>();
                if (t.GetComponent<TextMeshProUGUI>() != null && t.GetComponent<BoxText>() == null)
                    t.gameObject.AddComponent<BoxText>();
            }

            SaveInstance(go, path);
            Debug.Log($"[Phase4] repaired missing scripts: {path}");
        }
    }

    // ---- 主菜单:中文文案(FontSetup 提供 MiSans 全字符集)+ 布局修复(原 Daily/Settings 重叠) ----

    static void UpgradeMainMenu()
    {
        var path = PrefabsDir + "/MainMenuView.prefab";
        var go = LoadInstance(path);
        if (go == null) { Debug.LogError("[Phase4] MainMenuView.prefab 缺失,先执行 Phase3"); return; }

        SetText(go, "Title", "数独游戏盒");
        SetText(go, "Hint", "选择难度 / 每日挑战");
        EnsureButton(go.transform, "DailyChallengeButton", "每日挑战", new Vector2(0, -40), new Vector2(420, 100));

        // 垂直布局:Title→Start→Daily→Settings→Hint,间距拉开(原 Daily(-60) 与 Settings(-100) 高度重叠)
        SetRect(go.transform.Find("StartButton"), new Vector2(0, 100), new Vector2(420, 110));
        SetRect(go.transform.Find("DailyChallengeButton"), new Vector2(0, -40), new Vector2(420, 100));
        SetRect(go.transform.Find("SettingsButton"), new Vector2(0, -170), new Vector2(420, 90));
        SetRect(go.transform.Find("Hint"), new Vector2(0, -340), new Vector2(900, 60));

        SaveInstance(go, path);
    }

    // ---- 对局:标题/时间/提示数 + 正方形棋盘 + 工具条 + 数字盘 ----

    static void UpgradeGameplay()
    {
        var path = PrefabsDir + "/GameplayView.prefab";
        var go = LoadInstance(path);
        if (go == null) { Debug.LogError("[Phase4] GameplayView.prefab 缺失,先执行 Phase3"); return; }

        EnsureText(go.transform, "TitleText", "数独", new Vector2(0, 850), new Vector2(600, 80), 56, true);
        EnsureText(go.transform, "HintCountText", "提示 0/3", new Vector2(420, 850), new Vector2(240, 50), 32);
        EnsureText(go.transform, "TimeText", "用时 00:00", new Vector2(0, 775), new Vector2(400, 50), 34);

        // 棋盘:中心锚正方形 900x900(原拉伸 anchor 0.1/0.2-0.9/0.9 是 864x1344 长方形,修复)
        var board = go.transform.Find("BoardPlaceholder");
        if (board != null)
        {
            var brt = board.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(900, 900);
            brt.anchoredPosition = new Vector2(0, -60);
        }

        // 返回键左上角(原居中顶部与标题冲突)
        SetRect(go.transform.Find("BackButton"), new Vector2(-420, 860), new Vector2(200, 80));

        // 工具条:模式/撤销/重做/擦除/提示(160x80,间距 20;棋盘底 -510 下方)
        string[] toolNames = { "ModeButton", "UndoButton", "RedoButton", "EraseButton", "HintButton" };
        string[] toolLabels = { "笔记", "撤销", "重做", "擦除", "提示" };
        for (int i = 0; i < toolNames.Length; i++)
        {
            float x = -440f + i * 180f;
            EnsureButton(go.transform, toolNames[i], toolLabels[i], new Vector2(x, -645), new Vector2(160, 80));
            SetRect(go.transform.Find(toolNames[i]), new Vector2(x, -645), new Vector2(160, 80));
        }

        // 数字盘:1-9(96x96,间距 14)
        for (int i = 1; i <= 9; i++)
        {
            float x = -440f + (i - 1) * 110f;
            EnsureButton(go.transform, "Num" + i, i.ToString(), new Vector2(x, -790), new Vector2(96, 96));
            SetRect(go.transform.Find("Num" + i), new Vector2(x, -790), new Vector2(96, 96));
        }

        SaveInstance(go, path);
    }

    // ---- 结算:星级/用时独立文本(中文文案,Confirm/Cancel 改 Next/Home 语义) ----

    static void UpgradeSettlement()
    {
        var path = PopupDir + "/SettlementPopup.prefab";
        var go = LoadInstance(path);
        if (go == null) { Debug.LogError("[Phase4] SettlementPopup.prefab 缺失,先执行 Phase3"); return; }

        SetText(go, "Title", "对局完成"); // 运行时按结果覆盖
        SetText(go, "Message", "用时 00:00 / 错误 0");
        EnsureText(go.transform, "StarsText", "星级 3/3", new Vector2(0, 140), new Vector2(400, 60), 40, true);
        EnsureText(go.transform, "TimeText", "用时 00:00", new Vector2(0, 75), new Vector2(400, 50), 32);
        SetText(go, "Confirm/Label", "再来一局");
        SetText(go, "Cancel/Label", "返回菜单");

        SaveInstance(go, path);
    }

    // ---- 难度选择弹窗(新建,中文文案) ----

    static void CreateDifficultySelect()
    {
        var path = PopupDir + "/DifficultySelect.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var root = new GameObject("DifficultySelect", typeof(RectTransform), typeof(DifficultySelectView), typeof(CanvasGroup));
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(720, 560);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.97f);

        CreateText(root.transform, "Title", "选择难度", new Vector2(0, 190), new Vector2(600, 80), 56, true);
        CreateButton(root.transform, "EasyButton", "简单", new Vector2(0, 50), new Vector2(420, 100));
        CreateButton(root.transform, "MediumButton", "中等", new Vector2(0, -80), new Vector2(420, 100));
        CreateButton(root.transform, "HardButton", "困难", new Vector2(0, -210), new Vector2(420, 100));

        SavePrefab(root, path);
    }

    // ---- 退出确认弹窗(新建,复用 BoxDialogView:Title/Message/Confirm/Cancel 约定) ----

    static void CreateExitConfirm()
    {
        var path = PopupDir + "/ExitConfirm.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var root = new GameObject("ExitConfirm", typeof(RectTransform), typeof(BoxDialogView), typeof(CanvasGroup));
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(640, 400);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.97f);

        CreateText(root.transform, "Title", "退出对局", new Vector2(0, 130), new Vector2(560, 80), 52, true);
        CreateText(root.transform, "Message", "当前进度将丢失,确定退出?", new Vector2(0, 20), new Vector2(560, 60), 34);
        // 语义色:Confirm 红(退出,危险操作) Cancel 蓝
        var confirm = CreateButton(root.transform, "Confirm", "退出", new Vector2(-90, -120), new Vector2(240, 90));
        confirm.GetComponent<Image>().color = new Color(0.75f, 0.30f, 0.28f);
        CreateButton(root.transform, "Cancel", "取消", new Vector2(90, -120), new Vector2(240, 90));

        SavePrefab(root, path);
    }

    // ---- helpers ----

    static GameObject LoadInstance(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return prefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    static void SaveInstance(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void SavePrefab(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void SetText(GameObject root, string childName, string text)
    {
        var t = root.transform.Find(childName)?.GetComponent<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    /// <summary>强制设置节点锚点位置与尺寸(幂等覆盖,修复既有节点布局)。</summary>
    static void SetRect(Transform t, Vector2 pos, Vector2 size)
    {
        if (t == null) return;
        var rt = t.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void EnsureText(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, bool bold = false)
    {
        if (parent.Find(name) != null) return;
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(BoxText));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (bold) tmp.fontStyle = FontStyles.Bold;
    }

    static GameObject EnsureButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        return CreateButton(parent, name, label, pos, size);
    }

    static GameObject CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.20f, 0.55f, 0.90f);
        CreateText(go.transform, "Label", label, Vector2.zero, size, 40);
        return go;
    }

    static GameObject CreateText(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, bool bold = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(BoxText));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        return go;
    }
}
