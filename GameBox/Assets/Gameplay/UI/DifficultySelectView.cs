using Box.UI;
using Cysharp.Threading.Tasks;
using Sudoku.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Box.Gameplay
{
    /// <summary>
    /// 难度选择弹窗(Phase 4 4-1):用户主动弹窗,走 Router.Push(不进 PopupArbiter 被动队列)。
    /// 三档难度 → SetNormalGame → 关弹窗 → 切对局场景。
    /// </summary>
    public sealed class DifficultySelectView : UIView
    {
        UIService _svc;

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

        void Bind(string path, Difficulty difficulty)
        {
            var btn = transform.Find(path)?.GetComponent<BoxButton>();
            if (btn != null) btn.OnClick(() => Choose(difficulty));
        }

        async void Choose(Difficulty difficulty)
        {
            GameContext.SetNormalGame(difficulty);
            if (_svc == null) return;
            await _svc.Router.PopAsync(); // 先关弹窗再切场景,防竞态
            await SceneManager.LoadSceneAsync("Gameplay");
        }
    }
}
