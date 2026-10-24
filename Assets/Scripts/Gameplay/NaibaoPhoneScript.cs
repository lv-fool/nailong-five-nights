namespace NailongFNAF.Gameplay
{
    /// <summary>电话里的一句：说什么、什么表情。</summary>
    public struct NaibaoCallLine
    {
        public string Text;
        public NaibaoPose Pose;

        /// <summary>这句的配音 id（对应 Assets/Resources/Voice/&lt;id&gt;.mp3）；没配音就是空串。</summary>
        public string Id;
    }

    /// <summary>
    /// 桌上那部手机里的奶包说什么。
    ///
    /// 设计上分三层：
    /// <list type="bullet">
    /// <item>第 1 夜（引导期）—— 她把这行当新手教学，顺便埋一句「我可能不在你这边」。</item>
    /// <item>第 2 夜起（手欠期）—— 开始拆台：把门说成墙、把看不见说成省心。</item>
    /// <item>越往后越不装 —— 她开始说不确定自己是不是在说谎（对应「或真或假」）。</item>
    /// </list>
    ///
    /// 纯数据 + 纯逻辑，不引用 UnityEngine：测试可以断言「第 1 夜和第 3 夜说的话不一样」。
    /// </summary>
    public static class NaibaoPhoneScript
    {
        /// <summary>一次通话最多几句。</summary>
        public const int MaxLinesPerCall = 4;

        private static readonly NaibaoCallLine[] GuideFirst =
        {
            Line("喂？听得见吗。我就在你桌上那部手机里——屏幕亮着的那个就是。", NaibaoPose.Guide),
            Line("头一天我陪你。记三件事：门关着的时候你什么都看不见；灯开着才看得见门外站着谁；正门那个小孩，只有手电筒赶得走。", NaibaoPose.Guide),
            Line("还有一句，你别当真：我不是从这边打给你的。", NaibaoPose.Peek),
        };

        private static readonly NaibaoCallLine[] GuideAgain =
        {
            Line("又打过来了？我说过的都记得吧。", NaibaoPose.Huffy),
            Line("记不住也没关系，反正今晚我也帮不上什么。", NaibaoPose.Smug),
        };

        private static readonly NaibaoCallLine[] NightEarly =
        {
            Line("今晚我不帮你看门了——我忙着呢。", NaibaoPose.Smug),
            Line("你要是害怕就把门关上。反正关上了，你也看不见外面。", NaibaoPose.Bored),
            Line("你们这儿的人真有意思，把一扇门当成一堵墙。", NaibaoPose.Huffy),
        };

        private static readonly NaibaoCallLine[] NightMiddle =
        {
            Line("今天我路过你们说的那扇门。门后面没有走廊，你知道吗？", NaibaoPose.Peek),
            Line("别问我是怎么知道的。我也不确定我是不是在说谎。", NaibaoPose.Bored),
            Line("——行，你忙吧。我就在这儿，反正我也只在这儿。", NaibaoPose.Guide),
        };

        private static readonly NaibaoCallLine[] NightLate =
        {
            Line("明天还来吗？", NaibaoPose.Peek),
            Line("我数了，你今晚关了很多次门。门是关不住的，它只会变旧。", NaibaoPose.Bored),
            Line("我们两边都在数着同一个人。你说，这算不算见过面。", NaibaoPose.Guide),
        };

        private static readonly NaibaoCallLine[] Repeat =
        {
            Line("说过了。你又要我再讲一遍？", NaibaoPose.Huffy),
            Line("……好吧。左边那个是袋鼠，右边那个是牛来，正门是小孩。名字我记不清，反正都是冲你来的。", NaibaoPose.Bored),
        };

        /// <summary>
        /// 取这次通话的台词。<paramref name="night"/> 是第几夜（0 起），
        /// <paramref name="callIndex"/> 是这一夜第几次接通（0 起）。
        /// </summary>
        /// <summary>
        /// 台词表（json 优先，缺了就用 <see cref="NaibaoCallLines"/> 里的默认）。
        ///
        /// 静态缓存：一次通话要取整组词，不能每次读 Resources。
        /// 编辑器窗口保存后会调 <see cref="InvalidateLinesCache"/>。
        /// </summary>
        private static NaibaoCallLines _lines;
        private static bool _linesLoaded;

        public static NaibaoCallLines Lines
        {
            get
            {
                if (_linesLoaded)
                {
                    return _lines;
                }

                _linesLoaded = true;

                var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>(NaibaoCallLines.ResourcePath);

                if (asset != null)
                {
                    NaibaoCallLines parsed;
                    if (NaibaoCallLines.TryParse(asset.text, out parsed))
                    {
                        _lines = parsed;
                        return _lines;
                    }
                }

                _lines = new NaibaoCallLines();   // 纯默认
                return _lines;
            }
        }

        /// <summary>编辑器里改了 json 之后让缓存失效（窗口保存时调）。</summary>
        public static void InvalidateLinesCache()
        {
            _lines = null;
            _linesLoaded = false;
        }

        /// <summary>
        /// 取这次通话的台词。<paramref name="night"/> 是第几夜（0 起），
        /// <paramref name="callIndex"/> 是这一夜第几次接通（0 起）。
        ///
        /// **台词内容从 NaibaoCallLines 读**（「奶龙 / 13. 编辑奶包通话台词」维护），
        /// 这个函数只负责「哪一夜该用哪一组」。
        /// </summary>
        public static NaibaoCallLine[] ForCall(int night, bool guide, int callIndex)
        {
            return ForCall(night, guide, callIndex, false);
        }

        /// <summary>
        /// 取这次通话的台词，**带自定义夜标记**。
        ///
        /// ⚠️ 自定义夜必须走这个重载：那一局没有「第几夜」的概念，
        /// <c>night</c> 保留的是上一次进入的夜晚（1~5 随机），
        /// 照它选档会让台词随机落到 early / middle / late —— 玩家听着莫名其妙。
        /// </summary>
        public static NaibaoCallLine[] ForCall(int night, bool guide, int callIndex, bool custom)
        {
            NaibaoCallLines table = Lines;

            // 自定义夜优先：它不属于任何一夜
            if (custom)
            {
                return callIndex <= 0
                    ? Tag("call_custom_", table.CallLinesFor(NaibaoCallLines.KeyCustom))
                    : Tag("call_repeat_", table.CallLinesFor(NaibaoCallLines.KeyRepeat));
            }

            if (guide)
            {
                return callIndex <= 0
                    ? Tag("call_guide_first_", table.CallLinesFor(NaibaoCallLines.KeyGuideFirst))
                    : Tag("call_guide_again_", table.CallLinesFor(NaibaoCallLines.KeyGuideAgain));
            }

            // 同一夜第二次之后就不给新词了：剧情是慢慢给的，不是一次说完
            if (callIndex >= 1)
            {
                return Tag("call_repeat_", table.CallLinesFor(NaibaoCallLines.KeyRepeat));
            }

            if (night <= 2)
            {
                return Tag("call_early_", table.CallLinesFor(NaibaoCallLines.KeyNightEarly));
            }

            if (night <= 4)
            {
                return Tag("call_mid_", table.CallLinesFor(NaibaoCallLines.KeyNightMiddle));
            }

            return Tag("call_late_", table.CallLinesFor(NaibaoCallLines.KeyNightLate));
        }

        /// <summary>给一组台词按顺序贴上配音 id（<c>前缀 + 序号</c>，序号从 1 开始）。</summary>
        private static NaibaoCallLine[] Tag(string prefix, NaibaoCallLine[] lines)
        {
            if (lines == null)
            {
                return new NaibaoCallLine[0];
            }

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i].Id = prefix + (i + 1);
            }

            return lines;
        }

        private static NaibaoCallLine Line(string text, NaibaoPose pose)
        {
            var line = new NaibaoCallLine();
            line.Text = text;
            line.Pose = pose;
            return line;
        }
    }

    /// <summary>
    /// 一次通话：把一段台词按顺序念完。
    /// 只管「念到第几句、有没有念完」，不碰 UI 也不碰电 —— 那些由 GameManager 接。
    /// </summary>
    public class NaibaoCall
    {
        private readonly NaibaoCallLine[] _lines;
        private int _index;

        public NaibaoCall(NaibaoCallLine[] lines)
        {
            _lines = lines ?? new NaibaoCallLine[0];
            _index = 0;
        }

        /// <summary>这一次通话一共几句。</summary>
        public int Count
        {
            get { return _lines.Length; }
        }

        /// <summary>现在是第几句（0 起）。念完之后等于 <see cref="Count"/>。</summary>
        public int Index
        {
            get { return _index; }
        }

        /// <summary>还开着吗。</summary>
        public bool Ended
        {
            get { return _index >= _lines.Length; }
        }

        /// <summary>现在这一句（念完时返回空句）。</summary>
        public NaibaoCallLine Current
        {
            get
            {
                if (Ended)
                {
                    var empty = new NaibaoCallLine();
                    empty.Text = string.Empty;
                    empty.Pose = NaibaoPose.Bored;
                    return empty;
                }

                return _lines[_index];
            }
        }

        /// <summary>翻到下一句。翻完返回 false（此时 <see cref="Ended"/> 为真）。</summary>
        public bool Advance()
        {
            if (_index < _lines.Length)
            {
                _index++;
            }

            return !Ended;
        }
    }
}
