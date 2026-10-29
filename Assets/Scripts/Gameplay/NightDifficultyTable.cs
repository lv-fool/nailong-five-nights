using System;
using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 各关卡 AI 值的**可编辑副本** —— 由编辑器窗口
    /// 「奶龙 / 14. 调整关卡 AI 值」读写，落 <c>Assets/Resources/night_difficulty.json</c>。
    ///
    /// 运行时 <see cref="NightDifficulty.CreateDefault"/> 会先读这个文件；
    /// 文件不存在 / 某格没写 → 用 <see cref="NightDifficulty"/> 里写死的那套默认曲线。
    /// 所以删掉这个 json 也不会让游戏没难度。
    ///
    /// 每格 = 一夜 × 一只敌人的活动度（0~20）。
    /// </summary>
    [Serializable]
    public sealed class NightDifficultyTable
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "night_difficulty";

        /// <summary>一行：某一夜的四个值。</summary>
        [Serializable]
        public class Row
        {
            /// <summary>第几夜（0 起）。</summary>
            public int Night;

            /// <summary>四只敌人的活动度，下标 = (int)EnemyId。</summary>
            public int[] Levels = new int[4];
        }

        public List<Row> Rows = new List<Row>();

        /// <summary>取某夜某个敌人的活动度；没配就返回 -1（调用方退回默认）。</summary>
        public int GetLevel(int night, EnemyId enemy)
        {
            if (Rows != null)
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    var row = Rows[i];
                    if (row == null || row.Night != night || row.Levels == null)
                    {
                        continue;
                    }

                    int idx = (int)enemy;
                    if (idx >= 0 && idx < row.Levels.Length)
                    {
                        return Mathf.Clamp(row.Levels[idx], 0, EnemyBrain.MaxActivityLevel);
                    }
                }
            }

            return -1;
        }

        /// <summary>写某夜某个敌人的活动度（编辑器窗口用）。</summary>
        public void SetLevel(int night, EnemyId enemy, int level)
        {
            if (Rows == null)
            {
                Rows = new List<Row>();
            }

            int idx = (int)enemy;
            if (idx < 0 || idx >= 4)
            {
                return;
            }

            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i] != null && Rows[i].Night == night)
                {
                    if (Rows[i].Levels == null || Rows[i].Levels.Length < 4)
                    {
                        Rows[i].Levels = new int[4];
                    }

                    Rows[i].Levels[idx] = Mathf.Clamp(level, 0, EnemyBrain.MaxActivityLevel);
                    return;
                }
            }

            var row = new Row { Night = night, Levels = new int[4] };
            row.Levels[idx] = Mathf.Clamp(level, 0, EnemyBrain.MaxActivityLevel);
            Rows.Add(row);
        }

        /// <summary>把默认曲线搬进来（编辑器窗口的「恢复默认」用）。</summary>
        public void ResetToDefault()
        {
            Rows = new List<Row>();

            var def = NightDifficulty.CreateDefaultRaw();

            for (int n = 0; n < NightDifficulty.NightCount; n++)
            {
                var row = new Row { Night = n, Levels = new int[4] };

                foreach (var id in EnemyIds.All)
                {
                    row.Levels[(int)id] = def.GetLevel(n, id);
                }

                Rows.Add(row);
            }
        }

        // ---------------------------------------------------------------
        // 存读
        // ---------------------------------------------------------------

        /// <summary>序列化成一行 JSON。</summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"rows\":[");

            bool first = true;

            if (Rows != null)
            {
                foreach (var row in Rows)
                {
                    if (row == null)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;

                    sb.Append("{\"night\":").Append(row.Night).Append(",\"levels\":[");

                    for (int i = 0; i < 4; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        int v = (row.Levels != null && i < row.Levels.Length) ? row.Levels[i] : 0;
                        sb.Append(v);
                    }

                    sb.Append("]}");
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>从 JSON 解析。格式不对返回 false（调用方退回默认）。</summary>
        public static bool TryParse(string raw, out NightDifficultyTable result)
        {
            result = null;

            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            try
            {
                var data = new NightDifficultyTable();

                // 只认 "night" 和 "levels" 两个键 —— 结构简单，不值得引 JSON 库
                int i = 0;

                while (i < raw.Length)
                {
                    int nightAt = raw.IndexOf("\"night\"", i, StringComparison.Ordinal);
                    if (nightAt < 0)
                    {
                        break;
                    }

                    int colon = raw.IndexOf(':', nightAt);
                    if (colon < 0)
                    {
                        break;
                    }

                    // 读数字
                    int p = colon + 1;
                    while (p < raw.Length && !char.IsDigit(raw[p]) && raw[p] != '-')
                    {
                        p++;
                    }

                    int nightEnd = p;
                    while (nightEnd < raw.Length && (char.IsDigit(raw[nightEnd]) || raw[nightEnd] == '-'))
                    {
                        nightEnd++;
                    }

                    int night;
                    if (!int.TryParse(raw.Substring(p, nightEnd - p), out night))
                    {
                        i = nightAt + 7;
                        continue;
                    }

                    // 读 levels 数组
                    int levelsAt = raw.IndexOf("\"levels\"", nightEnd, StringComparison.Ordinal);
                    if (levelsAt < 0)
                    {
                        break;
                    }

                    int arrOpen = raw.IndexOf('[', levelsAt);
                    int arrClose = raw.IndexOf(']', arrOpen);
                    if (arrOpen < 0 || arrClose < 0)
                    {
                        break;
                    }

                    string body = raw.Substring(arrOpen + 1, arrClose - arrOpen - 1);
                    string[] parts = body.Split(',');

                    for (int e = 0; e < parts.Length && e < 4; e++)
                    {
                        int v;
                        if (int.TryParse(parts[e].Trim(), out v))
                        {
                            data.SetLevel(night, (EnemyId)e, v);
                        }
                    }

                    i = arrClose + 1;
                }

                result = data;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
