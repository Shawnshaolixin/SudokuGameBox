namespace Box.HotUpdate.Core
{
    /// <summary>
    /// 热更代码版本常量(Phase 9 9-2 Core 基座)。
    /// 语义:热更代码自身的版本,与包版本(PlayerSettings version)解耦,
    /// 9-3 启动校验 / 9-4 远程版本对比时使用;更新热更代码时递增。
    /// </summary>
    public static class HotUpdateVersion
    {
        /// <summary>当前热更代码版本(v1.1 热更主线首个基线)。</summary>
        public const string CodeVersion = "1.1.0";
    }
}
