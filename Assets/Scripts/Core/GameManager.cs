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
            if (Clock.IsNightComplete)
            {
                return;
            }

            float dt = Time.deltaTime;

            Clock.Tick(dt);
            Power.SetUsageLevel(CountUsageLevel());
            Power.Tick(dt);
        }

        /// <summary>重开一夜。</summary>
        public void Restart()
        {
            Power = new PowerSystem();
            Clock = new NightClock(_secondsPerHour);

            LeftDoorClosed = false;
            RightDoorClosed = false;
            LeftLightOn = false;
            RightLightOn = false;
            MonitorUp = false;
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

        /// <summary>断电或天亮之后，玩家不能再操作设备。</summary>
        private bool CanOperate()
        {
            return !HasPowerOut && !HasSurvived;
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
