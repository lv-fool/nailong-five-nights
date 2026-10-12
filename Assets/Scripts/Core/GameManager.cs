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

        public bool LeftDoorClosed { get; private set; }
        public bool RightDoorClosed { get; private set; }
        public bool LeftLightOn { get; private set; }
        public bool RightLightOn { get; private set; }
        public bool MonitorUp { get; private set; }

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

        /// <summary>被敌人抓到，冻结本夜。</summary>
        public void MarkCaught()
        {
            HasBeenCaught = true;
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
            if (Clock.IsNightComplete || HasBeenCaught)
            {
                return;
            }

            float dt = Time.deltaTime;

            Clock.Tick(dt);

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
        }

        /// <summary>重开一夜。</summary>
        public void Restart()
        {
            Power = new PowerSystem(PowerSystem.InitialLevel);
            Clock = new NightClock(_secondsPerHour);

            LeftDoorClosed = false;
            RightDoorClosed = false;
            LeftLightOn = false;
            RightLightOn = false;
            MonitorUp = false;

            CurrentView = ViewSide.Front;
            CurrentCameraIndex = 0;
            HasBeenCaught = false;

            if (Restarted != null)
            {
                Restarted();
            }
        }

        /// <summary>视角控制脚本每帧调用，写入玩家当前看向哪边。</summary>
        public void SetView(ViewSide view)
        {
            CurrentView = view;
        }

        /// <summary>切换到指定监控机位（0 起，超出范围忽略）。</summary>
        public void SetCamera(int index)
        {
            if (index >= 0)
            {
                CurrentCameraIndex = index;
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
        }

        // ---------------- 操作入口（由 UI 按钮调用）----------------

        public void ToggleLeftDoor()
        {
            if (CanOperate())
            {
                LeftDoorClosed = !LeftDoorClosed;
            }
        }

        public void ToggleRightDoor()
        {
            if (CanOperate())
            {
                RightDoorClosed = !RightDoorClosed;
            }
        }

        public void ToggleLeftLight()
        {
            if (CanOperate())
            {
                LeftLightOn = !LeftLightOn;
            }
        }

        public void ToggleRightLight()
        {
            if (CanOperate())
            {
                RightLightOn = !RightLightOn;
            }
        }

        public void ToggleMonitor()
        {
            if (CanOperate())
            {
                MonitorUp = !MonitorUp;
            }
        }

        /// <summary>断电、天亮或被抓到之后，玩家不能再操作设备。</summary>
        private bool CanOperate()
        {
            return !HasPowerOut && !HasSurvived && !HasBeenCaught;
        }

        /// <summary>
        /// 使用等级 = 1（基础） + 每一项正在耗电的设备。
        /// 最多 5 级：左门、右门、左灯、右灯、监控。
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

            return usage;
        }
    }
}
