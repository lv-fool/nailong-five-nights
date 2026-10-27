namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 奶龙的两个状态 —— 项目作者 2026-10-17 定：
    /// **偷到电就转化成门类敌人进攻，被防御后再回去偷电**。
    /// </summary>
    public enum NailongMode
    {
        /// <summary>蹲在某个机位偷电。看监控即止，被看到就换位。</summary>
        Stealing = 0,

        /// <summary>已经偷到电、转化成门类敌人 —— 沿右门路径进攻，只能关门挡。</summary>
        Approaching = 1
    }

    /// <summary>
    /// 奶龙：一边偷电，一边摸右门。
    ///
    /// **两个状态来回切**（2026-10-17 项目作者定）：
    ///   1. <see cref="NailongMode.Stealing"/> —— 蹲在某个机位偷电。
    ///      切到它所在的机位看它一眼，它就停手并换位。
    ///   2. **偷到电就转化成门类敌人**（<see cref="NailongMode.Approaching"/>）——
    ///      从路径起点开始沿右门一路推进，跟袋鼠 / 牛来一个待遇：
    ///      到门口门关着就被挡回起点，**被挡回来就回去重新偷电**；
    ///      门开着撑过宽限就动手。
    ///
    /// 两个状态**贴图不一样**（见 <c>PrototypeScene.ApplyMonitorCharacter</c>）：
    /// 偷电时用 stare（正对镜头），进攻时用 idle（侧身站在机位上）。
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

        /// <summary>
        /// **仅调试用**：把奶龙直接放到指定机位。
        ///
        /// 奶龙有两个身份：偷电时用 CameraIndex，转化成门类敌人后用
        /// 基类的 PathCameraIndex。这里按当前 Mode 走对应的那条路。
        /// </summary>
        public override void DebugTeleport(int cameraIndex)
        {
            if (Mode == NailongMode.Approaching)
            {
                base.DebugTeleport(cameraIndex);   // 已转化：走门类敌人那套
                return;
            }

            if (cameraIndex < 0)
            {
                return;   // 偷电阶段没有「门口」这个概念
            }

            CameraIndex = cameraIndex;
        }


        /// <summary>当前状态：偷电 / 已转化成门类敌人进攻。</summary>
        public NailongMode Mode { get; private set; }

        /// <summary>当前状态对应的贴图姿态代号 —— 监控画面按它挑图。</summary>
        public bool IsApproaching
        {
            get { return Mode == NailongMode.Approaching; }
        }

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

        /// <summary>偷到一次电之后要静止多少秒（「一二三木头人」支援）。</summary>
        public float StealFreezeSeconds { get; set; }

        /// <summary>当前还剩多少秒静止。</summary>
        private float _stealFreezeLeft;
        private float _pendingDrain;

        /// <summary>
        /// 本帧刚从偷电态转化成进攻态。
        /// 偷电间隔（6 秒）比移动间隔（5 秒）长 —— 不加这道闸门，
        /// 转化的同一帧就会立刻掷骰推进一步，玩家看到它「一步跨两格」。
        /// </summary>
        private bool _justConverted;

        /// <summary>它能蹲的机位（升序）—— 不去的机位不在这张表里。</summary>
        private readonly int[] _cameras;

        public NailongBrain(int activityLevel, IRandomSource rng, int cameraCount)
            : this(activityLevel, rng, cameraCount,
                DefaultSlowStealIntervalSeconds, DefaultFastStealIntervalSeconds,
                DefaultStealPercentPerTick, DefaultRevealSeconds, FreddyDoorWindowSeconds)
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
                DefaultStealPercentPerTick, DefaultRevealSeconds, FreddyDoorWindowSeconds)
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
            // 每帧开头先清「刚转化」闸门 —— 它只挡转化当帧那一次推进
            _justConverted = false;

            // ---- 状态一：蹲机位偷电 ----
            if (Mode == NailongMode.Stealing)
            {
                // 监控正停在它这一格 → 现形、停手（不偷电）
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

                Steal(dt);
                return;
            }

            // ---- 状态二：已转化成门类敌人，沿右门推进 ----
            // 这一段完全走父类的门口逻辑：门关着 → Retreat（退回偷电态），门开着撑够 → 动手。
            base.OnTick(dt, ctx);
        }

        /// <summary>
        /// 通用「每 5 秒掷骰」推进只在**进攻态**打开。
        /// 偷电态关掉（它那会儿不动，只在原地偷电）。
        /// </summary>
        protected override bool CanAdvance(EnemyContext ctx)
        {
            if (Mode != NailongMode.Approaching)
            {
                return false;
            }

            // 转化的那一帧不许推进（见 _justConverted）
            if (_justConverted)
            {
                return false;
            }

            return base.CanAdvance(ctx);
        }

        /// <summary>
        /// 偷电。**偷到一次电就转化成门类敌人**（项目作者 2026-10-17 定）：
        /// 从路径起点开始沿右门进攻，被门挡回来再回去偷电。
        /// </summary>
        private void Steal(float dt)
        {
            // 活动度 0 = 整夜不偷电，也就永远不会转化成门类敌人
            if (ActivityLevel <= 0)
            {
                return;
            }

            if (_stealFreezeLeft > 0f)
            {
                _stealFreezeLeft -= dt;
                return;
            }

            float interval = StealIntervalSeconds;
            _stealTimer += dt;

            while (_stealTimer >= interval)
            {
                _stealTimer -= interval;
                _pendingDrain += StealPercentPerTick;

                // 「一二三木头人」：偷完这一下先静止一段时间，下一次偷电要等它过去
                if (StealFreezeSeconds > 0f)
                {
                    _stealFreezeLeft = StealFreezeSeconds;
                }

                // 偷到电 → 转化。一次就够，不用再掷骰（掷骰交给进攻态的通用推进）
                BeginApproach();
                return;
            }
        }

        /// <summary>偷到电了 —— 从偷电态切成门类敌人，从路径起点开始进攻。</summary>
        private void BeginApproach()
        {
            Mode = NailongMode.Approaching;
            Stage = 0;
            DoorTimer = 0f;
            IsBeingWatched = false;
            RevealTimer = 0f;

            // 转化当帧不许再推进 —— 偷电间隔比移动间隔长，
            // 不挡这一下就会「刚偷到电就跨一格」。
            _justConverted = true;

            // 站到路径起点那个机位上，玩家切过去能看见它开始往门口走
            int pathCam = PathCameraIndex;
            if (pathCam >= 0)
            {
                CameraIndex = pathCam;
            }
        }

        /// <summary>
        /// 沿路径推进一格 —— 同时把「可见机位」同步到路径对应位置，
        /// 让玩家在监控里能看着它一步步逼近右门（照弗莱迪）。
        /// </summary>
        protected override void Advance(EnemyContext ctx)
        {
            base.Advance(ctx);
            AdvanceCount++;

            // 到路径尽头（= 门口）时 PathCameraIndex 返回 -1 —— 那时它属于门口威胁，
            // 监控里看不到，只能靠门灯现形。
            int pathCam = PathCameraIndex;
            if (pathCam >= 0)
            {
                CameraIndex = pathCam;
            }
        }

        /// <summary>
        /// 被门挡回 / 到门口被赶走 —— 退回**偷电态**，重新蹲机位偷电。
        /// （父类的 Retreat 会把 Stage 归零，这里再补状态切换。）
        /// </summary>
        public override void Retreat()
        {
            base.Retreat();

            Mode = NailongMode.Stealing;
            _stealTimer = 0f;
            IsBeingWatched = false;
            RevealTimer = 0f;

            // 回到某个能去的机位继续偷电
            if (CameraCount > 0)
            {
                CameraIndex = _cameras[Rng.Range(0, CameraCount)];
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
            DoorTimer = 0f;          // 换位等于这一轮「摸门」从头开始，别把门口计时留着

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
