using NailongFNAF.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 自定义夜配置：四个敌人的活动度各 0 ~ 20，存档读出不变、越界要夹住。
    /// </summary>
    public class CustomNightConfigTests
    {
        [SetUp]
        public void SetUp()
        {
            ClearPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            ClearPrefs();
        }

        private static void ClearPrefs()
        {
            foreach (EnemyId id in EnemyIds.All)
            {
                PlayerPrefs.DeleteKey(CustomNightConfig.KeyFor(id));
            }
        }

        [Test]
        public void 默认全零()
        {
            var config = new CustomNightConfig();

            foreach (EnemyId id in EnemyIds.All)
            {
                Assert.AreEqual(0, config.GetLevel(id));
            }

            Assert.AreEqual(0, config.TotalLevel());
        }

        [Test]
        public void 设置后读出相同值()
        {
            var config = new CustomNightConfig();
            config.SetLevel(EnemyId.Niulai, 7);
            config.SetLevel(EnemyId.Daishu, 3);
            config.SetLevel(EnemyId.Lulu, 20);
            config.SetLevel(EnemyId.Nailong, 1);

            Assert.AreEqual(7, config.GetLevel(EnemyId.Niulai));
            Assert.AreEqual(3, config.GetLevel(EnemyId.Daishu));
            Assert.AreEqual(20, config.GetLevel(EnemyId.Lulu));
            Assert.AreEqual(1, config.GetLevel(EnemyId.Nailong));
            Assert.AreEqual(31, config.TotalLevel());
        }

        [Test]
        public void 越界自动夹在0到20()
        {
            var config = new CustomNightConfig();
            config.SetLevel(EnemyId.Niulai, -5);
            config.SetLevel(EnemyId.Lulu, 99);

            Assert.AreEqual(0, config.GetLevel(EnemyId.Niulai));
            Assert.AreEqual(EnemyBrain.MaxActivityLevel, config.GetLevel(EnemyId.Lulu));
        }

        [Test]
        public void SetAll统一设置()
        {
            var config = new CustomNightConfig();
            config.SetAll(20);

            Assert.AreEqual(EnemyBrain.MaxActivityLevel * EnemyIds.All.Length, config.TotalLevel());
        }

        [Test]
        public void 存档读回不变()
        {
            var config = new CustomNightConfig();
            config.SetLevel(EnemyId.Niulai, 12);
            config.SetLevel(EnemyId.Daishu, 10);
            config.SetLevel(EnemyId.Lulu, 16);
            config.SetLevel(EnemyId.Nailong, 4);
            config.Save();

            CustomNightConfig loaded = CustomNightConfig.Load();

            foreach (EnemyId id in EnemyIds.All)
            {
                Assert.AreEqual(config.GetLevel(id), loaded.GetLevel(id), id.ToString());
            }
        }

        [Test]
        public void 没存过档读出来是全零()
        {
            CustomNightConfig loaded = CustomNightConfig.Load();

            Assert.AreEqual(0, loaded.TotalLevel());
        }
    }

    /// <summary>
    /// 成就存储：解锁一次、重复解锁不报新、未知 id 不解锁。
    /// </summary>
    public class AchievementStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            AchievementStore.ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            AchievementStore.ResetAll();
        }

        [Test]
        public void 初始全部未解锁()
        {
            Assert.AreEqual(0, AchievementStore.UnlockedCount());

            foreach (AchievementCatalog.Def def in AchievementCatalog.All)
            {
                Assert.IsFalse(AchievementStore.IsUnlocked(def.Id), def.Id);
            }
        }

        [Test]
        public void 解锁返回true且状态落盘()
        {
            Assert.IsTrue(AchievementStore.Unlock(AchievementCatalog.FirstNight));
            Assert.IsTrue(AchievementStore.IsUnlocked(AchievementCatalog.FirstNight));
            Assert.AreEqual(1, AchievementStore.UnlockedCount());
        }

        [Test]
        public void 重复解锁返回false()
        {
            AchievementStore.Unlock(AchievementCatalog.FirstNight);

            Assert.IsFalse(AchievementStore.Unlock(AchievementCatalog.FirstNight));
            Assert.AreEqual(1, AchievementStore.UnlockedCount());
        }

        [Test]
        public void 未知id不解锁()
        {
            Assert.IsFalse(AchievementStore.Unlock("not_an_achievement"));
            Assert.IsFalse(AchievementStore.IsUnlocked("not_an_achievement"));
            Assert.AreEqual(0, AchievementStore.UnlockedCount());
        }

        [Test]
        public void 清单里没有重复id()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (AchievementCatalog.Def def in AchievementCatalog.All)
            {
                Assert.IsTrue(seen.Add(def.Id), "重复的成就 id：" + def.Id);
                Assert.IsNotEmpty(def.Title);
                Assert.IsNotEmpty(def.Description);
            }
        }

        [Test]
        public void ResetAll清空全部()
        {
            AchievementStore.Unlock(AchievementCatalog.FirstNight);
            AchievementStore.Unlock(AchievementCatalog.FiveNights);

            AchievementStore.ResetAll();

            Assert.AreEqual(0, AchievementStore.UnlockedCount());
        }
    }
}
