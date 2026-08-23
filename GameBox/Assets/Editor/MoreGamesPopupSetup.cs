using Box.Gameplay;
using Box.UI;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 大厅改造生成器(13 文档 §5 落地:More Games 弹窗动态化)。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod MoreGamesPopupSetup.Build
/// 幂等:已存在则跳过,可反复执行。
/// ① 新建 MoreGamesPopup.prefab(Assets/UI/Prefabs/Popups/,Phase6 迁移后的最终位置):
///    根挂 MoreGamesView + CanvasGroup + Image;Title + Content 容器 + 隐藏的 ItemTemplate
///    + CloseButton;ItemTemplate 由运行时克隆渲染列表(MoreGamesView.RenderItems)。
/// ② 升级 MainMenuView.prefab 布局:SettingsButton 移至右上角(参考分辨率 1080x1920,
///    中心锚),原 (0,-170) 位置新建 MoreGamesButton(420x90,"更多游戏")。
/// ③ 弹窗 prefab 入库 Addressables UI_Local 组(地址 UI/Popups/MoreGamesPopup,与
///    SettingsPopup 同组,首包内加载,标签 UI)。
/// </summary>
public static class MoreGamesPopupSetup
{
    const string PrefabsDir = "Assets/UI/Prefabs";
    const string PopupsDir = "Assets/UI/Prefabs/Popups";
    const string MainMenuPath = "Assets/UI/Prefabs/MainMenuView.prefab";
    const string MoreGamesAddress = "UI/Popups/MoreGamesPopup";
    const string GroupUiLocal = "UI_Local";

    [MenuItem("Box/Lobby/Build MoreGames Popup + Layout")]
    public static void Build()
    {
        CreateMoreGamesPopup();
        UpgradeMainMenu();
        AddMoreGamesToAddressables();
        AssetDatabase.SaveAssets();
        Debug.Log("[MoreGames] lobby upgraded: MoreGamesPopup created + MainMenu layout (settings top-right)");
    }

    // ---- ① 弹窗 prefab ----

    static void CreateMoreGamesPopup()
    {
        var path = PopupsDir + "/MoreGamesPopup.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var root = new GameObject("MoreGamesPopup", typeof(RectTransform), typeof(MoreGamesView), typeof(CanvasGroup));
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(720, 780);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.97f); // 深色底(与 SettingsPopup 一致)

        CreateText(root.transform, "Title", "更多游戏", new Vector2(0, 300), new Vector2(560, 80), 56, true);

        // Content 容器:列表项运行时挂载处(锚中心,与模板同轴)
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(root.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(0, -20);
        crt.sizeDelta = new Vector2(600, 540);

        // 隐藏的 ItemTemplate:运行时克隆(MoreGamesView 每项重设 anchoredPosition)
        CreateButton(content.transform, "ItemTemplate", "玩法", new Vector2(0, 0), new Vector2(520, 96), false);
        content.transform.Find("ItemTemplate").gameObject.SetActive(false);

        CreateButton(root.transform, "CloseButton", "完成", new Vector2(0, -380), new Vector2(420, 96), true);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    // ---- ② MainMenu 布局升级(Ensure 模式,幂等) ----

    static void UpgradeMainMenu()
    {
        var go = LoadInstance(MainMenuPath);
        if (go == null)
        {
            Debug.LogError("[MoreGames] MainMenuView.prefab 缺失: " + MainMenuPath);
            return;
        }

        // 设置按钮移至右上角:1080x1920 参考,中心锚半宽 540/半高 960 → 右上区域 (400, 850)
        var settings = go.transform.Find("SettingsButton");
        if (settings != null)
            SetRect(settings, new Vector2(400, 850), new Vector2(240, 88));

        // 原设置位置 (0,-170) 改放 More Games 按钮(缺则建,有则校准尺寸)
        var more = go.transform.Find("MoreGamesButton");
        if (more == null)
            more = CreateButton(go.transform, "MoreGamesButton", "更多游戏", new Vector2(0, -170), new Vector2(420, 90), true).transform;
        else
            SetRect(more, new Vector2(0, -170), new Vector2(420, 90));

        SaveInstance(go, MainMenuPath);
        Debug.Log("[MoreGames] MainMenu layout: settings top-right(400,850), more games at (0,-170)");
    }

    // ---- ③ Addressables 入库(UI_Local 组,标签 UI) ----

    static void AddMoreGamesToAddressables()
    {
        var path = PopupsDir + "/MoreGamesPopup.prefab";
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
        {
            Debug.LogWarning("[MoreGames] 弹窗 prefab 不存在,跳过入库: " + path);
            return;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[MoreGames] Addressables 未初始化,跳过入库(先执行 Phase6AddressablesSetup.EnsureSetup)");
            return;
        }
        var uiGroup = settings.FindGroup(GroupUiLocal);
        if (uiGroup == null)
        {
            Debug.LogWarning("[MoreGames] 分组 " + GroupUiLocal + " 不存在,跳过入库(先执行 Phase6AddressablesSetup.EnsureSetup)");
            return;
        }

        var guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) return;
        if (settings.FindAssetEntry(guid) != null) return; // 已入库

        var entry = settings.CreateOrMoveEntry(guid, uiGroup, false);
        if (entry != null)
        {
            entry.address = MoreGamesAddress;
            entry.labels.Add("UI");
            Debug.Log("[MoreGames] Addressables 入库: " + path + " -> " + MoreGamesAddress);
        }
        EditorUtility.SetDirty(settings);
    }

    // ---- helpers(与 Phase4/Phase5 同构) ----

    static GameObject LoadInstance(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return null;
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    static void SaveInstance(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void SetRect(Transform t, Vector2 pos, Vector2 size)
    {
        var rt = t as RectTransform;
        if (rt == null) return;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static GameObject CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, bool active)
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
        go.SetActive(active);
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