using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sprite 管线快捷菜单 — 在 Unity 菜单栏提供一键操作。
///
/// 菜单路径：Tools → Sprite Pipeline →
///   • Open Input Folder        — 打开放 AI 原始图的文件夹
///   • Open Art Folder          — 打开 Unity 资源输出目录
///   • Run Python Pipeline      — 一键调用 Python 脚本（交互式）
///   • Refresh All Art Assets   — 刷新所有 Art 资源的导入设置
/// </summary>
public class SpritePipelineMenu : EditorWindow
{
    // ============================================================
    // 路径配置
    // ============================================================

    // AI 原始图输入文件夹（你在 AI 工具下载图片后放到这里）
    private static readonly string AiInputDir = Path.Combine(
        Application.dataPath, "..", "..", "tools", "ai_input"
    );

    // Python 脚本路径
    private static readonly string PythonScriptPath = Path.Combine(
        Application.dataPath, "..", "..", "tools", "sprite_pipeline.py"
    );

    // ============================================================
    // 菜单项
    // ============================================================

    /// <summary>打开 AI 原始图输入目录</summary>
    [MenuItem("Tools/Sprite Pipeline/Open AI Input Folder")]
    public static void OpenAiInputFolder()
    {
        EnsureDirectoryExists(AiInputDir);
        Process.Start("explorer.exe", AiInputDir);
    }

    /// <summary>打开 Unity Art 输出目录</summary>
    [MenuItem("Tools/Sprite Pipeline/Open Art Folder")]
    public static void OpenArtFolder()
    {
        string artDir = Path.Combine(Application.dataPath, "Art");
        Process.Start("explorer.exe", artDir);
    }

    /// <summary>调用 Python 管线（交互式模式）</summary>
    [MenuItem("Tools/Sprite Pipeline/Run Python Pipeline (Interactive)")]
    public static void RunPythonPipelineInteractive()
    {
        RunPythonScript("interactive");
    }

    /// <summary>打开 Python 脚本所在目录</summary>
    [MenuItem("Tools/Sprite Pipeline/Open Tools Folder")]
    public static void OpenToolsFolder()
    {
        string toolsDir = Path.Combine(Application.dataPath, "..", "..", "tools");
        Process.Start("explorer.exe", toolsDir);
    }

    /// <summary>窗口：批量导入指引</summary>
    [MenuItem("Tools/Sprite Pipeline/Pipeline Guide Window")]
    public static void ShowWindow()
    {
        var window = GetWindow<SpritePipelineMenu>("AIGC 精灵管线");
        window.minSize = new Vector2(400, 520);
        window.Show();
    }

    // ============================================================
    // 辅助
    // ============================================================

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static void RunPythonScript(string args)
    {
        string scriptPath = Path.GetFullPath(PythonScriptPath);

        if (!File.Exists(scriptPath))
        {
            EditorUtility.DisplayDialog(
                "脚本未找到",
                $"找不到 Python 脚本:\n{scriptPath}\n\n请确认 tools/sprite_pipeline.py 存在。",
                "确定"
            );
            return;
        }

        // 打开终端运行 Python
        string cmd = $"python \"{scriptPath}\" {args}";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"{cmd}\"",
                WorkingDirectory = Path.GetDirectoryName(scriptPath),
                UseShellExecute = true,
            }
        };

        process.Start();
        EditorUtility.DisplayDialog(
            "Python 管线已启动",
            $"已在终端窗口中运行:\n{cmd}\n\n按终端提示操作即可。",
            "确定"
        );
    }

    // ============================================================
    // EditorWindow GUI（引导面板）
    // ============================================================

    private void OnGUI()
    {
        GUILayout.Space(12);

        // 标题
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter
        };
        GUILayout.Label("🎨 AIGC → Unity Sprite 半自动管线", titleStyle);

        GUILayout.Space(8);

        var subtitleStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };
        GUILayout.Label(
            "AI 生成图片 → Python 后处理 → Unity 自动导入\n三步出精灵，搞定就切回来用",
            subtitleStyle
        );

        GUILayout.Space(16);

        // 分隔线
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // === 步骤引导 ===
        GUILayout.Space(8);
        DrawStep(1, "AI 生成图片", "用 Midjourney / ComfyUI 生成原始图片，下载到本地");
        DrawStep(2, "Python 后处理", "去背景 → 裁切 → 缩放，输出到 Assets/Art/");
        DrawStep(3, "Unity 自动导入", "切回 Unity，自动检测新 PNG 并设 Sprite + 9-slice");

        GUILayout.Space(12);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(12);

        // 快捷按钮
        GUILayout.Label("📂 打开文件夹", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("AI 输入目录", GUILayout.Height(32)))
            OpenAiInputFolder();
        if (GUILayout.Button("Art 输出目录", GUILayout.Height(32)))
            OpenArtFolder();
        GUILayout.EndHorizontal();

        GUILayout.Space(12);

        GUILayout.Label("⚡ 快捷操作", EditorStyles.boldLabel);
        if (GUILayout.Button("▶ 运行 Python 管线（交互式）", GUILayout.Height(36)))
            RunPythonPipelineInteractive();
        if (GUILayout.Button("🔄 刷新所有 Art 导入设置", GUILayout.Height(36)))
            SpritePipelineImporter.RefreshAllArtAssets();

        GUILayout.Space(16);

        // 命名约定提示
        GUILayout.Label("📋 命名约定（自动识别）", EditorStyles.boldLabel);
        DrawConvention("_btn", "按钮 → 自动 9-slice");
        DrawConvention("_panel", "面板背景 → 自动 9-slice");
        DrawConvention("_icon", "图标 → 单张 Sprite, Pivot 居中");
        DrawConvention("_particle", "粒子贴图 → 无压缩, Clamp");
        DrawConvention("_bg", "背景 → 大尺寸, 压缩");

        GUILayout.Space(16);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        var hintStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 10,
            wordWrap = true,
            normal = { textColor = Color.gray }
        };
        GUILayout.Label(
            "💡 提示：AI 生成的图片放到 tools/ai_input/ 后，\n" +
            "点上面的\"运行 Python 管线\"即可一键处理。\n" +
            "处理完切回 Unity，图片已经自动配好了。",
            hintStyle
        );
    }

    private void DrawStep(int number, string title, string desc)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(16);

        // 步骤编号圆圈
        var circleStyle = new GUIStyle(GUI.skin.box)
        {
            fixedWidth = 32,
            fixedHeight = 32,
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
        };
        GUILayout.Box(number.ToString(), circleStyle, GUILayout.Width(32), GUILayout.Height(32));

        GUILayout.Space(8);

        GUILayout.BeginVertical();
        GUILayout.Label(title, EditorStyles.boldLabel);
        GUILayout.Label(desc, EditorStyles.wordWrappedMiniLabel);
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    private void DrawConvention(string suffix, string desc)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(16);
        var codeStyle = new GUIStyle(EditorStyles.label)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.4f, 0.7f, 1f) }
        };
        GUILayout.Label($"*{suffix}", codeStyle, GUILayout.Width(80));
        GUILayout.Label(desc, EditorStyles.label);
        GUILayout.EndHorizontal();
    }
}