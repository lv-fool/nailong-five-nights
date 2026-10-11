namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 掷骰接口。敌人 AI 的推进全靠它。
    ///
    /// 抽成接口是为了在测试里换成确定性实现 —— 让「活动度 20 必定前进、
    /// 活动度 0 绝不前进」这类断言可写。生产代码用 <see cref="SystemRandomSource"/>。
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>返回 [minInclusive, maxExclusive) 之间的整数。</summary>
        int Range(int minInclusive, int maxExclusive);
    }

    /// <summary>默认实现，用 System.Random，不依赖 UnityEngine。</summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly System.Random _random;

        public SystemRandomSource()
        {
            _random = new System.Random();
        }

        public SystemRandomSource(int seed)
        {
            _random = new System.Random(seed);
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            return _random.Next(minInclusive, maxExclusive);
        }
    }
}
