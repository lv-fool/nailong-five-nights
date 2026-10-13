namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 奶龙：一边偷电，一边摸右门。
    ///
    /// 它平时蹲在某个监控机位偷电。**每偷到一次电，就沿右门那条路尝试推进一格**
    /// （同 FNAF 的掷骰：不超过活动度才算数）。所以它偷得越勤，来得越快。
    ///
    /// 应对方式还是看监控：把监控切到它所在的机位，它就停手并换位 ——
    /// 既不偷电、也不推进。这是四个敌人里唯一需要「看监控」才能按住的。
    ///
    /// **右门是它和袋鼠共用的通道**：门口已经站着对方时，它留在走廊等（先来后到）。
    /// 到了门口，门关着就被挡回起点，门开着撑过宽限时间就动手。
    ///
    /// 它**只在「能去的机位表」里换位** —— 不去的机位（比如只有声音、没有画面的厨房）
    /// 在构造时从表里剔掉即可，见 <see cref="NailongBrain(int, IRandomSource, int[])"/>。
    ///
    /// 断电之后它是另一副样子：从左门冒出来「收电费」，见 <see cref="BlackoutSequence"/>。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class NailongBrain : DoorEnemyBrain
    {
        /// <summary>活动度 0 时的偷电间隔（秒）。</summary>
        public const float DefaultSlowStealIntervalSeconds = 24f;

        /// <summary>活动度满级时的偷电间隔（秒）。</summary>
        public const float DefaultFastStealIntervalSeconds = 6f;

        /// <summary>每偷一次扣多少电量（百分点）。</summary>
        public const float DefaultStealPercentPerTick = 1f;

        /// <summary>
        /// 被监控照到之后，它会在原地愣这么久才换位。
        /// 纯粹是给玩家看清它的时间 —— 否则一帧就闪走，等于没看见。
        /// </summary>
        public const float DefaultRevealSeconds = 1.2f;

        /// <summary>它现在蹲在第几个机位（0 起）。</summary>
        public int CameraIndex { get; private set; }

        /// <summary>它**可以去**的机位个数 —— 它不去的机位（比如只有声音的厨房）不算在内。</summary>
        public int CameraCount { get; private set; }

        /// <summary>活动度 0 时的偷电间隔（秒）。</summary>
        public float SlowStealIntervalSeconds { get; private set; }

        /// <summary>活动度满级时的偷电间隔（秒）。</summary>
        public float FastStealIntervalSeconds { get; private set; }

        /// <summary>每偷一次扣多少电。</summary>
        public float StealPercentPerTick { get; private set; }

        public float RevealSeconds { get; private set; }

        /// <summary>当前活动度对应的偷电间隔（秒）—— 活动度越高越短。</summary>
        public float StealIntervalSeconds
        {
            get { return IntervalFor(ActivityLevel); }
        }

        /// <summary>正被玩家从监控里看着。</summary>
        public bool IsBeingWatched { get; private set; }

        /// <summary>已经被看了多久（秒）。</summary>
        public float RevealTimer { get; private set; }

        /// <summary>被看到而换位的次数，供调试显示。</summary>
        public int RelocateCount { get; private set; }

        /// <summary>偷到电并成功推进一步的次数，供调试显示。</summary>
        public int AdvanceCount { get; private set; }

        private float _stealTimer;
        private float _pendingDrain;

        /// <summary>它能蹲的机位（升序）—— 不去的机位不在这张表里。</summary>
        private readonly int[] _cameras;

        public NailongBrain(int activityLevel, IRandomSource rng, int cameraCount)
            : this(activityLevel, rng, cameraCount,
                DefaultSlowStealIntervalSeconds, DefaultFastStealIntervalSeconds,
                DefaultStealPercentPerTick, DefaultRevealSeconds, DefaultDoorGraceSeconds)
        {
        }

        public NailongBrain(int activityLevel, IRandomSource rng, int cameraCount,
            float slowStealIntervalSeconds, float fastStealIntervalSeconds,
            float stealPercentPerTick, float revealSeconds, float doorGraceSeconds)
            : this(activityLevel, rng, AllCameras(cameraCount),
                slowStealIntervalSeconds, fastStealIntervalSeconds,
                stealPercentPerTick, revealSeconds, doorGraceSeconds)
        {
        }

        public NailongBrain(int activityLevel, IRandomSource rng, int[] allowedCameras)
            : this(activityLevel, rng, allowedCameras,
                DefaultSlowStealIntervalSeconds, DefaultFastStealIntervalSeconds,
                DefaultStealPercentPerTick, DefaultRevealSeconds, DefaultDoorGraceSeconds)
        {
        }

        /// <summary>
        /// 用「它能去的机位表」构造 —— 空表（或 null）时退回「第 0 ~ cameraCount-1 个全都能去」。
        /// 想让它绕开某个机位（比如只有声音、没有画面的厨房），从表里剔掉即可。
        /// </summary>
        public NailongBrain(int activityLevel, IRandomSource rng, int[] allowedCameras,
            float slowStealIntervalSeconds, float fastStealIntervalSeconds,
            float stealPercentPerTick, float revealSeconds, float doorGraceSeconds)
            : base(EnemyId.Nailong, Side.Right, activityLevel, rng,
                DefaultMoveIntervalSeconds, doorGraceSeconds)
        {
            _cameras = (allowedCameras != null && allowedCameras.Length > 0)
                ? (int[])allowedCameras.Clone()
                : AllCameras(1);

            CameraCount = _cameras.Length;
            CameraIndex = _cameras[Rng.Range(0, CameraCount)];

            SlowStealIntervalSeconds = slowStealIntervalSeconds > 0f
                ? slowStealIntervalSeconds
                : DefaultSlowStealIntervalSeconds;

            FastStealIntervalSeconds = fastStealIntervalSeconds > 0f
                ? fastStealIntervalSeconds
                : DefaultFastStealIntervalSeconds;

            StealPercentPerTick = stealPercentPerTick > 0f
                ? stealPercentPerTick
                : DefaultStealPercentPerTick;

            RevealSeconds = revealSeconds > 0f ? revealSeconds : DefaultRevealSeconds;
        }

        /// <summary>「第 0 ~ count-1 个机位」这张表（count 至少 1）。</summary>
        private static int[] AllCameras(int count)
        {
            if (count < 1)
            {
                count = 1;
            }

            var cameras = new int[count];

            for (int i = 0; i < count; i++)
            {
                cameras[i] = i;
            }

            return cameras;
        }

        protected override void OnTick(float dt, EnemyContext ctx)
        {
            // 监控正停在他这一格 → 现形、停手（不偷电也不推进）
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

            int stageBefore = Stage;
            StealAndPush(dt, ctx);

            // 本帧刚踏进门口 —— 宽限计时从下一帧才开始算。
            // 不然「偷电间隔」那一下大步长会直接灌满门口计时，玩家连关门的机会都没有。
            if (Stage >= MaxStage && stageBefore < MaxStage)
            {
                return;
            }

            // 到了门口之后的宽限：门关着挡回起点、门开着撑够就动手
            base.OnTick(dt, ctx);
        }

        /// <summary>
        /// 奶龙不参与通用的「每 5 秒掷骰」推进 —— 它的每一步都必须由偷到电换来。
        /// 覆盖成 false 把通用推进关掉，真正的前进判断在 <see cref="TryAdvance"/> 里，
        /// 那里调的是 <c>base.CanAdvance</c>（门口占用检查照旧生效）。
        /// </summary>
        protected override bool CanAdvance(EnemyContext ctx)
        {
            return false;
        }

        /// <summary>偷电。每偷到一次，就沿右门这条路尝试推进一步。</summary>
        private void StealAndPush(float dt, EnemyContext ctx)
        {
            // 活动度 0 = 整夜不偷电，也就不推进
            if (ActivityLevel <= 0)
            {
                return;
            }

            float interval = StealIntervalSeconds;
            _stealTimer += dt;

            while (_stealTimer >= interval)
            {
                _stealTimer -= interval;
                _pendingDrain += StealPercentPerTick;

                TryAdvance(ctx);
            }
        }

        /// <summary>偷到电 → 掷一次骰，不超过活动度就前进一格。</summary>
        private void TryAdvance(EnemyContext ctx)
        {
            // 已经到门口、或门口站着别人 —— 原地等着
            // （这里绕开自己覆盖的 CanAdvance，走父类那套「门口占用」判断）
            if (!base.CanAdvance(ctx))
            {
                return;
            }

            if (Rng.Range(1, MaxActivityLevel + 1) <= ActivityLevel)
            {
                Advance(ctx);
                AdvanceCount++;
            }
        }

        /// <summary>取出本帧累积的偷电量（并清零）。由 EnemyDirector 交给电量系统。</summary>
        public float ConsumePendingDrain()
        {
            float drain = _pendingDrain;
            _pendingDrain = 0f;
            return drain;
        }

        /// <summary>强制换到指定机位（调试 / 测试用）。不在它能去的表里就忽略。</summary>
        public void ForceCamera(int index)
        {
            if (SlotOf(index) < 0)
            {
                return;
            }

            CameraIndex = index;
            _stealTimer = 0f;
            RevealTimer = 0f;
            IsBeingWatched = false;
        }

        /// <summary>活动度 → 偷电间隔：0 级最慢、满级最快，中间线性。</summary>
        private float IntervalFor(int activity)
        {
            float t = (float)activity / MaxActivityLevel;

            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            return SlowStealIntervalSeconds
                + (FastStealIntervalSeconds - SlowStealIntervalSeconds) * t;
        }

        private void Relocate()
        {
            _stealTimer = 0f;

            if (CameraCount <= 1)
            {
                return;
            }

            // 在它能去的表里随机挑一个「不同的」机位
            int current = SlotOf(CameraIndex);
            int next = Rng.Range(0, CameraCount - 1);

            if (current >= 0 && next >= current)
            {
                next++;
            }

            CameraIndex = _cameras[next];
            RelocateCount++;
        }

        /// <summary>机位号在 <see cref="_cameras"/> 里的位置；不在表里返回 -1。</summary>
        private int SlotOf(int cameraIndex)
        {
            for (int i = 0; i < _cameras.Length; i++)
            {
                if (_cameras[i] == cameraIndex)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
