using System;

namespace Box.Gameplay.HotUpdate
{
    /// <summary>
    /// 远程内容版本指针(tools/deploy_firebase.ps1 每次发布/回滚时生成或改写,布局见 20 文档 §11)。
    ///
    /// 回滚机制(Phase 10-2 前置):服务器每版内容存独立目录 public/&lt;Channel&gt;/&lt;version&gt;/Android/,
    /// 本指针指明"当前版本";客户端启动时拉取解析,把 catalog URL 拼为
    /// {RemoteServerUrl}/{version}/Android/catalog_1.0.bin。**发布/回滚都只是改写本文件再 deploy**,
    /// 旧版本目录与 bundle 永不删除 → 秒级回退、无需重新构建。
    /// </summary>
    [Serializable]
    public sealed class RemoteContentIndex
    {
        /// <summary>当前内容版本(=服务器版本目录名,形如 v20260904-153001)。</summary>
        public string version;

        /// <summary>通道标识(staging/production),诊断用。</summary>
        public string channel;

        /// <summary>该版本 catalog hash,诊断用(变更检测仍以 Addressables 自身的 .hash 文件机制为准)。</summary>
        public string catalogHash;

        /// <summary>部署时间字符串,审计用。</summary>
        public string deployedAt;

        /// <summary>回滚来源版本(仅回滚部署时写入),审计用;正常发布为空。</summary>
        public string previousVersion;
    }
}
