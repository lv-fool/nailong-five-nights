using System;
using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 跳杀后台词的**随机池**，按敌人分开。
    ///
    /// 存 <c>Assets/Resources/death_quotes.json</c>，由编辑器窗口
    /// 「奶龙 / 11. 编辑跳杀台词」读写。运行时从 Resources 读；
    /// 文件不存在（或某只敌人没写 / 池子空）就退回
    /// <see cref="DefaultPool"/> 里那套默认文案 —— 删掉这个 json 也不会让结算画面变空白。
    ///
    /// 为什么是「池」而不是一句：同一只敌人被抓两次说一模一样的话很假。
    /// 池子里每句都可编辑，每次被抓**随机抽一句**（且尽量不和上一句重复）。
    /// </summary>
    [Serializable]
    public sealed class DeathQuotes
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "death_quotes";

        /// <summary>某只敌人的台词池。</summary>
        [Serializable]
        public class Pool
        {
            public string Id;
            public List<string> Lines = new List<string>();
        }

        public List<Pool> Quotes = new List<Pool>();

        /// <summary>
        /// 每只敌人的**默认**台词池（给 2~3 句）。
        /// 调子跟美术一样是「可爱反差」—— 不骂人，用撒娇 / 无辜的语气说可怕的话。
        ///
        /// ⚠️ 这是兜底值：json 里有就用 json 的，没有才用这里。
        /// 编辑器窗口的「恢复默认」也是读这张表。
        /// </summary>
        public static string[] DefaultPool(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Nailong:
                    return new[]
                    {
                        "「电量我收下啦～下次记得省着点用哦。」",
                        "「灯也是耗电的呀，你忘了吗？」",
                        "「我只是想看看你，顺便把电带走。」",
                    };

                case EnemyId.Niulai:
                    return new[]
                    {
                        "「门关得挺快嘛……可惜我比门快。」",
                        "「你按关门键的手，抖了一下呢。」",
                        "「门后面是走廊，我知道得比你清楚。」",
                    };

                case EnemyId.Daishu:
                    return new[]
                    {
                        "「你刚才没开灯吧？那你怎么知道门外是谁呢。」",
                        "「我一直在那儿站着，只是你看不见。」",
                        "「下回记得先开灯，再决定怕不怕。」",
                    };

                case EnemyId.Lulu:
                    return new[]
                    {
                        "「手电筒呢？这次好像闪得不够亮呀。」",
                        "「你照过来的时候，我已经在动了。」",
                        "「别眨眼，眨一下我就不在原地了。」",
                    };

                default:
                    return new string[0];
            }
        }

        /// <summary>取某只敌人的台词池（json 优先，没有就默认）。</summary>
        public List<string> PoolFor(EnemyId id)
        {
            string code = id.Code();

            if (Quotes != null)
            {
                for (int i = 0; i < Quotes.Count; i++)
                {
                    var p = Quotes[i];
                    if (p != null && p.Id == code && p.Lines != null && p.Lines.Count > 0)
                    {
                        return p.Lines;
                    }
                }
            }

            return new List<string>(DefaultPool(id));
        }

        /// <summary>
        /// 随机抽一句。**尽量不和 <paramref name="avoid"/> 重复** ——
        /// 池子里不止一句时，连续两次被抓不会听到同一句话。
        ///
        /// 池子只有一句时只能重复（没办法）；空池返回空串。
        /// </summary>
        public string RandomLineFor(EnemyId id, string avoid = null)
        {
            string line;
            RandomLineIndexFor(id, avoid, out line);
            return line;
        }

        /// <summary>
        /// 同 <see cref="RandomLineFor"/>，但**连这一句在池子里的下标一起返回**。
        ///
        /// 下标是给语音用的：结算画面按「敌人 + 抽中的第几句」去载
        /// <c>Audio/voice_&lt;代号&gt;_&lt;下标&gt;</c>，这样字幕和录音才对得上。
        /// （台词是随机抽的，只有一条通用录音必然会出现「说的和写的不是一句」。）
        ///
        /// 抽不到（空池）返回 -1，<paramref name="line"/> 为空串。
        /// </summary>
        public int RandomLineIndexFor(EnemyId id, string avoid, out string line)
        {
            line = string.Empty;

            List<string> pool = PoolFor(id);

            if (pool == null || pool.Count == 0)
            {
                return -1;
            }

            var candidates = new List<int>(pool.Count);

            for (int i = 0; i < pool.Count; i++)
            {
                string s = pool[i];

                if (string.IsNullOrEmpty(s))
                {
                    continue;
                }

                if (avoid != null && s == avoid)
                {
                    continue;
                }

                candidates.Add(i);
            }

            // 全是避免项（池子只有一句）→ 退回整池随机
            if (candidates.Count == 0)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (!string.IsNullOrEmpty(pool[i]))
                    {
                        candidates.Add(i);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return -1;
            }

            int pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            line = pool[pick];
            return pick;
        }

        /// <summary>写一只敌人的整池（编辑器窗口用）。</summary>
        public void SetPool(EnemyId id, List<string> lines)
        {
            if (Quotes == null)
            {
                Quotes = new List<Pool>();
            }

            string code = id.Code();
            var copy = lines != null ? new List<string>(lines) : new List<string>();

            for (int i = 0; i < Quotes.Count; i++)
            {
                if (Quotes[i] != null && Quotes[i].Id == code)
                {
                    Quotes[i].Lines = copy;
                    return;
                }
            }

            Quotes.Add(new Pool { Id = code, Lines = copy });
        }

        /// <summary>把四只敌人全填成默认池（编辑器窗口的「重置」用）。</summary>
        public void ResetToDefaults()
        {
            Quotes = new List<Pool>();

            foreach (var id in EnemyIds.All)
            {
                Quotes.Add(new Pool
                {
                    Id = id.Code(),
                    Lines = new List<string>(DefaultPool(id))
                });
            }
        }

        // ---------------------------------------------------------------
        // 存读
        // ---------------------------------------------------------------

        private const string Header = "{\"quotes\":[";

        /// <summary>序列化成一行 JSON。</summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Header);

            bool firstPool = true;

            if (Quotes != null)
            {
                foreach (var p in Quotes)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id))
                    {
                        continue;
                    }

                    if (!firstPool)
                    {
                        sb.Append(',');
                    }

                    firstPool = false;

                    sb.Append("{\"id\":\"").Append(Escape(p.Id)).Append("\",\"lines\":[");

                    bool firstLine = true;

                    if (p.Lines != null)
                    {
                        foreach (var line in p.Lines)
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
        public static bool TryParse(string raw, out DeathQuotes result)
        {
            result = null;

            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            try
            {
                // 项目没引 Newtonsoft；JsonUtility 不认 List&lt;自定义类&gt; 的顶层包装，
                // 所以这里手写一个只认 "id" / "lines" 的轻量解析 —— 够用且不引新依赖。
                var quotes = new DeathQuotes();

                int i = 0;
                while (i < raw.Length)
                {
                    int idKey = raw.IndexOf("\"id\"", i, StringComparison.Ordinal);
                    if (idKey < 0)
                    {
                        break;
                    }

                    string id = ReadStringAfterKey(raw, idKey, out int idEnd);
                    if (id == null)
                    {
                        i = idKey + 4;
                        continue;
                    }

                    int linesKey = raw.IndexOf("\"lines\"", idEnd, StringComparison.Ordinal);
                    if (linesKey < 0)
                    {
                        break;
                    }

                    int arrayOpen = raw.IndexOf('[', linesKey);
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

                    quotes.SetPool(ParseId(id), lines);
                    i = cursor;
                }

                result = quotes;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadStringAfterKey(string raw, int keyIndex, out int endIndex)
        {
            endIndex = keyIndex + 4;

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

        private static EnemyId ParseId(string code)
        {
            foreach (var id in EnemyIds.All)
            {
                if (id.Code() == code)
                {
                    return id;
                }
            }

            return EnemyId.Niulai;
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
