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
        public void EaseOutBounce_Start_IsZero_End_IsOne()
        {
            Assert.AreEqual(0f, BoxTween.EaseOutBounce(0f), Eps);
            Assert.AreEqual(1f, BoxTween.EaseOutBounce(1f), Eps);
        }

        [Test]
        public void EaseOutBounce_FirstTouch_IsOne_Then_SingleBounce()
        {
            const float d1 = 2.75f;
            // 触底(t=1/2.75 处=1)→ 单次回弹(1.5/2.75 处=0.75)→ 回落停住(2/2.75 处=1)
            Assert.AreEqual(1f, BoxTween.EaseOutBounce(1f / d1), Eps, "触底应到达目标位置");
            Assert.AreEqual(0.75f, BoxTween.EaseOutBounce(1.5f / d1), Eps, "反弹峰值:弹到落地高度的 25%");
            Assert.AreEqual(1f, BoxTween.EaseOutBounce(2f / d1), Eps, "回落停住,不再弹跳");
            Assert.AreEqual(1f, BoxTween.EaseOutBounce(0.9f), Eps, "触底之后应保持 1,不再有后续反弹");
        }

        [Test]
        public void EaseOutBounce_Bounces_Only_Once()
        {
            // 用户拍板:落下来弹一下就好。曲线应只有一个反弹谷值(由降转升恰好一次),不允许多次弹跳。
            int valleys = 0;
            bool wasRising = true;
            float prev = 0f;
            for (int i = 1; i <= 30; i++)
            {
                float v = BoxTween.EaseOutBounce(i / 30f);
                bool rising = v >= prev - Eps;
                if (!wasRising && rising) valleys++; // 由降转升 = 一个谷值(一次反弹)
                wasRising = rising;
                prev = v;
            }
            Assert.AreEqual(1, valleys, "应只有一个反弹谷值(弹一下),不允许多次反弹");
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
