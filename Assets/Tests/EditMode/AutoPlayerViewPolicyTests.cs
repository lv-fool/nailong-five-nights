using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// AI 代打**什么时候可以动视角**的规则（用户 2026-11-03 定）。
    ///
    /// 核心要求：**只有闪手电筒的时候**才把视角收回正前方，其余时候归玩家。
    /// 这个测试存在的意义就是防止以后有人「顺手」把它改回每帧无条件回正 ——
    /// 那等于把玩家唯一的操作自由也抢了。
    /// </summary>
    public class AutoPlayerViewPolicyTests
    {
        private const int FlashesNeeded = 4;

        [Test]
        public void 噜噜堵在正门且还没闪够时接管视角()
        {
            Assert.IsTrue(AutoPlayerViewPolicy.ShouldHoldView(
                luluAtFrontDoor: true, flashCount: 0,
                flashesNeeded: FlashesNeeded, luluAttacking: false));
        }

        [Test]
        public void 还没走到正门时不接管视角()
        {
            // 它还在路上 —— 这时玩家想转头看侧门，代打不许抢镜头
            Assert.IsFalse(AutoPlayerViewPolicy.ShouldHoldView(
                luluAtFrontDoor: false, flashCount: 0,
                flashesNeeded: FlashesNeeded, luluAttacking: false));
        }

        [Test]
        public void 已经闪够了立刻把视角还给玩家()
        {
            // 闪够 4 下它就退走，从这一刻起视角必须归玩家
            Assert.IsFalse(AutoPlayerViewPolicy.ShouldHoldView(
                luluAtFrontDoor: true, flashCount: FlashesNeeded,
                flashesNeeded: FlashesNeeded, luluAttacking: false));
        }

        [Test]
        public void 还在闪的中途继续接管()
        {
            // 闪到第 3 下（还差一下）时仍要回正，否则最后一下打空它就扑上来了
            Assert.IsTrue(AutoPlayerViewPolicy.ShouldHoldView(
                luluAtFrontDoor: true, flashCount: FlashesNeeded - 1,
                flashesNeeded: FlashesNeeded, luluAttacking: false));
        }

        [Test]
        public void 它已经在扑人时不再抢镜头()
        {
            // 这一下已经不是闪手电救得回来的了，抢镜头只会让玩家更懵
            Assert.IsFalse(AutoPlayerViewPolicy.ShouldHoldView(
                luluAtFrontDoor: true, flashCount: 0,
                flashesNeeded: FlashesNeeded, luluAttacking: true));
        }

        [Test]
        public void 没威胁的平常时刻一律不接管()
        {
            Assert.IsFalse(AutoPlayerViewPolicy.ShouldHoldView(
                luluAtFrontDoor: false, flashCount: 0,
                flashesNeeded: FlashesNeeded, luluAttacking: false));
        }

        [Test]
        public void 闪够了之后不会再接管回来()
        {
            // 回归保护：FlashCount 涨过阈值后，无论再调用多少次都必须是 false ——
            // 曾经每帧无条件回正，就是这条没守住
            for (int count = FlashesNeeded; count < FlashesNeeded + 5; count++)
            {
                Assert.IsFalse(
                    AutoPlayerViewPolicy.ShouldHoldView(true, count, FlashesNeeded, false),
                    "FlashCount=" + count + " 时不该再接管视角");
            }
        }
    }
}
