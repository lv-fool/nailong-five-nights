using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 后半程难度加成（用户 2026-11-04 定）：
    ///   · 3AM 起 → **所有**非 0 的敌人 +1
    ///   · 4AM 起 → **除奶龙以外**再 +1
    ///
    /// 这两条是会反复调的设计参数，抽出来单独盯住。
    /// </summary>
    public class LateNightBoostTests
    {
        private const int Max = EnemyBrain.MaxActivityLevel;

        [Test]
        public void 三点之前不加成()
        {
            foreach (var id in EnemyIds.All)
            {
                for (int hour = 0; hour < NightClock.BoostFromHour; hour++)
                {
                    Assert.AreEqual(5, LateNightBoost.Apply(id, 5, hour),
                        id + " 在 " + hour + " 点不该有加成");
                }
            }
        }

        [Test]
        public void 三点起所有敌人加一()
        {
            foreach (var id in EnemyIds.All)
            {
                Assert.AreEqual(6, LateNightBoost.Apply(id, 5, 3),
                    id + " 在 3AM 应该 +1");
            }
        }

        [Test]
        public void 四点起除奶龙外再加一()
        {
            // 累加：袋鼠 / 牛来 / 噜噜 = +2，奶龙停在 +1
            foreach (var id in EnemyIds.All)
            {
                int expected = id == EnemyId.Nailong ? 6 : 7;
                Assert.AreEqual(expected, LateNightBoost.Apply(id, 5, 4),
                    id + " 在 4AM 的加成不对");
            }
        }

        [Test]
        public void 五点六点维持四点那一档()
        {
            foreach (var id in EnemyIds.All)
            {
                int at4 = LateNightBoost.Apply(id, 5, 4);
                Assert.AreEqual(at4, LateNightBoost.Apply(id, 5, 5), id + " 5AM 不该再涨");
                Assert.AreEqual(at4, LateNightBoost.Apply(id, 5, 6), id + " 6AM 不该再涨");
            }
        }

        [Test]
        public void 活动度为零时任何时刻都保持零()
        {
            // ⚠️ 0 表示「这一夜它压根不来」，抬到 1 等于把它请出场
            foreach (var id in EnemyIds.All)
            {
                for (int hour = 0; hour <= 6; hour++)
                {
                    Assert.AreEqual(0, LateNightBoost.Apply(id, 0, hour),
                        id + " 在 " + hour + " 点不该从 0 被抬起来");
                }
            }
        }

        [Test]
        public void 加成不会超过上限()
        {
            foreach (var id in EnemyIds.All)
            {
                int result = LateNightBoost.Apply(id, Max, 4);
                Assert.AreEqual(Max, result, id + " 加成后溢出了上限");
                Assert.LessOrEqual(result, EnemyBrain.MaxActivityLevel);
            }
        }

        [Test]
        public void 接近上限时按实际差额加()
        {
            // 19 在 4AM：袋鼠 +2 会到 21，必须夹回 20；奶龙 +1 正好 20
            Assert.AreEqual(Max, LateNightBoost.Apply(EnemyId.Daishu, Max - 1, 4));
            Assert.AreEqual(Max, LateNightBoost.Apply(EnemyId.Nailong, Max - 1, 4));

            // 19 在 3AM：全体 +1 → 20，正好顶到上限
            Assert.AreEqual(Max, LateNightBoost.Apply(EnemyId.Daishu, Max - 1, 3));
        }

        [Test]
        public void 负数当零处理()
        {
            foreach (var id in EnemyIds.All)
            {
                Assert.AreEqual(0, LateNightBoost.Apply(id, -3, 4), id + " 负数应该归 0");
            }
        }
    }
}
