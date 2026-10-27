namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 永夜模式的得分。
    ///
    /// 只认两件事：**撑了多久**、**挡住过多少次**。挡的次数单独加权，
    /// 因为「活下来」本身在永夜里只是被动 —— 真正操作是关门、关灯、照它一下。
    ///
    /// 纯 C#，不引用 UnityEngine，EditMode 里直接测。
    /// </summary>
    public sealed class EndlessScore
    {
        /// <summary>每秒给几分。</summary>
        public const int PointsPerSecond = 1;

        /// <summary>每挡住一次怪物给几分。</summary>
        public const int PointsPerBlock = 25;

        /// <summary>每撑满一小时额外给几分。</summary>
        public const int PointsPerHour = 100;

        /// <summary>兜底的一小时有多少秒（正常由时钟传进来）。</summary>
        public const float DefaultSecondsPerHour = 85f;

        private readonly float _secondsPerHour;
        private float _seconds;
        private int _blocks;

        public EndlessScore(float secondsPerHour = DefaultSecondsPerHour)
        {
            _secondsPerHour = secondsPerHour > 0f ? secondsPerHour : DefaultSecondsPerHour;
        }

        /// <summary>本局撑过的秒数。</summary>
        public float Seconds
        {
            get { return _seconds; }
        }

        /// <summary>本局挡住怪物的次数。</summary>
        public int Blocks
        {
            get { return _blocks; }
        }

        /// <summary>撑满的整小时数。</summary>
        public int Hours
        {
            get { return (int)(_seconds / _secondsPerHour); }
        }

        /// <summary>当前得分。</summary>
        public int Score
        {
            get
            {
                return (int)_seconds * PointsPerSecond
                    + _blocks * PointsPerBlock
                    + Hours * PointsPerHour;
            }
        }

        /// <summary>推进时间。小于等于 0 的 dt 直接忽略（暂停、卡帧都不该加分）。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _seconds += deltaSeconds;
        }

        /// <summary>挡住一次（可以一次记多次，按次数加）。</summary>
        public void AddBlocks(int count)
        {
            if (count <= 0)
            {
                return;
            }

            _blocks += count;
        }

        public void Reset()
        {
            _seconds = 0f;
            _blocks = 0;
        }

        /// <summary>不建实例也能算分（测试与结算面板复用同一套公式）。</summary>
        public static int ScoreFor(float seconds, int blocks, float secondsPerHour)
        {
            if (seconds < 0f)
            {
                seconds = 0f;
            }

            if (blocks < 0)
            {
                blocks = 0;
            }

            float perHour = secondsPerHour > 0f ? secondsPerHour : DefaultSecondsPerHour;
            int hours = (int)(seconds / perHour);
            return (int)seconds * PointsPerSecond + blocks * PointsPerBlock + hours * PointsPerHour;
        }
    }
}
