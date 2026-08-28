using Box.Gameplay;
using Box.HotUpdate.Sudoku;
using Box.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Phase 3-3 场景框架生成器(10 文档 §8):主菜单/对局/结算 prefab 骨架 + 场景 + build 注册。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod Phase3SceneSetup.Build
/// 幂等:已存在的资源跳过重建。功能接线在 Phase 4 填充。
/// </summary>
public static class Phase3SceneSetup
{
    const string ScenesDir = "Assets/Scenes";
    const string PrefabsDir = "Assets/Resources/UI";
    const string PopupPrefabsDir = "Assets/Resources/UI/Popups";

    [MenuItem("Box/Phase3/Build Scene Framework")]
    public static void Build()
    {
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "UI");
        EnsureFolder("Assets/Resources/UI", "Popups");

        CreateMainMenuPrefab();
        CreateGameplayPrefab();
        CreateSettlementPrefab();

        CreateScene("MainMenu", PrefabsDir + "/MainMenuView.prefab");
        CreateScene("Gameplay", PrefabsDir + "/GameplayView.prefab");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenesDir + "/MainMenu.unity", true),
            new EditorBuildSettingsScene(ScenesDir + "/Gameplay.unity", true),
        };

        AssetDatabase.SaveAssets();
        Debug.Log("[Phase3] scene framework built: 3 prefabs + MainMenu/Gameplay scenes registered");
    }

    // ---- prefab 骨架 ----

    static void CreateMainMenuPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/MainMenuView.prefab") != null) return;
        var root = new GameObject("MainMenuView", typeof(RectTransform), typeof(MainMenuView), typeof(CanvasGroup));
        StretchFull(root.GetComponent<RectTransform>());
        CreateText("Title", root.transform, "数独游戏盒", new Vector2(0, 260), new Vector2(800, 120), 72, true);
        CreateButton("StartButton", root.transform, "开始游戏", new Vector2(0, 40), new Vector2(420, 110));
        CreateButton("SettingsButton", root.transform, "设置", new Vector2(0, -100), new Vector2(420, 90));
        CreateText("Hint", root.transform, "难度选择 / 每日挑战(Phase 4)", new Vector2(0, -300), new Vector2(900, 60), 32);
        SavePrefab(root, PrefabsDir + "/MainMenuView.prefab");
    }

    static void CreateGameplayPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/GameplayView.prefab") != null) return;
        var root = new GameObject("GameplayView", typeof(RectTransform), typeof(GameplayView), typeof(CanvasGroup));
        StretchFull(root.GetComponent<RectTransform>());
        var board = new GameObject("BoardPlaceholder", typeof(RectTransform), typeof(Image));
        board.transform.SetParent(root.transform, false);
        var boardRt = board.GetComponent<RectTransform>();
        boardRt.anchorMin = new Vector2(0.1f, 0.2f);
        boardRt.anchorMax = new Vector2(0.9f, 0.9f);
        boardRt.offsetMin = Vector2.zero;
        boardRt.offsetMax = Vector2.zero;
        board.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f);
        CreateButton("BackButton", root.transform, "返回", new Vector2(-420, 780), new Vector2(200, 80));
        SavePrefab(root, PrefabsDir + "/GameplayView.prefab");
    }

    static void CreateSettlementPrefab()
    {
        var path = PopupPrefabsDir + "/SettlementPopup.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject("SettlementPopup", typeof(RectTransform), typeof(SettlementPopupView), typeof(CanvasGroup));
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(720, 520);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.97f);

        CreateText("Title", root.transform, "对局完成", new Vector2(0, 180), new Vector2(600, 80), 56, true);
        CreateText("Message", root.transform, "用时 00:00 / 错误 0", new Vector2(0, 60), new Vector2(600, 60), 36);
        CreateButton("Confirm", root.transform, "确定", new Vector2(0, -110), new Vector2(300, 90));
        CreateButton("Cancel", root.transform, "取消", new Vector2(0, -230), new Vector2(300, 80));
        PopupCardMigration.MigrateInstance(root); // 弹窗改造(2026-08):全屏遮罩根 + Card 浅色卡片
        SavePrefab(root, path);
    }

    // ---- 场景 ----

    static void CreateScene(string name, string prefabPath)
    {
        var scenePath = ScenesDir + "/" + name + ".unity";
        if (System.IO.File.Exists(scenePath)) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        cam.AddComponent<Camera>().orthographic = true;
        cam.AddComponent<AudioListener>();

        var canvasGo = new GameObject("Canvas_Scene", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(canvasGo.transform, false);
        }

        EditorSceneManager.SaveScene(scene, scenePath);
    }

    // ---- helpers ----

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static GameObject CreateText(string name, Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, bool bold = false)
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

    static GameObject CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.20f, 0.55f, 0.90f);
        CreateText("Label", go.transform, label, Vector2.zero, size, 40);
        return go;
    }

    static void SavePrefab(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void EnsureFolder(string parent, string name)
    {
        var full = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, name);
    }
}
