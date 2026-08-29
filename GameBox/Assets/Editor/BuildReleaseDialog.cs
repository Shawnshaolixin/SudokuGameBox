using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器一键 AAB 构建弹窗(2026-08-29 用户需求:不依赖 CLI/CI,编辑器内自己点构建)。
///
/// 解决两个 CLI 场景不存在的编辑器难题:
///   1. 签名密码:CLI 由环境变量注入,编辑器菜单无环境变量 → 从 Build/keystore/README.md
///      (gitignore 受控文件,唯一权威密码记录)解析预填,可修改,确认后注入进程环境变量。
///      密码不写代码/不写 git,符合 15 号文档 §4.3 约束。
///   2. 版本号递增(SKILL.md 规则):构建前弹窗确认 Play Console 最新 versionCode,
///      自动写入 Console 最新 + 1 到 ProjectSettings.asset,杜绝提测被拒。
///
/// 入口:Box/Build/Android AAB (release) 菜单(batchmode 下走 BuildScript 原逻辑,不弹窗)。
/// </summary>
public class BuildReleaseDialog : EditorWindow
{
    // 版本号与密码输入框持久字段(窗口关闭重开保留上次输入,少打字)
    static int _consoleMax;      // Play Console 上已存在的最新 versionCode(用户确认)
    static string _password;     // keystore 密码(预填 README 解析值,可改)
    static bool _building;       // 构建中禁用按钮,防重复触发

    /// <summary>菜单入口(非 batchmode 才弹窗;CLI executeMethod 走 BuildScript 原逻辑)。</summary>
    public static void ShowDialog()
    {
        // 预填:版本号建议 = 当前 + 1(用户核对 Console 后微调);密码从 README 解析
        if (_consoleMax == 0)
            _consoleMax = PlayerSettings.Android.bundleVersionCode; // 语义:Console 最新 ≈ 本地当前,弹窗内提示 +1
        if (string.IsNullOrEmpty(_password))
            _password = TryReadPasswordFromReadme();
        var win = GetWindow<BuildReleaseDialog>("Build AAB (release)");
        win.minSize = new Vector2(420, 210);
        win.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("AAB 发布构建(上传签名 + 版本号确认)", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        // 版本号:要求输入 Play Console 最新已发布 versionCode,构建时自动写 Console+1
        EditorGUILayout.HelpBox("Google Play 要求 versionCode 严格递增。填写 Console 上当前最大的 versionCode,构建将使用其 +1。", MessageType.Info);
        _consoleMax = EditorGUILayout.IntField("Play Console 最新 versionCode", _consoleMax);
        int nextCode = _consoleMax + 1;
        EditorGUILayout.LabelField("本次将构建为:", nextCode.ToString());
        EditorGUILayout.Space(8);

        // 签名密码(预填 README 解析值;构建结束即从进程环境变量清除)
        EditorGUILayout.LabelField("keystore 密码(README 预填,可修改)");
        _password = EditorGUILayout.PasswordField(_password);
        EditorGUILayout.Space(12);

        GUI.enabled = !_building && nextCode > 0 && !string.IsNullOrEmpty(_password);
        if (GUILayout.Button(_building ? "构建中…(请勿关闭编辑器)" : "开始构建 AAB", GUILayout.Height(32)))
            StartBuild(nextCode);
        GUI.enabled = true;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(_building ? "构建中:Addressables + IL2CPP + Gradle,约 20-40 分钟" : "", EditorStyles.miniLabel);
    }

    /// <summary>
    /// 注入密码环境变量 + 写入 versionCode 后触发构建。
    /// 构建内 BuildPipeline 主线程阻塞 → 窗口停更但进程正常,完事后日志见 SUCCESS 行。
    /// </summary>
    void StartBuild(int nextCode)
    {
        // 版本号写盘(ProjectSettings.asset,后续随 git 提交;构建快照生效)
        PlayerSettings.Android.bundleVersionCode = nextCode;
        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildReleaseDialog] versionCode 已写入 {nextCode}");

        // 密码注入进程环境变量(BuildScript.ApplyReleaseSigningIfAvailable 读取;构建后清除)
        System.Environment.SetEnvironmentVariable("BOX_KEYSTORE_PASS", _password);
        System.Environment.SetEnvironmentVariable("BOX_KEY_PASS", _password);

        _building = true;
        try
        {
            BuildScript.BuildAndroidAabCore(); // 复用既有构建管线(含签名应用/硬失败/清理;不调菜单入口防递归弹窗)
            Debug.Log("[BuildReleaseDialog] 构建流程结束,请查 Console 与产物 Build/Android/Rovilo.aab");
        }
        catch (System.Exception e)
        {
            // BuildPlayer 失败(Gradle 异常/签名失败等)会抛异常而非返回失败 report → 显式弹窗告知,别让用户干等
            Debug.LogError($"[BuildReleaseDialog] 构建失败: {e.GetType().Name}: {e.Message}");
            EditorUtility.DisplayDialog("AAB 构建失败", $"构建失败:\n{e.Message}\n\n详见 Console 日志。", "知道了");
        }
        finally
        {
            // 构建结束立即清除进程环境变量中的密码(防同进程其他逻辑读走)
            System.Environment.SetEnvironmentVariable("BOX_KEYSTORE_PASS", null);
            System.Environment.SetEnvironmentVariable("BOX_KEY_PASS", null);
            _building = false;
        }
    }

    /// <summary>从 Build/keystore/README.md 解析密码(反引号包裹字段;解析失败返回 null 由用户手输)。</summary>
    static string TryReadPasswordFromReadme()
    {
        var projectRoot = new DirectoryInfo(Application.dataPath).Parent!; // .../GameBox
        var readme = Path.Combine(projectRoot.Parent!.FullName, "Build", "keystore", "README.md");
        if (!File.Exists(readme)) return null;
        // 匹配"密钥库密码"行内的反引号值(README 唯一权威记录,被 gitignore)。
        // 注意:README 表格里也有反引号(如 `upload.keystore`),必须锚定密码行再取,否则取错值签名失败
        var m = Regex.Match(File.ReadAllText(readme), @"密钥密码[：:]\s*`([^`]+)`");
        return m.Success ? m.Groups[1].Value : null;
    }
}
