using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 敌人 AI 的行为规格。
    ///
    /// 推进方式依据 FNAF 1：每 5 秒掷一次 1~20 的骰子，不超过活动度就前进。
    /// 测试里注入确定性骰子（FixedRandom），把随机性拿掉，只验证行为规则。
    /// </summary>
    public class EnemyBrainTests
    {
        private const float Tolerance = 0.0001f;

        /// <summary>确定性骰子：永远掷出同一个数（会夹到调用方给的范围里）。</summary>
        private sealed class FixedRandom : IRandomSource
        {
            private readonly int _value;

            public FixedRandom(int value)
            {
                _value = value;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive)
                {
                    return minInclusive;
                }

                int value = _value;
                if (value < minInclusive)
                {
                    value = minInclusive;
                }

                if (value >= maxExclusive)
                {
                    value = maxExclusive - 1;
                }

                return value;
            }
        }

        private static void AdvanceToStage(EnemyBrain brain, EnemyContext ctx, int targetStage)
        {
            for (int i = 0; i < 50 && brain.Stage < targetStage; i++)
            {
                brain.Tick(EnemyBrain.DefaultMoveIntervalSeconds, ctx);
            }
        }

        // ---------------------------------------------------------------
        // 通用推进规则
        // ---------------------------------------------------------------

        [Test]
        public void 活动度为零的敌人整夜不前进()
        {
            var brain = new DoorEnemyBrain(EnemyId.Niulai, Side.Left, 0, new FixedRandom(1));
            var ctx = EnemyContext.Idle();

            for (int i = 0; i < 100; i++)
            {
                brain.Tick(5f, ctx);
            }

            Assert.AreEqual(0, brain.Stage);
            Assert.IsFalse(brain.IsAttacking);
        }

        [Test]
        public void 活动度满的敌人每次掷骰都前进()
        {
            var brain = new DoorEnemyBrain(EnemyId.Niulai, Side.Left,
                EnemyBrain.MaxActivityLevel, new FixedRandom(EnemyBrain.MaxActivityLevel));
            var ctx = EnemyContext.Idle();

            brain.Tick(5f, ctx);
            Assert.AreEqual(1, brain.Stage);

            brain.Tick(5f, ctx);
            Assert.AreEqual(2, brain.Stage);
        }

        [Test]
        public void 掷骰间隔没到就不会前进()
        {
            var brain = new DoorEnemyBrain(EnemyId.Niulai, Side.Left,
                EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = EnemyContext.Idle();

            brain.Tick(4.9f, ctx);

            Assert.AreEqual(0, brain.Stage);
        }

        [Test]
        public void 活动度被夹在零到二十之间()
        {
            var brain = new DoorEnemyBrain(EnemyId.Niulai, Side.Left, 99, new FixedRandom(1));

            Assert.AreEqual(EnemyBrain.MaxActivityLevel, brain.ActivityLevel);

            brain.SetActivityLevel(-5);
            Assert.AreEqual(0, brain.ActivityLevel);
        }

        // ---------------------------------------------------------------
        // 牛来 / 袋鼠：关门挡
        // ---------------------------------------------------------------

        [Test]
        public void 门关着时_门类敌人到门口会被挡回去()
        {
            var brain = new DoorEnemyBrain(EnemyId.Niulai, Side.Left,
                EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = new EnemyContext(true, false, false, false, false, ViewSide.Front, 0);

            AdvanceToStage(brain, ctx, 2);
            Assert.AreEqual(2, brain.Stage);

            brain.Tick(0.1f, ctx);

            Assert.AreEqual(0, brain.Stage);
            Assert.IsFalse(brain.IsAttacking);
            Assert.IsTrue(brain.RetreatedThisTick);
        }

        [Test]
        public void 门开着时_门类敌人撑过宽限时间就动手()
        {
            const float grace = 3f;
            var brain = new DoorEnemyBrain(EnemyId.Daishu, Side.Right,
                EnemyBrain.MaxActivityLevel, new FixedRandom(1),
                EnemyBrain.DefaultMoveIntervalSeconds, grace);
            var ctx = EnemyContext.Idle();

            AdvanceToStage(brain, ctx, 2);

            brain.Tick(2f, ctx);
            Assert.IsFalse(brain.IsAttacking);

            brain.Tick(1.5f, ctx);
            Assert.IsTrue(brain.IsAttacking);
        }

        [Test]
        public void 门类敌人还在走廊时不会动手()
        {
            var brain = new DoorEnemyBrain(EnemyId.Niulai, Side.Left,
                EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = EnemyContext.Idle();

            brain.Tick(5f, ctx);

            Assert.AreEqual(1, brain.Stage);
            Assert.IsFalse(brain.IsAttacking);
        }

        // ---------------------------------------------------------------
        // 噜噜：开灯 + 盯着门外那侧 2 秒
        // ---------------------------------------------------------------

        [Test]
        public void 噜噜_开灯并盯着那一侧两秒就会退走()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            // FixedRandom(1) 选边结果：Range(0,2) = 1 → 右侧
            var watching = new EnemyContext(false, false, false, true, false, ViewSide.Right, 0);

            AdvanceToStage(brain, watching, 2);
            Assert.AreEqual(Side.Right, brain.Side);

            brain.Tick(1f, watching);
            Assert.IsFalse(brain.IsAttacking);
            Assert.AreEqual(1f, brain.StareProgress, Tolerance);

            brain.Tick(1f, watching);
            Assert.AreEqual(0, brain.Stage);
            Assert.IsTrue(brain.RetreatedThisTick);
        }

        [Test]
        public void 噜噜_只开灯不盯着_到时间会动手()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            // 右侧灯亮着，但玩家看着正前方
            var ctx = new EnemyContext(false, false, false, true, false, ViewSide.Front, 0);

            AdvanceToStage(brain, ctx, 2);

            brain.Tick(brain.OutsideGraceSeconds, ctx);

            Assert.IsTrue(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_盯着但没开灯_到时间会动手()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            // 看着右侧，但灯没开
            var ctx = new EnemyContext(false, false, false, false, false, ViewSide.Right, 0);

            AdvanceToStage(brain, ctx, 2);

            brain.Tick(brain.OutsideGraceSeconds, ctx);

            Assert.IsTrue(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_视线中断则重新计时()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var watching = new EnemyContext(false, false, false, true, false, ViewSide.Right, 0);
            var lookingAway = new EnemyContext(false, false, false, true, false, ViewSide.Front, 0);

            AdvanceToStage(brain, watching, 2);

            brain.Tick(1.5f, watching);
            Assert.AreEqual(1.5f, brain.StareProgress, Tolerance);

            // 转开视线 → 计时归零
            brain.Tick(0.5f, lookingAway);
            Assert.AreEqual(0f, brain.StareProgress, Tolerance);

            // 转回来，重新数
            brain.Tick(1f, watching);
            Assert.AreEqual(1f, brain.StareProgress, Tolerance);
            Assert.IsFalse(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_开着监控不算盯着()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = new EnemyContext(false, false, false, true, true, ViewSide.Right, 0);

            AdvanceToStage(brain, ctx, 2);
            brain.Tick(3f, ctx);

            Assert.AreEqual(0f, brain.StareProgress, Tolerance);
        }

        // --- 门外宽限跟活动度走 ---

        [Test]
        public void 噜噜_活动度越高门外宽限越短()
        {
            var easy = new LuluBrain(EnemyId.Lulu, 0, new FixedRandom(1));
            var hard = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));

            Assert.AreEqual(LuluBrain.GraceAtZeroActivity, easy.OutsideGraceSeconds, Tolerance);
            Assert.AreEqual(LuluBrain.GraceAtMaxActivity, hard.OutsideGraceSeconds, Tolerance);
            Assert.Greater(easy.OutsideGraceSeconds, hard.OutsideGraceSeconds);
        }

        [Test]
        public void 噜噜_宽限按活动度线性折算_两端会被夹住()
        {
            var brain = new LuluBrain(EnemyId.Lulu, 0, new FixedRandom(1));
            float mid = (LuluBrain.GraceAtZeroActivity + LuluBrain.GraceAtMaxActivity) * 0.5f;

            Assert.AreEqual(mid, brain.GraceForActivity(10), Tolerance);
            Assert.AreEqual(LuluBrain.GraceAtZeroActivity, brain.GraceForActivity(-5), Tolerance);
            Assert.AreEqual(LuluBrain.GraceAtMaxActivity,
                brain.GraceForActivity(EnemyBrain.MaxActivityLevel + 5), Tolerance);
        }

        [Test]
        public void 噜噜_宽限的两端可以按项目改()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            Assert.AreEqual(LuluBrain.GraceAtMaxActivity, brain.OutsideGraceSeconds, Tolerance);

            brain.SetGraceRange(20f, 10f);

            Assert.AreEqual(10f, brain.OutsideGraceSeconds, Tolerance);
            Assert.AreEqual(20f, brain.GraceForActivity(0), Tolerance);
        }

        [Test]
        public void 噜噜_切夜时宽限跟着活动度重算()
        {
            var brain = new LuluBrain(EnemyId.Lulu, 0, new FixedRandom(1));
            Assert.AreEqual(LuluBrain.GraceAtZeroActivity, brain.OutsideGraceSeconds, Tolerance);

            brain.SetActivityLevel(EnemyBrain.MaxActivityLevel);

            Assert.AreEqual(LuluBrain.GraceAtMaxActivity, brain.OutsideGraceSeconds, Tolerance);
        }

        [Test]
        public void 噜噜_显式传的宽限不受活动度影响()
        {
            var brain = new LuluBrain(EnemyId.Lulu, 0, new FixedRandom(1), 5f, 2f, 3f);

            brain.SetActivityLevel(EnemyBrain.MaxActivityLevel);

            Assert.AreEqual(3f, brain.OutsideGraceSeconds, Tolerance);
        }

        [Test]
        public void 噜噜_默认曲线下第1夜比隐藏夜宽松得多()
        {
            var brain = new LuluBrain(EnemyId.Lulu, 0, new FixedRandom(1));
            var difficulty = NightDifficulty.CreateDefault();

            float night1 = brain.GraceForActivity(difficulty.GetLevel(0, EnemyId.Lulu));
            float hidden = brain.GraceForActivity(
                difficulty.GetLevel(NightDifficulty.NightCount - 1, EnemyId.Lulu));

            Assert.AreEqual(15.6f, night1, 0.1f);
            Assert.AreEqual(9.2f, hidden, 0.1f);
        }

        [Test]
        public void 噜噜_低活动度给的时间足够转头开灯再盯满两秒()
        {
            // 第 1 夜：活动度 1 → 15.6 秒，扣掉盯视的 2 秒还剩 13 秒多留给玩家反应
            var brain = new LuluBrain(EnemyId.Lulu, 1, new FixedRandom(1));
            var watching = new EnemyContext(false, false, false, true, false, ViewSide.Right, 0);

            AdvanceToStage(brain, watching, 2);

            brain.Tick(LuluBrain.DefaultStareSeconds, watching);

            Assert.AreEqual(0, brain.Stage);
            Assert.IsFalse(brain.IsAttacking);
        }

        // ---------------------------------------------------------------
        // 奶龙：偷电 + 摸右门，看监控即止
        // ---------------------------------------------------------------

        [Test]
        public void 奶龙_活动度越高偷电越快()
        {
            var slowest = new NailongBrain(0, new FixedRandom(1), 3);
            var fastest = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);

            Assert.AreEqual(NailongBrain.DefaultSlowStealIntervalSeconds,
                slowest.StealIntervalSeconds, Tolerance);
            Assert.AreEqual(NailongBrain.DefaultFastStealIntervalSeconds,
                fastest.StealIntervalSeconds, Tolerance);
            Assert.Greater(slowest.StealIntervalSeconds, fastest.StealIntervalSeconds);
        }

        [Test]
        public void 奶龙_走的是右门()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);

            Assert.AreEqual(Side.Right, brain.Side);
        }

        [Test]
        public void 奶龙_活动度为零时整夜不偷电也不推进()
        {
            var brain = new NailongBrain(0, new FixedRandom(1), 3);
            var ctx = EnemyContext.Idle();

            for (int i = 0; i < 200; i++)
            {
                brain.Tick(1f, ctx);
            }

            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
            Assert.AreEqual(0, brain.Stage);
        }

        [Test]
        public void 奶龙_没人看时按间隔偷电()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var ctx = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, ctx);

            Assert.AreEqual(1f, brain.ConsumePendingDrain(), Tolerance);

            // 取过之后就清零了
            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
        }

        [Test]
        public void 奶龙_偷到电就沿右门推进一步()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var ctx = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, ctx);

            Assert.AreEqual(1f, brain.ConsumePendingDrain(), Tolerance);
            Assert.AreEqual(1, brain.Stage);
            Assert.AreEqual(1, brain.AdvanceCount);
        }

        [Test]
        public void 奶龙_掷骰没过就不推进()
        {
            // 活动度 1，骰子固定掷 20 —— 过不了
            var brain = new NailongBrain(1, new FixedRandom(EnemyBrain.MaxActivityLevel), 3);
            var ctx = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, ctx);

            Assert.AreEqual(1f, brain.ConsumePendingDrain(), Tolerance);
            Assert.AreEqual(0, brain.Stage);
            Assert.AreEqual(0, brain.AdvanceCount);
        }

        [Test]
        public void 奶龙_右门口站着别人时在走廊等()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);

            // 袋鼠已经站在右门口
            var taken = new EnemyContext(false, false, false, false, false,
                ViewSide.Front, 0, EnemyId.Daishu);

            brain.Tick(brain.StealIntervalSeconds, taken);
            Assert.AreEqual(1, brain.Stage);

            // 再偷一次：走廊 → 门口这一步被挡住，停在走廊
            brain.Tick(brain.StealIntervalSeconds, taken);
            Assert.AreEqual(1, brain.Stage);
        }

        [Test]
        public void 奶龙_门口站着的是自己就不挡()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);

            var mine = new EnemyContext(false, false, false, false, false,
                ViewSide.Front, 0, EnemyId.Nailong);

            brain.Tick(brain.StealIntervalSeconds, mine);
            brain.Tick(brain.StealIntervalSeconds, mine);

            Assert.AreEqual(2, brain.Stage);
        }

        [Test]
        public void 奶龙_到了右门口门关着会被挡回起点()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var idle = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, idle);
            brain.Tick(brain.StealIntervalSeconds, idle);
            Assert.AreEqual(2, brain.Stage);

            var closed = new EnemyContext(false, true, false, false, false, ViewSide.Front, 0);
            brain.Tick(0.1f, closed);

            Assert.AreEqual(0, brain.Stage);
        }

        [Test]
        public void 奶龙_到了右门口门开着撑够就动手()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var idle = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, idle);
            brain.Tick(brain.StealIntervalSeconds, idle);
            Assert.AreEqual(2, brain.Stage);

            brain.Tick(NailongBrain.DefaultDoorGraceSeconds, idle);

            Assert.IsTrue(brain.IsAttacking);
        }

        [Test]
        public void 奶龙_被监控看到就换位并停手()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            int original = brain.CameraIndex;

            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, original);
            brain.Tick(NailongBrain.DefaultRevealSeconds, ctx);

            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
            Assert.AreEqual(0, brain.Stage);
            Assert.AreNotEqual(original, brain.CameraIndex);
            Assert.AreEqual(1, brain.RelocateCount);
        }

        [Test]
        public void 奶龙_刚被看到不会立刻闪走()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            int original = brain.CameraIndex;

            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, original);
            brain.Tick(0.3f, ctx);

            Assert.IsTrue(brain.IsBeingWatched);
            Assert.AreEqual(original, brain.CameraIndex);
            Assert.AreEqual(0, brain.RelocateCount);
            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
        }

        [Test]
        public void 奶龙_被盯着够久才换位()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            int original = brain.CameraIndex;

            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, original);

            brain.Tick(0.3f, ctx);
            Assert.AreEqual(original, brain.CameraIndex);

            brain.Tick(NailongBrain.DefaultRevealSeconds, ctx);

            Assert.IsFalse(brain.IsBeingWatched);
            Assert.AreNotEqual(original, brain.CameraIndex);
            Assert.AreEqual(1, brain.RelocateCount);
        }

        [Test]
        public void 奶龙_视线移开后要重新计时()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            int original = brain.CameraIndex;
            int other = (original + 1) % 3;

            var watching = new EnemyContext(false, false, false, false, true, ViewSide.Front, original);
            var away = new EnemyContext(false, false, false, false, true, ViewSide.Front, other);

            brain.Tick(0.5f, watching);
            Assert.IsTrue(brain.IsBeingWatched);

            brain.Tick(0.1f, away);
            Assert.IsFalse(brain.IsBeingWatched);
            Assert.AreEqual(0f, brain.RevealTimer, Tolerance);

            // 再切回来看，得从头数够
            brain.Tick(0.5f, watching);

            Assert.AreEqual(original, brain.CameraIndex);
            Assert.AreEqual(0, brain.RelocateCount);
        }

        [Test]
        public void 奶龙_看的是别的机位则继续偷电()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            int other = (brain.CameraIndex + 1) % 3;

            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, other);
            brain.Tick(brain.StealIntervalSeconds, ctx);

            Assert.AreEqual(1f, brain.ConsumePendingDrain(), Tolerance);
            Assert.AreEqual(0, brain.RelocateCount);
        }

        [Test]
        public void 奶龙只认一个机位时被看到不换位也不报错()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 1);
            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, 0);

            brain.Tick(NailongBrain.DefaultRevealSeconds, ctx);

            Assert.AreEqual(0, brain.CameraIndex);
            Assert.AreEqual(0, brain.RelocateCount);
            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
        }

        // ---------------------------------------------------------------
        // 断电序列（奶龙收电费）
        // ---------------------------------------------------------------

        [Test]
        public void 断电序列_没开始时不推进()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);

            sequence.Tick(100f);

            Assert.IsFalse(sequence.HasFired);
            Assert.AreEqual(0f, sequence.Elapsed, Tolerance);
            Assert.AreEqual(BlackoutPhase.Silence, sequence.Phase);
        }

        [Test]
        public void 断电序列_静默到音乐到漆黑依次推进()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);
            sequence.Begin();

            Assert.AreEqual(BlackoutPhase.Silence, sequence.Phase);

            sequence.Tick(1.9f);
            Assert.AreEqual(BlackoutPhase.Silence, sequence.Phase);

            // 跨过静默段 → 音乐盒响起
            sequence.Tick(0.2f);
            Assert.AreEqual(BlackoutPhase.Music, sequence.Phase);
            Assert.IsFalse(sequence.HasFired);

            sequence.Tick(9.5f);
            Assert.AreEqual(BlackoutPhase.Music, sequence.Phase);

            // 音乐播完 → 彻底漆黑
            sequence.Tick(0.5f);
            Assert.AreEqual(BlackoutPhase.Dark, sequence.Phase);
            Assert.IsFalse(sequence.HasFired);

            // 漆黑段走完才动手
            sequence.Tick(3f);
            Assert.IsTrue(sequence.HasFired);
            Assert.AreEqual(BlackoutPhase.Done, sequence.Phase);
        }

        [Test]
        public void 断电序列_音乐段的脸跟着拍子走()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);
            sequence.Begin();
            sequence.Tick(2f);

            Assert.AreEqual(BlackoutPhase.Music, sequence.Phase);
            Assert.AreEqual(0f, sequence.BeatPhase, Tolerance);

            // 半拍：相位到 0.5
            sequence.Tick(0.25f);
            Assert.AreEqual(0.5f, sequence.BeatPhase, Tolerance);

            // 再过半拍正好一整拍，回到 0
            sequence.Tick(0.25f);
            Assert.AreEqual(0f, sequence.BeatPhase, Tolerance);
        }

        [Test]
        public void 断电序列_音乐段长度可以按音频校准()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);
            sequence.SetMusicSeconds(20f);

            Assert.AreEqual(20f, sequence.MusicSeconds, Tolerance);

            sequence.Begin();
            sequence.Tick(2f);
            sequence.Tick(19f);
            Assert.AreEqual(BlackoutPhase.Music, sequence.Phase);

            sequence.Tick(1f);
            Assert.AreEqual(BlackoutPhase.Dark, sequence.Phase);
        }

        [Test]
        public void 断电序列_掉帧跨过整段也会动手()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);
            sequence.Begin();

            sequence.Tick(100f);

            Assert.IsTrue(sequence.HasFired);
        }

        [Test]
        public void 断电序列_到点后不再累计()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);
            sequence.Begin();

            sequence.Tick(6f);
            sequence.Tick(20f);
            Assert.IsTrue(sequence.HasFired);

            float atFire = sequence.TotalElapsed;
            sequence.Tick(10f);

            Assert.AreEqual(atFire, sequence.TotalElapsed, Tolerance);
        }

        [Test]
        public void 断电序列_复位后可以重来()
        {
            var sequence = new BlackoutSequence(2f, 10f, 3f, 0.5f);
            sequence.Begin();
            sequence.Tick(100f);
            Assert.IsTrue(sequence.HasFired);

            sequence.Reset();

            Assert.IsFalse(sequence.IsRunning);
            Assert.IsFalse(sequence.HasFired);
            Assert.AreEqual(0f, sequence.Elapsed, Tolerance);
            Assert.AreEqual(BlackoutPhase.Silence, sequence.Phase);

            sequence.Begin();
            sequence.Tick(1f);

            Assert.IsFalse(sequence.HasFired);
        }

        // ---------------------------------------------------------------
        // 难度表
        // ---------------------------------------------------------------

        [Test]
        public void 默认难度表_夜数越靠后活动度越高()
        {
            var difficulty = NightDifficulty.CreateDefault();

            for (int night = 0; night < NightDifficulty.NightCount - 1; night++)
            {
                foreach (var enemy in EnemyIds.All)
                {
                    int current = difficulty.GetLevel(night, enemy);
                    int next = difficulty.GetLevel(night + 1, enemy);

                    Assert.GreaterOrEqual(next, current,
                        enemy.Code() + " 在第 " + (night + 2) + " 夜的活动度不该低于前一夜");
                }
            }
        }

        [Test]
        public void 难度表_越界的夜数返回零()
        {
            var difficulty = NightDifficulty.CreateDefault();

            Assert.AreEqual(0, difficulty.GetLevel(-1, EnemyId.Nailong));
            Assert.AreEqual(0, difficulty.GetLevel(NightDifficulty.NightCount, EnemyId.Nailong));
        }

        [Test]
        public void 难度表_活动度被夹在合法范围()
        {
            var difficulty = new NightDifficulty();

            difficulty.SetLevel(0, EnemyId.Niulai, 99);
            Assert.AreEqual(EnemyBrain.MaxActivityLevel, difficulty.GetLevel(0, EnemyId.Niulai));

            difficulty.SetLevel(0, EnemyId.Niulai, -3);
            Assert.AreEqual(0, difficulty.GetLevel(0, EnemyId.Niulai));
        }
    }
}
