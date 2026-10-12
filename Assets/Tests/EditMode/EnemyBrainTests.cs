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

            brain.Tick(LuluBrain.DefaultOutsideGraceSeconds, ctx);

            Assert.IsTrue(brain.IsAttacking);
        }

        [Test]
        public void 噜噜_盯着但没开灯_到时间会动手()
        {
            var brain = new LuluBrain(EnemyId.Lulu, EnemyBrain.MaxActivityLevel, new FixedRandom(1));
            // 看着右侧，但灯没开
            var ctx = new EnemyContext(false, false, false, false, false, ViewSide.Right, 0);

            AdvanceToStage(brain, ctx, 2);

            brain.Tick(LuluBrain.DefaultOutsideGraceSeconds, ctx);

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

        // ---------------------------------------------------------------
        // 奶龙：偷电，看监控即止
        // ---------------------------------------------------------------

        [Test]
        public void 奶龙_没人看时按间隔偷电()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var ctx = EnemyContext.Idle();

            brain.Tick(NailongBrain.DefaultStealIntervalSeconds, ctx);

            Assert.AreEqual(1f, brain.ConsumePendingDrain(), Tolerance);

            // 取过之后就清零了
            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
        }

        [Test]
        public void 奶龙_被监控看到就换位并停手()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            int original = brain.CameraIndex;

            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, original);
            brain.Tick(NailongBrain.DefaultStealIntervalSeconds, ctx);

            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
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
            brain.Tick(NailongBrain.DefaultStealIntervalSeconds, ctx);

            Assert.AreEqual(1f, brain.ConsumePendingDrain(), Tolerance);
            Assert.AreEqual(0, brain.RelocateCount);
        }

        [Test]
        public void 奶龙永远不会攻击()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 3);
            var ctx = EnemyContext.Idle();

            for (int i = 0; i < 500; i++)
            {
                brain.Tick(1f, ctx);
            }

            Assert.IsFalse(brain.IsAttacking);
        }

        [Test]
        public void 奶龙只认一个机位时被看到不换位也不报错()
        {
            var brain = new NailongBrain(EnemyBrain.MaxActivityLevel, new FixedRandom(1), 1);
            var ctx = new EnemyContext(false, false, false, false, true, ViewSide.Front, 0);

            brain.Tick(NailongBrain.DefaultStealIntervalSeconds, ctx);

            Assert.AreEqual(0, brain.CameraIndex);
            Assert.AreEqual(0, brain.RelocateCount);
            Assert.AreEqual(0f, brain.ConsumePendingDrain(), Tolerance);
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
