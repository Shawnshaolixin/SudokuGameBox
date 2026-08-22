using Box.Gameplay;
using Box.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 5 增量生成器:设置弹窗 prefab(5-2,偏好设置页)。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod Phase5SceneSetup.Build
/// 幂等:已存在则跳过。
/// prefab 布局约定(与 DifficultySelect/ExitConfirm 一致):根挂 SettingsView + CanvasGroup + Image,
/// 按钮节点下 "Label" 文本子节点文本运行时由 SettingsView 刷新。
/// </summary>
public static class Phase5SceneSetup
{
    const string PopupDir = "Assets/Resources/UI/Popups";

    [MenuItem("Box/Phase5/Build Settings Popup")]
    public static void Build()
    {
        CreateSettingsPopup();
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase5] scene setup upgraded: SettingsPopup created");
    }

    // ---- 设置弹窗(新建,中文文案) ----

    static void CreateSettingsPopup()
    {
        var path = PopupDir + "/SettingsPopup.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var root = new GameObject("SettingsPopup", typeof(RectTransform), typeof(SettingsView), typeof(CanvasGroup));
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(720, 620);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.97f); // 深色底(浅色主题由 SettingsView.Refresh 切换)

        CreateText(root.transform, "Title", "设置", new Vector2(0, 210), new Vector2(560, 80), 56, true);
        CreateButton(root.transform, "SoundButton", "音效:开", new Vector2(0, 80), new Vector2(500, 90));
        CreateButton(root.transform, "MusicButton", "音乐:开", new Vector2(0, -30), new Vector2(500, 90));
        CreateButton(root.transform, "ThemeButton", "主题:浅色", new Vector2(0, -140), new Vector2(500, 90));
        CreateButton(root.transform, "LangButton", "语言:中文", new Vector2(0, -250), new Vector2(500, 90));
        CreateButton(root.transform, "CloseButton", "完成", new Vector2(0, -390), new Vector2(420, 100));

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    // ---- helpers(与 Phase4SceneSetup 同构) ----

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