using System;
using System.Globalization;
using System.Text;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 永夜模式的存档：只存「再接着跑」所必需的那几个数。
    ///
    /// 刻意不存敌人的内部状态 —— 敌人 AI 有随机数与状态机，存了也难保证一致；
    /// 读档后敌人从当前难度的初始状态重新开始，玩家保住的是时间、电量和记账。
    ///
    /// 序列化成一行文本（`v1|a|b|...`），直接丢进 PlayerPrefs 就够了，
    /// 不用 JsonUtility，也就保持了这个类**不引用 UnityEngine**、能脱离 Unity 单测。
    /// </summary>
    public sealed class EternalNightSave
    {
        /// <summary>当前格式版本。字段改了就要换版本，旧档当作「没有存档」丢掉。</summary>
        public const string Version = "v1";

        private const int FieldCount = 10;

        /// <summary>已经撑过的秒数。</summary>
        public float ElapsedSeconds;

        /// <summary>存档时的剩余电量（0 ~ 100）。</summary>
        public float Power;

        /// <summary>已经结算过几次补给。</summary>
        public int Settlements;

        /// <summary>本周期已攒的账。</summary>
        public float Credits;

        /// <summary>当前动静（决定难度）。</summary>
        public float Noise;

        /// <summary>本周期已经走了多少秒。</summary>
        public float SecondsIntoPeriod;

        /// <summary>累计补进去的电量。</summary>
        public float TotalGranted;

        /// <summary>登记过的操作总数。</summary>
        public int ActionsRegistered;

        /// <summary>存这一档时游戏里的整点小时数（只给标题页显示用）。</summary>
        public int Hour;

        /// <summary>序列化成一行文本。</summary>
        public string ToStorage()
        {
            var sb = new StringBuilder();
            sb.Append(Version);
            sb.Append('|').Append(Float(ElapsedSeconds));
            sb.Append('|').Append(Float(Power));
            sb.Append('|').Append(Settlements.ToString(CultureInfo.InvariantCulture));
            sb.Append('|').Append(Float(Credits));
            sb.Append('|').Append(Float(Noise));
            sb.Append('|').Append(Float(SecondsIntoPeriod));
            sb.Append('|').Append(Float(TotalGranted));
            sb.Append('|').Append(ActionsRegistered.ToString(CultureInfo.InvariantCulture));
            sb.Append('|').Append(Hour.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// 从文本解析。版本不对、字段缺失、数字非法、数值越界都算失败
        /// （返回 false），调用方当作「没有存档」处理即可。
        /// </summary>
        public static bool TryParse(string raw, out EternalNightSave save)
        {
            save = null;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            string[] parts = raw.Split('|');
            if (parts.Length != FieldCount)
            {
                return false;
            }

            if (parts[0] != Version)
            {
                return false;
            }

            float elapsed;
            float power;
            int settlements;
            float credits;
            float noise;
            float secondsIntoPeriod;
            float totalGranted;
            int actions;
            int hour;

            if (!TryFloat(parts[1], out elapsed) ||
                !TryFloat(parts[2], out power) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out settlements) ||
                !TryFloat(parts[4], out credits) ||
                !TryFloat(parts[5], out noise) ||
                !TryFloat(parts[6], out secondsIntoPeriod) ||
                !TryFloat(parts[7], out totalGranted) ||
                !int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out actions) ||
                !int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour))
            {
                return false;
            }

            if (elapsed < 0f || power < 0f || power > 100f || settlements < 0 ||
                credits < 0f || noise < 0f || secondsIntoPeriod < 0f || totalGranted < 0f ||
                actions < 0 || hour < 0)
            {
                return false;
            }

            save = new EternalNightSave
            {
                ElapsedSeconds = elapsed,
                Power = power,
                Settlements = settlements,
                Credits = credits,
                Noise = noise,
                SecondsIntoPeriod = secondsIntoPeriod,
                TotalGranted = totalGranted,
                ActionsRegistered = actions,
                Hour = hour,
            };
            return true;
        }

        private static string Float(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
