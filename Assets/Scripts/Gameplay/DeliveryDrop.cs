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
        /// 捡起来能补的电量档位（百分比）。每落一份随机抽一档 ——
        /// 1 点算运气差，5 点算运气好，中间是 2 点。
        /// </summary>
        public static readonly float[] Gains = { 5f, 2f, 1f };

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
            _gain = Gains[roll.Range(0, Gains.Length)];
            return fresh;
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
