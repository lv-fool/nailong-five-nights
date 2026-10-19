using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 无尽模式「行动换电」的规格。
    /// 老板原话：不以时间为标准补电，按玩家在时间里的行动补电；
    /// 干得越多补得越多，但补给必须是递减的。
    /// </summary>
    public class EndlessRechargeTests
    {
        private const float Tolerance = 0.0001f;

        private static EndlessRecharge MakeRecharge()
        {
            return new EndlessRecharge();
        }

        /// <summary>一直做同一种动作，直到攒出 count 次结算（或做满 limit 次）。</summary>
        private static float DrainActions(EndlessRecharge recharge, PlayerAction action, int count, int limit = 500)
        {
            float gained = 0f;
            int settlements = 0;
            for (int i = 0; i < limit && settlements < count; i++)
            {
                float gain = recharge.RegisterAction(action);
                if (gain > 0f)
                {
                    gained += gain;
                    settlements++;
                }
            }

            return gained;
        }

        [Test]
        public void 不登记任何操作就不补电()
        {
            var recharge = MakeRecharge();

            Assert.AreEqual(0f, recharge.TotalGranted, Tolerance);
            Assert.AreEqual(0, recharge.Settlements);
            Assert.AreEqual(0f, recharge.Credits, Tolerance);
        }

        [Test]
        public void 攒够记点后结算一次并把记点清零()
        {
            var recharge = new EndlessRecharge(2f, 8f, 0.82f, 1.5f, 20f, 0.5f);

            float first = recharge.RegisterAction(PlayerAction.Door);
            Assert.AreEqual(0f, first, Tolerance, "1 点还不够 2 点的门槛");
            Assert.AreEqual(0, recharge.Settlements);

            // 第二次关门：权重 1.0 / (1 + 1*0.5) = 0.667，累计 1.667 还不够
            float second = recharge.RegisterAction(PlayerAction.Door);
            Assert.AreEqual(0f, second, Tolerance);

            // 再来几次总能凑够；一旦结算，记点必须清零
            for (int i = 0; i < 20 && recharge.Settlements == 0; i++)
            {
                recharge.RegisterAction(PlayerAction.Door);
            }

            Assert.AreEqual(1, recharge.Settlements);
            Assert.AreEqual(0f, recharge.Credits, Tolerance);
        }

        [Test]
        public void 动作越多累计补电越多()
        {
            var few = MakeRecharge();
            var many = MakeRecharge();

            DrainActions(few, PlayerAction.Camera, 2);
            DrainActions(many, PlayerAction.Camera, 6);

            Assert.Greater(many.TotalGranted, few.TotalGranted);
            Assert.Greater(many.Settlements, few.Settlements);
        }

        [Test]
        public void 每次结算补的电都比上一次少()
        {
            var recharge = MakeRecharge();
            float previous = float.MaxValue;

            for (int i = 0; i < 8; i++)
            {
                float gain = recharge.GainForSettlement(i);
                Assert.Less(gain, previous, "第 " + (i + 1) + " 次补给应该比上一次少");
                previous = gain;
            }
        }

        [Test]
        public void 结算给的电不会低于下限()
        {
            var recharge = MakeRecharge();

            float deep = recharge.GainForSettlement(80);

            Assert.AreEqual(EndlessRecharge.DefaultMinGain, deep, Tolerance);
        }

        [Test]
        public void 同类操作越做越不值钱()
        {
            var recharge = new EndlessRecharge(1000f, 8f, 0.82f, 1.5f, 20f, 0.5f);

            // 门槛调到 1000，谁也结算不了，只看记点的边际收益
            recharge.RegisterAction(PlayerAction.Door);
            float afterFirst = recharge.Credits;

            recharge.RegisterAction(PlayerAction.Door);
            float firstStep = recharge.Credits - afterFirst;

            recharge.RegisterAction(PlayerAction.Door);
            float secondStep = recharge.Credits - afterFirst - firstStep;

            Assert.Greater(afterFirst, 0f);
            Assert.Less(firstStep, afterFirst);
            Assert.Less(secondStep, firstStep);
        }

        [Test]
        public void 换着花样操作比死点同一类更值钱()
        {
            var repeated = new EndlessRecharge(1000f, 8f, 0.82f, 1.5f, 20f, 0.5f);
            var mixed = new EndlessRecharge(1000f, 8f, 0.82f, 1.5f, 20f, 0.5f);

            for (int i = 0; i < 4; i++)
            {
                repeated.RegisterAction(PlayerAction.Camera);
            }

            mixed.RegisterAction(PlayerAction.Door);
            mixed.RegisterAction(PlayerAction.Light);
            mixed.RegisterAction(PlayerAction.Camera);
            mixed.RegisterAction(PlayerAction.Flashlight);

            Assert.Greater(mixed.Credits, repeated.Credits);
        }

        [Test]
        public void 离下次结算的差额和进度是自洽的()
        {
            var recharge = MakeRecharge();

            recharge.RegisterAction(PlayerAction.Light);

            Assert.Greater(recharge.CreditsToNext, 0f);
            Assert.Less(recharge.ProgressToNext, 1f);
            Assert.AreEqual(recharge.Threshold, recharge.Credits + recharge.CreditsToNext, Tolerance);
        }

        [Test]
        public void 重置之后账本归零()
        {
            var recharge = MakeRecharge();
            DrainActions(recharge, PlayerAction.Door, 2);

            recharge.Reset();

            Assert.AreEqual(0f, recharge.TotalGranted, Tolerance);
            Assert.AreEqual(0f, recharge.Credits, Tolerance);
            Assert.AreEqual(0, recharge.Settlements);
            Assert.AreEqual(0, recharge.ActionsRegistered);
        }

        // ---------------- 电量的补给接口 ----------------

        [Test]
        public void 补电会被上限截断并返回实际补进去的量()
        {
            var power = new PowerSystem(98f);

            float granted = power.Grant(8f);

            Assert.AreEqual(100f, power.Level, Tolerance);
            Assert.AreEqual(2f, granted, Tolerance);
        }

        [Test]
        public void 断电之后不再补电()
        {
            var power = new PowerSystem(0f);

            float granted = power.Grant(8f);

            Assert.AreEqual(0f, granted, Tolerance);
            Assert.IsTrue(power.IsDepleted);
        }

        // ---------------- 无尽时钟 ----------------

        [Test]
        public void 无尽模式时钟一直往上走且永远不会天亮()
        {
            var clock = new NightClock(10f, true);

            clock.Tick(95f);

            Assert.AreEqual(9, clock.CurrentHour);
            Assert.IsFalse(clock.IsNightComplete);
            Assert.IsTrue(clock.Endless);
        }

        [Test]
        public void 无尽模式的夜数和点数会自己循环()
        {
            var clock = new NightClock(10f, true);

            clock.Tick(75f); // 7.5 小时 → 第 7 点 = 第 2 夜（0 起）的 1 点

            Assert.AreEqual(7, clock.CurrentHour);
            Assert.AreEqual(1, clock.NightNumber);
            Assert.AreEqual(1, clock.HourOfNight);
        }

        [Test]
        public void 普通时钟到六点就停()
        {
            var clock = new NightClock(10f);

            clock.Tick(500f);

            Assert.AreEqual(NightClock.HoursPerNight, clock.CurrentHour);
            Assert.IsTrue(clock.IsNightComplete);
            Assert.IsFalse(clock.Endless);
        }
    }
}
