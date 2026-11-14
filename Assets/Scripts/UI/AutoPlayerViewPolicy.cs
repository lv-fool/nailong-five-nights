using NailongFNAF.Gameplay;

namespace NailongFNAF.UI
{
    /// <summary>
    /// AI 代打每个行为要转到的视角 —— 纯逻辑，不引用 UnityEngine，可脱机测试。
    ///
    /// ⚠️ 用户 2026-11-13 定：**AI 做行为必须转到对应角度**，行为结束后**不必回正**。
    /// （这条推翻了 2026-11-03 的「只有闪手电才动视角」。）
    ///
    /// 各行为的对应角度：
    /// <list type="bullet">
    /// <item>左门 / 左灯 / 左门外外卖 → 左侧</item>
    /// <item>右门 / 右灯 → 右侧</item>
    /// <item>闪正门噜噜 → 正前方（手电判定只认 <c>View == Front</c>）</item>
    /// <item>接通桌上手机 → 正前方（手机就摆在桌面正中）</item>
    /// </list>
    ///
    /// 抽成纯逻辑是为了：① 规则只有一处，不会在几个分支里各写一遍；
    /// ② 能写单元测试盯着，防止以后又被「顺手」改回只回正前方或干脆不动视角。
    /// </summary>
    public static class AutoPlayerViewPolicy
    {
        /// <summary>
        /// 侧门 / 侧灯 / 那一侧门外的东西 → 转到对应那一侧。
        /// </summary>
        public static ViewSide ForSide(Side side)
        {
            return side == Side.Left ? ViewSide.Left : ViewSide.Right;
        }

        /// <summary>转头的目标量：左 -1 / 正前 0 / 右 +1（对应 <c>PrototypeScene.ViewExternalPan</c>）。</summary>
        public static float PanFor(ViewSide side)
        {
            switch (side)
            {
                case ViewSide.Left:
                    return -1f;

                case ViewSide.Right:
                    return 1f;

                default:
                    return 0f;
            }
        }
    }
}