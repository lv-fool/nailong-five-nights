using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 奶包（<see cref="NaibaoDirector"/>）：第 1 夜只引导、之后只手欠，
    /// 而且她只会「烦」你 —— 不出门、不偷电、不改难度。
    /// </summary>
    public class NaibaoDirectorTests
    {
        /// <summary>想让哪一档就返回哪一档的骰子。</summary>
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

        /// <summary>把所有时间一次推进到点，再把队列里的东西全取出来。</summary>
        private static void Pump(NaibaoDirector director, float seconds,
            out int cues, out NaibaoAction firstAction, out string firstLine)
        {
            director.Tick(seconds);

            cues = 0;
            firstAction = NaibaoAction.None;
            firstLine = string.Empty;

            NaibaoCue cue;
            while (director.TryTakeCue(out cue))
            {
                if (cues == 0)
                {
                    firstAction = cue.Action;
                    firstLine = cue.Line;
                }

                cues++;
            }
        }

        [Test]
        public void 引导期开场十几秒内不吭声()
        {
            var director = new NaibaoDirector(true, new FixedRandom(0));

            director.Tick(NaibaoDirector.GuideFirstDelay - 1f);

            NaibaoCue cue;
            Assert.IsFalse(director.TryTakeCue(out cue), "还没到点就说话了");
        }

        [Test]
        public void 引导期到点必说第一句()
        {
            var director = new NaibaoDirector(true, new FixedRandom(0));

            director.Tick(NaibaoDirector.GuideFirstDelay);

            NaibaoCue cue;
            Assert.IsTrue(director.TryTakeCue(out cue));
            Assert.AreEqual(NaibaoAction.Line, cue.Action);
            Assert.AreEqual(NaibaoPose.Guide, cue.Pose);
            Assert.AreEqual(NaibaoDirector.GuideLines[0], cue.Line);
        }

        [Test]
        public void 引导期五句按顺序说完就收工()
        {
            var director = new NaibaoDirector(true, new FixedRandom(0));

            int cues;
            NaibaoAction action;
            string line;

            // 一次推进到最后一句之后
            Pump(director, NaibaoDirector.GuideTimes[NaibaoDirector.GuideTimes.Length - 1],
                out cues, out action, out line);

            Assert.AreEqual(NaibaoDirector.GuideLines.Length, cues, "句子数不对");
            Assert.IsTrue(director.IsFinished);

            // 已经说完，再推时间也不该冒新的
            Pump(director, 300f, out cues, out action, out line);
            Assert.AreEqual(0, cues);
        }

        [Test]
        public void 引导期永远不捣乱()
        {
            var director = new NaibaoDirector(true, new FixedRandom(0));

            for (int i = 0; i < 40; i++)
            {
                director.Tick(30f);

                NaibaoCue cue;
                while (director.TryTakeCue(out cue))
                {
                    Assert.AreEqual(NaibaoAction.Line, cue.Action,
                        "引导期不该动设备");
                }
            }

            Assert.AreEqual(0, director.MischiefCount);
        }

        [Test]
        public void 手欠期开场三十秒内不动手()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            director.Tick(NaibaoDirector.MischiefFirstDelay - 1f);

            NaibaoCue cue;
            Assert.IsFalse(director.TryTakeCue(out cue));
            Assert.AreEqual(0, director.MischiefCount);
        }

        [Test]
        public void 手欠期到点出手且一定带台词()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            int cues;
            NaibaoAction action;
            string line;
            Pump(director, NaibaoDirector.MischiefFirstDelay, out cues, out action, out line);

            Assert.AreEqual(1, cues);
            Assert.AreEqual(NaibaoAction.UsageSpike, action);
            Assert.IsFalse(string.IsNullOrEmpty(line), "奶包出手不说话");
            Assert.AreEqual(1, director.MischiefCount);
        }

        [Test]
        public void 四种手欠都认得出来()
        {
            NaibaoAction[] expected =
            {
                NaibaoAction.UsageSpike,
                NaibaoAction.MonitorGrab,
                NaibaoAction.LightOff,
                NaibaoAction.Line,
            };

            for (int pick = 0; pick < expected.Length; pick++)
            {
                var director = new NaibaoDirector(false, new FixedRandom(pick));

                int cues;
                NaibaoAction action;
                string line;
                Pump(director, NaibaoDirector.MischiefFirstDelay, out cues, out action, out line);

                Assert.AreEqual(expected[pick], action, "第 " + pick + " 档不对");
                Assert.IsFalse(string.IsNullOrEmpty(line));
            }
        }

        [Test]
        public void 加耗电和抢监控都给了持续时间()
        {
            var spike = new NaibaoDirector(false, new FixedRandom(0));
            var grab = new NaibaoDirector(false, new FixedRandom(1));

            spike.Tick(NaibaoDirector.MischiefFirstDelay);
            grab.Tick(NaibaoDirector.MischiefFirstDelay);

            NaibaoCue cue;
            Assert.IsTrue(spike.TryTakeCue(out cue));
            Assert.AreEqual(NaibaoDirector.UsageSpikeSeconds, cue.Seconds);

            Assert.IsTrue(grab.TryTakeCue(out cue));
            Assert.AreEqual(NaibaoDirector.MonitorGrabSeconds, cue.Seconds);
        }

        [Test]
        public void 两次捣乱之间隔够了才动手()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            // 第一次
            director.Tick(NaibaoDirector.MischiefFirstDelay);
            NaibaoCue cue;
            Assert.IsTrue(director.TryTakeCue(out cue));

            // 最短间隔差一点点，不该有第二次
            director.Tick(NaibaoDirector.MischiefMinGap - 1f);
            Assert.IsFalse(director.TryTakeCue(out cue), "间隔没到就又来了");

            // 补足最短间隔就有了
            director.Tick(1f);
            Assert.IsTrue(director.TryTakeCue(out cue));
            Assert.AreEqual(2, director.MischiefCount);
        }

        [Test]
        public void 取过的出场不会再给第二次()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));
            director.Tick(NaibaoDirector.MischiefFirstDelay);

            NaibaoCue cue;
            Assert.IsTrue(director.TryTakeCue(out cue));
            Assert.IsFalse(director.TryTakeCue(out cue));
        }

        [Test]
        public void 重置以后从头再来()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));
            director.Tick(NaibaoDirector.MischiefFirstDelay);
            NaibaoCue cue;
            director.TryTakeCue(out cue);

            director.Reset();

            Assert.AreEqual(0f, director.Elapsed);
            Assert.AreEqual(0, director.MischiefCount);
            Assert.IsFalse(director.TryTakeCue(out cue));

            director.Tick(NaibaoDirector.MischiefFirstDelay);
            Assert.IsTrue(director.TryTakeCue(out cue), "重置后应该能重新出手");
        }

        [Test]
        public void 台词数组齐全且不空()
        {
            Assert.AreEqual(NaibaoDirector.GuideTimes.Length,
                NaibaoDirector.GuideLines.Length, "引导台词和时刻表对不上");

            foreach (string line in NaibaoDirector.GuideLines)
            {
                Assert.IsFalse(string.IsNullOrEmpty(line));
            }
        }

        [Test]
        public void 没有骰子也不会炸()
        {
            var director = new NaibaoDirector(false, null);

            director.Tick(NaibaoDirector.MischiefFirstDelay);

            NaibaoCue cue;
            Assert.IsTrue(director.TryTakeCue(out cue));
            Assert.IsFalse(string.IsNullOrEmpty(cue.Line));
        }

        [Test]
        public void 手欠期不接电话冷却不变()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            director.Tick(NaibaoDirector.MischiefFirstDelay);
            NaibaoCue cue;
            director.TryTakeCue(out cue);

            // 没接电话：EndTalk() 视为未听任何语音，冷却不变
            director.EndTalk();

            // 最短间隔差 1 秒，不该出手
            director.Tick(NaibaoDirector.MischiefMinGap - 1f);
            Assert.IsFalse(director.TryTakeCue(out cue), "间隔没到就又来了");

            // 补够 1 秒就该出手
            director.Tick(1f);
            Assert.IsTrue(director.TryTakeCue(out cue), "间隔到了应该出手");
            Assert.AreEqual(2, director.MischiefCount);
        }

        [Test]
        public void 手欠期完整听完冷却增加一半()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            director.Tick(NaibaoDirector.MischiefFirstDelay);
            director.TryTakeCue(out _);

            // 接通 → 挂断，完整听完：通话时长 = 设定总时长，冷却增加一半
            director.BeginTalk();
            director.EndTalk(60f, 60f);

            // 原本需 38 秒，现在需 57 秒
            director.Tick(56f);
            NaibaoCue cue;
            Assert.IsFalse(director.TryTakeCue(out cue), "57秒内不该出手");

            director.Tick(1f);
            Assert.IsTrue(director.TryTakeCue(out cue), "57秒后应该出手");
        }

        [Test]
        public void 手欠期听了一半冷却增加四分之一()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            director.Tick(NaibaoDirector.MischiefFirstDelay);
            director.TryTakeCue(out _);

            // 接通 → 挂断，听了一半：ratio=0.5，gap = 38 * 1.25 = 47.5
            director.BeginTalk();
            director.EndTalk(30f, 60f);

            director.Tick(47f);
            NaibaoCue cue;
            Assert.IsFalse(director.TryTakeCue(out cue), "47.5秒内不该出手");

            director.Tick(1f);
            Assert.IsTrue(director.TryTakeCue(out cue), "47.5秒后应该出手");
        }

        [Test]
        public void 引导期挂断后不捣乱()
        {
            var director = new NaibaoDirector(true, new FixedRandom(0));

            // 先让第一句引导台词出队
            director.Tick(NaibaoDirector.GuideFirstDelay);
            NaibaoCue cue;
            director.TryTakeCue(out cue);

            // 接通 → 挂断，即使通话很长，引导期也不进入捣乱冷却
            director.BeginTalk();
            director.EndTalk(60f, 60f);

            // 把整夜的引导时刻表走完，剩下的台词应该照常按序播完
            director.Tick(NaibaoDirector.GuideTimes[NaibaoDirector.GuideTimes.Length - 1]);

            // 引导期永远不捣乱：队列里只允许出现 Line，捣乱次数保持 0
            int lines = 0;
            while (director.TryTakeCue(out cue))
            {
                Assert.AreEqual(NaibaoAction.Line, cue.Action, "引导期不该捣乱");
                lines++;
            }

            Assert.AreEqual(NaibaoDirector.GuideLines.Length - 1, lines, "剩下的引导台词应该照常播完");
            Assert.AreEqual(0, director.MischiefCount);
        }
    }
}
