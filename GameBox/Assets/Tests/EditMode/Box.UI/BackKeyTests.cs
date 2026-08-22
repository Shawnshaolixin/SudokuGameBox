using System;
using System.Collections;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Box.UI.Tests
{
    /// <summary>
    /// UIService 返回键扩展(Phase 4):CustomBackHandler 消费/交还/注销。
    /// BackKeyRunner 的逐帧监听依赖帧循环,EditMode 不驱动,由 PlayMode 冒烟覆盖。
    /// </summary>
    public class BackKeyTests
    {
        GameObject _prefab;
        FakeLoader _loader;
        UIService _service;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("Prefab", typeof(RectTransform), typeof(RecordingView));
            // 返回键只弹 Popup 层栈顶(Phase 3 既定语义);Layer 无 [SerializeField],克隆体不拷贝,须在实例化回调中设层
            _loader = new FakeLoader { Prefab = _prefab };
            _loader.OnInstantiated = v => ((RecordingView)v).ForceLayer(UILayer.Popup);
            _service = new UIService(_loader);
        }

        [TearDown]
        public void TearDown()
        {
            _service.ClearBackHandler();
            UnityEngine.Object.DestroyImmediate(_prefab);
            var es = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
            var runner = GameObject.Find("BoxUI_BackKey");
            if (runner != null) UnityEngine.Object.DestroyImmediate(runner);
        }

        [UnityTest]
        public IEnumerator No_Handler_Delegates_To_Router()
        {
            yield return _service.Router.PushAsync<RecordingView>("k").ToCoroutine();
            Assert.AreEqual(1, _service.Router.StackCount);

            yield return _service.HandleBackAsync().ToCoroutine();

            Assert.AreEqual(0, _service.Router.StackCount, "未注册 handler 时应由路由弹栈");
        }

        [UnityTest]
        public IEnumerator Handler_Consumed_True_Does_Not_Pop()
        {
            _service.RegisterBackHandler(() => UniTask.FromResult(true));
            yield return _service.Router.PushAsync<RecordingView>("k").ToCoroutine();
            Assert.AreEqual(1, _service.Router.StackCount);

            yield return _service.HandleBackAsync().ToCoroutine();

            Assert.AreEqual(1, _service.Router.StackCount, "handler 返回 true 视为已消费,路由不得弹栈");
        }

        [UnityTest]
        public IEnumerator Handler_Delegates_When_False()
        {
            // 对局视图语义:栈深>0 时返回 false 交还路由关弹窗
            _service.RegisterBackHandler(() => UniTask.FromResult(false));
            yield return _service.Router.PushAsync<RecordingView>("k").ToCoroutine();
            Assert.AreEqual(1, _service.Router.StackCount);

            yield return _service.HandleBackAsync().ToCoroutine();

            Assert.AreEqual(0, _service.Router.StackCount, "handler 返回 false 时应交还路由弹栈");
            Assert.IsNotNull(_service.CustomBackHandler, "返回 false 不应注销 handler(弹窗关闭后仍需继续消费返回键)");
        }

        [UnityTest]
        public IEnumerator Handler_Still_Consumes_After_Popup_Closed()
        {
            // 回归:弹窗关闭后返回键继续=对局 Undo(handler 保持注册)
            int consumed = 0;
            _service.RegisterBackHandler(() => { consumed++; return UniTask.FromResult(true); });
            yield return _service.Router.PushAsync<RecordingView>("k").ToCoroutine();

            yield return _service.HandleBackAsync().ToCoroutine(); // 弹窗打开时返回键:handler 消费(+1)
            yield return _service.Router.PopAsync().ToCoroutine(); // 弹窗关闭

            yield return _service.HandleBackAsync().ToCoroutine(); // 弹窗关闭后返回键:handler 仍生效(+1)
            Assert.AreEqual(2, consumed, "弹窗关闭后 handler 应继续生效并消费返回键");
        }

        [Test]
        public void Register_Overwrites_And_Clear_Removes()
        {
            int a = 0, b = 0;
            Func<UniTask<bool>> h1 = () => { a++; return UniTask.FromResult(true); };
            Func<UniTask<bool>> h2 = () => { b++; return UniTask.FromResult(true); };

            _service.RegisterBackHandler(h1);
            Assert.AreSame(h1, _service.CustomBackHandler);

            _service.RegisterBackHandler(h2);
            Assert.AreSame(h2, _service.CustomBackHandler, "重复注册应覆盖旧 handler");

            _service.ClearBackHandler();
            Assert.IsNull(_service.CustomBackHandler);
        }

        [Test]
        public void BackKeyRunner_GameObject_Created()
        {
            var runner = GameObject.Find("BoxUI_BackKey");
            Assert.IsNotNull(runner, "UIService 构造时应创建返回键监听器");
        }
    }
}
