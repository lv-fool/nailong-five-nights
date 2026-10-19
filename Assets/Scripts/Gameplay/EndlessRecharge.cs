using System;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 永夜模式的补电账本（中文名「永夜模式」，内部代号仍是 endless）。
    ///
    /// 三件事：
    /// 1. **固定节奏结算** —— 每过一个 <see cref="Interval"/>（默认一个游戏小时 = 85 秒）结算一次，
    ///    不随操作提前、也不会攒着不结算。
    /// 2. **行动换电** —— 一个周期内做的有效操作越多，这次结算补得越多；
    ///    同类操作越点越不值钱（权重 ÷ (1 + (n-1) × RepeatFalloff)）。
    /// 3. **越动越难** —— 每次操作同时往 <see cref="Noise"/> 里记一笔「动静」，
    ///    动静会随时间自然消散，但攒得越多敌人越疯（见 <see cref="EndlessDifficulty"/>）。
    ///
    /// 递减在这儿的两层意思：同类操作递减，以及第 k 次结算整体乘 Decay^k。
    /// 于是「动作越多累计补给越多」仍然成立，但越往后每小时能换到的电越少 —— 这一局总有尽头。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public class EndlessRecharge
    {
        /// <summary>默认结算周期：一个游戏小时。</summary>
        public const float DefaultInterval = 85f;

        /// <summary>什么都不做也会有的底薪（百分比）—— 但抵不过基础耗电，光站着还是会死。</summary>
        public const float DefaultBaseGain = 4f;

        /// <summary>记 1 点账能换多少电。</summary>
        public const float DefaultGainPerCredit = 0.75f;

        /// <summary>每结算一次整体衰减的系数。</summary>
        public const float DefaultDecay = 0.9f;

        /// <summary>单次结算的下限（百分比）。</summary>
        public const float DefaultMinGain = 1f;

        /// <summary>单次结算的上限（百分比）。</summary>
        public const float DefaultMaxGain = 24f;

        /// <summary>同一周期内，同类操作每多一次、权重下降多少。</summary>
        public const float DefaultRepeatFalloff = 0.5f;

        /// <summary>动静每秒自然消散多少。</summary>
        public const float DefaultNoiseDecayPerSecond = 0.1f;

        /// <summary>一次门操作产生的动静。</summary>
        public const float DoorNoise = 2f;

        /// <summary>一次灯操作产生的动静。</summary>
        public const float LightNoise = 1.5f;

        /// <summary>一次监控操作产生的动静。</summary>
        public const float CameraNoise = 1f;

        /// <summary>一次手电筒操作产生的动静。</summary>
        public const float FlashlightNoise = 0.8f;

        /// <summary>一格时间最多补多少秒（防止一帧卡顿把周期跳过好多次）。</summary>
        private const int MaxSettlementsPerTick = 8;

        private readonly int[] _counts = new int[4];

        /// <summary>结算周期（秒）。</summary>
        public float Interval { get; private set; }

        /// <summary>本周期已经攒下的账。</summary>
        public float Credits { get; private set; }

        /// <summary>当前动静（驱动难度）。</summary>
        public float Noise { get; private set; }

        /// <summary>已经结算过几次。</summary>
        public int Settlements { get; private set; }

        /// <summary>累计补进去的电量（百分比）。</summary>
        public float TotalGranted { get; private set; }

        /// <summary>登记过的操作总数。</summary>
        public int ActionsRegistered { get; private set; }

        /// <summary>本次周期已经走过多少秒。</summary>
        public float SecondsIntoPeriod { get; private set; }

        /// <summary>距离下次结算还有多少秒。</summary>
        public float SecondsToNext
        {
            get { return Math.Max(0f, Interval - SecondsIntoPeriod); }
        }

        /// <summary>距离下次结算的进度（0 ~ 1）。</summary>
        public float ProgressToNext
        {
            get { return Interval > 0f ? Clamp01(SecondsIntoPeriod / Interval) : 1f; }
        }

        /// <summary>按当前账本预估下一次结算能补多少（百分比）。</summary>
        public float NextGain
        {
            get { return GainForSettlement(Settlements); }
        }

        /// <summary>默认参数。</summary>
        public EndlessRecharge()
            : this(DefaultInterval, DefaultBaseGain, DefaultGainPerCredit, DefaultDecay,
                DefaultMinGain, DefaultMaxGain, DefaultRepeatFalloff)
        {
        }

        /// <summary>自定义参数（非正数退回默认值）。</summary>
        public EndlessRecharge(float interval, float baseGain, float gainPerCredit, float decay,
            float minGain, float maxGain, float repeatFalloff)
        {
            Interval = interval > 0f ? interval : DefaultInterval;
            BaseGain = baseGain >= 0f ? baseGain : DefaultBaseGain;
            GainPerCredit = gainPerCredit > 0f ? gainPerCredit : DefaultGainPerCredit;
            Decay = decay > 0f && decay <= 1f ? decay : DefaultDecay;
            MinGain = minGain >= 0f ? minGain : DefaultMinGain;
            MaxGain = maxGain >= MinGain ? maxGain : DefaultMaxGain;
            RepeatFalloff = repeatFalloff >= 0f ? repeatFalloff : DefaultRepeatFalloff;
        }

        public float BaseGain { get; private set; }

        public float GainPerCredit { get; private set; }

        public float Decay { get; private set; }

        public float MinGain { get; private set; }

        public float MaxGain { get; private set; }

        public float RepeatFalloff { get; private set; }

        /// <summary>
        /// 登记一次有效操作：记账、加动静。**不会立刻补电** —— 补电只在周期到了才发生。
        /// 返回这次操作折算出多少账（调试与显示用）。
        /// </summary>
        public float RegisterAction(PlayerAction action)
        {
            int index = (int)action;
            if (index < 0 || index >= _counts.Length)
            {
                return 0f;
            }

            _counts[index]++;
            ActionsRegistered++;

            float credit = WeightOf(action) / (1f + (_counts[index] - 1) * RepeatFalloff);
            Credits += credit;
            Noise += NoiseOf(action);
            return credit;
        }

        /// <summary>
        /// 推进时钟。到结算点就结算一次并返回补给量（百分比），否则返回 0。
        /// 一次调用最多结算 <see cref="MaxSettlementsPerTick"/> 次，避免卡顿时无限循环。
        /// </summary>
        public float Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return 0f;
            }

            Noise = Math.Max(0f, Noise - DefaultNoiseDecayPerSecond * deltaSeconds);
            SecondsIntoPeriod += deltaSeconds;

            float granted = 0f;
            int guard = 0;
            while (SecondsIntoPeriod >= Interval && guard < MaxSettlementsPerTick)
            {
                SecondsIntoPeriod -= Interval;
                guard++;
                granted += Settle();
            }

            if (guard >= MaxSettlementsPerTick)
            {
                SecondsIntoPeriod = 0f;
            }

            return granted;
        }

        /// <summary>
        /// 记下实际补进电量的数额（由调用方在 <see cref="NailongFNAF.Gameplay.PowerSystem.Grant"/>
        /// 之后调用）—— 电量封顶时实补可能小于名义值，这里只统计真的补进去的。
        /// </summary>
        public void NoteGranted(float actual)
        {
            if (actual > 0f)
            {
                TotalGranted += actual;
            }
        }

        /// <summary>第 index 次结算（0 起）的补给量：底薪 + 本周期账 × 单价，再乘 Decay^index。</summary>
        public float GainForSettlement(int index)
        {
            if (index < 0)
            {
                index = 0;
            }

            float raw = BaseGain + Credits * GainPerCredit;
            float decayed = raw * (float)Math.Pow(Decay, index);
            return Clamp(decayed, MinGain, MaxGain);
        }

        /// <summary>
        /// 从存档把账本恢复过来（读永夜模式的存档用）。负数一律按 0 处理。
        /// </summary>
        public void Restore(float credits, float noise, int settlements,
            float secondsIntoPeriod, float totalGranted, int actionsRegistered)
        {
            Credits = credits > 0f ? credits : 0f;
            Noise = noise > 0f ? noise : 0f;
            Settlements = settlements > 0 ? settlements : 0;
            SecondsIntoPeriod = secondsIntoPeriod > 0f ? secondsIntoPeriod : 0f;
            TotalGranted = totalGranted > 0f ? totalGranted : 0f;
            ActionsRegistered = actionsRegistered > 0 ? actionsRegistered : 0;

            for (int i = 0; i < _counts.Length; i++)
            {
                _counts[i] = 0;
            }

            // 周期进度别越过结算线，否则读档瞬间就连爆几次结算
            if (SecondsIntoPeriod >= Interval)
            {
                SecondsIntoPeriod = 0f;
            }
        }

        /// <summary>清空整本账（重开一局）。</summary>
        public void Reset()
        {
            for (int i = 0; i < _counts.Length; i++)
            {
                _counts[i] = 0;
            }

            Credits = 0f;
            Noise = 0f;
            Settlements = 0;
            TotalGranted = 0f;
            ActionsRegistered = 0;
            SecondsIntoPeriod = 0f;
        }

        /// <summary>一次操作折算多少账（同一周期内会被 <see cref="RepeatFalloff"/> 递减）。</summary>
        public static float WeightOf(PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.Door:
                    return 1.0f;
                case PlayerAction.Light:
                    return 0.9f;
                case PlayerAction.Camera:
                    return 0.7f;
                case PlayerAction.Flashlight:
                    return 0.6f;
                default:
                    return 0f;
            }
        }

        /// <summary>一次操作产生多少动静。</summary>
        public static float NoiseOf(PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.Door:
                    return DoorNoise;
                case PlayerAction.Light:
                    return LightNoise;
                case PlayerAction.Camera:
                    return CameraNoise;
                case PlayerAction.Flashlight:
                    return FlashlightNoise;
                default:
                    return 0f;
            }
        }

        private float Settle()
        {
            int index = Settlements;
            float gain = GainForSettlement(index);
            Settlements++;
            Credits = 0f;
            for (int i = 0; i < _counts.Length; i++)
            {
                _counts[i] = 0;
            }

            return gain;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }
    }
}
