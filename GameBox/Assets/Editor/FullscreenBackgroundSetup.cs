using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全屏背景独立节点落地工具(2026-08-29,用户反馈背景顶部让出刘海区)。
///
/// 问题根因:背景(贴图或纯色)挂在场景根视图(如 MainMenuView/GameplayView)根节点 Image 上,
/// 而根挂 SafeAreaFitter(UIView.Awake 统一挂载),运行帧把根锚点从全屏改写为安全区内缩矩形
/// → 背景跟着缩,顶部刘海/挖孔露黑。
///
/// 修复方案:背景独立成 Canvas 直接子节点 Background(全屏 stretch,不挂 SafeAreaFitter,
/// 永远铺满含刘海),根视图保持内缩(内容继续避让)。根 Image 清空外观(贴图摘除+透明)。
/// 外观首迁:根有贴图→贴图版(白底),纯色→纯色版(拷贝颜色)。
///
/// 幂等:Background 已存在则仅清理根覆盖(贴图摘除+透明),可反复执行。
/// 由 CLI 无头执行:unity run GameBox -- -executeMethod FullscreenBackgroundSetup.Run
/// </summary>
public static class FullscreenBackgroundSetup
{
    /// <summary>待处理的场景与根视图名(后续新增全屏场景在此登记)。</summary>
    static readonly (string Scene, string View)[] Targets =
    {
        ("Assets/Scenes/MainMenu.unity", "MainMenuView"),
        ("Assets/Scenes/Gameplay.unity", "GameplayView"),
    };

    [MenuItem("Box/UI/Setup Fullscreen Background")]
    public static void Run()
    {
        foreach (var t in Targets)
        {
            try { SetupScene(t.Scene, t.View); }
            catch (System.Exception e) { Debug.LogError($"[FullscreenBg] {t.Scene} 处理失败: {e.Message}"); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[FullscreenBg] 完成: {Targets.Length} 个场景全屏背景就位");
    }

    static void SetupScene(string scenePath, string viewName)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[FullscreenBg] 打开场景失败: " + scenePath);
            return;
        }

        // Canvas_Scene 根(场景根顺序不保证,EventSystem 等对象可能排首位,须遍历查找)
        GameObject canvas = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Canvas_Scene") { canvas = root; break; }
        if (canvas == null)
        {
            Debug.LogError("[FullscreenBg] 未找到 Canvas_Scene 根: " + scenePath);
            return;
        }
        var view = canvas.transform.Find(viewName);
        if (view == null)
        {
            Debug.LogError($"[FullscreenBg] 未找到 {viewName} 实例,场景结构异常: {scenePath}");
            return;
        }
        var viewImg = view.GetComponent<Image>();

        // 建/复用 Background(幂等):插到视图之前,渲染序在最底层
        var bg = canvas.transform.Find("Background");
        bool created = bg == null;
        if (created)
        {
            var go = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg = go.transform;
            bg.SetParent(canvas.transform, false);
            bg.SetSiblingIndex(0);
            var brt = bg.GetComponent<RectTransform>();
            // 全屏 stretch:不受 SafeAreaFitter 影响(该组件只作用于挂载节点,此处不挂)
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().raycastTarget = false; // 纯背景,不拦截点击(事件派发自上而下,也无影响)
            Debug.Log($"[FullscreenBg] {scenePath}: 新建 Background 全屏节点");
        }
        var bgImg = bg.GetComponent<Image>();
        if (bgImg == null)
        {
            Debug.LogError("[FullscreenBg] Background 缺 Image 组件,跳过: " + scenePath);
            return;
        }

        // 外观首迁:根有贴图→贴图版(白底),纯色→纯色版(拷贝颜色)
        if (created && viewImg != null)
        {
            if (viewImg.sprite != null)
            {
                bgImg.sprite = viewImg.sprite;
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.color = viewImg.color;
            }
        }

        // 根 Image 清空外观:摘贴图 + 全透明(背景由 Background 全屏呈现)
        if (viewImg != null)
        {
            viewImg.sprite = null;
            viewImg.color = new Color(0f, 0f, 0f, 0f);
            // ⚠️ 实测:直接置 null 时 m_Sprite 覆盖条目的 objectReference 不被清理(序列化保留旧引用),
            // 运行时根 Image 仍叠画缩进背景。用 PrefabUtility 显式移除 m_Sprite 覆盖 → 回落到 prefab 值(fileID:0)
            var mods = PrefabUtility.GetPropertyModifications(viewImg);
            if (mods != null)
            {
                var kept = System.Array.FindAll(mods, m => m.propertyPath != "m_Sprite");
                if (kept.Length != mods.Length)
                    PrefabUtility.SetPropertyModifications(viewImg, kept);
            }
        }
        else
        {
            Debug.LogWarning("[FullscreenBg] 根视图无 Image,仅确认 Background 结构: " + scenePath);
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[FullscreenBg] {scenePath}: Background 就位, {viewName} 根外观已清空");
    }
}
