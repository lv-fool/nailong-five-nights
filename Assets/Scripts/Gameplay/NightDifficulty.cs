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
        /// <summary>
        /// 总夜数：**五夜**。
        ///
        /// 隐藏夜暂时移除（用户 2026-10-22 定），所以这里从 6 降到 5。
        /// 要恢复就把 NightCount 改回 6、并把 HiddenNightIndex 加回来。
        /// </summary>
        public const int NightCount = 5;

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

        /// <summary>
        /// 运行时入口：**优先读 Assets/Resources/night_difficulty.json**，
        /// 读不到或某格没写才退回写死的默认曲线。
        ///
        /// ⚠️ 默认曲线在 <see cref="CreateDefaultRaw"/> 里 —— 那个不读文件，
        /// 编辑器窗口的「恢复默认」用它，免得自己读自己写出来的东西。
        /// </summary>
        public static NightDifficulty CreateDefault()
        {
            NightDifficultyTable table = LoadTable();

            if (table == null)
            {
                return CreateDefaultRaw();
            }

            var difficulty = new NightDifficulty();
            var fallback = CreateDefaultRaw();

            for (int n = 0; n < NightCount; n++)
            {
                foreach (var id in EnemyIds.All)
                {
                    int v = table.GetLevel(n, id);

                    // -1 = json 里没写这一格 → 用默认
                    difficulty.SetLevel(n, id, v >= 0 ? v : fallback.GetLevel(n, id));
                }
            }

            return difficulty;
        }

        /// <summary>缓存的难度表 —— 只在第一次用时读一次 Resources。</summary>
        private static NightDifficultyTable _table;
        private static bool _tableLoaded;

        private static NightDifficultyTable LoadTable()
        {
            if (_tableLoaded)
            {
                return _table;
            }

            _tableLoaded = true;

            var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>(
                NightDifficultyTable.ResourcePath);

            if (asset == null)
            {
                return null;
            }

            NightDifficultyTable parsed;
            if (NightDifficultyTable.TryParse(asset.text, out parsed))
            {
                _table = parsed;
            }

            return _table;
        }

        /// <summary>编辑器改了 json 之后让缓存失效（窗口保存时调）。</summary>
        public static void InvalidateTableCache()
        {
            _table = null;
            _tableLoaded = false;
        }

        /// <summary>写死的默认难度曲线（不读文件）。</summary>
        public static NightDifficulty CreateDefaultRaw()
        {
            var difficulty = new NightDifficulty();

            // 走门的三条照 **FNAF 1 原版 AI 表**（technicalfnaf 核实）：
            //   袋鼠 = 邦尼：  夜1=0 夜2=3 夜3=0 夜4=2 夜5=5 夜6=10
            //   牛来 = 奇卡：  夜1=0 夜2=1 夜3=5 夜4=4 夜5=7 夜6=12
            //   奶龙 = 弗莱迪：原版 0/0/1/2/3/4，但项目作者要求**最低 1** ——
            //     活动度 0 = 整夜不动，奶龙是偷电捣乱的核心，任何一夜都不能歇着
            // ⚠️ 原版是有回落的（邦尼夜3=0 < 夜2=3），不是每夜递增 ——
            //   改测试时别想当然地要求单调。
            // 噜噜走手电筒（不是门类），保留我们的曲线：1/2/4/7/10。
            //
            // ⚠️ **隐藏夜（原第 6 夜）已暂时移除**（用户 2026-10-22 定），
            // 原来那行 12/10/16/4 先注释掉留着 —— 恢复时把 NightCount 改回 6、
            // 解开下面这行、并把 HiddenNightIndex 加回来即可。
            //
            //        夜   牛来 袋鼠 噜噜 奶龙
            SetRow(difficulty, 0, 0, 0, 1, 1);
            SetRow(difficulty, 1, 1, 3, 2, 1);
            SetRow(difficulty, 2, 5, 0, 4, 1);
            SetRow(difficulty, 3, 4, 2, 7, 2);
            SetRow(difficulty, 4, 7, 5, 10, 3);
            // SetRow(difficulty, 5, 12, 10, 16, 4);   // 隐藏夜 —— 暂时移除

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
