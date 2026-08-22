using Box.ModuleFramework;
using Box.UI;
using Cysharp.Threading.Tasks;
using Sudoku.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Box.HotUpdate.Sudoku
{
    /// <summary>
    /// 难度选择弹窗(Phase 4 4-1):用户主动弹窗,走 Router.Push(不进 PopupArbiter 被动队列)。
    /// 三档难度 → SetNormalGame → 关弹窗 → 切对局场景。
    /// </summary>
    public sealed class DifficultySelectView : UIView
    {
        UIService _svc;

        /// <summary>已选难度(Choose 置 true):OnHide 据此区分「取消进入」与「完成进入」。</summary>
        bool _chosen;

        protected override void Awake()
        {
            Layer = UILayer.Popup; // 返回键可关
            base.Awake();
        }

        protected override UniTask OnCreate()
        {
            _svc = UIService.Instance;
            Bind("EasyButton", Difficulty.Easy);
            Bind("MediumButton", Difficulty.Medium);
            Bind("HardButton", Difficulty.Hard);
            return UniTask.CompletedTask;
        }

        protected override async UniTask OnShow(object args)
        {
            await BoxTween.ScalePulse(transform, 0.8f, 1f, 0.22f); // 弹入(D-15)
        }

        // 弹窗被关闭(返回键/取消)= 取消本次进入:复位模块状态(Idle),
        // 否则 _states[sudoku] 卡 Active,再次点开始被 EnterAsync 拒绝。
        // Choose 路径已置 _chosen,此钩子不触发,不会误退(完成进入保持 Active)。
        protected override async UniTask OnHide()
        {
            if (_chosen) return;
            if (_svc == null || _svc.Router.StackCount != 0) return; // 被上层压栈(hide)而非关闭,不处理
            var loader = ModuleLoader.Instance;
            if (loader != null) await loader.ExitAsync("sudoku");
        }

        void Bind(string path, Difficulty difficulty)
        {
            var btn = transform.Find(path)?.GetComponent<BoxButton>();
            if (btn != null) btn.OnClick(() => Choose(difficulty));
        }

        async void Choose(Difficulty difficulty)
        {
            _chosen = true; // 先标记,再关弹窗:OnHide 不会误退模块(完成进入,保持 Active)
            GameContext.SetNormalGame(difficulty);
            if (_svc == null) return;
            await _svc.Router.PopAsync(); // 先关弹窗再切场景,防竞态
            await SceneManager.LoadSceneAsync("Gameplay");
        }
    }
}
