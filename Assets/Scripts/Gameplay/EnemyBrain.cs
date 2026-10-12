namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 敌人 AI 的公共骨架。
    ///
    /// 推进方式沿用 FNAF 1 的经典掷骰：每隔 MoveIntervalSeconds 掷一次 1~20 的骰子，
    /// 结果不超过活动度就前进一格。活动度越高，敌人越勤快；活动度 0 则整夜不动。
    ///
    /// 「格」的含义由子类定义 —— 门类敌人是「远处 → 走廊 → 门口」，
    /// 噜噜多一层「门外」，奶龙也走这条路（右门），只是它的每一步得先偷到电，
    /// 所以它把 <see cref="CanAdvance"/> 覆盖成 false，关掉这里这套通用掷骰。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public abstract class EnemyBrain
    {
        /// <summary>活动度上限（同 FNAF 1 的 AI 等级）。</summary>
        public const int MaxActivityLevel = 20;

        /// <summary>默认掷骰间隔（秒）。</summary>
        public const float DefaultMoveIntervalSeconds = 5f;

        public EnemyId Id { get; private set; }

        /// <summary>活动度 0 ~ 20。0 表示整夜不动。</summary>
        public int ActivityLevel { get; private set; }

        public float MoveIntervalSeconds { get; private set; }

        /// <summary>距离下一次掷骰已经攒了多少秒。</summary>
        protected float MoveTimer;

        /// <summary>当前位置阶梯。0 = 起点，越大越靠近玩家。具体含义由子类定义。</summary>
        public int Stage { get; protected set; }

        /// <summary>是否已经触发跳杀。</summary>
        public bool IsAttacking { get; protected set; }

        /// <summary>本帧是否刚从门口 / 门外被赶走（供音效与提示用）。</summary>
        public bool RetreatedThisTick { get; protected set; }

        protected readonly IRandomSource Rng;

        protected EnemyBrain(EnemyId id, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds)
        {
            Id = id;
            Rng = rng;
            ActivityLevel = ClampActivity(activityLevel);
            MoveIntervalSeconds = moveIntervalSeconds > 0f
                ? moveIntervalSeconds
                : DefaultMoveIntervalSeconds;
        }

        /// <summary>推进一帧。dt 为真实秒数。</summary>
        public void Tick(float dt, EnemyContext ctx)
        {
            if (IsAttacking || dt <= 0f)
            {
                return;
            }

            RetreatedThisTick = false;

            OnTick(dt, ctx);

            // 本帧已经动手、或被赶走，就不再推进
            if (IsAttacking || RetreatedThisTick)
            {
                return;
            }

            if (!CanAdvance(ctx))
            {
                return;
            }

            MoveTimer += dt;
            if (MoveTimer < MoveIntervalSeconds)
            {
                return;
            }

            MoveTimer = 0f;

            // 掷 1 ~ 20 的骰子，不超过活动度就前进
            if (Rng.Range(1, MaxActivityLevel + 1) <= ActivityLevel)
            {
                Advance(ctx);
            }
        }

        /// <summary>子类：每帧的特殊逻辑（门口计时、偷电、换位等）。</summary>
        protected virtual void OnTick(float dt, EnemyContext ctx)
        {
        }

        /// <summary>子类：现在能不能往前推进。</summary>
        protected virtual bool CanAdvance(EnemyContext ctx)
        {
            return Stage < MaxStage;
        }

        /// <summary>子类：掷骰成功，前进一格。</summary>
        protected abstract void Advance(EnemyContext ctx);

        /// <summary>子类：位置阶梯的终点。公开是为了让调度层能判断「谁站在门口」。</summary>
        public virtual int MaxStage
        {
            get { return 2; }
        }

        /// <summary>退回起点。子类在门口 / 门外被赶走时调用。</summary>
        public virtual void Retreat()
        {
            Stage = 0;
            MoveTimer = 0f;
            RetreatedThisTick = true;
        }

        /// <summary>运行中调整活动度（切夜、调试用）。</summary>
        public void SetActivityLevel(int level)
        {
            ActivityLevel = ClampActivity(level);
        }

        private static int ClampActivity(int level)
        {
            if (level < 0)
            {
                return 0;
            }

            return level > MaxActivityLevel ? MaxActivityLevel : level;
        }
    }
}
