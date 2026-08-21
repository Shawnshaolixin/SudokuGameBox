using Box.UI;
using NUnit.Framework;
using UnityEngine;

namespace Box.UI.Tests
{
    /// <summary>UILayerManager:独立 Canvas 创建 / sortingOrder / Scene 层不创建。</summary>
    public class UILayerTests
    {
        [TearDown]
        public void TearDown()
        {
            var root = GameObject.Find("BoxUI");
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void Each_Layer_Gets_Own_Canvas_With_SortingOrder()
        {
            var mgr = new UILayerManager();
            var hud = mgr.GetCanvas(UILayer.HUD);
            var window = mgr.GetCanvas(UILayer.Window);
            var popup = mgr.GetCanvas(UILayer.Popup);

            Assert.IsNotNull(hud);
            Assert.IsNotNull(window);
            Assert.IsNotNull(popup);
            Assert.AreNotEqual(hud, window);
            Assert.AreNotEqual(window, popup);
            Assert.AreEqual(100, hud.sortingOrder);
            Assert.AreEqual(200, window.sortingOrder);
            Assert.AreEqual(300, popup.sortingOrder);
        }

        [Test]
        public void Same_Layer_Returns_Same_Canvas()
        {
            var mgr = new UILayerManager();
            Assert.AreSame(mgr.GetCanvas(UILayer.Toast), mgr.GetCanvas(UILayer.Toast));
        }

        [Test]
        public void Scene_Layer_Returns_Null()
        {
            var mgr = new UILayerManager();
            Assert.IsNull(mgr.GetCanvas(UILayer.Scene), "Scene 层 Canvas 由玩法场景管理,UIKit 不创建");
        }
    }
}
