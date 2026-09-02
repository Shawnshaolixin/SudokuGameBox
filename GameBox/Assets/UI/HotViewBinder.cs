using System;
using UnityEngine;

namespace Box.UI
{
    /// <summary>
    /// 热更视图桥(Phase 9 9-4 真机踩坑修复,10 文档 §16.5)。
    ///
    /// 背景:v1.1(HYBRIDCLR)构建中 FilterHotFixAssemblies 把热更程序集从主包剥离,
    /// 序列化进 prefab/场景的热更视图组件(GameplayView/DifficultySelectView 等)
    /// 真机反序列化时脚本表找不到 → 组件静默丢失(Awake/OnCreate 永不执行,UI 皮正常但不响应)。
    /// 本桥挂在视图 prefab 根上(由 Editor 迁移脚本 Phase9HotViewBinderSetup 写入):
    /// Awake 时若根上没有 UIView(序列化组件已被剥)则按 viewTypeFullName 动态 AddComponent 挂回,
    /// AddComponent 同步触发 Awake,生命周期与序列化直挂一致。
    /// v1.0/编辑器下组件正常序列化 → GetComponent 命中 → 桥空转零副作用(幂等,双模式共用同一 prefab)。
    /// 架构纪律:热更视图组件不直接进 prefab/场景,一律经本桥运行时附加(HybridCLR 官方推荐路径)。
    /// </summary>
    public sealed class HotViewBinder : MonoBehaviour
    {
        [Tooltip("热更视图类型(UIView 子类):程序集限定名或裸全名均可,如 Box.HotUpdate.Sudoku.GameplayView")]
        [SerializeField]
        string viewTypeFullName;

        /// <summary>类型未就绪时的重试上限(帧数)。热更 dll 通常在启动后 1~2s 装载,此处仅防御性兜底。</summary>
        const int MaxRetryFrames = 600; // 10s @60fps

        /// <summary>视图类型名(Editor 迁移脚本 Phase9HotViewBinderSetup 写入;运行期仅读)。</summary>
        public string ViewTypeFullName
        {
            get => viewTypeFullName;
            set => viewTypeFullName = value;
        }

        int _retryFrames;

        void Awake()
        {
            if (!string.IsNullOrEmpty(viewTypeFullName) && TryAttach()) return;
            // 首帧未达成(组件缺失且类型未解析)→ 进入轮询等待热更程序集装载
            _retryFrames = MaxRetryFrames;
        }

        void Update()
        {
            if (_retryFrames <= 0) return;
            if (TryAttach())
            {
                _retryFrames = 0; // 附加达成,停止轮询
                return;
            }
            if (--_retryFrames == 0)
                Debug.LogWarning($"[HotViewBinder] 视图 {viewTypeFullName} 重试 {MaxRetryFrames} 帧仍未附加" +
                                 "(热更程序集未装载?静态 UI 保持可见但不响应)", this);
        }

        /// <summary>
        /// 幂等附加:根上已有 UIView(序列化还原成功 → v1.0/编辑器)即视为达成;
        /// 否则解析目标类型并 AddComponent(类型不可解析返回 false,调用方稍后重试)。
        /// </summary>
        bool TryAttach()
        {
            if (GetComponent<UIView>() != null) return true; // 视图组件已在,无需附加
            var type = ResolveViewType(viewTypeFullName);
            if (type == null) return false; // 热更 dll 尚未装载,等待重试
            if (!typeof(UIView).IsAssignableFrom(type))
            {
                // 配置错误:一次性告警并停轮询(避免每帧重复报错)
                Debug.LogError($"[HotViewBinder] {viewTypeFullName} 不是 UIView 子类,拒绝附加", this);
                return true;
            }
            gameObject.AddComponent(type); // 同步触发目标组件 Awake/OnEnable,与序列化挂载生命周期一致
            Debug.Log($"[HotViewBinder] 运行时附加热更视图: {viewTypeFullName}", this);
            return true;
        }

        /// <summary>
        /// 类型解析:先按原样 Type.GetType(程序集限定名直接命中),
        /// 再以裸全名(逗号前段)扫描已加载程序集 —— 覆盖 Assembly.Load 后的热更 dll(与 ModuleLoader.ResolveType 同款)。
        /// </summary>
        static Type ResolveViewType(string fullName)
        {
            var t = Type.GetType(fullName, false);
            if (t != null) return t;
            var bare = fullName;
            int comma = fullName.IndexOf(',');
            if (comma > 0) bare = fullName.Substring(0, comma).Trim();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(bare, false);
                if (t != null) return t;
            }
        return null;
        }
    }
}
