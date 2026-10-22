using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 桌上那部手机：接通以后她停下来听你说，代价是电表多走一格。
    /// 这里只测纯逻辑那一半（通话状态 + 台词本），电量的接入靠冒烟验。
    /// </summary>
    public class NaibaoPhoneTests
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

        private static int Drain(NaibaoDirector director)
        {
            int cues = 0;
            NaibaoCue cue;
            while (director.TryTakeCue(out cue))
            {
                cues++;
            }

            return cues;
        }

        [Test]
        public void 通话期间她不出手()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));
            director.BeginTalk();

            // 一直推进到远超「第一次出手 + 好几个间隔」的时长
            director.Tick(600f);

            Assert.IsTrue(director.IsTalking);
            Assert.AreEqual(0, Drain(director), "通话期间不该有任何出场");
            Assert.AreEqual(0, director.MischiefCount);
        }

        [Test]
        public void 通话期间时间也不推进()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));
            director.BeginTalk();
            director.Tick(120f);

            Assert.AreEqual(0f, director.Elapsed, 0.0001f,
                "她在通话里应该是「停住」，不是「一边听一边掐时间」");
        }

        [Test]
        public void 接通时把已经排好队的那次丢掉()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));

            director.Tick(NaibaoDirector.MischiefFirstDelay + 1f);
            Assert.AreEqual(1, Drain(director), "先确认这一次确实排进队列了");

            // 再来一次出场排队，然后接通
            director.Tick(NaibaoDirector.MischiefMaxGap + 1f);
            director.BeginTalk();

            Assert.AreEqual(0, Drain(director), "接通以后，排队里那个也不能再演");
        }

        [Test]
        public void 挂断之后要等满一个间隔才再出手()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));
            director.BeginTalk();
            director.Tick(100f);
            director.EndTalk();

            Assert.IsFalse(director.IsTalking);

            director.Tick(NaibaoDirector.MischiefMinGap - 1f);
            Assert.AreEqual(0, Drain(director), "挂断瞬间就闹，打电话就永远是亏的");

            director.Tick(2f);
            Assert.AreEqual(1, Drain(director), "等满一个间隔以后她照常出手");
        }

        [Test]
        public void 引导期通话把到点的讲解也停掉()
        {
            var director = new NaibaoDirector(true, new FixedRandom(0));

            director.Tick(NaibaoDirector.GuideTimes[0] + 1f);
            Assert.AreEqual(1, Drain(director));

            director.BeginTalk();
            director.Tick(NaibaoDirector.GuideTimes[NaibaoDirector.GuideTimes.Length - 1] + 10f);

            Assert.AreEqual(0, Drain(director), "引导期通话时也不该继续往外冒讲解");
        }

        [Test]
        public void Reset之后不再算通话中()
        {
            var director = new NaibaoDirector(false, new FixedRandom(0));
            director.BeginTalk();
            director.Reset();

            Assert.IsFalse(director.IsTalking);

            director.Tick(NaibaoDirector.MischiefFirstDelay + 1f);
            Assert.AreEqual(1, Drain(director));
        }

        [Test]
        public void 第1夜给的是引导词()
        {
            NaibaoCallLine[] lines = NaibaoPhoneScript.ForCall(0, true, 0);

            Assert.Greater(lines.Length, 0);
            Assert.IsTrue(lines[0].Text.Contains("手机"),
                "第一通电话得先解释「我在你桌上那部手机里」，否则玩家不知道这是什么");
        }

        [Test]
        public void 越往后词越不一样()
        {
            string early = NaibaoPhoneScript.ForCall(1, false, 0)[0].Text;
            string middle = NaibaoPhoneScript.ForCall(3, false, 0)[0].Text;
            string late = NaibaoPhoneScript.ForCall(5, false, 0)[0].Text;

            Assert.AreNotEqual(early, middle);
            Assert.AreNotEqual(middle, late);
        }

        [Test]
        public void 同一夜第二次通话给短版()
        {
            int first = NaibaoPhoneScript.ForCall(2, false, 0).Length;
            int second = NaibaoPhoneScript.ForCall(2, false, 1).Length;

            Assert.Greater(first, 0);
            Assert.Less(second, first, "第二次不该再讲一遍新剧情");
        }

        [Test]
        public void 台词都不为空且表情合法()
        {
            for (int night = 0; night < 8; night++)
            {
                for (int index = 0; index < 3; index++)
                {
                    foreach (bool guide in new[] { true, false })
                    {
                        NaibaoCallLine[] lines = NaibaoPhoneScript.ForCall(night, guide, index);
                        Assert.Greater(lines.Length, 0);

                        for (int i = 0; i < lines.Length; i++)
                        {
                            Assert.IsFalse(string.IsNullOrEmpty(lines[i].Text));
                            Assert.IsTrue(lines[i].Pose >= NaibaoPose.Peek
                                && lines[i].Pose <= NaibaoPose.Bored);
                        }
                    }
                }
            }
        }

        [Test]
        public void 一次通话从头念到尾()
        {
            NaibaoCallLine[] lines = NaibaoPhoneScript.ForCall(1, false, 0);
            var call = new NaibaoCall(lines);

            Assert.IsFalse(call.Ended);
            Assert.AreEqual(0, call.Index);
            Assert.AreEqual(lines[0].Text, call.Current.Text);

            for (int i = 0; i < lines.Length; i++)
            {
                bool more = call.Advance();
                Assert.AreEqual(i + 1, call.Index);

                if (i == lines.Length - 1)
                {
                    Assert.IsFalse(more, "最后一句之后 Advance 该返回 false");
                }
                else
                {
                    Assert.IsTrue(more);
                }
            }

            Assert.IsTrue(call.Ended);
            Assert.AreEqual(string.Empty, call.Current.Text, "念完之后没有「当前句」");
        }

        [Test]
        public void 空台词本直接算结束()
        {
            var call = new NaibaoCall(null);

            Assert.AreEqual(0, call.Count);
            Assert.IsTrue(call.Ended);
            Assert.IsFalse(call.Advance());
        }
    }
}
