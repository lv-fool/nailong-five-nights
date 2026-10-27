namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 一条「来自奶包的支援」的数据定义 —— 只有数据，没有逻辑。
    ///
    /// 支援之所以能做成一张表，是因为 <see cref="BelialNight"/> 只认
    /// <see cref="SupportEffect"/> 这一个类型去改对应系统；玩家看到的名字、描述、
    /// 稀有度、数值全部来自这里。以后加内容就是往 <see cref="SupportCatalog"/> 里加一行。
    ///
    /// 纯数据类，不引用 UnityEngine。
    /// </summary>
    public sealed class SupportDef
    {
        /// <summary>内部 id（存档与测试用，改了要连带改存档）。</summary>
        public string Id { get; private set; }

        /// <summary>卡片标题。</summary>
        public string Title { get; private set; }

        /// <summary>卡片正文 —— 写成「她会怎么跟你说」，不是数值说明书。</summary>
        public string Description { get; private set; }

        /// <summary>类别（界面分组用；「加量」靠它禁掉电力类）。</summary>
        public SupportCategory Category { get; private set; }

        /// <summary>稀有度（只影响抽取权重）。</summary>
        public SupportRarity Rarity { get; private set; }

        /// <summary>改哪条规则。</summary>
        public SupportEffect Effect { get; private set; }

        /// <summary>效果数值，含义由 <see cref="Effect"/> 决定。</summary>
        public float Value { get; private set; }

        /// <summary>第二个数值 —— 只有需要两个数字的支援才用（如「上限 ×2，立刻 +50」）。</summary>
        public float Value2 { get; private set; }

        /// <summary>同一局最多拿几次（1 = 不叠加）。</summary>
        public int MaxStack { get; private set; }

        public SupportDef(string id, string title, string description, SupportCategory category,
            SupportRarity rarity, SupportEffect effect, float value, int maxStack, float value2 = 0f)
        {
            Id = id;
            Title = title;
            Description = description;
            Category = category;
            Rarity = rarity;
            Effect = effect;
            Value = value;
            Value2 = value2;
            MaxStack = maxStack > 1 ? maxStack : 1;
        }
    }
}
