namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 噜噜：从**正门**摸进来，怕手电筒的光。
    ///
    /// 路线：远处(0) → 逼近正门(1) → 停在正门口(2)。
    /// 到了门口，玩家得**朝正前方按住手电筒照它**。
    ///
    /// 照法照 FNAF 2 的狐狸（Withered Foxy）：**照住一小段算一次有效闪光，
    /// 闪一下就把它逼退一步**（推进计时清零），**闪够 <see cref="FlashesToRetreat"/> 次才退走**。
    /// 光断了、或者一直不照，推进计时走满 <see cref="OutsideGraceSeconds"/> 它就动手。
    /// 所以是「不停地闪它」而不是「一口气盯够」—— 中间断太久照样会被它扑上来。
    ///
    /// 门外宽限不是定值 —— 它按活动度折算：活动度越高，留给玩家的时间越短。
    /// 详见 <see cref="GraceForActivity"/>。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class LuluBrain : EnemyBrain
    {
        /// <summary>一次有效闪光要照住多久（秒）。太短的乱点不算数。</summary>
        public const float DefaultFlashSeconds = 0.5f;

        /// <summary>闪够几次它就退走。</summary>
        public const int DefaultFlashesToRetreat = 4;

        /// <summary>活动度 0（整夜不动）时的门外宽限（秒）—— 最宽松。默认值，可被覆盖。</summary>
        public const float GraceAtZeroActivity = 16f;

        /// <summary>活动度满级时的门外宽限（秒）—— 最紧。默认值，可被覆盖。</summary>
        public const float GraceAtMaxActivity = 7.5f;

        /// <summary>一次有效闪光要照住多久（秒）。</summary>
        public float FlashSeconds { get; private set; }

        /// <summary>闪够几次它就退走。</summary>
        public int FlashesToRetreat { get; private set; }

        /// <summary>
        /// 到了门口之后，两次闪光之间最多能隔多久（秒）。
        /// 默认由活动度折算；构造时显式传了正数就固定用那个值（测试与调试用）。
        /// </summary>
        public float OutsideGraceSeconds { get; private set; }

        /// <summary>这一轮闪光已经照了多久（秒），照满 <see cref="FlashSeconds"/> 算一次。</summary>
        public float FlashProgress { get; private set; }

        /// <summary>这一轮已经被有效闪光逼退了几步。</summary>
        public int FlashCount { get; private set; }

        /// <summary>距离上一次闪光已经过了多久（秒）。</summary>
        public float SinceFlashTimer { get; private set; }

        /// <summary>宽限是否跟着活动度自动走（false = 构造时写死了）。</summary>
        private readonly bool _graceFollowsActivity;

        private float _graceAtZero = GraceAtZeroActivity;
        private float _graceAtMax = GraceAtMaxActivity;

        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng)
            : this(id, activityLevel, rng, DefaultMoveIntervalSeconds,
                DefaultFlashSeconds, DefaultFlashesToRetreat, 0f)
        {
        }

        /// <summary>
        /// <paramref name="outsideGraceSeconds"/> 传 0 或不传 = 按活动度自动折算；
        /// 传正数 = 写死这个秒数（测试与调试用）。
        /// </summary>
        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, float flashSeconds, int flashesToRetreat,
            float outsideGraceSeconds)
            : base(id, activityLevel, rng, moveIntervalSeconds)
        {
            FlashSeconds = flashSeconds > 0f ? flashSeconds : DefaultFlashSeconds;
            FlashesToRetreat = flashesToRetreat > 0 ? flashesToRetreat : DefaultFlashesToRetreat;

            _graceFollowsActivity = outsideGraceSeconds <= 0f;
            OutsideGraceSeconds = _graceFollowsActivity
                ? GraceForActivity(ActivityLevel)
                : outsideGraceSeconds;
        }

        /// <summary>
        /// 活动度 → 两次闪光之间最多能隔多久（秒）。
        /// 线性：活动度 0 给最宽松的一端，满级给最紧的一端。
        ///
        /// 套默认难度曲线大致是：第 1 夜 15.6 秒、第 3 夜 14.3 秒、第 5 夜 11.8 秒、
        /// 隐藏夜 9.2 秒。
        /// </summary>
        public float GraceForActivity(int activityLevel)
        {
            int level = activityLevel < 0
                ? 0
                : (activityLevel > MaxActivityLevel ? MaxActivityLevel : activityLevel);

            float t = level / (float)MaxActivityLevel;
            return _graceAtZero + (_graceAtMax - _graceAtZero) * t;
        }

        /// <summary>改宽限曲线的两端（Inspector 注入）。传 0 或负数表示保持原样。</summary>
        public void SetGraceRange(float atZeroActivity, float atMaxActivity)
        {
            if (atZeroActivity > 0f)
            {
                _graceAtZero = atZeroActivity;
            }

            if (atMaxActivity > 0f)
            {
                _graceAtMax = atMaxActivity;
            }

            if (_graceFollowsActivity)
            {
                OutsideGraceSeconds = GraceForActivity(ActivityLevel);
            }
        }

        /// <summary>切夜时活动度会变，宽限跟着重算（除非构造时写死了）。</summary>
        public override void SetActivityLevel(int level)
        {
            base.SetActivityLevel(level);

            if (_graceFollowsActivity)
            {
                OutsideGraceSeconds = GraceForActivity(ActivityLevel);
            }
        }

        protected override void Advance(EnemyContext ctx)
        {
            if (Stage == 0)
            {
                // 离开起点：开始逼近正门（这时它就在正前方，玩家能看见它过来）
                Stage = 1;
                return;
            }

            // 走到正门口。没有左右之分 —— 它从正前方来
            Stage = 2;
            SinceFlashTimer = 0f;
            FlashProgress = 0f;
            FlashCount = 0;
        }

        protected override void OnTick(float dt, EnemyContext ctx)
        {
            if (Stage < MaxStage)
            {
                return;
            }

            // 手电筒正照着它 → 累计「照住」的时长
            bool flashed = false;

            if (ctx.IsShiningAtFront())
            {
                FlashProgress += dt;

                if (FlashProgress >= FlashSeconds)
                {
                    FlashProgress = 0f;
                    FlashCount++;
                    flashed = true;

                    if (FlashCount >= FlashesToRetreat)
                    {
                        Retreat();
                        return;
                    }
                }
            }
            else
            {
                // 光断了，这一轮的照住时长作废，得重新数
                FlashProgress = 0f;
            }

            // 闪一下就把它逼退一步：推进计时清零（照 FNAF 2 狐狸的 D 值）
            SinceFlashTimer = flashed ? 0f : SinceFlashTimer + dt;

            if (SinceFlashTimer >= OutsideGraceSeconds)
            {
                IsAttacking = true;
            }
        }

        public override void Retreat()
        {
            base.Retreat();
            SinceFlashTimer = 0f;
            FlashProgress = 0f;
            FlashCount = 0;
        }
    }
}
