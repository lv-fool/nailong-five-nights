namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 玩家能做的「有效操作」类别 —— 永夜模式靠这些动作换电量。
    ///
    /// 只列真正花电、真正有用处的操作：关门、开灯、切监控、按手电筒。
    /// 纯逻辑枚举，不引用 UnityEngine。
    /// </summary>
    public enum PlayerAction
    {
        /// <summary>开关任意一侧的门。</summary>
        Door = 0,

        /// <summary>开关任意一侧的灯。</summary>
        Light = 1,

        /// <summary>切监控（升起 / 放下 / 换机位）。</summary>
        Camera = 2,

        /// <summary>按下手电筒（松手不算）。</summary>
        Flashlight = 3,
    }
}
