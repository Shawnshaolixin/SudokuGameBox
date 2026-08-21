using System;
using System.Collections;
using Box.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Box.UI.Tests
{
    /// <summary>PopupArbiter:互斥串行 / 资源缺失不挂起(回归) / 异常不挂起。</summary>
    public class PopupArbiterTests
    {
        GameObject _prefab;
        UIRouter _router;
        PopupArbiter _arbiter;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("Prefab", typeof(RectTransform), typeof(RecordingView));
            _router = new UIRouter(new FakeLoader { Prefab = _prefab });
            _arbiter = new PopupArbiter(_router);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_prefab);
            var root = GameObject.Find("BoxUI");
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [UnityTest]
        public IEnumerator Missing_Resource_Does_Not_Hang()
        {
            var router = new UIRouter(new FakeLoader { Prefab = null });
            var arbiter = new PopupArbiter(router);
            int win = -1;
            var race = UniTask.WhenAny(
                arbiter.ShowAsync("missing"),
                UniTask.Delay(TimeSpan.FromSeconds(3)));
            yield return race.ContinueWith(i => { win = i; return i; }).ToCoroutine();

            Assert.AreEqual(0, win, "资源缺失时 ShowAsync 必须完成,不得挂起(评审缺陷回归)");
            Assert.AreEqual(0, router.StackCount);
        }

        [UnityTest]
        public IEnumerator Throwing_Loader_Does_Not_Hang()
        {
            var loader = new FakeLoader { Prefab = _prefab, ThrowOnLoad = true };
            var router = new UIRouter(loader);
            var arbiter = new PopupArbiter(router);
            int win = -1;
            var race = UniTask.WhenAny(
                arbiter.ShowAsync("boom"),
                UniTask.Delay(TimeSpan.FromSeconds(3)));
            yield return race.ContinueWith(i => { win = i; return i; }).ToCoroutine();

            Assert.AreEqual(0, win, "加载抛异常时 ShowAsync 必须完成,不得挂起");
            Assert.AreEqual(0, router.StackCount);
        }

        [UnityTest]
        public IEnumerator Popups_Are_Serialized()
        {
            var first = _arbiter.ShowAsync("p1");
            var second = _arbiter.ShowAsync("p2");

            yield return UniTask.Delay(TimeSpan.FromMilliseconds(100)).ToCoroutine();
            Assert.AreEqual(1, _router.StackCount, "同一时刻只展示一个弹窗,p2 应排队");
            Assert.AreEqual(UniTaskStatus.Pending, second.Status, "p2 在 p1 关闭前不得完成");

            yield return _router.PopAsync().ToCoroutine(); // 关闭 p1
            yield return UniTask.Delay(TimeSpan.FromMilliseconds(100)).ToCoroutine();

            Assert.AreEqual(1, _router.StackCount, "p1 关闭后 p2 自动顶替展示");
            Assert.AreEqual(UniTaskStatus.Pending, second.Status, "p2 未关闭前仍不完成");

            yield return _router.PopAsync().ToCoroutine(); // 关闭 p2
            yield return UniTask.Delay(TimeSpan.FromMilliseconds(100)).ToCoroutine();

            Assert.AreEqual(UniTaskStatus.Succeeded, second.Status, "p2 关闭后 ShowAsync 完成");
            Assert.AreEqual(0, _router.StackCount);
        }
    }
}
