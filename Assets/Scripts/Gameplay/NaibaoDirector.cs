using System.Collections.Generic;

namespace NailongFNAF.Gameplay
{
    /// <summary>奶包的表情（对应 <c>Assets/Resources/Naibao/</c> 下的五张立绘）。</summary>
    public enum NaibaoPose
    {
        /// <summary>从手机屏里探身、皱眉嫌弃 —— 第一次登场。</summary>
        Peek = 0,

        /// <summary>微笑、手做「请」—— 教学与引导。</summary>
        Guide = 1,

        /// <summary>叉腰撇嘴 —— 不耐烦（被无视时）。</summary>
        Huffy = 2,

        /// <summary>斜眼坏笑 —— 捣完乱得意。</summary>
        Smug = 3,

        /// <summary>半睁眼垮脸 —— 嘲讽。</summary>
        Bored = 4,
    }

    /// <summary>奶包这一下干了什么。</summary>
    public enum NaibaoAction
    {
        /// <summary>什么都不干（占位，不会出现在队列里）。</summary>
        None = 0,

        /// <summary>纯说话 —— 引导期全是这种。</summary>
        Line = 1,

        /// <summary>偷偷多开一档用电：使用等级临时 +1。</summary>
        UsageSpike = 2,

        /// <summary>抢监控：把监控硬升起来几秒（玩家自己收起也没用）。</summary>
        MonitorGrab = 3,

        /// <summary>关掉玩家正开着的一盏灯。</summary>
        LightOff = 4,
    }

    /// <summary>奶包的一次出场：说什么、什么表情、顺手干了什么。</summary>
    public struct NaibaoCue
    {
        public NaibaoAction Action;
        public NaibaoPose Pose;
        public string Line;

        /// <summary>持续秒数（只有 <see cref="NaibaoAction.UsageSpike"/> 与
        /// <see cref="NaibaoAction.MonitorGrab"/> 用得上）。</summary>
        public float Seconds;
    }

    /// <summary>
    /// 奶包：豆包那个「很热心、帮倒忙」的外部世界。
    ///
    /// 定位（和用户定过的）：
    /// <list type="bullet">
    /// <item>**第 1 夜只做引导** —— 到点弹一句字幕，不碰任何设备（像戴夫）。</item>
    /// <item>**第 2 夜起只做手欠** —— 加一档耗电 / 抢监控 / 关你的灯 / 纯嘲讽。</item>
    /// <item>**她不会杀你，也不偷电** —— 不碰门、不改敌人路线。惩罚都是「烦」，不是「死」。</item>
    /// <item>永夜模式暂时不接她（用户说先搁置）。</item>
    /// </list>
    ///
    /// 纯逻辑类，不引用 UnityEngine：时间推进靠外部喂 dt，随机靠
    /// <see cref="IRandomSource"/>，所以能脱离 Unity 断言「第 18 秒必说第一句」。
    /// </summary>
    public class NaibaoDirector
    {
        /// <summary>引导期第一句的延迟（秒）。给玩家一点时间先看清屋子。</summary>
        public const float GuideFirstDelay = 18f;

        /// <summary>引导期五句话分别在什么时候说。</summary>
        public static readonly float[] GuideTimes = { 18f, 72f, 132f, 200f, 274f };

        /// <summary>捣乱期第一次出手的延迟（秒）。</summary>
        public const float MischiefFirstDelay = 30f;

        /// <summary>两次捣乱之间最短隔多少秒。</summary>
        public const float MischiefMinGap = 38f;

        /// <summary>两次捣乱之间最长隔多少秒。</summary>
        public const float MischiefMaxGap = 72f;

        /// <summary>「偷偷多开一档用电」持续多久。</summary>
        public const float UsageSpikeSeconds = 8f;

        /// <summary>「抢监控」持续多久。</summary>
        public const float MonitorGrabSeconds = 2.6f;

        /// <summary>引导期的台词，按 <see cref="GuideTimes"/> 的顺序。</summary>
        public static readonly string[] GuideLines =
        {
            "我是奶包，这屋里最靠谱的那个。先看监控吧——鼠标压到屏幕最底下，它就升起来。",
            "两边墙上那两块板子，上面是门，下面是灯。门关着是看不见人的，得先把灯打开才看得见。",
            "正门那边有个小孩，只有手电筒能赶走。按住屏幕中间那个 FLASHLIGHT，别松。",
            "左下角那排格子是现在的用电量。格子越多，电掉得越快，你自己看着办。",
            "好了，该说的我都说了。撑到六点，我在这儿看着你。",
        };

        private static readonly string[] UsageSpikeLines =
        {
            "我看你电还挺多的，替你多开了一档。",
            "别谢我，功率我帮你调高了。",
        };

        private static readonly string[] MonitorGrabLines =
        {
            "这个机位我替你看看……哎，什么都没有。",
            "诶，我好像按到什么了？",
        };

        private static readonly string[] LightOffLines =
        {
            "你灯开太久了，帮你关了啊。",
            "省点电吧，我关你一盏。",
        };

        private static readonly string[] TauntLines =
        {
            "你关门的样子，有点像在给谁道歉。",
            "我在这儿看着呢，别紧张。",
            "其实你可以不用这么用力的。",
        };

        private readonly bool _guide;
        private readonly IRandomSource _random;
        private readonly Queue<NaibaoCue> _pending = new Queue<NaibaoCue>();

        private float _elapsed;
        private float _nextMischiefAt;
        private int _guideStep;
        private int _mischiefCount;
        private bool _finished;

        /// <summary>
        /// 建一个奶包。<paramref name="guide"/> = true 时是第 1 夜的引导模式，
        /// 否则是手欠模式。<paramref name="random"/> 传 null 会自动用系统随机。
        /// </summary>
        public NaibaoDirector(bool guide, IRandomSource random)
        {
            _guide = guide;
            _random = random ?? new SystemRandomSource();
            _nextMischiefAt = MischiefFirstDelay;
        }

        /// <summary>是不是引导模式。</summary>
        public bool IsGuide
        {
            get { return _guide; }
        }

        /// <summary>已经捣乱了几次（引导模式下恒为 0）。</summary>
        public int MischiefCount
        {
            get { return _mischiefCount; }
        }

        /// <summary>这一局她还有没有戏（引导说完 / 不会说完，手欠永不结束）。</summary>
        public bool IsFinished
        {
            get { return _finished; }
        }

        /// <summary>从开局到现在过了多少秒。</summary>
        public float Elapsed
        {
            get { return _elapsed; }
        }

        /// <summary>推进时间。到点就把该出的场排进队列，等外面用 <see cref="TryTakeCue"/> 取。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || _finished)
            {
                return;
            }

            _elapsed += deltaSeconds;

            if (_guide)
            {
                TickGuide();
                return;
            }

            if (_elapsed >= _nextMischiefAt)
            {
                EmitMischief();
                _mischiefCount++;
                _nextMischiefAt = _elapsed + NextGap();
            }
        }

        /// <summary>
        /// 下一次出手的间隔。夹在 [MinGap, MaxGap] 里是防御性的写法：
        /// <see cref="IRandomSource"/> 的约定是「返回 [min, max) 之间」，
        /// 但测试用的固定骰子会直接把它要的值丢回来，夹一下两边都稳。
        /// </summary>
        private int NextGap()
        {
            int gap = _random.Range((int)MischiefMinGap, (int)MischiefMaxGap + 1);
            if (gap < (int)MischiefMinGap)
            {
                gap = (int)MischiefMinGap;
            }

            if (gap > (int)MischiefMaxGap)
            {
                gap = (int)MischiefMaxGap;
            }

            return gap;
        }

        /// <summary>取一次出场。没轮到就返回 false。</summary>
        public bool TryTakeCue(out NaibaoCue cue)
        {
            if (_pending.Count == 0)
            {
                cue = default(NaibaoCue);
                return false;
            }

            cue = _pending.Dequeue();
            return true;
        }

        /// <summary>清空（重开一夜时用）。</summary>
        public void Reset()
        {
            _pending.Clear();
            _elapsed = 0f;
            _guideStep = 0;
            _mischiefCount = 0;
            _finished = false;
            _nextMischiefAt = MischiefFirstDelay;
        }

        private void TickGuide()
        {
            while (_guideStep < GuideTimes.Length && _elapsed >= GuideTimes[_guideStep])
            {
                _pending.Enqueue(new NaibaoCue
                {
                    Action = NaibaoAction.Line,
                    Pose = NaibaoPose.Guide,
                    Line = GuideLines[_guideStep],
                    Seconds = 0f,
                });

                _guideStep++;
            }

            if (_guideStep >= GuideTimes.Length)
            {
                _finished = true;
            }
        }

        private void EmitMischief()
        {
            int pick = _random.Range(0, 4);
            if (pick < 0 || pick > 3)
            {
                pick = 0;
            }

            int index = _mischiefCount;

            switch (pick)
            {
                case 0:
                    _pending.Enqueue(new NaibaoCue
                    {
                        Action = NaibaoAction.UsageSpike,
                        Pose = NaibaoPose.Smug,
                        Line = Pick(UsageSpikeLines, index),
                        Seconds = UsageSpikeSeconds,
                    });
                    break;

                case 1:
                    _pending.Enqueue(new NaibaoCue
                    {
                        Action = NaibaoAction.MonitorGrab,
                        Pose = NaibaoPose.Peek,
                        Line = Pick(MonitorGrabLines, index),
                        Seconds = MonitorGrabSeconds,
                    });
                    break;

                case 2:
                    _pending.Enqueue(new NaibaoCue
                    {
                        Action = NaibaoAction.LightOff,
                        Pose = NaibaoPose.Huffy,
                        Line = Pick(LightOffLines, index),
                        Seconds = 0f,
                    });
                    break;

                default:
                    _pending.Enqueue(new NaibaoCue
                    {
                        Action = NaibaoAction.Line,
                        Pose = NaibaoPose.Bored,
                        Line = Pick(TauntLines, index),
                        Seconds = 0f,
                    });
                    break;
            }
        }

        private static string Pick(string[] lines, int index)
        {
            if (lines == null || lines.Length == 0)
            {
                return string.Empty;
            }

            return lines[index % lines.Length];
        }
    }
}
