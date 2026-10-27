using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 袋鼠留下的那份外卖（2026-10-26 加）：被门挡住 → 敲门 → 走人 → 门口剩一份，
    /// 捡起来按 1 / 2 / 5 三档里的随机一档补电，放久了会凉。
    /// </summary>
    public class DeliveryDropTests
    {
        private const float Tolerance = 0.01f;

        /// <summary>固定档位的骰子：Range 恒定返回同一个值。</summary>
        private sealed class FixedRandom : IRandomSource
        {
            private readonly int _value;

            public FixedRandom(int value)
            {
                _value = value;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                return _value;
            }
        }

        [Test]
        public void 一开始门口什么都没有()
        {
            var drop = new DeliveryDrop();

            Assert.IsFalse(drop.Available);
            Assert.AreEqual(0f, drop.RemainingSeconds, Tolerance);
            Assert.IsFalse(drop.TryPickup(), "没货的时候捡不到");
        }

        [Test]
        public void 落一份外卖有三档电量()
        {
            Assert.AreEqual(3, DeliveryDrop.Gains.Length);
            Assert.AreEqual(5f, DeliveryDrop.Gains[0], Tolerance);
            Assert.AreEqual(2f, DeliveryDrop.Gains[1], Tolerance);
            Assert.AreEqual(1f, DeliveryDrop.Gains[2], Tolerance);
        }

        [Test]
        public void 落下来时按骰子定这一份补多少()
        {
            var best = new DeliveryDrop();
            best.Drop(new FixedRandom(0));

            var worst = new DeliveryDrop();
            worst.Drop(new FixedRandom(2));

            Assert.IsTrue(best.Available);
            Assert.AreEqual(5f, best.Gain, Tolerance, "抽到第一档就是 5 点");
            Assert.AreEqual(1f, worst.Gain, Tolerance, "抽到最后一档就是 1 点");
            Assert.AreEqual(DeliveryDrop.LifetimeSeconds, best.RemainingSeconds, Tolerance);
        }

        [Test]
        public void 没货的时候电量为零()
        {
            var drop = new DeliveryDrop();
            drop.Drop(new FixedRandom(1));
            Assert.AreEqual(2f, drop.Gain, Tolerance);

            drop.Clear();

            Assert.AreEqual(0f, drop.Gain, Tolerance, "捡走/凉了以后不该还留着上一份的数值");
        }

        [Test]
        public void 放久了会自己凉掉()
        {
            var drop = new DeliveryDrop();
            drop.Drop();
            drop.Tick(DeliveryDrop.LifetimeSeconds - 1f);

            Assert.IsTrue(drop.Available, "还没到时间");

            drop.Tick(1f);

            Assert.IsFalse(drop.Available, "到时间就该消失");
            Assert.IsFalse(drop.TryPickup());
        }

        [Test]
        public void 捡一次就没了()
        {
            var drop = new DeliveryDrop();
            drop.Drop();

            Assert.IsTrue(drop.TryPickup());
            Assert.IsFalse(drop.Available);
            Assert.IsFalse(drop.TryPickup(), "捡过一次就拿不到第二次");
        }

        [Test]
        public void 再落一份只是刷新计时不会叠成两份()
        {
            var drop = new DeliveryDrop();
            drop.Drop();
            drop.Tick(10f);

            Assert.IsFalse(drop.Drop(), "第二份不算新的");
            Assert.AreEqual(0f, drop.Age, Tolerance, "计时刷新");
            Assert.IsTrue(drop.TryPickup());
            Assert.IsFalse(drop.TryPickup(), "只有一份，不可能连捡两次");
        }

        [Test]
        public void 换夜清场()
        {
            var drop = new DeliveryDrop();
            drop.Drop();
            drop.Clear();

            Assert.IsFalse(drop.Available);
        }
    }
}
