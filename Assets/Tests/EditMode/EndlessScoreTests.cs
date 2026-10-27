using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 永夜模式得分的规格：只认「撑了多久」和「挡住过几次」。
    /// </summary>
    public class EndlessScoreTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void 一开始是零分()
        {
            var score = new EndlessScore(85f);

            Assert.AreEqual(0, score.Score);
            Assert.AreEqual(0, score.Blocks);
            Assert.AreEqual(0, score.Hours);
            Assert.AreEqual(0f, score.Seconds, Tolerance);
        }

        [Test]
        public void 撑时间就有分每秒一分()
        {
            var score = new EndlessScore(85f);
            score.Tick(30f);

            Assert.AreEqual(30, score.Score);
        }

        [Test]
        public void 挡住怪物比干撑更值钱()
        {
            var timeOnly = new EndlessScore(85f);
            timeOnly.Tick(30f);

            var withBlock = new EndlessScore(85f);
            withBlock.Tick(30f);
            withBlock.AddBlocks(1);

            Assert.Greater(withBlock.Score, timeOnly.Score);
            Assert.AreEqual(EndlessScore.PointsPerBlock, withBlock.Score - timeOnly.Score);
        }

        [Test]
        public void 撑满一小时有额外奖励()
        {
            var score = new EndlessScore(85f);
            score.Tick(85f);

            // 85 秒 * 1 分 + 1 小时 * 100 分
            Assert.AreEqual(85 + EndlessScore.PointsPerHour, score.Score);
            Assert.AreEqual(1, score.Hours);
        }

        [Test]
        public void 不足一小时不给小时奖励()
        {
            var score = new EndlessScore(85f);
            score.Tick(84.9f);

            Assert.AreEqual(0, score.Hours);
            Assert.AreEqual(84, score.Score);
        }

        [Test]
        public void 暂停与卡帧不加分()
        {
            var score = new EndlessScore(85f);
            score.Tick(10f);
            score.Tick(0f);
            score.Tick(-5f);

            Assert.AreEqual(10, score.Score);
        }

        [Test]
        public void 负的次数不会被记进去()
        {
            var score = new EndlessScore(85f);
            score.AddBlocks(0);
            score.AddBlocks(-3);

            Assert.AreEqual(0, score.Blocks);
        }

        [Test]
        public void 一次记多次挡住()
        {
            var score = new EndlessScore(85f);
            score.AddBlocks(3);

            Assert.AreEqual(3, score.Blocks);
            Assert.AreEqual(3 * EndlessScore.PointsPerBlock, score.Score);
        }

        [Test]
        public void 重置之后从头开始()
        {
            var score = new EndlessScore(85f);
            score.Tick(120f);
            score.AddBlocks(2);
            score.Reset();

            Assert.AreEqual(0, score.Score);
            Assert.AreEqual(0f, score.Seconds, Tolerance);
        }

        [Test]
        public void 静态公式和实例算出来一样()
        {
            var score = new EndlessScore(85f);
            score.Tick(180f);
            score.AddBlocks(4);

            Assert.AreEqual(score.Score, EndlessScore.ScoreFor(180f, 4, 85f));
        }

        [Test]
        public void 时钟秒数非法时退回默认值()
        {
            var score = new EndlessScore(0f);
            score.Tick(85f);

            Assert.AreEqual(1, score.Hours);
        }
    }
}
