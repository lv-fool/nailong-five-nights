namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 成就清单：所有成就的固定定义。解锁状态存在 <see cref="AchievementStore"/>，
    /// 这里只描述「有什么成就、叫什么、怎么拿」。
    ///
    /// 加新成就 = 在这里加一行 + 在触发点调 <see cref="AchievementStore.Unlock"/>。
    ///
    /// 命名约定（2026-10-20 定）：**名字是意象，不是说明书** ——
    /// 「多出来的呼吸」而不是「意识醒了」，玩家第一次看不懂，解锁后回头看才"啊"一声。
    /// 彩蛋类成就 <see cref="Def.Hidden"/> = true，列表里未解锁时显示 `???`，
    /// 连描述也藏起来；解锁后弹 <see cref="Def.Flavor"/> 那行世界观旁白。
    /// </summary>
    public static class AchievementCatalog
    {
        /// <summary>一条成就的定义。</summary>
        public sealed class Def
        {
            public readonly string Id;
            public readonly string Title;
            public readonly string Description;

            /// <summary>隐藏成就：未解锁时列表里显示 `???`，描述也不给。</summary>
            public readonly bool Hidden;

            /// <summary>解锁时追加的一行世界观旁白；空串表示没有。</summary>
            public readonly string Flavor;

            public Def(string id, string title, string description)
                : this(id, title, description, false, string.Empty)
            {
            }

            public Def(string id, string title, string description, bool hidden, string flavor)
            {
                Id = id;
                Title = title;
                Description = description;
                Hidden = hidden;
                Flavor = flavor ?? string.Empty;
            }
        }

        // ---- 故事模式 ----

        /// <summary>通关第 1 夜。</summary>
        public const string FirstNight = "first_night";

        /// <summary>通关第 3 夜。</summary>
        public const string HalfWay = "half_way";

        /// <summary>通关第 5 夜。</summary>
        public const string FiveNights = "five_nights";

        /// <summary>
        /// 通关隐藏夜（原第 6 夜）。
        ///
        /// ⚠️ **隐藏夜已暂时移除（用户 2026-10-22 定），这条成就先不进
        /// <see cref="All"/>** —— 否则它会永远显示成「???」拿不到，
        /// 而且文案还写着已经不存在的一夜。
        /// 恢复隐藏夜时把它加回 <see cref="All"/> 即可。
        /// </summary>
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

        // ---- 彩蛋（隐藏）----

        /// <summary>单夜关门 12 次以上还通关。</summary>
        public const string DoorKeeper = "door_keeper";

        /// <summary>整夜一次监控都不升起来，通关。</summary>
        public const string NoLookUp = "no_look_up";

        /// <summary>整夜一次灯都不开，通关。</summary>
        public const string NoLight = "no_light";

        /// <summary>单夜把 11 个机位全看一遍，通关。</summary>
        public const string EveryCorner = "every_corner";

        /// <summary>在厨房机位（CAM 6）待满 20 秒，通关。</summary>
        public const string KitchenSilence = "kitchen_silence";

        /// <summary>单夜和奶龙对上眼 3 次，通关。</summary>
        public const string ItBlinksFirst = "it_blinks_first";

        /// <summary>在 5:50 之后才被抓住。</summary>
        public const string FiveFiftyNine = "five_fifty_nine";

        /// <summary>在 0:30 之前就被抓住。</summary>
        public const string ZeroTwenty = "zero_twenty";

        /// <summary>单夜开关门与灯合计 40 次以上。</summary>
        public const string FingerRhythm = "finger_rhythm";

        /// <summary>自定义夜全 20，通关时电量还剩 1% 以上。</summary>
        public const string FullHouse = "full_house";

        /// <summary>全部成就，按展示顺序（普通 9 条 → 彩蛋 10 条）。</summary>
        public static readonly Def[] All =
        {
            // 普通成就：名字是意象，描述照旧写清条件
            new Def(FirstNight, "多出来的呼吸", "通关第 1 夜",
                false, "他后来总说，小时候一个人也能等。书包上的带子却被攥得很皱。"),
            new Def(HalfWay, "手臂比门先累", "通关第 3 夜",
                false, "他把那顿饭说成多余的关心，第二天却还是带走了饭盒。"),
            new Def(FiveNights, "把过去锁在门后", "通关第 5 夜",
                false, "电话里他说一切都好。挂断以后，那句没说完的话还在屋里。"),
            // 隐藏夜暂时移除，这条跟着一起撤 —— 恢复时取消注释。
            // new Def(HiddenNight, "让影子先走", "通关隐藏夜（第 6 夜）"),
            new Def(PowerSaver, "还剩一口气", "通关任意一夜时剩余电量不低于 30%"),
            new Def(BlackoutSurvivor, "它松了手", "断电之后仍撑到早上 6 点"),
            new Def(LuluMaster, "小孩怕的是光", "单夜用手电筒逼退噜噜 3 次"),
            new Def(EndlessDozen, "第十二个小时", "永夜模式一局撑过 12 小时"),
            new Def(CustomMax, "四张脸", "自定义夜四只敌人全 20 通关"),

            // 彩蛋：隐藏，未解锁显示 ???
            new Def(DoorKeeper, "门槛", "单夜关门 12 次以上，还撑到天亮",
                true, "门挡住的东西，从来没真的走远。"),
            new Def(NoLookUp, "不抬头的一夜", "整夜一次监控都不升起来，撑到天亮",
                true, "有些夜里，不看反而更清楚。"),
            new Def(NoLight, "摸黑", "整夜一次灯都不开，撑到天亮",
                true, "灯是开给自己看的，不是开给门外那位。"),
            new Def(EveryCorner, "十一个角落", "单夜把 11 个机位全看一遍，撑到天亮",
                true, "十一个角落都记得你站在这儿。"),
            new Def(KitchenSilence, "厨房里没有人", "在厨房（CAM 6）待满 20 秒，撑到天亮",
                true, "他说不用留饭。锅里的那份，晚上还是热了一遍。"),
            new Def(ItBlinksFirst, "它先眨眼", "单夜和奶龙对上眼 3 次，撑到天亮",
                true, "它知道你在看。它也知道你就是它。"),
            new Def(FiveFiftyNine, "五点五十九", "在 5:50 之后才被抓住",
                true, "他查了回去的车次，又把页面关掉。再等等，等混出个样子。"),
            new Def(ZeroTwenty, "零点二十", "在 0:30 之前就被抓住",
                true, "第一夜，你以为是风扇。"),
            new Def(FingerRhythm, "手指的节奏", "单夜开关门与灯合计 40 次以上",
                true, "手比脑子先记住这屋里的规矩。"),
            new Def(FullHouse, "满格", "自定义夜四只全 20，通关时电量还剩 1% 以上",
                true, "满格的恶意，一格的电。"),
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

        /// <summary>隐藏成就且还没解锁 —— 界面上要显示成 `???`。</summary>
        public static bool ShouldMask(Def def)
        {
            return def != null && def.Hidden && !AchievementStore.IsUnlocked(def.Id);
        }
    }
}
