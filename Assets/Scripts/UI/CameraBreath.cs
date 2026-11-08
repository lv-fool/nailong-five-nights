using System;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 每路监控摄像头的「呼吸」亮度 —— 模拟老摄像机的自动曝光起伏。
    /// **纯逻辑，不引用 UnityEngine**，可脱机测试。
    ///
    /// ## 为什么每路 CAM 相位不同
    /// 同一把尺（全局时间）+ 不同相位 → 切台后明暗节奏明显不一样，
    /// 玩家能感觉到「这是另一台机器在看」，而不是同一张图换了个标签。
    ///
    /// 相位用黄金角（0.618034 圈）递推：任意相邻两路 CAM 的相位差都够大，
    /// 不会出现 0 号和 6 号「同呼吸」的巧合。
    ///
    /// ## 幅度
    /// ±3%。再大就会被当成画面故障而不是曝光 —— 这个效果要能被感觉到，
    /// 但不能被注意到。
    /// </summary>
    public static class CameraBreath
    {
        /// <summary>亮度起伏幅度（±0.03 = 3%）。</summary>
        public const float Amplitude = 0.03f;

        /// <summary>呼吸周期（秒）。慢到不会和 CRT 抖动频率混在一起。</summary>
        public const float PeriodSeconds = 15f;

        /// <summary>相邻机位的相位递进（黄金角，单位：圈）。</summary>
        public const float PhaseStepCycles = 0.618034f;

        /// <summary>
        /// 时刻 <paramref name="t"/> 下机位 <paramref name="cameraIndex"/> 的亮度乘数。
        /// 输出恒在 [0.97, 1.03]。
        /// </summary>
        public static float Value(int cameraIndex, float t)
        {
            double phase = (cameraIndex * PhaseStepCycles + t / PeriodSeconds) * 2.0 * Math.PI;
            return 1f + Amplitude * (float)Math.Sin(phase);
        }
    }
}
