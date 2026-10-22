namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 门类敌人：袋鼠（左门）与牛来（右门）；奶龙也走这条路（右门）。
    ///
    /// **照 FNAF 1 沿固定机位路径逐房间推进**（袋鼠 ≈ 邦尼、牛来 ≈ 奇卡）：
    /// 每一步停在一个监控机位上，走过的机位都能在监控里被拍到。
    /// 路径走完（<see cref="MaxStage"/>）就是**站到了门口** ——
    /// 这段不再属于任何机位，监控里看不到它，只能靠门口那盏灯现形。
    ///
    /// 到了门口：门关着 → 被挡回去；门开着 → 撑过 DoorGraceSeconds 就动手
    /// （这个窗口照原版角色的移动机会间隔：邦尼 4.97s / 奇卡 4.98s / 弗莱迪 3.02s）。
    ///
    /// **右门是共用通道** —— 门口已经站着别人时，后来者停在走廊等（先来后到）。
    ///
    /// 没给机位路径时（奶龙）退回原来的三档：远处(0) → 走廊(1) → 门口(2)，
    /// <see cref="MaxStage"/> = 2。奶龙的「蹲机位偷电」另有它自己的 CameraIndex，
    /// 与这里的路径无关。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public class DoorEnemyBrain : EnemyBrain
    {
        // 动手窗口 = 原版「移动机会」间隔（technicalfnaf 核实）：
        // 邦尼 4.97s、奇卡 4.98s、弗莱迪 3.02s。
        // 原版里到门口后是「下一次移动机会」就动手，所以玩家能反应的一格就是它。
        public const float BonnieDoorWindowSeconds = 4.97f;
        public const float ChicaDoorWindowSeconds = 4.98f;
        public const float FreddyDoorWindowSeconds = 3.02f;

        /// <summary>没按角色指定时的兜底窗口 —— 取原版邦尼/奇卡那一档（≈5 秒）。</summary>
        public const float DefaultDoorGraceSeconds = BonnieDoorWindowSeconds;

        /// <summary>没配置机位路径时的默认阶梯终点（远处 → 走廊 → 门口）。</summary>
        public const int DefaultMaxStage = 2;

        /// <summary>它盯的是哪一侧的门。</summary>
        public Side Side { get; private set; }

        /// <summary>
        /// 到门口后的**「移动机会」间隔**（秒）—— 门开着还是关着都用它。
        ///
        /// 原版语义：敌人每次移动机会才做一次决定，
        ///   门关着 → 这次机会用来退走
        ///   门开着 → 这次机会用来动手
        /// 所以它同时是「关门后多久才走」和「开门后多久才动手」。
        ///
        /// 名字沿用旧称 DoorGraceSeconds（大量引用和测试都在用），
        /// 但含义已经从「开门宽限」扩展成「回合间隔」。
        /// </summary>
        public float DoorGraceSeconds { get; private set; }

        /// <summary>已经站在门口多久（秒）。派生类（奶龙换机位时）要能清零，所以是 protected set。</summary>
        public float DoorTimer { get; protected set; }

        /// <summary>
        /// 「最后一搏」窗口（秒）—— 移动机会攒满后才开始算。
        ///
        /// 规则（按用户 2026-10-22 定）：
        /// <list type="number">
        /// <item>到门口后先攒满 <see cref="DoorGraceSeconds"/>（一个移动机会）。</item>
        /// <item>攒满后**不立刻决定**，而是再进入 2 秒的缓冲期。</item>
        /// <item>这 2 秒里门**一旦被打开就立刻重置**，重新计时 —— 玩家还有补救机会。</item>
        /// <item>2 秒走完：门还开着 → 动手；门关着 → 退走。</item>
        /// </list>
        ///
        /// 为什么加这一段：原来的实现里「攒满就立刻判」，
        /// 玩家在最后一刻开门会被立刻抓住、关门的瞬间也可能刚好撞上判定，
        /// 手感像「不给反应」。多这 2 秒既是给玩家的宽限，
        /// 也让「在门口反复开关门」变成一个有代价的赌博。
        /// </summary>
        public const float LastChanceSeconds = 2f;

        /// <summary>「最后一搏」已经走了多久（秒）。只在 <see cref="DoorTimer"/> 攒满后才累计。</summary>
        public float LastChanceTimer { get; protected set; }

        /// <summary>是否已经进入「最后一搏」阶段（移动机会攒满了）。</summary>
        public bool InLastChance
        {
            get { return Stage >= MaxStage && DoorTimer >= DoorGraceSeconds; }
        }

        /// <summary>
        /// 上一帧门是不是关着 —— 用来检测「从关变开」的那个上升沿。
        /// </summary>
        private bool _doorWasClosed;

        /// <summary>
        /// 最后一搏期间门被打开过 —— 下一帧要把 2 秒清零重计。
        ///
        /// 单独记一个标记而不是直接清零，是因为「检测上升沿」和「清零计时」
        /// 发生在 OnTick 的不同阶段（先攒移动机会、再走最后一搏），
        /// 中间隔着 return；用标记能保证不管哪一步返回都不会漏掉这次重置。
        /// </summary>
        private bool _doorWasOpenedWhileLastChance;

        /// <summary>
        /// 沿路停留的监控机位（下标）—— 照 FNAF 1 的房间顺序。
        /// 空表表示没配路径，走默认三档。
        /// ⚠️ 不是 readonly：奶龙是构造之后再调 <see cref="SetCameraPath"/> 补上的。
        /// </summary>
        private int[] _cameraPath;

        /// <summary>是否配了机位路径（配了就按机位逐房间推进，否则走默认三档）。</summary>
        public bool HasCameraPath
        {
            get { return _cameraPath != null && _cameraPath.Length > 0; }
        }

        /// <summary>
        /// 运行时配置推进路径（奶龙用）。
        /// 奶龙构造时还不知道路径（要先算它蹲点偷电的机位表），
        /// 建完再调这个方法把「弗里迪路线」补上 —— 之后 MaxStage = 路径长度。
        /// </summary>
        public void SetCameraPath(int[] path)
        {
            _cameraPath = (path != null && path.Length > 0) ? (int[])path.Clone() : null;
        }

        /// <summary>
        /// **仅调试用**：传送到指定机位。
        ///
        /// PathCameraIndex 是根据 Stage 查 _cameraPath 算出来的，不是独立字段，
        /// 所以要反查：找到 path 里第一个等于 cameraIndex 的位置，把 Stage 设过去。
        /// 传 -1 表示送到门口（Stage 到顶）。
        /// </summary>
        public override void DebugTeleport(int cameraIndex)
        {
            if (!HasCameraPath)
            {
                return;
            }

            if (cameraIndex < 0)
            {
                Stage = _cameraPath.Length;   // 到顶 = 站在门口
                DoorTimer = 0f;
                return;
            }

            for (int i = 0; i < _cameraPath.Length; i++)
            {
                if (_cameraPath[i] == cameraIndex)
                {
                    Stage = i;
                    DoorTimer = 0f;
                    MoveTimer = 0f;
                    return;
                }
            }
        }
        /// <summary>
        /// 当前停在哪个监控机位（下标）。**站在门口**（<see cref="Stage"/> 到顶）、
        /// 或没配路径时返回 -1 —— 两者都不属于任何机位。
        /// </summary>
        public int PathCameraIndex
        {
            get
            {
                if (!HasCameraPath || Stage < 0 || Stage >= _cameraPath.Length)
                {
                    return -1;
                }

                return _cameraPath[Stage];
            }
        }

        public DoorEnemyBrain(EnemyId id, Side side, int activityLevel, IRandomSource rng)
            : this(id, side, activityLevel, rng, DefaultMoveIntervalSeconds, DefaultDoorGraceSeconds)
        {
        }

        public DoorEnemyBrain(EnemyId id, Side side, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, float doorGraceSeconds)
            : this(id, side, activityLevel, rng, moveIntervalSeconds, doorGraceSeconds, null)
        {
        }

        /// <summary>
        /// 带机位路径构造 —— <paramref name="cameraPath"/> 是沿路停留的机位下标，
        /// 最后一个机位之后就是门口（<see cref="MaxStage"/> = 路径长度）。
        /// 传 null / 空表退回默认三档。
        /// </summary>
        public DoorEnemyBrain(EnemyId id, Side side, int activityLevel, IRandomSource rng,
            float moveIntervalSeconds, float doorGraceSeconds, int[] cameraPath)
            : base(id, activityLevel, rng, moveIntervalSeconds)
        {
            Side = side;
            DoorGraceSeconds = doorGraceSeconds > 0f ? doorGraceSeconds : DefaultDoorGraceSeconds;

            _cameraPath = (cameraPath != null && cameraPath.Length > 0)
                ? (int[])cameraPath.Clone()
                : null;
        }

        /// <summary>位置阶梯的终点：配了路径就是路径长度，否则默认三档的 2（门口）。</summary>
        public override int MaxStage
        {
            get { return HasCameraPath ? _cameraPath.Length : DefaultMaxStage; }
        }

        /// <summary>再走一步就到门口，可门口已经站着别人 —— 留在走廊等。</summary>
        protected override bool CanAdvance(EnemyContext ctx)
        {
            if (Stage >= MaxStage)
            {
                return false;
            }

            if (Stage + 1 >= MaxStage && ctx.IsDoorTakenByOther(Side, Id))
            {
                return false;
            }

            return true;
        }

        protected override void Advance(EnemyContext ctx)
        {
            Stage = Stage + 1;

            if (Stage >= MaxStage)
            {
                DoorTimer = 0f;
            }
        }

        /// <summary>
        /// 站在门口之后的行为 —— **照原版 FNAF 1 的回合制**。
        ///
        /// 关键：无论门开还是门关，它都不会「立刻」做反应，
        /// 而是等到**下一次移动机会**（每 MoveIntervalSeconds 一次）才行动：
        ///
        ///   门关着 → 用这次机会**退走**
        ///   门开着 → 用这次机会**动手**
        ///
        /// 所以玩家看到的是：
        ///   · 关上门后它还要在门口站一会儿才走（不是一关就消失）
        ///   · 打开门后它也不是立刻扑进来，得等下一次机会
        ///
        /// 早先的实现是「关门立刻退、开门立刻开始算动手计时」，
        /// 等于关门即无敌、开门即必死，两头都没有喘息。
        /// </summary>
        protected override void OnTick(float dt, EnemyContext ctx)
        {
            if (Stage < MaxStage)
            {
                return;
            }

            // ── 第一阶段：攒够一个「移动机会」 ──
            // 用 DoorGraceSeconds（= 该角色自己的移动机会间隔）而不是
            // MoveIntervalSeconds：后者是「走廊推进」的掷骰间隔。
            //
            // ⚠️ dt 可能一次很大（测试里会一步 Tick(3f)）——
            // 攒满之后**剩下的那点时间要留给第二阶段**，不能整份都吃掉，
            // 否则一步 Tick(3f) 会直接把 2 秒的最后一搏一起冲过去、当场退走。
            float remaining = dt;

            // 每帧先更新「门这一帧是开还是关」，供最后一搏检测上升沿用。
            // 必须在攒移动机会之前更新，否则「攒满那一帧门刚好打开」会漏判。
            bool doorClosedNow = ctx.IsDoorClosed(Side);

            if (doorClosedNow && !_doorWasClosed && DoorTimer >= DoorGraceSeconds)
            {
                // 已经在最后一搏里、门又关上 → 不重置，继续走（下面正常累计）
            }
            else if (!doorClosedNow && _doorWasClosed && DoorTimer >= DoorGraceSeconds)
            {
                // 门刚被打开 → 标记「下一帧清零重计」
                _doorWasOpenedWhileLastChance = true;
            }

            _doorWasClosed = doorClosedNow;

            // 记录「这一帧是不是刚好攒满」—— 只有这一帧才做「门开着就动手」的判定。
            //
            // ⚠️ 不能写成「攒满后只要门开着就动手」：那样玩家在最后一搏中途开门，
            // 下一帧就会被判定动手，和「开门重置 2 秒」的设计自相矛盾。
            bool justFilled = DoorTimer < DoorGraceSeconds;

            if (justFilled)
            {
                float need = DoorGraceSeconds - DoorTimer;
                float spend = remaining < need ? remaining : need;

                DoorTimer += spend;
                remaining -= spend;

                // 还在攒 —— 门开还是门关都先站着，不做决定
                if (DoorTimer < DoorGraceSeconds)
                {
                    return;
                }
            }

            bool closed = ctx.IsDoorClosed(Side);

            // ── 攒满的那一刻：门开着就**立刻动手** ──
            //
            // 这里的语义是「它扑上来了」—— 移动机会刚到、门又开着，
            // 没有理由再等 2 秒。2 秒缓冲只服务于「门关着」的情况。
            if (justFilled && !closed)
            {
                IsAttacking = true;
                return;
            }

            // ── 门关着：进入「最后一搏」2 秒 ──
            //
            // 规则（用户 2026-10-22 定）：
            //   · 攒满移动机会、且门关着 → 开始计这 2 秒
            //   · 期间**门被打开的那一刻**重置计时（从关变开 = 上升沿）
            //   · 2 秒走完门还关着 → 退走
            //
            // 「门被打开就重置」的含义：玩家在最后关头开门，它反而要重来一遍，
            // 于是「反复开关门」变成一场有代价的赌博，而不是无脑按住关门。
            if (_doorWasOpenedWhileLastChance)
            {
                LastChanceTimer = 0f;
                _doorWasOpenedWhileLastChance = false;
            }

            // 用**剩余**的 dt（见第一阶段注释），不是整份 dt
            LastChanceTimer += remaining;

            if (LastChanceTimer < LastChanceSeconds)
            {
                return;   // 这 2 秒还没走完，继续站着
            }

            // 2 秒走完、门一直关着 → 退走
            //
            // ⚠️ 这里**不能**再补一句 IsAttacking = true —— 早先改逻辑时
            // 残留了一行无条件赋值，导致「退走」和「动手」同时发生：
            // Stage 归零（看着像走了）、IsAttacking 却是 true（下一帧立刻判突破），
            // 玩家会看到它明明退了、却照样跳杀。
            LastChanceTimer = 0f;
            Retreat();
        }

        public override void Retreat()
        {
            base.Retreat();
            DoorTimer = 0f;
            LastChanceTimer = 0f;
            _doorWasClosed = false;
        }
    }
}
