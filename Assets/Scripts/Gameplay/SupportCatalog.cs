namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 「来自奶包的支援」全部内容表。
    ///
    /// 内容来源：老板 2026-10-26 定的七条（童年轨）+ 同一天发来的二十八条
    /// （电力 / 判定 / 敌人 / 情报 / 保险 / 时间 / 支援 / 双刃 / 彩蛋）。
    /// 加新支援只改这张表，不用动 <see cref="BelialNight"/> 的逻辑 —— 除非那条
    /// 支援需要一条全新的 <see cref="SupportEffect"/> 规则。
    ///
    /// 命名两条轨：童年轨走「小时候经历过的某件事」（拉钩、罚站、数到三……），
    /// 干预轨走药名（舍曲林、氯硝西泮……）。前者藏剧情，后者是外来的东西。
    ///
    /// 纯静态数据，不引用 UnityEngine。
    /// </summary>
    public static class SupportCatalog
    {
        /// <summary>
        /// 这一叠卡片的正式名字（界面上直接用这个字符串）。
        /// 老板 2026-10-26 定的：叫「来自奶包的支援」，跟游戏里她的名字一致。
        /// </summary>
        public const string Title = "来自奶包的支援";

        /// <summary>开局白送那一条的 id —— 第一次抽取三张全是它。</summary>
        public const string FirstPickId = "truthful";

        /// <summary>全部支援，按类别排，顺序即同权重下的默认展示顺序。</summary>
        public static readonly SupportDef[] All =
        {
            // ==================== 电力 ====================
            new SupportDef(
                "refund",
                "存钱罐",
                "你这一回合做的每个动作，我都替你存着。下次交接的时候，一起折成电还给你。",
                SupportCategory.Power,
                SupportRarity.Legendary,
                SupportEffect.ActionRefund,
                0.75f,
                2),

            new SupportDef(
                "thrift",
                "关灯睡觉",
                "这一局所有耗电降一级。小时候一到点就关灯，屋里能省下这么些。",
                SupportCategory.Power,
                SupportRarity.Common,
                SupportEffect.UsageDown,
                1f,
                1),

            new SupportDef(
                "sertraline",
                "舍曲林",
                "上限翻倍，再立刻给你补五十。这瓶是我从外面弄来的，别问哪来的。",
                SupportCategory.Power,
                SupportRarity.Legendary,
                SupportEffect.PowerCapBoost,
                2f,
                1,
                50f),

            new SupportDef(
                "serotonin",
                "血清素",
                "掉电慢四分之一。就……心情稳一点，电也稳一点。",
                SupportCategory.Power,
                SupportRarity.Rare,
                SupportEffect.DrainRateDown,
                0.25f,
                1),

            new SupportDef(
                "fluoxetine",
                "氟西汀",
                "每到整点，我偷偷给你补八个点。不多，但一直有。",
                SupportCategory.Power,
                SupportRarity.Rare,
                SupportEffect.HourlyRegen,
                8f,
                2),

            new SupportDef(
                "venlafaxine",
                "文拉法辛",
                "开关门的耗电砍一半。手别抖，抖也不额外扣。",
                SupportCategory.Power,
                SupportRarity.Common,
                SupportEffect.DoorLightDrainDown,
                0.5f,
                2),

            new SupportDef(
                "bupropion",
                "安非他酮",
                "手电省四成。多照一会儿，我也不会说你。",
                SupportCategory.Power,
                SupportRarity.Common,
                SupportEffect.FlashlightDrainDown,
                0.4f,
                2),

            // ==================== 判定 ====================
            new SupportDef(
                "telegraph",
                "数到三",
                "它们动手之前得先数到三。够你把手放到门上的。",
                SupportCategory.Judgement,
                SupportRarity.Common,
                SupportEffect.LongerTelegraph,
                1.35f,
                2),

            new SupportDef(
                "norepinephrine",
                "去甲肾上腺素",
                "关门、开灯多给你零点四秒。零点四秒，够的。",
                SupportCategory.Judgement,
                SupportRarity.Rare,
                SupportEffect.InputGrace,
                0.4f,
                2),

            new SupportDef(
                "clonazepam",
                "氯硝西泮",
                "两次致命会被改成擦过。就两次，我数着呢。",
                SupportCategory.Judgement,
                SupportRarity.Rare,
                SupportEffect.LethalToGraze,
                2f,
                1),

            new SupportDef(
                "melatonin",
                "褪黑素",
                "每次过点，它们会停六秒。像睡着了一样，别去叫醒它们。",
                SupportCategory.Judgement,
                SupportRarity.Common,
                SupportEffect.HourlyFreeze,
                6f,
                1),

            new SupportDef(
                "oryzanol",
                "谷维素",
                "门口不开灯也看得见有没有人来。黑里那个影子，我一眼就认得。",
                SupportCategory.Judgement,
                SupportRarity.Common,
                SupportEffect.DoorSightWithoutLight,
                1f,
                1),

            // ==================== 敌人 ====================
            new SupportDef(
                "silence",
                "罚站",
                "奶龙每回偷到电，就会被按在原地罚站一阵子。那段时间它真的不动。",
                SupportCategory.Enemy,
                SupportRarity.Rare,
                SupportEffect.NailongSilence,
                45f,
                1),

            new SupportDef(
                "lock",
                "一二三木头人",
                "只要你在监控里看见它，它就得定住。不许动，动了就输。",
                SupportCategory.Enemy,
                SupportRarity.Rare,
                SupportEffect.CameraLock,
                25f,
                1),

            new SupportDef(
                "dopamine",
                "多巴胺",
                "它们走得慢两成。看着慢，其实还是在走。",
                SupportCategory.Enemy,
                SupportRarity.Common,
                SupportEffect.EnemySpeedDown,
                0.2f,
                2),

            new SupportDef(
                "olanzapine",
                "奥氮平",
                "黑化的那些，把「灯加门」那条还回来了——只关门就能拦住。",
                SupportCategory.Enemy,
                SupportRarity.Legendary,
                SupportEffect.BlackenedRuleRelaxed,
                1f,
                1),

            new SupportDef(
                "quetiapine",
                "喹硫平",
                "它们在门口会多站一秒半。那一秒半，你看得清是谁。",
                SupportCategory.Enemy,
                SupportRarity.Common,
                SupportEffect.DoorLinger,
                1.5f,
                2),

            // ==================== 情报 ====================
            new SupportDef(
                FirstPickId,
                "拉钩",
                "小时候拉过钩的事，我一件都没忘。这一局我说哪边有人，哪边就真的有人。",
                SupportCategory.Info,
                SupportRarity.Legendary,
                SupportEffect.TruthfulMessages,
                0f,
                1),

            new SupportDef(
                "caffeine",
                "咖啡因",
                "监控刷得更快，切机位会留半秒残影——你能看见它刚刚在哪儿。",
                SupportCategory.Info,
                SupportRarity.Common,
                SupportEffect.CameraRefresh,
                0.3f,
                1),

            new SupportDef(
                "phone",
                "一通电话",
                "每个阶段开头，我报一次准的机位。就一次，你自己记。",
                SupportCategory.Info,
                SupportRarity.Common,
                SupportEffect.StageAccurateSpot,
                1f,
                1),

            new SupportDef(
                "outing",
                "今天出门了",
                "开局有两个机位是空的。它们今天不走那边，我替你看着。",
                SupportCategory.Info,
                SupportRarity.Common,
                SupportEffect.SafeSpots,
                2f,
                1),

            new SupportDef(
                "recheck",
                "复诊",
                "你认识过的那些，弱点会亮在监控上。红色那块，照那儿打。",
                SupportCategory.Info,
                SupportRarity.Rare,
                SupportEffect.WeakPointHighlight,
                1f,
                1),

            new SupportDef(
                "wardround",
                "查房",
                "我趴在你旁边，告诉你谁在哪间。噜噜和牛来先来；再选一次，奶龙和袋鼠也归我管。",
                SupportCategory.Info,
                SupportRarity.Rare,
                SupportEffect.RevealEnemies,
                1f,
                2),

            // ==================== 保险 ====================
            new SupportDef(
                "miss",
                "摔了一跤",
                "它扑过来的时候会摔一跤。一次。就这一次。",
                SupportCategory.Insurance,
                SupportRarity.Rare,
                SupportEffect.MissOnce,
                1f,
                3),

            new SupportDef(
                "backup",
                "备用电源",
                "电量见底之前，我给你垫十五个点。一局就一次，我只有一块。",
                SupportCategory.Insurance,
                SupportRarity.Rare,
                SupportEffect.PowerRescue,
                15f,
                1,
                1f),

            new SupportDef(
                "twolives",
                "两条命",
                "一次致命，会变成重伤。你还站得住，只是会很疼。",
                SupportCategory.Insurance,
                SupportRarity.Rare,
                SupportEffect.LethalToWound,
                1f,
                1),

            new SupportDef(
                "nightlight",
                "床头灯",
                "手电不耗电了；就算电断干净，它也能亮。我给它留了一格。",
                SupportCategory.Insurance,
                SupportRarity.Common,
                SupportEffect.FreeFlashlight,
                0f,
                1),

            // ==================== 时间 ====================
            new SupportDef(
                "alarm",
                "闹钟",
                "时间走快一成半。早点到点，但一口气也更长。",
                SupportCategory.Time,
                SupportRarity.Rare,
                SupportEffect.TimeScale,
                1.15f,
                1),

            new SupportDef(
                "insomnia",
                "失眠",
                "时间走慢一成。你多熬一会儿，也多烧一会儿电。",
                SupportCategory.Time,
                SupportRarity.Common,
                SupportEffect.TimeScale,
                0.9f,
                1),

            // ==================== 支援机制 ====================
            new SupportDef(
                "prescription",
                "处方笺",
                "以后的支援变成四选一。多一张，就多一个能躲的地方。",
                SupportCategory.Draw,
                SupportRarity.Rare,
                SupportEffect.OfferCount,
                4f,
                1),

            new SupportDef(
                "rerollslip",
                "复诊单",
                "可以重抽一次。万一三张都不想要呢。",
                SupportCategory.Draw,
                SupportRarity.Common,
                SupportEffect.Reroll,
                1f,
                1),

            new SupportDef(
                "adherence",
                "依从性好",
                "你已经拿到的那些，全都再多一成半。听话的人有好报。",
                SupportCategory.Draw,
                SupportRarity.Legendary,
                SupportEffect.AmplifyAll,
                1.15f,
                1),

            // ==================== 双刃 ====================
            new SupportDef(
                "allnighter",
                "熬夜",
                "手电照得更远，但关门判定窗口少零点二秒。熬出来的眼睛，反应也是慢的。",
                SupportCategory.Risk,
                SupportRarity.Dual,
                SupportEffect.RiskFlashlight,
                1.5f,
                1,
                0.2f),

            new SupportDef(
                "overdose",
                "加量",
                "立刻补四十，上限再多三成。代价是这局不能再拿电力类。",
                SupportCategory.Risk,
                SupportRarity.Dual,
                SupportEffect.RiskNoPower,
                40f,
                1,
                0.3f),

            // ==================== 彩蛋 ====================
            new SupportDef(
                "placebo",
                "安慰剂",
                "说明书写得像传奇。其实只有三个点。……对不起。",
                SupportCategory.Egg,
                SupportRarity.Egg,
                SupportEffect.Placebo,
                3f,
                1),
        };

        /// <summary>按 id 找一条支援；找不到返回 null（不抛异常）。</summary>
        public static SupportDef Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id)
                {
                    return All[i];
                }
            }

            return null;
        }

        /// <summary>开局白送的那一条。</summary>
        public static SupportDef FirstPick
        {
            get { return Get(FirstPickId); }
        }

        /// <summary>抽取权重：普通 60 / 稀有 30 / 传奇 10 / 双刃 20 / 彩蛋 12。</summary>
        public static float WeightOf(SupportRarity rarity)
        {
            switch (rarity)
            {
                case SupportRarity.Common:
                    return 60f;
                case SupportRarity.Rare:
                    return 30f;
                case SupportRarity.Legendary:
                    return 10f;
                case SupportRarity.Dual:
                    return 20f;
                case SupportRarity.Egg:
                    return 12f;
                default:
                    return 1f;
            }
        }
    }
}
