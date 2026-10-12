namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 断电之后那一段：奶龙来「收电费」。
    ///
    /// 电量归零 → 奶龙在黑暗里现形、脑袋一闪一闪 → 撑过 GraceSeconds 还没到 6 点就跳杀。
    ///
    /// 期间**时钟照走** —— 离天亮够近的话有机会躲过去（同 FNAF 1 的断电处理）。
    /// 这段时间其他敌人全部停摆，只剩它。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class BlackoutSequence
    {
        /// <summary>断电后给玩家的宽限时间（秒）。</summary>
        public const float DefaultGraceSeconds = 12f;

        /// <summary>宽限时间。</summary>
        public float GraceSeconds { get; private set; }

        /// <summary>已经过去多久（秒）。</summary>
        public float Elapsed { get; private set; }

        /// <summary>是否已经开始演出。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>是否已经到点、该动手了。</summary>
        public bool HasFired { get; private set; }

        /// <summary>0 → 1 的进度。UI 拿它让闪烁越闪越快。</summary>
        public float Progress
        {
            get
            {
                if (GraceSeconds <= 0f)
                {
                    return 1f;
                }

                float progress = Elapsed / GraceSeconds;

                if (progress < 0f)
                {
                    return 0f;
                }

                return progress > 1f ? 1f : progress;
            }
        }

        public BlackoutSequence()
            : this(DefaultGraceSeconds)
        {
        }

        public BlackoutSequence(float graceSeconds)
        {
            GraceSeconds = graceSeconds > 0f ? graceSeconds : DefaultGraceSeconds;
        }

        /// <summary>断电那一刻调用，开始倒数。</summary>
        public void Begin()
        {
            IsRunning = true;
            HasFired = false;
            Elapsed = 0f;
        }

        /// <summary>重新供电 / 重开一夜时复位。</summary>
        public void Reset()
        {
            IsRunning = false;
            HasFired = false;
            Elapsed = 0f;
        }

        /// <summary>推进一帧。dt 为真实秒数。</summary>
        public void Tick(float dt)
        {
            if (!IsRunning || HasFired || dt <= 0f)
            {
                return;
            }

            Elapsed += dt;

            if (Elapsed >= GraceSeconds)
            {
                HasFired = true;
            }
        }
    }
}
