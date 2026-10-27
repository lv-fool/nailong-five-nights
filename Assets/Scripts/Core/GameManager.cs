using System;
using System.Collections.Generic;
using NailongFNAF.Gameplay;
using UnityEngine;

namespace NailongFNAF.Core
{
    /// <summary>
    /// 全局状态：电量、时间，以及门 / 灯 / 监控的开关。
    ///
    /// 场景里只需要一个，挂在一个空物体上。UI 通过它读写状态，
    /// 自己不做任何游戏逻辑判断。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>重开一夜时触发（R 键、换夜都走这里）。敌人 AI 订阅它来复位。</summary>
        public event Action Restarted;

        [Header("节奏")]
        [Tooltip("每游戏小时对应的真实秒数。85 秒 → 一整夜约 8 分 30 秒。")]
        [SerializeField] private float _secondsPerHour = NightClock.DefaultSecondsPerHour;

        /// <summary>电量系统。</summary>
        public PowerSystem Power { get; private set; }

        /// <summary>夜晚时钟。</summary>
        public NightClock Clock { get; private set; }

        /// <summary>
        /// 是否处于永夜模式：撑不到天亮，敌人一直来；玩家靠「行动换电」续命。
        /// 由菜单在开新一局之前设置，<see cref="Restart"/> 会照着它建时钟。
        /// </summary>
        public bool EndlessMode { get; private set; }

        /// <summary>永夜模式的换电账本（普通模式下为 null）。</summary>
        public EndlessRecharge Recharge { get; private set; }

        /// <summary>永夜模式的得分（不是永夜时为 null）。</summary>
        public EndlessScore Score { get; private set; }

        /// <summary>贝利亚之夜（12 小时肉鸽）模式下为 true。和 <see cref="EndlessMode"/> 互斥。</summary>
        public bool BelialMode { get; private set; }

        /// <summary>这一局的支援账本（12 小时、每两小时一次「来自奶包的支援」）。非贝利亚之夜为 null。</summary>
        public BelialNight Belial { get; private set; }

        /// <summary>
        /// 贝利亚本人（老板 2026-10-26 定）：第二阶段起会站到左右某一扇门外，
        /// 挡他的方式是**开门 + 用手电筒照**，照够五下他就走。
        /// </summary>
        public BelialEncounter BelialEncounter { get; private set; }

        /// <summary>敌人还要被冻住多少秒（「褪黑素」整点冻结给的值）。</summary>
        public float EnemyFreezeLeft
        {
            get { return _enemyFreezeLeft; }
        }

        /// <summary>敌人推进速度倍率（「多巴胺」让它变慢）；1 = 正常。</summary>
        public float EnemySpeedScale { get; private set; }

        /// <summary>门口读条额外宽限的秒数（「去甲肾上腺素」加判定窗口 + 「喹硫平」让人多站一会儿）。</summary>
        public float DoorGraceBonus
        {
            get
            {
                if (!BelialMode || Belial == null)
                {
                    return 0f;
                }

                return Belial.TotalOf(SupportEffect.InputGrace)
                    + Belial.TotalOf(SupportEffect.DoorLinger)
                    - Belial.AttackWindowPenalty;
            }
        }

        /// <summary>奶龙偷到电之后要静止多少秒（「一二三木头人」）。</summary>
        public float NailongStealFreeze
        {
            get
            {
                if (!BelialMode || Belial == null)
                {
                    return 0f;
                }

                return Belial.TotalOf(SupportEffect.NailongSilence);
            }
        }

        /// <summary>最近一次行动补给拿到多少电量（百分点），给 HUD 弹「+8%」用。</summary>
        public float LastGain { get; private set; }

        /// <summary>距离 <see cref="LastGain"/> 过去了多久（秒），HUD 用它决定要不要淡出。</summary>
        public float LastGainAge { get; private set; }

        /// <summary>已经同步进 <see cref="Belial"/> 的小时数（防止重复推进）。</summary>
        private int _belialHours;

        /// <summary>敌人被冻结的剩余秒数（整点冻结用）。</summary>
        private float _enemyFreezeLeft;

        /// <summary>已经看到贝利亚来过几次（用来判「新的一次」）。</summary>
        private int _belialVisitsSeen;

        /// <summary>「致命判定改成擦过」还剩几次（贝利亚之夜的支援给的）。</summary>
        private int _grazeLeft;

        /// <summary>「备用电源」还剩几次 —— 电量归零时被强行拉回来。</summary>
        private int _rescueLeft;

        /// <summary>备用电源拉回来的电量（百分比）。</summary>
        private float _rescuePercent;

        /// <summary>
        /// 是否暂停中（只有永夜模式能暂停）。暂停靠 <see cref="Time.timeScale"/> = 0
        /// 把时间、电量、敌人一起冻住，再停掉 <see cref="AudioListener"/>，
        /// 所以恢复时那一帧不会跳时间。
        /// </summary>
        public bool IsPaused { get; private set; }

        public bool LeftDoorClosed { get; private set; }
        public bool RightDoorClosed { get; private set; }
        public bool LeftLightOn { get; private set; }
        public bool RightLightOn { get; private set; }

        /// <summary>
        /// 监控是否升起。**不是简单的一个 bool**：奶包会「抢监控」几秒，
        /// 那几秒里不管玩家怎么按，监控都得是升起的（见 <see cref="_naibaoMonitorTimer"/>）。
        /// </summary>
        public bool MonitorUp
        {
            get { return _naibaoMonitorTimer > 0f || _monitorPlayerIntent; }
        }

        /// <summary>玩家自己想要的监控状态（被奶包抢走时也记着，抢完还给他）。</summary>
        private bool _monitorPlayerIntent;

        /// <summary>奶包抢监控的剩余秒数。</summary>
        private float _naibaoMonitorTimer;

        /// <summary>奶包偷偷加一档用电的剩余秒数。</summary>
        private float _naibaoUsageTimer;

        /// <summary>「这口锅是不是奶包的」—— 她动过设备、玩家又在窗口内死掉，就算她头上。</summary>
        private readonly NaibaoApology _naibaoApology = new NaibaoApology();

        /// <summary>
        /// 手电筒是否正亮着 —— **按住才亮，松手就灭**（不是点击切换）。
        /// 用来照正门外的噜噜，见 <see cref="SetFlashlight"/>。
        /// </summary>
        public bool FlashlightOn { get; private set; }

        /// <summary>玩家当前看向哪边，由视角控制脚本每帧写入。</summary>
        public ViewSide CurrentView { get; private set; }

        /// <summary>监控当前看着第几个机位（0 起）。</summary>
        public int CurrentCameraIndex { get; private set; }

        /// <summary>
        /// 这一夜的操作账本（关门次数、看过哪些机位、厨房待了多久…），
        /// 给隐藏成就判定用。每 <see cref="Restart"/> 一份新的。
        /// </summary>
        public NightStats Stats { get; private set; }

        /// <summary>谷维素：不开灯也能看清门口有没有人。</summary>
        public bool DoorSightActive
        {
            get { return BelialMode && Belial != null && Belial.DoorSightActive; }
        }

        /// <summary>咖啡因：切机位残影秒数。</summary>
        public float CameraGhostSeconds
        {
            get { return BelialMode && Belial != null ? Belial.CameraGhostSeconds : 0f; }
        }

        /// <summary>一通电话：阶段开头报准确机位。</summary>
        public bool StageSpotActive
        {
            get { return BelialMode && Belial != null && Belial.StageSpotActive; }
        }

        /// <summary>复诊：监控里标弱点。</summary>
        public bool WeakPointActive
        {
            get { return BelialMode && Belial != null && Belial.WeakPointActive; }
        }

        /// <summary>查房层级（0 / 1 / 2）。</summary>
        public int RevealLevel
        {
            get { return BelialMode && Belial != null ? Belial.RevealLevel : 0; }
        }

        /// <summary>熬夜：手电照得远（倍数）。</summary>
        public float FlashlightRangeScale
        {
            get { return BelialMode && Belial != null ? Belial.FlashlightRangeScale : 1f; }
        }

        /// <summary>时间流速倍数（闹钟 / 失眠）。</summary>
        public float TimeScaleFactor
        {
            get { return BelialMode && Belial != null ? Belial.TimeScaleFactor : 1f; }
        }

        /// <summary>本局被「今天出门了」空出来的机位（敌人不会进这几间）。</summary>
        public int[] SafeCameras { get; private set; }

        /// <summary>机位总数 —— 必须与 PrototypeScene.CameraNames 的长度一致。</summary>
        public const int CameraSlotCount = 11;

        /// <summary>
        /// 奶包。第 1 夜只说话引导，之后每夜手欠（加耗电 / 抢监控 / 关灯 / 嘲讽）。
        /// 菜单在开局前调 <see cref="SetNaibao"/> 塞进来；永夜模式暂时不接她，为 null。
        /// </summary>
        public NaibaoDirector Naibao { get; private set; }

        /// <summary>奶包最近一次出场 —— 表现层拿它决定显示哪张立绘、说哪句话。</summary>
        public NaibaoCue LastNaibaoCue { get; private set; }

        /// <summary>距 <see cref="LastNaibaoCue"/> 过了多少秒，表现层用它决定什么时候收起来。</summary>
        public float LastNaibaoCueAge { get; private set; }

        /// <summary>出场流水号：每来一次 +1，UI 靠它判断「这是新的一句」。</summary>
        public int NaibaoCueSerial { get; private set; }

        /// <summary>
        /// 玩家死的那一刻就钉住的道歉（死因算她的才有值）。
        /// 钉在 <see cref="MarkCaught"/> 上而不是现算：人死了之后计时不再走，
        /// 但 UI 也不能指望「窗口还在」这种巧合。
        /// </summary>
        public string NaibaoApologyLine { get; private set; }

        /// <summary>那句道歉的配音 id（Assets/Resources/Voice/apology_*.mp3）；不背锅时为空串。</summary>
        public string NaibaoApologyLineId { get; private set; }

        /// <summary>当前这一通电话（没在通话时为 null）。</summary>
        public NaibaoCall NaibaoCall { get; private set; }

        /// <summary>是不是正跟她通着话。</summary>
        public bool NaibaoTalking
        {
            get { return NaibaoCall != null && !NaibaoCall.Ended; }
        }

        /// <summary>通话流水号：接通、翻页、挂断都会 +1，UI 靠它判断要不要重画。</summary>
        public int NaibaoCallSerial { get; private set; }

        /// <summary>这一夜接过几次电话（决定她给不给新词）。</summary>
        public int NaibaoCallCount { get; private set; }

        /// <summary>是否已经撑到天亮。</summary>
        public bool HasSurvived
        {
            get
            {
                if (Clock == null)
                {
                    return false;
                }

                if (BelialMode)
                {
                    return Belial != null && Belial.IsComplete;
                }

                return Clock.IsNightComplete;
            }
        }

        /// <summary>是否已经断电。</summary>
        public bool HasPowerOut
        {
            get { return Power != null && Power.IsDepleted; }
        }

        /// <summary>是否已经被敌人抓到（走跳杀流程，时间与电量停止推进）。</summary>
        public bool HasBeenCaught { get; private set; }

        /// <summary>
        /// 敌人从哪一侧突破了防御。Null = 还没突破。
        /// 突破后该侧的门和灯**立刻失效**（照 FNAF 1 原作：玩家已经来不及反应了）。
        /// </summary>
        public Side? BreachedSide { get; private set; }

        /// <summary>被敌人抓到，冻结本夜。</summary>
        public void MarkCaught()
        {
            // 贝利亚之夜的支援：这一下能不能躲过去（「摔了一跤」扑空 / 「氯硝西泮」擦过）
            if (_grazeLeft > 0)
            {
                _grazeLeft--;
                return;
            }

            if (Belial != null && Belial.ConsumeMiss())
            {
                return;
            }

            HasBeenCaught = true;

            // 死因归到奶包头上？那就把她这句道歉钉住，死亡面板会显示
            NaibaoApologyLine = _naibaoApology.CulpableLine();
            NaibaoApologyLineId = _naibaoApology.CulpableLineId();

            // 结算彩蛋要看「死在第几个小时」——抓到的这一帧就把时刻钉住
            if (Stats != null && Clock != null)
            {
                Stats.MarkEnded(Clock.ElapsedSeconds, Clock.SecondsPerHour);
            }
        }

        /// <summary>
        /// 挡住一次怪物（永夜计分用）。非永夜模式直接忽略 —— 故事模式没有分。
        /// </summary>
        public void NoteBlocked(int count = 1)
        {
            if (Score != null)
            {
                Score.AddBlocks(count);
            }
        }

        /// <summary>
        /// 调试用：把游戏时间往前推一段（F1 面板上的「+2 分钟 / +2 小时」）。
        ///
        /// 按 1 秒一步推进，这样整点结算（永夜补电、贝利亚发卡）都会正常触发；
        /// 一次 Tick(7200) 会把中间这些整点全跳过去。
        /// </summary>
        public void DebugAdvanceGameSeconds(float seconds)
        {
            if (Clock == null || seconds <= 0f)
            {
                return;
            }

            int steps = Mathf.CeilToInt(seconds);
            float step = seconds / steps;

            for (int i = 0; i < steps; i++)
            {
                Clock.Tick(step);

                if (Score != null)
                {
                    Score.Tick(step);
                }

                if (EndlessMode && Recharge != null)
                {
                    float grant = Recharge.Tick(step);

                    if (grant > 0f && Power != null)
                    {
                        float actual = Power.Grant(grant);
                        Recharge.NoteGranted(actual);
                        LastGain = actual;
                        LastGainAge = 0f;
                    }
                }
            }

            if (BelialMode && Belial != null)
            {
                // 贝利亚模式没走 endless 账本，这里只要把小时对齐
                while (Belial.Hours < Clock.CurrentHour)
                {
                    if (!Belial.AdvanceHours(1))
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 记录「哪一侧被突破了」。照 FNAF 1：突破的瞬间该侧门与灯就废了，
        /// 玩家按了也没用 —— 只是还没跳杀而已（要等监控放下才动手）。
        /// </summary>
        public void MarkBreached(Side side)
        {
            BreachedSide = side;
        }

        /// <summary>该侧的门还能不能操作（被突破的那侧就废了）。</summary>
        public bool IsSideDisabled(Side side)
        {
            return BreachedSide.HasValue && BreachedSide.Value == side;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Restart();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;

                // 暂停状态下退出，别把 timeScale = 0 留给下一个人（编辑器里尤其明显）
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }

        private void Update()
        {
            // 编辑器里 Play 期间脚本一重编译就会 domain reload：静态字段和这些
            // 没序列化的属性全被清空，可对象还挂在场景里、Update 照跑。
            // 这时候 Clock 是空的 —— 直接跳过，别刷一屏空引用盖住真问题。
            if (Clock == null || Power == null)
            {
                return;
            }

            if (Clock.IsNightComplete || HasBeenCaught)
            {
                return;
            }

            float dt = Time.deltaTime;
            LastGainAge += dt;
            LastNaibaoCueAge += dt;
            _naibaoApology.Tick(dt);

            // 奶包：引导期到点说话，之后到点手欠。她只在玩家还能动的时候闹
            TickNaibao(dt);

            // 闹钟 / 失眠：时间流速改一改（敌人和电量照常按真实时间走）
            Clock.Tick(dt * TimeScaleFactor);

            // 永夜得分：和时钟一样按时间累积
            if (Score != null)
            {
                Score.Tick(dt * TimeScaleFactor);
            }

            TickBelial();
            TickBelialEncounter(dt);

            // 彩蛋账本：厨房机位停留计时 + 天亮那一刻的结束时刻
            if (Stats != null)
            {
                Stats.Tick(dt, MonitorUp, CurrentCameraIndex);

                if (Clock.IsNightComplete && !Stats.HasEnded)
                {
                    Stats.MarkEnded(Clock.ElapsedSeconds, Clock.SecondsPerHour);
                }
            }

            // 断电、天亮、被抓到之后手电筒也要灭（按住状态可能来不及松手）
            if (!CanOperate() && !FreeFlashlightActive)
            {
                FlashlightOn = false;
            }

            // 「备用电源」：归零的这一瞬间把人拉回来一次（贝利亚之夜的支援）
            if (Power.IsDepleted && _rescueLeft > 0)
            {
                _rescueLeft--;
                float before = Power.Level;
                Power.SetLevel(_rescuePercent);
                LastGain = Power.Level - before;
                LastGainAge = 0f;
            }

            // 断电的一瞬间所有设备一起熄火 —— 门弹开、灯灭、监控掉线。
            // 玩家也再打不开（CanOperate 会挡住），只剩黑暗和奶龙的闪脸。
            if (Power.IsDepleted)
            {
                ReleaseAllDevices();
            }

            SyncBelialModifiers();

            Power.SetUsageLevel(CountUsageLevel());
            Power.Tick(dt);

            // 永夜模式：固定周期结算补给。到点才补，跟手上正在做什么无关；
            // 一个周期里做了多少有效操作，只决定这次补多少（见 EndlessRecharge）。
            if (EndlessMode && Recharge != null && CanOperate())
            {
                float grant = Recharge.Tick(dt);
                if (grant > 0f)
                {
                    float actual = Power.Grant(grant);
                    Recharge.NoteGranted(actual);
                    LastGain = actual;
                    LastGainAge = 0f;
                }
            }
        }

        /// <summary>断电后设备全部停摆。</summary>
        private void ReleaseAllDevices()
        {
            LeftDoorClosed = false;
            RightDoorClosed = false;
            LeftLightOn = false;
            RightLightOn = false;
            _monitorPlayerIntent = false;
            _naibaoMonitorTimer = 0f;
            _naibaoUsageTimer = 0f;
            FlashlightOn = false;
        }

        /// <summary>
        /// 推进奶包：先扣计时器，再让她决定要不要出场，最后把出场的效果落到设备上。
        ///
        /// 计时器每帧都扣（包括她不在、玩家不能动的时候），否则断电后
        /// 「偷加的一档耗电」会一直挂着。
        /// </summary>
        private void TickNaibao(float dt)
        {
            if (_naibaoUsageTimer > 0f)
            {
                _naibaoUsageTimer -= dt;
            }

            if (_naibaoMonitorTimer > 0f)
            {
                _naibaoMonitorTimer -= dt;
            }

            if (Naibao == null || !CanOperate())
            {
                // 断电或被抓：电话自然断线，别让「通话中」的用电格子挂在身上
                EndNaibaoCall();
                return;
            }

            Naibao.Tick(dt);

            NaibaoCue cue;
            while (Naibao.TryTakeCue(out cue))
            {
                ApplyNaibaoCue(cue);
            }
        }

        /// <summary>把奶包这一次出场落到实际状态上，并留给表现层去演。</summary>
        private void ApplyNaibaoCue(NaibaoCue cue)
        {
            switch (cue.Action)
            {
                case NaibaoAction.UsageSpike:
                    _naibaoUsageTimer = cue.Seconds;
                    break;

                case NaibaoAction.MonitorGrab:
                    _naibaoMonitorTimer = cue.Seconds;
                    break;

                case NaibaoAction.LightOff:
                    if (RightLightOn)
                    {
                        RightLightOn = false;
                    }
                    else if (LeftLightOn)
                    {
                        LeftLightOn = false;
                    }
                    else
                    {
                        // 手欠落空：她伸手去关灯，结果你根本没开
                        cue.Line = "我想关你一盏灯来着……你压根没开。";
                    }
                    break;
            }

            LastNaibaoCue = cue;
            LastNaibaoCueAge = 0f;
            NaibaoCueSerial++;

            // 归因账本：她动了设备就记一笔，玩家在窗口内死掉就算她的锅（见 NaibaoApology）
            _naibaoApology.Note(cue.Action);
        }

        /// <summary>
        /// 「今天出门了」开局空出来的机位。不重复，且**不挑厨房**
        /// —— 厨房本来就只有声音，把它标成「安全」等于什么也没说。
        /// </summary>
        private static int[] PickSafeCameras(int count)
        {
            if (count <= 0)
            {
                return null;
            }

            int slots = CameraSlotCount;
            var picked = new List<int>(count);
            var rng = new System.Random();

            int guard = 0;

            while (picked.Count < count && guard < 200)
            {
                guard++;
                int candidate = rng.Next(0, slots);

                if (picked.Contains(candidate))
                {
                    continue;
                }

                // 厨房（9）画面上什么都没有，不选
                if (candidate == 9)
                {
                    continue;
                }

                picked.Add(candidate);
            }

            return picked.ToArray();
        }

        /// <summary>重开一夜。</summary>
        public void Restart()
        {
            Power = new PowerSystem(PowerSystem.InitialLevel);
            Clock = new NightClock(_secondsPerHour, EndlessMode || BelialMode);
            Recharge = EndlessMode ? new EndlessRecharge() : null;
            Score = EndlessMode ? new EndlessScore(_secondsPerHour) : null;
            Belial = BelialMode ? new BelialNight() : null;
            BelialEncounter = BelialMode ? new BelialEncounter(new SystemRandomSource()) : null;
            _belialHours = 0;
            _belialVisitsSeen = 0;
            _enemyFreezeLeft = 0f;
            EnemySpeedScale = 1f;
            _grazeLeft = 0;
            _rescueLeft = 0;
            _rescuePercent = 0f;

            if (Belial != null)
            {
                // 开局白送三张一样的「拉钩」—— 这不是选择，是剧情。
                Belial.Begin();
            }

            SafeCameras = Belial != null && Belial.SafeSpotCount > 0
                ? PickSafeCameras(Belial.SafeSpotCount)
                : null;
            Stats = new NightStats();
            LastGain = 0f;
            LastGainAge = 0f;
            SetPaused(false);

            // 奶包跟着这一局重来：菜单会在 Restart 之后重新塞一个，调试重开（R）走的是这个分支。
            if (Naibao != null)
            {
                Naibao.Reset();
            }
            LastNaibaoCueAge = 0f;
            _naibaoApology.Clear();
            NaibaoApologyLine = null;
            NaibaoApologyLineId = null;
            NaibaoCall = null;
            NaibaoCallCount = 0;

            LeftDoorClosed = false;
            RightDoorClosed = false;
            LeftLightOn = false;
            RightLightOn = false;
            _monitorPlayerIntent = false;
            _naibaoMonitorTimer = 0f;
            _naibaoUsageTimer = 0f;
            FlashlightOn = false;

            CurrentView = ViewSide.Front;
            CurrentCameraIndex = 0;
            HasBeenCaught = false;
            BreachedSide = null;

            if (Restarted != null)
            {
                Restarted();
            }
        }

        /// <summary>
        /// 设置永夜模式（下一局 <see cref="Restart"/> 生效）。菜单选「永夜模式」时传 true。
        /// </summary>
        public void SetEndless(bool endless)
        {
            EndlessMode = endless;

            if (endless)
            {
                BelialMode = false;
            }
        }

        /// <summary>
        /// 设置贝利亚之夜（下一局 <see cref="Restart"/> 生效）。
        /// 和永夜模式互斥 —— 它自己就带「撑满 12 小时」的计时。
        /// </summary>
        public void SetBelial(bool belial)
        {
            BelialMode = belial;

            if (belial)
            {
                EndlessMode = false;
            }
        }

        /// <summary>手电不耗电、断电能开的那条支援有没有拿到。</summary>
        private bool FreeFlashlightActive
        {
            get { return Belial != null && Belial.Has(SupportEffect.FreeFlashlight); }
        }

        /// <summary>
        /// 选完一张支援之后立刻生效的那部分（改电量上限、一次性的补电与保险次数都算「拿了就改」）。
        /// 靠使用等级 / 掉电倍率起作用的那些不用调这里，每帧会自己算。
        /// </summary>
        public void ApplySupport(SupportDef def)
        {
            if (def == null || Power == null)
            {
                return;
            }

            switch (def.Effect)
            {
                case SupportEffect.PowerCapBoost:
                    Power.MultiplyMaxLevel(def.Value);
                    Power.Grant(def.Value2);
                    break;

                case SupportEffect.RiskNoPower:
                    Power.Grant(def.Value);
                    Power.MultiplyMaxLevel(1f + def.Value2);
                    break;

                case SupportEffect.Placebo:
                    Power.Grant(def.Value);
                    break;

                case SupportEffect.SafeSpots:
                    // 这张是「开局空出两间」，可它往往是第 2 小时才抽到 ——
                    // 所以在这里补挑，而不是在 Restart 里一次定死。
                    SafeCameras = PickSafeCameras(Belial != null ? Belial.SafeSpotCount : 0);
                    break;

                case SupportEffect.PowerRescue:
                    _rescueLeft += Mathf.Max(1, (int)(def.Value2 + 0.5f));
                    _rescuePercent = def.Value;
                    break;

                case SupportEffect.LethalToGraze:
                case SupportEffect.LethalToWound:
                    _grazeLeft += Mathf.Max(1, (int)(def.Value + 0.5f));
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// 每帧按手上已有的支援重算掉电倍率。「血清素」压整体速度，
        /// 「文拉法辛」只在门/灯开着时打折，「安非他酮」只对手电打折。
        /// </summary>
        private void SyncBelialModifiers()
        {
            if (Power == null)
            {
                return;
            }

            if (!BelialMode || Belial == null)
            {
                Power.DrainScale = 1f;
                return;
            }

            float scale = 1f - ClampReduction(Belial.TotalOf(SupportEffect.DrainRateDown));

            if (LeftDoorClosed || RightDoorClosed || LeftLightOn || RightLightOn)
            {
                scale *= 1f - ClampReduction(Belial.TotalOf(SupportEffect.DoorLightDrainDown));
            }

            if (FlashlightOn)
            {
                scale *= 1f - ClampReduction(Belial.TotalOf(SupportEffect.FlashlightDrainDown));
            }

            Power.DrainScale = scale < 0f ? 0f : scale;

            // 「多巴胺」：敌人移动效率下降 → 推进间隔变长
            float slowdown = ClampReduction(Belial.TotalOf(SupportEffect.EnemySpeedDown));
            EnemySpeedScale = 1f - slowdown;
        }

        /// <summary>
        /// 贝利亚本人：第 6 小时起会站到某一边门外。
        /// 挡他的方式跟其他敌人刚好相反 —— **把门打开、用手电筒照**，
        /// 照一下他就白一下（白贝），照够五下他走；站着不理他九秒就动手。
        /// </summary>
        private void TickBelialEncounter(float dt)
        {
            if (_enemyFreezeLeft > 0f)
            {
                _enemyFreezeLeft -= dt;

                if (_enemyFreezeLeft < 0f)
                {
                    _enemyFreezeLeft = 0f;
                }
            }

            if (!BelialMode || BelialEncounter == null || Stats == null)
            {
                return;
            }

            bool active = Belial != null && Belial.BelialActive;
            BelialEncounter.Tick(dt, active);

            if (BelialEncounter.Visits > _belialVisitsSeen)
            {
                _belialVisitsSeen = BelialEncounter.Visits;
                Stats.NoteBelialMeeting();
            }

            if (BelialEncounter.ConsumeKill())
            {
                Stats.NoteBelialDeath();
                MarkBreached(BelialEncounter.Side == BelialSide.Left ? Side.Left : Side.Right);
                MarkCaught();
                return;
            }

            if (!BelialEncounter.AtDoor)
            {
                return;
            }

            bool left = BelialEncounter.Side == BelialSide.Left;
            bool doorOpen = left ? !LeftDoorClosed : !RightDoorClosed;
            bool facing = CurrentView == (left ? ViewSide.Left : ViewSide.Right);
            bool shining = FlashlightOn && facing && doorOpen;

            if (BelialEncounter.TryFlash(doorOpen, shining))
            {
                Stats.NoteBelialFlash();
            }
        }

        /// <summary>折扣最多打到九成 —— 别让几条支援叠出「完全不掉电」。</summary>
        private static float ClampReduction(float reduction)
        {
            if (reduction < 0f)
            {
                return 0f;
            }

            return reduction > 0.9f ? 0.9f : reduction;
        }

        /// <summary>
        /// 把时钟走到的小时数同步给支援账本：每满两小时结算一次电量返还，并摆出新的三张卡。
        /// 选卡由表现层负责（<see cref="BelialNight.AwaitingChoice"/> 为真时弹面板）。
        /// </summary>
        private void TickBelial()
        {
            if (!BelialMode || Belial == null || Clock == null)
            {
                return;
            }

            int hours = Clock.HoursSurvived;

            if (hours <= _belialHours)
            {
                return;
            }

            Belial.AdvanceHours(hours - _belialHours);
            _belialHours = hours;

            // 「褪黑素」：每个整点过后敌人停一会儿，正好用来换一口气
            float freeze = Belial.TotalOf(SupportEffect.HourlyFreeze);

            if (freeze > 0f && freeze > _enemyFreezeLeft)
            {
                _enemyFreezeLeft = freeze;
            }

            // 「氟西汀」：每过一个整点回一点电
            float regen = Belial.TotalOf(SupportEffect.HourlyRegen);

            if (regen > 0f)
            {
                Power.Grant(regen);
            }

            float refund = Belial.TryOffer();

            if (refund > 0f)
            {
                Power.Grant(refund);
                LastGain = refund;
                LastGainAge = 0f;
            }
        }

        /// <summary>
        /// 安插奶包（下一局生效）。<paramref name="director"/> 传 null 表示这局没有她。
        /// **要在 <see cref="Restart"/> 之后调**，因为 Restart 会清掉上一局的状态。
        /// </summary>
        public void SetNaibao(NaibaoDirector director)
        {
            Naibao = director;
            LastNaibaoCue = default(NaibaoCue);
            LastNaibaoCueAge = 0f;
            _naibaoApology.Clear();
            NaibaoApologyLine = null;
            NaibaoApologyLineId = null;
            NaibaoCall = null;
            NaibaoCallCount = 0;
        }

        // ---------------------------------------------------------------
        // 桌上那部手机：跟她通话（通话期间她不出手，电表多走一格）
        // ---------------------------------------------------------------

        /// <summary>
        /// 接通电话。返回 false 表示这一下没打通（没她 / 已断电 / 已被抓 / 已经在通话）。
        /// <paramref name="night"/> 是第几夜（0 起），决定她这次给什么词。
        /// </summary>
        /// <param name="custom">
        /// 这一局是不是**自定义夜**。自定义夜没有「第几夜」的概念，
        /// <paramref name="night"/> 保留的是上一次进入的夜晚 —— 照它选档会让
        /// 台词随机落到 early / middle / late，玩家听着莫名其妙。
        /// 所以这里显式传进来，让台词走自定义夜那一组。
        /// </param>
        public bool StartNaibaoCall(int night, bool custom = false)
        {
            if (Naibao == null || NaibaoCall != null || !CanOperate())
            {
                return false;
            }

            NaibaoCall call = new NaibaoCall(
                NaibaoPhoneScript.ForCall(night, Naibao.IsGuide, NaibaoCallCount, custom, !EndlessMode && !custom));

            if (call.Count <= 0)
            {
                return false;
            }

            NaibaoCall = call;
            NaibaoCallCount++;
            NaibaoCallSerial++;

            // 接通就让她撒手：抢着的监控立刻还回去（玩家的意图本来就没被改），
            // 挂在身上的「偷加一档」也一并熄掉
            _naibaoMonitorTimer = 0f;
            _naibaoUsageTimer = 0f;
            Naibao.BeginTalk();

            return true;
        }

        /// <summary>念下一句。念完了就自动挂断。没在通话返回 false。</summary>
        public bool AdvanceNaibaoCall()
        {
            if (NaibaoCall == null)
            {
                return false;
            }

            NaibaoCallSerial++;
            if (!NaibaoCall.Advance())
            {
                EndNaibaoCall();
            }

            return true;
        }

        /// <summary>挂断。</summary>
        public void EndNaibaoCall()
        {
            if (NaibaoCall == null)
            {
                return;
            }

            NaibaoCall = null;
            NaibaoCallSerial++;
            if (Naibao != null)
            {
                Naibao.EndTalk();
            }
        }

        // ---------------------------------------------------------------
        // 暂停 / 存档（只服务永夜模式）
        // ---------------------------------------------------------------

        /// <summary>
        /// 暂停或继续。**只有永夜模式能暂停**，故事模式调了也不会冻住 ——
        /// 免得玩家在夜里按 Esc 把时钟停住刷时间。
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (paused && !EndlessMode && !BelialMode)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
        }

        /// <summary>把当前这一局的进度打包成存档；不是永夜模式就返回 null。</summary>
        public EternalNightSave CaptureSave()
        {
            if (!EndlessMode || Clock == null || Power == null || Recharge == null)
            {
                return null;
            }

            return new EternalNightSave
            {
                ElapsedSeconds = Clock.ElapsedSeconds,
                Power = Power.Level,
                Settlements = Recharge.Settlements,
                Credits = Recharge.Credits,
                Noise = Recharge.Noise,
                SecondsIntoPeriod = Recharge.SecondsIntoPeriod,
                TotalGranted = Recharge.TotalGranted,
                ActionsRegistered = Recharge.ActionsRegistered,
                Hour = Clock.CurrentHour,
            };
        }

        /// <summary>
        /// 读档：把时间、电量、账本一起拨回存档那一刻。
        /// 必须在 <see cref="SetEndless"/>(true) + <see cref="Restart"/> 之后调，
        /// 因为它要写进 Restart 建好的那几个对象里。敌人不恢复状态，从当前难度重新开始。
        /// </summary>
        public bool LoadSave(EternalNightSave save)
        {
            if (save == null || !EndlessMode || Clock == null || Power == null || Recharge == null)
            {
                return false;
            }

            Clock.RestoreElapsed(save.ElapsedSeconds);
            Power.SetLevel(save.Power);
            Recharge.Restore(save.Credits, save.Noise, save.Settlements,
                save.SecondsIntoPeriod, save.TotalGranted, save.ActionsRegistered);

            LastGain = 0f;
            LastGainAge = 0f;
            SetPaused(false);
            return true;
        }


        /// <summary>视角控制脚本每帧调用，写入玩家当前看向哪边。</summary>
        public void SetView(ViewSide view)
        {
            CurrentView = view;
        }

        /// <summary>切换到指定监控机位（0 起，超出范围忽略）。</summary>
        public void SetCamera(int index)
        {
            if (index < 0 || index == CurrentCameraIndex)
            {
                return;
            }

            CurrentCameraIndex = index;
            NoteAction(PlayerAction.Camera);

            if (Stats != null)
            {
                Stats.NoteCamera(index);
            }
        }

        /// <summary>按机位数量循环切换监控（供「下一个机位」按钮用）。</summary>
        public void CycleCamera(int cameraCount)
        {
            if (cameraCount <= 1)
            {
                CurrentCameraIndex = 0;
                return;
            }

            CurrentCameraIndex = (CurrentCameraIndex + 1) % cameraCount;
            NoteAction(PlayerAction.Camera);

            if (Stats != null)
            {
                Stats.NoteCamera(CurrentCameraIndex);
            }
        }

        // ---------------- 操作入口（由 UI 按钮调用）----------------

        public void ToggleLeftDoor()
        {
            if (CanOperate() && !IsSideDisabled(Side.Left))
            {
                LeftDoorClosed = !LeftDoorClosed;
                NoteAction(PlayerAction.Door);

                if (Stats != null)
                {
                    Stats.NoteDoorToggle();
                }
            }
        }

        public void ToggleRightDoor()
        {
            if (CanOperate() && !IsSideDisabled(Side.Right))
            {
                RightDoorClosed = !RightDoorClosed;
                NoteAction(PlayerAction.Door);

                if (Stats != null)
                {
                    Stats.NoteDoorToggle();
                }
            }
        }

        public void ToggleLeftLight()
        {
            if (CanOperate() && !IsSideDisabled(Side.Left))
            {
                LeftLightOn = !LeftLightOn;
                NoteAction(PlayerAction.Light);

                if (Stats != null)
                {
                    Stats.NoteLightToggle();
                }
            }
        }

        public void ToggleRightLight()
        {
            if (CanOperate() && !IsSideDisabled(Side.Right))
            {
                RightLightOn = !RightLightOn;
                NoteAction(PlayerAction.Light);

                if (Stats != null)
                {
                    Stats.NoteLightToggle();
                }
            }
        }

        public void ToggleMonitor()
        {
            if (CanOperate())
            {
                // 按的是「玩家想要的状态」，这样奶包抢监控的那两秒里也能先把意图翻过来
                _monitorPlayerIntent = !MonitorUp;
                NoteAction(PlayerAction.Camera);

                if (Stats != null)
                {
                    Stats.NoteMonitorToggle();

                    // 升起来的这一眼就算看过当前机位
                    if (MonitorUp)
                    {
                        Stats.NoteCamera(CurrentCameraIndex);
                    }
                }
            }
        }

        /// <summary>
        /// 手电筒按住 / 松开 —— 由 UI 的按下与抬起事件直接调，不做切换。
        /// 松手（<paramref name="on"/> = false）永远生效，免得断电时卡在亮着。
        /// </summary>
        public void SetFlashlight(bool on)
        {
            bool wasOn = FlashlightOn;
            FlashlightOn = on && (CanOperate() || FreeFlashlightActive);

            if (!wasOn && FlashlightOn)
            {
                NoteAction(PlayerAction.Flashlight);
            }
        }

        /// <summary>
        /// 永夜模式的记账：登记一次有效操作 —— 攒账 + 加动静（动静会抬难度）。
        /// **补电不在这里发生**：补给按固定周期结算，见 <see cref="Update"/>。
        /// 普通模式下整个方法什么都不做（<see cref="Recharge"/> 为 null）。
        /// </summary>
        private void NoteAction(PlayerAction action)
        {
            if (!EndlessMode || Recharge == null || Power == null || !CanOperate())
            {
                return;
            }

            Recharge.RegisterAction(action);
        }

        /// <summary>断电、天亮或被抓到之后，玩家不能再操作设备。</summary>
        private bool CanOperate()
        {
            return Operable;
        }

        /// <summary>玩家这一刻还能不能动设备（给表现层用：断电 / 天亮 / 被抓之后就不行了）。</summary>
        public bool Operable
        {
            get { return !HasPowerOut && !HasSurvived && !HasBeenCaught; }
        }

        /// <summary>
        /// 使用等级 = 1（基础） + 每一项正在耗电的设备。
        /// 设备有六样（左门、右门、左灯、右灯、监控、手电筒），
        /// 但表只有 <see cref="PowerSystem.MaxUsageLevel"/> 格，全开时会顶格。
        /// </summary>
        private int CountUsageLevel()
        {
            int usage = PowerSystem.MinUsageLevel;

            if (LeftDoorClosed)
            {
                usage++;
            }

            if (RightDoorClosed)
            {
                usage++;
            }

            if (LeftLightOn)
            {
                usage++;
            }

            if (RightLightOn)
            {
                usage++;
            }

            if (MonitorUp)
            {
                usage++;
            }

            if (FlashlightOn && !FreeFlashlightActive)
            {
                usage++;
            }

            // 奶包偷偷拧上去的那一档。她不动你的东西，只让电表转快一点 ——
            // 所以这里加的是「看不见的一格」，玩家只能从 Usage 表和掉电速度察觉
            if (_naibaoUsageTimer > 0f)
            {
                usage++;
            }

            // 跟她说电话也是要钱的：通话期间整条线路多走一格（挂断就还回来）
            if (NaibaoTalking)
            {
                usage++;
            }

            // 「省着点」：整局所有消耗降一级（不低于系统的下限）
            if (Belial != null)
            {
                usage = Belial.EffectiveUsageLevel(usage);
            }

            return usage;
        }
    }
}
