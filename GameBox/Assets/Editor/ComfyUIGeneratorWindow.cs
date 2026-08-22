using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// ComfyUI 本地生成器 — 在 Unity 编辑器里直接生成 AI UI 素材。
///
/// 菜单路径：Tools → AIGC → ComfyUI Generator
///
/// 功能：
///   • 选择模型（自动扫描 ComfyUI/models/checkpoints/）
///   • 输入提示词 / 负面词（带常用模板）
///   • 参数：尺寸、步数、CFG、种子
///   • 生成 → 预览 → 一键去背景并导入 Unity Art 目录
///
/// 依赖：本机 ComfyUI 已启动（d:/Projects/AI/ComfyUI/start_comfyui.bat）
/// </summary>
public class ComfyUIGeneratorWindow : EditorWindow
{
    // ============================================================
    // 路径配置
    // ============================================================

    // ComfyUI 根目录（默认与项目同级: d:/Projects/AI/ComfyUI）
    private static readonly string DefaultComfyDir = Path.Combine(
        Application.dataPath, "..", "..", "..", "ComfyUI"
    );

    // 输出目录:项目 tools/ai_output（与 Python 管线共用）
    private static readonly string AiOutputDir = Path.Combine(
        Application.dataPath, "..", "..", "tools", "ai_output"
    );

    private static readonly string ToolsDir = Path.Combine(
        Application.dataPath, "..", "..", "tools"
    );

    // 类型后缀 → sprite_pipeline 的类型映射
    private static readonly string[] TypeSuffixes = { "_btn", "_panel", "_icon", "_bg" };
    private static readonly string[] PipelineTypes = { "button", "panel", "icon", "bg" };

    // ============================================================
    // 持久化键
    // ============================================================

    private const string PServer = "AIGC.ComfyServer";
    private const string PDir = "AIGC.ComfyDir";
    private const string PModel = "AIGC.ComfyModel";
    private const string PPrompt = "AIGC.Prompt";
    private const string PNeg = "AIGC.Negative";
    private const string PWidth = "AIGC.Width";
    private const string PHeight = "AIGC.Height";
    private const string PSteps = "AIGC.Steps";
    private const string PCfg = "AIGC.Cfg";
    private const string PType = "AIGC.TypeIndex";

    // ============================================================
    // 字段
    // ============================================================

    private string _server = "http://127.0.0.1:8188";
    private string _comfyDir = "";
    private string _model = "";
    private string[] _models = new string[0];

    private string _prompt = "";
    private string _negative = "";
    private string _assetName = "play";
    private int _typeIndex = 0;
    private int _width = 1024, _height = 1024, _steps = 28;
    private float _cfg = 5.5f;
    private long _seed = -1;

    // 运行状态
    private bool _busy;
    private float _elapsed;
    private string _status = "";
    private bool _isError;
    private Texture2D _preview;
    private string _lastOutput;
    private string _lastOutputName;

    // 生成状态机（EditorApplication.update 驱动，全部主线程）
    private enum Stage { Idle, Submitting, Polling, Downloading }
    private Stage _stage = Stage.Idle;
    private UnityWebRequest _request;
    private string _promptId;
    private float _pollTimer;
    private string _outPath;
    private string _imageFilename;
    private string _imageSubfolder = "";
    private string _imageType = "output";
    private byte[] _imageBytes;

    // 后处理进程
    private System.Diagnostics.Process _pendingProcess;

    // ============================================================
    // 窗口入口
    // ============================================================

    [MenuItem("Tools/AIGC/ComfyUI Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<ComfyUIGeneratorWindow>("AI 素材生成器");
        window.minSize = new Vector2(420, 720);
        window.Show();
    }

    private void OnEnable()
    {
        LoadPrefs();
        RefreshModels();
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        AbortRequest();
    }

    // ============================================================
    // 模型管理
    // ============================================================

    private void RefreshModels()
    {
        string dir = Path.Combine(_comfyDir, "models", "checkpoints");
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, "*.safetensors");
            _models = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                _models[i] = Path.GetFileName(files[i]);
            Array.Sort(_models);
            if (string.IsNullOrEmpty(_model) || Array.IndexOf(_models, _model) < 0)
                _model = _models.Length > 0 ? _models[0] : "";
        }
        else
        {
            _models = new string[0];
        }
    }

    // ============================================================
    // 生成状态机（主线程 Tick 驱动，无后台线程）
    // ============================================================

    private void Tick()
    {
        if (!_busy)
        {
            CheckProcessDone();
            return;
        }

        switch (_stage)
        {
            case Stage.Submitting:
                if (_request != null && _request.isDone) HandleSubmitDone();
                break;

            case Stage.Polling:
                _elapsed += Time.deltaTime;
                _pollTimer -= Time.deltaTime;
                if (_request != null && _request.isDone)
                {
                    HandleHistoryDone();
                }
                else if (_pollTimer <= 0)
                {
                    StartHistoryRequest();
                }
                _status = $"生成中... 已等待 {_elapsed:0}s（首图约 1-2 分钟）";
                break;

            case Stage.Downloading:
                if (_request != null && _request.isDone) HandleDownloadDone();
                break;
        }

        if (_busy) Repaint();
    }

    /// <summary>提交 txt2img 工作流</summary>
    private void StartGeneration()
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(_model))
        {
            EditorUtility.DisplayDialog("没有模型", "请先刷新模型列表并选择一个模型。", "确定");
            return;
        }

        long seed = _seed >= 0 ? _seed : (long)(DateTime.UtcNow.Ticks % int.MaxValue);
        _outPath = Path.Combine(AiOutputDir, _assetName + TypeSuffixes[_typeIndex] + ".png");
        Directory.CreateDirectory(AiOutputDir);

        string body = "{\"prompt\":" + BuildWorkflow(seed) + ",\"client_id\":\"unity_editor\"}";
        var req = new UnityWebRequest(_server + "/prompt", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SendWebRequest();
        _request = req;

        _busy = true;
        _isError = false;
        _elapsed = 0;
        _stage = Stage.Submitting;
        _preview = null;
        _status = "连接 ComfyUI...";
    }

    private void HandleSubmitDone()
    {
        var req = _request;
        AbortRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            FinishWithError($"无法连接 ComfyUI（{req.error}）\n请确认已运行 start_comfyui.bat");
            return;
        }

        string json = req.downloadHandler.text;
        var m = Regex.Match(json, "\"prompt_id\"\\s*:\\s*\"([^\"]+)\"");
        if (m.Success)
        {
            _promptId = m.Groups[1].Value;
            _pollTimer = 0;
            _stage = Stage.Polling;
            StartHistoryRequest();
        }
        else
        {
            var err = Regex.Match(json, "\"error\"\\s*:\\s*\"([^\"]+)\"");
            FinishWithError("提交失败:" + (err.Success ? err.Groups[1].Value : json));
        }
    }

    /// <summary>每 2 秒轮询 /history/{prompt_id}</summary>
    private void StartHistoryRequest()
    {
        AbortRequest();
        var req = UnityWebRequest.Get($"{_server}/history/{_promptId}");
        req.SendWebRequest();
        _request = req;
    }

    private void HandleHistoryDone()
    {
        var req = _request;
        AbortRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            // 轮询请求失败,下个周期重试
            _pollTimer = 0;
            return;
        }

        string json = req.downloadHandler.text;

        // 出错检测
        if (json.Contains("\"status_str\":\"error\"") || json.Contains("\"status_str\": \"error\""))
        {
            FinishWithError("生成出错,请查看 ComfyUI 控制台日志");
            return;
        }

        // 完成检测:出现 outputs 里的 images + filename
        var m = Regex.Match(json, "\"filename\"\\s*:\\s*\"([^\"]+)\"");
        if (m.Success)
        {
            _imageFilename = m.Groups[1].Value;
            _imageSubfolder = "";
            var sf = Regex.Match(json, "\"subfolder\"\\s*:\\s*\"([^\"]+)\"");
            if (sf.Success) _imageSubfolder = sf.Groups[1].Value;
            var ty = Regex.Match(json, "\"type\"\\s*:\\s*\"([^\"]+)\"");
            if (ty.Success) _imageType = ty.Groups[1].Value;

            // 进入下载阶段
            AbortRequest();
            var dl = UnityWebRequest.Get(
                $"{_server}/view?filename={_imageFilename}&subfolder={_imageSubfolder}&type={_imageType}");
            dl.SendWebRequest();
            _request = dl;
            _stage = Stage.Downloading;
            _status = "图片已生成,下载中...";
        }
    }

    private void HandleDownloadDone()
    {
        var req = _request;
        AbortRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            FinishWithError($"下载失败:{req.error}");
            return;
        }
        _imageBytes = req.downloadHandler.data;
        FinishGeneration();
    }

    private void FinishGeneration()
    {
        try
        {
            File.WriteAllBytes(_outPath, _imageBytes);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(_imageBytes))
            {
                FinishWithError("图片解码失败");
                return;
            }
            _preview = tex;
            _lastOutput = _outPath;
            _lastOutputName = Path.GetFileName(_outPath);
            _status = $"完成:{_lastOutputName}（{_imageBytes.Length / 1024} KB，{_elapsed:0}s）";
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            FinishWithError($"保存失败:{e.Message}");
            return;
        }
        _busy = false;
        _stage = Stage.Idle;
    }

    private void FinishWithError(string msg)
    {
        _status = msg;
        _isError = true;
        _busy = false;
        _stage = Stage.Idle;
    }

    private void AbortRequest()
    {
        if (_request != null)
        {
            _request.Abort();
            _request.Dispose();
            _request = null;
        }
    }

    /// <summary>SDXL 标准工作流(JSON)</summary>
    private string BuildWorkflow(long seed)
    {
        string cfg = _cfg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return "{" +
            "\"3\":{\"class_type\":\"KSampler\",\"inputs\":{" +
            $"\"seed\":{seed},\"steps\":{_steps},\"cfg\":{cfg}," +
            "\"sampler_name\":\"euler\",\"scheduler\":\"normal\",\"denoise\":1.0," +
            "\"model\":[\"4\",0],\"positive\":[\"6\",0],\"negative\":[\"7\",0],\"latent_image\":[\"5\",0]}}," +
            "\"4\":{\"class_type\":\"CheckpointLoaderSimple\",\"inputs\":{\"ckpt_name\":\"" + EscapeJson(_model) + "\"}}," +
            $"\"5\":{{\"class_type\":\"EmptyLatentImage\",\"inputs\":{{\"width\":{_width},\"height\":{_height},\"batch_size\":1}}}}," +
            "\"6\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"" + EscapeJson(_prompt) + "\",\"clip\":[\"4\",1]}}," +
            "\"7\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"" + EscapeJson(_negative) + "\",\"clip\":[\"4\",1]}}," +
            "\"8\":{\"class_type\":\"VAEDecode\",\"inputs\":{\"samples\":[\"3\",0],\"vae\":[\"4\",2]}}," +
            "\"9\":{\"class_type\":\"SaveImage\",\"inputs\":{\"filename_prefix\":\"unity_" + _assetName + "\",\"images\":[\"8\",0]}}" +
            "}";
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    // ============================================================
    // 后处理:调 Python sprite_pipeline 去背景并导入
    // ============================================================

    private void RunPostProcess()
    {
        if (string.IsNullOrEmpty(_lastOutput)) return;

        string script = Path.Combine(ToolsDir, "sprite_pipeline.py");
        string venvPython = Path.Combine(Application.dataPath, "..", "..", ".venv", "Scripts", "python.exe");
        string python = File.Exists(venvPython) ? venvPython : "python";
        string args = $"\"{script}\" single \"{_lastOutput}\" --type {PipelineTypes[_typeIndex]} --name {_assetName}";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = python,
            Arguments = args,
            WorkingDirectory = ToolsDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            var proc = System.Diagnostics.Process.Start(psi);
            proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[sprite_pipeline] {e.Data}"); };
            proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[sprite_pipeline] {e.Data}"); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            _pendingProcess = proc;
            _status = "后处理中(去背景 → 导入 Art 目录)...";
        }
        catch (Exception ex)
        {
            _status = "后处理启动失败:" + ex.Message;
            _isError = true;
        }
    }

    /// <summary>主线程轮询后处理进程,结束后刷新 Asset</summary>
    private void CheckProcessDone()
    {
        if (_pendingProcess == null) return;
        if (_pendingProcess.HasExited)
        {
            _pendingProcess = null;
            _status = "已导入 Unity Art 目录(Asset 已刷新)";
            AssetDatabase.Refresh();
        }
    }

    // ============================================================
    // GUI
    // ============================================================

    private void OnGUI()
    {
        GUILayout.Space(8);
        var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        GUILayout.Label("🎨 AI 素材生成器(本地 ComfyUI)", titleStyle);
        GUILayout.Space(4);

        // ---- 连接设置 ----
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("连接设置", EditorStyles.boldLabel);
        _server = EditorGUILayout.TextField("ComfyUI 地址", _server);
        _comfyDir = EditorGUILayout.TextField("ComfyUI 目录", _comfyDir);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新模型列表", GUILayout.Width(120)))
        {
            RefreshModels();
            SavePrefs();
        }
        GUILayout.Label($"找到 {_models.Length} 个模型", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        if (_models.Length > 0)
        {
            int idx = Mathf.Max(0, Array.IndexOf(_models, _model));
            _model = _models[EditorGUILayout.Popup("模型", idx, _models)];
        }
        else
        {
            EditorGUILayout.HelpBox("未找到模型,请确认 ComfyUI 目录正确且有 .safetensors 文件", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        // ---- 提示词 ----
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("提示词", EditorStyles.boldLabel);
        _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.Height(64));
        GUILayout.Label("模板:", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("按钮"))
            _prompt = "casual mobile game UI button, rounded rectangle, glossy, soft gradient, game art, centered, clean, high quality";
        if (GUILayout.Button("图标"))
            _prompt = "casual game UI icon, simple flat vector style, cute, clean shape, centered, isolated on plain background, high quality";
        if (GUILayout.Button("面板"))
            _prompt = "casual game UI panel background, rounded corner frame, subtle texture, soft gradient, game art style, empty in center, high quality";
        if (GUILayout.Button("背景"))
            _prompt = "relaxing casual puzzle game background, soft pastel colors, smooth gradient, subtle decorative elements, no text, wide composition, high quality";
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4);
        EditorGUILayout.LabelField("负面提示词", EditorStyles.miniLabel);
        _negative = EditorGUILayout.TextArea(_negative, GUILayout.Height(40));
        EditorGUILayout.EndVertical();

        // ---- 生成参数 ----
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("生成参数", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _width = EditorGUILayout.IntField("宽度", _width);
        _height = EditorGUILayout.IntField("高度", _height);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        _steps = EditorGUILayout.IntField("步数", _steps);
        _cfg = EditorGUILayout.FloatField("CFG", _cfg);
        EditorGUILayout.EndHorizontal();
        _seed = EditorGUILayout.LongField("种子(-1 随机)", _seed);
        EditorGUILayout.BeginHorizontal();
        _assetName = EditorGUILayout.TextField("文件名", _assetName);
        _typeIndex = EditorGUILayout.Popup(_typeIndex, new[] { "_btn 按钮", "_panel 面板", "_icon 图标", "_bg 背景" });
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // ---- 生成按钮 ----
        GUILayout.Space(6);
        using (new EditorGUI.DisabledScope(_busy))
        {
            if (GUILayout.Button(_busy ? "生成中..." : "🚀 生成素材", GUILayout.Height(40)))
            {
                SavePrefs();
                StartGeneration();
            }
        }

        if (!string.IsNullOrEmpty(_status))
        {
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(_status, _isError ? MessageType.Error : MessageType.Info);
        }

        if (_busy)
        {
            float progress = _stage == Stage.Polling
                ? Mathf.PingPong(_elapsed % 20f, 10f) / 10f
                : 0.2f;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18), progress, _stage.ToString());
        }

        // ---- 预览 ----
        if (_preview != null)
        {
            GUILayout.Space(8);
            var rect = GUILayoutUtility.GetRect(300, 300, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, _preview, ScaleMode.ScaleToFit);
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 去背景并导入 Unity Art", GUILayout.Height(32)))
                RunPostProcess();
            if (GUILayout.Button("打开输出文件夹", GUILayout.Height(32)))
            {
                Directory.CreateDirectory(AiOutputDir);
                System.Diagnostics.Process.Start("explorer.exe", AiOutputDir);
            }
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.FlexibleSpace();
        var hint = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.gray }, wordWrap = true };
        GUILayout.Label("💡 生成文件保存到 tools/ai_output/,文件名带 _btn/_panel/_icon/_bg 后缀会自动触发 Unity 导入设置。", hint);
    }

    // ============================================================
    // 持久化
    // ============================================================

    private void LoadPrefs()
    {
        _server = EditorPrefs.GetString(PServer, "http://127.0.0.1:8188");
        _comfyDir = EditorPrefs.GetString(PDir, DefaultComfyDir);
        _model = EditorPrefs.GetString(PModel, "");
        _prompt = EditorPrefs.GetString(PPrompt, "");
        _negative = EditorPrefs.GetString(PNeg,
            "lowres, bad anatomy, bad hands, text, watermark, signature, blurry, jpeg artifacts, " +
            "cropped, out of frame, duplicate, error, deformed, asymmetric, lopsided, uneven, " +
            "crooked, distorted, malformed, low quality, worst quality, ugly, oversaturated");
        _width = EditorPrefs.GetInt(PWidth, 1024);
        _height = EditorPrefs.GetInt(PHeight, 1024);
        _steps = EditorPrefs.GetInt(PSteps, 28);
        _cfg = EditorPrefs.GetFloat(PCfg, 5.5f);
        _typeIndex = EditorPrefs.GetInt(PType, 0);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetString(PServer, _server);
        EditorPrefs.SetString(PDir, _comfyDir);
        EditorPrefs.SetString(PModel, _model);
        EditorPrefs.SetString(PPrompt, _prompt);
        EditorPrefs.SetString(PNeg, _negative);
        EditorPrefs.SetInt(PWidth, _width);
        EditorPrefs.SetInt(PHeight, _height);
        EditorPrefs.SetInt(PSteps, _steps);
        EditorPrefs.SetFloat(PCfg, _cfg);
        EditorPrefs.SetInt(PType, _typeIndex);
    }
}
