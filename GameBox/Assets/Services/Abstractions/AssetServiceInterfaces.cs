using System;

namespace Box.Services
{
    /// <summary>
    /// 资源加载服务接口(Phase 6 引入,11 文档 §3.3:热更侧禁止直接 Addressables.LoadAssetAsync 或碰 SDK 类型)。
    /// 本程序集为纯 C#(无 UnityEngine/Addressables 依赖),T 由调用处指定为 UnityEngine.Object 派生类型,
    /// 壳层实现负责 Addressables 转换,热更侧只经 ServiceLocator.Assets 调用本接口。
    /// 地址即 Addressables key(分组内地址约定,如 "UI/MainMenuView")。
    /// </summary>
    public interface IAssetService
    {
        /// <summary>异步加载资源;加载完成回调 onLoaded(失败/缺失回调 null,不抛到业务层)。</summary>
        void LoadAsset<T>(string address, Action<T> onLoaded) where T : class;

        /// <summary>加载资源并实例化(仅 GameObject 用;普通资产请用 LoadAsset)。</summary>
        void Instantiate(string address, Action<object> onLoaded);

        /// <summary>释放该地址已加载的资源/实例(LoadAsset/Instantiate 成功后可调用;重复释放安全)。</summary>
        void Release(string address);
    }
}