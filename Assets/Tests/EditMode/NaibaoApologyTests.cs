using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 「这口锅是不是奶包的」（<see cref="NaibaoApology"/>）。
    ///
    /// 她本人不杀人，所以「因为她死」只能是归因：她刚动过设备、玩家又在窗口内死掉。
    /// 这些用例钉的就是窗口的边界 —— 改窗口就得改这里，别让手感悄悄变了。
    /// </summary>
    public class NaibaoApologyTests
    {
        [Test]
        public void 没动过设备时不背锅()
        {
            var apology = new NaibaoApology();

            Assert.IsFalse(apology.Culpable);
            Assert.IsNull(apology.CulpableLine());
        }

        [Test]
        public void 抢监控之后马上死算她头上()
        {
            var apology = new NaibaoApology();
            apology.Note(NaibaoAction.MonitorGrab);

            Assert.IsTrue(apology.Culpable);
            Assert.AreEqual(NaibaoFault.MonitorGrab, apology.Fault);
            Assert.AreEqual(NaibaoApology.LineFor(NaibaoFault.MonitorGrab), apology.CulpableLine());
        }

        [Test]
        public void 抢监控过去五秒就不算了()
        {
            var apology = new NaibaoApology();
            apology.Note(NaibaoAction.MonitorGrab);

            apology.Tick(NaibaoApology.MonitorWindow - 0.1f);
            Assert.IsTrue(apology.Culpable);

            apology.Tick(0.2f);
            Assert.IsFalse(apology.Culpable);
            Assert.AreEqual(NaibaoFault.None, apology.Fault);
            Assert.IsNull(apology.CulpableLine());
        }

        [Test]
        public void 纯嘲讽不背锅()
        {
            var apology = new NaibaoApology();
            apology.Note(NaibaoAction.Line);

            Assert.IsFalse(apology.Culpable);
            Assert.AreEqual(NaibaoFault.None, apology.Fault);
        }

        [Test]
        public void 关灯与偷加耗电各用各的窗口()
        {
            var light = new NaibaoApology();
            light.Note(NaibaoAction.LightOff);
            light.Tick(NaibaoApology.LightWindow - 0.5f);
            Assert.IsTrue(light.Culpable);
            Assert.AreEqual(NaibaoApology.LineFor(NaibaoFault.LightOff), light.CulpableLine());

            var usage = new NaibaoApology();
            usage.Note(NaibaoAction.UsageSpike);
            usage.Tick(NaibaoApology.LightWindow + 1f);
            Assert.IsTrue(usage.Culpable, "断电到跳杀中间还有十几秒，耗电的窗口要更长");

            usage.Tick(NaibaoApology.UsageWindow);
            Assert.IsFalse(usage.Culpable);
        }

        [Test]
        public void 后一次动作覆盖前一次的锅()
        {
            var apology = new NaibaoApology();

            apology.Note(NaibaoAction.LightOff);
            apology.Tick(1f);
            apology.Note(NaibaoAction.MonitorGrab);

            Assert.AreEqual(NaibaoFault.MonitorGrab, apology.Fault);
            Assert.AreEqual(0f, apology.Age);
        }

        [Test]
        public void 清空之后回到不背锅()
        {
            var apology = new NaibaoApology();
            apology.Note(NaibaoAction.UsageSpike);

            apology.Clear();

            Assert.IsFalse(apology.Culpable);
            Assert.AreEqual(NaibaoFault.None, apology.Fault);
        }

        [Test]
        public void 每一种设备故障都有道歉词()
        {
            Assert.IsNotNull(NaibaoApology.LineFor(NaibaoFault.MonitorGrab));
            Assert.IsNotNull(NaibaoApology.LineFor(NaibaoFault.LightOff));
            Assert.IsNotNull(NaibaoApology.LineFor(NaibaoFault.UsageSpike));
            Assert.IsNull(NaibaoApology.LineFor(NaibaoFault.None));
        }

        [Test]
        public void 动作映射到故障()
        {
            Assert.AreEqual(NaibaoFault.MonitorGrab, NaibaoApology.FaultOf(NaibaoAction.MonitorGrab));
            Assert.AreEqual(NaibaoFault.LightOff, NaibaoApology.FaultOf(NaibaoAction.LightOff));
            Assert.AreEqual(NaibaoFault.UsageSpike, NaibaoApology.FaultOf(NaibaoAction.UsageSpike));
            Assert.AreEqual(NaibaoFault.None, NaibaoApology.FaultOf(NaibaoAction.Line));
            Assert.AreEqual(NaibaoFault.None, NaibaoApology.FaultOf(NaibaoAction.None));
        }
    }
}
