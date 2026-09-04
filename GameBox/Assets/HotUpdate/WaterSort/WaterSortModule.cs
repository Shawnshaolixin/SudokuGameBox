using Box.ModuleFramework;
using Box.Services;
using Box.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序玩法模块入口(19 文档 WS-20,清单 id="watersort",v1.1 热更下发):
    /// 弹窗模式(单场景无场景切换):OnEnter → 新建会话实例 → Router 推入主视图 WaterSortView,
    /// 视图内部自管「选关/对局/结算」三面板;关闭 = 视图自弹,OnHide 统一收口 ExitAsync(见 WaterSortView 契约)。
    /// v1.0 随包 AOT 编译,入口类型经 link.xml 保留防 IL2CPP 裁剪(11 §3.1,13 文档步骤 5)。
    /// 埋点前缀 watersort.*,由视图上报(§8.4 契约,金额双通道 M1.4 起)。
    /// </summary>
    public sealed class WaterSortModule : IGameModule
    {
        public const string ModuleId = "watersort";

        /// <summary>主视图 prefab 地址(PRD WS-20 固定;进 Game_WaterSort 组,不入 Resources)。</summary>
        public const string MainViewAddress = "UI/WaterSortView";

        public string Id => ModuleId;

        public async UniTask OnEnter(ModuleContext ctx)
        {
            // args 预留:"daily"=每日挑战(M2 接;按日期取预生成题库关)。会话随模块每次进入新建,
            // 视图经 WaterSortSession.Instance 访问;旧会话随旧视图销毁退订,无跨局残留。
            var isDaily = ctx.Args as string == "daily";
            WaterSortSession.Instance = new WaterSortSession(isDaily);

            var router = UIService.Instance?.Router;
            var view = router != null
                ? await router.PushAsync<WaterSortView>(MainViewAddress, isDaily)
                : null;
            if (view != null) return;

            // 主视图加载失败(地址缺失/加载出错,仅开发期配置失误会出现):PushAsync 失败不隐藏下层
            // (MoreGames 仍可见),但模块随后会被 Loader 标记 Active —— 延迟数帧等状态就绪后自退,
            // 防"隐形模块"占位导致玩家无法重进(模块失败路径在 EnterCoreAsync 内不可自退,故延后)。
            Debug.LogError($"[WaterSort] 主视图加载失败: {MainViewAddress},模块自动退出");
            await UniTask.DelayFrame(2);
            ctx.Loader?.ExitAsync(ModuleId).Forget();
        }

        public UniTask OnExit()
        {
            // 弹窗模式退出常态 = 视图自弹(WaterSortView.OnHide 收口 ExitAsync),本回调执行时视图已不在栈,
            // 栈底剩余的 MoreGames 是壳层视图(模块入口来源),绝不可清栈误关。
            // 若未来出现"模块视图仍在栈上被外部强退"(如交叉导量入口),需按本模块视图 Id 精弹,
            // 勿用 PopToAsync(null)。
            return UniTask.CompletedTask;
        }
    }
}
