using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>規則層的多人局：玩家＋Pip／Rocco／Vex、各自的 AI、盲注升級、一局結束與重開。</summary>
    public class MultiwaySessionTests
    {
        static GameConfig Cfg(ulong seed = 7) => new GameConfig { Seed = seed, MonteCarloTrials = 40 };

        [Test]
        public void Default_IsPlayerPlusThreeAis()
        {
            var s = new GameSession(Cfg());
            Assert.AreEqual(4, s.SeatCount);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, s.AiSeats);
            Assert.AreEqual("Pip", s.AiAt(1).Personality.Name);
            Assert.AreEqual(Archetype.Loudmouth, s.AiAt(2).Personality.Archetype);
            Assert.AreEqual(Archetype.Cold, s.AiAt(3).Personality.Archetype);
            Assert.IsNull(s.AiAt(0), "seat 0 is the human");
            Assert.AreSame(s.AiAt(1), s.Ai, "Ai = first AI (heads-up compatibility)");
            Assert.AreEqual(4000, s.TotalChips);
            foreach (var seat in s.AiSeats) Assert.AreEqual(0, s.AiAt(seat).HumanSeat);
        }

        [Test]
        public void HeadsUpConfig_StillWorks()
        {
            var s = new GameSession(new GameConfig { Seed = 3, Opponents = new[] { Archetype.Rookie }, MonteCarloTrials = 40 });
            Assert.AreEqual(2, s.SeatCount);
            Assert.AreEqual(2000, s.TotalChips);
        }

        [Test]
        public void StartHand_DealsEveryone_AndDecideUsesTheSeatToAct()
        {
            var s = new GameSession(new GameConfig { Seed = 3, FirstButton = 0, MonteCarloTrials = 40 });
            s.StartHand();
            for (int seat = 0; seat < 4; seat++) Assert.AreEqual(2, s.HoleOf(seat).Count, "seat " + seat);
            Assert.AreEqual(3, s.State.ToAct, "UTG acts first 4-handed");
            Assert.IsTrue(s.IsAiTurn);
            var d = s.DecideAi(1.0);
            Assert.AreEqual(3, d.Command.Seat);
            Assert.IsTrue(s.Apply(d.Command).IsSuccess);
        }

        [Test]
        public void PlayerSignals_ReachEveryAi_OnlyTheGazedOneFeelsWatched()
        {
            var s = new GameSession(Cfg());
            s.StartHand();
            s.SignalPlayer(new PlayerSignals { GazeSeat = 2, GazeSeconds = 1.2f, Time = 1 });
            Assert.IsTrue(s.AiAt(2).IsGazed);
            Assert.IsFalse(s.AiAt(1).IsGazed);
            Assert.IsFalse(s.AiAt(3).IsGazed);
            s.SignalPlayer(new PlayerSignals { GazeSeat = 3, GazeSeconds = 1.2f, Time = 2 });
            Assert.IsFalse(s.AiAt(2).IsGazed, "switched to Vex");
            Assert.IsTrue(s.AiAt(3).IsGazed);
        }

        [Test]
        public void UnevenStacks_FromConfig()
        {
            var s = new GameSession(new GameConfig { Seed = 3, StartChipCounts = new[] { 100, 30, 60, 100 }, MonteCarloTrials = 40 });
            CollectionAssert.AreEqual(new[] { 1000, 300, 600, 1000 }, s.State.Stacks());
            Assert.AreEqual(2900, s.TotalChips);
        }

        [Test]
        public void Blinds_EscalateByTheSchedule()
        {
            var cfg = Cfg(5);
            cfg.Schedule = new MatchSchedule { HandsPerLevel = 2, Levels = new[] { 1, 2, 4 }, MaxHands = 50, TimeLimitSeconds = 1e6f };
            var s = new GameSession(cfg);
            var auto = new AiPlayer(Personality.Cold, 0, 5, new AiSettings { MonteCarloTrials = 30, ReactionTrials = 20 });
            var bbs = new System.Collections.Generic.List<int>();
            for (int h = 0; h < 5 && s.GameNumber == 1; h++)
            {
                auto.OnEvents(PlayerView.Filter(s.StartHand(), 0));
                bbs.Add(s.State.BigBlind);
                PlayOut(s, auto, h);
            }
            Assert.AreEqual(10, bbs[0]);
            Assert.AreEqual(10, bbs[1]);
            Assert.AreEqual(20, bbs[2]);
            Assert.AreEqual(40, bbs[4]);
        }

        [Test]
        public void SelfPlay_ManyHands_ConserveChips_GamesEnd_AndRestart()
        {
            var cfg = Cfg(21);
            cfg.Schedule = new MatchSchedule { MaxHands = 6 };
            var s = new GameSession(cfg);
            var auto = new AiPlayer(Personality.Loudmouth, 0, 9, new AiSettings { MonteCarloTrials = 30, ReactionTrials = 20 });
            int games = 0;
            for (int h = 0; h < 40; h++)
            {
                if (s.CheckGameOver(0) != GameEndReason.None)
                {
                    var sum = s.Summarize();
                    Assert.Greater(sum.HandsPlayed, 0);
                    Assert.LessOrEqual(sum.HandsPlayed, 6);
                    games++;
                    s.NewGame(0);
                    Assert.AreEqual(4000, s.TotalChips, "chips reset");
                    Assert.AreEqual(0, s.HandsThisGame);
                }
                auto.OnEvents(PlayerView.Filter(s.StartHand(), 0));
                PlayOut(s, auto, h);
                Assert.AreEqual(4000, s.TotalChips);
            }
            Assert.GreaterOrEqual(games, 5, "6-hand games over 40 hands");
            Assert.AreEqual(games + 1, s.GameNumber);
        }

        [Test]
        public void BustedAi_IsNotDealtIn()
        {
            var s = new GameSession(new GameConfig { Seed = 3, StartChipCounts = new[] { 100, 100, 0, 100 }, FirstButton = 0, MonteCarloTrials = 30 });
            s.StartHand();
            Assert.AreEqual(SeatStatus.Out, s.State.Seat(2).Status);
            Assert.AreEqual(0, s.HoleOf(2).Count);
            Assert.AreEqual(GameEndReason.None, s.CheckGameOver(0));
        }

        [Test]
        public void TutorialPreset_IsUsedForOneHandOnly()
        {
            var s = new GameSession(Cfg());
            s.NextHandPreset = TutorialScript.Preset(1);
            s.NextHandButton = TutorialScript.Button;
            s.StartHand();
            CollectionAssert.AreEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), s.PlayerHole);
            Assert.AreEqual(TutorialScript.Button, s.State.Button, "教學 v4：Rocco 當按鈕");
            Assert.IsNull(s.NextHandPreset);
            while (!s.IsHandOver)
            {
                var la = s.Legal(s.State.ToAct);
                s.Apply(la.CanCheck ? (PlayerCommand)new Check(s.State.ToAct) : new Fold(s.State.ToAct));
            }
            s.StartHand();
            CollectionAssert.AreNotEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), s.PlayerHole);
        }

        /// <summary>教學前後重設牌桌（2026-10-13 教學 v3）：籌碼回到起始、盲注回到第一級、這一局的戰績清空；局數不加（教學不是一局）。進行中的一手直接作廢。</summary>
        [Test]
        public void ResetTable_FreshStacksFirstBlindLevelEmptyStats_EvenMidHand()
        {
            var s = new GameSession(Cfg());
            for (int h = 0; h < 3; h++)
            {
                s.StartHand();
                while (!s.IsHandOver)
                {
                    var la = s.Legal(s.State.ToAct);
                    s.Apply(la.CanCall ? (PlayerCommand)new Call(s.State.ToAct) : new Check(s.State.ToAct));
                }
            }
            Assert.Greater(s.Stats.Hands.Count, 0);
            s.StartHand();
            Assert.IsFalse(s.IsHandOver);
            int game = s.GameNumber;
            s.ResetTable(42.0);
            Assert.IsTrue(s.IsHandOver, "the hand in progress is dropped");
            for (int i = 0; i < s.SeatCount; i++) Assert.AreEqual(s.Config.StartStack, s.StackOf(i));
            Assert.AreEqual(0, s.HandsThisGame);
            Assert.AreEqual(0, s.Stats.Hands.Count);
            Assert.AreEqual(42.0, s.GameStartedAt);
            Assert.AreEqual(game, s.GameNumber, "not a new game number");
            s.StartHand(42.0);
            Assert.AreEqual(s.Config.BigBlind, s.State.BigBlind);
        }

        static void PlayOut(GameSession s, AiPlayer auto, int h)
        {
            int guard = 0;
            while (!s.IsHandOver && guard++ < 120)
            {
                var cmd = s.State.ToAct == 0 ? auto.Decide(s.State).Command : s.DecideAi(h * 30.0).Command;
                var r = s.Apply(cmd);
                Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
                auto.OnEvents(PlayerView.Filter(r.Events, 0));
            }
            Assert.IsTrue(s.IsHandOver, "hand finished");
        }
    }
}
