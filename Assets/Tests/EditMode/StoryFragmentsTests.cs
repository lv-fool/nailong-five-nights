using NailongFNAF.Gameplay;
using NUnit.Framework;

namespace NailongFNAF.Tests
{
    public class StoryFragmentsTests
    {
        [Test]
        public void 五夜都有各不相同的生活片段()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int night = 0; night < 5; night++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(StoryFragments.Night(night)));
                Assert.IsTrue(seen.Add(StoryFragments.Night(night)));
                Assert.IsFalse(string.IsNullOrEmpty(StoryFragments.Phone(night).Text));
                Assert.IsTrue(string.IsNullOrEmpty(StoryFragments.Phone(night).Id), "新字幕不能借用旧配音");
            }
            Assert.AreEqual(string.Empty, StoryFragments.Night(-1));
            Assert.AreEqual(string.Empty, StoryFragments.Night(5));
        }

        [Test]
        public void 故事首次电话只多一句且保留原配音()
        {
            for (int night = 0; night < 5; night++)
            {
                var original = NaibaoPhoneScript.ForCall(night, night == 0, 0, false, false);
                var story = NaibaoPhoneScript.ForCall(night, night == 0, 0, false, true);
                Assert.AreEqual(original.Length + 1, story.Length);
                Assert.LessOrEqual(story.Length, NaibaoPhoneScript.MaxLinesPerCall);
                for (int i = 0; i < original.Length; i++)
                {
                    Assert.AreEqual(original[i].Text, story[i].Text);
                    Assert.AreEqual(original[i].Id, story[i].Id);
                }
                Assert.AreEqual(StoryFragments.Phone(night).Text, story[story.Length - 1].Text);
                Assert.IsTrue(string.IsNullOrEmpty(story[story.Length - 1].Id));
            }
        }

        [Test]
        public void 再次拨打和自定义夜不夹带故事片段()
        {
            foreach (bool custom in new[] { false, true })
            {
                var plain = NaibaoPhoneScript.ForCall(2, false, 1, custom, false);
                var story = NaibaoPhoneScript.ForCall(2, false, 1, custom, true);
                Assert.AreEqual(plain.Length, story.Length);
            }
            Assert.AreEqual(
                NaibaoPhoneScript.ForCall(2, false, 0, true, false).Length,
                NaibaoPhoneScript.ForCall(2, false, 0, true, true).Length);
        }

        [Test]
        public void 成就片段不替换达成条件()
        {
            Assert.AreEqual("通关第 1 夜", AchievementCatalog.Find(AchievementCatalog.FirstNight).Description);
            Assert.IsFalse(string.IsNullOrEmpty(AchievementCatalog.Find(AchievementCatalog.FirstNight).Flavor));
            Assert.IsFalse(AchievementCatalog.Find(AchievementCatalog.FirstNight).Hidden);
            Assert.IsTrue(AchievementCatalog.Find(AchievementCatalog.KitchenSilence).Hidden);
        }
    }
}
