using System;
using System.Collections.Generic;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 贝利亚之夜的核心账本：12 小时一局、每撑两小时来一次「来自奶包的支援」。
    ///
    /// 规则（老板 2026-10-26 定）：
    /// 1. 一局要撑 <see cref="TotalHours"/> 小时（12 小时 = 6 个两小时时段）。
    /// 2. 每撑过一个时段来一次支援；**第一次是白送的**：三张卡内容完全一样
    ///    （都是「拉钩」），选完立刻再给一次正常三选一 —— 所以总数是
    ///    <see cref="MaxPicks"/> 张，比时段数多一张。
    /// 3. 三条规则改动分别落在三个系统上：电量（关灯睡觉 / 存钱罐）、敌人
    ///    （罚站 / 数到三 / 一二三木头人 / 摔了一跤）、奶包（拉钩）。
    /// 4. 难度两段：前一半时间只有黑化小怪，过了中点贝利亚亲自上场。
    ///
    /// 这个类只管「谁在什么时候拿到什么、总共改了多少数值」，
    /// 不知道画面、不知道音频 —— 纯逻辑，不引用 UnityEngine。
    /// </summary>
    public class BelialNight
    {
        /// <summary>一局要撑过的小时数。</summary>
        public const int TotalHours = 12;

        /// <summary>第二阶段从第几个小时开始 —— 前一半只有黑化小怪，过中点贝利亚上场。</summary>
        public const int PhaseTwoHour = TotalHours / 2;

        /// <summary>每撑过这么多小时来一次支援。</summary>
        public const int HoursPerPick = 2;

        /// <summary>一次支援给几张卡可选。</summary>
        public const int OffersPerChoice = 3;

        /// <summary>
        /// 一局最多拿几张支援：6 个时段各一次，再加开局白送后补的那一次。
        /// </summary>
        public const int MaxPicks = TotalHours / HoursPerPick + 1;

        /// <summary>「存钱罐」每次都有的底薪（百分比）—— 跟永夜模式的结算一个量级。</summary>
        public const float RefundBaseGain = 4f;

        /// <summary>「存钱罐」每 1 点操作账折多少电（按层数叠乘）。</summary>
        public const float RefundPerCredit = 0.75f;

        /// <summary>
        /// 单次返还的电量上限（百分比），防止反复开关门刷电。
        /// ⚠️ 只在拿到 1 层时生效；连拿两次（2 层）直接取消上限 —— 老板 2026-10-26 定的。
        /// </summary>
        public const float RefundCap = 25f;

        /// <summary>取消返还上限需要的层数。</summary>
        public const int RefundUncappedStack = 2;

        private readonly List<SupportDef> _owned = new List<SupportDef>();
        private readonly Random _rng;

        private SupportDef[] _offer;
        private float _credits;
        private int _hours;
        private int _picks;
        private int _missesUsed;
        private int _rerollsUsed;

        /// <summary>用固定种子创建（测试与复现用）。</summary>
        public BelialNight(int seed)
        {
            _rng = new Random(seed);
        }

        /// <summary>用随机种子创建。</summary>
        public BelialNight()
            : this(Environment.TickCount)
        {
        }

        /// <summary>本局已经撑过的小时数。</summary>
        public int Hours
        {
            get { return _hours; }
        }

        /// <summary>本局已经拿到的支援数量。</summary>
        public int Picks
        {
            get { return _picks; }
        }

        /// <summary>当前阶段（1 ~ 2）。前半程只有黑化小怪，后半程贝利亚加入。</summary>
        public int Phase
        {
            get { return _hours < PhaseTwoHour ? 1 : 2; }
        }

        /// <summary>贝利亚本人是否已经上场（第二阶段开始）。</summary>
        public bool BelialActive
        {
            get { return Phase >= 2; }
        }

        /// <summary>是否已经撑满 12 小时。</summary>
        public bool IsComplete
        {
            get { return _hours >= TotalHours; }
        }

        /// <summary>支援是否已经拿满（拿满后不再弹选择）。</summary>
        public bool IsSupportExhausted
        {
            get { return _picks >= MaxPicks; }
        }

        /// <summary>当前正在等玩家选的三张卡；没有在等时返回 null。</summary>
        public SupportDef[] Offer
        {
            get { return _offer; }
        }

        /// <summary>
        /// 这一次摆几张卡 —— 默认三张；拿到「处方笺」之后变成四张。
        /// </summary>
        public int OfferSize
        {
            get
            {
                int size = OffersPerChoice;
                for (int i = 0; i < _owned.Count; i++)
                {
                    if (_owned[i].Effect == SupportEffect.OfferCount)
                    {
                        int offered = (int)(_owned[i].Value + 0.5f);
                        if (offered > size)
                        {
                            size = offered;
                        }
                    }
                }

                return size;
            }
        }

        /// <summary>还能重抽几次（「复诊单」给的）。</summary>
        public int RerollsLeft
        {
            get
            {
                int granted = (int)(TotalOf(SupportEffect.Reroll) + 0.5f);
                int left = granted - _rerollsUsed;
                return left > 0 ? left : 0;
            }
        }

        /// <summary>是否正在等玩家选择。</summary>
        public bool AwaitingChoice
        {
            get { return _offer != null && _offer.Length > 0; }
        }

        /// <summary>
        /// 这次是开局白送那次（三张一样）。界面可以据此加「这不是选错」的提示。
        /// </summary>
        public bool IsForcedOffer
        {
            get { return AwaitingChoice && _picks == 0; }
        }

        /// <summary>已经拿到的支援（按拿到顺序）。</summary>
        public IReadOnlyList<SupportDef> Owned
        {
            get { return _owned; }
        }

        /// <summary>本回合攒下的操作账（下次结算折算成电）。</summary>
        public float Credits
        {
            get { return _credits; }
        }

        // ---------------- 推进 ----------------

        /// <summary>开局：白送那一次的三张卡立刻摆出来。</summary>
        public void Begin()
        {
            if (_offer == null && _picks == 0 && _hours == 0)
            {
                _offer = BuildForcedOffer();
            }
        }

        /// <summary>撑过去若干小时。负数与 0 无效。返回是否真的推进了。</summary>
        public bool AdvanceHours(int hours)
        {
            if (hours <= 0)
            {
                return false;
            }

            _hours = Math.Min(TotalHours, _hours + hours);
            return true;
        }

        /// <summary>
        /// 该发支援了吗 —— 该发就摆出三张卡，并把本回合的操作账结算成电量返还。
        /// <b>返回这次返还的电量（百分比）</b>；没有结算时返回 0。
        /// </summary>
        public float TryOffer()
        {
            if (_offer != null || IsSupportExhausted)
            {
                return 0f;
            }

            if (_hours < RequiredHour(_picks))
            {
                return 0f;
            }

            float refund = SettleRefund();

            if (_picks == 0)
            {
                _offer = BuildForcedOffer();
            }
            else
            {
                _offer = BuildNormalOffer();
            }

            return refund;
        }

        /// <summary>
        /// 选第 index 张卡（超出范围按最近的一张处理）。返回拿到的那条支援。
        /// 第一次选完会立刻再摆出一组正常三选一。
        /// </summary>
        public SupportDef Choose(int index)
        {
            if (_offer == null || _offer.Length == 0)
            {
                return null;
            }

            if (index < 0)
            {
                index = 0;
            }

            if (index >= _offer.Length)
            {
                index = _offer.Length - 1;
            }

            SupportDef picked = _offer[index];
            _owned.Add(picked);
            _picks++;
            _offer = null;

            // 开局白送那一次：选完立刻再给一次机会，不占时段。
            if (_picks == 1 && !IsSupportExhausted)
            {
                _offer = BuildNormalOffer();
            }

            return picked;
        }

        /// <summary>
        /// 重抽当前这一组卡（「复诊单」给的）。没有在等选择、或者次数用完时返回 false。
        /// 重抽不会把已经拿过的支援还回来。
        /// </summary>
        public bool Reroll()
        {
            if (!AwaitingChoice || IsForcedOffer || RerollsLeft <= 0)
            {
                return false;
            }

            _rerollsUsed++;
            _offer = BuildNormalOffer();
            return true;
        }

        /// <summary>登记一次有效操作，攒进「记账」。返回这次折了多少账。</summary>
        public float NoteAction(PlayerAction action)
        {
            float credit = EndlessRecharge.WeightOf(action);
            _credits += credit;
            return credit;
        }

        // ---------------- 支援带来的数值 ----------------

        /// <summary>某条效果当前叠了几层。</summary>
        public int StackOf(SupportEffect effect)
        {
            int count = 0;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Effect == effect)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>是否已经拿到某条效果。</summary>
        public bool Has(SupportEffect effect)
        {
            return StackOf(effect) > 0;
        }

        /// <summary>
        /// 「依从性好」把已获得的支援效果整体放大。没有这条时是 1。
        /// 叠多层按连乘算（1.15 × 1.15）。
        /// </summary>
        public float AmplifyFactor
        {
            get
            {
                float factor = 1f;
                for (int i = 0; i < _owned.Count; i++)
                {
                    if (_owned[i].Effect == SupportEffect.AmplifyAll)
                    {
                        factor *= _owned[i].Value;
                    }
                }

                return factor;
            }
        }

        /// <summary>某条效果所有层的数值之和（已乘「依从性好」的加成）。</summary>
        public float TotalOf(SupportEffect effect)
        {
            float total = 0f;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Effect == effect)
                {
                    total += _owned[i].Value;
                }
            }

            return total * AmplifyFactor;
        }

        /// <summary>某条效果所有层的数值之积（倍数类效果用，已乘加成）。</summary>
        public float ProductOf(SupportEffect effect)
        {
            float product = 1f;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Effect == effect)
                {
                    product *= _owned[i].Value;
                }
            }

            return product;
        }

        /// <summary>某条效果里最大的那一层（已乘加成），没有就是 0。</summary>
        public float MaxOf(SupportEffect effect)
        {
            float best = 0f;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Effect == effect && _owned[i].Value > best)
                {
                    best = _owned[i].Value;
                }
            }

            return best * AmplifyFactor;
        }

        /// <summary>某个类别是不是被「加量」这类支援禁掉了。</summary>
        public bool IsBanned(SupportCategory category)
        {
            foreach (SupportCategory banned in BannedCategories)
            {
                if (banned == category)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>本局被禁选的类别（「加量」禁电力）。</summary>
        public IEnumerable<SupportCategory> BannedCategories
        {
            get
            {
                if (Has(SupportEffect.RiskNoPower))
                {
                    yield return SupportCategory.Power;
                }
            }
        }

        /// <summary>奶包会不会说真话。</summary>
        public bool MessagesTruthful
        {
            get { return Has(SupportEffect.TruthfulMessages); }
        }

        /// <summary>奶龙偷到电之后静止多少秒（0 = 没有这条支援）。</summary>
        public float NailongFreezeSeconds
        {
            get
            {
                return TotalOf(SupportEffect.NailongSilence);
            }
        }

        /// <summary>怪物读条（先兆）时长倍率，没拿到就是 1。</summary>
        public float TelegraphMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < _owned.Count; i++)
                {
                    if (_owned[i].Effect == SupportEffect.LongerTelegraph)
                    {
                        multiplier *= _owned[i].Value;
                    }
                }

                return multiplier;
            }
        }

        /// <summary>被监控看见就锁住的秒数（0 = 没有这条支援）。</summary>
        public float CameraLockSeconds
        {
            get
            {
                return TotalOf(SupportEffect.CameraLock);
            }
        }

        /// <summary>还能扑空几次。</summary>
        public int MissesLeft
        {
            get
            {
                int left = StackOf(SupportEffect.MissOnce) - _missesUsed;
                return left > 0 ? left : 0;
            }
        }

        /// <summary>消耗一次「扑空」。返回是否真的消耗掉了。</summary>
        public bool ConsumeMiss()
        {
            if (MissesLeft <= 0)
            {
                return false;
            }

            _missesUsed++;
            return true;
        }

        /// <summary>「省着点」让使用等级降几级。</summary>
        public int UsageLevelOffset
        {
            get { return Has(SupportEffect.UsageDown) ? -1 : 0; }
        }

        /// <summary>
        /// 把当前使用等级按支援打折（不低于系统的下限 1 级）。
        /// ⚠️ 什么都不开（已经是 1 级）时这条支援不生效 —— 待定是否允许降到半级。
        /// </summary>
        public int EffectiveUsageLevel(int current)
        {
            int level = current + UsageLevelOffset;
            return level < PowerSystem.MinUsageLevel ? PowerSystem.MinUsageLevel : level;
        }

        /// <summary>把整本账清空（重开一局）。</summary>
        public void Reset()
        {
            _owned.Clear();
            _offer = null;
            _credits = 0f;
            _hours = 0;
            _picks = 0;
            _missesUsed = 0;
            _rerollsUsed = 0;
        }

        // ---------------- 内部 ----------------

        /// <summary>第 picks 张卡最早在第几小时出现。</summary>
        private static int RequiredHour(int picks)
        {
            // 第 0、1 张都在开局（白送 + 立刻补的那次）；之后每两小时一张。
            return picks <= 1 ? 0 : (picks - 1) * HoursPerPick;
        }

        private float SettleRefund()
        {
            float credits = _credits;
            _credits = 0f;

            int stacks = StackOf(SupportEffect.ActionRefund);
            if (stacks <= 0)
            {
                return 0f;
            }

            float refund = RefundBaseGain + credits * RefundPerCredit * stacks;

            // 只拿了一层：有上限，防止刷电量。连拿两次：取消上限。
            if (stacks < RefundUncappedStack && refund > RefundCap)
            {
                refund = RefundCap;
            }

            return refund;
        }

        /// <summary>开局白送：三张一模一样的卡。</summary>
        private static SupportDef[] BuildForcedOffer()
        {
            SupportDef first = SupportCatalog.FirstPick;
            if (first == null)
            {
                first = SupportCatalog.All[0];
            }

            return new[] { first, first, first };
        }

        /// <summary>正常三选一：从「还没拿满的支援」里按稀有度加权抽不重复的三张。</summary>
        private SupportDef[] BuildNormalOffer()
        {
            var candidates = new List<SupportDef>();
            for (int i = 0; i < SupportCatalog.All.Length; i++)
            {
                SupportDef def = SupportCatalog.All[i];
                if (StackOf(def.Effect) >= def.MaxStack)
                {
                    continue;
                }

                // 「加量」把电力类整档禁掉。
                if (IsBanned(def.Category))
                {
                    continue;
                }

                candidates.Add(def);
            }

            int count = Math.Min(OfferSize, candidates.Count);
            var offer = new SupportDef[count];

            for (int i = 0; i < count; i++)
            {
                int index = WeightedIndex(candidates);
                offer[i] = candidates[index];
                candidates.RemoveAt(index);
            }

            return offer;
        }

        /// <summary>按稀有度权重随机取一个下标。</summary>
        private int WeightedIndex(List<SupportDef> candidates)
        {
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += SupportCatalog.WeightOf(candidates[i].Rarity);
            }

            if (total <= 0f)
            {
                return 0;
            }

            double roll = _rng.NextDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= SupportCatalog.WeightOf(candidates[i].Rarity);
                if (roll <= 0.0)
                {
                    return i;
                }
            }

            return candidates.Count - 1;
        }
    }
}
