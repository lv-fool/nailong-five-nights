using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 永夜模式难度曲线的规格：时间越久越难、动静越大越难，爬到第 6 夜后转为加压。
    /// </summary>
    public class EndlessDifficultyTests
    {
        [Test]
        public void 开局是第三夜的强度()
        {
            Assert.AreEqual(EndlessDifficulty.StartNight, EndlessDifficulty.TierFor(0f, 0f));
            Assert.AreEqual(2, EndlessDifficulty.TierFor(0f, 0f), "第 3 夜（0 起 = 2）");
        }

        [Test]
        public void 每撑过一个小时半涨一档()
        {
            int start = EndlessDifficulty.TierFor(0f, 0f);

            Assert.AreEqual(start, EndlessDifficulty.TierFor(EndlessDifficulty.HoursPerTier * 0.99f, 0f));
            Assert.AreEqual(start + 1, EndlessDifficulty.TierFor(EndlessDifficulty.HoursPerTier, 0f));
            Assert.AreEqual(start + 2, EndlessDifficulty.TierFor(EndlessDifficulty.HoursPerTier * 2f, 0f));
        }

        [Test]
        public void 动静攒够了也涨一档()
        {
            float quiet = EndlessDifficulty.TierFor(0f, 0f);
            float noisy = EndlessDifficulty.TierFor(0f, EndlessDifficulty.NoisePerTier);

            Assert.Greater(noisy, quiet);
        }

        [Test]
        public void 动静最多只加三档()
        {
            float huge = EndlessDifficulty.RawTierFor(0f, EndlessDifficulty.NoisePerTier * 99f);
            float capped = EndlessDifficulty.StartNight + EndlessDifficulty.MaxNoiseTiers;

            Assert.AreEqual(capped, huge);
        }

        [Test]
        public void 档位封顶在第六夜()
        {
            int cap = 6 - 1;

            Assert.AreEqual(cap, EndlessDifficulty.TierFor(999f, 999f));
        }

        [Test]
        public void 没爬到第六夜之前不加压()
        {
            Assert.AreEqual(0f, EndlessDifficulty.OverdriveFor(0f, 0f), 0.0001f);
            Assert.AreEqual(0f, EndlessDifficulty.OverdriveFor(EndlessDifficulty.HoursPerTier, 0f), 0.0001f);
        }

        [Test]
        public void 超过第六夜之后开始加压且越久越狠()
        {
            // 开局第 3 夜（2），每小时半一档：3 小时 → 第 5 夜，4.5 小时 → 第 6 夜，
            // 再往后才会超档加压
            float early = EndlessDifficulty.OverdriveFor(EndlessDifficulty.HoursPerTier * 5f, 0f);
            float late = EndlessDifficulty.OverdriveFor(EndlessDifficulty.HoursPerTier * 8f, 0f);

            Assert.Greater(early, 0f, "第 6 夜之后再撑下去必须加压");
            Assert.Greater(late, early);
        }

        [Test]
        public void 加压有上限()
        {
            Assert.AreEqual(EndlessDifficulty.MaxOverdrive,
                EndlessDifficulty.OverdriveFor(999f, 999f), 0.0001f);
        }
    }
}
