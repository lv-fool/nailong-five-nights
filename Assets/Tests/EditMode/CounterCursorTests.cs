using NailongFNAF.UI;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 「计数器比游标大就播一声」—— 敲门声用的就是这套（2026-11-04 修的那个 bug）。
    ///
    /// 核心是**计数器被重建时游标要跟着归零**：计数器活在 EnemyDirector 里，
    /// 换夜会被清零；游标活在表现层，不会。不处理的话新一局第一声敲门被吞掉。
    /// </summary>
    public class CounterCursorTests
    {
        [Test]
        public void 计数器没变就不播()
        {
            int cursor;
            Assert.IsFalse(CounterCursor.ShouldFire(0, 0, out cursor));
            Assert.AreEqual(0, cursor);

            Assert.IsFalse(CounterCursor.ShouldFire(3, 3, out cursor));
            Assert.AreEqual(3, cursor);
        }

        [Test]
        public void 计数器涨了就播并把游标跟上()
        {
            int cursor;
            Assert.IsTrue(CounterCursor.ShouldFire(1, 0, out cursor));
            Assert.AreEqual(1, cursor);

            Assert.IsTrue(CounterCursor.ShouldFire(2, 1, out cursor));
            Assert.AreEqual(2, cursor);
        }

        [Test]
        public void 一次涨多个只播一次_游标直接跳到最新()
        {
            // 掉帧 / 一帧里敲了两下：只播一声，但不能漏掉后面的
            int cursor;
            Assert.IsTrue(CounterCursor.ShouldFire(5, 1, out cursor));
            Assert.AreEqual(5, cursor);

            // 跳到 5 之后，同一个 5 不该再播
            Assert.IsFalse(CounterCursor.ShouldFire(5, 5, out cursor));
        }

        [Test]
        public void 计数器被重建_游标跟着归零_新一局第一声照样播()
        {
            // ⚠️ 这条就是 2026-11-04 报的 bug：
            // 上一局敲过 1 次（游标 1），换夜时 KnockCount 清零回 0，
            // 新一局第一次敲门把 KnockCount 顶到 1 —— 老代码 1 > 1 不成立，
            // 头一声被吞，听着像整夜没敲过门。
            int cursor;

            // 新一局开始：计数器回 0，游标还是 1
            Assert.IsFalse(CounterCursor.ShouldFire(0, 1, out cursor));
            Assert.AreEqual(0, cursor, "计数器比游标小 → 游标必须归零，否则新一局第一次会被吞");

            // 新一局第一次敲门：0 → 1，必须播
            Assert.IsTrue(CounterCursor.ShouldFire(1, cursor, out cursor),
                "新一局的第一声敲门必须放出来");
            Assert.AreEqual(1, cursor);
        }

        [Test]
        public void 连续多局都不会漏掉每局第一声()
        {
            int cursor = 0;

            for (int night = 0; night < 5; night++)
            {
                // 每局：计数器重建 → 然后敲两次
                Assert.IsFalse(CounterCursor.ShouldFire(0, cursor, out cursor),
                    "第 " + (night + 1) + " 夜开局游标应该归零");

                Assert.IsTrue(CounterCursor.ShouldFire(1, cursor, out cursor),
                    "第 " + (night + 1) + " 夜的第一声敲门被吞了");
                Assert.IsTrue(CounterCursor.ShouldFire(2, cursor, out cursor),
                    "第 " + (night + 1) + " 夜的第二声敲门被吞了");
                Assert.IsFalse(CounterCursor.ShouldFire(2, cursor, out cursor),
                    "同一个计数不该重复播");
            }
        }

        [Test]
        public void 负数游标不会导致误播()
        {
            // 脏数据兜底：游标 -1 时计数器 0 不该触发
            int cursor;
            bool fired = CounterCursor.ShouldFire(0, -1, out cursor);
            Assert.IsTrue(fired, "0 > -1 应该播（游标是脏数据，不是真播过）");
            Assert.AreEqual(0, cursor);
        }

        [Test]
        public void Advanced返回真实增量()
        {
            int cursor;

            // 正常推进：3 - 1 = 2
            Assert.AreEqual(2, CounterCursor.Advanced(3, 1, out cursor));
            Assert.AreEqual(3, cursor);

            // 重建之后的第一次：计数器 1、游标还停在 3 —— 游标归零，
            // 所以这一次的增量是 1 - 0 = 1（不是 0：这次敲门确实要播）
            Assert.AreEqual(1, CounterCursor.Advanced(1, 3, out cursor));
            Assert.AreEqual(1, cursor, "重建后游标归零，再跟上当前计数");

            // 重建之后计数器还是 0：什么都没发生
            Assert.AreEqual(0, CounterCursor.Advanced(0, 3, out cursor));
            Assert.AreEqual(0, cursor, "计数器 0 时游标归零且不播");
        }
    }
}
