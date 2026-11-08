using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 监控画面动态（REC 红点 + 摄像头呼吸）的纯逻辑测试。
    /// 2026-11-08 方向二「让画面活起来」的一部分。
    /// </summary>
    public class MonitorLivelinessTests
    {
        // ---------------- RecordBlink ----------------

        [Test]
        public void 红点前半秒亮后半秒灭()
        {
            Assert.IsTrue(RecordBlink.IsOn(0f), "0 秒起亮");
            Assert.IsTrue(RecordBlink.IsOn(0.49f), "0.49 还在亮");
            Assert.IsFalse(RecordBlink.IsOn(0.5f), "0.5 起灭");
            Assert.IsFalse(RecordBlink.IsOn(0.99f), "0.99 还在灭");
        }

        [Test]
        public void 红点周期严格一秒()
        {
            for (int i = 0; i < 60; i++)
            {
                float t = i * 0.137f;
                Assert.AreEqual(RecordBlink.IsOn(t), RecordBlink.IsOn(t + 1f),
                    "t=" + t + " 一秒后状态必须相同");
            }
        }

        [Test]
        public void 红点负时间按零处理不抛异常()
        {
            Assert.IsTrue(RecordBlink.IsOn(-5f), "负数当 0 → 亮");
            Assert.IsTrue(RecordBlink.IsOn(-0.2f));
        }

        [Test]
        public void 红点长时间运行不漂移()
        {
            // 一夜约 540 秒。用大数验证取模没有精度事故（float 在 540 处
            // 分辨率约 6e-5，远离 0.5 的边界）
            Assert.IsTrue(RecordBlink.IsOn(540.1f));
            Assert.IsFalse(RecordBlink.IsOn(540.6f));
        }

        // ---------------- CameraBreath ----------------

        [Test]
        public void 呼吸输出恒在正负百分之三()
        {
            for (int cam = 0; cam < 11; cam++)
            {
                for (int i = 0; i <= 100; i++)
                {
                    float t = i * 0.31f;
                    float v = CameraBreath.Value(cam, t);
                    Assert.GreaterOrEqual(v, 1f - CameraBreath.Amplitude - 1e-5f,
                        "cam=" + cam + " t=" + t + " 低于下限: " + v);
                    Assert.LessOrEqual(v, 1f + CameraBreath.Amplitude + 1e-5f,
                        "cam=" + cam + " t=" + t + " 高于上限: " + v);
                }
            }
        }

        [Test]
        public void 相邻机位不同相位()
        {
            // 黄金角递推 → 任意两路相邻 CAM 在同一时刻的差
            // 不该小到察觉不出「不是同一台机器」
            for (int cam = 0; cam < 10; cam++)
            {
                float maxGap = 0f;
                for (int i = 0; i < 40; i++)
                {
                    float t = i * 0.4f;
                    float gap = System.Math.Abs(CameraBreath.Value(cam, t)
                        - CameraBreath.Value(cam + 1, t));
                    if (gap > maxGap) { maxGap = gap; }
                }
                Assert.Greater(maxGap, 0.01f,
                    "cam" + cam + " 和 cam" + (cam + 1) + " 几乎同呼吸（最大差只有 "
                    + maxGap.ToString("F4") + "）");
            }
        }

        [Test]
        public void 呼吸是慢周期不是闪烁()
        {
            // 周期 15 秒 → 0.4 秒内变化必须很小（<1% 幅度），
            // 否则就成了闪烁，和 CRT 抖动混在一起没法看
            float v0 = CameraBreath.Value(3, 20f);
            float v1 = CameraBreath.Value(3, 20.4f);
            Assert.Less(System.Math.Abs(v1 - v0), 0.01f);
        }

        [Test]
        public void 呼吸周期精确十五秒()
        {
            for (int cam = 0; cam < 11; cam++)
            {
                float a = CameraBreath.Value(cam, 7.7f);
                float b = CameraBreath.Value(cam, 7.7f + CameraBreath.PeriodSeconds);
                Assert.AreEqual(a, b, 1e-4f, "cam=" + cam + " 一个周期后应回到同值");
            }
        }
    }
}
