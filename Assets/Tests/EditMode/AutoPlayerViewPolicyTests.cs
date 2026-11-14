using NailongFNAF.Gameplay;
using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// AI 代打**每个行为要转到哪个角度**的规则（用户 2026-11-13 定）。
    ///
    /// 核心要求：**AI 做行为必须转到对应角度，行为结束后不必回正**。
    /// 这个测试存在的意义是防止以后有人把它改回「一动不动」或者「一律回正前方」。
    /// </summary>
    public class AutoPlayerViewPolicyTests
    {
        [Test]
        public void 左侧行为转到左边()
        {
            Assert.AreEqual(ViewSide.Left, AutoPlayerViewPolicy.ForSide(Side.Left));
        }

        [Test]
        public void 右侧行为转到右边()
        {
            Assert.AreEqual(ViewSide.Right, AutoPlayerViewPolicy.ForSide(Side.Right));
        }

        [Test]
        public void 侧门行为永远不会回正前方()
        {
            // 「行为结束不必回正」：侧门行为本身必须指向那一侧，
            // 不能顺手返回 Front 把镜头拉回中间。
            Assert.AreNotEqual(ViewSide.Front, AutoPlayerViewPolicy.ForSide(Side.Left));
            Assert.AreNotEqual(ViewSide.Front, AutoPlayerViewPolicy.ForSide(Side.Right));
        }

        [Test]
        public void 左转的目标量是负一()
        {
            Assert.AreEqual(-1f, AutoPlayerViewPolicy.PanFor(ViewSide.Left));
        }

        [Test]
        public void 右转的目标量是正一()
        {
            Assert.AreEqual(1f, AutoPlayerViewPolicy.PanFor(ViewSide.Right));
        }

        [Test]
        public void 正前方目标量是零()
        {
            Assert.AreEqual(0f, AutoPlayerViewPolicy.PanFor(ViewSide.Front));
        }

        [Test]
        public void 目标量都落在合法区间()
        {
            // ViewExternalPan 的取值必须夹在 [-1, 1]，否则 UpdateView 里要额外兜底
            foreach (ViewSide side in new[] { ViewSide.Front, ViewSide.Left, ViewSide.Right })
            {
                float pan = AutoPlayerViewPolicy.PanFor(side);
                Assert.GreaterOrEqual(pan, -1f);
                Assert.LessOrEqual(pan, 1f);
            }
        }
    }
}