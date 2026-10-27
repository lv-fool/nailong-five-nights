using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 「贝利亚之夜」的解锁条件（2026-10-26 定）：
    /// 一开始完全隐藏；前五夜被咬死的次数攒够 <see cref="BelialUnlock.RequiredDeaths"/> 次，
    /// 并且通关第 5 夜，两个都齐了才开。
    /// </summary>
    public class BelialUnlockTests
    {
        [SetUp]
        public void SetUp()
        {
            BelialUnlock.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            BelialUnlock.Reset();
        }

        private static void Die(int times)
        {
            for (int i = 0; i < times; i++)
            {
                BelialUnlock.NoteDeath();
            }
        }

        [Test]
        public void 一开始是锁着的()
        {
            Assert.IsFalse(BelialUnlock.ClearedNight5);
            Assert.AreEqual(0, BelialUnlock.Deaths);
            Assert.IsFalse(BelialUnlock.IsUnlocked, "什么都没做的时候不该解锁");
        }

        [Test]
        public void 死够次数但第五夜没过还不算()
        {
            Die(BelialUnlock.RequiredDeaths);

            Assert.AreEqual(BelialUnlock.RequiredDeaths, BelialUnlock.Deaths);
            Assert.IsFalse(BelialUnlock.IsUnlocked, "第五夜还没通关");
            Assert.AreEqual("还差：通关第 5 夜", BelialUnlock.MissingCondition);
        }

        [Test]
        public void 通关第五夜但死得不够也不算()
        {
            BelialUnlock.MarkNight5Cleared();
            Die(BelialUnlock.RequiredDeaths - 1);

            Assert.IsFalse(BelialUnlock.IsUnlocked);
            Assert.AreEqual("还差：1 次", BelialUnlock.MissingCondition);
        }

        [Test]
        public void 两条都齐了才解锁()
        {
            BelialUnlock.MarkNight5Cleared();
            Die(BelialUnlock.RequiredDeaths);

            Assert.IsTrue(BelialUnlock.IsUnlocked);
            Assert.AreEqual(string.Empty, BelialUnlock.MissingCondition);
        }

        [Test]
        public void 两条先后顺序不影响结果()
        {
            Die(BelialUnlock.RequiredDeaths);
            BelialUnlock.MarkNight5Cleared();

            Assert.IsTrue(BelialUnlock.IsUnlocked);
        }

        [Test]
        public void 死亡次数可以一直累加()
        {
            Die(3);
            Assert.AreEqual(3, BelialUnlock.Deaths);

            Die(10);
            Assert.AreEqual(13, BelialUnlock.Deaths);
        }

        [Test]
        public void 通关记录不会因为重复写入而变()
        {
            BelialUnlock.MarkNight5Cleared();
            BelialUnlock.MarkNight5Cleared();

            Assert.IsTrue(BelialUnlock.ClearedNight5);
        }

        [Test]
        public void 清档之后重新锁上()
        {
            BelialUnlock.MarkNight5Cleared();
            Die(BelialUnlock.RequiredDeaths);
            Assert.IsTrue(BelialUnlock.IsUnlocked);

            BelialUnlock.Reset();

            Assert.IsFalse(BelialUnlock.IsUnlocked);
            Assert.AreEqual(0, BelialUnlock.Deaths);
        }
    }
}
