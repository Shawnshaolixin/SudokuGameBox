using Box.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置弹窗反馈入口生成器(2026-09-10,落地 05 号文档 §5 检查清单
/// 「应用内评分引导与『联系支持』入口就绪」)。
/// ① 新增 RateButton(去评分)/ SupportButton(联系支持)两个 500x90 次级按钮;
/// ② 卡片重排:按钮由 5 个增至 7 个,卡片加高 784→1000,并整体上移 112 使 8 行内容在加高后重新居中。
/// 幂等:LoadPrefabContents → 缺则补建、全量校准坐标 → SaveAsPrefabAsset 覆盖
/// (绝不用 DeleteAsset 重建:换 GUID 会使 Addressables UI_Local 已注册条目失效,Phase 6 的教训)。
/// 末尾调用 PopupButtonSkin.ApplyAll 统一贴图与语义色。
/// CLI 无头:unity run GameBox -- -executeMethod SettingsFeedbackSetup.Build
///
/// ⚠️ 布局数值以 SettingsPopup.prefab 的**实际状态**为准:Phase5SceneSetup 里的坐标是弹窗改造前的
///    陈旧值,且该生成器对已升级的 prefab 直接早退,不要再回头改它——改这里。
/// </summary>
public static class SettingsFeedbackSetup
{
    const string PrefabPath = "Assets/UI/Prefabs/Popups/SettingsPopup.prefab";

    /// <summary>卡片尺寸:原 720x784,容纳两个新行(每行 110 间距)后加高至 720x1000。</summary>
    static readonly Vector2 CardSize = new Vector2(720, 1000);

    /// <summary>
    /// 卡片内各行纵向坐标。行距 110(与既有视觉节奏一致,标题后一行 140);
    /// 相对旧布局整体上移 112 = 新增两行(220)的一半,使内容块重新居中于加高后的卡片。
    /// </summary>
    static readonly (string Name, float Y)[] Layout =
    {
        ("Title",           405f),
        ("SoundButton",     265f),
        ("MusicButton",     155f),
        ("RemoveAdsButton",  45f),
        ("RateButton",      -65f),
        ("SupportButton",  -175f),
        ("PrivacyButton",  -285f),
        ("CloseButton",    -395f),
    };

    /// <summary>新增按钮尺寸(与既有行按钮一致)。</summary>
    static readonly Vector2 ButtonSize = new Vector2(500, 90);

    [MenuItem("Box/Phase8/3. Build Settings Feedback Buttons")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError("[SettingsFeedback] 缺失 prefab: " + PrefabPath);
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var card = root.transform.Find("Card");
        if (card == null)
        {
            // 弹窗改造(2026-08)后按钮统一在 Card 下;未迁移说明 prefab 结构不对
            Debug.LogError("[SettingsFeedback] 未找到 Card 节点,请先执行弹窗改造迁移");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        // ① 补建反馈入口(已存在则跳过,保节点引用不重建)
        EnsureButton(card, "RateButton", "Rate Us");
        EnsureButton(card, "SupportButton", "Support");

        // ② 重排:卡片加高 + 全量校准坐标(既有按钮一并上移,防历史 prefab 残留旧坐标)
        card.GetComponent<RectTransform>().sizeDelta = CardSize;
        foreach (var (name, y) in Layout)
        {
            var rt = card.Find(name) as RectTransform;
            if (rt == null)
            {
                Debug.LogWarning("[SettingsFeedback] 找不到行节点,已跳过: " + name);
                continue;
            }
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();

        // ③ 统一换肤:UISprite + 语义色 + Label 字色 + 英文兜底文案
        //    (RateButton/SupportButton 已登记进 PopupButtonSkin.SurfaceNames,否则会被判为"未识别按钮"而不换肤)
        PopupButtonSkin.ApplyAll();

        Debug.Log("[SettingsFeedback] 设置弹窗反馈入口就绪:RateButton + SupportButton,卡片 720x1000");
    }

    /// <summary>补建按钮(幂等:同名节点已存在时不重建,仅由调用方统一校准坐标)。</summary>
    static void EnsureButton(Transform card, string name, string label)
    {
        if (card.Find(name) != null) return;

        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxButton));
        go.transform.SetParent(card, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = ButtonSize;
        // 占位蓝:随紧随其后的 PopupButtonSkin.ApplyAll 覆盖为设计系统语义色(奶白次级)
        go.GetComponent<Image>().color = new Color(0.20f, 0.55f, 0.90f);
        CreateText(go.transform, "Label", label);
    }

    /// <summary>按钮文案子节点(约定名 "Label",与 Phase4/5 弹窗及 SettingsView.SetLabel 一致)。</summary>
    static void CreateText(Transform parent, string name, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(BoxText));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = ButtonSize;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; // 英文兜底:运行时由 SettingsView.Refresh 经 L10n 覆盖,防刷新前闪现中文
        tmp.fontSize = 40;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }
}
