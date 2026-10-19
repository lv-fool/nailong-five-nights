using System;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 无尽模式的「行动换电」账本。
    ///
    /// 设计目标（老板原话）：<b>不以时间为标准补电，按玩家在这段时间里的行动补电；
    /// 干得越多补得越多，但补给必须是递减的。</b>
    ///
    /// 所以这里没有「每撑过一小时回多少」的定时补给，只有两条规则：
    ///
    /// 1. 每做一次有效操作就往 <see cref="Credits"/> 里记点。**同类操作会越来越不值钱** ——
    ///    一个补给周期内第 n 次同类操作的记点 = 权重 / (1 + (n-1) × <see cref="RepeatFalloff"/>)。
    ///    所以疯狂连点同一个按钮，收益会自己塌下去。
    /// 2. 记点攒够 <see cref="Threshold"/> 就结算一次补给，然后记点清零、同类计数清零。
    ///    第 k 次结算的补给量 = <see cref="BaseGain"/> × <see cref="Decay"/>^k，
    ///    并夹在 [<see cref="MinGain"/>, <see cref="MaxGain"/>] 之间。
    ///
    /// 两个性质是有意保证的，测试里也锁住了：
    /// - <b>单调</b>：动作越多，累计补给越多（不会因为做得太多反而变少）。
    /// - <b>递减</b>：每次结算给的比上一次少，同类操作越点越不值钱。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public class EndlessRecharge
    {
        /// <summary>默认结算门槛：攒够这么多记点就补一次电。</summary>
        public const float DefaultThreshold = 5f;

        /// <summary>默认首次补给量（百分点）。之后按 <see cref="DefaultDecay"/> 递减。</summary>
        public const float DefaultBaseGain = 8f;

        /// <summary>默认每次结算的衰减系数 —— 0.82 表示下一次只有上一次的 82%。</summary>
        public const float DefaultDecay = 0.82f;

        /// <summary>默认单次补给下限（百分点）。递减到这就封底，不会归零。</summary>
        public const float DefaultMinGain = 1.5f;

        /// <summary>默认单次补给上限（百分点），防止调参调出离谱数值。</summary>
        public const float DefaultMaxGain = 20f;

        /// <summary>默认同类操作衰减：1 → 0.667 → 0.5 → 0.4 ……</summary>
        public const float DefaultRepeatFalloff = 0.5f;

        private readonly float _threshold;
        private readonly float _baseGain;
        private readonly float _decay;
        private readonly float _minGain;
        private readonly float _maxGain;
        private readonly float _repeatFalloff;

        /// <summary>本次补给周期里各类动作已经做过多少次 —— 用来算同类衰减。</summary>
        private readonly int[] _repeatCounts;

        /// <summary>当前周期已攒下的记点，攒够 <see cref="Threshold"/> 就结算。</summary>
        public float Credits { get; private set; }

        /// <summary>已经结算过多少次补给。</summary>
        public int Settlements { get; private set; }

        /// <summary>累计已经补出去多少电量（百分点）。</summary>
        public float TotalGranted { get; private set; }

        /// <summary>累计登记过多少次有效操作。</summary>
        public int ActionsRegistered { get; private set; }

        /// <summary>下一次结算会补多少（百分点），供 UI 预告或调试用。</summary>
        public float NextGain
        {
            get { return GainForSettlement(Settlements); }
        }

        /// <summary>离下一次结算还差多少记点（0 表示已经攒够）。</summary>
        public float CreditsToNext
        {
            get
            {
                float left = _threshold - Credits;
                return left > 0f ? left : 0f;
            }
        }

        /// <summary>当前周期的记点进度，0 ~ 1。</summary>
        public float ProgressToNext
        {
            get { return _threshold <= 0f ? 1f : Math.Min(Credits / _threshold, 1f); }
        }

        /// <summary>结算门槛。</summary>
        public float Threshold
        {
            get { return _threshold; }
        }

        /// <summary>用默认数值创建。</summary>
        public EndlessRecharge()
            : this(DefaultThreshold, DefaultBaseGain, DefaultDecay, DefaultMinGain, DefaultMaxGain,
                DefaultRepeatFalloff)
        {
        }

        /// <summary>自定义数值创建（调参 / 测试用）。非正数一律退回默认值。</summary>
        public EndlessRecharge(float threshold, float baseGain, float decay,
            float minGain, float maxGain, float repeatFalloff)
        {
            _threshold = threshold > 0f ? threshold : DefaultThreshold;
            _baseGain = baseGain > 0f ? baseGain : DefaultBaseGain;
            _decay = decay > 0f && decay <= 1f ? decay : DefaultDecay;
            _maxGain = maxGain > 0f ? maxGain : DefaultMaxGain;
            _minGain = minGain >= 0f && minGain <= _maxGain ? minGain : DefaultMinGain;
            _repeatFalloff = repeatFalloff >= 0f ? repeatFalloff : DefaultRepeatFalloff;
            _repeatCounts = new int[4];
        }

        /// <summary>
        /// 登记一次有效操作，返回这一次结算出来的补给量（百分点）。
        /// 记点还没攒够时返回 0。
        /// </summary>
        public float RegisterAction(PlayerAction action)
        {
            int index = IndexOf(action);
            if (index < 0)
            {
                return 0f;
            }

            float weight = WeightOf(action);
            float falloff = 1f + _repeatCounts[index] * _repeatFalloff;
            Credits += weight / falloff;
            _repeatCounts[index] = _repeatCounts[index] + 1;
            ActionsRegistered++;

            if (Credits < _threshold)
            {
                return 0f;
            }

            return Settle();
        }

        /// <summary>把账本清空（重开一局无尽模式时调用）。</summary>
        public void Reset()
        {
            Credits = 0f;
            Settlements = 0;
            TotalGranted = 0f;
            ActionsRegistered = 0;

            for (int i = 0; i < _repeatCounts.Length; i++)
            {
                _repeatCounts[i] = 0;
            }
        }

        /// <summary>第 <paramref name="settlementIndex"/> 次结算会补多少（0 起）。</summary>
        public float GainForSettlement(int settlementIndex)
        {
            if (settlementIndex < 0)
            {
                return 0f;
            }

            float gain = _baseGain * (float)Math.Pow(_decay, settlementIndex);
            if (gain < _minGain)
            {
                gain = _minGain;
            }

            if (gain > _maxGain)
            {
                gain = _maxGain;
            }

            return gain;
        }

        /// <summary>各类动作的基础权重：越「有用」的操作给得越多。</summary>
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

        private float Settle()
        {
            float gain = GainForSettlement(Settlements);
            Settlements++;
            TotalGranted += gain;

            Credits = 0f;
            for (int i = 0; i < _repeatCounts.Length; i++)
            {
                _repeatCounts[i] = 0;
            }

            return gain;
        }

        private static int IndexOf(PlayerAction action)
        {
            int index = (int)action;
            return index >= 0 && index < 4 ? index : -1;
        }
    }
}
