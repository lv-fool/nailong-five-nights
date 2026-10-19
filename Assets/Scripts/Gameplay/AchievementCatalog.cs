namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 成就清单：所有成就的固定定义。解锁状态存在 <see cref="AchievementStore"/>，
    /// 这里只描述「有什么成就、叫什么、怎么拿」。
    ///
    /// 加新成就 = 在这里加一行 + 在触发点调 <see cref="AchievementStore.Unlock"/>。
    /// </summary>
    public static class AchievementCatalog
    {
        /// <summary>一条成就的定义。</summary>
        public sealed class Def
        {
            public readonly string Id;
            public readonly string Title;
            public readonly string Description;

            public Def(string id, string title, string description)
            {
                Id = id;
                Title = title;
                Description = description;
            }
        }

        // ---- 故事模式 ----

        /// <summary>通关第 1 夜。</summary>
        public const string FirstNight = "first_night";

        /// <summary>通关第 3 夜。</summary>
        public const string HalfWay = "half_way";

        /// <summary>通关第 5 夜。</summary>
        public const string FiveNights = "five_nights";

        /// <summary>通关隐藏夜（第 6 夜）。</summary>
        public const string HiddenNight = "hidden_night";

        /// <summary>任意一夜通关时剩余电量 ≥ 30%。</summary>
        public const string PowerSaver = "power_saver";

        /// <summary>断电之后仍撑到 6 点。</summary>
        public const string BlackoutSurvivor = "blackout_survivor";

        /// <summary>单夜用手电筒逼退噜噜 3 次。</summary>
        public const string LuluMaster = "lulu_master";

        // ---- 永夜模式 ----

        /// <summary>永夜模式一局撑过 12 小时。</summary>
        public const string EndlessDozen = "endless_dozen";

        // ---- 自定义夜 ----

        /// <summary>自定义夜四只全 20 通关（照 FNAF 1 的 20/20/20/20）。</summary>
        public const string CustomMax = "custom_max";

        /// <summary>全部成就，按展示顺序。</summary>
        public static readonly Def[] All =
        {
            new Def(FirstNight, "初来乍到", "通关第 1 夜"),
            new Def(HalfWay, "渐入佳境", "通关第 3 夜"),
            new Def(FiveNights, "五夜老兵", "通关第 5 夜"),
            new Def(HiddenNight, "隐藏夜征服者", "通关隐藏夜（第 6 夜）"),
            new Def(PowerSaver, "省电达人", "通关任意一夜时剩余电量不低于 30%"),
            new Def(BlackoutSurvivor, "黑暗中的幸运儿", "断电之后仍撑到早上 6 点"),
            new Def(LuluMaster, "闪光灯大师", "单夜用手电筒逼退噜噜 3 次"),
            new Def(EndlessDozen, "永夜常客", "永夜模式一局撑过 12 小时"),
            new Def(CustomMax, "终极挑战", "自定义夜四只敌人全 20 通关"),
        };

        /// <summary>按 id 找定义；找不到返回 null。</summary>
        public static Def Find(string id)
        {
            foreach (Def def in All)
            {
                if (def.Id == id)
                {
                    return def;
                }
            }

            return null;
        }
    }
}
