namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 贝利亚在**故事模式**里的彩蛋（2026-10-30 定）。
    ///
    /// 和贝利亚之夜里那套「开门用手电筒照」完全不同 —— 这不是一个能打的敌人，
    /// 是一条**极小概率的死亡陷阱**：
    ///
    ///   · 每 <see cref="RollIntervalSeconds"/> 秒掷一次骰，命中率 <see cref="TriggerChance"/>（0.05%）；
    ///   · 掷中之后不立刻发作，而是**挂起**，等他「出现在下一次监控屏幕上」——
    ///     也就是说玩家每开一次监控，都有可能正好撞上他；
    ///   · 真撞上时开始 <see cref="ReactionSeconds"/> 的倒计时：**1.5 秒内必须放下监控**；
    ///   · 放下监控 = 躲过去（虚影散掉，什么也不会发生）；
    ///   · 超时 = 他动手，播贝利亚跳杀，这一局结束；
    ///   · 他在屏幕上的这 1.5 秒里**不许切机位**（手已经不听使唤了）。
    ///
    /// 只写在**第 4、5 夜**彩蛋，且只在故事模式里 —— 永夜 / 自定义夜 / 贝利亚之夜都不参与
    /// （那三种模式要么没有「第几夜」，要么本来就全是贝利亚）。
    ///
    /// 纯 C#、不引用 UnityEngine，EditMode 里可以直接 new 出来测。
    /// </summary>
    public sealed class BelialCameo
    {
        /// <summary>多久掷一次骰（秒）。</summary>
        public const float RollIntervalSeconds = 5f;

        /// <summary>一次掷骰命中「贝利亚」的概率。</summary>
        public const float TriggerChance = 0.0005f;

        /// <summary>
        /// 掷骰的整数分母 —— <see cref="IRandomSource.Range"/> 只能取整数，
        /// 所以 0.05% 写成「1 / 2000」。**改概率改这里，别改上面的 float**（两处必须一致）。
        /// </summary>
        public const int RollOdds = 2000;

        /// <summary>看到他的大头之后，还有多少秒可以放下监控。</summary>
        public const float ReactionSeconds = 1.5f;

        /// <summary>彩蛋参与判定的最低夜（0 起）。3 = 第 4 夜。</summary>
        public const int FirstNightIndex = 3;

        /// <summary>彩蛋参与判定的最高夜（0 起）。4 = 第 5 夜。</summary>
        public const int LastNightIndex = 4;

        private readonly IRandomSource _random;

        private float _rollTimer;
        private bool _pending;
        private bool _onScreen;
        private float _reactionLeft;
        private bool _expired;
        private int _triggers;
        private int _escapes;

        /// <summary>用指定的骰子创建（测试用固定档位；生产走 <see cref="SystemRandomSource"/>）。</summary>
        public BelialCameo(IRandomSource random)
        {
            _random = random ?? new SystemRandomSource();
        }

        /// <summary>掷中了，正等着玩家下一次打开监控（此时屏幕上还什么都没有）。</summary>
        public bool Pending
        {
            get { return _pending; }
        }

        /// <summary>他的大头正压在监控画面上，倒计时进行中。</summary>
        public bool OnScreen
        {
            get { return _onScreen; }
        }

        /// <summary>还要在屏幕上待多少秒就动手（<see cref="OnScreen"/> 为假时是 0）。</summary>
        public float ReactionLeft
        {
            get { return _onScreen ? _reactionLeft : 0f; }
        }

        /// <summary>这 1.5 秒里切机位的操作要不要挡掉。</summary>
        public bool BlocksCameraSwitch
        {
            get { return _onScreen; }
        }

        /// <summary>这一局他盯上过玩家几次（躲过去也算，成就 / 调试用）。</summary>
        public int Triggers
        {
            get { return _triggers; }
        }

        /// <summary>这一局玩家成功在一秒半内放下监控的次数。</summary>
        public int Escapes
        {
            get { return _escapes; }
        }

        /// <summary>某个夜（0 起）是否有彩蛋。</summary>
        public static bool NightHasCameo(int nightIndex)
        {
            return nightIndex >= FirstNightIndex && nightIndex <= LastNightIndex;
        }

        /// <summary>
        /// 每帧推进。
        /// </summary>
        /// <param name="deltaSeconds">这一帧的秒数。</param>
        /// <param name="eligible">
        /// 这一刻允不允许掷骰：故事模式、第 4 或 5 夜、玩家还活着。
        /// ⚠️ 只挡**掷骰**，不挡已经挂起 / 已经在屏幕上的那一次 ——
        /// 否则玩家一死（eligible 变 false）就再也不会超时，彩蛋白白躲过去。
        /// </param>
        /// <param name="monitorUp">监控此刻是不是升起的。</param>
        public void Tick(float deltaSeconds, bool eligible, bool monitorUp)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            // ── 已经在屏幕上：唯一要做的是倒计时，或者被玩家放下监控 ──
            if (_onScreen)
            {
                if (!monitorUp)
                {
                    // 放下了 —— 躲过去，虚影散掉，这一夜他不会再找上来（要重新掷骰）
                    _onScreen = false;
                    _reactionLeft = 0f;
                    _escapes++;
                    _rollTimer = 0f;
                    return;
                }

                _reactionLeft -= deltaSeconds;
                if (_reactionLeft <= 0f)
                {
                    _reactionLeft = 0f;
                    _onScreen = false;
                    _expired = true;
                }

                return;
            }

            // ── 挂起中：等玩家把监控升起来 ──
            //
            // 这里**故意不看 eligible**：他已经在「下一次屏幕上」等着了，
            // 玩家不开监控就永远撞不上 —— 这本身就是躲开他的办法。
            if (_pending)
            {
                if (monitorUp)
                {
                    _pending = false;
                    _onScreen = true;
                    _reactionLeft = ReactionSeconds;
                    _triggers++;
                }

                return;
            }

            // ── 平时：每 5 秒掷一次骰 ──
            if (!eligible)
            {
                _rollTimer = 0f;
                return;
            }

            _rollTimer += deltaSeconds;

            if (_rollTimer < RollIntervalSeconds)
            {
                return;
            }

            // 用减法不用清零：帧率不稳时也不会把多出来的时间丢掉
            _rollTimer -= RollIntervalSeconds;

            if (_random.Range(0, RollOdds) == 0)
            {
                _pending = true;
            }
        }

        /// <summary>
        /// 取走「他动手了」的信号 —— 取过一次就清掉，调用方拿它去 MarkCaught。
        /// </summary>
        public bool ConsumeExpired()
        {
            if (!_expired)
            {
                return false;
            }

            _expired = false;
            return true;
        }

        /// <summary>重开一夜时清空（挂起与屏幕上的都清掉，掷骰也重新计时）。</summary>
        public void Reset()
        {
            _rollTimer = 0f;
            _pending = false;
            _onScreen = false;
            _reactionLeft = 0f;
            _expired = false;
            _triggers = 0;
            _escapes = 0;
        }

        /// <summary>
        /// 调试用：直接把「下一次监控」占上，不用等那 0.05% 的骰子。
        ///
        /// ⚠️ 置的是 **挂起**（<see cref="Pending"/>）而不是「已经在屏幕上」。
        /// 早先写的是后者，结果玩家点完按钮、监控还是放下的，下一帧
        /// <see cref="Tick"/> 就把「监控没升起来」当成「玩家已经放下了」，
        /// 直接算躲过去 —— 虚影一帧都没画出来，看着就像没生效。
        /// 挂起语义和真实触发一模一样：升起监控才撞上。
        /// </summary>
        public void DebugArm()
        {
            _onScreen = false;
            _reactionLeft = 0f;
            _pending = true;
        }
    }
}
