namespace NailongFNAF.UI
{
    /// <summary>
    /// 「显示比例」的黑边计算 —— 纯逻辑，不引用 UnityEngine，可脱机测试。
    ///
    /// 给窗口尺寸和目标比例，算出一个**居中的安全区**（归一化 0~1，左下原点）：
    /// 目标比窗口宽 → 上下加黑边（letterbox）；目标比窗口窄 → 左右加黑边（pillarbox）。
    /// 自动档 = 整屏，不留黑边。
    /// </summary>
    public static class LetterboxPolicy
    {
        /// <summary>
        /// 算安全区。返回值单位是归一化屏幕坐标（0~1）。
        /// </summary>
        /// <param name="aspect">目标显示比例（自动 = 整屏）</param>
        /// <param name="screenWidth">窗口宽（像素）</param>
        /// <param name="screenHeight">窗口高（像素）</param>
        public static void Compute(DisplayAspect aspect,
            int screenWidth, int screenHeight,
            out float x, out float y, out float width, out float height)
        {
            x = 0f;
            y = 0f;
            width = 1f;
            height = 1f;

            int aw, ah;
            if (!DisplayAspectPolicy.TryGetAspect(aspect, out aw, out ah))
            {
                return;   // 自动 / 非法：整屏
            }

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }

            float target = aw / (float)ah;
            float screen = screenWidth / (float)screenHeight;

            if (screen > target)
            {
                // 窗口比目标宽 → 左右黑边（pillarbox）
                width = target / screen;
                height = 1f;
                x = (1f - width) * 0.5f;
            }
            else
            {
                // 窗口比目标窄 → 上下黑边（letterbox）
                height = screen / target;
                width = 1f;
                y = (1f - height) * 0.5f;
            }
        }
    }
}