using NailongFNAF.Core;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 后半程难度加成：「几点起、谁加多少」—— 纯逻辑，不引用 UnityEngine，可脱机测试。
    ///
    /// 规则（用户 2026-11-04 定）：
    /// <list type="number">
    /// <item>**3AM 起**（<see cref="NightClock.BoostFromHour"/>）→ **所有**非 0 的敌人 +1</item>
    /// <item>**4AM 起** → **除奶龙以外**的敌人**再 +1**（奶龙停在 +1）</item>
    /// </list>
    ///
    /// ⚠️ **0 必须保持 0**：0 表示「这一夜它压根不来」，
    /// 抬到 1 等于把它请出场，每夜的角色安排就废了。
    ///
    /// ⚠️ 两级是**累加**的：4AM 时袋鼠/牛来/噜噜是 +2，奶龙还是 +1。
    ///
    /// 为什么奶龙不跟：它靠**偷电**施加压力，AI 值影响的是「偷多快」，
    /// 和门口那几个靠移动频率施压的不一样 —— 后半程再抬它会让电量直接崩盘。
    /// </summary>
    public static class LateNightBoost
    {
        /// <summary>
        /// 第二次加成的起始小时（0 起）。4 = **4AM 起除奶龙外再 +1**。
        /// </summary>
        public const int SecondBoostHour = 4;

        /// <summary>
        /// 算上后半程加成之后，这个敌人此刻真正的活动度。
        /// </summary>
        /// <param name="id">敌人</param>
        /// <param name="level">难度表 / 自定义夜给的**原始**活动度</param>
        /// <param name="hour">当前是几点（0 = 12AM，3 = 3AM）</param>
        public static int Apply(EnemyId id, int level, int hour)
        {
            // 0 保持 0；负数是脏数据，也归 0
            if (level <= 0)
            {
                return 0;
            }

            int boost = 0;

            if (hour >= NightClock.BoostFromHour)
            {
                boost++;
            }

            // 4AM 起：除奶龙外再抬一级
            if (hour >= SecondBoostHour && id != EnemyId.Nailong)
            {
                boost++;
            }

            if (boost <= 0)
            {
                return level;
            }

            int boosted = level + boost;
            return boosted > EnemyBrain.MaxActivityLevel
                ? EnemyBrain.MaxActivityLevel
                : boosted;
        }
    }
}
