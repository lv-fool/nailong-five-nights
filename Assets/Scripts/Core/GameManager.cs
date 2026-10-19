using System;
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
        /// 是否处于无尽模式：撑不到天亮，敌人一直来；玩家靠「行动换电」续命。
        /// 由菜单在开新一局之前设置，<see cref="Restart"/> 会照着它建时钟。
        /// </summary>
        public bool EndlessMode { get; private set; }

        /// <summary>无尽模式的换电账本（普通模式下为 null）。</summary>
        public EndlessRecharge Recharge { get; private set; }

        /// <summary>最近一次行动补给拿到多少电量（百分点），给 HUD 弹「+8%」用。</summary>
        public float LastGain { get; private set; }

        /// <summary>距离 <see cref="LastGain"/> 过去了多久（秒），HUD 用它决定要不要淡出。</summary>
        public float LastGainAge { get; private set; }

        public bool LeftDoorClosed { get; private set; }
        public bool RightDoorClosed { get; private set; }
        public bool LeftLightOn { get; private set; }
        public bool RightLightOn { get; private set; }
        public bool MonitorUp { get; private set; }

        /// <summary>
        /// 手电筒是否正亮着 —— **按住才亮，松手就灭**（不是点击切换）。
        /// 用来照正门外的噜噜，见 <see cref="SetFlashlight"/>。
        /// </summary>
        public bool FlashlightOn { get; private set; }

        /// <summary>玩家当前看向哪边，由视角控制脚本每帧写入。</summary>
        public ViewSide CurrentView { get; private set; }

        /// <summary>监控当前看着第几个机位（0 起）。</summary>
        public int CurrentCameraIndex { get; private set; }

        /// <summary>是否已经撑到天亮。</summary>
        public bool HasSurvived
        {
            get { return Clock != null && Clock.IsNightComplete; }
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
            HasBeenCaught = true;
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

            Clock.Tick(dt);

            // 断电、天亮、被抓到之后手电筒也要灭（按住状态可能来不及松手）
            if (!CanOperate())
            {
                FlashlightOn = false;
            }

            // 断电的一瞬间所有设备一起熄火 —— 门弹开、灯灭、监控掉线。
            // 玩家也再打不开（CanOperate 会挡住），只剩黑暗和奶龙的闪脸。
            if (Power.IsDepleted)
            {
                ReleaseAllDevices();
            }

            Power.SetUsageLevel(CountUsageLevel());
            Power.Tick(dt);
        }

        /// <summary>断电后设备全部停摆。</summary>
        private void ReleaseAllDevices()
        {
            LeftDoorClosed = false;
            RightDoorClosed = false;
            LeftLightOn = false;
            RightLightOn = false;
            MonitorUp = false;
            FlashlightOn = false;
        }

        /// <summary>重开一夜。</summary>
        public void Restart()
        {
            Power = new PowerSystem(PowerSystem.InitialLevel);
            Clock = new NightClock(_secondsPerHour, EndlessMode);
            Recharge = EndlessMode ? new EndlessRecharge() : null;
            LastGain = 0f;
            LastGainAge = 0f;

            LeftDoorClosed = false;
            RightDoorClosed = false;
            LeftLightOn = false;
            RightLightOn = false;
            MonitorUp = false;
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
        /// 设置无尽模式（下一局 <see cref="Restart"/> 生效）。菜单选「无尽模式」时传 true。
        /// </summary>
        public void SetEndless(bool endless)
        {
            EndlessMode = endless;
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
        }

        // ---------------- 操作入口（由 UI 按钮调用）----------------

        public void ToggleLeftDoor()
        {
            if (CanOperate() && !IsSideDisabled(Side.Left))
            {
                LeftDoorClosed = !LeftDoorClosed;
                NoteAction(PlayerAction.Door);
            }
        }

        public void ToggleRightDoor()
        {
            if (CanOperate() && !IsSideDisabled(Side.Right))
            {
                RightDoorClosed = !RightDoorClosed;
                NoteAction(PlayerAction.Door);
            }
        }

        public void ToggleLeftLight()
        {
            if (CanOperate() && !IsSideDisabled(Side.Left))
            {
                LeftLightOn = !LeftLightOn;
                NoteAction(PlayerAction.Light);
            }
        }

        public void ToggleRightLight()
        {
            if (CanOperate() && !IsSideDisabled(Side.Right))
            {
                RightLightOn = !RightLightOn;
                NoteAction(PlayerAction.Light);
            }
        }

        public void ToggleMonitor()
        {
            if (CanOperate())
            {
                MonitorUp = !MonitorUp;
                NoteAction(PlayerAction.Camera);
            }
        }

        /// <summary>
        /// 手电筒按住 / 松开 —— 由 UI 的按下与抬起事件直接调，不做切换。
        /// 松手（<paramref name="on"/> = false）永远生效，免得断电时卡在亮着。
        /// </summary>
        public void SetFlashlight(bool on)
        {
            bool wasOn = FlashlightOn;
            FlashlightOn = on && CanOperate();

            if (!wasOn && FlashlightOn)
            {
                NoteAction(PlayerAction.Flashlight);
            }
        }

        /// <summary>
        /// 无尽模式的「行动换电」：登记一次有效操作，攒够记点就补电。
        /// 普通模式下整个方法什么都不做（<see cref="Recharge"/> 为 null）。
        /// </summary>
        private void NoteAction(PlayerAction action)
        {
            if (!EndlessMode || Recharge == null || Power == null || !CanOperate())
            {
                return;
            }

            float gain = Recharge.RegisterAction(action);
            if (gain <= 0f)
            {
                return;
            }

            float granted = Power.Grant(gain);
            LastGain = granted;
            LastGainAge = 0f;
        }

        /// <summary>断电、天亮或被抓到之后，玩家不能再操作设备。</summary>
        private bool CanOperate()
        {
            return !HasPowerOut && !HasSurvived && !HasBeenCaught;
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

            if (FlashlightOn)
            {
                usage++;
            }

            return usage;
        }
    }
}
