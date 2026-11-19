using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    public class GameSessionTests
    {
        static GameConfig Cfg(ulong seed = 7) => new GameConfig { Seed = seed, MonteCarloTrials = 60, Opponents = GameConfig.HeadsUp };

        [Test]
        public void Defaults_MatchTheBrief()
        {
            var c = new GameConfig();
            Assert.AreEqual(10, c.Denomination);
            Assert.AreEqual(100, c.StartChipCount);
            Assert.AreEqual(5, c.SmallBlind);
            Assert.AreEqual(10, c.BigBlind);
        }

        [Test]
        public void StartHand_PostsBlinds_AndDealsTwoHoleCardsToThePlayer()
        {
            var s = new GameSession(Cfg());
            var ev = s.StartHand();
            Assert.AreEqual(HandPhase.Betting, s.State.Phase);
            Assert.AreEqual(2000, s.TotalChips);
            Assert.AreEqual(2, s.PlayerHole.Count);
            Assert.IsTrue(ev.OfType<BlindPosted>().Any(b => b.Amount == 10));
        }

        [Test]
        public void ChipCommit_SingleOversizedChip_IsACallWithChange()
        {
            // TDA 單一大面額籌碼規則：面對下注只丟一枚、沒有宣告加注 → 跟注，找零退回
            var s = new GameSession(new GameConfig { Seed = 3, FirstButton = 0, MonteCarloTrials = 60, Opponents = GameConfig.HeadsUp });
            s.StartHand();
            Assert.AreEqual(0, s.State.ToAct, "heads-up: button/SB acts first preflop");
            var it = s.InterpretChips(1);
            Assert.AreEqual(ChipCommitKind.Call, it.Kind);
            Assert.IsInstanceOf<Call>(it.Command);
            Assert.AreEqual(-5, it.Adjustment);
        }

        [Test]
        public void ChipCommit_TwoChips_FollowCore()
        {
            var s = new GameSession(new GameConfig { Seed = 3, FirstButton = 0, MonteCarloTrials = 60, Opponents = GameConfig.HeadsUp });
            s.StartHand();
            var core = Engine.InterpretChipCommit(s.State, 0, 20);
            var it = s.InterpretChips(2);
            Assert.AreEqual(core.Kind, it.Kind);
            Assert.AreEqual(core.Adjustment, it.Adjustment);
            Assert.AreEqual(ChipCommitKind.Raise, it.Kind);
        }

        [Test]
        public void ChipCommit_TooMany_IsInvalid()
        {
            var s = new GameSession(new GameConfig { Seed = 3, FirstButton = 0, MonteCarloTrials = 60, Opponents = GameConfig.HeadsUp });
            s.StartHand();
            Assert.IsFalse(s.InterpretChips(1000).IsValid);
        }

        [Test]
        public void InvalidCommand_DoesNotChangeState()
        {
            var s = new GameSession(new GameConfig { Seed = 3, FirstButton = 0, MonteCarloTrials = 60, Opponents = GameConfig.HeadsUp });
            s.StartHand();
            var before = s.State;
            var r = s.Apply(new Check(0));
            Assert.IsFalse(r.IsSuccess);
            Assert.AreSame(before, s.State);
        }

        [Test]
        public void AiVsAutoPlayer_ManyHands_ConserveChips_AndRestartOnBust()
        {
            var s = new GameSession(Cfg(11));
            var auto = new Poker.AI.AiPlayer(Poker.AI.Personality.Rookie, 0, 99, new Poker.AI.AiSettings { MonteCarloTrials = 60, ReactionTrials = 30 }) { HumanSeat = 1 };
            int matches = s.MatchNumber;
            for (int h = 0; h < 40; h++)
            {
                auto.OnEvents(PlayerView.Filter(s.StartHand(), 0));
                int guard = 0;
                while (!s.IsHandOver && guard++ < 50)
                {
                    var cmd = s.State.ToAct == 0 ? auto.Decide(s.State).Command : s.DecideAi(h * 30.0).Command;
                    var r = s.Apply(cmd);
                    Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
                    auto.OnEvents(PlayerView.Filter(r.Events, 0));
                    Assert.AreEqual(2000, s.TotalChips);
                }
                Assert.IsTrue(s.IsHandOver);
            }
            Assert.GreaterOrEqual(s.HandsPlayed, 40);
            Assert.GreaterOrEqual(s.MatchNumber, matches);
        }

        [Test]
        public void AiReveal_IsAvailableAfterEveryHand()
        {
            var s = new GameSession(new GameConfig { Seed = 3, FirstButton = 0, MonteCarloTrials = 60, Opponents = GameConfig.HeadsUp });
            s.StartHand();
            s.Apply(new Fold(0));
            Assert.IsTrue(s.IsHandOver);
            Assert.IsNotNull(s.Ai.LastReveal);
            Assert.AreEqual(2, s.AiHole.Count);
        }
    }
}
