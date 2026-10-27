using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 贝利亚本人（2026-10-26 定）：从两边门里随便一边出现，得把门打开、
    /// 用手电筒照他 —— 照一下白一下，照够 5 下他走；站着不动 9 秒就动手。
    /// </summary>
    public class BelialEncounterTests
    {
        private const float Tolerance = 0.01f;

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

        private static BelialEncounter Create(int side)
        {
            return new BelialEncounter(new FixedRandom(side));
        }

        /// <summary>把时间推到「他站在门外」。</summary>
        private static void WaitForAppear(BelialEncounter enc)
        {
            int guard = 0;
            while (!enc.AtDoor && guard++ < 600)
            {
                enc.Tick(0.1f, true);
            }

            Assert.IsTrue(enc.AtDoor, "等了 60 秒他还没来");
        }

        [Test]
        public void 前半程他不会来()
        {
            var enc = Create(0);

            for (int i = 0; i < 300; i++)
            {
                enc.Tick(0.1f, false);
            }

            Assert.IsFalse(enc.AtDoor);
            Assert.AreEqual(0, enc.Visits);
        }

        [Test]
        public void 到点会出现在某一边门外()
        {
            var left = Create(0);
            var right = Create(1);

            WaitForAppear(left);
            WaitForAppear(right);

            Assert.AreEqual(BelialSide.Left, left.Side);
            Assert.AreEqual(BelialSide.Right, right.Side);
            Assert.AreEqual(1, left.Visits);
            Assert.AreEqual(BelialEncounter.DoorGraceSeconds, left.GraceLeft, Tolerance);
        }

        [Test]
        public void 门关着照不到他()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            Assert.IsFalse(enc.TryFlash(false, true), "关着门，手电筒也没用");
            Assert.AreEqual(0, enc.Flashes);
        }

        [Test]
        public void 手电筒不亮也照不到()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            Assert.IsFalse(enc.TryFlash(true, false));
            Assert.AreEqual(0, enc.Flashes);
        }

        [Test]
        public void 一次快门只算一下()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            Assert.IsTrue(enc.TryFlash(true, true));
            Assert.IsFalse(enc.TryFlash(true, true), "冷却还没过，按住不重复计");
            Assert.AreEqual(1, enc.Flashes);

            enc.Tick(BelialEncounter.FlashCooldownSeconds + 0.01f, true);

            Assert.IsTrue(enc.TryFlash(true, true));
            Assert.AreEqual(2, enc.Flashes);
        }

        [Test]
        public void 照到会变成白贝()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            Assert.IsFalse(enc.IsWhite);

            enc.TryFlash(true, true);

            Assert.IsTrue(enc.IsWhite, "照到的那一下就白了");

            enc.Tick(BelialEncounter.WhiteSeconds + 0.1f, true);

            Assert.IsFalse(enc.IsWhite, "白一下就要变回黑的");
        }

        [Test]
        public void 照够五下他就走()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            for (int i = 0; i < BelialEncounter.FlashCount; i++)
            {
                Assert.IsTrue(enc.TryFlash(true, true), "第 " + (i + 1) + " 下该算数");
                enc.Tick(BelialEncounter.FlashCooldownSeconds + 0.01f, true);
            }

            Assert.AreEqual(BelialEncounter.FlashCount, enc.Flashes);
            Assert.IsTrue(enc.IsRetreating, "照够就该走");
            Assert.IsFalse(enc.AtDoor);
            Assert.IsFalse(enc.ConsumeKill(), "照走的人不该杀人");
        }

        [Test]
        public void 站着不动九秒就动手()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            enc.Tick(BelialEncounter.DoorGraceSeconds - 0.5f, true);
            Assert.IsFalse(enc.ConsumeKill(), "还没到点");

            enc.Tick(1f, true);

            Assert.IsTrue(enc.ConsumeKill(), "超时就是要死");
            Assert.IsFalse(enc.ConsumeKill(), "致命信号只给一次");
        }

        [Test]
        public void 走之后隔一会儿才会再来()
        {
            var enc = Create(0);
            WaitForAppear(enc);

            for (int i = 0; i < BelialEncounter.FlashCount; i++)
            {
                enc.TryFlash(true, true);
                enc.Tick(BelialEncounter.FlashCooldownSeconds + 0.01f, true);
            }

            enc.Tick(BelialEncounter.RetreatSeconds + 0.1f, true);

            Assert.IsFalse(enc.AtDoor, "走了就是走了");
            Assert.Greater(enc.SecondsUntilAppear, 0f, "还得等一阵");

            for (int i = 0; i < 600 && !enc.AtDoor; i++)
            {
                enc.Tick(0.1f, true);
            }

            Assert.IsTrue(enc.AtDoor, "过一会儿他又会来");
            Assert.AreEqual(2, enc.Visits);
        }

        [Test]
        public void 换夜之后重新计时()
        {
            var enc = Create(0);
            WaitForAppear(enc);
            enc.TryFlash(true, true);

            enc.Reset();

            Assert.IsFalse(enc.AtDoor);
            Assert.AreEqual(0, enc.Flashes);
            Assert.AreEqual(0, enc.Visits);
            Assert.AreEqual(BelialEncounter.FirstDelaySeconds, enc.SecondsUntilAppear, Tolerance);
            Assert.IsFalse(enc.ConsumeKill());
        }
    }
}
