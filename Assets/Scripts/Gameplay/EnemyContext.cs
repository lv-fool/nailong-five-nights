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

        /// <summary>手电筒是否正亮着（按住才亮）。用来照正门外的噜噜。</summary>
        public bool FlashlightOn;

        /// <summary>玩家此刻看向哪边。</summary>
        public ViewSide View;

        /// <summary>监控正看着第几个机位（0 起，仅 MonitorUp 时有意义）。</summary>
        public int CameraIndex;

        /// <summary>
        /// 右门口现在被谁占着（没人就是 null）。
        ///
        /// 右门是奶龙与牛来**共用的通道** —— 同一条路上不能同时挤两个威胁，
        /// 先到的站门口，后到的在走廊等着。
        /// </summary>
        public EnemyId? RightDoorHolder;

        public EnemyContext(bool leftDoorClosed, bool rightDoorClosed,
            bool leftLightOn, bool rightLightOn, bool monitorUp,
            ViewSide view, int cameraIndex)
            : this(leftDoorClosed, rightDoorClosed, leftLightOn, rightLightOn,
                monitorUp, view, cameraIndex, null)
        {
        }

        public EnemyContext(bool leftDoorClosed, bool rightDoorClosed,
            bool leftLightOn, bool rightLightOn, bool monitorUp,
            ViewSide view, int cameraIndex, EnemyId? rightDoorHolder,
            bool flashlightOn = false)
        {
            LeftDoorClosed = leftDoorClosed;
            RightDoorClosed = rightDoorClosed;
            LeftLightOn = leftLightOn;
            RightLightOn = rightLightOn;
            MonitorUp = monitorUp;
            View = view;
            CameraIndex = cameraIndex;
            RightDoorHolder = rightDoorHolder;
            FlashlightOn = flashlightOn;
        }

        public bool IsDoorClosed(Side side)
        {
            return side == Side.Left ? LeftDoorClosed : RightDoorClosed;
        }

        public bool IsLightOn(Side side)
        {
            return side == Side.Left ? LeftLightOn : RightLightOn;
        }

        /// <summary>
        /// 这一侧的门口是不是已经站着别人了。
        /// 左门目前是牛来的专属通道，不存在争抢，所以只看右门。
        /// </summary>
        public bool IsDoorTakenByOther(Side side, EnemyId self)
        {
            if (side != Side.Right || !RightDoorHolder.HasValue)
            {
                return false;
            }

            return RightDoorHolder.Value != self;
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

        /// <summary>
        /// 手电筒是否正照着**正前方** —— 噜噜从正门来，就靠这个把它逼退。
        /// 转头看两边、或开着监控时都不算。
        /// </summary>
        public bool IsShiningAtFront()
        {
            return FlashlightOn && !MonitorUp && View == ViewSide.Front;
        }

        /// <summary>构造一份「什么都没开、看向正前方」的安静状态，测试与调试用。</summary>
        public static EnemyContext Idle()
        {
            return new EnemyContext(false, false, false, false, false, ViewSide.Front, 0);
        }
    }
}
