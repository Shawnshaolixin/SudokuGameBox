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
///   • 布局控制（ControlNet）：给一张线框草图,生成结果严格遵循布局
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
    private const string PControlOn = "AIGC.ControlOn";
    private const string PControlSketch = "AIGC.ControlSketch";
    private const string PControlStrength = "AIGC.ControlStrength";
    private const string PLastOutput = "AIGC.LastOutput";
    private const string PExternalImage = "AIGC.ExternalImage";
    private const string PExternalSkipBg = "AIGC.ExternalSkipBg";
    private const string PExternalType = "AIGC.ExternalType";
    private const string PExternalSizeMode = "AIGC.ExternalSizeMode";
    private const string PExternalSizeW = "AIGC.ExternalSizeW";
    private const string PExternalSizeH = "AIGC.ExternalSizeH";

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

    // ControlNet 布局控制(草图 → 规整 UI)
    private bool _controlEnabled;
    private string _sketchPath = "";
    private float _controlStrength = 0.85f;

    // 运行状态
    private bool _busy;
    private float _elapsed;
    private string _status = "";
    private bool _isError;
    private Texture2D _preview;
    private string _lastOutput;
    private string _lastOutputName;

    // 外部图片导入（其他 AI 工具生成的图，不经本窗口生成）
    private string _externalImagePath = "";
    private bool _externalSkipBg;
    private int _externalTypeIndex = -1; // -1 = 未选过图；选图时按文件名后缀自动推断
    // 外部导入尺寸模式: 0=保持原始(不缩放,默认) 1=按类型预设 2=自定义 WxH
    private int _externalSizeMode;
    private int _externalSizeW = 256;
    private int _externalSizeH = 96;

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
        SavePrefs();
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

        // SDXL 尺寸下限保护: 低于 512 直接修正(否则生成模糊畸形图)
        _width = Mathf.Clamp(_width, 512, 1536);
        _height = Mathf.Clamp(_height, 512, 1536);
        if (_width < 768 || _height < 768)
        {
            if (!EditorUtility.DisplayDialog("分辨率过小",
                    $"当前 {_width}×{_height} 低于 SDXL 合理范围,生成结果会严重模糊畸形。\n\n修正为 1024×1024 再继续?", "修正并生成", "取消"))
                return;
            _width = 1024;
            _height = 1024;
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
        if (_request == null) return;
        var req = _request;
        // 先读数据再销毁: AbortRequest 会 Dispose 请求,之后访问 result/text 会抛 NRE
        bool ok = req.result == UnityWebRequest.Result.Success;
        string error = req.error;
        string json = ok ? req.downloadHandler.text : "";
        AbortRequest();
        if (!ok)
        {
            FinishWithError($"无法连接 ComfyUI（{error}）\n请确认已运行 start_comfyui.bat");
            return;
        }

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
        if (_request == null) return;
        var req = _request;
        // 先读数据再销毁(见 HandleSubmitDone 注释)
        bool ok = req.result == UnityWebRequest.Result.Success;
        string json = ok ? req.downloadHandler.text : "";
        AbortRequest();
        if (!ok)
        {
            // 轮询请求失败,下个周期重试
            _pollTimer = 0;
            return;
        }

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
        if (_request == null) return;
        var req = _request;
        // 先读数据再销毁(见 HandleSubmitDone 注释)
        bool ok = req.result == UnityWebRequest.Result.Success;
        string error = req.error;
        byte[] data = ok ? req.downloadHandler.data : null;
        AbortRequest();
        if (!ok)
        {
            FinishWithError($"下载失败:{error}");
            return;
        }
        _imageBytes = data;
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

    /// <summary>SDXL 工作流(JSON): 可选 ControlNet 布局控制</summary>
    private string BuildWorkflow(long seed)
    {
        string cfg = _cfg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        bool ctrl = _controlEnabled && !string.IsNullOrEmpty(_sketchPath);

        // KSampler 条件来源: 无 ControlNet 直连 CLIP;有则接 ControlNetApplyAdvanced 输出
        string positiveSrc = "[\"6\",0]", negativeSrc = "[\"7\",0]", extra = "";
        if (ctrl)
        {
            // 复制草图到 ComfyUI input 目录
            string inputDir = Path.Combine(_comfyDir, "input");
            Directory.CreateDirectory(inputDir);
            string sketchName = "sketch_" + Path.GetFileName(_sketchPath);
            File.Copy(_sketchPath, Path.Combine(inputDir, sketchName), true);

            string strength = _controlStrength.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            positiveSrc = "[\"12\",0]";
            negativeSrc = "[\"12\",1]";
            // 每个节点前带逗号: 9 号节点后不需要(由这里补充)
            extra =
                ",\"10\":{\"class_type\":\"ControlNetLoader\",\"inputs\":{\"control_net_name\":\"controlnet-canny-sdxl-1.0.fp16.safetensors\"}}" +
                ",\"11\":{\"class_type\":\"LoadImage\",\"inputs\":{\"image\":\"" + sketchName + "\"}}" +
                ",\"12\":{\"class_type\":\"ControlNetApplyAdvanced\",\"inputs\":{" +
                "\"positive\":[\"6\",0],\"negative\":[\"7\",0],\"control_net\":[\"10\",0],\"image\":[\"11\",0]," +
                "\"strength\":" + strength + ",\"start_percent\":0.0,\"end_percent\":1.0}}";
        }

        return "{" +
            "\"3\":{\"class_type\":\"KSampler\",\"inputs\":{" +
            $"\"seed\":{seed},\"steps\":{_steps},\"cfg\":{cfg}," +
            "\"sampler_name\":\"euler\",\"scheduler\":\"normal\",\"denoise\":1.0," +
            $"\"model\":[\"4\",0],\"positive\":{positiveSrc},\"negative\":{negativeSrc},\"latent_image\":[\"5\",0]}}" +
            "}," +
            "\"4\":{\"class_type\":\"CheckpointLoaderSimple\",\"inputs\":{\"ckpt_name\":\"" + EscapeJson(_model) + "\"}}," +
            $"\"5\":{{\"class_type\":\"EmptyLatentImage\",\"inputs\":{{\"width\":{_width},\"height\":{_height},\"batch_size\":1}}}}," +
            "\"6\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"" + EscapeJson(_prompt) + "\",\"clip\":[\"4\",1]}}," +
            "\"7\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"" + EscapeJson(_negative) + "\",\"clip\":[\"4\",1]}}," +
            "\"8\":{\"class_type\":\"VAEDecode\",\"inputs\":{\"samples\":[\"3\",0],\"vae\":[\"4\",2]}}," +
            "\"9\":{\"class_type\":\"SaveImage\",\"inputs\":{\"filename_prefix\":\"unity_" + _assetName + "\",\"images\":[\"8\",0]}}" +
            extra +
            "}";
    }

    /// <summary>按文件名后缀推断资源类型索引(与 SpritePipelineImporter 命名约定一致),推断不到返回当前生成区类型</summary>
    private int GuessTypeFromFileName(string path)
    {
        string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (n.Contains("_btn") || n.Contains("_button")) return 0;
        if (n.Contains("_panel")) return 1;
        if (n.Contains("_icon")) return 2;
        if (n.Contains("_bg") || n.Contains("_background")) return 3;
        return _typeIndex;
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    // ============================================================
    // 后处理:调 Python sprite_pipeline 去背景并导入
    // ============================================================

    /// <summary>
    /// 调 Python sprite_pipeline 去背景并导入 Art 目录。
    /// 参数：inputPath 源图路径；outputName 输出文件名(不含扩展名,保留类型后缀以触发 9-slice 等导入设置)；
    ///       typeIndex 资源类型索引；skipBg 已透明则跳过 rembg 抠图；
    ///       sizeMode 0=保持原始(跳过缩放) 1=按类型预设 2=自定义；customW/customH 自定义尺寸(sizeMode=2 时生效)。
    /// </summary>
    private void RunPostProcess(string inputPath, string outputName, int typeIndex, bool skipBg, int sizeMode, int customW, int customH)
    {
        if (string.IsNullOrEmpty(inputPath)) return;

        string script = Path.Combine(ToolsDir, "sprite_pipeline.py");
        string venvPython = Path.Combine(Application.dataPath, "..", "..", ".venv", "Scripts", "python.exe");
        string python = File.Exists(venvPython) ? venvPython : "python";
        string args = $"\"{script}\" single \"{inputPath}\" --type {PipelineTypes[typeIndex]} --name {outputName}";
        if (skipBg) args += " --skip-bg";
        // 尺寸控制: 保持原始 → 跳过缩放; 自定义 → 覆盖类型预设(--size)
        if (sizeMode == 0) args += " --skip-resize";
        else if (sizeMode == 2) args += $" --size {customW}x{customH}";

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

    /// <summary>主线程轮询后处理进程,结束后刷新 Asset;失败要看退出码,不能假装成功</summary>
    private void CheckProcessDone()
    {
        if (_pendingProcess == null) return;
        if (_pendingProcess.HasExited)
        {
            int code = _pendingProcess.ExitCode;
            _pendingProcess = null;
            if (code == 0)
            {
                _status = "已导入 Unity Art 目录(Asset 已刷新)";
                AssetDatabase.Refresh();
            }
            else
            {
                _status = $"后处理失败(退出码 {code}),详见 Console 的 [sprite_pipeline] 日志";
                _isError = true;
            }
        }
    }

    // ============================================================
    // GUI
    // ============================================================

    private Vector2 _scroll;
    private void OnGUI()
    {
        // 内容多(连接/提示词/参数/布局控制/预览),包滚动视图防止按钮被挤出可视区
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
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

        // ---- 外部图片导入（其他 AI 工具生成的图）----
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("📥 外部图片导入", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择图片", GUILayout.Width(96)))
        {
            string picked = EditorUtility.OpenFilePanel("选择 AI 生成的图片", "", "png,jpg,jpeg,webp,bmp");
            if (!string.IsNullOrEmpty(picked))
            {
                _externalImagePath = picked;
                // 按文件名后缀自动推断资源类型,推断不到用生成区当前类型
                _externalTypeIndex = GuessTypeFromFileName(picked);
            }
        }
        if (!string.IsNullOrEmpty(_externalImagePath))
            GUILayout.Label(Path.GetFileName(_externalImagePath), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_externalImagePath))
        {
            EditorGUILayout.BeginHorizontal();
            _externalTypeIndex = EditorGUILayout.Popup("类型", _externalTypeIndex, new[] { "_btn 按钮", "_panel 面板", "_icon 图标", "_bg 背景" });
            _externalSkipBg = EditorGUILayout.Toggle("已透明(跳过抠图)", _externalSkipBg);
            EditorGUILayout.EndHorizontal();
            // 尺寸模式: 默认保持原图尺寸(外部 AI 已定稿,不再强制缩放)
            _externalSizeMode = EditorGUILayout.Popup("尺寸", _externalSizeMode, new[] { "保持原始", "按类型预设", "自定义" });
            if (_externalSizeMode == 2)
            {
                EditorGUILayout.BeginHorizontal();
                _externalSizeW = EditorGUILayout.IntField("宽度", _externalSizeW);
                _externalSizeH = EditorGUILayout.IntField("高度", _externalSizeH);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("🔄 去背景并导入 Unity Art", GUILayout.Height(32)))
            {
                SavePrefs();
                RunPostProcess(_externalImagePath, Path.GetFileNameWithoutExtension(_externalImagePath),
                    _externalTypeIndex, _externalSkipBg, _externalSizeMode, _externalSizeW, _externalSizeH);
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "选择其他 AI 工具(即梦 / MJ / Liblib 等)生成的图片,一键去背景并导入 Unity Art 目录。\n" +
                "文件名带 _btn / _panel / _icon / _bg 后缀会自动配置 9-slice 等导入设置。",
                MessageType.Info);
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
        // SDXL 训练于 1024,过小会生成模糊畸形图;用滑块防呆
        EditorGUILayout.LabelField($"分辨率(SDXL 建议 1024,勿低于 768)", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        _width = Mathf.RoundToInt(EditorGUILayout.Slider("宽度", _width, 512, 1536));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        _height = Mathf.RoundToInt(EditorGUILayout.Slider("高度", _height, 512, 1536));
        EditorGUILayout.EndHorizontal();
        if (_width < 768 || _height < 768)
            EditorGUILayout.HelpBox("⚠ 低于 768 会严重失真(模糊/畸形),建议 1024×1024", MessageType.Warning);
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

        // ---- 布局控制(ControlNet)----
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        _controlEnabled = EditorGUILayout.Toggle("布局控制(草图)", _controlEnabled);
        if (_controlEnabled)
            GUILayout.Label("📐 按草图线框生成规整 UI", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        if (_controlEnabled)
        {
            EditorGUILayout.BeginHorizontal();
            _sketchPath = EditorGUILayout.TextField("草图文件", _sketchPath);
            if (GUILayout.Button("浏览", GUILayout.Width(56)))
            {
                string picked = EditorUtility.OpenFilePanel("选择布局草图", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(picked)) _sketchPath = picked;
            }
            EditorGUILayout.EndHorizontal();
            _controlStrength = EditorGUILayout.Slider("遵循力度", _controlStrength, 0.0f, 1.0f);
            EditorGUILayout.HelpBox(
                "草图上画圆角矩形/圆形线框,生成结果会严格遵循布局。力度 1.0 = 完全照草图。",
                MessageType.Info);
        }
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
        }

        // ---- 后处理操作:只要有已生成文件就显示按钮(不依赖预览图,窗口重开也有效)----
        if (_preview != null || !string.IsNullOrEmpty(_lastOutput))
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 去背景并导入 Unity Art", GUILayout.Height(32)))
                RunPostProcess(_lastOutput, _assetName, _typeIndex, false, 1, 0, 0); // 生成区保持按类型预设缩放(原行为)
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

        EditorGUILayout.EndScrollView();
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
        _controlEnabled = EditorPrefs.GetBool(PControlOn, false);
        _sketchPath = EditorPrefs.GetString(PControlSketch, "");
        _controlStrength = EditorPrefs.GetFloat(PControlStrength, 0.85f);
        // 上次生成的文件:窗口重开后仍显示"去背景并导入"按钮(文件还在才有效)
        _lastOutput = EditorPrefs.GetString(PLastOutput, "");
        if (!string.IsNullOrEmpty(_lastOutput) && !System.IO.File.Exists(_lastOutput))
            _lastOutput = "";
        // 外部图片路径同理:文件没了就清掉
        _externalImagePath = EditorPrefs.GetString(PExternalImage, "");
        if (!string.IsNullOrEmpty(_externalImagePath) && !System.IO.File.Exists(_externalImagePath))
            _externalImagePath = "";
        _externalSkipBg = EditorPrefs.GetBool(PExternalSkipBg, false);
        _externalTypeIndex = EditorPrefs.GetInt(PExternalType, -1);
        _externalSizeMode = EditorPrefs.GetInt(PExternalSizeMode, 0);
        _externalSizeW = EditorPrefs.GetInt(PExternalSizeW, 256);
        _externalSizeH = EditorPrefs.GetInt(PExternalSizeH, 96);
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
        EditorPrefs.SetBool(PControlOn, _controlEnabled);
        EditorPrefs.SetString(PControlSketch, _sketchPath);
        EditorPrefs.SetFloat(PControlStrength, _controlStrength);
        if (!string.IsNullOrEmpty(_lastOutput))
            EditorPrefs.SetString(PLastOutput, _lastOutput);
        if (!string.IsNullOrEmpty(_externalImagePath))
            EditorPrefs.SetString(PExternalImage, _externalImagePath);
        EditorPrefs.SetBool(PExternalSkipBg, _externalSkipBg);
        EditorPrefs.SetInt(PExternalType, _externalTypeIndex);
        EditorPrefs.SetInt(PExternalSizeMode, _externalSizeMode);
        EditorPrefs.SetInt(PExternalSizeW, _externalSizeW);
        EditorPrefs.SetInt(PExternalSizeH, _externalSizeH);
    }
}
