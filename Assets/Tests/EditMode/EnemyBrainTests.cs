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

        /// <summary>确定性骰子：从下界开始 0,1,2,... 循环（用来把「换位」走遍整张机位表）。</summary>
        private sealed class CyclingRandom : IRandomSource
        {
            private int _next;

            public int Range(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive)
                {
                    return minInclusive;
                }

                int span = maxExclusive - minInclusive;
                int value = minInclusive + _next % span;
                _next++;
                return value;
            }
        }

        /// <summary>朝正前方按着手电筒的状态（噜噜的解法）。</summary>
        private static EnemyContext Shining()
        {
            return new EnemyContext(false, false, false, false, false,
                ViewSide.Front, 0, null, true);
        }

        /// <summary>
        /// 按一下手电筒再松手 —— **按次数计**，一次完整的按下算一次闪光。
        /// 按住不放只算一次，所以测试里必须走「按下 → 松开」这个来回。
        /// </summary>
        private static void PressFlashlight(LuluBrain brain)
        {
            brain.Tick(0.05f, Shining());
            brain.Tick(0.05f, EnemyContext.Idle());
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
        // 噜噜：从正门来，朝正前方按手电筒闪够几次（照 FNAF 2 的狐狸）
        // **按次数计** —— 一次完整的按下算一次闪光，按住不放只算一次
        // ---------------------------------------------------------------

        [Test]
        public void 噜噜_闪够次数就退走()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var shining = Shining();

            // 路上不能一直按着灯 —— 不然到门口那一下没有「上升沿」，白闪
            AdvanceToStage(brain, EnemyContext.Idle(), 2);

            for (int i = 0; i < LuluBrain.DefaultFlashesToRetreat - 1; i++)
            {
                PressFlashlight(brain);
                Assert.AreEqual(i + 1, brain.FlashCount);
                Assert.AreEqual(2, brain.Stage);
            }

            brain.Tick(0.05f, shining);     // 最后一次按下

            Assert.AreEqual(0, brain.Stage);
            Assert.IsTrue(brain.RetreatedThisTick);
            Assert.IsFalse(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_按住不放只算一次闪光()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var shining = Shining();

            AdvanceToStage(brain, EnemyContext.Idle(), 2);

            // 一口气按着不放 —— 只有「按下」那一下算数，想再闪一次得松手重按
            brain.Tick(5f, shining);

            Assert.AreEqual(1, brain.FlashCount);
            Assert.AreEqual(2, brain.Stage);
            Assert.IsFalse(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_松手再按才算下一次闪光()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var shining = Shining();

            AdvanceToStage(brain, EnemyContext.Idle(), 2);

            brain.Tick(0.5f, shining);
            Assert.AreEqual(1, brain.FlashCount);

            brain.Tick(0.5f, EnemyContext.Idle());   // 松手
            Assert.AreEqual(1, brain.FlashCount);

            brain.Tick(0.5f, shining);               // 再按下
            Assert.AreEqual(2, brain.FlashCount);
        }

        [Test]
        public void 噜噜_不照它_到时间就动手()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = EnemyContext.Idle();

            AdvanceToStage(brain, ctx, 2);

            brain.Tick(brain.OutsideGraceSeconds, ctx);

            Assert.IsTrue(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_每次闪光都把推进计时清零()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var shining = Shining();
            var off = EnemyContext.Idle();

            AdvanceToStage(brain, EnemyContext.Idle(), 2);

            // 先干等到快没时间
            float almost = brain.OutsideGraceSeconds - 0.1f;
            brain.Tick(almost, off);
            Assert.IsFalse(brain.IsAttacking);

            // 按一下 → 计数 +1、计时归零，又能拖一会儿
            brain.Tick(0.05f, shining);
            Assert.AreEqual(1, brain.FlashCount);
            Assert.AreEqual(0f, brain.SinceFlashTimer, Tolerance);

            brain.Tick(almost, off);
            Assert.IsFalse(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_转头看两边不算照它()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = new EnemyContext(false, false, false, false, false,
                ViewSide.Left, 0, null, true);

            AdvanceToStage(brain, ctx, 2);
            brain.Tick(3f, ctx);

            Assert.AreEqual(0, brain.FlashCount);
        }

        [Test]
        public void 噜噜_开着监控不算照它()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var ctx = new EnemyContext(false, false, false, false, true,
                ViewSide.Front, 0, null, true);

            AdvanceToStage(brain, ctx, 2);
            brain.Tick(3f, ctx);

            Assert.AreEqual(0, brain.FlashCount);
        }

        [Test]
        public void 噜噜_还没到正门口时照它没用()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            var shining = Shining();

            AdvanceToStage(brain, EnemyContext.Idle(), 1);
            Assert.AreEqual(1, brain.Stage);

            brain.Tick(0.05f, shining);
            brain.Tick(0.05f, EnemyContext.Idle());

            Assert.AreEqual(0, brain.FlashCount);
            Assert.IsFalse(brain.IsAttacking);
        }

        // --- 闪光间隔的宽限跟活动度走 ---

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
            var brain = new LuluBrain(EnemyId.Lulu, 0, new FixedRandom(1),
                EnemyBrain.DefaultMoveIntervalSeconds,
                LuluBrain.DefaultFlashesToRetreat, 3f);

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
        public void 噜噜_低活动度给的时间足够把手电筒按起来闪完()
        {
            // 第 1 夜：活动度 1 → 15.6 秒。按 4 次手电筒不到 1 秒，
            // 剩下的时间全留给玩家反应
            var brain = new LuluBrain(EnemyId.Lulu, 1, new FixedRandom(1));

            AdvanceToStage(brain, EnemyContext.Idle(), 2);

            for (int i = 0; i < LuluBrain.DefaultFlashesToRetreat; i++)
            {
                PressFlashlight(brain);
            }

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
        public void 奶龙_配了推进路径时出现在路径机位()
        {
            // 绿子：进攻环节在监控里看不见奶龙 —— 配了路径后，
            // 每推进一格 CameraIndex 要跳到路径对应机位。
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            brain.SetCameraPath(new[] { 1, 6, 7 });   // 餐厅 → 东走廊 → 东转角
            var ctx = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, ctx);   // 推进到路径第 1 格
            Assert.AreEqual(1, brain.Stage);
            Assert.AreEqual(6, brain.CameraIndex, "第一步之后应停到东走廊（6）");

            brain.Tick(brain.StealIntervalSeconds, ctx);   // 推进到路径第 2 格
            Assert.AreEqual(2, brain.Stage);
            Assert.AreEqual(7, brain.CameraIndex, "第二步之后应停到东转角（7）");

            brain.Tick(brain.StealIntervalSeconds, ctx);   // 推进到门口
            Assert.AreEqual(3, brain.Stage);
            Assert.AreEqual(-1, brain.PathCameraIndex, "到门口后不再属于任何机位");
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

            // 牛来已经站在右门口（它和奶龙共用右门）
            var taken = new EnemyContext(false, false, false, false, false,
                ViewSide.Front, 0, EnemyId.Niulai);

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
        public void 奶龙_到了门口就不再看监控换位了()
        {
            // 站到门口之后它就不再是「蹲机位偷电」的状态 —— 监控里看不到它，
            // 换位也就无从谈起；这时的应对手段只剩关门。
            // 不加这个限制的话，它会一边换机位一边还占着右门口，自相矛盾。
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var idle = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, idle);
            brain.Tick(brain.StealIntervalSeconds, idle);
            Assert.AreEqual(2, brain.Stage);

            int original = brain.CameraIndex;
            var watching = new EnemyContext(false, false, false, false, true, ViewSide.Front, original);

            brain.Tick(NailongBrain.DefaultRevealSeconds * 2f, watching);

            Assert.AreEqual(original, brain.CameraIndex);
            Assert.AreEqual(0, brain.RelocateCount);
            Assert.IsFalse(brain.IsBeingWatched);
            Assert.AreEqual(2, brain.Stage);
        }

        [Test]
        public void 奶龙_到门口后监控照不到它就不会被门口宽限动手()
        {
            // 反过来说：到门口之后宽限计时照常走，看监控拦不住它。
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var idle = EnemyContext.Idle();

            brain.Tick(brain.StealIntervalSeconds, idle);
            brain.Tick(brain.StealIntervalSeconds, idle);
            Assert.AreEqual(2, brain.Stage);

            var watching = new EnemyContext(false, false, false, false, true, ViewSide.Front,
                brain.CameraIndex);
            brain.Tick(NailongBrain.DefaultDoorGraceSeconds + 0.1f, watching);

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

        [Test]
        public void 奶龙_不会去被剔掉的机位()
        {
            // 11 个机位里剔掉厨房（下标 9）
            int[] allowed = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 };
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new CyclingRandom(), allowed);

            Assert.AreEqual(10, brain.CameraCount, "能去的机位是 10 个");
            Assert.AreNotEqual(9, brain.CameraIndex, "一开始就不该落在被剔掉的机位上");

            // 反复被看到 → 反复换位，把整张表走一遍
            for (int i = 0; i < 60; i++)
            {
                int before = brain.CameraIndex;
                var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, before);
                brain.Tick(NailongBrain.DefaultRevealSeconds, ctx);

                Assert.AreNotEqual(9, brain.CameraIndex, "换位后也不该落在被剔掉的机位上");
                Assert.AreNotEqual(before, brain.CameraIndex, "换位必须换到别的机位");
            }

            Assert.Greater(brain.RelocateCount, 50);
        }

        [Test]
        public void 奶龙_允许表只剩一个机位时不换位()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), new int[] { 4 });

            Assert.AreEqual(1, brain.CameraCount);
            Assert.AreEqual(4, brain.CameraIndex);

            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, 4);
            brain.Tick(NailongBrain.DefaultRevealSeconds, ctx);

            Assert.AreEqual(4, brain.CameraIndex);
            Assert.AreEqual(0, brain.RelocateCount);
        }

        [Test]
        public void 奶龙_强制换到被剔掉的机位会被忽略()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1),
                new int[] { 0, 1, 3 });
            int before = brain.CameraIndex;

            brain.ForceCamera(9);
            Assert.AreEqual(before, brain.CameraIndex, "不在表里的机位应当忽略");

            brain.ForceCamera(3);
            Assert.AreEqual(3, brain.CameraIndex, "表里的机位照常生效");
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

        /// <summary>
        /// 门类敌人（袋鼠/牛来/奶龙）的活动度照 **FNAF 1 原版 AI 表**，
        /// 原版是有回落的（邦尼夜 3=0 < 夜 2=3），所以不能要求逐夜递增。
        /// 校验改成：前三夜总和 ≤ 后三夜总和（整体是变难的）。
        /// </summary>
        [Test]
        public void 默认难度表_门类敌人整体变难()
        {
            var difficulty = NightDifficulty.CreateDefault();

            EnemyId[] doorEnemies = { EnemyId.Niulai, EnemyId.Daishu, EnemyId.Nailong };
            foreach (var enemy in doorEnemies)
            {
                int firstHalf = 0;
                int secondHalf = 0;
                for (int night = 0; night < 3; night++)
                {
                    firstHalf += difficulty.GetLevel(night, enemy);
                }
                for (int night = 3; night < 6; night++)
                {
                    secondHalf += difficulty.GetLevel(night, enemy);
                }

                Assert.Greater(secondHalf, firstHalf,
                    enemy.Code() + " 后三夜总和应该大于前三夜（整体变难）");
            }
        }

        /// <summary>噜噜走手电筒线（不是门类），它仍要求逐夜递增。</summary>
        [Test]
        public void 默认难度表_噜噜逐夜递增()
        {
            var difficulty = NightDifficulty.CreateDefault();

            for (int night = 0; night < NightDifficulty.NightCount - 1; night++)
            {
                int current = difficulty.GetLevel(night, EnemyId.Lulu);
                int next = difficulty.GetLevel(night + 1, EnemyId.Lulu);
                Assert.GreaterOrEqual(next, current,
                    "噜噜 在第 " + (night + 2) + " 夜的活动度不该低于前一夜");
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
