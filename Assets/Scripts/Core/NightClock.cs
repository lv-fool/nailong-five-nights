namespace NailongFNAF.Core
{
    /// <summary>
    /// 夜晚时钟：把真实时间映射成游戏里的「0 点到 6 点」。
    ///
    /// FNAF 1 的节奏是每游戏小时 85 秒，一整夜 6 小时 ≈ 8 分 30 秒。
    /// 这个类只负责时间，不管电量、敌人，方便单独测试和调参。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public class NightClock
    {
        /// <summary>一夜要走完的小时数（0 点走到 6 点，天亮）。</summary>
        public const int HoursPerNight = 6;

        /// <summary>默认每游戏小时对应的真实秒数。整夜 = 6 × 85 = 510 秒。</summary>
        public const float DefaultSecondsPerHour = 85f;

        /// <summary>每游戏小时对应的真实秒数（可调）。</summary>
        public float SecondsPerHour { get; private set; }

        /// <summary>
        /// 是否永夜模式：永夜模式下小时数一直往上走，永远不到天亮。
        /// 默认 false —— 原来的五夜流程完全不受影响。
        /// </summary>
        public bool Endless { get; private set; }

        /// <summary>本夜已经过去的真实秒数。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// 当前小时：0 表示 12 AM，1 表示 1 AM …… 6 表示 6 AM（天亮）。
        /// 普通夜晚取值 0 ~ 6；永夜模式下不封顶（可以到 7、8、9……）。
        /// </summary>
        public int CurrentHour { get; private set; }

        /// <summary>是否已经撑到天亮（6 AM）。永夜模式永远返回 false。</summary>
        public bool IsNightComplete
        {
            get { return !Endless && CurrentHour >= HoursPerNight; }
        }

        /// <summary>已经撑过去的整点小时数 —— 永夜模式用来显示「撑了多久」。</summary>
        public int HoursSurvived
        {
            get { return CurrentHour > HoursPerNight ? HoursPerNight : CurrentHour; }
        }

        /// <summary>永夜模式下这是第几「夜」（0 起）；普通夜晚恒为 0。</summary>
        public int NightNumber
        {
            get { return Endless ? CurrentHour / HoursPerNight : 0; }
        }

        /// <summary>永夜模式下当前这一「夜」的第几点（0 ~ 5）；普通夜晚等于 <see cref="CurrentHour"/>。</summary>
        public int HourOfNight
        {
            get
            {
                if (!Endless)
                {
                    return CurrentHour;
                }

                return CurrentHour % HoursPerNight;
            }
        }

        /// <summary>本夜进度，0 ~ 1。</summary>
        public float NightProgress
        {
            get
            {
                float total = SecondsPerHour * HoursPerNight;
                if (total <= 0f)
                {
                    return 1f;
                }

                float elapsed = Endless ? ElapsedSeconds % total : ElapsedSeconds;
                float progress = elapsed / total;
                return progress > 1f ? 1f : progress;
            }
        }

        /// <summary>用默认节奏创建（每小时 85 秒）。</summary>
        public NightClock()
            : this(DefaultSecondsPerHour)
        {
        }

        /// <summary>用自定义节奏创建。</summary>
        public NightClock(float secondsPerHour)
            : this(secondsPerHour, false)
        {
        }

        /// <summary>用自定义节奏创建，并指定是否永夜模式。</summary>
        public NightClock(float secondsPerHour, bool endless)
        {
            SecondsPerHour = secondsPerHour > 0f ? secondsPerHour : DefaultSecondsPerHour;
            Endless = endless;
        }

        /// <summary>推进时间。天亮之后不再变化（永夜模式不会天亮）。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || IsNightComplete)
            {
                return;
            }

            ElapsedSeconds += deltaSeconds;

            int hour = (int)(ElapsedSeconds / SecondsPerHour);
            CurrentHour = Endless ? hour : (hour > HoursPerNight ? HoursPerNight : hour);
        }

        /// <summary>回到午夜 0 点，用于开始新的一夜。</summary>
        public void Reset()
        {
            ElapsedSeconds = 0f;
            CurrentHour = 0;
        }
    }
}
