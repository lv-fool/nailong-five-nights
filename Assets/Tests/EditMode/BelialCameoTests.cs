using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 故事模式里的贝利亚彩蛋（2026-10-30 定）：
    /// 每 5 秒掷一次 0.05% 的骰；掷中之后挂起，等玩家下一次开监控；
    /// 真撞上时 1.5 秒内必须放下监控，否则他动手。
    /// </summary>
    public class BelialCameoTests
    {
        private const float Tolerance = 0.01f;

        /// <summary>永远掷中（Range 返回 0 = 命中）。</summary>
        private sealed class AlwaysHit : IRandomSource
        {
            public int Range(int minInclusive, int maxExclusive)
            {
                return minInclusive;
            }
        }

        /// <summary>永远掷不中。</summary>
        private sealed class NeverHit : IRandomSource
        {
            public int Range(int minInclusive, int maxExclusive)
            {
                return maxExclusive - 1;
            }
        }

        /// <summary>记下每次掷骰的上界，用来核对 1/2000。</summary>
        private sealed class SpyRandom : IRandomSource
        {
            public int LastMax = -1;
            public int Calls;

            public int Range(int minInclusive, int maxExclusive)
            {
                LastMax = maxExclusive;
                Calls++;
                return maxExclusive - 1;
            }
        }

        private static BelialCameo Make(IRandomSource random)
        {
            return new BelialCameo(random);
        }

        /// <summary>推到「掷中、挂起」的状态。</summary>
        private static BelialCameo Armed(IRandomSource random)
        {
            BelialCameo cameo = Make(random);
            cameo.Tick(BelialCameo.RollIntervalSeconds + 0.01f, true, false);
            Assert.IsTrue(cameo.Pending, "应该已经掷中了");
            return cameo;
        }

        // ---------------------------------------------------------------
        // 夜数与模式
        // ---------------------------------------------------------------

        [Test]
        public void 只有第四第五夜有彩蛋()
        {
            Assert.IsFalse(BelialCameo.NightHasCameo(0), "第 1 夜");
            Assert.IsFalse(BelialCameo.NightHasCameo(1), "第 2 夜");
            Assert.IsFalse(BelialCameo.NightHasCameo(2), "第 3 夜");
            Assert.IsTrue(BelialCameo.NightHasCameo(3), "第 4 夜");
            Assert.IsTrue(BelialCameo.NightHasCameo(4), "第 5 夜");
        }

        [Test]
        public void 故事模式之外不参与掷骰()
        {
            var spy = new SpyRandom();
            BelialCameo cameo = Make(spy);

            for (int i = 0; i < 600; i++)
            {
                cameo.Tick(0.1f, false, false);
            }

            Assert.AreEqual(0, spy.Calls, "不该掷骰");
            Assert.IsFalse(cameo.Pending);
        }

        // ---------------------------------------------------------------
        // 掷骰节奏
        // ---------------------------------------------------------------

        [Test]
        public void 每五秒才掷一次()
        {
            var spy = new SpyRandom();
            BelialCameo cameo = Make(spy);

            // 4.9 秒 —— 还不到点
            cameo.Tick(4.9f, true, false);
            Assert.AreEqual(0, spy.Calls, "不到 5 秒不该掷");

            // 再走 0.2 秒 —— 过线，掷一次
            cameo.Tick(0.2f, true, false);
            Assert.AreEqual(1, spy.Calls);

            // 又攒满 5 秒 —— 第二次
            cameo.Tick(5f, true, false);
            Assert.AreEqual(2, spy.Calls);
        }

        [Test]
        public void 一次长帧只结算一次骰子()
        {
            // 时间累加用减法而不是清零，但一帧里只掷一次 ——
            // 卡顿一大跳不会连掷好几把
            var spy = new SpyRandom();
            BelialCameo cameo = Make(spy);

            cameo.Tick(60f, true, false);
            Assert.AreEqual(1, spy.Calls);
        }

        [Test]
        public void 骰子是一比两千()
        {
            var spy = new SpyRandom();
            BelialCameo cameo = Make(spy);

            cameo.Tick(5.01f, true, false);

            Assert.AreEqual(BelialCameo.RollOdds, spy.LastMax, "上界就是概率的分母");
            Assert.AreEqual(2000, BelialCameo.RollOdds);
            Assert.AreEqual(0.0005f, BelialCameo.TriggerChance, 0.000001f, "0.05%");
        }

        [Test]
        public void 没掷中就一直待在平时状态()
        {
            BelialCameo cameo = Make(new NeverHit());

            for (int i = 0; i < 1000; i++)
            {
                cameo.Tick(1f, true, false);
            }

            Assert.IsFalse(cameo.Pending);
            Assert.IsFalse(cameo.OnScreen);
        }

        // ---------------------------------------------------------------
        // 挂起 → 出现在下一次监控上
        // ---------------------------------------------------------------

        [Test]
        public void 掷中之后先挂起而不是立刻发作()
        {
            BelialCameo cameo = Armed(new AlwaysHit());

            Assert.IsTrue(cameo.Pending);
            Assert.IsFalse(cameo.OnScreen, "还没开监控，屏幕上不该有他");
        }

        [Test]
        public void 挂起时开监控才真撞上()
        {
            BelialCameo cameo = Armed(new AlwaysHit());

            cameo.Tick(0.1f, true, true);

            Assert.IsFalse(cameo.Pending);
            Assert.IsTrue(cameo.OnScreen);
            Assert.AreEqual(BelialCameo.ReactionSeconds, cameo.ReactionLeft, Tolerance);
            Assert.AreEqual(1, cameo.Triggers);
        }

        [Test]
        public void 不开监控就永远撞不上()
        {
            BelialCameo cameo = Armed(new AlwaysHit());

            for (int i = 0; i < 1000; i++)
            {
                cameo.Tick(0.1f, true, false);
            }

            Assert.IsTrue(cameo.Pending);
            Assert.IsFalse(cameo.OnScreen);
            Assert.IsFalse(cameo.ConsumeExpired(), "不开监控不该死人");
        }

        [Test]
        public void 挂起时即使不再符合条件也还等着()
        {
            // 玩家死了 / 换夜之前已经掷中，这个「等着」不该被 eligible 抹掉
            BelialCameo cameo = Armed(new AlwaysHit());

            cameo.Tick(0.1f, false, false);
            Assert.IsTrue(cameo.Pending);

            cameo.Tick(0.1f, false, true);
            Assert.IsTrue(cameo.OnScreen, "开监控照样撞上");
        }

        // ---------------------------------------------------------------
        // 1.5 秒：放下监控 vs 超时
        // ---------------------------------------------------------------

        [Test]
        public void 一秒半内放下监控就躲过去()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);

            cameo.Tick(1.0f, true, true);
            Assert.IsTrue(cameo.OnScreen, "还不到 1.5 秒");

            cameo.Tick(0.1f, true, false);   // 放下监控

            Assert.IsFalse(cameo.OnScreen);
            Assert.AreEqual(1, cameo.Escapes);
            Assert.IsFalse(cameo.ConsumeExpired(), "躲过去了，不该有人动手");
        }

        [Test]
        public void 超时就会动手()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);

            // 一直开着监控
            for (int i = 0; i < 30; i++)
            {
                cameo.Tick(0.1f, true, true);
            }

            Assert.IsFalse(cameo.OnScreen, "已经收场");
            Assert.IsTrue(cameo.ConsumeExpired(), "该动手了");
        }

        [Test]
        public void 动手信号只取一次()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);
            cameo.Tick(2f, true, true);

            Assert.IsTrue(cameo.ConsumeExpired());
            Assert.IsFalse(cameo.ConsumeExpired(), "第二次该是空的");
        }

        [Test]
        public void 正好一秒半就来不及了()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);

            cameo.Tick(BelialCameo.ReactionSeconds, true, true);

            Assert.IsTrue(cameo.ConsumeExpired(), "压线也算超");
        }

        [Test]
        public void 动手之后短时间内不会重复吐信号()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);
            cameo.Tick(2f, true, true);

            Assert.IsTrue(cameo.ConsumeExpired(), "第一次该有");

            // 下一个 5 秒判定点之前（约 1 秒），不该冒出第二发
            for (int i = 0; i < 10; i++)
            {
                cameo.Tick(0.1f, true, true);
            }

            Assert.IsFalse(cameo.ConsumeExpired(), "动作只该播一次");
        }

        [Test]
        public void 掩过去之后的剩余夜里他还是可能再来()
        {
            // 「每 5 秒判一次」是持续的：躲过一次不等于这一夜就安全了，
            // 只是得重新攒满 5 秒、再撞上那 0.05%。
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);
            cameo.Tick(0.1f, true, false);   // 躲过第一次
            Assert.AreEqual(1, cameo.Escapes);

            // 重新攒满 5 秒 → 又掷中 → 再开监控 → 第二次撞上
            cameo.Tick(BelialCameo.RollIntervalSeconds + 0.01f, true, false);
            cameo.Tick(0.1f, true, true);

            Assert.IsTrue(cameo.OnScreen, "第二次同样会撞上");
            Assert.AreEqual(2, cameo.Triggers);
        }

        // ---------------------------------------------------------------
        // 屏幕期间锁机位
        // ---------------------------------------------------------------

        [Test]
        public void 他在屏幕上时不许切机位()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);

            Assert.IsTrue(cameo.BlocksCameraSwitch);
        }

        [Test]
        public void 放下去之后就能切机位了()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);
            cameo.Tick(0.1f, true, false);

            Assert.IsFalse(cameo.BlocksCameraSwitch);
        }

        [Test]
        public void 还没撞上的时候不锁机位()
        {
            BelialCameo cameo = Armed(new AlwaysHit());

            Assert.IsFalse(cameo.BlocksCameraSwitch, "挂起阶段照常切");
            Assert.IsFalse(Make(new NeverHit()).BlocksCameraSwitch);
        }

        // ---------------------------------------------------------------
        // 重置 / 调试
        // ---------------------------------------------------------------

        [Test]
        public void 重开一夜会清空所有状态()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);
            cameo.Tick(0.1f, true, false);

            cameo.Reset();

            Assert.IsFalse(cameo.Pending);
            Assert.IsFalse(cameo.OnScreen);
            Assert.AreEqual(0, cameo.Triggers);
            Assert.AreEqual(0, cameo.Escapes);
            Assert.IsFalse(cameo.ConsumeExpired());
        }

        [Test]
        public void 重置之后掷骰重新计时()
        {
            var spy = new SpyRandom();
            BelialCameo cameo = Make(spy);
            cameo.Tick(4f, true, false);

            cameo.Reset();
            cameo.Tick(1f, true, false);

            Assert.AreEqual(0, spy.Calls, "重置后这 1 秒不该接上之前的 4 秒");
        }

        [Test]
        public void 调试置的是挂起不是屏幕()
        {
            // 早先这里置的是「已经在屏幕上」，结果玩家点完按钮、监控还放着，
            // 下一帧就被当成「已经放下了」算躲过去 —— 虚影一帧都没画出来。
            BelialCameo cameo = Make(new NeverHit());
            cameo.DebugArm();

            Assert.IsTrue(cameo.Pending, "该是挂起");
            Assert.IsFalse(cameo.OnScreen, "监控还没升起来，屏幕上不该有他");
            Assert.IsFalse(cameo.BlocksCameraSwitch, "挂起阶段不锁机位");

            // 升起监控才真撞上
            cameo.Tick(0.05f, true, true);

            Assert.IsTrue(cameo.OnScreen);
            Assert.AreEqual(BelialCameo.ReactionSeconds, cameo.ReactionLeft, Tolerance);
        }

        [Test]
        public void 调试挂起后放着监控不动不会自己消失()
        {
            // 点完调试按钮，玩家可能先愣一会儿才去升监控 —— 这期间不能算躲过
            BelialCameo cameo = Make(new NeverHit());
            cameo.DebugArm();

            for (int i = 0; i < 100; i++)
            {
                cameo.Tick(0.05f, true, false);
            }

            Assert.IsTrue(cameo.Pending, "还没升监控，不该被清掉");
            Assert.AreEqual(0, cameo.Escapes, "更不该记成躲过一次");
        }

        [Test]
        public void 躲过一次之后会重新开始掷骰()
        {
            BelialCameo cameo = Armed(new AlwaysHit());
            cameo.Tick(0.1f, true, true);
            cameo.Tick(0.1f, true, false);   // 躲过去

            Assert.IsFalse(cameo.Pending, "躲过去之后不能马上又挂起");

            cameo.Tick(BelialCameo.RollIntervalSeconds + 0.01f, true, false);

            Assert.IsTrue(cameo.Pending, "攒够 5 秒再掷一次");
        }
    }
}
