using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 「贝利亚之夜」的解锁条件（2026-10-26 定，当晚改过一版）。
    ///
    /// 一开始是**隐藏**的：连标题页的按钮都不出现。想进去要同时满足两件事：
    ///   1. 前五夜**被怪物咬死的次数**攒够 <see cref="RequiredDeaths"/> 次
    ///      —— 死得够多，才算真的摸透了它们怎么杀你；
    ///   2. **通关第 5 夜**。
    ///
    /// 顺序不限，达成后**永久解锁**。
    ///
    /// 两个值都存 PlayerPrefs，跟 <see cref="AchievementStore"/> 一个路子。
    /// ⚠️ 引用了 UnityEngine.PlayerPrefs，但逻辑本身不依赖场景，EditMode 测试可以直接跑。
    /// </summary>
    public static class BelialUnlock
    {
        /// <summary>通关第 5 夜时写 1。</summary>
        public const string ClearedNight5Key = "nailong.belialClearedNight5";

        /// <summary>被怪物咬死的累计次数。</summary>
        public const string DeathsKey = "nailong.belialDeaths";

        /// <summary>要死多少次才算「摸透了」—— 5 次。</summary>
        public const int RequiredDeaths = 5;

        /// <summary>第 5 夜通关了没有。</summary>
        public static bool ClearedNight5
        {
            get { return PlayerPrefs.GetInt(ClearedNight5Key, 0) == 1; }
        }

        /// <summary>被怪物咬死过多少次（累计，跨局跨夜）。</summary>
        public static int Deaths
        {
            get { return PlayerPrefs.GetInt(DeathsKey, 0); }
        }

        /// <summary>两个条件都齐了才解锁。</summary>
        public static bool IsUnlocked
        {
            get { return ClearedNight5 && Deaths >= RequiredDeaths; }
        }

        /// <summary>还差什么 —— 给调试面板和测试看，正式界面不显示。</summary>
        public static string MissingCondition
        {
            get
            {
                bool enoughDeaths = Deaths >= RequiredDeaths;
                if (!ClearedNight5 && !enoughDeaths)
                {
                    return "还差：通关第 5 夜，再死 " + (RequiredDeaths - Deaths) + " 次";
                }

                if (!ClearedNight5)
                {
                    return "还差：通关第 5 夜";
                }

                if (!enoughDeaths)
                {
                    return "还差：" + (RequiredDeaths - Deaths) + " 次";
                }

                return string.Empty;
            }
        }

        /// <summary>记一笔「第 5 夜通关」。重复调用无副作用。</summary>
        public static void MarkNight5Cleared()
        {
            if (ClearedNight5)
            {
                return;
            }

            PlayerPrefs.SetInt(ClearedNight5Key, 1);
            PlayerPrefs.Save();
        }

        /// <summary>记一笔「被怪物咬死」。不加任何上限 —— 死了就是死了。</summary>
        public static void NoteDeath()
        {
            PlayerPrefs.SetInt(DeathsKey, Deaths + 1);
            PlayerPrefs.Save();
        }

        /// <summary>把两个条件都清掉（测试与调试用；正式流程不会调）。</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(ClearedNight5Key);
            PlayerPrefs.DeleteKey(DeathsKey);
            PlayerPrefs.Save();
        }
    }
}
