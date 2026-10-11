namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 噜噜：怕光。
    ///
    /// 路线：远处(0) → 走廊(1) → 门外(2，随机挑一侧)。
    /// 到了门外，必须「那一侧的灯亮着」且「视角正看着那一侧」连续盯够 StareSeconds，
    /// 它才退走；盯不满、或断断续续，撑过 OutsideGraceSeconds 就动手。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class LuluBrain : EnemyBrain
    {
        /// <summary>需要连续盯着它多久（秒）。</summary>
        public const float DefaultStareSeconds = 2f;

        /// <summary>到了门外之后，总共给它多久就会动手（秒）。</summary>
        public const float DefaultOutsideGraceSeconds = 5f;

        /// <summary>它现在蹲在哪一侧的门外。</summary>
        public Side Side { get; private set; }

        public float StareSeconds { get; private set; }
        public float OutsideGraceSeconds { get; private set; }

        /// <summary>当前已经连续盯了多久（秒），供 UI 显示进度。</summary>
        public float StareProgress { get; private set; }

        /// <summary>已经蹲在门外多久（秒）。</summary>
        public float OutsideTimer { get; private set; }

        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng)
            : this(id, activityLevel, rng, DefaultMoveIntervalSeconds,
                DefaultStareSeconds, DefaultOutsideGraceSeconds)
        {
        }

        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, float stareSeconds, float outsideGraceSeconds)
            : base(id, activityLevel, rng, moveIntervalSeconds)
        {
            Side = Side.Left;
            StareSeconds = stareSeconds > 0f ? stareSeconds : DefaultStareSeconds;
            OutsideGraceSeconds = outsideGraceSeconds > 0f
                ? outsideGraceSeconds
                : DefaultOutsideGraceSeconds;
        }

        protected override void Advance(EnemyContext ctx)
        {
            if (Stage == 0)
            {
                Stage = 1;
                return;
            }

            // 走到门外，随机挑一侧
            Stage = 2;
            Side = Rng.Range(0, 2) == 0 ? Side.Left : Side.Right;
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
