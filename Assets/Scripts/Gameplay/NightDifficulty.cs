namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 难度表：每夜 × 每个敌人一个活动度（0 ~ 20）。
    ///
    /// 第 1 夜温和、第 5 夜疯狂，隐藏夜最难。数值全部可调 ——
    /// 外层用 EnemyConfigAsset（ScriptableObject）包装，项目作者在编辑器里改。
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

            // 走门的三条照 **FNAF 1 原版 AI 表**（technicalfnaf 核实）：
            //   袋鼠 = 邦尼：  夜1=0 夜2=3 夜3=0 夜4=2 夜5=5 夜6=10
            //   牛来 = 奇卡：  夜1=0 夜2=1 夜3=5 夜4=4 夜5=7 夜6=12
            //   奶龙 = 弗莱迪：原版 0/0/1/2/3/4，但项目作者要求**最低 1** ——
            //     活动度 0 = 整夜不动，奶龙是偷电捣乱的核心，任何一夜都不能歇着
            // ⚠️ 原版是有回落的（邦尼夜3=0 < 夜2=3），不是每夜递增 ——
            //   改测试时别想当然地要求单调。
            // 噜噜走手电筒（不是门类），保留我们的曲线：1/2/4/7/10/16。
            //
            //        夜   牛来 袋鼠 噜噜 奶龙
            SetRow(difficulty, 0, 0, 0, 1, 1);
            SetRow(difficulty, 1, 1, 3, 2, 1);
            SetRow(difficulty, 2, 5, 0, 4, 1);
            SetRow(difficulty, 3, 4, 2, 7, 2);
            SetRow(difficulty, 4, 7, 5, 10, 3);
            SetRow(difficulty, 5, 12, 10, 16, 4);

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
