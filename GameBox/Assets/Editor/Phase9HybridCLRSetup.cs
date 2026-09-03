using System;
using System.Collections.Generic;
using System.IO;
using Box.Gameplay.HotUpdate;
using Box.ModuleFramework;
using HybridCLR.Editor.Settings; // HybridCLRSettings
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Phase 9 9-1:HybridCLR 设置资产管理(10 文档 §16 9-1)+ 9-4 远程内容管线(§16.5)。
///
/// D-2 双模式构建开关 = ProjectSettings/HybridCLRSettings.asset 的 enable 字段:
///  v1.0(纯 AOT 自包含) = enable=false —— FilterHotFixAssemblies 不介入、原版 il2cpp;
///  v1.1(热更主线)     = enable=true  —— 过滤热更程序集 + CheckSettings 把
///                                       UNITY_IL2CPP_PATH 指向 hybridclr 运行时。
/// 包默认 enable=true(FilterHotFixAssemblies 会自动过滤名单内程序集),
/// 首次创建资产必须显式置 false 并入库 —— 漏置会把 v1.0 构建链带偏(热更程序集消失)。
///
/// 9-4 内容管线(§16.5):EnsureRemoteSetup 开启远程 catalog + RemoteHostURL 开发值 +
/// HotUpdate_Local 组;GenerateContent 从 HybridCLRData 白名单拷贝 dll/metadata
/// 到 Assets/RemoteContent(生成物,gitignore)并注册地址 + 生成 module_overrides 模板。
/// 更新流程:改代码 → GenerateAll → GenerateContent → BuildPlayerContent → deploy。
/// </summary>
public static class Phase9HybridCLRSetup
{
    /// <summary>
    /// 热更程序集名单(无 .dll 后缀,与热更 asmdef 名字一一对应)。
    /// 9-2 起含 Core 基座(内容最小化)+ Sudoku 玩法。
    /// </summary>
    public static readonly string[] HotUpdateAssemblies = { "Box.HotUpdate.Core", "Box.HotUpdate.Sudoku" };

    /// <summary>热更远程组(9-4:dll/metadata/overrides 组,Local + 可变更)。</summary>
    public const string GroupHotUpdateLocal = "HotUpdate_Local";

    /// <summary>热更内置兜底组(2026-09-03 断网空降级缺陷修复):dll/metadata 副本随主包,
    /// 断网/远程不可达时装载兜底 —— 真"包内版本"(v1.1 剥离后远程失效不再是裸奔)。</summary>
    public const string GroupHotUpdateBuiltin = "HotUpdate_Builtin";

    /// <summary>生成内容根目录(不入库,红线 9:仓库无远程内容)。</summary>
    public const string RemoteContentRoot = "Assets/RemoteContent";

    /// <summary>内置兜底内容根目录(不入库;GenerateAll 产物副本,随包构建,打包前由 BuildV11 内部 GenerateContent 刷新)。</summary>
    public const string BuiltinContentRoot = "Assets/BuiltinHotUpdate";

    /// <summary>module_overrides 模板版本(与 HotUpdateVersion.CodeVersion 约定一致;Box.HotUpdate.Core 是热更程序集,Editor 侧不可引用,故常量同步)。</summary>
    public const string OverridesTemplateVersion = "1.1.0";

    /// <summary>v1.0 语义:enable=false + 名单(Filter 不介入,热更程序集照常编译进主包)。</summary>
    public static void SetupV10()
    {
        var s = HybridCLRSettings.Instance;
        s.enable = false;
        s.hotUpdateAssemblies = HotUpdateAssemblies;
        HybridCLRSettings.Save();
        UnityEngine.Debug.Log($"[Phase9Setup] HybridCLRSettings.asset: enable=false, " +
                              $"hotUpdateAssemblies=[{string.Join(", ", HotUpdateAssemblies)}]");
    }

    /// <summary>v1.1 语义:enable=true + 名单(GenerateAll 与热更构建的前置条件)。</summary>
    public static void SetV11()
    {
        var s = HybridCLRSettings.Instance;
        s.enable = true;
        s.hotUpdateAssemblies = HotUpdateAssemblies;
        HybridCLRSettings.Save();
        UnityEngine.Debug.Log($"[Phase9Setup] enable=true, " +
                              $"hotUpdateAssemblies=[{string.Join(", ", HotUpdateAssemblies)}]");
    }

    /// <summary>
    /// 9-4 远程 catalog 配置(幂等):BuildRemoteCatalog 开关 + Profile RemoteHostURL 开发值 +
    /// Remote.BuildPath/LoadPath + HotUpdate_Local 组。
    /// 红线 9:上架前 RemoteHostURL 必须换生产地址,仓库只允许开发值。
    /// </summary>
    [MenuItem("Box/Phase9/4.1 Ensure Remote Setup")]
    public static void EnsureRemoteSetup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Phase9Setup] Addressables 未初始化,请先执行 Phase6AddressablesSetup.EnsureSetup");
            return;
        }

        // ① 远程 catalog 开关(构建时额外产出远程 catalog)
        settings.BuildRemoteCatalog = true;

        // ② Profile:RemoteHostURL 开发值(不存在则建,存在则校正)
        var profile = settings.profileSettings;
        var profileId = settings.activeProfileId;
        var host = profile.GetProfileDataByName("RemoteHostURL");
        if (host == null)
        {
            profile.CreateValue("RemoteHostURL", "http://127.0.0.1:8000");
            Debug.Log("[Phase9Setup] Profile 新增变量 RemoteHostURL = http://127.0.0.1:8000 (开发值,红线 9:上架前替换生产地址)");
        }
        else
        {
            profile.SetValue(profileId, host.Id, "http://127.0.0.1:8000");
        }

        // ③ 远程构建/加载路径(相对 AddressableAssetsData;[BuildTarget] 构建时自动替换为当前目标)
        SetProfileValue(settings, "Remote.BuildPath", "ServerData/[BuildTarget]");
        SetProfileValue(settings, "Remote.LoadPath", "{RemoteHostURL}/[BuildTarget]");

        // ③b 主 settings 的远程 catalog 路径:Phase 6 建 settings 时未填充(不开远程 catalog 无感),
        //     开 BuildRemoteCatalog 后 CreateRemoteCatalog 解析空引用直接失败,必须显式指向 profile 变量
        settings.RemoteCatalogBuildPath.SetVariableByName(settings, "Remote.BuildPath");
        settings.RemoteCatalogLoadPath.SetVariableByName(settings, "Remote.LoadPath");

        // ④ 热更远程组(本地组 + 可变更标记,Content Update 增量构建对象)
        EnsureHotUpdateGroup(settings);

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase9Setup] 远程 catalog 就绪:BuildRemoteCatalog=true, " +
                  "Remote.BuildPath=ServerData/[BuildTarget], Remote.LoadPath={RemoteHostURL}/[BuildTarget]");
    }

    /// <summary>
    /// 9-4 热更内容生成:从 HybridCLRData 白名单拷贝 dll/metadata 到 RemoteContent 并注册地址,
    /// 再从 ModuleCatalog.asset 生成 module_overrides 模板(远程模块清单)。
    /// 2026-09-03(断网空降级修复):同批产物再拷一份到 BuiltinHotUpdate 本地组(HotUpdate_Builtin,
    /// 随主包内置的兜底副本 —— v1.1 打包必须在本方法之后(BuildV11 内部已插入调用),保证副本与包内 AOT 同批)。
    /// 前置:已执行 HybridCLR GenerateAll(产物在 HybridCLRData/,gitignore 不入库)。
    /// 只拷名单内文件(2 热更 dll + 5 AOT metadata),绝不整目录拷贝。
    /// </summary>
    [MenuItem("Box/Phase9/4.2 Generate Content")]
    public static void GenerateContent()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Phase9Setup] Addressables 未初始化,请先执行 EnsureRemoteSetup");
            return;
        }
        var remoteGroup = settings.FindGroup(GroupHotUpdateLocal);
        if (remoteGroup == null)
        {
            Debug.LogError($"[Phase9Setup] 组 {GroupHotUpdateLocal} 不存在,请先执行 EnsureRemoteSetup");
            return;
        }
        var builtinGroup = EnsureBuiltinGroup(settings);

        // ① 准备目标目录(Dll / Metadata 分置,避免同名 dll 冲突;.dll.bytes 后缀导入为 TextAsset)
        EnsureFolder(RemoteContentRoot + "/Dll");
        EnsureFolder(RemoteContentRoot + "/Metadata");
        EnsureFolder(BuiltinContentRoot + "/Dll");
        EnsureFolder(BuiltinContentRoot + "/Metadata");

        // ② dll/metadata 双份:RemoteContent(远程组,可变更)+ BuiltinHotUpdate(内置兜底组,随包)。
        //    注意 Builtin 目录只覆盖不清空(首次生成的 .meta 保留 → GUID 稳定 → 组 entry 不漂移)
        int copied = 0;
        copied += CopyAssemblyBatch(settings, remoteGroup, $"{RemoteContentRoot}/Dll", "HotUpdate/Dll/", metadata: false);
        copied += CopyAssemblyBatch(settings, remoteGroup, $"{RemoteContentRoot}/Metadata", "HotUpdate/Metadata/", metadata: true);
        copied += CopyAssemblyBatch(settings, builtinGroup, $"{BuiltinContentRoot}/Dll", "BuiltinHotUpdate/Dll/", metadata: false);
        copied += CopyAssemblyBatch(settings, builtinGroup, $"{BuiltinContentRoot}/Metadata", "BuiltinHotUpdate/Metadata/", metadata: true);

        // ③ module_overrides 模板:从 ModuleCatalog.asset 序列化(与 ModuleOverrides 字段一一对应)。
        //    只走远程(内置兜底清单 = 包内 Resources ModuleCatalog,无需副本)
        var catalog = AssetDatabase.LoadAssetAtPath<ModuleCatalog>("Assets/Resources/Config/ModuleCatalog.asset");
        if (catalog == null)
        {
            Debug.LogError("[Phase9Setup] ModuleCatalog.asset 缺失(先执行 Phase45ModuleSetup)");
            return;
        }
        var overrides = new ModuleOverrides { version = OverridesTemplateVersion, entries = catalog.entries };
        var overridesPath = $"{RemoteContentRoot}/module_overrides.json";
        File.WriteAllText(overridesPath, JsonUtility.ToJson(overrides));
        AssetDatabase.ImportAsset(overridesPath, ImportAssetOptions.ForceUpdate);
        RegisterAsset(settings, remoteGroup, overridesPath, "HotUpdate/module_overrides"); // 已写盘,无需再拷贝
        copied++;

        AssetDatabase.SaveAssets();
        Debug.Log($"[Phase9Setup] GenerateContent 完成:拷贝/写入 {copied} 个" +
                  $"(dll×{HotUpdateAssemblies.Length}×2源 + metadata×{HotUpdateService.AotMetadataAssemblies.Count}×2源 + overrides)");
    }

    /// <summary>
    /// 拷一批 dll/metadata 到指定组与目录(调用方显式给出目标根目录与地址前缀,远程组/内置组各调两批)。
    /// </summary>
    static int CopyAssemblyBatch(AddressableAssetSettings settings, AddressableAssetGroup group,
        string dstRoot, string addressPrefix, bool metadata)
    {
        var buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        var srcRoot = metadata
            ? Path.Combine("HybridCLRData", "AssembliesPostIl2CppStrip", buildTarget)
            : Path.Combine("HybridCLRData", "HotUpdateDlls", buildTarget);
        // 两分支统一 IReadOnlyList 遍历(避免引入 System.Linq 只为 ToArray)
        IReadOnlyList<string> names = metadata ? HotUpdateService.AotMetadataAssemblies : HotUpdateAssemblies;
        int copied = 0;
        foreach (var asm in names)
        {
            var src = Path.Combine(srcRoot, asm + ".dll");
            if (!File.Exists(src))
            {
                Debug.LogError($"[Phase9Setup] 缺少 {(metadata ? "AOT 剥离" : "热更")} dll: {src} —— 请先执行 HybridCLR GenerateAll(目标 {buildTarget})");
                continue;
            }
            copied += CopyAndRegister(settings, group, src, $"{dstRoot}/{asm}.dll.bytes", addressPrefix + asm);
        }
        return copied;
    }

    /// <summary>
    /// HotUpdate_Builtin 本地组(内置兜底内容):Local 构建 + 无 ContentUpdate schema(内置副本不可变更,变更走新包)。
    /// 幂等:已存在仅校正路径;新组 .asset 自动入库(与 HotUpdate_Local 同套路)。
    /// </summary>
    static AddressableAssetGroup EnsureBuiltinGroup(AddressableAssetSettings settings)
    {
        var group = settings.FindGroup(GroupHotUpdateBuiltin);
        if (group == null)
        {
            group = settings.CreateGroup(GroupHotUpdateBuiltin, false, false, false,
                new List<AddressableAssetGroupSchema>());
            if (group == null)
            {
                Debug.LogError("[Phase9Setup] 分组创建失败: " + GroupHotUpdateBuiltin);
                return null;
            }
            // 本地组默认模板带 BundledAssetGroupSchema + ContentUpdate 模板?CreateGroup 传空 schema 需自建
            group.AddSchema<BundledAssetGroupSchema>();
            Debug.Log($"[Phase9Setup] 分组已创建: {GroupHotUpdateBuiltin}(Bundled,本地内置)");
        }
        // 指向本地构建/加载路径(内置兜底永远随包,禁止指远程变量 —— 那会让"兜底"也依赖网络)
        var bundle = group.GetSchema<BundledAssetGroupSchema>();
        if (bundle == null)
        {
            bundle = group.AddSchema<BundledAssetGroupSchema>();
        }
        bundle.BuildPath.SetVariableByName(settings, "Local.BuildPath");
        bundle.LoadPath.SetVariableByName(settings, "Local.LoadPath");
        return group;
    }

    /// <summary>
    /// 拷贝文件到目标并注册进热更组(幂等:文件已存在直接覆盖;地址一致即跳过注册)。
    /// 返回 1 = 完成一次拷贝/写入。
    /// </summary>
    static int CopyAndRegister(AddressableAssetSettings settings, AddressableAssetGroup group,
        string srcPath, string dstPath, string address)
    {
        File.Copy(srcPath, dstPath, true);
        AssetDatabase.ImportAsset(dstPath, ImportAssetOptions.ForceUpdate);
        RegisterAsset(settings, group, dstPath, address);
        return 1;
    }

    /// <summary>把目标路径资源注册进热更组并设地址(幂等:已注册仅做地址校正)。</summary>
    static void RegisterAsset(AddressableAssetSettings settings, AddressableAssetGroup group,
        string assetPath, string address)
    {
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogError($"[Phase9Setup] 目标资源 GUID 解析失败: {assetPath}");
            return;
        }
        var entry = settings.FindAssetEntry(guid);
        if (entry == null)
        {
            entry = settings.CreateOrMoveEntry(guid, group, false);
            if (entry == null) return;
            entry.address = address;
            entry.labels.Add("HotUpdate");
        }
        else if (entry.address != address)
        {
            entry.address = address; // 地址校正(防历史漂移)
        }
    }

    /// <summary>
    /// HotUpdate_Local 组:Local 构建 + ContentUpdateGroupSchema(可变更),BuildPath/LoadPath 指向远程 Profile 变量。
    /// 幂等:组已存在时也校正路径(曾出现 schema 路径仍指向 Local 变量导致 bundle 落入本地构建路径的坑)。
    /// </summary>
    static void EnsureHotUpdateGroup(AddressableAssetSettings settings)
    {
        var group = settings.FindGroup(GroupHotUpdateLocal);
        if (group == null)
        {
            group = settings.CreateGroup(GroupHotUpdateLocal, false, false, false,
                new List<AddressableAssetGroupSchema>());
            if (group == null)
            {
                Debug.LogError("[Phase9Setup] 分组创建失败: " + GroupHotUpdateLocal);
                return;
            }
            group.AddSchema<ContentUpdateGroupSchema>(); // 标记可变更组,Content Update 增量构建只处理该组
            Debug.Log($"[Phase9Setup] 分组已创建: {GroupHotUpdateLocal}(Bundled + ContentUpdate)");
        }
        // 显式指向 Profile 变量(新建 schema 默认空引用/历史残留 Local 路径 → 构建进本地目录)
        var bundle = group.GetSchema<BundledAssetGroupSchema>();
        if (bundle == null)
        {
            bundle = group.AddSchema<BundledAssetGroupSchema>();
            group.AddSchema<ContentUpdateGroupSchema>();
        }
        bundle.BuildPath.SetVariableByName(settings, "Remote.BuildPath");
        bundle.LoadPath.SetVariableByName(settings, "Remote.LoadPath");
        Debug.Log($"[Phase9Setup] HotUpdate_Local 路径自检: BuildPath={bundle.BuildPath.GetValue(settings)}, " +
                  $"LoadPath={bundle.LoadPath.GetValue(settings)}");
    }

    /// <summary>
    /// 按变量名写 Profile 值(内建变量 Remote.BuildPath/LoadPath 始终存在)。
    /// 注意:SetValue 第二参数是变量名(内部按名查 id),传 GUID 会静默失败且不打日志 —— 曾踩坑。
    /// </summary>
    static void SetProfileValue(AddressableAssetSettings settings, string variableName, string value)
    {
        settings.profileSettings.SetValue(settings.activeProfileId, variableName, value);
    }

    /// <summary>确保目录存在(递归创建 Assets 下目录)。</summary>
    static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, name);
    }
}
