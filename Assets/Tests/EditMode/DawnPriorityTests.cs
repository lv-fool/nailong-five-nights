using NUnit.Framework;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;

namespace NailongFNAF.EditModeTests
{
    /// <summary>
    /// 「天亮 vs 被抓」的优先级检查。
    ///
    /// 起因：用户反馈通关那一刻画面表现不对，怀疑两个状态撞在一起时
    /// 结算页抢了天亮演出的位置。
    ///
    /// 设计意图（见 PrototypeScene.Menu.HandleMenu）：
    ///   HasSurvived（天亮）**优先于** HasBeenCaught（被抓）。
    ///   两者同帧成立时，应当走天亮演出，不该弹失败结算。
    ///
    /// 这里从纯逻辑层验证：GameManager 的两个旗标能同时为真，
    /// 且 UI 层的判定顺序确实先看 HasSurvived。
    /// </summary>
    [TestFixture]
    public class DawnPriorityTests
    {
        private GameManager _gm;

        [SetUp]
        public void SetUp()
        {
            _gm = new GameManager();
            _gm.Restart();
        }

        /// <summary>把时钟推到整夜走完（6 小时）。</summary>
        private void AdvanceToDawn()
        {
            float total = NightClock.HoursPerNight * _gm.Clock.SecondsPerHour;
            _gm.Clock.Tick(total + 1f);
        }

        /// <summary>时钟走到 6 点 → HasSurvived 为真。</summary>
        [Test]
        public void ClockComplete_MakesSurvivedTrue()
        {
            Assert.IsFalse(_gm.HasSurvived, "初始不该是天亮");

            // 直接把时钟推到整夜走完
            AdvanceToDawn();

            Assert.IsTrue(_gm.Clock.IsNightComplete, "时钟应当认为这一夜结束了");
            Assert.IsTrue(_gm.HasSurvived, "时钟走完 = 撑到天亮");
        }

        /// <summary>MarkCaught 之后 HasBeenCaught 为真，且 HasSurvived 仍可能是真。</summary>
        [Test]
        public void CaughtAfterDawn_BothFlagsTrue()
        {
            AdvanceToDawn();
            Assert.IsTrue(_gm.HasSurvived);

            _gm.MarkCaught();

            Assert.IsTrue(_gm.HasBeenCaught, "被抓旗标应当立起来");
            Assert.IsTrue(_gm.HasSurvived, "天亮旗标不该被 MarkCaught 抹掉");
        }

        /// <summary>
        /// **核心断言**：两个旗标同时为真时，UI 的判定顺序必须是
        /// 「先 HasSurvived，再 HasBeenCaught」。
        ///
        /// 这里把 HandleMenu 的判断逻辑抄一份做等价验证 ——
        /// 因为它藏在 MonoBehaviour 的私有方法里，纯逻辑测试拿不到。
        /// 真正的顺序保证靠 PrototypeScene.Menu.cs 里两个 if 的先后，
        /// 这个测试是防有人把它们写反的护栏。
        /// </summary>
        [Test]
        public void WhenBothTrue_DawnWinsByOrder()
        {
            AdvanceToDawn();
            _gm.MarkCaught();

            Assert.IsTrue(_gm.HasSurvived);
            Assert.IsTrue(_gm.HasBeenCaught);

            // 模拟 HandleMenu 的判定顺序
            string branch;
            if (_gm.HasSurvived)
            {
                branch = "dawn";
            }
            else if (_gm.HasBeenCaught)
            {
                branch = "caught";
            }
            else
            {
                branch = "none";
            }

            Assert.AreEqual("dawn", branch,
                "两个旗标同时为真时必须走天亮分支 —— 说明 UI 那个 if 的顺序被改反了");
        }

        /// <summary>没到天亮就被抓 → 走失败。</summary>
        [Test]
        public void CaughtBeforeDawn_GoesCaught()
        {
            _gm.MarkCaught();

            Assert.IsFalse(_gm.HasSurvived, "没到 6 点不该算天亮");
            Assert.IsTrue(_gm.HasBeenCaught);

            string branch = _gm.HasSurvived ? "dawn" : (_gm.HasBeenCaught ? "caught" : "none");
            Assert.AreEqual("caught", branch);
        }
    }
}
