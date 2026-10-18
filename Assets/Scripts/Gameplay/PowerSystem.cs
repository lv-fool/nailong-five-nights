namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 电量系统。
    ///
    /// FNAF 的核心压力来源：电量只减不增，关门、开灯、看监控都会加快消耗，归零即断电。
    ///
    /// 数值取自 FNAF 1 的经典设定 —— 基础使用等级（什么都不开）下，每 9.6 秒掉 1%。
    /// 使用等级每高一级，消耗速率线性增加。这些数字都是常量，调参时改这里。
    ///
    /// 纯逻辑类，不引用 UnityEngine，因此能脱离 Unity 做单元测试。
    /// </summary>
    public class PowerSystem
    {
        /// <summary>电量上限（百分比）。</summary>
        public const float MaxLevel = 100f;

        /// <summary>
        /// 开局电量（百分比）。FNAF 1 从 99% 开始 —— 不是满的，
        /// 让玩家第一眼就看见它在往下掉。
        /// </summary>
        public const float InitialLevel = 99f;

        /// <summary>基础使用等级下，掉 1% 电量所需的秒数。</summary>
        public const float SecondsPerPercentAtBaseUsage = 9.6f;

        /// <summary>使用等级下限：什么都不开时也算 1 级。</summary>
        public const int MinUsageLevel = 1;

        /// <summary>使用等级上限：左门、右门、左灯、右灯、监控，最多同时 5 项。</summary>
        public const int MaxUsageLevel = 5;

        /// <summary>剩余电量（0 ~ 100）。</summary>
        public float Level { get; private set; }

        /// <summary>当前使用等级（1 ~ 5），由门 / 灯 / 监控的状态汇总而来。</summary>
        public int UsageLevel { get; private set; }

        /// <summary>电量是否已耗尽（断电）。</summary>
        public bool IsDepleted
        {
            get { return Level <= 0f; }
        }

        /// <summary>以满电量创建。</summary>
        public PowerSystem()
            : this(MaxLevel)
        {
        }

        /// <summary>以指定电量创建（测试与调试用）。</summary>
        public PowerSystem(float initialLevel)
        {
            Level = Clamp(initialLevel, 0f, MaxLevel);
            UsageLevel = MinUsageLevel;
        }

        /// <summary>
        /// 直接把电量设成某个值（0 ~ 100）。
        /// **只给调试面板用** —— 正常玩法里电量只减不增。
        /// </summary>
        public void SetLevel(float level)
        {
            Level = Clamp(level, 0f, MaxLevel);
        }

        /// <summary>设置当前使用等级，超出 1 ~ 5 会被夹回来。</summary>
        public void SetUsageLevel(int level)
        {
            UsageLevel = Clamp(level, MinUsageLevel, MaxUsageLevel);
        }

        /// <summary>推进时间，按当前使用等级扣除电量。断电后不再变化。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || IsDepleted)
            {
                return;
            }

            float drain = deltaSeconds / SecondsPerPercentAtBaseUsage * UsageLevel;
            Level = Clamp(Level - drain, 0f, MaxLevel);
        }

        /// <summary>
        /// 直接扣掉指定百分点 —— 用于设备耗电之外的原因，比如奶龙偷电。
        /// 与设备无关，因此不乘使用等级。
        /// </summary>
        public void DrainAmount(float percent)
        {
            if (percent <= 0f || IsDepleted)
            {
                return;
            }

            Level = Clamp(Level - percent, 0f, MaxLevel);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
