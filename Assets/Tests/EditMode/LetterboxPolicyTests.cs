using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 「显示比例」黑边计算：目标比窗口宽 → 上下黑边；比窗口窄 → 左右黑边；自动 → 满屏。
    /// </summary>
    public class LetterboxPolicyTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void 自动档满屏()
        {
            float x, y, w, h;
            LetterboxPolicy.Compute(DisplayAspect.Auto, 1920, 1080, out x, out y, out w, out h);

            Assert.AreEqual(0f, x, Epsilon);
            Assert.AreEqual(0f, y, Epsilon);
            Assert.AreEqual(1f, w, Epsilon);
            Assert.AreEqual(1f, h, Epsilon);
        }

        [Test]
        public void 十六比九窗口选十六比九时没有黑边()
        {
            float x, y, w, h;
            LetterboxPolicy.Compute(DisplayAspect.Widescreen, 1920, 1080, out x, out y, out w, out h);

            Assert.AreEqual(1f, w, Epsilon);
            Assert.AreEqual(1f, h, Epsilon);
        }

        [Test]
        public void 十六比九窗口选四比三时左右黑边()
        {
            float x, y, w, h;
            LetterboxPolicy.Compute(DisplayAspect.Standard, 1920, 1080, out x, out y, out w, out h);

            // 4:3 比 16:9 窄 → 左右黑边，画面居中
            Assert.AreEqual(0.75f, w, Epsilon);
            Assert.AreEqual(1f, h, Epsilon);
            Assert.AreEqual(0.125f, x, Epsilon);
            Assert.AreEqual(0f, y, Epsilon);
        }

        [Test]
        public void 四比三窗口选十六比九时上下黑边()
        {
            float x, y, w, h;
            LetterboxPolicy.Compute(DisplayAspect.Widescreen, 1024, 768, out x, out y, out w, out h);

            // 16:9 比 4:3 宽 → 上下黑边
            Assert.AreEqual(1f, w, Epsilon);
            Assert.AreEqual(0.75f, h, Epsilon);
            Assert.AreEqual(0f, x, Epsilon);
            Assert.AreEqual(0.125f, y, Epsilon);
        }

        [Test]
        public void 十六比十窗口选十六比九时上下黑边()
        {
            float x, y, w, h;
            LetterboxPolicy.Compute(DisplayAspect.Widescreen, 1920, 1200, out x, out y, out w, out h);

            // 16:9 比 16:10 宽一点点 → 上下细黑边
            Assert.AreEqual(1f, w, Epsilon);
            Assert.AreEqual(0.9f, h, Epsilon);
            Assert.AreEqual(0.05f, y, Epsilon);
        }

        [Test]
        public void 安全区始终居中且不越界()
        {
            foreach (DisplayAspect aspect in new[]
            {
                DisplayAspect.Widescreen, DisplayAspect.Wide16x10, DisplayAspect.Standard
            })
            {
                float x, y, w, h;
                LetterboxPolicy.Compute(aspect, 1600, 900, out x, out y, out w, out h);

                Assert.GreaterOrEqual(x, 0f);
                Assert.GreaterOrEqual(y, 0f);
                Assert.LessOrEqual(x + w, 1.0001f);
                Assert.LessOrEqual(y + h, 1.0001f);
                Assert.Greater(w, 0f);
                Assert.Greater(h, 0f);
            }
        }

        [Test]
        public void 窗口尺寸非法时退回满屏()
        {
            float x, y, w, h;
            LetterboxPolicy.Compute(DisplayAspect.Standard, 0, 0, out x, out y, out w, out h);

            Assert.AreEqual(0f, x, Epsilon);
            Assert.AreEqual(0f, y, Epsilon);
            Assert.AreEqual(1f, w, Epsilon);
            Assert.AreEqual(1f, h, Epsilon);
        }
    }
}