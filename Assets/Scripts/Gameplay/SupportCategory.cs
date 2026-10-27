namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 支援的类别 —— 只用于界面分组和「加量」那条的禁选规则，不影响抽取权重。
    ///
    /// 老板 2026-10-26 给的分类表：电力 / 判定 / 敌人 / 情报 / 保险 / 时间 /
    /// 支援（他自己叫「海克斯」）/ 双刃 / 彩蛋。
    ///
    /// 纯逻辑枚举，不引用 UnityEngine。
    /// </summary>
    public enum SupportCategory
    {
        /// <summary>电力：上限、掉电速度、各处耗电。</summary>
        Power = 0,

        /// <summary>判定：生效窗口、致命改非致命、过点暂停。</summary>
        Judgement = 1,

        /// <summary>敌人：移动效率、门口停留、黑化怪的规则。</summary>
        Enemy = 2,

        /// <summary>情报：监控、机位、弱点高亮、她提醒你。</summary>
        Info = 3,

        /// <summary>保险：断电兜底、免死。</summary>
        Insurance = 4,

        /// <summary>时间：12 小时这个盘子本身的速度。</summary>
        Time = 5,

        /// <summary>支援机制本身：四选一、重抽、整体加成。</summary>
        Draw = 6,

        /// <summary>双刃：强得很明显，代价也写在同一张卡上。</summary>
        Risk = 7,

        /// <summary>彩蛋：写着好玩的那一条。</summary>
        Egg = 8,
    }
}
