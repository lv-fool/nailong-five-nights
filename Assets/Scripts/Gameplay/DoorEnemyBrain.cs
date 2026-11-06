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
    /// 到了门口分两个阶段，**每个阶段的末尾都判一次门，门开着就动手**：
    ///   · **读数阶段**（<see cref="DoorWindow"/>）：门一直开着 → 走完就扑。
    ///     长度随活动度线性走：**活动度 0 → 7.62 秒，满级 → 各角色原值**
    ///     （袋鼠 4.97s / 牛来 4.98s，照原版邦尼/奇卡的移动机会间隔）
    ///   · **最后一搏**（<see cref="LastChanceSeconds"/>，随活动度 1~3 秒）：
    ///     门**一关**就进这一段（不用等读数走完）；走完时门开着 → 扑，门关着 → 退走。
    ///     期间门被打开会**重置**计时，等于重新给一份窗口。
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

        /// <summary>
        /// 黑化之后必须「开灯 + 关门」才拦得住 —— 光关门它照样会进来。
        /// 贝利亚之夜第一阶段起为 true（除非拿到「谷维素」把它放宽）。
        /// </summary>
        public bool RequireLightToBlock { get; set; }

        /// <summary>
        /// 「读条」时长倍率（贝利亚之夜的「数到三」给 1.35）—— 大于 1 表示给玩家更多反应时间。
        /// 只作用于门口那一格，不改变它在走廊里走的节奏。
        /// </summary>
        public float TelegraphMultiplier { get; set; } = 1f;

        /// <summary>门口的额外宽限秒数（贝利亚之夜的「去甲肾上腺素」/「喹硫平」）。</summary>
        public float GraceBonusSeconds { get; set; }

        /// <summary>
        /// 活动度 **0** 时「读数窗口」有多长（秒）。活动度满级时线性压回各角色自己的
        /// <see cref="DoorGraceSeconds"/>。
        ///
        /// 用户 2026-11-04 定：**AI 值 0~20 对应 7.62s → 原值**。
        /// 低难度给玩家更长的反应窗口，高难度压回原版节奏。
        ///
        /// ⚠️ 只作用于**读数**（门一直开着的那段）。最后一搏另有
        /// <see cref="LastChanceSeconds"/>（1~3 秒，也是难度越高越久）。
        /// </summary>
        public const float DoorWindowAtZeroActivity = 7.62f;

        /// <summary>
        /// 按当前活动度折算出来的**读数窗口**（秒），**不含**贝利亚夜里那些加成。
        ///
        /// 活动度 0 → <see cref="DoorWindowAtZeroActivity"/>（7.62s）；
        /// 活动度满级 → <see cref="DoorGraceSeconds"/>（袋鼠 4.97 / 牛来 4.98）。
        /// 中间线性。运行时 <c>SetActivityLevel</c> 一改就立刻生效，不用重建。
        /// </summary>
        public float DoorReadoutSeconds
        {
            get
            {
                float t = ActivityLevel / (float)MaxActivityLevel;
                if (t < 0f) { t = 0f; }
                if (t > 1f) { t = 1f; }

                return DoorWindowAtZeroActivity
                    + (DoorGraceSeconds - DoorWindowAtZeroActivity) * t;
            }
        }

        /// <summary>
        /// 真正用的窗口长度：按活动度折算的读数窗口，再加贝利亚那些加成、乘倍率。
        /// </summary>
        public float DoorWindow
        {
            get
            {
                float window = DoorReadoutSeconds + GraceBonusSeconds;

                if (window < 0f)
                {
                    window = 0f;
                }

                return TelegraphMultiplier > 0f ? window * TelegraphMultiplier : window;
            }
        }

        /// <summary>没配置机位路径时的默认阶梯终点（远处 → 走廊 → 门口）。</summary>
        public const int DefaultMaxStage = 2;

        /// <summary>它盯的是哪一侧的门。</summary>
        public Side Side { get; private set; }

        /// <summary>
        /// 门**一直开着**时，它在门口撑多久就动手（秒）—— **活动度满级**时的值。
        ///
        /// 照原版 FNAF 1 的「移动机会」间隔定（邦尼 4.97 / 奇卡 4.98 / 弗莱迪 3.02）。
        /// 也正是玩家从「它站到门口」到「必须关门」的**反应窗口**。
        ///
        /// ⚠️ 真正用的窗口在低难度下比它**长** —— 见 <see cref="DoorReadoutSeconds"/>。
        /// 别直接拿这个字段当窗口用，要窗口请用 <see cref="DoorWindow"/>。
        ///
        /// ⚠️ 门在这个窗口里**任何时刻**关上，都会立刻转到
        /// <see cref="LastChanceSeconds"/> 那条路，不必撑满 —— 见 <see cref="OnTick"/>。
        ///
        /// 名字沿用旧称 DoorGraceSeconds（大量引用和测试都在用）。
        /// </summary>
        public float DoorGraceSeconds { get; private set; }

        /// <summary>已经站在门口多久（秒）。派生类（奶龙换机位时）要能清零，所以是 protected set。</summary>
        public float DoorTimer { get; protected set; }

        /// <summary>
        /// 这次站在门口一共敲过几次门（被关着的门挡住时敲）。
        ///
        /// 只有袋鼠会敲 —— 表现层读到这个计数就去放敲门声，
        /// 并在它走人之后往门口放一份外卖（见 <see cref="DeliveryDrop"/>）。
        /// 纯逻辑类不认识音频和道具，这里只记账。
        /// </summary>
        public int KnocksIssued { get; private set; }

        /// <summary>这一趟站门口已经敲过了吗 —— 防「门一直关着每帧都敲」。</summary>
        private bool _knockedThisVisit;

        /// <summary>取走一次「敲门」记录。表现层每帧问一次，没记录就返回 false。</summary>
        public bool ConsumeKnock()
        {
            if (KnocksIssued <= 0)
            {
                return false;
            }

            KnocksIssued--;
            return true;
        }

        /// <summary>
        /// 「最后一搏」窗口的**基准时长**（秒）—— 活动度 0 时的值。
        ///
        /// 规则（按用户 2026-10-22 定）：
        /// <list type="number">
        /// <item>门**一关**就进这段缓冲（不必等 <see cref="DoorGraceSeconds"/> 读数走完）。</item>
        /// <item>缓冲期内门**一旦被打开就重置**，重新计时 —— 玩家还能再补救一次。</item>
        /// <item>缓冲期走完：**门开着 → 动手**（和读数阶段末尾那条对称）；门关着 → 退走。</item>
        /// </list>
        ///
        /// **缓冲时长随难度增加**（见 <see cref="LastChanceSeconds"/>）：
        /// 活动度 0 → 1.0 秒，活动度 20 → 3.0 秒。
        /// 高难度下它赖在门口更久 —— 玩家被逼着持续关门，电掉得更快。
        /// </summary>
        public const float LastChanceBaseSeconds = 1f;

        /// <summary>
        /// 「最后一搏」窗口的**上限**（秒）—— 活动度满级时的值。
        ///
        /// 为什么高难度要**更长**：它站在门口不走的那几秒，玩家必须一直按着关门，
        /// 而关门是持续耗电的。所以「赖得越久」= 拖的电越多 = 越难。
        /// 这和直觉上「高难度反应时间更短」相反，但符合这游戏的资源压力模型 ——
        /// 难度不体现在手速，体现在你还剩多少电。
        /// </summary>
        public const float LastChanceMaxSeconds = 3f;

        /// <summary>
        /// 当前这个敌人的「最后一搏」窗口（秒）—— 按活动度在 1.0 → 3.0 之间线性折算。
        ///
        /// ⚠️ 不再是一个常量：难度直接体现在这段缓冲的长度上。
        /// 活动度越高，它赖在门口越久，玩家被逼着关门更久、电掉得更快。
        /// </summary>
        public float LastChanceSeconds
        {
            get
            {
                float t = ActivityLevel / (float)MaxActivityLevel;
                if (t < 0f) { t = 0f; }
                if (t > 1f) { t = 1f; }

                return LastChanceBaseSeconds
                    + (LastChanceMaxSeconds - LastChanceBaseSeconds) * t;
            }
        }

        /// <summary>「最后一搏」已经走了多久（秒）。只在 <see cref="DoorTimer"/> 攒满后才累计。</summary>
        public float LastChanceTimer { get; protected set; }

        /// <summary>是否已经进入「最后一搏」阶段（移动机会攒满了）。</summary>
        public bool InLastChance
        {
            get { return Stage >= MaxStage && DoorTimer >= DoorWindow; }
        }

        /// <summary>
        /// 上一帧门是不是关着 —— 用来检测「从关变开」的那个上升沿。
        /// </summary>
        private bool _doorWasClosed;

        // ── 进攻失败后的「随机游走」 ──────────────────────────────────
        //
        // 被门挡回来时**不直接回出生地**，而是随机传送到某个机位，
        // 再按活动度决定游走几步才重新开始往门口摸。
        //
        // 为什么这么做（照 FNAF 1 的观感）：原版里邦尼被你关门挡一次之后
        // 不会老实回起点，它会换个房间继续晃 —— 这让玩家觉得它一直在楼里，
        // 而不是每挡一次就重置进度、可以精准预测。

        /// <summary>还要随机游走几步（0 = 正常往门口推进）。</summary>
        public int WanderStepsLeft { get; private set; }

        /// <summary>
        /// 进攻失败后随机游走的**步数**（按活动度折算 1~5 步）。
        /// 活动度越高，被挡回来之后晃得越久、越难预测。
        /// </summary>
        private int WanderStepsForActivity()
        {
            int steps = 1 + (int)((ActivityLevel / (float)MaxActivityLevel) * 4f);
            return steps < 1 ? 1 : (steps > 5 ? 5 : steps);
        }

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
        /// <summary>当前机位路径的副本（「今天出门了」要按它换格子，别直接改内部数组）。</summary>
        public int[] CameraPath
        {
            get { return _cameraPath != null ? (int[])_cameraPath.Clone() : null; }
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

        /// <summary>
        /// 指定 Stage 对应的机位下标；越界或没配路径返回 -1。
        ///
        /// 和 <see cref="PathCameraIndex"/> 的区别：那个只看**当前** Stage，
        /// 这个能查任意一格 —— 用来判断「上一帧它是不是在厨房」。
        /// </summary>
        public int PathCameraIndexAt(int stage)
        {
            if (!HasCameraPath || stage < 0 || stage >= _cameraPath.Length)
            {
                return -1;
            }

            return _cameraPath[stage];
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
            // ── 游走期：不往门口推进，而是在路径上随机换位 ──
            //
            // 被门挡回来之后它会「在楼里晃几步」，晃完才重新往门口摸。
            // 这让玩家没法靠「刚挡回去 = 安全一阵」来精确预测。
            if (WanderStepsLeft > 0)
            {
                WanderStepsLeft--;

                if (HasCameraPath)
                {
                    // 随机跳到一个**不同的**格子，避开出生地（第 0 格）
                    int slots = _cameraPath.Length;
                    if (slots > 1)
                    {
                        int prev = Stage;
                        int pick = 1 + Rng.Range(0, slots - 1);

                        // 原地不动会很怪 —— 撞上了就往前挪一格（到头了往回挪）
                        if (pick == prev)
                        {
                            pick = pick + 1 < slots ? pick + 1 : (pick > 1 ? pick - 1 : 1);
                        }

                        Stage = pick;
                    }
                }

                DoorTimer = 0f;
                return;
            }

            Stage = Stage + 1;

            if (Stage >= MaxStage)
            {
                DoorTimer = 0f;
            }
        }

        /// <summary>
        /// 站在门口之后的行为。两条路二选一，判据只看**门关没关**：
        ///
        ///   · **门关着** → 立刻进「最后一搏」，撑完 <see cref="LastChanceSeconds"/>
        ///     （随活动度 1~3 秒）就退走。期间门被打开只是把这段**重置**，重头再算。
        ///   · **门一直开着** → 撑满 <see cref="DoorWindow"/>（活动度 0 时 7.62 秒、
        ///     满级时 4.97/4.98 秒）就动手。
        ///
        /// 所以玩家看到的是：
        ///   · 关上门后它还要在门口站一会儿才走（不是一关就消失）
        ///   · 打开门后它也不是立刻扑进来，得先撑完那段反应窗口
        ///
        /// ⚠️ 一句话概括：**判定只发生在两个阶段的末尾，那两帧门开着就扑** ——
        ///   · 读数走完那一帧门开着 → 扑
        ///   · 最后一搏走完那一帧门开着 → 扑
        ///
        /// 中途关一次门只能把判定点从「读数末尾」挪到「最后一搏末尾」，**不免疫**。
        /// 反过来说，最后一搏期间开门会把计时清零、重新给一份窗口 ——
        /// 那是「拿电费换时间」的赌博：计时走完时门还开着，就当场扑。
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

            // ⚠️ **门只要关着，就立刻进最后一搏**（用户 2026-11-03 定，同日追加）。
            //
            // 早先是「走到门口先攒一个移动机会（4.97~7.62 秒，看难度），攒满发现门关着才开始计时」——
            // 等于白白给玩家 5 秒免费时间。所以改成：到门口发现门关着，立刻填满。
            //
            // 但只补「刚跨进门口那一下」还不够 —— 读数走到一半才关门的话：
            //   · 它要等 5 秒读数**走完**才进最后一搏（玩家多挨一段）
            //   · 更糟的是，玩家要是在读数走完前又把门打开，
            //     那一帧门是开着的 → 当场扑上来，前面关的那几秒全白费
            // 用户 2026-11-03 追加：**五秒内关过门，一样要进最后一搏**。
            // 所以现在门一关就把 DoorTimer 填满 —— 判定点整个挪到「最后一搏末尾」，
            // 读数阶段再也不会判它动手。
            //
            // 每帧填是安全的：DoorTimer 一旦到 DoorWindow 就只增不减
            //（只有退走 / 换位 / 再次跨进门口才清零），填了不会有副作用。
            if (Stage >= MaxStage && DoorTimer < DoorWindow && ctx.IsDoorClosed(Side))
            {
                DoorTimer = DoorWindow;
            }

            // 每帧先更新「门这一帧是开还是关」，供最后一搏检测上升沿用。
            // 必须在攒移动机会之前更新，否则「攒满那一帧门刚好打开」会漏判。
            bool doorClosedNow = ctx.IsDoorClosed(Side);

            if (doorClosedNow && !_doorWasClosed && DoorTimer >= DoorWindow)
            {
                // 已经在最后一搏里、门又关上 → 不重置，继续走（下面正常累计）
            }
            else if (!doorClosedNow && _doorWasClosed && DoorTimer >= DoorWindow)
            {
                // 门刚被打开 → 标记「下一帧清零重计」
                _doorWasOpenedWhileLastChance = true;
            }

            _doorWasClosed = doorClosedNow;

            // ── 被门挡住 → 敲两下门（放在「攒满」判定**之前**）──
            //
            // ⚠️ 必须在前面：下面那个 `if (justFilled) { ... return; }` 会提前返回，
            // 把敲门吃掉。而「到门口发现门关着」现在是立刻填满 DoorTimer，
            // 走的就是那条提前返回的路径 —— 敲门永远不触发、外卖永远不掉（踩过）。
            //
            // _knockedThisVisit 防「门一直关着每帧都敲」。
            bool doorBlocksNow = ctx.IsDoorClosed(Side)
                && (!RequireLightToBlock || ctx.IsLightOn(Side));

            if (doorBlocksNow && !_knockedThisVisit)
            {
                _knockedThisVisit = true;
                KnocksIssued++;
            }
            else if (!doorBlocksNow)
            {
                _knockedThisVisit = false;   // 门开了 → 下次关门可以再敲
            }

            // 记录「这一帧是不是刚好攒满」—— 只有这一帧才做「门开着就动手」的判定。
            //
            // ⚠️ 不能写成「攒满后只要门开着就动手」：那样玩家在最后一搏中途开门，
            // 下一帧就会被判定动手，和「开门重置 2 秒」的设计自相矛盾。
            bool justFilled = DoorTimer < DoorWindow;

            if (justFilled)
            {
                float need = DoorWindow - DoorTimer;
                float spend = remaining < need ? remaining : need;

                DoorTimer += spend;
                remaining -= spend;

                // 还在攒 —— 门开还是门关都先站着，不做决定
                if (DoorTimer < DoorWindow)
                {
                    return;
                }
            }

            // 黑化怪：关着的门不算数，必须同时照着灯
            bool closed = ctx.IsDoorClosed(Side)
                && (!RequireLightToBlock || ctx.IsLightOn(Side));

            // ── 读数攒满的那一刻：门开着就**立刻动手** ──
            //
            // 这里的语义是「它扑上来了」—— 读数刚到、门又开着，
            // 没有理由再等最后一搏。那段缓冲只服务于「门关着」的情况。
            if (justFilled && !closed)
            {
                IsAttacking = true;
                return;
            }


            // ── 门关着：进入「最后一搏」 ──
            //
            // 规则（用户 2026-10-22 定，2026-11-03 补齐最后一条）：
            //   · 读数攒满、且门关着 → 开始计这段缓冲
            //   · 期间**门被打开的那一刻**重置计时（从关变开 = 上升沿）
            //   · 走完时**门开着 → 动手**；门关着 → **退走**
            //
            // 「门被打开就重置」的含义：玩家在最后关头开门，它反而要重来一遍 ——
            // 于是「反复开关门」是一场**有代价的赌博**：开门能省电、能把判定往后拖，
            // 但计时走完那一刻门还开着，它就当场扑上来。
            //
            // ⚠️ 最后那条「门开着 → 动手」是 2026-11-03 补的。
            // 早先这里是无条件 Retreat()，等于「关过一次门就永久免疫」——
            // 开门零风险，上面说的「赌博」根本没有赌注。
            if (_doorWasOpenedWhileLastChance)
            {
                LastChanceTimer = 0f;
                _doorWasOpenedWhileLastChance = false;
            }

            // 用**剩余**的 dt（见第一阶段注释），不是整份 dt
            LastChanceTimer += remaining;

            if (LastChanceTimer < LastChanceSeconds)
            {
                return;   // 还没走完，继续站着
            }

            // ── 走完了：门开着就动手 ──
            //
            // 和上面「读数攒满那一帧」对称 —— 两个阶段的末尾都判门。
            if (!closed)
            {
                IsAttacking = true;
                return;
            }

            // 门一直关着 → 退走
            //
            // ⚠️ 退走和动手**必须二选一**。早先改逻辑时残留过一行无条件的
            // IsAttacking = true，导致两者同时发生：Stage 归零（看着像走了）、
            // IsAttacking 却是 true（下一帧立刻判突破），玩家会看到它明明退了、照样跳杀。
            LastChanceTimer = 0f;
            NoteBlocked();
            Retreat();
        }

        /// <summary>
        /// 进攻失败（被门挡回来）：**不直接回出生地**，而是随机传送 + 随机游走。
        ///
        /// 原版观感：邦尼被关门挡一次之后不会老实回起点，它会换个房间继续晃。
        /// 这让玩家觉得它一直在楼里，而不是每挡一次就重置进度、可以精准预测。
        ///
        /// 具体做法：
        ///   1. 随机跳到路径上的某个机位（不是第 0 格 = 不是出生地）
        ///   2. 记下「还要晃几步」（按活动度 1~5 步）
        ///   3. 游走期间每步都在路径上随机换位，不往门口推进
        ///   4. 晃完了才恢复正常推进
        /// </summary>
        public override void Retreat()
        {
            base.Retreat();
            DoorTimer = 0f;
            LastChanceTimer = 0f;
            _doorWasClosed = false;
            _knockedThisVisit = false;   // 下次再来可以再敲一次

            // 只有配了机位路径的（袋鼠 / 牛来 / 奶龙进攻态）才谈得上「游走」。
            // 没路径的老三档实现只能回 Stage 0，保持原样。
            if (!HasCameraPath)
            {
                WanderStepsLeft = 0;
                return;
            }

            // 随机跳到路径上的某个格子。
            // ⚠️ **避开第 0 格**（= 出生地 / 路径起点），
            // 用户明确要求「出生地不会被返回」。
            int slots = _cameraPath.Length > 1 ? _cameraPath.Length : 1;
            if (slots > 1)
            {
                Stage = 1 + Rng.Range(0, slots - 1);   // 1 .. length-1
            }
            else
            {
                Stage = 0;
            }

            MoveTimer = 0f;
            WanderStepsLeft = WanderStepsForActivity();
        }
    }
}
