using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 「袋鼠被门挡住 → 敲门 → 退走后掉外卖」这条链路的回归测试。
    ///
    /// 背景：2026-11-03 把「被门挡住立刻进最后一搏」加进去之后，
    /// 敲门判定（原本挂在 justFilled 上）悄悄失效了，外卖再也不掉 —— 而
    /// <see cref="DeliveryDropTests"/> 只测了 DeliveryDrop 自己，测不出这个断层。
    ///
    /// 这里把两段串起来测：
    ///   1. 挡住门 → DoorEnemyBrain.KnocksIssued 要涨
    ///   2. ConsumeKnock 后 → DeliveryDrop.Drop 要成功
    ///
    /// 复刻的是 EnemyDirector 里那段调度逻辑（纯逻辑，不碰 MonoBehaviour）。
    /// </summary>
    [TestFixture]
    public class DeliveryChainTests
    {
        private const float Tolerance = 0.001f;

        /// <summary>
        /// 游走骰子固定掷出的值。
        ///
        /// ⚠️ **必须是 0**：Retreat() 里用 `Stage = 1 + Rng.Range(0, slots-1)`
        /// 把敌人甩到路径中间。测试配的路径是 {0, 1}（slots=2），
        /// 骰子掷 1 的话 Stage = 1 + 1 = 2 = 又回到门口，看着像「没退走」（踩过）。
        /// 掷 0 才是 Stage = 1，确实退到走廊。
        /// </summary>
        private const int RngForWander = 0;

        /// <summary>骰子永远掷出同一个数 —— 让「必定前进 / 必定不动」可断言。</summary>
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

        /// <summary>
        /// 造一个「已经站在门口」的袋鼠。
        ///
        /// ⚠️ DebugTeleport 的参数是**机位号**，不是 Stage ——
        /// 传 -1 才是「站到门口（Stage 到顶）」。而且必须先 SetCameraPath，
        /// 没配路径的话 DebugTeleport 直接 return，敌人还留在 Stage 0。
        /// </summary>
        private static DoorEnemyBrain MakeBlockedDaishu(IRandomSource rng)
        {
            var brain = new DoorEnemyBrain(EnemyId.Daishu, Side.Left,
                EnemyBrain.MaxActivityLevel, rng);

            brain.SetCameraPath(new[] { 0, 1 });   // 先配路径
            brain.DebugTeleport(-1);               // 再传 -1 = 到门口
            return brain;
        }

        /// <summary>门关着、灯灭的上下文。</summary>
        private static EnemyContext DoorClosed()
        {
            return new EnemyContext(true, false, false, false, false, ViewSide.Front, 0);
        }

        [Test]
        public void 被门挡住会敲门()
        {
            var brain = MakeBlockedDaishu(new FixedRandom(RngForWander));
            var ctx = DoorClosed();

            Assert.AreEqual(0, brain.KnocksIssued, "刚开始不该有敲门记录");

            brain.Tick(0.1f, ctx);

            Assert.AreEqual(1, brain.KnocksIssued,
                "到门口发现门关着 → 应当敲一次门（外卖依赖这个）");
        }

        [Test]
        public void 门一直关着只敲一次()
        {
            var brain = MakeBlockedDaishu(new FixedRandom(RngForWander));
            var ctx = DoorClosed();

            brain.Tick(0.1f, ctx);
            Assert.AreEqual(1, brain.KnocksIssued);

            // 继续站着（最后一搏还没走完）—— 不该每帧都敲
            brain.Tick(brain.LastChanceSeconds * 0.3f, ctx);
            Assert.AreEqual(1, brain.KnocksIssued, "同一趟只敲一次");
        }

        [Test]
        public void 退走后再来可以再敲()
        {
            var brain = MakeBlockedDaishu(new FixedRandom(RngForWander));
            var ctx = DoorClosed();

            brain.Tick(0.1f, ctx);
            Assert.AreEqual(1, brain.KnocksIssued);

            // 最后一搏走完 → 退走。一次 Tick 给足 3 秒就够（LastChanceSeconds=3）
            brain.Tick(brain.LastChanceSeconds + 0.5f, ctx);

            Assert.Less(brain.Stage, brain.MaxStage,
                "应当已经退了。实际 Stage=" + brain.Stage
                + " MaxStage=" + brain.MaxStage
                + " DoorTimer=" + brain.DoorTimer.ToString("F2")
                + " LastChanceTimer=" + brain.LastChanceTimer.ToString("F2")
                + " LastChanceSeconds=" + brain.LastChanceSeconds.ToString("F2")
                + " IsAttacking=" + brain.IsAttacking);

            // 再来一次（DebugTeleport 传 -1 = 站到门口）
            brain.DebugTeleport(-1);
            brain.Tick(0.1f, ctx);

            Assert.AreEqual(2, brain.KnocksIssued, "第二次上门应当能再敲");
        }

        [Test]
        public void 门关着没敲过就不该有敲门记录()
        {
            // 反证：门开着时它直接动手，不该敲
            var brain = new DoorEnemyBrain(EnemyId.Daishu, Side.Left,
                EnemyBrain.MaxActivityLevel, new FixedRandom(RngForWander));
            brain.SetCameraPath(new[] { 0, 1 });
            brain.DebugTeleport(-1);

            var ctx = EnemyContext.Idle();   // 门开着
            brain.Tick(0.1f, ctx);

            Assert.AreEqual(0, brain.KnocksIssued, "门开着是扑上来，不是敲门");
        }

        [Test]
        public void 敲门之后外卖能掉下来()
        {
            // ── 完整链路：挡住门 → 敲门 → 退走 → 掉外卖 ──
            var brain = MakeBlockedDaishu(new FixedRandom(RngForWander));
            var delivery = new DeliveryDrop();
            var ctx = DoorClosed();

            // ① 挡住 → 敲门
            brain.Tick(0.1f, ctx);
            Assert.AreEqual(1, brain.KnocksIssued, "先要有敲门");

            // ② 复刻 EnemyDirector 的调度：取走敲门记录 → 标记「它要走人了」
            bool willDrop = false;
            if (brain.ConsumeKnock())
            {
                willDrop = true;
            }

            Assert.IsTrue(willDrop, "敲门记录应当能被调度层取走");
            Assert.IsFalse(delivery.Available, "它还没走，外卖不该现在就出现");

            // ③ 最后一搏走完 → 它真的退走了 → 这时候才掉外卖
            brain.Tick(brain.LastChanceSeconds + 0.5f, ctx);

            Assert.Less(brain.Stage, brain.MaxStage, "它应当已经退走");

            if (willDrop && brain.Stage < brain.MaxStage)
            {
                delivery.Drop(new FixedRandom(RngForWander));
            }

            Assert.IsTrue(delivery.Available, "退走后外卖应当出现在门口");

            bool knownGain = false;
            foreach (float g in DeliveryDrop.Gains)
            {
                if (System.Math.Abs(delivery.Gain - g) < Tolerance)
                {
                    knownGain = true;
                    break;
                }
            }

            Assert.IsTrue(knownGain, "电量数应当是 5/2/1 之一，实际 " + delivery.Gain);
        }

        [Test]
        public void 外卖二十秒后消失()
        {
            var delivery = new DeliveryDrop();
            delivery.Drop(new FixedRandom(RngForWander));

            Assert.IsTrue(delivery.Available);

            delivery.Tick(DeliveryDrop.LifetimeSeconds + 1f);

            Assert.IsFalse(delivery.Available, "超过 20 秒就没了");
        }
    }
}
