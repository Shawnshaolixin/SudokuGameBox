using System.Collections;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Box.UI.Tests
{
    /// <summary>UIRouter:生命周期顺序 / 缓存复用 / 下层隐藏恢复 / Replace 泛型 / 非栈层。</summary>
    public class UIRouterTests
    {
        GameObject _prefab;
        FakeLoader _loader;
        UIRouter _router;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("Prefab", typeof(RectTransform), typeof(RecordingView));
            _loader = new FakeLoader { Prefab = _prefab };
            _router = new UIRouter(_loader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_prefab);
            var root = GameObject.Find("BoxUI");
            if (root != null) Object.DestroyImmediate(root);
        }

        [UnityTest]
        public IEnumerator Push_Runs_Lifecycle_In_Order()
        {
            RecordingView view = null;
            yield return _router.PushAsync<RecordingView>("k", "arg1").ContinueWith(x => { view = x; return x; }).ToCoroutine();

            Assert.IsNotNull(view);
            Assert.AreEqual(1, _router.StackCount);
            Assert.IsTrue(view.IsShown);
            // EditMode 下 Instantiate 不执行克隆体 Awake,log 自 Create 起
            CollectionAssert.AreEqual(new[] { "Create", "Show:arg1" }, view.Log);
        }

        [UnityTest]
        public IEnumerator Pop_Hides_And_Caches_View()
        {
            RecordingView view = null;
            yield return _router.PushAsync<RecordingView>("k").ContinueWith(x => { view = x; return x; }).ToCoroutine();
            yield return _router.PopAsync().ToCoroutine();

            Assert.AreEqual(0, _router.StackCount);
            Assert.IsFalse(view.IsShown);
            CollectionAssert.AreEqual(new[] { "Create", "Show:", "Hide" }, view.Log);
            // 默认 CacheN(3):Pop 后进缓存,不销毁
            Assert.IsFalse(view == null);
        }

        [UnityTest]
        public IEnumerator Push_Same_Key_Reuses_Cache()
        {
            yield return _router.PushAsync<RecordingView>("k").ToCoroutine();
            yield return _router.PopAsync().ToCoroutine();
            Assert.AreEqual(1, _loader.LoadCount);

            yield return _router.PushAsync<RecordingView>("k").ToCoroutine();
            Assert.AreEqual(1, _loader.LoadCount, "第二次 Push 同 key 应命中缓存,不再加载");
            Assert.AreEqual(1, _router.StackCount);
        }

        [UnityTest]
        public IEnumerator Push_Hides_Previous_Pop_Restores_It()
        {
            RecordingView first = null;
            yield return _router.PushAsync<RecordingView>("k1").ContinueWith(x => { first = x; return x; }).ToCoroutine();
            RecordingView second = null;
            yield return _router.PushAsync<RecordingView>("k2").ContinueWith(x => { second = x; return x; }).ToCoroutine();

            Assert.IsFalse(first.IsShown, "新视图展示时应隐藏下层");
            Assert.IsTrue(second.IsShown);

            yield return _router.PopAsync().ToCoroutine();
            Assert.IsTrue(first.IsShown, "关闭顶层后应恢复下层");
            Assert.IsFalse(second.IsShown);
        }

        [UnityTest]
        public IEnumerator Replace_Returns_New_View_Type()
        {
            var dialogPrefab = new GameObject("DialogPrefab", typeof(RectTransform), typeof(BoxDialogView));
            _loader.Prefabs["dialog"] = dialogPrefab;
            try
            {
                yield return _router.PushAsync<RecordingView>("k1").ToCoroutine();

                BoxDialogView replaced = null;
                yield return _router.ReplaceAsync<BoxDialogView>("dialog").ContinueWith(x => { replaced = x; return x; }).ToCoroutine();

                Assert.IsNotNull(replaced);
                Assert.IsInstanceOf<BoxDialogView>(replaced);
                Assert.AreEqual(1, _router.StackCount, "Replace 保持栈深不变");
                Assert.AreEqual(2, _loader.LoadCount); // k1 + dialog 各加载一次
            }
            finally
            {
                Object.DestroyImmediate(dialogPrefab);
            }
        }

        [UnityTest]
        public IEnumerator Non_Stacked_View_Not_Pushed()
        {
            var hudPrefab = new GameObject("HudPrefab", typeof(RectTransform), typeof(HUDView));
            _loader.Prefabs["hud"] = hudPrefab;
            // 克隆体不拷贝 auto-property backing field(模拟真实 prefab 的序列化层值在加载回调中设置)
            _loader.OnInstantiated = v => ((HUDView)v).ForceLayer(UILayer.HUD);
            try
            {
                yield return _router.PushAsync<HUDView>("hud").ToCoroutine();
                Assert.AreEqual(0, _router.StackCount, "HUD 等非栈层不入栈");
            }
            finally
            {
                Object.DestroyImmediate(hudPrefab);
            }
        }
    }
}
