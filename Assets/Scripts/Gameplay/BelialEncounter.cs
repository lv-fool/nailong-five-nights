namespace NailongFNAF.Gameplay
{
    /// <summary>贝利亚从哪一边的门出现。</summary>
    public enum BelialSide
    {
        Left = 0,
        Right = 1
    }

    /// <summary>
    /// 贝利亚本人 —— 第二阶段（第 <see cref="BelialNight.PhaseTwoHour"/> 小时）起上场（老板 2026-10-26 定）。
    ///
    /// 规则：
    ///   · 他会出现在左边或右边门外，站一段时间不动。
    ///   · 挡住他的方式**不是关门**，而是反过来：**把门打开、用手电筒照他**。
    ///   · 每照一下他就白一下（「白贝」），照够 <see cref="FlashCount"/> 下他就走。
    ///   · 站够 <see cref="DoorGraceSeconds"/> 秒还没被照够 —— 这局结束。
    ///
    /// 判断「照到了」的条件由调用方传进来：那一侧的门是开的、且手电筒正亮着。
    /// 纯逻辑，不引用 UnityEngine。
    /// </summary>
    public class BelialEncounter
    {
        /// <summary>从第几个小时开始上场（= 贝利亚之夜的中点）。</summary>
        public const int AppearanceHour = BelialNight.PhaseTwoHour;

        /// <summary>照够几下他才走。</summary>
        public const int FlashCount = 5;

        /// <summary>第一次出现前先安静这么久（秒）。</summary>
        public const float FirstDelaySeconds = 14f;

        /// <summary>他走之后最短隔多久再来。</summary>
        public const float MinGapSeconds = 26f;

        /// <summary>他走之后最长隔多久再来。</summary>
        public const float MaxGapSeconds = 48f;

        /// <summary>站在门外等你照他的宽限时间（秒）。超了这局结束。</summary>
        public const float DoorGraceSeconds = 9f;

        /// <summary>两下闪光之间至少隔这么久，免得按住手电筒一秒算五下。</summary>
        public const float FlashCooldownSeconds = 0.32f;

        /// <summary>被照到之后保持「白贝」状态多久（秒）。</summary>
        public const float WhiteSeconds = 0.85f;

        /// <summary>照够之后他退场所用的时间（秒）。</summary>
        public const float RetreatSeconds = 1.6f;

        private readonly IRandomSource _random;

        private bool _atDoor;
        private bool _retreating;
        private bool _kill;
        private BelialSide _side;
        private int _flashes;
        private int _visits;
        private float _timer;
        private float _graceLeft;
        private float _cooldownLeft;
        private float _whiteLeft;
        private float _retreatLeft;

        /// <summary>用指定的骰子创建（测试用固定档位）。</summary>
        public BelialEncounter(IRandomSource random)
        {
            _random = random ?? new SystemRandomSource();
            Reset();
        }

        /// <summary>他现在是不是站在门外等着被照。</summary>
        public bool AtDoor
        {
            get { return _atDoor && !_retreating; }
        }

        /// <summary>他是不是已经照够、正在退场。</summary>
        public bool IsRetreating
        {
            get { return _atDoor && _retreating; }
        }

        /// <summary>他在哪一边。</summary>
        public BelialSide Side
        {
            get { return _side; }
        }

        /// <summary>这一趟已经被照了几下。</summary>
        public int Flashes
        {
            get { return _flashes; }
        }

        /// <summary>当前是不是「白贝」状态（刚被照到的那一下）。</summary>
        public bool IsWhite
        {
            get { return _whiteLeft > 0f; }
        }

        /// <summary>这一局他一共来过几次。</summary>
        public int Visits
        {
            get { return _visits; }
        }

        /// <summary>还要站多久才动手（秒）。他没在门外时是 0。</summary>
        public float GraceLeft
        {
            get { return AtDoor ? _graceLeft : 0f; }
        }

        /// <summary>再过多久他会来（秒）。他已经在门外时是 0。</summary>
        public float SecondsUntilAppear
        {
            get { return _atDoor ? 0f : (_timer > 0f ? _timer : 0f); }
        }

        /// <summary>
        /// 推进计时。<paramref name="belialActive"/> 为 false（还没到第 6 小时）时他不会来。
        /// </summary>
        public void Tick(float deltaSeconds, bool belialActive)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            if (_whiteLeft > 0f)
            {
                _whiteLeft -= deltaSeconds;
                if (_whiteLeft < 0f)
                {
                    _whiteLeft = 0f;
                }
            }

            if (_cooldownLeft > 0f)
            {
                _cooldownLeft -= deltaSeconds;
                if (_cooldownLeft < 0f)
                {
                    _cooldownLeft = 0f;
                }
            }

            if (!_atDoor)
            {
                if (!belialActive)
                {
                    return;
                }

                _timer -= deltaSeconds;
                if (_timer <= 0f)
                {
                    Appear();
                }

                return;
            }

            if (_retreating)
            {
                _retreatLeft -= deltaSeconds;
                if (_retreatLeft <= 0f)
                {
                    _atDoor = false;
                    _retreating = false;
                    _timer = NextGap();
                }

                return;
            }

            _graceLeft -= deltaSeconds;
            if (_graceLeft <= 0f)
            {
                _kill = true;
                BeginRetreat();
            }
        }

        /// <summary>
        /// 玩家照了他一下：门得开着、手电筒得亮着，并且离上次闪光隔够冷却。
        /// 返回 true 表示这一下真的算数。
        /// </summary>
        public bool TryFlash(bool doorOpen, bool flashlightOn)
        {
            if (!AtDoor || !doorOpen || !flashlightOn || _cooldownLeft > 0f)
            {
                return false;
            }

            _flashes++;
            _cooldownLeft = FlashCooldownSeconds;
            _whiteLeft = WhiteSeconds;

            if (_flashes >= FlashCount)
            {
                BeginRetreat();
            }

            return true;
        }

        /// <summary>
        /// 取走「这一下致命」的信号 —— 取过一次就清掉，调用方拿它去 MarkCaught。
        /// </summary>
        public bool ConsumeKill()
        {
            if (!_kill)
            {
                return false;
            }

            _kill = false;
            return true;
        }

        /// <summary>换夜 / 重开一局时清空。</summary>
        public void Reset()
        {
            _atDoor = false;
            _retreating = false;
            _kill = false;
            _side = BelialSide.Left;
            _flashes = 0;
            _visits = 0;
            _timer = FirstDelaySeconds;
            _graceLeft = 0f;
            _cooldownLeft = 0f;
            _whiteLeft = 0f;
            _retreatLeft = 0f;
        }

        private void Appear()
        {
            _side = _random.Range(0, 2) == 0 ? BelialSide.Left : BelialSide.Right;
            _flashes = 0;
            _graceLeft = DoorGraceSeconds;
            _atDoor = true;
            _retreating = false;
            _visits++;
        }

        private void BeginRetreat()
        {
            _retreating = true;
            _retreatLeft = RetreatSeconds;
        }

        private float NextGap()
        {
            int gap = _random.Range((int)MinGapSeconds, (int)MaxGapSeconds + 1);

            // 骰子只保证「[min, max)」这个约定，实现得糙的话可能给到界外 —— 兜一下
            if (gap < MinGapSeconds)
            {
                gap = (int)MinGapSeconds;
            }

            if (gap > MaxGapSeconds)
            {
                gap = (int)MaxGapSeconds;
            }

            return gap;
        }
    }
}
