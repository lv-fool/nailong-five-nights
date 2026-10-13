namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 四个敌人的固定代号。代码、配置表、素材名统一用这一套，别用别的拼法。
    ///
    /// 素材文件名 = char_&lt;代号&gt;_idle|stare|jumpscare.png，
    /// 跳杀视频 = char_&lt;代号&gt;_jumpscare.mp4。代号见 <see cref="EnemyIds.Code"/>。
    /// </summary>
    public enum EnemyId
    {
        /// <summary>牛来 —— 对应奇卡，摸右门，关门挡回去。</summary>
        Niulai = 0,

        /// <summary>袋鼠 —— 对应邦尼，摸左门，关门挡回去。</summary>
        Daishu = 1,

        /// <summary>噜噜 —— 怕光，从正门来；朝正前方按手电筒闪够几次就退走。</summary>
        Lulu = 2,

        /// <summary>奶龙 —— 不管就偷电，切监控看它一眼即止。</summary>
        Nailong = 3
    }

    /// <summary>玩家左右两侧。</summary>
    public enum Side
    {
        Left = 0,
        Right = 1
    }

    /// <summary>玩家当前看向哪边（转头的结果）。</summary>
    public enum ViewSide
    {
        Front = 0,
        Left = 1,
        Right = 2
    }

    public static class EnemyIds
    {
        /// <summary>四个敌人，按固定顺序。</summary>
        public static readonly EnemyId[] All =
        {
            EnemyId.Niulai,
            EnemyId.Daishu,
            EnemyId.Lulu,
            EnemyId.Nailong
        };

        /// <summary>代号字符串（拼音），用于素材名与存档。</summary>
        public static string Code(this EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Niulai:
                    return "niulai";
                case EnemyId.Daishu:
                    return "daishu";
                case EnemyId.Lulu:
                    return "lulu";
                case EnemyId.Nailong:
                    return "nailong";
                default:
                    return "unknown";
            }
        }
    }
}
