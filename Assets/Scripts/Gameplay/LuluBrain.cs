namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 噜噜：从**正门**摸进来，怕手电筒的光。
    ///
    /// 路线：远处(0) → 逼近正门(1) → 停在正门口(2)。
    /// 到了门口，玩家得**朝正前方反复按手电筒闪它**。
    ///
    /// 闪法照 FNAF 2 的狐狸（Withered Foxy）：**按一次算一次闪光**，
    /// 每按一次把推进计时清零，**按够 <see cref="FlashesToRetreat"/> 次**它就退走。
    /// 所以是「**不停地闪它**」—— 按住不放只算一次，得松手再按。
    /// 一直不按、或者中间隔太久，推进计时走满 <see cref="OutsideGraceSeconds"/> 它就动手。
    ///
    /// 门外宽限不是定值 —— 它按活动度折算：活动度越高，留给玩家的时间越短。
    /// 详见 <see cref="GraceForActivity"/>。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class LuluBrain : EnemyBrain
    {
        /// <summary>闪够几次它就退走。</summary>
        public const int DefaultFlashesToRetreat = 4;

        /// <summary>活动度 0（整夜不动）时的门外宽限（秒）—— 最宽松。默认值，可被覆盖。</summary>
        public const float GraceAtZeroActivity = 16f;

        /// <summary>活动度满级时的门外宽限（秒）—— 最紧。默认值，可被覆盖。</summary>
        public const float GraceAtMaxActivity = 7.5f;

        /// <summary>
        /// 沿路停留的监控机位（下标）—— 绿子 2026-10-17 要求：
        /// **出生地是海盗湾（1C）**，路线要在监控里看得见。
        /// 空表 = 没配路径，监控里看不到它（旧行为）。
        /// </summary>
        private int[] _cameraPath;

        /// <summary>配它的推进路径（海盗湾 → 餐厅 → 正门）。</summary>
        public void SetCameraPath(int[] path)
        {
            _cameraPath = (path != null && path.Length > 0) ? (int[])path.Clone() : null;
        }

        /// <summary>
        /// 当前停在哪个监控机位（下标）。**站在正门口**（<see cref="Stage"/> 到顶）、
        /// 或没配路径时返回 -1 —— 两者都不属于任何机位。
        /// </summary>
        public int PathCameraIndex
        {
            get
            {
                if (_cameraPath == null || _cameraPath.Length == 0)
                {
                    return -1;
                }

                if (Stage < 0 || Stage >= _cameraPath.Length)
                {
                    return -1;
                }

                return _cameraPath[Stage];
            }
        }

        /// <summary>闪够几次它就退走。</summary>
        public int FlashesToRetreat { get; private set; }

        /// <summary>
        /// 到了门口之后，两次闪光之间最多能隔多久（秒）。
        /// 默认由活动度折算；构造时显式传了正数就固定用那个值（测试与调试用）。
        /// </summary>
        public float OutsideGraceSeconds { get; private set; }

        /// <summary>这一轮已经被有效闪光逼退了几步（按了几次）。</summary>
        public int FlashCount { get; private set; }

        /// <summary>距离上一次闪光已经过了多久（秒）。</summary>
        public float SinceFlashTimer { get; private set; }

        /// <summary>宽限是否跟着活动度自动走（false = 构造时写死了）。</summary>
        private readonly bool _graceFollowsActivity;

        /// <summary>
        /// 上一帧手电筒是不是正照在它身上。
        /// 抓「按下」那一下的**上升沿** —— 一次按住只算一次闪光，得松手再按才算下一次。
        /// </summary>
        private bool _wasShining;

        private float _graceAtZero = GraceAtZeroActivity;
        private float _graceAtMax = GraceAtMaxActivity;

        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng)
            : this(id, activityLevel, rng, DefaultMoveIntervalSeconds,
                DefaultFlashesToRetreat, 0f)
        {
        }

        /// <summary>
        /// <paramref name="outsideGraceSeconds"/> 传 0 或不传 = 按活动度自动折算；
        /// 传正数 = 写死这个秒数（测试与调试用）。
        /// </summary>
        public LuluBrain(EnemyId id, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, int flashesToRetreat, float outsideGraceSeconds)
            : base(id, activityLevel, rng, moveIntervalSeconds)
        {
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
            FlashCount = 0;
        }

        protected override void OnTick(float dt, EnemyContext ctx)
        {
            // 「按下」= 照住状态的上升沿。按住不放只有这一下算数，
            // 想再闪一次必须松手重按 —— 这就是「不停地闪它」。
            bool shining = ctx.IsShiningAtFront();
            bool pressed = shining && !_wasShining;
            _wasShining = shining;

            // 还没堵到门口时照它没用，但上升沿要一直跟，
            // 否则「到门口那一刻玩家正按着」会被当成按过了、白闪一下
            if (Stage < MaxStage)
            {
                return;
            }

            if (pressed)
            {
                FlashCount++;
                SinceFlashTimer = 0f;

                if (FlashCount >= FlashesToRetreat)
                {
                    Retreat();
                    return;
                }
            }
            else
            {
                SinceFlashTimer += dt;
            }

            if (SinceFlashTimer >= OutsideGraceSeconds)
            {
                IsAttacking = true;
            }
        }

        public override void Retreat()
        {
            base.Retreat();
            SinceFlashTimer = 0f;
            FlashCount = 0;
        }
    }
}
