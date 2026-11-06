namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 袋鼠留下的那份外卖（2026-10-26 加）。
    ///
    /// 来由：袋鼠被关上的门挡住、敲门走人之后，门口会剩下一份外卖。
    /// 玩家捡起来能补 <see cref="Gain"/>% 的电 —— 每次 1 / 2 / 5 点不等（老板 2026-10-26 定）。
    ///
    /// 规则：
    ///   · 同一时间只有一份；再落一份就是刷新位置与计时（不会叠成两份）。
    ///   · 放一会儿没人捡就会凉（<see cref="LifetimeSeconds"/> 秒），凉了自动消失。
    ///   · 捡走之后要等它下次再送 —— 所以 <see cref="TryPickup"/> 只有在场时才成立。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public class DeliveryDrop
    {
        /// <summary>
        /// 捡起来能补的电量档位（百分比）。**加权**抽取，不是均匀三档
        /// （老板 2026-11-03 定）：
        ///
        ///   · 1 点 —— 70%，大多数时候是「聊胜于无」
        ///   · 2 点 —— 15%
        ///   · 5 点 —— 15%
        ///
        /// 平均下来一份约 1.75 点。这个表要和 <see cref="GainWeights"/> 一一对应，
        /// 两边的顺序不能各自改 —— 改档位请连权重一起改。
        /// </summary>
        public static readonly float[] Gains = { 5f, 2f, 1f };

        /// <summary>
        /// 与 <see cref="Gains"/> 一一对应的权重（相对值，不必加起来等于 100）：
        /// 5 点 15、2 点 15、1 点 70。
        ///
        /// ⚠️ 之所以按「先抽权重表下标、再映射回 Gains」来写，是为了保住 Gains 原有的
        /// 排列（外部和测试都按下标读它）。**别用「把 1 点重复 70 次塞进数组」那种写法**：
        /// 档位一多数组就爆炸，而且抽档概率会跟数组长度耦合。
        /// </summary>
        public static readonly int[] GainWeights = { 15, 15, 70 };

        /// <summary>默认骰子（<see cref="Drop()"/> 不带参数时用它）。</summary>
        private static readonly IRandomSource SharedRandom = new SystemRandomSource();

        /// <summary>放多久没人捡就凉（秒）。</summary>
        public const float LifetimeSeconds = 20f;

        private bool _available;
        private float _age;
        private float _gain;

        /// <summary>门口现在有没有一份外卖。</summary>
        public bool Available
        {
            get { return _available; }
        }

        /// <summary>这一份能补多少电（百分比）。没货时是 0。</summary>
        public float Gain
        {
            get { return _available ? _gain : 0f; }
        }

        /// <summary>这份外卖已经放了多久（秒）。没货时是 0。</summary>
        public float Age
        {
            get { return _available ? _age : 0f; }
        }

        /// <summary>还要多久凉（秒）。没货时是 0。</summary>
        public float RemainingSeconds
        {
            get
            {
                if (!_available)
                {
                    return 0f;
                }

                float left = LifetimeSeconds - _age;
                return left > 0f ? left : 0f;
            }
        }

        /// <summary>
        /// 袋鼠走了，留下一份。再落一份不会叠成两份，只会把计时清回 0。
        /// 返回「是不是新出现的一份」（刷新已有的返回 false）。
        /// </summary>
        public bool Drop()
        {
            return Drop(SharedRandom);
        }

        /// <summary>同上，但用指定的骰子（测试里给固定档位用）。</summary>
        public bool Drop(IRandomSource random)
        {
            IRandomSource roll = random ?? SharedRandom;
            bool fresh = !_available;
            _available = true;
            _age = 0f;
            _gain = RollGain(roll);
            return fresh;
        }

        /// <summary>
        /// 按权重抽一档电量（70% 是 1 点、各 15% 是 2 点和 5 点）。
        ///
        /// 做法：把权重累加成一条 [0, total) 的区间带，掷一个点落在哪段就是哪档。
        /// 这样骰子只需要「随机整数」这一种能力（<see cref="IRandomSource"/> 就这些），
        /// 测试里塞个固定值也能精确命中想要的档位。
        ///
        /// 骰子返回越界值（测试里的 FixedRandom 不会夹）时按 clamp 处理，
        /// 不让它抛异常 —— 外卖不该因为骰子不听话把整局搞崩。
        /// </summary>
        private static float RollGain(IRandomSource roll)
        {
            int count = GainWeights.Length < Gains.Length ? GainWeights.Length : Gains.Length;
            if (count <= 0)
            {
                return 0f;
            }

            int total = 0;
            for (int i = 0; i < count; i++)
            {
                if (GainWeights[i] > 0)
                {
                    total += GainWeights[i];
                }
            }

            if (total <= 0)
            {
                // 权重表被填成 0 也别崩 —— 退回最朴素的一档
                return Gains[count - 1];
            }

            int pick = roll.Range(0, total);
            if (pick < 0)
            {
                pick = 0;
            }
            else if (pick >= total)
            {
                pick = total - 1;
            }

            int edge = 0;
            for (int i = 0; i < count; i++)
            {
                int weight = GainWeights[i];
                if (weight <= 0)
                {
                    continue;
                }

                edge += weight;
                if (pick < edge)
                {
                    return Gains[i];
                }
            }

            // 理论上走不到这儿（上面的 total 已经把权重加全了）
            return Gains[count - 1];
        }

        /// <summary>时间流逝。放凉的自己消失。</summary>
        public void Tick(float dt)
        {
            if (!_available || dt <= 0f)
            {
                return;
            }

            _age += dt;
            if (_age >= LifetimeSeconds)
            {
                Clear();
            }
        }

        /// <summary>捡起来。门口没货时返回 false（补电由调用方按 <see cref="Gain"/> 加）。</summary>
        public bool TryPickup()
        {
            if (!_available)
            {
                return false;
            }

            Clear();
            return true;
        }

        /// <summary>把门口那份清掉（换夜、重开一局都走这里）。</summary>
        public void Clear()
        {
            _available = false;
            _age = 0f;
            _gain = 0f;
        }
    }
}
