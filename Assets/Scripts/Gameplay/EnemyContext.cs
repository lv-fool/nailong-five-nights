namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 玩家当前状态的快照，喂给敌人 AI 做决策用。
    ///
    /// 做成值类型，每次 Tick 传一份 —— 敌人 AI 因此完全不依赖 UnityEngine，
    /// 可以脱离 Unity 做单元测试。
    /// </summary>
    public struct EnemyContext
    {
        public bool LeftDoorClosed;
        public bool RightDoorClosed;
        public bool LeftLightOn;
        public bool RightLightOn;

        /// <summary>监控是否正开着。</summary>
        public bool MonitorUp;

        /// <summary>玩家此刻看向哪边。</summary>
        public ViewSide View;

        /// <summary>监控正看着第几个机位（0 起，仅 MonitorUp 时有意义）。</summary>
        public int CameraIndex;

        public EnemyContext(bool leftDoorClosed, bool rightDoorClosed,
            bool leftLightOn, bool rightLightOn, bool monitorUp,
            ViewSide view, int cameraIndex)
        {
            LeftDoorClosed = leftDoorClosed;
            RightDoorClosed = rightDoorClosed;
            LeftLightOn = leftLightOn;
            RightLightOn = rightLightOn;
            MonitorUp = monitorUp;
            View = view;
            CameraIndex = cameraIndex;
        }

        public bool IsDoorClosed(Side side)
        {
            return side == Side.Left ? LeftDoorClosed : RightDoorClosed;
        }

        public bool IsLightOn(Side side)
        {
            return side == Side.Left ? LeftLightOn : RightLightOn;
        }

        /// <summary>玩家此刻是否正看着指定这一侧（开着监控时不算 —— 那时在看屏幕）。</summary>
        public bool IsLookingAt(Side side)
        {
            if (MonitorUp)
            {
                return false;
            }

            return side == Side.Left ? View == ViewSide.Left : View == ViewSide.Right;
        }

        /// <summary>构造一份「什么都没开、看向正前方」的安静状态，测试与调试用。</summary>
        public static EnemyContext Idle()
        {
            return new EnemyContext(false, false, false, false, false, ViewSide.Front, 0);
        }
    }
}
