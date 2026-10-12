namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 奶龙：不直接杀人，只偷电。
    ///
    /// 它待在某个监控机位，持续扣电（默认每 10 秒 1%，约等于多开一档设备）。
    /// 把监控切到它所在的机位看一眼，它就停手并换到别的机位。
    ///
    /// 它不走门、不看灯 —— 四个敌人里唯一需要「看监控」才能应对的。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class NailongBrain : EnemyBrain
    {
        /// <summary>每偷一次电的间隔（秒）。</summary>
        public const float DefaultStealIntervalSeconds = 10f;

        /// <summary>每偷一次扣多少电量（百分点）。</summary>
        public const float DefaultStealPercentPerTick = 1f;

        /// <summary>
        /// 被监控照到之后，它会在原地愣这么久才换位。
        /// 纯粹是给玩家看清它的时间 —— 否则一帧就闪走，等于没看见。
        /// </summary>
        public const float DefaultRevealSeconds = 1.2f;

        /// <summary>它现在蹲在第几个机位（0 起）。</summary>
        public int CameraIndex { get; private set; }

        /// <summary>监控一共有几个机位。</summary>
        public int CameraCount { get; private set; }

        public float StealIntervalSeconds { get; private set; }
        public float StealPercentPerTick { get; private set; }
        public float RevealSeconds { get; private set; }

        /// <summary>正被玩家从监控里看着。</summary>
        public bool IsBeingWatched { get; private set; }

        /// <summary>已经被看了多久（秒）。</summary>
        public float RevealTimer { get; private set; }

        /// <summary>被看到而换位的次数，供调试显示。</summary>
        public int RelocateCount { get; private set; }

        private float _stealTimer;
        private float _pendingDrain;

        public NailongBrain(int activityLevel, IRandomSource rng, int cameraCount)
            : this(activityLevel, rng, cameraCount, DefaultMoveIntervalSeconds,
                DefaultStealIntervalSeconds, DefaultStealPercentPerTick)
        {
        }

        public NailongBrain(int activityLevel, IRandomSource rng, int cameraCount,
            float moveIntervalSeconds, float stealIntervalSeconds, float stealPercentPerTick)
            : this(activityLevel, rng, cameraCount, moveIntervalSeconds,
                stealIntervalSeconds, stealPercentPerTick, DefaultRevealSeconds)
        {
        }

        public NailongBrain(int activityLevel, IRandomSource rng, int cameraCount,
            float moveIntervalSeconds, float stealIntervalSeconds, float stealPercentPerTick,
            float revealSeconds)
            : base(EnemyId.Nailong, activityLevel, rng, moveIntervalSeconds)
        {
            CameraCount = cameraCount < 1 ? 1 : cameraCount;
            CameraIndex = Rng.Range(0, CameraCount);
            StealIntervalSeconds = stealIntervalSeconds > 0f
                ? stealIntervalSeconds
                : DefaultStealIntervalSeconds;
            StealPercentPerTick = stealPercentPerTick > 0f
                ? stealPercentPerTick
                : DefaultStealPercentPerTick;
            RevealSeconds = revealSeconds > 0f ? revealSeconds : DefaultRevealSeconds;
        }

        /// <summary>奶龙不走路，永远不推进位置。</summary>
        protected override bool CanAdvance(EnemyContext ctx)
        {
            return false;
        }

        protected override void Advance(EnemyContext ctx)
        {
        }

        protected override void OnTick(float dt, EnemyContext ctx)
        {
            // 监控正停在他这一格 → 现形、停手
            if (ctx.MonitorUp && ctx.CameraIndex == CameraIndex)
            {
                IsBeingWatched = true;
                RevealTimer += dt;

                if (RevealTimer >= RevealSeconds)
                {
                    IsBeingWatched = false;
                    RevealTimer = 0f;
                    Relocate();
                }

                return;
            }

            IsBeingWatched = false;
            RevealTimer = 0f;

            _stealTimer += dt;

            while (_stealTimer >= StealIntervalSeconds)
            {
                _stealTimer -= StealIntervalSeconds;
                _pendingDrain += StealPercentPerTick;
            }
        }

        /// <summary>取出本帧累积的偷电量（并清零）。由 EnemyDirector 交给电量系统。</summary>
        public float ConsumePendingDrain()
        {
            float drain = _pendingDrain;
            _pendingDrain = 0f;
            return drain;
        }

        /// <summary>强制换到指定机位（调试 / 测试用）。</summary>
        public void ForceCamera(int index)
        {
            if (index < 0 || index >= CameraCount)
            {
                return;
            }

            CameraIndex = index;
            _stealTimer = 0f;
            RevealTimer = 0f;
            IsBeingWatched = false;
        }

        private void Relocate()
        {
            _stealTimer = 0f;

            if (CameraCount <= 1)
            {
                return;
            }

            // 随机挑一个「不同的」机位
            int next = Rng.Range(0, CameraCount - 1);
            if (next >= CameraIndex)
            {
                next++;
            }

            CameraIndex = next;
            RelocateCount++;
        }
    }
}
