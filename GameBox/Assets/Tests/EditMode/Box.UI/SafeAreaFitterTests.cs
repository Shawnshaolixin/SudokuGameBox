using Box.UI;
using NUnit.Framework;
using UnityEngine;

namespace Box.UI.Tests
{
    /// <summary>SafeAreaFitter.SetInsets 纯函数:无刘海全拉伸 / 刘海缩边 / 非法尺寸防除零。</summary>
    public class SafeAreaFitterTests
    {
        static RectTransform NewRect() => new GameObject().AddComponent<RectTransform>();

        [Test]
        public void No_Notch_Stretches_Full()
        {
            var rt = NewRect();
            SafeAreaFitter.SetInsets(rt, new Rect(0, 0, 1080, 1920), new Vector2(1080, 1920));
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
        }

        [Test]
        public void Top_Notch_Shrinks_Anchors()
        {
            var rt = NewRect();
            SafeAreaFitter.SetInsets(rt, new Rect(0, 100, 1080, 1720), new Vector2(1080, 1920));
            Assert.AreEqual(0f, rt.anchorMin.x);
            Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.0001f);
            Assert.AreEqual(1f, rt.anchorMax.x);
            Assert.AreEqual(1f - 100f / 1920f, rt.anchorMax.y, 0.0001f);
        }

        [Test]
        public void Zero_Screen_Does_Not_Divide_By_Zero()
        {
            var rt = NewRect();
            // 先设已知锚点(勿依赖 RectTransform 默认值——实际为 (0.5,0.5)),再验证非法屏幕尺寸时不被改动
            rt.anchorMin = new Vector2(0.25f, 0.35f);
            rt.anchorMax = new Vector2(0.75f, 0.85f);
            Assert.DoesNotThrow(() => SafeAreaFitter.SetInsets(rt, new Rect(0, 0, 1080, 1920), Vector2.zero));
            Assert.AreEqual(new Vector2(0.25f, 0.35f), rt.anchorMin);
            Assert.AreEqual(new Vector2(0.75f, 0.85f), rt.anchorMax, "非法屏幕尺寸时不改动锚点");
        }
    }
}
