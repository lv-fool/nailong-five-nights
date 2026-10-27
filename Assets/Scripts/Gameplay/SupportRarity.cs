namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 支援的稀有度 —— 只影响抽取权重，不影响能不能拿到。
    ///
    /// 纯逻辑枚举，不引用 UnityEngine。
    /// </summary>
    public enum SupportRarity
    {
        /// <summary>普通：常见，负责把这一局的底子撑住。</summary>
        Common = 0,

        /// <summary>稀有：改变节奏的那一条。</summary>
        Rare = 1,

        /// <summary>传奇：一局里见到就该记住的。</summary>
        Legendary = 2,

        /// <summary>双刃：效果强，代价写在卡上。</summary>
        Dual = 3,

        /// <summary>彩蛋：概率给，图一乐。</summary>
        Egg = 4,
    }
}
