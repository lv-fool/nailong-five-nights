using System;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 永夜模式的难度曲线：时间越长越难，动静越大越难。
    ///
    /// 档位直接用「夜的序号」（0 起，沿用 <see cref="NightDifficulty"/> 的表）：
    /// 开局就是第 3 夜，每撑 <see cref="HoursPerTier"/> 小时加一档，
    /// 动静每攒 <see cref="NoisePerTier"/> 点再加一档（最多 <see cref="MaxNoiseTiers"/> 档）。
    ///
    /// 爬到第 6 夜（表的最后一档）之后不再换夜，而是转为 <b>加压</b>
    /// （<see cref="OverdriveFor"/>）：所有敌人的活动度按比例上浮，
    /// 所以待得越久越不可能靠「熟悉了」混过去。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public static class EndlessDifficulty
    {
        /// <summary>开局档位（第 3 夜，0 起算 = 2）。</summary>
        public const int StartNight = 2;

        /// <summary>每撑多少小时加一档。</summary>
        public const float HoursPerTier = 1.5f;

        /// <summary>动静每多少点加一档。</summary>
        public const float NoisePerTier = 10f;

        /// <summary>动静最多能加几档 —— 免得光靠乱点就把难度顶穿。</summary>
        public const int MaxNoiseTiers = 3;

        /// <summary>超过第 6 夜后，每多一档给敌人活动度加成多少。</summary>
        public const float OverdrivePerTier = 0.25f;

        /// <summary>加压上限（1.2 = 活动度最多上浮 120%，之后被 0~20 的夹取兜住）。</summary>
        public const float MaxOverdrive = 1.2f;

        /// <summary>不封顶的档位 —— 用来判断超档后该加多少压。</summary>
        public static int RawTierFor(float hoursSurvived, float noise)
        {
            float hours = hoursSurvived > 0f ? hoursSurvived : 0f;
            float quiet = noise > 0f ? noise : 0f;

            int timeTiers = (int)(hours / HoursPerTier);
            int noiseTiers = (int)(quiet / NoisePerTier);
            if (noiseTiers > MaxNoiseTiers)
            {
                noiseTiers = MaxNoiseTiers;
            }

            return StartNight + timeTiers + noiseTiers;
        }

        /// <summary>实际使用的档位，夹在 0 ~ 第 6 夜。</summary>
        public static int TierFor(float hoursSurvived, float noise)
        {
            int cap = NightDifficulty.NightCount - 1;
            int raw = RawTierFor(hoursSurvived, noise);
            if (raw < 0)
            {
                return 0;
            }

            return raw > cap ? cap : raw;
        }

        /// <summary>超档后的加压系数（0 = 不加压）。</summary>
        public static float OverdriveFor(float hoursSurvived, float noise)
        {
            int cap = NightDifficulty.NightCount - 1;
            int raw = RawTierFor(hoursSurvived, noise);
            if (raw <= cap)
            {
                return 0f;
            }

            float overdrive = (raw - cap) * OverdrivePerTier;
            return Math.Min(overdrive, MaxOverdrive);
        }
    }
}
