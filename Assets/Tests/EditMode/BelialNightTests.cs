using System;
using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 贝利亚之夜 + 「来自奶包的支援」的规格。
    ///
    /// 老板原话（2026-10-26）：12 小时一局，每撑两小时给一个支援；
    /// 第一次必须刷出三张一样的「拉钩」，
    /// 选完之后再给一次选择机会；支援名字统一叫「来自奶包的支援」。
    /// </summary>
    public class BelialNightTests
    {
        private const float Tolerance = 0.01f;

        /// <summary>开局，白送那三张卡已经摆好。</summary>
        private static BelialNight Started(int seed)
        {
            var night = new BelialNight(seed);
            night.Begin();
            return night;
        }

        /// <summary>选完白送那张 + 补的那次，到「下一次要等两小时」的状态。</summary>
        private static BelialNight AfterOpening(int seed)
        {
            BelialNight night = Started(seed);
            night.Choose(0);
            night.Choose(0);
            return night;
        }

        /// <summary>找一个能抽到指定支援的种子，并在第一次正常三选一里把它选走。</summary>
        private static BelialNight WithSupport(params string[] ids)
        {
            var wanted = new System.Collections.Generic.List<string>(ids);

            for (int seed = 0; seed < 600; seed++)
            {
                BelialNight night = Started(seed);
                night.Choose(0); // 白送那张
                var left = new System.Collections.Generic.List<string>(wanted);

                for (int guard = 0; guard < 24 && left.Count > 0; guard++)
                {
                    if (!night.AwaitingChoice)
                    {
                        night.AdvanceHours(1);
                        night.TryOffer();
                        if (!night.AwaitingChoice)
                        {
                            break;
                        }
                    }

                    SupportDef[] offer = night.Offer;
                    int index = -1;
                    for (int i = 0; i < offer.Length && index < 0; i++)
                    {
                        if (left.Contains(offer[i].Id))
                        {
                            index = i;
                        }
                    }

                    if (index >= 0)
                    {
                        left.Remove(offer[index].Id);
                        night.Choose(index);
                    }
                    else
                    {
                        night.Choose(0);
                    }
                }

                if (left.Count == 0)
                {
                    return night;
                }
            }

            Assert.Fail("600 个种子里都没凑齐支援：" + string.Join("+", ids));
            return null;
        }

        /// <summary>直接往账本里塞一条支援（测数值用，绕过抽取池）。</summary>
        private static void Grant(BelialNight night, string id)
        {
            var field = typeof(BelialNight).GetField(
                "_owned",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var owned = (System.Collections.Generic.List<SupportDef>)field.GetValue(night);
            owned.Add(SupportCatalog.Get(id));
        }

        [Test]
        public void 开局白送的三张是同一张拉钩()
        {
            BelialNight night = Started(0);
            Assert.IsTrue(night.AwaitingChoice);
            Assert.IsTrue(night.IsForcedOffer, "开局这一次是白送的");
            Assert.AreEqual(BelialNight.OffersPerChoice, night.Offer.Length);

            for (int i = 1; i < night.Offer.Length; i++)
            {
                Assert.AreEqual(night.Offer[0].Id, night.Offer[i].Id, "三张必须是同一张");
            }

            Assert.AreEqual(SupportCatalog.FirstPickId, night.Offer[0].Id);
        }

        [Test]
        public void 选完白送那张会立刻再给一次选择()
        {
            BelialNight night = Started(3);
            night.Choose(0);

            Assert.AreEqual(1, night.Picks);
            Assert.IsTrue(night.AwaitingChoice, "白送之后再给一次机会");
            Assert.IsFalse(night.IsForcedOffer, "补的这次是正常三选一");
        }

        [Test]
        public void 白送和补的那次都不占两个小时()
        {
            BelialNight night = AfterOpening(0);
            Assert.AreEqual(2, night.Picks);
            Assert.IsFalse(night.AwaitingChoice);
            Assert.AreEqual(0f, night.TryOffer(), Tolerance, "还不到两个小时，不发卡");
            Assert.IsFalse(night.AwaitingChoice);
        }

        [Test]
        public void 撑到两小时才给第三张()
        {
            BelialNight night = AfterOpening(0);
            night.AdvanceHours(BelialNight.HoursPerPick);
            night.TryOffer();

            Assert.IsTrue(night.AwaitingChoice);
            Assert.AreEqual(2, night.Hours);
        }

        [Test]
        public void 一局最多七张卡()
        {
            var night = new BelialNight(1);
            night.Begin();

            for (int guard = 0; guard < 40 && night.AwaitingChoice; guard++)
            {
                night.Choose(0);
                if (!night.AwaitingChoice)
                {
                    night.AdvanceHours(1);
                    night.TryOffer();
                }
            }

            for (int hour = 0; hour < BelialNight.TotalHours; hour++)
            {
                night.AdvanceHours(1);
                night.TryOffer();
                while (night.AwaitingChoice)
                {
                    night.Choose(0);
                }
            }

            Assert.AreEqual(BelialNight.MaxPicks, night.Picks, "12 小时 / 每两小时 + 白送 = 七张");
        }

        [Test]
        public void 记账按动作权重累加()
        {
            BelialNight night = AfterOpening(0);
            float door = night.NoteAction(PlayerAction.Door);
            float camera = night.NoteAction(PlayerAction.Camera);

            Assert.Greater(door, camera, "关门比切监控更值钱");
            Assert.AreEqual(door + camera, night.Credits, Tolerance);
        }

        [Test]
        public void 存钱罐按这一回合的动作折算电量()
        {
            BelialNight night = AfterOpening(0);
            Grant(night, "refund");
            night.NoteAction(PlayerAction.Door);
            night.NoteAction(PlayerAction.Door);
            night.NoteAction(PlayerAction.Door);

            night.AdvanceHours(BelialNight.HoursPerPick);
            float refund = night.TryOffer();

            Assert.AreEqual(BelialNight.RefundBaseGain + 3f * BelialNight.RefundPerCredit, refund, Tolerance);
        }

        [Test]
        public void 只拿一层存钱罐时返还有上限()
        {
            BelialNight night = AfterOpening(0);
            Grant(night, "refund");
            for (int i = 0; i < 200; i++)
            {
                night.NoteAction(PlayerAction.Door);
            }

            night.AdvanceHours(BelialNight.HoursPerPick);
            float refund = night.TryOffer();

            Assert.AreEqual(BelialNight.RefundCap, refund, Tolerance);
        }

        [Test]
        public void 连拿两次存钱罐之后返还不设上限()
        {
            BelialNight night = AfterOpening(0);
            Grant(night, "refund");
            Grant(night, "refund");
            for (int i = 0; i < 200; i++)
            {
                night.NoteAction(PlayerAction.Door);
            }

            night.AdvanceHours(BelialNight.HoursPerPick);
            float refund = night.TryOffer();

            Assert.AreEqual(4f + 200f * 0.75f * 2f, refund, 0.5f);
        }

        [Test]
        public void 拿到拉钩之后她说的都是真的()
        {
            BelialNight night = Started(0);
            night.Choose(0);
            Assert.IsTrue(night.MessagesTruthful);
        }

        [Test]
        public void 罚站木头人和扑空都读得出来()
        {
            BelialNight night = AfterOpening(0);
            Grant(night, "silence");
            Grant(night, "lock");
            Grant(night, "miss");

            Assert.AreEqual(45f, night.NailongFreezeSeconds, Tolerance);
            Assert.AreEqual(25f, night.CameraLockSeconds, Tolerance);
            Assert.AreEqual(1, night.MissesLeft);
            Assert.IsTrue(night.ConsumeMiss());
            Assert.AreEqual(0, night.MissesLeft);
            Assert.IsFalse(night.ConsumeMiss(), "只有一次，不能一直扑空");
        }

        [Test]
        public void 拿满层的支援不再出现()
        {
            BelialNight night = AfterOpening(0);
            Grant(night, "truthful");

            for (int hour = 0; hour < BelialNight.TotalHours; hour++)
            {
                night.AdvanceHours(1);
                night.TryOffer();
                if (!night.AwaitingChoice)
                {
                    continue;
                }

                for (int i = 0; i < night.Offer.Length; i++)
                {
                    Assert.AreNotEqual("truthful", night.Offer[i].Id, "拉钩只有一层，不该再出现");
                }

                night.Choose(0);
            }
        }

        [Test]
        public void 前六小时贝利亚不出场()
        {
            BelialNight night = AfterOpening(0);

            for (int hour = 0; hour < BelialNight.PhaseTwoHour; hour++)
            {
                Assert.AreEqual(1, night.Phase, "第 " + hour + " 小时还在第一阶段");
                Assert.IsFalse(night.BelialActive);
                night.AdvanceHours(1);
            }
        }

        [Test]
        public void 第六小时起贝利亚上场()
        {
            BelialNight night = AfterOpening(0);
            night.AdvanceHours(BelialNight.PhaseTwoHour);

            Assert.AreEqual(2, night.Phase);
            Assert.IsTrue(night.BelialActive);

            night.AdvanceHours(BelialNight.TotalHours);
            Assert.AreEqual(2, night.Phase, "后半段一直是第二阶段");
        }

        /// <summary>把当前还挂着的选择都选掉，方便测「下一次抽取」。</summary>
        private static void ConsumePending(BelialNight night)
        {
            while (night.AwaitingChoice)
            {
                night.Choose(0);
            }
        }

        [Test]
        public void 处方笺把三选一变成四选一()
        {
            BelialNight night = WithSupport("prescription");
            Assert.AreEqual(4, night.OfferSize);

            ConsumePending(night);
            night.AdvanceHours(BelialNight.TotalHours);
            night.TryOffer();
            Assert.IsTrue(night.AwaitingChoice, "应该还有一次抽取机会");
            Assert.AreEqual(4, night.Offer.Length, "拿到处方笺之后要摆四张");
        }

        [Test]
        public void 复诊单只能重抽一次()
        {
            BelialNight night = WithSupport("rerollslip");
            ConsumePending(night);
            Assert.AreEqual(1, night.RerollsLeft);

            night.AdvanceHours(BelialNight.TotalHours);
            night.TryOffer();
            Assert.IsTrue(night.AwaitingChoice);

            Assert.IsTrue(night.Reroll(), "第一次重抽应该成功");
            Assert.AreEqual(0, night.RerollsLeft);
            Assert.IsFalse(night.Reroll(), "没有次数了，第二次要失败");
            Assert.AreEqual(3, night.Offer.Length);

            for (int i = 0; i < night.Offer.Length; i++)
            {
                SupportDef def = night.Offer[i];
                Assert.Less(night.StackOf(def.Effect), def.MaxStack, "重抽不该给出已经拿满的：" + def.Title);
            }
        }

        [Test]
        public void 依从性好把所有加算类效果抬高一层()
        {
            BelialNight night = AfterOpening(0);
            Grant(night, "adherence");
            Grant(night, "silence");

            Assert.AreEqual(1.15f, night.AmplifyFactor, 0.001f);
            Assert.AreEqual(45f * 1.15f, night.NailongFreezeSeconds, 0.01f, "罚站 45 秒要变成 51.75 秒");
        }

        [Test]
        public void 加量之后不再给电力类()
        {
            BelialNight night = WithSupport("overdose");
            Assert.IsTrue(night.IsBanned(SupportCategory.Power));

            int offers = 0;
            for (int hour = night.Hours; hour < BelialNight.TotalHours; hour++)
            {
                night.AdvanceHours(1);
                night.TryOffer();
                if (!night.AwaitingChoice)
                {
                    continue;
                }

                offers++;
                for (int i = 0; i < night.Offer.Length; i++)
                {
                    Assert.AreNotEqual(
                        SupportCategory.Power,
                        night.Offer[i].Category,
                        "加量之后还给了电力类：" + night.Offer[i].Title);
                }

                night.Choose(0);
            }

            Assert.Greater(offers, 2, "应该还抽了好几次");
        }
        [Test]
        public void 谷维素让不开灯也看得见门口()
        {
            var night = AfterOpening(0);
            Grant(night, "oryzanol");

            Assert.IsTrue(night.DoorSightActive);
        }

        [Test]
        public void 咖啡因切机位会留残影()
        {
            var night = AfterOpening(0);
            Grant(night, "caffeine");

            Assert.AreEqual(0.3f, night.CameraGhostSeconds, Tolerance);
        }

        [Test]
        public void 一通电话每个阶段报一次机位()
        {
            var night = AfterOpening(0);
            Grant(night, "phone");

            Assert.IsTrue(night.StageSpotActive);
        }

        [Test]
        public void 今天出门了要空出两个机位()
        {
            var night = AfterOpening(0);
            Grant(night, "outing");

            Assert.AreEqual(2, night.SafeSpotCount);
        }

        [Test]
        public void 复诊会在监控上标弱点()
        {
            var night = AfterOpening(0);
            Assert.IsFalse(night.WeakPointActive);

            Grant(night, "recheck");

            Assert.IsTrue(night.WeakPointActive);
        }

        [Test]
        public void 查房叠两层才能全看()
        {
            var night = AfterOpening(0);

            Grant(night, "wardround");
            Assert.AreEqual(1, night.RevealLevel);

            Grant(night, "wardround");
            Assert.AreEqual(2, night.RevealLevel);
        }

        [Test]
        public void 闹钟让时间走快失眠让时间走慢()
        {
            var fast = AfterOpening(0);
            Grant(fast, "alarm");
            Assert.AreEqual(1.15f, fast.TimeScaleFactor, Tolerance);

            var slow = AfterOpening(1);
            Grant(slow, "insomnia");
            Assert.AreEqual(0.9f, slow.TimeScaleFactor, Tolerance);
        }

        [Test]
        public void 熬夜照得远但判定窗口更短()
        {
            var night = AfterOpening(0);
            Assert.AreEqual(1f, night.FlashlightRangeScale, Tolerance);

            Grant(night, "allnighter");

            Assert.AreEqual(1.5f, night.FlashlightRangeScale, Tolerance);
            Assert.AreEqual(0.2f, night.AttackWindowPenalty, Tolerance);
        }
    }
}
