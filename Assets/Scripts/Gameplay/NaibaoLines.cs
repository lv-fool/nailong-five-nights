using System;
using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 奶包**所有**台词的可编辑副本 —— 由编辑器窗口
    /// 「奶龙 / 12. 编辑奶包台词」读写，落 <c>Assets/Resources/naibao_lines.json</c>。
    ///
    /// 分六组，对应她出场的六种场合：
    /// <list type="bullet">
    /// <item><b>Guide</b>    第 1 夜引导五句（顺序固定，按下标取）</item>
    /// <item><b>Usage</b>    偷偷加耗电时的话（随机）</item>
    /// <item><b>Monitor</b>  抢监控时的话（随机）</item>
    /// <item><b>LightOff</b> 关你灯时的话（随机）</item>
    /// <item><b>Taunt</b>    纯嘲讽（随机）</item>
    /// <item><b>Apology</b>  因她而死时的道歉（按故障分三条，顺序固定）</item>
    /// </list>
    ///
    /// 手机通话那六组（GuideFirst / GuideAgain / NightEarly / NightMiddle /
    /// NightLate / Repeat）带**表情**，结构比纯文本复杂，暂不并进来 ——
    /// 它们留在 <see cref="NaibaoPhoneScript"/> 里，改台词仍要动代码。
    ///
    /// ⚠️ **文件缺失 / 某组没写 / 某组为空 → 自动退回写死的默认值**，
    /// 所以删掉这个 json 也不会让奶包变成哑巴。
    /// </summary>
    [Serializable]
    public sealed class NaibaoLines
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "naibao_lines";

        /// <summary>六组的键（存 JSON 用，别改 —— 改了旧存档读不回来）。</summary>
        public const string KeyGuide = "guide";
        public const string KeyUsage = "usage";
        public const string KeyMonitor = "monitor";
        public const string KeyLightOff = "lightoff";
        public const string KeyTaunt = "taunt";
        public const string KeyApology = "apology";

        /// <summary>所有键，按界面显示顺序。</summary>
        public static readonly string[] Keys =
        {
            KeyGuide, KeyUsage, KeyMonitor, KeyLightOff, KeyTaunt, KeyApology
        };

        /// <summary>一组的台词。</summary>
        [Serializable]
        public class Group
        {
            public string Key;
            public List<string> Lines = new List<string>();
        }

        public List<Group> Groups = new List<Group>();

        /// <summary>键 → 中文标题（编辑器窗口用）。</summary>
        public static string TitleOf(string key)
        {
            switch (key)
            {
                case KeyGuide: return "第 1 夜引导（五句，固定顺序）";
                case KeyUsage: return "偷偷加耗电（随机）";
                case KeyMonitor: return "抢监控（随机）";
                case KeyLightOff: return "关你的灯（随机）";
                case KeyTaunt: return "纯嘲讽（随机）";
                case KeyApology: return "道歉（三条，按故障固定顺序）";
                default: return key;
            }
        }

        /// <summary>键 → 这组的说明。</summary>
        public static string HintOf(string key)
        {
            switch (key)
            {
                case KeyGuide:
                    return "第 18 / 72 / 132 / 200 / 274 秒依次弹出。删了几条后面的会顺延，别删。";
                case KeyApology:
                    return "依次对应：抢监控 / 关灯 / 偷加耗电。顺序不能乱。";
                default:
                    return "每次出手从那几句里随机抽一句。";
            }
        }

        // ---------------------------------------------------------------
        // 默认文案（兜底）
        // ---------------------------------------------------------------

        /// <summary>某组的默认台词 —— 和写死在代码里的那套完全一致。</summary>
        public static string[] DefaultLines(string key)
        {
            switch (key)
            {
                case KeyGuide:
                    return new[]
                    {
                        "我是奶包，这屋里最靠谱的那个。先看监控吧——鼠标压到屏幕最底下，它就升起来。",
                        "两边墙上那两块板子，上面是门，下面是灯。门关着是看不见人的，得先把灯打开才看得见。",
                        "正门那边有个小孩，只有手电筒能赶走。按住屏幕中间那个 FLASHLIGHT，别松。",
                        "左下角那排格子是现在的用电量。格子越多，电掉得越快，你自己看着办。",
                        "好了，该说的我都说了。撑到六点，我在这儿看着你。",
                    };

                case KeyUsage:
                    return new[]
                    {
                        "我看你电还挺多的，替你多开了一档。",
                        "别谢我，功率我帮你调高了。",
                    };

                case KeyMonitor:
                    return new[]
                    {
                        "这个机位我替你看看……哎，什么都没有。",
                        "诶，我好像按到什么了？",
                    };

                case KeyLightOff:
                    return new[]
                    {
                        "你灯开太久了，帮你关了啊。",
                        "省点电吧，我关你一盏。",
                    };

                case KeyTaunt:
                    return new[]
                    {
                        "你关门的样子，有点像在给谁道歉。",
                        "我在这儿看着呢，别紧张。",
                        "其实你可以不用这么用力的。",
                    };

                case KeyApology:
                    return new[]
                    {
                        "对不起，刚才监控是我按住的。我想替你看一眼，可我什么都没看见。",
                        "对不起，灯是我关的。我以为你不需要看那边。",
                        "对不起，电是我偷偷调高的。我以为还够用。",
                    };

                default:
                    return new string[0];
            }
        }

        // ---------------------------------------------------------------
        // 取词
        // ---------------------------------------------------------------

        /// <summary>取某组的台词（json 优先，没有就默认）。</summary>
        public List<string> LinesFor(string key)
        {
            if (Groups != null)
            {
                for (int i = 0; i < Groups.Count; i++)
                {
                    var g = Groups[i];
                    if (g != null && g.Key == key && g.Lines != null && g.Lines.Count > 0)
                    {
                        return g.Lines;
                    }
                }
            }

            return new List<string>(DefaultLines(key));
        }

        /// <summary>写下标对应的那句；越界返回空串（调用方按「没这句」处理）。</summary>
        public string LineAt(string key, int index)
        {
            List<string> lines = LinesFor(key);

            if (index < 0 || index >= lines.Count)
            {
                return string.Empty;
            }

            return lines[index] ?? string.Empty;
        }

        /// <summary>从某组随机抽一句（空组返回空串）。</summary>
        public string RandomFrom(string key, IRandomSource rng)
        {
            List<string> lines = LinesFor(key);

            var usable = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    usable.Add(lines[i]);
                }
            }

            if (usable.Count == 0)
            {
                return string.Empty;
            }

            if (rng == null)
            {
                return usable[UnityEngine.Random.Range(0, usable.Count)];
            }

            return usable[rng.Range(0, usable.Count)];
        }

        /// <summary>写一整组（编辑器窗口用）。</summary>
        public void SetGroup(string key, List<string> lines)
        {
            if (Groups == null)
            {
                Groups = new List<Group>();
            }

            var copy = lines != null ? new List<string>(lines) : new List<string>();

            for (int i = 0; i < Groups.Count; i++)
            {
                if (Groups[i] != null && Groups[i].Key == key)
                {
                    Groups[i].Lines = copy;
                    return;
                }
            }

            Groups.Add(new Group { Key = key, Lines = copy });
        }

        /// <summary>六组全部填成默认值。</summary>
        public void ResetToDefaults()
        {
            Groups = new List<Group>();

            foreach (var key in Keys)
            {
                Groups.Add(new Group
                {
                    Key = key,
                    Lines = new List<string>(DefaultLines(key))
                });
            }
        }

        // ---------------------------------------------------------------
        // 存读
        // ---------------------------------------------------------------

        private const string Header = "{\"groups\":[";

        /// <summary>序列化成一行 JSON。</summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Header);

            bool firstGroup = true;

            if (Groups != null)
            {
                foreach (var g in Groups)
                {
                    if (g == null || string.IsNullOrEmpty(g.Key))
                    {
                        continue;
                    }

                    if (!firstGroup)
                    {
                        sb.Append(',');
                    }

                    firstGroup = false;

                    sb.Append("{\"key\":\"").Append(Escape(g.Key)).Append("\",\"lines\":[");

                    bool firstLine = true;

                    if (g.Lines != null)
                    {
                        foreach (var line in g.Lines)
                        {
                            if (line == null)
                            {
                                continue;
                            }

                            if (!firstLine)
                            {
                                sb.Append(',');
                            }

                            firstLine = false;

                            sb.Append('"').Append(Escape(line)).Append('"');
                        }
                    }

                    sb.Append("]}");
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>从 JSON 解析。格式不对返回 false（调用方退回默认）。</summary>
        public static bool TryParse(string raw, out NaibaoLines result)
        {
            result = null;

            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            try
            {
                var data = new NaibaoLines();

                int i = 0;
                while (i < raw.Length)
                {
                    int keyAt = raw.IndexOf("\"key\"", i, StringComparison.Ordinal);
                    if (keyAt < 0)
                    {
                        break;
                    }

                    string key = ReadStringAfterKey(raw, keyAt, out int keyEnd);
                    if (key == null)
                    {
                        i = keyAt + 5;
                        continue;
                    }

                    int linesAt = raw.IndexOf("\"lines\"", keyEnd, StringComparison.Ordinal);
                    if (linesAt < 0)
                    {
                        break;
                    }

                    int arrayOpen = raw.IndexOf('[', linesAt);
                    if (arrayOpen < 0)
                    {
                        break;
                    }

                    var lines = new List<string>();
                    int cursor = arrayOpen + 1;

                    while (cursor < raw.Length)
                    {
                        int q = raw.IndexOf('"', cursor);
                        if (q < 0)
                        {
                            break;
                        }

                        string line = ReadQuoted(raw, q, out int closing);
                        if (line == null)
                        {
                            break;
                        }

                        lines.Add(line);

                        int next = closing;
                        while (next < raw.Length && char.IsWhiteSpace(raw[next]))
                        {
                            next++;
                        }

                        if (next >= raw.Length)
                        {
                            cursor = raw.Length;
                            break;
                        }

                        if (raw[next] == ',')
                        {
                            cursor = next + 1;
                            continue;
                        }

                        cursor = next;
                        break;
                    }

                    data.SetGroup(key, lines);
                    i = cursor;
                }

                result = data;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadStringAfterKey(string raw, int keyIndex, out int endIndex)
        {
            endIndex = keyIndex + 5;

            int colon = raw.IndexOf(':', keyIndex);
            if (colon < 0)
            {
                return null;
            }

            int open = raw.IndexOf('"', colon + 1);
            if (open < 0)
            {
                return null;
            }

            return ReadQuoted(raw, open, out endIndex);
        }

        private static string ReadQuoted(string raw, int quoteIndex, out int endIndex)
        {
            endIndex = quoteIndex + 1;

            var sb = new System.Text.StringBuilder();
            int i = quoteIndex + 1;

            while (i < raw.Length)
            {
                char c = raw[i];

                if (c == '\\' && i + 1 < raw.Length)
                {
                    char n = raw[i + 1];

                    if (n == 'n') { sb.Append('\n'); }
                    else if (n == 't') { sb.Append('\t'); }
                    else if (n == 'r') { sb.Append('\r'); }
                    else { sb.Append(n); }

                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    endIndex = i + 1;
                    return sb.ToString();
                }

                sb.Append(c);
                i++;
            }

            return null;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(s.Length + 8);

            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }
    }
}
