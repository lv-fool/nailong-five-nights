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
        public void 三档的权重是一点七成两点一成半五点一成半()
        {
            // 老板 2026-11-03 定：1 电 70%、2 电 15%、5 电 15%
            Assert.AreEqual(3, DeliveryDrop.GainWeights.Length,
                "权重表必须和 Gains 一一对应，多一项或少一项都不行");

            Assert.AreEqual(70, DeliveryDrop.GainWeights[2], "1 点（Gains[2]）占 70%");
            Assert.AreEqual(15, DeliveryDrop.GainWeights[1], "2 点（Gains[1]）占 15%");
            Assert.AreEqual(15, DeliveryDrop.GainWeights[0], "5 点（Gains[0]）占 15%");

            int total = 0;
            foreach (int w in DeliveryDrop.GainWeights)
            {
                total += w;
            }

            Assert.AreEqual(100, total, "权重加起来正好 100，概率才好对");
        }

        /// <summary>按权重区间带回真实骰子的映射：掷 0~99 看落到哪一档。</summary>
        private static float GainForRoll(int roll)
        {
            var drop = new DeliveryDrop();
            drop.Drop(new FixedRandom(roll));
            return drop.Gain;
        }

        [Test]
        public void 掷点落在前十五命中五点()
        {
            // 累计区间：5 点占 [0,15)、2 点占 [15,30)、1 点占 [30,100)
            Assert.AreEqual(5f, GainForRoll(0), Tolerance, "区间起点");
            Assert.AreEqual(5f, GainForRoll(14), Tolerance, "区间最后一点");
        }

        [Test]
        public void 掷点落在次十五命中两点()
        {
            Assert.AreEqual(2f, GainForRoll(15), Tolerance, "2 点区间起点");
            Assert.AreEqual(2f, GainForRoll(29), Tolerance, "2 点区间最后一点");
        }

        [Test]
        public void 掷点落在后七十命中一点()
        {
            Assert.AreEqual(1f, GainForRoll(30), Tolerance, "1 点区间起点");
            Assert.AreEqual(1f, GainForRoll(99), Tolerance, "区间最后一点");
        }

        [Test]
        public void 一点是压倒性多数()
        {
            // 拿真骰子跑一批，统计各档占比 —— 这是「加权真的生效」的端到端证据。
            // 用固定种子的 SystemRandomSource，结果稳定，不会偶发失败。
            const int draws = 20000;
            var rng = new SystemRandomSource(20261103);
            var drop = new DeliveryDrop();

            int one = 0;
            int two = 0;
            int five = 0;

            for (int i = 0; i < draws; i++)
            {
                drop.Drop(rng);

                if (drop.Gain < 1.5f)
                {
                    one++;
                }
                else if (drop.Gain < 3.5f)
                {
                    two++;
                }
                else
                {
                    five++;
                }
            }

            // 允许 ±3 个百分点的统计抖动
            Assert.That(one / (double)draws, Is.EqualTo(0.70).Within(0.03),
                "1 电应当约占 70%，实际 " + one);
            Assert.That(two / (double)draws, Is.EqualTo(0.15).Within(0.03),
                "2 电应当约占 15%，实际 " + two);
            Assert.That(five / (double)draws, Is.EqualTo(0.15).Within(0.03),
                "5 电应当约占 15%，实际 " + five);
        }

        [Test]
        public void 骰子返回越界值也不会崩()
        {
            // 防御性：FixedRandom 不夹范围，喂个离谱的值看看
            var drop = new DeliveryDrop();
            drop.Drop(new FixedRandom(999));

            Assert.IsTrue(drop.Available, "越界骰子不该让外卖直接消失");

            bool knownGain = false;
            foreach (float g in DeliveryDrop.Gains)
            {
                if (System.Math.Abs(drop.Gain - g) < Tolerance)
                {
                    knownGain = true;
                }
            }

            Assert.IsTrue(knownGain, "越界时也该落回某一档，实际 " + drop.Gain);
        }

        [Test]
        public void 落下来时按骰子定这一份补多少()
        {
            // 累计区间：[0,15)=5 点、[15,30)=2 点、[30,100)=1 点
            var best = new DeliveryDrop();
            best.Drop(new FixedRandom(0));

            var mid = new DeliveryDrop();
            mid.Drop(new FixedRandom(15));

            var worst = new DeliveryDrop();
            worst.Drop(new FixedRandom(99));

            Assert.IsTrue(best.Available);
            Assert.AreEqual(5f, best.Gain, Tolerance, "掷到最前面 15 分之一就是 5 点");
            Assert.AreEqual(2f, mid.Gain, Tolerance, "掷到中间那一档是 2 点");
            Assert.AreEqual(1f, worst.Gain, Tolerance, "掷到绝大多数那一段是 1 点");
            Assert.AreEqual(DeliveryDrop.LifetimeSeconds, best.RemainingSeconds, Tolerance);
        }

        [Test]
        public void 没货的时候电量为零()
        {
            var drop = new DeliveryDrop();
            drop.Drop(new FixedRandom(15));
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
