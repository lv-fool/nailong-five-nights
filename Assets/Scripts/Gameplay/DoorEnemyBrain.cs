namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 门类敌人：牛来（左门）与袋鼠（右门）。
    ///
    /// 路线：远处(0) → 走廊(1) → 门口(2)。
    /// 到了门口：门关着 → 被挡回去；门开着 → 撑过 DoorGraceSeconds 就动手。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class DoorEnemyBrain : EnemyBrain
    {
        /// <summary>到门口后，门开着能撑多久（秒）。给玩家留出关门的时间。</summary>
        public const float DefaultDoorGraceSeconds = 3f;

        /// <summary>它盯的是哪一侧的门。</summary>
        public Side Side { get; private set; }

        /// <summary>到门口后，门开着能撑多久（秒）。</summary>
        public float DoorGraceSeconds { get; private set; }

        /// <summary>已经站在门口多久（秒）。</summary>
        public float DoorTimer { get; private set; }

        public DoorEnemyBrain(EnemyId id, Side side, int activityLevel, IRandomSource rng)
            : this(id, side, activityLevel, rng, DefaultMoveIntervalSeconds, DefaultDoorGraceSeconds)
        {
        }

        public DoorEnemyBrain(EnemyId id, Side side, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, float doorGraceSeconds)
            : base(id, activityLevel, rng, moveIntervalSeconds)
        {
            Side = side;
            DoorGraceSeconds = doorGraceSeconds > 0f ? doorGraceSeconds : DefaultDoorGraceSeconds;
        }

        protected override void Advance(EnemyContext ctx)
        {
            Stage = Stage + 1;

            if (Stage >= MaxStage)
            {
                DoorTimer = 0f;
            }
        }

        protected override void OnTick(float dt, EnemyContext ctx)
        {
            if (Stage < MaxStage)
            {
                return;
            }

            // 门关着 —— 直接挡回去
            if (ctx.IsDoorClosed(Side))
            {
                Retreat();
                return;
            }

            DoorTimer += dt;
            if (DoorTimer >= DoorGraceSeconds)
            {
                IsAttacking = true;
            }
        }

        public override void Retreat()
        {
            base.Retreat();
            DoorTimer = 0f;
        }
    }
}
