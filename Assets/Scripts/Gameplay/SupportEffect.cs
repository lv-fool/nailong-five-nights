namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 「来自奶包的支援」具体改哪一条规则 —— 效果类型。
    ///
    /// 加新支援时基本不用动这里：同一规则的不同数值只是
    /// <see cref="SupportDef.Value"/> / <see cref="SupportDef.Value2"/> 不同。
    /// 只有真的需要一条<b>全新的规则</b>才新增枚举值。
    ///
    /// 前面的 0–6 是第一批七条的内容，数值不要改 —— 存档按 id 存，effect 只做派发。
    ///
    /// 纯逻辑枚举，不引用 UnityEngine。
    /// </summary>
    public enum SupportEffect
    {
        /// <summary>她说的每一句都是真的（不再误导）。</summary>
        TruthfulMessages = 0,

        /// <summary>奶龙每次偷到电之后完全静止一段时间。</summary>
        NailongSilence = 1,

        /// <summary>按这一回合的操作记账，下次结算折算成电量返还。</summary>
        ActionRefund = 2,

        /// <summary>怪物动手前的读条（先兆）时间变长。</summary>
        LongerTelegraph = 3,

        /// <summary>监控里看到怪物时，把它锁在当前房间。</summary>
        CameraLock = 4,

        /// <summary>怪物扑空一次。</summary>
        MissOnce = 5,

        /// <summary>本局所有电量消耗下降一级。</summary>
        UsageDown = 6,

        // ---------------- 第二批（老板 2026-10-26 那张表） ----------------

        /// <summary>电量上限乘 Value，并立刻补 Value2 点电。</summary>
        PowerCapBoost = 7,

        /// <summary>掉电速度按 Value 的比例减少（0.25 = 少掉 25%）。</summary>
        DrainRateDown = 8,

        /// <summary>每过一个整点回复 Value% 的电。</summary>
        HourlyRegen = 9,

        /// <summary>开关门耗电按 Value 的比例减少。</summary>
        DoorLightDrainDown = 10,

        /// <summary>手电耗电按 Value 的比例减少。</summary>
        FlashlightDrainDown = 11,

        /// <summary>关门/开灯的生效判定窗口加 Value 秒。</summary>
        InputGrace = 12,

        /// <summary>致命判定改为「擦过」，一局 Value 次。</summary>
        LethalToGraze = 13,

        /// <summary>每次过点之后的 Value 秒里敌人不动。</summary>
        HourlyFreeze = 14,

        /// <summary>门口不开灯也能看出有没有人来（暗处剪影）。Value = 看见的概率，1 = 总是看得见。</summary>
        DoorSightWithoutLight = 15,

        /// <summary>敌人移动效率按 Value 的比例下降。</summary>
        EnemySpeedDown = 16,

        /// <summary>黑化怪恢复「只关门就能拦」。</summary>
        BlackenedRuleRelaxed = 17,

        /// <summary>敌人每次在门口多停 Value 秒。</summary>
        DoorLinger = 18,

        /// <summary>监控刷新更快，切换机位有 Value 秒残影。</summary>
        CameraRefresh = 19,

        /// <summary>每阶段开始她报一次准确机位。</summary>
        StageAccurateSpot = 20,

        /// <summary>开局随机 Value 个机位标「安全」。</summary>
        SafeSpots = 21,

        /// <summary>已解锁的敌人弱点在监控上高亮。</summary>
        WeakPointHighlight = 22,

        /// <summary>电量归零时强制回 Value%，一局 Value2 次。</summary>
        PowerRescue = 23,

        /// <summary>一次致命攻击改为重伤。</summary>
        LethalToWound = 24,

        /// <summary>手电完全不消耗电量，且断电之后也还能开。</summary>
        FreeFlashlight = 25,

        /// <summary>时间流速乘 Value（闹钟 1.15 / 失眠 0.9）。</summary>
        TimeScale = 26,

        /// <summary>之后的抽取变成 Value 选一。</summary>
        OfferCount = 27,

        /// <summary>可以重抽 Value 次。</summary>
        Reroll = 28,

        /// <summary>已获得的所有支援效果乘以 Value。</summary>
        AmplifyAll = 29,

        /// <summary>手电范围乘 Value，但判定窗口减 Value2 秒。</summary>
        RiskFlashlight = 30,

        /// <summary>立刻补 Value 电、上限加 Value2，但这局不能再拿电力类。</summary>
        RiskNoPower = 31,

        /// <summary>写着像传奇，实际只补 Value 电。</summary>
        Placebo = 32,

        /// <summary>监控上直接看到敌人在哪个房间；Value = 批次（1 = 噜噜+牛来，2 = 再加奶龙+袋鼠）。</summary>
        RevealEnemies = 33,
    }
}
