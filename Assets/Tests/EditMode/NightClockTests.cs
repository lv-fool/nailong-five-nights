using NailongFNAF.Core;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 夜晚时钟的行为规格。
    /// 节奏依据：FNAF 1 —— 每游戏小时 85 秒，一夜 510 秒。
    ///
    /// ⚠️ **2026-11-03 起：普通夜从 3AM 开始**（NightClock.StartHour），
    /// 跳过 12AM~3AM。所以「新建的时钟」不再是 0 点，
    /// 这里所有断言都按 3 点起点重算过。
    /// 永夜模式仍从 0 起（它没有天亮）。
    /// </summary>
    public class NightClockTests
    {
        private const float Tolerance = 0.0001f;

        /// <summary>普通夜的起点小时。</summary>
        private const int StartHour = NightClock.StartHour;

        [Test]
        public void 新建的时钟从三点开始()
        {
            var clock = new NightClock();

            Assert.AreEqual(StartHour, clock.CurrentHour, "普通夜应当从 3AM 开始");
            Assert.AreEqual(StartHour * 85f, clock.ElapsedSeconds, Tolerance);
            Assert.AreEqual(StartHour / 6f, clock.NightProgress, Tolerance);
            Assert.IsFalse(clock.IsNightComplete);
        }

        [Test]
        public void 永夜模式仍从零点开始()
        {
            var clock = new NightClock(85f, true);

            Assert.AreEqual(0, clock.CurrentHour, "永夜没有天亮，不该跳过时长");
            Assert.AreEqual(0f, clock.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void 从三点走满一小时_来到四点()
        {
            var clock = new NightClock();

            clock.Tick(85f);

            Assert.AreEqual(StartHour + 1, clock.CurrentHour);
            Assert.IsFalse(clock.IsNightComplete);
        }

        [Test]
        public void 差一点点到下一个小时_还停在原地()
        {
            var clock = new NightClock();

            clock.Tick(84.9f);

            Assert.AreEqual(StartHour, clock.CurrentHour);
        }

        [Test]
        public void 走到六点_天亮了()
        {
            var clock = new NightClock();

            // 从 3 点到 6 点还差 3 小时
            clock.Tick(85f * (6f - StartHour));

            Assert.AreEqual(6, clock.CurrentHour);
            Assert.IsTrue(clock.IsNightComplete);
            Assert.AreEqual(1f, clock.NightProgress, Tolerance);
        }

        [Test]
        public void 天亮之后继续推进_时间不再变化()
        {
            var clock = new NightClock();
            clock.Tick(85f * (6f - StartHour));

            float atDawn = clock.ElapsedSeconds;
            clock.Tick(1000f);

            Assert.AreEqual(6, clock.CurrentHour);
            Assert.AreEqual(atDawn, clock.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void 从三点走一半到天亮_进度是百分之七十五()
        {
            var clock = new NightClock();

            // 剩余 3 小时，走一半 = 1.5 小时
            clock.Tick(85f * 1.5f);

            // (3 + 1.5) / 6 = 0.75
            Assert.AreEqual(0.75f, clock.NightProgress, Tolerance);
            Assert.AreEqual(4, clock.CurrentHour);
        }

        [Test]
        public void 重置后回到三点起点()
        {
            var clock = new NightClock();
            clock.Tick(200f);

            clock.Reset();

            Assert.AreEqual(StartHour, clock.CurrentHour, "重置应当回到 3AM 起点");
            Assert.AreEqual(StartHour * 85f, clock.ElapsedSeconds, Tolerance);
            Assert.IsFalse(clock.IsNightComplete);
        }

        [Test]
        public void 永夜模式重置回零点()
        {
            var clock = new NightClock(85f, true);
            clock.Tick(200f);

            clock.Reset();

            Assert.AreEqual(0, clock.CurrentHour);
            Assert.AreEqual(0f, clock.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void 传入负数时间不产生任何效果()
        {
            var clock = new NightClock();

            float before = clock.ElapsedSeconds;
            clock.Tick(-30f);

            Assert.AreEqual(StartHour, clock.CurrentHour);
            Assert.AreEqual(before, clock.ElapsedSeconds, Tolerance);
        }
    }
}
