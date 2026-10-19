using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 自定义夜配置：四个敌人的活动度各由玩家自调（0 ~ 20），照 FNAF 1 的第 7 夜。
    ///
    /// 纯数据类 —— 不碰敌人、不碰场景，只负责「存四个数」：
    /// 菜单读它显示上次的设置，开场时交给 <see cref="EnemyDirector.SetCustomLevels"/>。
    /// 存档用 PlayerPrefs，四个敌人各一个键（键名见 <see cref="KeyFor"/>）。
    ///
    /// ⚠️ 引用了 UnityEngine.PlayerPrefs，但逻辑本身不依赖场景，
    ///    EditMode 测试可以直接跑（PlayerPrefs 在编辑器里可用）。
    /// </summary>
    public sealed class CustomNightConfig
    {
        /// <summary>存档键前缀，后面接敌人代号（如 nailong.custom.niulai）。</summary>
        public const string KeyPrefix = "nailong.custom.";

        private readonly int[] _levels = new int[EnemyIds.All.Length];

        /// <summary>取某个敌人的自定义活动度（0 ~ 20）。</summary>
        public int GetLevel(EnemyId enemy)
        {
            return _levels[(int)enemy];
        }

        /// <summary>设置某个敌人的自定义活动度，自动夹在 0 ~ 20。</summary>
        public void SetLevel(EnemyId enemy, int level)
        {
            _levels[(int)enemy] = Clamp(level);
        }

        /// <summary>所有敌人统一设成一个值（菜单的「全 20」之类会用）。</summary>
        public void SetAll(int level)
        {
            foreach (EnemyId id in EnemyIds.All)
            {
                SetLevel(id, level);
            }
        }

        /// <summary>某个敌人的存档键。</summary>
        public static string KeyFor(EnemyId enemy)
        {
            return KeyPrefix + enemy.Code();
        }

        /// <summary>从 PlayerPrefs 读出上次的自定义配置；没存过就是全 0。</summary>
        public static CustomNightConfig Load()
        {
            var config = new CustomNightConfig();
            foreach (EnemyId id in EnemyIds.All)
            {
                config.SetLevel(id, PlayerPrefs.GetInt(KeyFor(id), 0));
            }

            return config;
        }

        /// <summary>写进 PlayerPrefs。</summary>
        public void Save()
        {
            foreach (EnemyId id in EnemyIds.All)
            {
                PlayerPrefs.SetInt(KeyFor(id), GetLevel(id));
            }

            PlayerPrefs.Save();
        }

        /// <summary>全 0 意味着整夜没有敌人动 —— 合法，就是睡觉模拟器。</summary>
        public int TotalLevel()
        {
            int total = 0;
            foreach (int level in _levels)
            {
                total += level;
            }

            return total;
        }

        private static int Clamp(int level)
        {
            if (level < 0)
            {
                return 0;
            }

            return level > EnemyBrain.MaxActivityLevel ? EnemyBrain.MaxActivityLevel : level;
        }
    }
}
