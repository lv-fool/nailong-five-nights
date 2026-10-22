using System;
using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 手机通话里奶包说过的**所有**台词 —— 每组一句「文本 + 表情」。
    ///
    /// 由编辑器窗口「奶龙 / 13. 编辑奶包通话台词」读写，
    /// 落 <c>Assets/Resources/naibao_call_lines.json</c>。
    ///
    /// 六组对应六种通话场合（和 <see cref="NaibaoPhoneScript.ForCall"/> 一一对应）：
    /// <list type="bullet">
    /// <item><b>guide_first</b>  第 1 夜第一次接通（新手教学，三句）</item>
    /// <item><b>guide_again</b>  第 1 夜再打（两句）</item>
    /// <item><b>night_early</b>  第 2~3 夜（三句）</item>
    /// <item><b>night_middle</b> 第 4~5 夜（三句）</item>
    /// <item><b>night_late</b>   第 6 夜之后（三句）</item>
    /// <item><b>repeat</b>       同一夜第二次之后（两句）</item>
    /// </list>
    ///
    /// ⚠️ **文件缺失 / 某组没写 / 某组为空 → 自动退回写死的默认值**，
    /// 所以删掉这个 json 也不会让手机变成哑巴。
    /// </summary>
    [Serializable]
    public sealed class NaibaoCallLines
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "naibao_call_lines";

        public const string KeyGuideFirst = "guide_first";
        public const string KeyGuideAgain = "guide_again";
        public const string KeyNightEarly = "night_early";
        public const string KeyNightMiddle = "night_middle";
        public const string KeyNightLate = "night_late";
        public const string KeyRepeat = "repeat";

        /// <summary>所有键，按界面显示顺序。</summary>
        public static readonly string[] Keys =
        {
            KeyGuideFirst, KeyGuideAgain, KeyNightEarly,
            KeyNightMiddle, KeyNightLate, KeyRepeat
        };

        /// <summary>一句：说什么、什么表情。</summary>
        [Serializable]
        public class Line
        {
            public string Text;
            public NaibaoPose Pose;
        }

        /// <summary>一组。</summary>
        [Serializable]
        public class Group
        {
            public string Key;
            public List<Line> Lines = new List<Line>();
        }

        public List<Group> Groups = new List<Group>();

        /// <summary>键 → 中文标题（编辑器窗口用）。</summary>
        public static string TitleOf(string key)
        {
            switch (key)
            {
                case KeyGuideFirst: return "第 1 夜 · 第一次接通（教学）";
                case KeyGuideAgain: return "第 1 夜 · 再打过来";
                case KeyNightEarly: return "第 2~3 夜";
                case KeyNightMiddle: return "第 4~5 夜";
                case KeyNightLate: return "第 6 夜之后";
                case KeyRepeat: return "同一夜第二次之后（短版）";
                default: return key;
            }
        }

        /// <summary>键 → 这组的说明。</summary>
        public static string HintOf(string key)
        {
            switch (key)
            {
                case KeyGuideFirst:
                    return "新手教学 —— 解释手机、门灯、手电筒，最后埋一句「我不是从这边打给你的」。";
                case KeyRepeat:
                    return "同一夜已经打过一次，不再重复剧情，只给短版。";
                default:
                    return "这一夜第一次接通时按顺序念完。";
            }
        }

        /// <summary>表情的中文名（编辑器下拉框用）。</summary>
        public static string PoseName(NaibaoPose pose)
        {
            switch (pose)
            {
                case NaibaoPose.Peek: return "探身（皱眉嫌弃）";
                case NaibaoPose.Guide: return "微笑（手做「请」）";
                case NaibaoPose.Huffy: return "撇嘴（叉腰）";
                case NaibaoPose.Smug: return "坏笑（斜眼）";
                case NaibaoPose.Bored: return "垮脸（半睁眼）";
                default: return pose.ToString();
            }
        }

        /// <summary>所有可选表情，顺序和下拉框一致。</summary>
        public static readonly NaibaoPose[] AllPoses =
        {
            NaibaoPose.Peek, NaibaoPose.Guide, NaibaoPose.Huffy,
            NaibaoPose.Smug, NaibaoPose.Bored
        };

        // ---------------------------------------------------------------
        // 默认文案（兜底）—— 和 NaibaoPhoneScript 里写死在代码中的完全一致
        // ---------------------------------------------------------------

        /// <summary>某组的默认台词。</summary>
        public static Line[] DefaultLines(string key)
        {
            switch (key)
            {
                case KeyGuideFirst:
                    return new[]
                    {
                        Make("喂？听得见吗。我就在你桌上那部手机里——屏幕亮着的那个就是。", NaibaoPose.Guide),
                        Make("头一天我陪你。记三件事：门关着的时候你什么都看不见；灯开着才看得见门外站着谁；正门那个小孩，只有手电筒赶得走。", NaibaoPose.Guide),
                        Make("还有一句，你别当真：我不是从这边打给你的。", NaibaoPose.Peek),
                    };

                case KeyGuideAgain:
                    return new[]
                    {
                        Make("又打过来了？我说过的都记得吧。", NaibaoPose.Huffy),
                        Make("记不住也没关系，反正今晚我也帮不上什么。", NaibaoPose.Smug),
                    };

                case KeyNightEarly:
                    return new[]
                    {
                        Make("今晚我不帮你看门了——我忙着呢。", NaibaoPose.Smug),
                        Make("你要是害怕就把门关上。反正关上了，你也看不见外面。", NaibaoPose.Bored),
                        Make("你们这儿的人真有意思，把一扇门当成一堵墙。", NaibaoPose.Huffy),
                    };

                case KeyNightMiddle:
                    return new[]
                    {
                        Make("今天我路过你们说的那扇门。门后面没有走廊，你知道吗？", NaibaoPose.Peek),
                        Make("别问我是怎么知道的。我也不确定我是不是在说谎。", NaibaoPose.Bored),
                        Make("——行，你忙吧。我就在这儿，反正我也只在这儿。", NaibaoPose.Guide),
                    };

                case KeyNightLate:
                    return new[]
                    {
                        Make("明天还来吗？", NaibaoPose.Peek),
                        Make("我数了，你今晚关了很多次门。门是关不住的，它只会变旧。", NaibaoPose.Bored),
                        Make("我们两边都在数着同一个人。你说，这算不算见过面。", NaibaoPose.Guide),
                    };

                case KeyRepeat:
                    return new[]
                    {
                        Make("说过了。你又要我再讲一遍？", NaibaoPose.Huffy),
                        Make("……好吧。左边那个是袋鼠，右边那个是牛来，正门是小孩。名字我记不清，反正都是冲你来的。", NaibaoPose.Bored),
                    };

                default:
                    return new Line[0];
            }
        }

        private static Line Make(string text, NaibaoPose pose)
        {
            return new Line { Text = text, Pose = pose };
        }

        // ---------------------------------------------------------------
        // 取词
        // ---------------------------------------------------------------

        /// <summary>取某组的台词（json 优先，没有就默认）。</summary>
        public List<Line> LinesFor(string key)
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

            return new List<Line>(DefaultLines(key));
        }

        /// <summary>
        /// 转成运行时用的 <see cref="NaibaoCallLine"/> 数组。
        /// 空文本会被跳过（编辑器里留的空行不该占一次通话）。
        /// </summary>
        public NaibaoCallLine[] CallLinesFor(string key)
        {
            List<Line> source = LinesFor(key);

            var result = new List<NaibaoCallLine>(source.Count);

            for (int i = 0; i < source.Count; i++)
            {
                var line = source[i];

                if (line == null || string.IsNullOrEmpty(line.Text))
                {
                    continue;
                }

                var call = new NaibaoCallLine();
                call.Text = line.Text;
                call.Pose = line.Pose;
                result.Add(call);
            }

            return result.ToArray();
        }

        /// <summary>写一整组（编辑器窗口用）。</summary>
        public void SetGroup(string key, List<Line> lines)
        {
            if (Groups == null)
            {
                Groups = new List<Group>();
            }

            var copy = lines != null ? new List<Line>(lines) : new List<Line>();

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
                    Lines = new List<Line>(DefaultLines(key))
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

                            sb.Append("{\"text\":\"").Append(Escape(line.Text ?? string.Empty))
                              .Append("\",\"pose\":\"").Append(line.Pose.ToString())
                              .Append("\"}");
                        }
                    }

                    sb.Append("]}");
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>从 JSON 解析。格式不对返回 false（调用方退回默认）。</summary>
        public static bool TryParse(string raw, out NaibaoCallLines result)
        {
            result = null;

            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            try
            {
                var data = new NaibaoCallLines();

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

                    int arrayClose = FindArrayEnd(raw, arrayOpen);
                    if (arrayClose < 0)
                    {
                        break;
                    }

                    string body = raw.Substring(arrayOpen + 1, arrayClose - arrayOpen - 1);
                    data.SetGroup(key, ParseLines(body));

                    i = arrayClose + 1;
                }

                result = data;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>找和 <paramref name="openIndex"/>（指向 '['）配对的 ']'。</summary>
        private static int FindArrayEnd(string raw, int openIndex)
        {
            int depth = 0;
            bool inString = false;

            for (int i = openIndex; i < raw.Length; i++)
            {
                char c = raw[i];

                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == '"') { inString = false; }
                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == '[') { depth++; }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>解析一组台词：每个元素是 {"text":..,"pose":..}。</summary>
        private static List<Line> ParseLines(string body)
        {
            var lines = new List<Line>();

            int i = 0;
            while (i < body.Length)
            {
                int textAt = body.IndexOf("\"text\"", i, StringComparison.Ordinal);
                if (textAt < 0)
                {
                    break;
                }

                string text = ReadStringAfterKey(body, textAt, out int textEnd);
                if (text == null)
                {
                    i = textAt + 6;
                    continue;
                }

                var line = new Line();
                line.Text = text;
                line.Pose = NaibaoPose.Guide;   // 没写 pose 时的兜底

                int poseAt = body.IndexOf("\"pose\"", textEnd, StringComparison.Ordinal);
                if (poseAt >= 0)
                {
                    string poseName = ReadStringAfterKey(body, poseAt, out int poseEnd);

                    NaibaoPose parsed;
                    if (poseName != null && TryParsePose(poseName, out parsed))
                    {
                        line.Pose = parsed;
                    }

                    i = poseEnd;
                }
                else
                {
                    i = textEnd;
                }

                lines.Add(line);
            }

            return lines;
        }

        private static bool TryParsePose(string name, out NaibaoPose pose)
        {
            foreach (var p in AllPoses)
            {
                if (p.ToString() == name)
                {
                    pose = p;
                    return true;
                }
            }

            // 也接受数字写法（老存档）
            int numeric;
            if (int.TryParse(name, out numeric))
            {
                pose = (NaibaoPose)numeric;
                return true;
            }

            pose = NaibaoPose.Guide;
            return false;
        }

        private static string ReadStringAfterKey(string raw, int keyIndex, out int endIndex)
        {
            endIndex = keyIndex + 6;

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
