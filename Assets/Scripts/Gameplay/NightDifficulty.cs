namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 难度表：每夜 × 每个敌人一个活动度（0 ~ 20）。
    ///
    /// 第 1 夜温和、第 5 夜疯狂，隐藏夜最难。数值全部可调 ——
    /// 外层用 EnemyConfigAsset（ScriptableObject）包装，绿子在编辑器里改。
    ///
    /// 纯逻辑类，不引用 UnityEngine。
    /// </summary>
    public sealed class NightDifficulty
    {
        /// <summary>总夜数：五夜 + 隐藏夜。</summary>
        public const int NightCount = 6;

        /// <summary>隐藏夜的序号（0 起）。通关第 5 夜解锁。</summary>
        public const int HiddenNightIndex = 5;

        private readonly int[] _levels;

        public NightDifficulty()
        {
            _levels = new int[NightCount * EnemyIds.All.Length];
        }

        /// <summary>取某夜某个敌人的活动度。</summary>
        public int GetLevel(int nightIndex, EnemyId enemy)
        {
            if (nightIndex < 0 || nightIndex >= NightCount)
            {
                return 0;
            }

            return _levels[nightIndex * EnemyIds.All.Length + (int)enemy];
        }

        /// <summary>设置某夜某个敌人的活动度，自动夹在 0 ~ 20。</summary>
        public void SetLevel(int nightIndex, EnemyId enemy, int level)
        {
            if (nightIndex < 0 || nightIndex >= NightCount)
            {
                return;
            }

            _levels[nightIndex * EnemyIds.All.Length + (int)enemy] = Clamp(level);
        }

        /// <summary>默认难度曲线。</summary>
        public static NightDifficulty CreateDefault()
        {
            var difficulty = new NightDifficulty();

            //        夜   牛来 袋鼠 噜噜 奶龙
            SetRow(difficulty, 0, 2, 1, 1, 3);
            SetRow(difficulty, 1, 3, 3, 2, 5);
            SetRow(difficulty, 2, 5, 5, 4, 7);
            SetRow(difficulty, 3, 8, 8, 7, 10);
            SetRow(difficulty, 4, 12, 12, 10, 14);
            SetRow(difficulty, 5, 18, 18, 16, 18);

            return difficulty;
        }

        private static void SetRow(NightDifficulty difficulty, int night,
            int niulai, int daishu, int lulu, int nailong)
        {
            difficulty.SetLevel(night, EnemyId.Niulai, niulai);
            difficulty.SetLevel(night, EnemyId.Daishu, daishu);
            difficulty.SetLevel(night, EnemyId.Lulu, lulu);
            difficulty.SetLevel(night, EnemyId.Nailong, nailong);
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
