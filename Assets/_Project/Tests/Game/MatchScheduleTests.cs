using NUnit.Framework;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>一局約 8 分鐘：盲注每 N 手升級、最多 M 手、時間上限、玩家輸光或獨贏就結束。</summary>
    public class MatchScheduleTests
    {
        [Test]
        public void Blinds_StepUpEveryLevel_AndStayAtTheTop()
        {
            var s = new MatchSchedule { HandsPerLevel = 4, Levels = new[] { 1, 2, 4 } };
            Assert.AreEqual((5, 10), s.BlindsFor(0, 5, 10));
            Assert.AreEqual((5, 10), s.BlindsFor(3, 5, 10));
            Assert.AreEqual((10, 20), s.BlindsFor(4, 5, 10));
            Assert.AreEqual((20, 40), s.BlindsFor(8, 5, 10));
            Assert.AreEqual((20, 40), s.BlindsFor(50, 5, 10), "stays at the last level");
            Assert.AreEqual(2, s.LevelFor(9));
        }

        [Test]
        public void Defaults_AimForAboutEightMinutes()
        {
            var s = new MatchSchedule();
            Assert.That(s.MaxHands, Is.InRange(10, 16));
            Assert.AreEqual(480f, s.TimeLimitSeconds, 1e-3f);
            Assert.GreaterOrEqual(s.Levels.Length, 3);
        }

        [Test]
        public void EndReason_PlayerBusted_WonAll_HandLimit_TimeUp()
        {
            var s = new MatchSchedule { MaxHands = 12, TimeLimitSeconds = 480f };
            Assert.AreEqual(GameEndReason.None, s.Check(3, 100, new[] { 500, 700, 800, 0 }, 0));
            Assert.AreEqual(GameEndReason.PlayerBusted, s.Check(3, 100, new[] { 0, 700, 800, 500 }, 0));
            Assert.AreEqual(GameEndReason.PlayerWonAll, s.Check(3, 100, new[] { 4000, 0, 0, 0 }, 0));
            Assert.AreEqual(GameEndReason.HandLimit, s.Check(12, 100, new[] { 900, 1100, 1000, 1000 }, 0));
            Assert.AreEqual(GameEndReason.TimeUp, s.Check(5, 481, new[] { 900, 1100, 1000, 1000 }, 0));
        }

        [Test]
        public void Rank_IsPlacementByStack()
        {
            Assert.AreEqual(1, MatchSchedule.PlaceOf(new[] { 1500, 700, 800, 1000 }, 0));
            Assert.AreEqual(3, MatchSchedule.PlaceOf(new[] { 750, 700, 800, 1750 }, 0));
        }
    }
}
