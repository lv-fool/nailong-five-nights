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

            // 到门口之后开始累计「一次移动机会」的时长
            DoorTimer += dt;

            // 还没到下一次机会 —— 不管门开还是门关，都先站着等。
            // 用 DoorGraceSeconds（= 该角色自己的移动机会间隔）而不是
            // MoveIntervalSeconds：后者是「走廊推进」的掷骰间隔，
            // 两者在原版里是同一个数，但这里语义上要用门口那个。
            if (DoorTimer < DoorGraceSeconds)
            {
                return;
            }

            // 机会到了，根据此刻门的开关做决定
            DoorTimer = 0f;

            if (ctx.IsDoorClosed(Side))
            {
                Retreat();   // 门关着：这次机会用来退走
                return;
            }

            // 门开着：这次机会用来动手
            IsAttacking = true;
        }

        public override void Retreat()
        {
            base.Retreat();
            DoorTimer = 0f;
        }
    }
}
