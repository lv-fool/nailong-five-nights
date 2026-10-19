using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 永夜模式存档的规格：一行文本写出去、读回来，坏数据要当成「没有存档」。
    /// 存的是时间 / 电量 / 账本；不存敌人状态（读档后敌人从当前难度重新开始）。
    /// </summary>
    public class EternalNightSaveTests
    {
        private const float Tolerance = 0.0001f;

        private static EternalNightSave MakeSave()
        {
            return new EternalNightSave
            {
                ElapsedSeconds = 123.5f,
                Power = 42.25f,
                Settlements = 7,
                Credits = 3.5f,
                Noise = 12.75f,
                SecondsIntoPeriod = 19f,
                TotalGranted = 61.5f,
                ActionsRegistered = 96,
                Hour = 1,
            };
        }

        [Test]
        public void 存进去再读回来数值不变()
        {
            var original = MakeSave();

            EternalNightSave parsed;
            bool ok = EternalNightSave.TryParse(original.ToStorage(), out parsed);

            Assert.IsTrue(ok);
            Assert.AreEqual(original.ElapsedSeconds, parsed.ElapsedSeconds, Tolerance);
            Assert.AreEqual(original.Power, parsed.Power, Tolerance);
            Assert.AreEqual(original.Settlements, parsed.Settlements);
            Assert.AreEqual(original.Credits, parsed.Credits, Tolerance);
            Assert.AreEqual(original.Noise, parsed.Noise, Tolerance);
            Assert.AreEqual(original.SecondsIntoPeriod, parsed.SecondsIntoPeriod, Tolerance);
            Assert.AreEqual(original.TotalGranted, parsed.TotalGranted, Tolerance);
            Assert.AreEqual(original.ActionsRegistered, parsed.ActionsRegistered);
            Assert.AreEqual(original.Hour, parsed.Hour);
        }

        [Test]
        public void 空字符串不算存档()
        {
            EternalNightSave parsed;
            Assert.IsFalse(EternalNightSave.TryParse(string.Empty, out parsed));
            Assert.IsNull(parsed);
        }

        [Test]
        public void 版本不对的存档要丢掉()
        {
            string raw = MakeSave().ToStorage().Replace(EternalNightSave.Version, "v0");

            EternalNightSave parsed;
            Assert.IsFalse(EternalNightSave.TryParse(raw, out parsed));
        }

        [Test]
        public void 字段缺一块的存档要丢掉()
        {
            string raw = "v1|1|2|3";

            EternalNightSave parsed;
            Assert.IsFalse(EternalNightSave.TryParse(raw, out parsed));
        }

        [Test]
        public void 数字写成乱码的存档要丢掉()
        {
            string raw = "v1|abc|50|0|0|0|0|0|0|0";

            EternalNightSave parsed;
            Assert.IsFalse(EternalNightSave.TryParse(raw, out parsed));
        }

        [Test]
        public void 电量越界的存档要丢掉()
        {
            EternalNightSave parsed;
            Assert.IsFalse(EternalNightSave.TryParse("v1|10|150|0|0|0|0|0|0|0", out parsed));
            Assert.IsFalse(EternalNightSave.TryParse("v1|10|-1|0|0|0|0|0|0|0", out parsed));
        }

        [Test]
        public void 负数时间的存档要丢掉()
        {
            EternalNightSave parsed;
            Assert.IsFalse(EternalNightSave.TryParse("v1|-5|50|0|0|0|0|0|0|0", out parsed));
        }

        // ---------------- 读档要写进去的那两处接口 ----------------

        [Test]
        public void 时钟可以拨回到指定秒数()
        {
            var clock = new NightClock(10f, true);

            clock.RestoreElapsed(72f);

            Assert.AreEqual(72f, clock.ElapsedSeconds, Tolerance);
            Assert.AreEqual(7, clock.CurrentHour);
        }

        [Test]
        public void 账本可以整体恢复()
        {
            var recharge = new EndlessRecharge();

            recharge.Restore(4.5f, 9f, 3, 20f, 33f, 41);

            Assert.AreEqual(4.5f, recharge.Credits, Tolerance);
            Assert.AreEqual(9f, recharge.Noise, Tolerance);
            Assert.AreEqual(3, recharge.Settlements);
            Assert.AreEqual(20f, recharge.SecondsIntoPeriod, Tolerance);
            Assert.AreEqual(33f, recharge.TotalGranted, Tolerance);
            Assert.AreEqual(41, recharge.ActionsRegistered);
        }

        [Test]
        public void 恢复时周期进度不会越过结算线()
        {
            var recharge = new EndlessRecharge();

            recharge.Restore(0f, 0f, 0, recharge.Interval + 5f, 0f, 0);

            Assert.Less(recharge.SecondsIntoPeriod, recharge.Interval);
        }
    }
}
