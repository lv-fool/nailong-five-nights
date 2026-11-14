namespace NailongFNAF.UI
{
    /// <summary>
    /// 全屏氛围后处理（方案 A）的纯逻辑规则 —— 不引用 UnityEngine，可脱机测试。
    ///
    /// 规则来源：`docs/11-画面技术对照.md` 方案 A。
    /// 目标：Bloom / 暗角 / 颗粒 / 色差只在最终画面上叠一层「恐怖片镜头」，
    /// 不碰现有全景、门、立绘、监控 CRT。
    /// </summary>
    public static class ScreenPostPolicy
    {
        /// <summary>
        /// 开启 Bloom 需要的最低画质档：2 = Medium。
        ///
        /// 默认运行时是 Low（1），所以**默认不跑 Bloom 的降采样/模糊 pass**，
        /// 只保留暗角 + 颗粒 + 色差（几乎零成本）。调到 Medium 以上才开 Bloom。
        /// </summary>
        public const int BloomMinimumQualityLevel = 2;

        /// <summary>监控升起时后处理强度打几折 —— 监控画面不该被氛围层抢戏。</summary>
        public const float MonitorIntensityScale = 0.5f;

        /// <summary>设置菜单里「强度 + / -」一步走多少。</summary>
        public const float IntensityStep = 0.05f;

        /// <summary>这一刻的 Bloom 该不该跑。</summary>
        public static bool BloomAllowed(int qualityLevel)
        {
            return qualityLevel >= BloomMinimumQualityLevel;
        }

        /// <summary>设置菜单：按 <paramref name="delta"/> 步进强度，结果夹在 0 ~ 1。</summary>
        public static float StepIntensity(float current, int delta)
        {
            return Clamp01(current + delta * IntensityStep);
        }

        /// <summary>
        /// 设置菜单的「画质」循环：Low(1) → Medium(2) → High(3) → Low(1)。
        /// 只在这三档之间转 —— Very Low 没有意义、Very High/Ultra 对这个 2D 游戏也用不上。
        /// 传进来的档位不规范时统一回到 Low。
        /// </summary>
        public static int NextQuality(int current)
        {
            switch (current)
            {
                case 1:
                    return 2;   // Low → Medium

                case 2:
                    return 3;   // Medium → High

                default:
                    return 1;   // High 或任何不规范档位 → Low
            }
        }

        /// <summary>
        /// 实际生效的总强度：夹在 0 ~ 1；监控升起时乘 <see cref="MonitorIntensityScale"/>。
        /// </summary>
        public static float EffectiveIntensity(float baseIntensity, bool monitorUp)
        {
            float v = Clamp01(baseIntensity);

            if (monitorUp)
            {
                v *= MonitorIntensityScale;
            }

            return Clamp01(v);
        }

        /// <summary>0 ~ 1 夹取（负数和超界都收回来，避免 shader 参数失控）。</summary>
        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}