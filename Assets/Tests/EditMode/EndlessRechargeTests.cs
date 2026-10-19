using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 永夜模式「固定周期结算 + 行动换电」的规格。
    ///
    /// 老板原话（2026-10-19 改版）：补给改成**固定时间结算**；行动会让难度变大，
    /// 但行动也给更多电；「增加递减」的模式要保留。这个名字玩家看到的是「永夜模式」，
    /// 代码里的 Endless* 是历史代号。
    /// </summary>
    public class EndlessRechargeTests
    {
        private const float Tolerance = 0.0001f;

        /// <summary>把周期调大的账本：谁也结算不了，只看记账本身。</summary>
        private static EndlessRecharge MakeLedger()
        {
            return new EndlessRecharge(1000f, 4f, 0.75f, 0.9f, 1f, 24f, 0.5f);
        }

        // ---------------- 固定周期结算 ----------------

        [Test]
        public void 时间没到就不补电()
        {
            var recharge = new EndlessRecharge();
            recharge.RegisterAction(PlayerAction.Door);
            recharge.RegisterAction(PlayerAction.Light);

            float grant = recharge.Tick(recharge.Interval * 0.9f);

            Assert.AreEqual(0f, grant, Tolerance);
            Assert.AreEqual(0, recharge.Settlements);
            Assert.AreEqual(0f, recharge.TotalGranted, Tolerance);
        }

        [Test]
        public void 时间一到就结算一次并把本周期账清零()
        {
            var recharge = new EndlessRecharge();
            recharge.RegisterAction(PlayerAction.Door);

            float grant = recharge.Tick(recharge.Interval);

            Assert.Greater(grant, 0f);
            Assert.AreEqual(1, recharge.Settlements);
            Assert.AreEqual(0f, recharge.Credits, Tolerance);
        }

        [Test]
        public void 一口气走过两个周期会结算两次()
        {
            var recharge = new EndlessRecharge();

            float grant = recharge.Tick(recharge.Interval * 2f + 1f);

            Assert.AreEqual(2, recharge.Settlements);
            Assert.Greater(grant, 0f);
        }

        [Test]
        public void 同一个周期里动作越多补得越多()
        {
            var lazy = new EndlessRecharge();
            var busy = new EndlessRecharge();

            lazy.RegisterAction(PlayerAction.Camera);
            for (int i = 0; i < 8; i++)
            {
                busy.RegisterAction(PlayerAction.Camera);
            }

            float lazyGain = lazy.Tick(lazy.Interval);
            float busyGain = busy.Tick(busy.Interval);

            Assert.Greater(busyGain, lazyGain, "动作多的那一个周期应该补得更多");
        }

        [Test]
        public void 第几次结算比上一次少()
        {
            var recharge = new EndlessRecharge();
            float previous = float.MaxValue;

            for (int i = 0; i < 10; i++)
            {
                float gain = recharge.GainForSettlement(i);
                Assert.Less(gain, previous, "第 " + (i + 1) + " 次补给应该比上一次少");
                previous = gain;
            }
        }

        [Test]
        public void 结算不会低于下限()
        {
            var recharge = new EndlessRecharge();

            Assert.AreEqual(EndlessRecharge.DefaultMinGain, recharge.GainForSettlement(120), Tolerance);
        }

        [Test]
        public void 结算不会高于上限()
        {
            var recharge = new EndlessRecharge();
            for (int i = 0; i < 200; i++)
            {
                recharge.RegisterAction(PlayerAction.Door);
            }

            Assert.LessOrEqual(recharge.GainForSettlement(0), EndlessRecharge.DefaultMaxGain);
        }

        [Test]
        public void 同一周期里同类操作越点越不值钱()
        {
            var recharge = MakeLedger();

            float first = recharge.RegisterAction(PlayerAction.Door);
            float second = recharge.RegisterAction(PlayerAction.Door);
            float third = recharge.RegisterAction(PlayerAction.Door);

            Assert.Greater(first, second);
            Assert.Greater(second, third);
        }

        [Test]
        public void 换着花样操作比死点同一类更值钱()
        {
            var repeated = MakeLedger();
            var mixed = MakeLedger();

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

        // ---------------- 动静（难度输入） ----------------

        [Test]
        public void 动作会攒动静且门最吵()
        {
            var door = MakeLedger();
            var camera = MakeLedger();

            door.RegisterAction(PlayerAction.Door);
            camera.RegisterAction(PlayerAction.Camera);

            Assert.Greater(door.Noise, 0f);
            Assert.Greater(door.Noise, camera.Noise);
        }

        [Test]
        public void 不动手的话动静会慢慢散掉()
        {
            var recharge = MakeLedger();
            recharge.RegisterAction(PlayerAction.Door);
            float afterAction = recharge.Noise;

            recharge.Tick(6f);

            Assert.Less(recharge.Noise, afterAction);
            Assert.GreaterOrEqual(recharge.Noise, 0f);
        }

        // ---------------- 读数自洽 ----------------

        [Test]
        public void 距离下次结算的秒数和进度是自洽的()
        {
            var recharge = new EndlessRecharge();

            recharge.Tick(recharge.Interval * 0.25f);

            Assert.AreEqual(recharge.Interval * 0.75f, recharge.SecondsToNext, 0.001f);
            Assert.AreEqual(0.25f, recharge.ProgressToNext, 0.001f);
        }

        [Test]
        public void 预计补给量和实际结算量一致()
        {
            var recharge = new EndlessRecharge();
            recharge.RegisterAction(PlayerAction.Light);
            recharge.RegisterAction(PlayerAction.Camera);

            float predicted = recharge.NextGain;
            float actual = recharge.Tick(recharge.Interval);

            Assert.AreEqual(predicted, actual, 0.001f);
        }

        [Test]
        public void 重置之后账本归零()
        {
            var recharge = new EndlessRecharge();
            recharge.RegisterAction(PlayerAction.Door);
            recharge.Tick(recharge.Interval);

            recharge.Reset();

            Assert.AreEqual(0f, recharge.Credits, Tolerance);
            Assert.AreEqual(0f, recharge.Noise, Tolerance);
            Assert.AreEqual(0f, recharge.TotalGranted, Tolerance);
            Assert.AreEqual(0f, recharge.SecondsIntoPeriod, Tolerance);
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

        // ---------------- 永夜的时钟 ----------------

        [Test]
        public void 永夜模式时钟一直往上走且永远不会天亮()
        {
            var clock = new NightClock(10f, true);

            clock.Tick(95f);

            Assert.AreEqual(9, clock.CurrentHour);
            Assert.IsFalse(clock.IsNightComplete);
            Assert.IsTrue(clock.Endless);
        }

        [Test]
        public void 永夜模式的夜数和点数会自己循环()
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
