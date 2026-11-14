using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 全屏氛围后处理（方案 A）的规则：
    /// Bloom 只在 Medium 以上开；监控升起时强度减半；参数夹在 0~1。
    ///
    /// 这些规则抽成纯逻辑就是为了能在不启 Unity 的情况下盯住 ——
    /// 别以后顺手把 Bloom 在每个档位都打开（Low 档会掉帧）。
    /// </summary>
    public class ScreenPostPolicyTests
    {
        [Test]
        public void Bloom在Low档关闭()
        {
            // Very Low = 0，Low = 1 —— 默认运行时就是 Low
            Assert.IsFalse(ScreenPostPolicy.BloomAllowed(0));
            Assert.IsFalse(ScreenPostPolicy.BloomAllowed(1));
        }

        [Test]
        public void Bloom在Medium及以上开启()
        {
            Assert.IsTrue(ScreenPostPolicy.BloomAllowed(2));
            Assert.IsTrue(ScreenPostPolicy.BloomAllowed(3));
            Assert.IsTrue(ScreenPostPolicy.BloomAllowed(4));
            Assert.IsTrue(ScreenPostPolicy.BloomAllowed(5));
        }

        [Test]
        public void 监控升起时强度减半()
        {
            Assert.AreEqual(0.5f, ScreenPostPolicy.EffectiveIntensity(1f, true), 0.0001f);
            Assert.AreEqual(0.25f, ScreenPostPolicy.EffectiveIntensity(0.5f, true), 0.0001f);
        }

        [Test]
        public void 监控放下时强度不变()
        {
            Assert.AreEqual(1f, ScreenPostPolicy.EffectiveIntensity(1f, false), 0.0001f);
            Assert.AreEqual(0.85f, ScreenPostPolicy.EffectiveIntensity(0.85f, false), 0.0001f);
        }

        [Test]
        public void 强度夹在零到一之间()
        {
            Assert.AreEqual(0f, ScreenPostPolicy.EffectiveIntensity(-1f, false), 0.0001f);
            Assert.AreEqual(1f, ScreenPostPolicy.EffectiveIntensity(2f, false), 0.0001f);
        }

        [Test]
        public void 强度为零时无论监控都为零()
        {
            Assert.AreEqual(0f, ScreenPostPolicy.EffectiveIntensity(0f, false), 0.0001f);
            Assert.AreEqual(0f, ScreenPostPolicy.EffectiveIntensity(0f, true), 0.0001f);
        }

        [Test]
        public void 监控减半后仍然夹在合法区间()
        {
            for (float v = -0.5f; v <= 1.5f; v += 0.1f)
            {
                float result = ScreenPostPolicy.EffectiveIntensity(v, true);
                Assert.GreaterOrEqual(result, 0f);
                Assert.LessOrEqual(result, 1f);
            }
        }
    }
}