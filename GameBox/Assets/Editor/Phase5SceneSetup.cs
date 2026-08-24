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
    // Phase 6 已迁至 Assets/UI/Prefabs(保 GUID,Addressables 地址 "UI/Popups/SettingsPopup"),生成器同步新路径
    const string PopupDir = "Assets/UI/Prefabs/Popups";

    [MenuItem("Box/Phase5/Build Settings Popup")]
    public static void Build()
    {
        CreateSettingsPopup();
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase5] scene setup upgraded: SettingsPopup created");
    }

    // ---- 设置弹窗(新建,中文文案) ----
    // Phase 7 7-1 升级:布局重排(窗口加高)+ 新增 RemoveAdsButton(去广告购买)/PrivacyButton(隐私政策)。
    // ⚠️ 保 GUID:旧版升级用「加载实例 → 原地修改 → SaveAsPrefabAsset 覆盖」,绝不 DeleteAsset 重建
    // (换 GUID 会使 Addressables UI_Local 已注册条目失效,Phase 6 迁移的教训)。

    static void CreateSettingsPopup()
    {
        var path = PopupDir + "/SettingsPopup.prefab"; // Phase 6 后新路径:Assets/UI/Prefabs/Popups
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        GameObject root;
        if (existing != null)
        {
            if (existing.transform.Find("RemoveAdsButton") != null) return; // 已升级,幂等跳过
            root = (GameObject)PrefabUtility.InstantiatePrefab(existing);   // 旧版:实例化原地升级
        }
        else
        {
            root = new GameObject("SettingsPopup", typeof(RectTransform), typeof(SettingsView), typeof(CanvasGroup));
            var rt0 = root.GetComponent<RectTransform>();
            rt0.anchorMin = new Vector2(0.5f, 0.5f);
            rt0.anchorMax = new Vector2(0.5f, 0.5f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.10f, 0.97f); // 深色底(浅色主题由 SettingsView.Refresh 切换)
            CreateText(root.transform, "Title", "设置", new Vector2(0, 280), new Vector2(560, 80), 56, true);
        }

        // 统一布局(新建与升级共用):窗口加高 + 既有按钮移位 + 新增商业化按钮
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(720, 760);
        SetPosition(root, "Title", new Vector2(0, 280));
        SetPosition(root, "SoundButton", new Vector2(0, 150));
        SetPosition(root, "MusicButton", new Vector2(0, 40));
        SetPosition(root, "ThemeButton", new Vector2(0, -70));
        SetPosition(root, "LangButton", new Vector2(0, -180));
        if (root.transform.Find("RemoveAdsButton") == null)
        {
            // 商业化(7-1):去广告购买(橙色强调) + 隐私政策(灰色次级)
            var removeAds = CreateButton(root.transform, "RemoveAdsButton", "去广告", new Vector2(0, -290), new Vector2(500, 90));
            removeAds.GetComponent<Image>().color = new Color(0.90f, 0.62f, 0.18f);
            var privacy = CreateButton(root.transform, "PrivacyButton", "隐私政策", new Vector2(0, -400), new Vector2(500, 90));
            privacy.GetComponent<Image>().color = new Color(0.30f, 0.30f, 0.34f);
        }
        SetPosition(root, "CloseButton", new Vector2(0, -510));

        PrefabUtility.SaveAsPrefabAsset(root, path); // 覆盖保存:同路径同 GUID,Addressables 引用不断
        Object.DestroyImmediate(root);
    }

    /// <summary>设置节点 anchoredPosition(升级时移动既有按钮)。</summary>
    static void SetPosition(GameObject root, string childName, Vector2 pos)
    {
        var t = root.transform.Find(childName);
        if (t == null) return;
        t.GetComponent<RectTransform>().anchoredPosition = pos;
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