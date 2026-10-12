namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 噜噜：怕光。
    ///
    /// 路线：远处(0) → 走廊(1) → 门外(2，随机挑一侧)。
    /// 到了门外，必须「那一侧的灯亮着」且「视角正看着那一侧」连续盯够 StareSeconds，
    /// 它才退走；盯不满、或断断续续，撑过 OutsideGraceSeconds 就动手。
    ///
    /// 门外宽限不是定值 —— 它按活动度折算：活动度越高，留给玩家的时间越短。
    /// 详见 <see cref="GraceForActivity"/>。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class LuluBrain : EnemyBrain
    {
        /// <summary>需要连续盯着它多久（秒）。</summary>
        public const float DefaultStareSeconds = 2f;

        /// <summary>活动度 0（整夜不动）时的门外宽限（秒）—— 最宽松。默认值，可被覆盖。</summary>
        public const float GraceAtZeroActivity = 16f;

        /// <summary>活动度满级时的门外宽限（秒）—— 最紧。默认值，可被覆盖。</summary>
        public const float GraceAtMaxActivity = 7.5f;

        /// <summary>它现在蹲在哪一侧的门外。</summary>
        public Side Side { get; private set; }

        public float StareSeconds { get; private set; }

        /// <summary>
        /// 到了门外之后，总共给它多久就会动手（秒）。
        /// 默认由活动度折算；构造时显式传了正数就固定用那个值（测试与调试用）。
        /// </summary>
        public float OutsideGraceSeconds { get; private set; }

        /// <summary>当前已经连续盯了多久（秒），供 UI 显示进度。</summary>
        public float StareProgress { get; private set; }

        /// <summary>已经蹲在门外多久（秒）。</summary>
        public float OutsideTimer { get; private set; }

        /// <summary>宽限是否跟着活动度自动走（false = 构造时写死了）。</summary>
        private readonly bool _graceFollowsActivity;

        private float _graceAtZero = GraceAtZeroActivity;
        private float _graceAtMax = GraceAtMaxActivity;

        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng)
            : this(id, activityLevel, rng, DefaultMoveIntervalSeconds,
                DefaultStareSeconds, 0f)
        {
        }

        /// <summary>
        /// <paramref name="outsideGraceSeconds"/> 传 0 或不传 = 按活动度自动折算；
        /// 传正数 = 写死这个秒数（测试与调试用）。
        /// </summary>
        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, float stareSeconds, float outsideGraceSeconds)
            : base(id, activityLevel, rng, moveIntervalSeconds)
        {
            Side = Side.Left;
            StareSeconds = stareSeconds > 0f ? stareSeconds : DefaultStareSeconds;

            _graceFollowsActivity = outsideGraceSeconds <= 0f;
            OutsideGraceSeconds = _graceFollowsActivity
                ? GraceForActivity(ActivityLevel)
                : outsideGraceSeconds;
        }

        /// <summary>
        /// 活动度 → 门外宽限（秒）。线性：活动度 0 给最宽松的一端，满级给最紧的一端。
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
                // 一离开起点就把路线定下来 —— 这样它在走廊阶段就能被监控看到，
                // 玩家能提前知道它从哪边来（不然走到门口才现形，等于突然出现）
                Stage = 1;
                Side = Rng.Range(0, 2) == 0 ? Side.Left : Side.Right;
                return;
            }

            // 走到门外。边在走廊阶段已经定好，这里不再重掷
            Stage = 2;
            OutsideTimer = 0f;
            StareProgress = 0f;
        }

        protected override void OnTick(float dt, EnemyContext ctx)
        {
            if (Stage < MaxStage)
            {
                return;
            }

            bool lightOn = ctx.IsLightOn(Side);
            bool looking = ctx.IsLookingAt(Side);

            if (lightOn && looking)
            {
                StareProgress += dt;

                if (StareProgress >= StareSeconds)
                {
                    Retreat();
                    return;
                }
            }
            else
            {
                // 条件断了，重新数
                StareProgress = 0f;
            }

            OutsideTimer += dt;
            if (OutsideTimer >= OutsideGraceSeconds)
            {
                IsAttacking = true;
            }
        }

        public override void Retreat()
        {
            base.Retreat();
            OutsideTimer = 0f;
            StareProgress = 0f;
        }
    }
}
