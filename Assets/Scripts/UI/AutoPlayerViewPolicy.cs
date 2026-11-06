namespace NailongFNAF.UI
{
    /// <summary>
    /// AI 代打**什么时候可以动视角**的规则 —— 纯逻辑，不引用 UnityEngine，可脱机测试。
    ///
    /// ⚠️ 用户 2026-11-03 定：**只有闪手电筒的时候**才把视角收回正前方，
    /// 其余时候视角完全归玩家。
    ///
    /// 这条规则以前埋在 AutoPlayer.Think 里（每帧无条件钉死正前方），
    /// 后果是代打一开，玩家就再也转不了头 —— 而转头是这个游戏唯一的操作自由。
    /// 抽出来是为了：① 规则只有一处，不会在几个分支里各写一遍；
    /// ② 能写单元测试盯着，防止以后又被「顺手回正」加回去。
    ///
    /// 为什么闪的时候**必须**回正：噜噜的手电判定是
    /// <c>IsShiningAtFront() = FlashlightOn &amp;&amp; !MonitorUp &amp;&amp; View == Front</c>。
    /// 镜头歪在侧边时，AI 就算把手电打开也照不到正门，等于白闪。
    /// </summary>
    public static class AutoPlayerViewPolicy
    {
        /// <summary>
        /// 这一刻 AI 该不该接管视角（把镜头收回正前方）。
        ///
        /// 只在「噜噜堵到正门 && 还没闪够 && 它没在扑人」时为 true。
        /// 别的任何情况 —— 没事干、扫监控、关侧门、电量低 —— 都必须返回 false。
        /// </summary>
        /// <param name="luluAtFrontDoor">噜噜是否已经站到正门口（Stage 满）</param>
        /// <param name="flashCount">已经闪到第几下</param>
        /// <param name="flashesNeeded">闪到第几下它就退走</param>
        /// <param name="luluAttacking">它是不是已经在扑人（这时代打救不回来，别抢镜头）</param>
        public static bool ShouldHoldView(
            bool luluAtFrontDoor, int flashCount, int flashesNeeded, bool luluAttacking)
        {
            if (!luluAtFrontDoor || luluAttacking)
            {
                return false;
            }

            return flashCount < flashesNeeded;
        }
    }
}
