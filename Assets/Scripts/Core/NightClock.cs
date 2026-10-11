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

        /// <summary>本夜已经过去的真实秒数。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// 当前小时：0 表示 12 AM，1 表示 1 AM …… 6 表示 6 AM（天亮）。
        /// 取值 0 ~ 6。
        /// </summary>
        public int CurrentHour { get; private set; }

        /// <summary>是否已经撑到天亮（6 AM）。</summary>
        public bool IsNightComplete
        {
            get { return CurrentHour >= HoursPerNight; }
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

                float progress = ElapsedSeconds / total;
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
        {
            SecondsPerHour = secondsPerHour > 0f ? secondsPerHour : DefaultSecondsPerHour;
        }

        /// <summary>推进时间。天亮之后不再变化。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || IsNightComplete)
            {
                return;
            }

            ElapsedSeconds += deltaSeconds;

            int hour = (int)(ElapsedSeconds / SecondsPerHour);
            CurrentHour = hour > HoursPerNight ? HoursPerNight : hour;
        }

        /// <summary>回到午夜 0 点，用于开始新的一夜。</summary>
        public void Reset()
        {
            ElapsedSeconds = 0f;
            CurrentHour = 0;
        }
    }
}
