using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 电量系统的行为规格。
    /// 数值依据：FNAF 1 —— 基础使用等级下，每 9.6 秒掉 1%。
    /// </summary>
    public class PowerSystemTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void 新建的电量系统是满的()
        {
            var power = new PowerSystem();

            Assert.AreEqual(100f, power.Level, Tolerance);
            Assert.AreEqual(1, power.UsageLevel);
            Assert.IsFalse(power.IsDepleted);
        }

        [Test]
        public void 什么都不开时_每九点六秒掉一个百分点()
        {
            var power = new PowerSystem();

            power.Tick(9.6f);

            Assert.AreEqual(99f, power.Level, Tolerance);
        }

        [Test]
        public void 使用等级越高_耗电越快()
        {
            var idle = new PowerSystem();
            var busy = new PowerSystem();
            busy.SetUsageLevel(3);

            idle.Tick(9.6f);
            busy.Tick(9.6f);

            Assert.AreEqual(99f, idle.Level, Tolerance);
            Assert.AreEqual(97f, busy.Level, Tolerance);
        }

        [Test]
        public void 使用等级被夹在1到5之间()
        {
            var power = new PowerSystem();

            power.SetUsageLevel(0);
            Assert.AreEqual(1, power.UsageLevel);

            power.SetUsageLevel(99);
            Assert.AreEqual(5, power.UsageLevel);
        }

        [Test]
        public void 电量耗尽后判定为断电且不会变成负数()
        {
            var power = new PowerSystem(0.5f);
            power.SetUsageLevel(5);

            power.Tick(60f);

            Assert.AreEqual(0f, power.Level, Tolerance);
            Assert.IsTrue(power.IsDepleted);
        }

        [Test]
        public void 断电之后继续推进时间_电量不再变化()
        {
            var power = new PowerSystem(0f);

            power.Tick(100f);

            Assert.AreEqual(0f, power.Level, Tolerance);
            Assert.IsTrue(power.IsDepleted);
        }

        [Test]
        public void 传入负数时间不产生任何效果()
        {
            var power = new PowerSystem();

            power.Tick(-5f);

            Assert.AreEqual(100f, power.Level, Tolerance);
        }

        [Test]
        public void 舍曲林把电量上限抬到两百()
        {
            PowerSystem power = new PowerSystem(99f);
            Assert.AreEqual(100f, power.MaxLevel, Tolerance, "默认上限还是一百");

            power.MultiplyMaxLevel(2f);
            Assert.AreEqual(200f, power.MaxLevel, Tolerance);

            power.Grant(500f);
            Assert.AreEqual(200f, power.Level, Tolerance, "上限抬了之后能补到两百");
        }

        [Test]
        public void 上限调低时当前电量跟着夹回来()
        {
            PowerSystem power = new PowerSystem(99f);
            power.MultiplyMaxLevel(2f);
            power.Grant(500f);
            Assert.AreEqual(200f, power.Level, Tolerance);

            power.SetMaxLevel(130f);
            Assert.AreEqual(130f, power.MaxLevel, Tolerance);
            Assert.AreEqual(130f, power.Level, Tolerance);

            power.SetMaxLevel(0f);
            Assert.AreEqual(130f, power.MaxLevel, Tolerance, "非正数的上限要忽略");
        }
    }
}
