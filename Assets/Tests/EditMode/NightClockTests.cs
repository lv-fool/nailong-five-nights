using NailongFNAF.Core;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 夜晚时钟的行为规格。
    /// 节奏依据：FNAF 1 —— 每游戏小时 85 秒，一夜 510 秒。
    /// </summary>
    public class NightClockTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void 新建的时钟停在午夜零点()
        {
            var clock = new NightClock();

            Assert.AreEqual(0, clock.CurrentHour);
            Assert.AreEqual(0f, clock.NightProgress, Tolerance);
            Assert.IsFalse(clock.IsNightComplete);
        }

        [Test]
        public void 走满一个小时_时间来到一点()
        {
            var clock = new NightClock();

            clock.Tick(85f);

            Assert.AreEqual(1, clock.CurrentHour);
            Assert.IsFalse(clock.IsNightComplete);
        }

        [Test]
        public void 差一点点到一小时_还停在原地()
        {
            var clock = new NightClock();

            clock.Tick(84.9f);

            Assert.AreEqual(0, clock.CurrentHour);
        }

        [Test]
        public void 走满六个小时_天亮了()
        {
            var clock = new NightClock();

            clock.Tick(85f * 6f);

            Assert.AreEqual(6, clock.CurrentHour);
            Assert.IsTrue(clock.IsNightComplete);
            Assert.AreEqual(1f, clock.NightProgress, Tolerance);
        }

        [Test]
        public void 天亮之后继续推进_时间不再变化()
        {
            var clock = new NightClock();
            clock.Tick(85f * 6f);

            clock.Tick(1000f);

            Assert.AreEqual(6, clock.CurrentHour);
            Assert.AreEqual(85f * 6f, clock.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void 零点五小时后_进度是百分之五十()
        {
            var clock = new NightClock();

            clock.Tick(85f * 6f * 0.5f);

            Assert.AreEqual(0.5f, clock.NightProgress, Tolerance);
            Assert.AreEqual(3, clock.CurrentHour);
        }

        [Test]
        public void 重置后回到午夜()
        {
            var clock = new NightClock();
            clock.Tick(200f);

            clock.Reset();

            Assert.AreEqual(0, clock.CurrentHour);
            Assert.AreEqual(0f, clock.ElapsedSeconds, Tolerance);
            Assert.IsFalse(clock.IsNightComplete);
        }

        [Test]
        public void 传入负数时间不产生任何效果()
        {
            var clock = new NightClock();

            clock.Tick(-30f);

            Assert.AreEqual(0, clock.CurrentHour);
            Assert.AreEqual(0f, clock.ElapsedSeconds, Tolerance);
        }
    }
}
