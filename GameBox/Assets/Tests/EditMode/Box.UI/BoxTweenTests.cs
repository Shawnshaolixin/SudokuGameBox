using Box.UI;
using NUnit.Framework;
using UnityEngine;

namespace Box.UI.Tests
{
    /// <summary>
    /// BoxTween 纯曲线单测(D-15 自研补间)。
    /// 动画循环依赖 Time.deltaTime,EditMode 无帧步进,只在 PlayMode 冒烟;此处只测纯函数。
    /// </summary>
    public class BoxTweenTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void EaseOutBack_AtStart_IsZero() => Assert.AreEqual(0f, BoxTween.EaseOutBack(0f), Eps);

        [Test]
        public void EaseOutBack_AtEnd_IsOne() => Assert.AreEqual(1f, BoxTween.EaseOutBack(1f), Eps);

        [Test]
        public void EaseOutBack_Overshoots_Midway()
        {
            // 回弹特性:t≈0.5 时超过 1(过冲)
            Assert.Greater(BoxTween.EaseOutBack(0.5f), 1f);
        }

        [Test]
        public void EaseOutCubic_StartEnd()
        {
            Assert.AreEqual(0f, BoxTween.EaseOutCubic(0f), Eps);
            Assert.AreEqual(1f, BoxTween.EaseOutCubic(1f), Eps);
        }

        [Test]
        public void EaseOutCubic_IsMonotonic()
        {
            float prev = 0f;
            for (int i = 1; i <= 20; i++)
            {
                float v = BoxTween.EaseOutCubic(i / 20f);
                Assert.GreaterOrEqual(v, prev - Eps);
                prev = v;
            }
        }

        [Test]
        public void EaseInOutCubic_KeyPoints()
        {
            Assert.AreEqual(0f, BoxTween.EaseInOutCubic(0f), Eps);
            Assert.AreEqual(0.5f, BoxTween.EaseInOutCubic(0.5f), Eps);
            Assert.AreEqual(1f, BoxTween.EaseInOutCubic(1f), Eps);
        }

        [Test]
        public void EaseInOutCubic_IsMonotonic()
        {
            float prev = 0f;
            for (int i = 1; i <= 20; i++)
            {
                float v = BoxTween.EaseInOutCubic(i / 20f);
                Assert.GreaterOrEqual(v, prev - Eps);
                prev = v;
            }
        }
    }
}
