namespace NailongFNAF.UI
{
    /// <summary>
    /// 监控画面左上角「REC ●」红点的闪烁节奏 —— **纯逻辑，不引用 UnityEngine**，可脱机测试。
    ///
    /// 官方 FNAF 监控就是 1 秒一闪：亮 0.5 秒、灭 0.5 秒。
    /// 刻意不用平滑呼吸（PingPong 之类）—— 老录像机的红点是**硬开关**，
    /// 平滑反而假。
    /// </summary>
    public static class RecordBlink
    {
        /// <summary>完整周期（秒）。</summary>
        public const float PeriodSeconds = 1f;

        /// <summary>一个周期里亮着的时间（秒）。亮灭各半。</summary>
        public const float OnSeconds = 0.5f;

        /// <summary>
        /// 时刻 <paramref name="t"/>（秒，累计时间）红点是否亮着。
        ///
        /// 负数按 0 处理 —— 时间不该为负，但断电回卷这类边界传进来时不许抛异常，
        /// 表现层一帧都闪不得。
        /// </summary>
        public static bool IsOn(float t)
        {
            if (t < 0f)
            {
                t = 0f;
            }

            return (t % PeriodSeconds) < OnSeconds;
        }
    }
}
