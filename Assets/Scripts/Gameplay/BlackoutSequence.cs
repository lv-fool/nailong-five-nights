namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 断电之后那一段：奶龙来「收电费」。
    ///
    /// 流程照着 FNAF 1 原版的断电走，分三段：
    ///
    /// 1. **静默**（默认 2 秒）—— 门弹开、灯灭、监控掉线，一片漆黑，它还没来。
    /// 2. **音乐**（默认 13.5 秒，跟音频长度走）—— 它从左门现形，音乐盒响起，
    ///    脸跟着**节拍**一亮一亮：拍点上看清它，其余时候沉在黑暗里。
    /// 3. **漆黑**（默认 2.5 秒）—— 音乐停了，画面彻底黑掉，只剩一个勉强能辨认的轮廓。
    ///
    /// 三段走完就动手。期间**时钟照走** —— 离天亮够近的话有机会躲过去。
    /// 这段时间其他敌人全部停摆，只剩它。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class BlackoutSequence
    {
        /// <summary>断电到它现身之间的静默（秒）。</summary>
        public const float DefaultSilenceSeconds = 2f;

        /// <summary>音乐盒的长度（秒）。运行时会按实际音频长度覆盖。</summary>
        public const float DefaultMusicSeconds = 13.5f;

        /// <summary>音乐停下之后、动手之前的漆黑（秒）。</summary>
        public const float DefaultDarkSeconds = 2.5f;

        /// <summary>音乐盒一拍多长（秒）。脸就是跟着这个节拍闪的。</summary>
        public const float DefaultBeatSeconds = 0.5f;

        public float SilenceSeconds { get; private set; }
        public float MusicSeconds { get; private set; }
        public float DarkSeconds { get; private set; }
        public float BeatSeconds { get; private set; }

        /// <summary>当前处在哪一段。</summary>
        public BlackoutPhase Phase { get; private set; }

        /// <summary>演出是否正在进行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>是否已经到点、该动手了。</summary>
        public bool HasFired { get; private set; }

        /// <summary>当前这一段已经走了多久（秒）。</summary>
        public float Elapsed { get; private set; }

        /// <summary>整段演出已经走了多久（秒）。</summary>
        public float TotalElapsed { get; private set; }

        /// <summary>整段演出的总长（秒）。</summary>
        public float TotalSeconds
        {
            get { return SilenceSeconds + MusicSeconds + DarkSeconds; }
        }

        /// <summary>0 → 1 的总进度。</summary>
        public float Progress
        {
            get
            {
                float total = TotalSeconds;
                if (total <= 0f)
                {
                    return 1f;
                }

                float progress = TotalElapsed / total;

                if (progress < 0f)
                {
                    return 0f;
                }

                return progress > 1f ? 1f : progress;
            }
        }

        /// <summary>
        /// 音乐阶段里的拍相位（0 → 1）。UI 拿它让脸跟着节拍亮。
        /// 不在音乐阶段时恒为 0。
        /// </summary>
        public float BeatPhase
        {
            get
            {
                if (BeatSeconds <= 0f)
                {
                    return 0f;
                }

                float phase = Elapsed % BeatSeconds;
                return phase / BeatSeconds;
            }
        }

        public BlackoutSequence()
            : this(DefaultSilenceSeconds, DefaultMusicSeconds,
                DefaultDarkSeconds, DefaultBeatSeconds)
        {
        }

        public BlackoutSequence(float silenceSeconds, float musicSeconds,
            float darkSeconds, float beatSeconds)
        {
            SilenceSeconds = silenceSeconds > 0f ? silenceSeconds : DefaultSilenceSeconds;
            MusicSeconds = musicSeconds > 0f ? musicSeconds : DefaultMusicSeconds;
            DarkSeconds = darkSeconds > 0f ? darkSeconds : DefaultDarkSeconds;
            BeatSeconds = beatSeconds > 0f ? beatSeconds : DefaultBeatSeconds;
        }

        /// <summary>按实际音频长度调整音乐阶段 —— 音乐播完就该进漆黑段。</summary>
        public void SetMusicSeconds(float seconds)
        {
            if (seconds > 0f)
            {
                MusicSeconds = seconds;
            }
        }

        /// <summary>断电那一刻调用，从静默段开始。</summary>
        public void Begin()
        {
            IsRunning = true;
            HasFired = false;
            Phase = BlackoutPhase.Silence;
            Elapsed = 0f;
            TotalElapsed = 0f;
        }

        /// <summary>重新供电 / 重开一夜时复位。</summary>
        public void Reset()
        {
            IsRunning = false;
            HasFired = false;
            Phase = BlackoutPhase.Silence;
            Elapsed = 0f;
            TotalElapsed = 0f;
        }

        /// <summary>推进一帧。dt 为真实秒数。</summary>
        public void Tick(float dt)
        {
            if (!IsRunning || HasFired || dt <= 0f)
            {
                return;
            }

            Elapsed += dt;
            TotalElapsed += dt;

            // 掉帧时一帧可能跨过整段，所以用 while 把阶段一次推到位
            while (!HasFired)
            {
                float limit = LengthOf(Phase);
                if (Elapsed < limit)
                {
                    break;
                }

                Elapsed -= limit;
                Step();
            }
        }

        private void Step()
        {
            switch (Phase)
            {
                case BlackoutPhase.Silence:
                    Phase = BlackoutPhase.Music;
                    break;

                case BlackoutPhase.Music:
                    Phase = BlackoutPhase.Dark;
                    break;

                default:
                    Phase = BlackoutPhase.Done;
                    HasFired = true;
                    break;
            }
        }

        private float LengthOf(BlackoutPhase phase)
        {
            switch (phase)
            {
                case BlackoutPhase.Silence:
                    return SilenceSeconds;

                case BlackoutPhase.Music:
                    return MusicSeconds;

                case BlackoutPhase.Dark:
                    return DarkSeconds;

                default:
                    return 0f;
            }
        }
    }
}
