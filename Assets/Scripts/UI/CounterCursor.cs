namespace NailongFNAF.UI
{
    /// <summary>
    /// 「计数器比游标大就播一声」这套模式的纯逻辑 —— 不引用 UnityEngine，可脱机测试。
    ///
    /// 现在用在敲门声上（`EnemyDirector.KnockCount` 对 `PrototypeScene._lastKnockCount`），
    /// 别的「累计次数 → 播一次」的地方也能直接换个 `Func` 复用。
    ///
    /// ⚠️ **为什么需要「计数器变小」这一支**（2026-11-04 踩过）：
    /// 计数器活在 <c>EnemyDirector</c> 里，重建（换夜 / 重试 / 永夜）时会被清零；
    /// 游标活在表现层，**不会跟着重置**。于是新一局第一次敲门是 `1`，
    /// 而游标还停在上一局的 `1` —— `1 > 1` 不成立，**头一声敲门被吞掉**。
    /// 来得少的夜听着就像「整夜没敲过门」。
    ///
    /// 判据用「计数器比游标小」而不是「等于某值」，任何重建入口都覆盖得到，
    /// 不用在五六个开局按钮里各补一行。
    /// </summary>
    public static class CounterCursor
    {
        /// <summary>
        /// 给定「计数器当前值」和「上次消费到的游标」，算出这次该播几次。
        /// </summary>
        /// <param name="counter">累计计数器（如 KnockCount）</param>
        /// <param name="cursor">已经消费到的位置</param>
        /// <param name="newCursor">回调后应当写回的游标值</param>
        /// <returns>这次应该触发的次数（0 = 不播）</returns>
        public static int Advanced(int counter, int cursor, out int newCursor)
        {
            // 计数器比游标小 → 计数器被重建过（新一局），游标得跟着归零，
            // 否则这一局的第一次会被当成「已经播过了」。
            int effective = counter < cursor ? 0 : cursor;

            if (counter <= effective)
            {
                newCursor = effective;
                return 0;
            }

            newCursor = counter;
            return counter - effective;
        }

        /// <summary>只要「要不要播」的简版。</summary>
        public static bool ShouldFire(int counter, int cursor, out int newCursor)
        {
            return Advanced(counter, cursor, out newCursor) > 0;
        }
    }
}
