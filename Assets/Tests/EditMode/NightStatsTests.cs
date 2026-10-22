using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 一夜的账本（<see cref="NightStats"/>）：门/灯/监控计数、机位去重、
    /// 厨房停留计时、和奶龙对视、结束在第几小时 —— 彩蛋成就全靠它。
    /// </summary>
    public class NightStatsTests
    {
        [TearDown]
        public void TearDown()
        {
            // 成就判定会碰到 PlayerPrefs，别把测试状态留给下一次
            AchievementStore.ResetAll();
        }

        [Test]
        public void 新账本全是零()
        {
            var stats = new NightStats();

            Assert.AreEqual(0, stats.DoorToggles);
            Assert.AreEqual(0, stats.LightToggles);
            Assert.AreEqual(0, stats.MonitorToggles);
            Assert.AreEqual(0, stats.CameraChanges);
            Assert.AreEqual(0, stats.NailongLooks);
            Assert.AreEqual(0f, stats.KitchenSeconds);
            Assert.AreEqual(0, stats.VisitedCameraCount);
            Assert.IsFalse(stats.HasEnded);
        }

        [Test]
        public void 门和灯各记各的()
        {
            var stats = new NightStats();

            stats.NoteDoorToggle();
            stats.NoteDoorToggle();
            stats.NoteLightToggle();

            Assert.AreEqual(2, stats.DoorToggles);
            Assert.AreEqual(1, stats.LightToggles);
            Assert.AreEqual(3, stats.TotalSwitchToggles);
        }

        [Test]
        public void 同一个机位看两次只算一个角落()
        {
            var stats = new NightStats();

            stats.NoteCamera(3);
            stats.NoteCamera(3);
            stats.NoteCamera(7);

            Assert.AreEqual(2, stats.VisitedCameraCount);
            Assert.AreEqual(3, stats.CameraChanges);
            Assert.IsTrue(stats.VisitedCamera(3));
            Assert.IsFalse(stats.VisitedCamera(4));
        }

        [Test]
        public void 十一个机位看完才算全角落()
        {
            var stats = new NightStats();

            for (int i = 0; i < 10; i++)
            {
                stats.NoteCamera(i);
            }

            Assert.IsFalse(stats.HasVisitedAll(11));

            stats.NoteCamera(10);

            Assert.IsTrue(stats.HasVisitedAll(11));
            Assert.IsFalse(stats.HasVisitedAll(12));
            Assert.IsFalse(stats.HasVisitedAll(0));
            Assert.IsFalse(stats.HasVisitedAll(33));
        }

        [Test]
        public void 机位下标越界不记()
        {
            var stats = new NightStats();

            stats.NoteCamera(-1);
            stats.NoteCamera(NightStats.MaxCameras);

            Assert.AreEqual(0, stats.VisitedCameraCount);
            Assert.AreEqual(0, stats.CameraChanges);
        }

        [Test]
        public void 厨房只在监控升起且停在厨房时计时()
        {
            var stats = new NightStats();
            int kitchen = NightStats.KitchenCameraIndex;

            stats.Tick(1f, false, kitchen);          // 监控没升起
            stats.Tick(1f, true, kitchen + 1);       // 别的机位
            stats.Tick(0f, true, kitchen);           // dt 为 0

            Assert.AreEqual(0f, stats.KitchenSeconds);

            stats.Tick(20f, true, kitchen);

            Assert.AreEqual(20f, stats.KitchenSeconds);
        }

        [Test]
        public void 和奶龙对视按次数记()
        {
            var stats = new NightStats();

            stats.NoteNailongLook();
            stats.NoteNailongLook();
            stats.NoteNailongLook();

            Assert.AreEqual(3, stats.NailongLooks);
        }

        [Test]
        public void 结束时刻按游戏内小时换算()
        {
            var stats = new NightStats();

            stats.MarkEnded(85f * 5.9f, 85f);

            Assert.IsTrue(stats.HasEnded);
            Assert.IsTrue(stats.EndedAfterHour(5.8333f));
            Assert.IsFalse(stats.EndedBeforeHour(0.5f));
        }

        [Test]
        public void 没结束时不判死活时刻()
        {
            var stats = new NightStats();

            Assert.IsFalse(stats.EndedAfterHour(5.8333f));
            Assert.IsFalse(stats.EndedBeforeHour(0.5f));
        }

        [Test]
        public void 死在很早的夜里算零点二十()
        {
            var stats = new NightStats();

            stats.MarkEnded(85f * 0.4f);   // 不传 secondsPerHour 也按 85 算

            Assert.IsTrue(stats.EndedBeforeHour(0.5f));
            Assert.IsFalse(stats.EndedAfterHour(5.8333f));
        }

        [Test]
        public void 隐藏成就未解锁时遮起来()
        {
            AchievementStore.ResetAll();

            AchievementCatalog.Def egg = AchievementCatalog.Find(AchievementCatalog.DoorKeeper);
            AchievementCatalog.Def normal = AchievementCatalog.Find(AchievementCatalog.FirstNight);

            Assert.IsTrue(egg.Hidden);
            Assert.IsTrue(AchievementCatalog.ShouldMask(egg));
            Assert.IsFalse(AchievementCatalog.ShouldMask(normal));

            AchievementStore.Unlock(AchievementCatalog.DoorKeeper);

            Assert.IsFalse(AchievementCatalog.ShouldMask(egg), "解锁之后要露出真名");
        }

        [Test]
        public void 每条成就都有名字且彩蛋带旁白()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (AchievementCatalog.Def def in AchievementCatalog.All)
            {
                Assert.IsTrue(seen.Add(def.Id), "重复的成就 id：" + def.Id);
                Assert.IsNotEmpty(def.Title);
                Assert.IsNotEmpty(def.Description);

                if (def.Hidden)
                {
                    Assert.IsNotEmpty(def.Flavor, "彩蛋「" + def.Title + "」应该有世界观旁白");
                }
            }

            // 隐藏夜暂时移除后，「让影子先走」那条跟着撤了 → 19 变 18。
            // 恢复隐藏夜时改回 19。
            Assert.AreEqual(18, AchievementCatalog.All.Length);
        }

        [Test]
        public void 彩蛋判定用到的阈值都够得着()
        {
            var stats = new NightStats();

            for (int i = 0; i < 12; i++)
            {
                stats.NoteDoorToggle();
            }

            Assert.GreaterOrEqual(stats.DoorToggles, 12, "「门槛」要求 12 次关门");

            stats = new NightStats();
            for (int i = 0; i < 40; i++)
            {
                stats.NoteLightToggle();
            }

            Assert.GreaterOrEqual(stats.TotalSwitchToggles, 40, "「手指的节奏」要求 40 次开关");
        }
    }
}
