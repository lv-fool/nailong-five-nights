namespace NailongFNAF.UI
{
    /// <summary>设置菜单「显示比例」的档位。</summary>
    public enum DisplayAspect
    {
        /// <summary>跟随当前窗口，不做任何改动。</summary>
        Auto = 0,

        /// <summary>16:9（1920×1080 那一系）。</summary>
        Widescreen = 1,

        /// <summary>16:10。</summary>
        Wide16x10 = 2,

        /// <summary>4:3。</summary>
        Standard = 3
    }

    /// <summary>
    /// 设置菜单「显示比例」的纯逻辑 —— 不引用 UnityEngine，可脱机测试。
    ///
    /// 做法：选一个比例后，把**窗口**调整成该比例（不是加黑边）。
    /// 这样 UGUI、监控 16:9 设计区、墙按钮投影全都不用动 —— 它们本来就是
    /// 按窗口尺寸自适应的；加黑边反而要重新处理 HUD / 监控 / 投影坐标。
    ///
    /// 尺寸从**桌面可用高度**反推：优先 1080 高，放不下就按屏幕留边缩，
    /// 宽了再按宽度反推高度，保证窗口完整落在屏幕里。
    /// </summary>
    public static class DisplayAspectPolicy
    {
        /// <summary>循环下一档：自动 → 16:9 → 16:10 → 4:3 → 自动。</summary>
        public static DisplayAspect Next(DisplayAspect current)
        {
            switch (current)
            {
                case DisplayAspect.Auto:
                    return DisplayAspect.Widescreen;

                case DisplayAspect.Widescreen:
                    return DisplayAspect.Wide16x10;

                case DisplayAspect.Wide16x10:
                    return DisplayAspect.Standard;

                case DisplayAspect.Standard:
                    return DisplayAspect.Auto;

                default:
                    return DisplayAspect.Auto;
            }
        }

        /// <summary>菜单文字。</summary>
        public static string Label(DisplayAspect aspect)
        {
            switch (aspect)
            {
                case DisplayAspect.Widescreen:
                    return "16:9";

                case DisplayAspect.Wide16x10:
                    return "16:10";

                case DisplayAspect.Standard:
                    return "4:3";

                default:
                    return "自动";
            }
        }

        /// <summary>取比例的分子 / 分母；自动返回 false。</summary>
        public static bool TryGetAspect(DisplayAspect aspect, out int width, out int height)
        {
            switch (aspect)
            {
                case DisplayAspect.Widescreen:
                    width = 16;
                    height = 9;
                    return true;

                case DisplayAspect.Wide16x10:
                    width = 16;
                    height = 10;
                    return true;

                case DisplayAspect.Standard:
                    width = 4;
                    height = 3;
                    return true;

                default:
                    width = 0;
                    height = 0;
                    return false;
            }
        }

        /// <summary>玩家存档里的整数 → 档位（越界回自动）。</summary>
        public static DisplayAspect FromInt(int value)
        {
            if (value < (int)DisplayAspect.Auto || value > (int)DisplayAspect.Standard)
            {
                return DisplayAspect.Auto;
            }

            return (DisplayAspect)value;
        }
    }
}