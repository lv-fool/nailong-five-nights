namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 可选阅读的生活残片。不是结局判定，也不记录阅读次数。
    /// 这些是第一版叙事草稿：不直接给角色贴心理标签。
    /// </summary>
    public static class StoryFragments
    {
        private static readonly string[] Nights =
        {
            "接人的车一辆辆走了。他把书包放在脚边，说再等一会儿。",
            "他说不用来接。走到街口，还是回头看了一眼。",
            "出门前，他嫌那句叮嘱太长。晚上热饭时，又想不起最后半句。",
            "报平安的消息写好了。发出去之前，他删掉了房租的事。",
            "旧书包、没吃完的饭、没发出的消息。换了几个住处，搬家时还是都带着。"
        };

        private static readonly string[] PhoneLines =
        {
            "小时候等人来接，别人总说他乖。也可能只是没人问他急不急。",
            "大了就得自己走——他们都这么说。可是那天雨那么大。",
            "有人说他离不开家，也有人嫌他不打电话。说的好像都是他。",
            "报喜不报忧，听着挺懂事的。那不太好的那些事，要说给谁听呢。",
            "以前的事就别提了——这话我也说过。可你搬家的时候，怎么什么都没扔。"
        };

        public static string Night(int night)
        {
            return night >= 0 && night < Nights.Length ? Nights[night] : string.Empty;
        }

        public static NaibaoCallLine Phone(int night)
        {
            return new NaibaoCallLine
            {
                Text = night >= 0 && night < PhoneLines.Length ? PhoneLines[night] : string.Empty,
                Pose = NaibaoPose.Bored,
                Id = string.Empty
            };
        }
    }
}
