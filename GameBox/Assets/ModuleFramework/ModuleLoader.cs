using System;
using System.Collections.Generic;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.ModuleFramework
{
    /// <summary>
    /// 模块加载器实现(全 AOT,AppBootstrap 注册,静态访问与 UIService 同模式):
    /// 中间态(v1.0):按 entryType 反射实例化入口类型,模块内部负责场景切换;
    /// v1.1:入口类型在热更 dll(Assembly.Load 后 Type.GetType)+ Addressables 单场景,接口不变。
    /// 入口类型经 link.xml 保留,防 IL2CPP 裁剪导致 Type.GetType 返回 null。
    /// </summary>
    public sealed class ModuleLoader : IModuleLoader
    {
        public static ModuleLoader Instance { get; private set; }

        /// <summary>注册应用级唯一实例(启动引导调用);重复注册告警并覆盖。</summary>
        public static void Register(ModuleLoader loader)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            if (Instance != null) Debug.LogWarning("[ModuleFramework] ModuleLoader 重复注册,旧实例将被覆盖");
            Instance = loader;
        }

        public IReadOnlyList<ModuleEntry> Entries => _entriesList;

        readonly UIService _ui;
        readonly List<ModuleEntry> _entriesList = new();
        readonly Dictionary<string, ModuleEntry> _entries = new();
        readonly Dictionary<string, IGameModule> _active = new();
        readonly Dictionary<string, ModuleLoadState> _states = new();

        public ModuleLoader(UIService ui, IReadOnlyList<ModuleEntry> entries)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            if (entries != null)
                foreach (var e in entries)
                    if (e != null && !string.IsNullOrEmpty(e.id))
                    {
                        _entries[e.id] = e;
                        _entriesList.Add(e);
                    }
        }

        public UniTask<bool> EnterAsync(string moduleId, object args = null)
        {
            if (!_entries.TryGetValue(moduleId, out var entry))
            {
                Debug.LogWarning($"[ModuleFramework] 未知模块: {moduleId}");
                return UniTask.FromResult(false);
            }
            if (!entry.enabled || string.IsNullOrEmpty(entry.entryType))
            {
                Debug.LogWarning($"[ModuleFramework] 模块 {moduleId} 未启用或缺少入口类型");
                return UniTask.FromResult(false);
            }
            if (GetState(moduleId) != ModuleLoadState.Idle)
            {
                Debug.LogWarning($"[ModuleFramework] 模块 {moduleId} 正在加载/运行中,拒绝重入");
                return UniTask.FromResult(false);
            }

            _states[moduleId] = ModuleLoadState.Entering;
            try
            {
                var type = ResolveType(entry.entryType);
                if (type == null || !typeof(IGameModule).IsAssignableFrom(type))
                {
                    Debug.LogWarning($"[ModuleFramework] 入口类型不可用(检查 link.xml 保留): {entry.entryType}");
                    _states[moduleId] = ModuleLoadState.Idle;
                    return UniTask.FromResult(false);
                }

                var module = (IGameModule)Activator.CreateInstance(type);
                if (module == null) throw new InvalidOperationException($"入口类型无法实例化: {entry.entryType}");
                _active[moduleId] = module;
                return EnterCoreAsync(moduleId, module, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModuleFramework] 模块 {moduleId} 进入失败: {ex.Message}");
                _states[moduleId] = ModuleLoadState.Idle;
                _active.Remove(moduleId);
                return UniTask.FromResult(false);
            }
        }

        async UniTask<bool> EnterCoreAsync(string moduleId, IGameModule module, object args)
        {
            try
            {
                await module.OnEnter(new ModuleContext(_ui, this, args));
                _states[moduleId] = ModuleLoadState.Active;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModuleFramework] 模块 {moduleId} OnEnter 异常: {ex}");
                _states[moduleId] = ModuleLoadState.Idle;
                _active.Remove(moduleId);
                return false;
            }
        }

        public async UniTask<bool> ExitAsync(string moduleId)
        {
            if (!_active.TryGetValue(moduleId, out var module) || GetState(moduleId) != ModuleLoadState.Active)
                return false;

            _states[moduleId] = ModuleLoadState.Exiting;
            try
            {
                await module.OnExit();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModuleFramework] 模块 {moduleId} OnExit 异常: {ex}");
                return false;
            }
            finally
            {
                _states[moduleId] = ModuleLoadState.Idle;
                _active.Remove(moduleId);
            }
        }

        public ModuleLoadState GetState(string moduleId) =>
            _states.TryGetValue(moduleId, out var s) ? s : ModuleLoadState.Idle;

        /// <summary>
        /// 入口类型解析:先按 Type.GetType(程序集限定名直接命中),
        /// 再以裸全名扫描已加载程序集 —— 覆盖 v1.1 热更 dll(Assembly.Load 后已加载)
        /// 与 v1.0 跨 AOT 程序集(Box.ModuleFramework → HotUpdate.Sudoku)两种场景。
        /// </summary>
        static Type ResolveType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
