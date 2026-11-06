using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 鼠标 → 转头量的映射（用户 2026-11-03：「镜头跟随太晃」）。
    ///
    /// 这个测试盯三件事，都是踩过或者算出来会踩的：
    /// 1. **中心死区真的不动** —— 鼠标在屏幕中间那一小段，画面必须纹丝不动
    /// 2. **仍然能满转** —— 加了死区如果满转不了，侧门就再也看不见了
    /// 3. **曲线不会中间更灵敏** —— 用 smoothstep 之类的「平滑」曲线，中间段斜率会大于
    ///    线性，反而更晃。这里用数值微分把斜率钉死，防止以后有人「优化」成曲线。
    /// </summary>
    public class ViewPanMappingTests
    {
        private const float DeadZone = ViewPanMapping.DefaultDeadZone;
        private const float Span = ViewPanMapping.DefaultSpan;

        /// <summary>浮点比较容差。</summary>
        private const float Epsilon = 0.0001f;

        /// <summary>饱和起点：过了这个鼠标位置，转头量就一直是满的。</summary>
        private const float SaturationStart = 0.5f + DeadZone + Span;

        [Test]
        public void 中心死区里转头量恒为零()
        {
            // 死区半宽 0.10 → [0.40, 0.60] 这一段。
            // 边界值也测：<= 死区就算死区，不要留一条缝
            for (float x = 0.5f - DeadZone; x <= 0.5f + DeadZone + Epsilon; x += 0.0025f)
            {
                Assert.AreEqual(0f, ViewPanMapping.Map(x, DeadZone, Span), Epsilon,
                    "x=" + x + " 落在死区里，镜头不该动");
            }
        }

        [Test]
        public void 死区外沿之后开始转且单调递增()
        {
            // 从死区右沿往右扫到屏幕底边，pan 必须严格不降
            float previous = 0f;
            for (float x = 0.5f + DeadZone; x <= 1.0001f; x += 0.002f)
            {
                float pan = ViewPanMapping.Map(x, DeadZone, Span);
                Assert.GreaterOrEqual(pan, previous - Epsilon,
                    "x=" + x + " 处转头量回退了（" + previous + " → " + pan + "）");
                previous = pan;
            }
        }

        [Test]
        public void 推到边角能满转()
        {
            // 满转是硬需求：门洞在 ±77°~103°、墙按钮在 ±106.6°，
            // 满转角上不去，侧门和按钮就永远进不了画面
            Assert.AreEqual(1f, ViewPanMapping.Map(1f, DeadZone, Span), Epsilon, "屏幕最右要满转");
            Assert.AreEqual(-1f, ViewPanMapping.Map(0f, DeadZone, Span), Epsilon, "屏幕最左要满转");
        }

        [Test]
        public void 饱和区里转头量不再变化()
        {
            // 饱和起点 = 0.5 + 死区 + 活跃区 = 0.75。过了它，鼠标再怎么挪都是满转 ——
            // 这是墙按钮能「压住」的前提（饱和区内 dx/dmouse = 0）
            for (float x = SaturationStart; x <= 1.0001f; x += 0.01f)
            {
                Assert.AreEqual(1f, ViewPanMapping.Map(x, DeadZone, Span), Epsilon,
                    "x=" + x + " 已在饱和区，应该一直是满转");
            }
        }

        [Test]
        public void 左右完全对称()
        {
            for (float offset = 0f; offset <= 0.5f; offset += 0.01f)
            {
                float right = ViewPanMapping.Map(0.5f + offset, DeadZone, Span);
                float left = ViewPanMapping.Map(0.5f - offset, DeadZone, Span);
                Assert.AreEqual(-right, left, Epsilon,
                    "偏 " + offset + " 时左右不对称：" + left + " vs " + right);
            }
        }

        [Test]
        public void 活跃区里斜率不超过线性的理论值()
        {
            // ⚠️ 这条是防回归的核心。smoothstep 一类「平滑」曲线在中间段的斜率
            // 会是线性值的 1.5 倍 —— 手感上就是「中间更晃」，正是这次要治的病。
            float theoretical = 1f / Span;
            const float step = 0.0005f;

            for (float x = 0.5f + DeadZone + step; x < SaturationStart - step; x += step)
            {
                float slope = (ViewPanMapping.Map(x + step, DeadZone, Span)
                    - ViewPanMapping.Map(x, DeadZone, Span)) / step;

                Assert.LessOrEqual(slope, theoretical + 0.01f,
                    "x=" + x + " 处斜率 " + slope + " 超过线性值 " + theoretical +
                    " —— 中间段比线性更灵敏，会晃");
            }
        }

        [Test]
        public void 取消死区后退回原来的线性映射()
        {
            // 死区设 0 应该等价于老行为，方便对比和回退
            Assert.AreEqual(0f, ViewPanMapping.Map(0.5f, 0f, 0.20f), Epsilon);
            Assert.AreEqual(0.5f, ViewPanMapping.Map(0.6f, 0f, 0.20f), Epsilon);
            Assert.AreEqual(1f, ViewPanMapping.Map(0.7f, 0f, 0.20f), Epsilon);
        }

        [Test]
        public void 参数退化时不会炸()
        {
            // span <= 0 应该是「永远正前方」，不是除零或 NaN
            Assert.AreEqual(0f, ViewPanMapping.Map(0.9f, 0.05f, 0f), Epsilon);
            Assert.AreEqual(0f, ViewPanMapping.Map(0.9f, 0.05f, -1f), Epsilon);

            // 负死区当 0 处理
            Assert.AreEqual(ViewPanMapping.Map(0.9f, 0f, 0.20f), ViewPanMapping.Map(0.9f, -0.5f, 0.20f), Epsilon);

            // 输入超出 0~1 也要钳住，不能转过头
            Assert.AreEqual(1f, ViewPanMapping.Map(2f, 0.05f, 0.20f), Epsilon);
            Assert.AreEqual(-1f, ViewPanMapping.Map(-1f, 0.05f, 0.20f), Epsilon);
        }

        [Test]
        public void 默认档位是死区零点一加活跃区零点一五()
        {
            // 死区 0.10 + 活跃区 0.15 → 饱和起点 0.75，和调大死区之前一样。
            // 常量被人顺手改掉的话这里会先炸：饱和起点一旦超过 0.75，
            // 宽屏（3.15:1 以上）鼠标推到边也压不住墙按钮了。
            Assert.AreEqual(0.10f, ViewPanMapping.DefaultDeadZone, Epsilon);
            Assert.AreEqual(0.15f, ViewPanMapping.DefaultSpan, Epsilon);

            float saturationStart = 0.5f + ViewPanMapping.DefaultDeadZone + ViewPanMapping.DefaultSpan;
            Assert.AreEqual(0.75f, saturationStart, Epsilon,
                "饱和起点变了 —— 墙按钮在宽屏下会点不着，先看 ViewPanMapping 的注释");
        }
    }
}
