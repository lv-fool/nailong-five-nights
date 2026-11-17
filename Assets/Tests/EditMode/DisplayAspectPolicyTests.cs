using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 设置菜单「显示比例」的档位与文字。
    /// </summary>
    public class DisplayAspectPolicyTests
    {
        [Test]
        public void 档位按自动到十六比九到十六比十到四比三循环()
        {
            Assert.AreEqual(DisplayAspect.Widescreen,
                DisplayAspectPolicy.Next(DisplayAspect.Auto));
            Assert.AreEqual(DisplayAspect.Wide16x10,
                DisplayAspectPolicy.Next(DisplayAspect.Widescreen));
            Assert.AreEqual(DisplayAspect.Standard,
                DisplayAspectPolicy.Next(DisplayAspect.Wide16x10));
            Assert.AreEqual(DisplayAspect.Auto,
                DisplayAspectPolicy.Next(DisplayAspect.Standard));
        }

        [Test]
        public void 非法档位回到自动()
        {
            Assert.AreEqual(DisplayAspect.Auto, DisplayAspectPolicy.Next((DisplayAspect)99));
            Assert.AreEqual(DisplayAspect.Auto, DisplayAspectPolicy.FromInt(-1));
            Assert.AreEqual(DisplayAspect.Auto, DisplayAspectPolicy.FromInt(99));
        }

        [Test]
        public void 菜单文字()
        {
            Assert.AreEqual("自动", DisplayAspectPolicy.Label(DisplayAspect.Auto));
            Assert.AreEqual("16:9", DisplayAspectPolicy.Label(DisplayAspect.Widescreen));
            Assert.AreEqual("16:10", DisplayAspectPolicy.Label(DisplayAspect.Wide16x10));
            Assert.AreEqual("4:3", DisplayAspectPolicy.Label(DisplayAspect.Standard));
        }

        [Test]
        public void 比例分子分母正确()
        {
            int w, h;

            Assert.IsTrue(DisplayAspectPolicy.TryGetAspect(DisplayAspect.Widescreen, out w, out h));
            Assert.AreEqual(16, w);
            Assert.AreEqual(9, h);

            Assert.IsTrue(DisplayAspectPolicy.TryGetAspect(DisplayAspect.Wide16x10, out w, out h));
            Assert.AreEqual(16, w);
            Assert.AreEqual(10, h);

            Assert.IsTrue(DisplayAspectPolicy.TryGetAspect(DisplayAspect.Standard, out w, out h));
            Assert.AreEqual(4, w);
            Assert.AreEqual(3, h);

            Assert.IsFalse(DisplayAspectPolicy.TryGetAspect(DisplayAspect.Auto, out w, out h));
        }
    }
}