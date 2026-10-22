using System;
using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 跳杀后那句台词的**可编辑副本**。
    ///
    /// 存在 <c>Assets/Resources/death_quotes.json</c>，由编辑器窗口
    /// 「奶龙 / 11. 编辑跳杀台词」读写。运行时从 Resources 读；
    /// 文件不存在（或某个敌人没写）就退回 <see cref="QuoteFor"/> 里那套默认文案 ——
    /// 所以删掉这个 json 也不会让结算画面变成空白。
    ///
    /// 为什么放 Resources 而不是写回源码：台词是文案，不该逼着编辑器去改 .cs；
    /// 而且和项目里 monitor_layout.json / occluders.json 的做法一致。
    /// </summary>
    [Serializable]
    public sealed class DeathQuotes
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "death_quotes";

        /// <summary>一条台词：敌人代号 + 文本。</summary>
        [Serializable]
        public class Entry
        {
            public string Id;
            public string Text;
        }

        public List<Entry> Quotes = new List<Entry>();

        /// <summary>
        /// 每只敌人被抓到后的**默认**台词。
        /// 调子跟美术一样是「可爱反差」—— 不骂人，用撒娇 / 无辜的语气说可怕的话。
        ///
        /// ⚠️ 这是兜底值：json 里有就用 json 的，没有才用这里。
        /// 编辑器窗口的「重置为默认」也是读这张表。
        /// </summary>
        public static string DefaultFor(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Nailong:
                    return "「电量我收下啦～下次记得省着点用哦。」";

                case EnemyId.Niulai:
                    return "「门关得挺快嘛……可惜我比门快。」";

                case EnemyId.Daishu:
                    return "「你刚才没开灯吧？那你怎么知道门外是谁呢。」";

                case EnemyId.Lulu:
                    return "「手电筒呢？这次好像闪得不够亮呀。」";

                default:
                    return string.Empty;
            }
        }

        /// <summary>取某只敌人的台词：json 里有就用 json，否则退回默认。</summary>
        public string For(EnemyId id)
        {
            string code = id.Code();

            if (Quotes != null)
            {
                for (int i = 0; i < Quotes.Count; i++)
                {
                    var e = Quotes[i];
                    if (e != null && e.Id == code && !string.IsNullOrEmpty(e.Text))
                    {
                        return e.Text;
                    }
                }
            }

            return DefaultFor(id);
        }

        /// <summary>写一条台词（编辑器窗口用）。</summary>
        public void Set(EnemyId id, string text)
        {
            if (Quotes == null)
            {
                Quotes = new List<Entry>();
            }

            string code = id.Code();

            for (int i = 0; i < Quotes.Count; i++)
            {
                if (Quotes[i] != null && Quotes[i].Id == code)
                {
                    Quotes[i].Text = text;
                    return;
                }
            }

            Quotes.Add(new Entry { Id = code, Text = text });
        }

        /// <summary>把四只敌人全填成默认值（编辑器窗口的「重置」用）。</summary>
        public void ResetToDefaults()
        {
            Quotes = new List<Entry>();

            foreach (var id in EnemyIds.All)
            {
                Quotes.Add(new Entry { Id = id.Code(), Text = DefaultFor(id) });
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

            bool first = true;

            if (Quotes != null)
            {
                foreach (var e in Quotes)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;

                    sb.Append("{\"id\":\"").Append(Escape(e.Id))
                      .Append("\",\"text\":\"").Append(Escape(e.Text ?? string.Empty))
                      .Append("\"}");
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
                // 项目里没引 Newtonsoft，用 Unity 内置的 JsonUtility —— 但它不认
                // List&lt;自定义类&gt; 的顶层包装，所以这里手写一个轻量解析：
                // 只找 "id" 和 "text" 两个键，够用且不引入依赖。
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

                    int textKey = raw.IndexOf("\"text\"", idEnd, StringComparison.Ordinal);
                    if (textKey < 0)
                    {
                        break;
                    }

                    string text = ReadStringAfterKey(raw, textKey, out int textEnd);

                    quotes.Set(ParseId(id), text ?? string.Empty);
                    i = textEnd;
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

            var sb = new System.Text.StringBuilder();
            int i = open + 1;

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

        /// <summary>代号 → 敌人。认不出来就当作没有（返回 Niulai 只为满足 out 语义）。</summary>
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
