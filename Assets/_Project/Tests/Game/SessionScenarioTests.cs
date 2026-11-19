using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>
    /// Fool Rocco 這種插在牌局中間的情境（2026-10-14）：先存下牌桌，情境的每一手都在乾淨的牌桌上打、不算戰績，
    /// 結束後牌桌（籌碼、這一局第幾手、盲注級數、這一局的計時）原樣還原，戰績也沒被情境的手弄髒。
    /// </summary>
    public class SessionScenarioTests
    {
        static GameSession FourSeats() => new GameSession(new GameConfig { Seed = 11, MonteCarloTrials = 40 });

        static void PlayOutWithChecksAndCalls(GameSession s)
        {
            int guard = 0;
            while (!s.IsHandOver && guard++ < 200)
            {
                var la = s.Legal(s.State.ToAct);
                s.Apply(la.CanCheck ? (PlayerCommand)new Check(s.State.ToAct) : new Call(s.State.ToAct));
            }
        }

        [Test]
        public void ScenarioHands_DoNotTouchStats_AndTheTableIsRestoredAfterwards()
        {
            var s = FourSeats();
            for (int i = 0; i < 5; i++) { s.StartHand(100 + i); PlayOutWithChecksAndCalls(s); }
            var stacksBefore = s.State.Stacks().ToArray();
            int handsThisGame = s.HandsThisGame, played = s.HandsPlayed, statHands = s.Stats.Hands.Count, handNo = s.State.HandNumber;
            double startedAt = s.GameStartedAt;

            var snap = s.SaveTable(200);
            s.RecordStats = false;
            for (int i = 0; i < 3; i++)
            {
                s.ScenarioTable(200 + i * 30);
                Assert.AreEqual(0, s.HandsThisGame, "scenario hands use level-0 blinds");
                s.StartHand(200 + i * 30);
                Assert.AreEqual(10, s.State.BigBlind);
                PlayOutWithChecksAndCalls(s);
            }
            Assert.AreEqual(statHands, s.Stats.Hands.Count, "scenario hands are not recorded");
            Assert.AreEqual(played, s.HandsPlayed);

            s.RestoreTable(snap, 290);
            s.RecordStats = true;
            CollectionAssert.AreEqual(stacksBefore, s.State.Stacks());
            Assert.AreEqual(handsThisGame, s.HandsThisGame);
            Assert.AreEqual(handNo, s.State.HandNumber);
            Assert.AreEqual(startedAt + 90, s.GameStartedAt, 1e-9, "the game clock pauses during the scenario");
            Assert.IsTrue(s.IsHandOver);

            s.StartHand(291);
            Assert.AreEqual(handNo + 1, s.State.HandNumber);
            PlayOutWithChecksAndCalls(s);
            Assert.AreEqual(statHands + 1, s.Stats.Hands.Count, "normal hands are recorded again");
            Assert.AreEqual(played + 1, s.HandsPlayed);
        }

        [Test]
        public void ScenarioTable_GivesFreshStacks_AndClearsPresets()
        {
            var s = FourSeats();
            s.StartHand(0);
            PlayOutWithChecksAndCalls(s);
            s.NextHandButton = 3;
            s.ScenarioTable(10);
            Assert.IsNull(s.NextHandButton);
            Assert.IsNull(s.NextHandPreset);
            CollectionAssert.AreEqual(Enumerable.Repeat(s.Config.StartStack, 4), s.State.Stacks());
            Assert.IsTrue(s.HoleOf(0).Count == 0);
        }
    }
}
