using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>端到端：合成手勢 → GestureRecognizer → GameController → Core／AI／角色。</summary>
    public class GameFlowTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        // 第一手玩家是按鈕／小盲，翻牌前先行動（面對 5 的跟注）
        static GameConfig PlayerFirst => new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 0, MonteCarloTrials = 80 };
        // 第一手 Pip 是按鈕先行動；AI 固定跟注，玩家在大盲位可以過牌
        static GameConfig PipFirst => new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 80 };

        [Test]
        public void PickThrowConfirm_BetReachesCore()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn(), "player's turn");
            var before = _rig.Game.Session.State;
            var expected = ChipCommit.Interpret(before, 0, 2, 10);

            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.05f, 0.02f, 0.42f));
            s.Wait(2.0);
            _rig.Play(s);

            Assert.AreEqual(1, _rig.Gestures.Count(g => g.Kind == GestureKind.BetCommitted), "one commit");
            Assert.AreEqual(2, _rig.Gestures.First(g => g.Kind == GestureKind.BetCommitted).Count);
            var act = _rig.Actions.First(a => a.seat == 0);
            Assert.AreEqual(expected.Command.GetType(), act.cmd.GetType());
            Assert.AreEqual(ChipCommitKind.Raise, expected.Kind);
            Assert.AreEqual(25, ((Raise)act.cmd).ToAmount, "20 more on top of the 5 small blind");
            Assert.AreEqual(2000, _rig.Game.Session.TotalChips);
            Assert.IsTrue(_rig.Signals.Any(x => x.s.Delivery != Poker.AI.ChipDelivery.None), "delivery signal sent to the AI");
        }

        [Test]
        public void SingleChip_FacingTheSmallBlindCall_IsACall_AndChangeGoesBack()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: 0.005f);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.0f, 0.02f, 0.40f));
            s.Wait(2.0);
            _rig.Play(s);
            var act = _rig.Actions.First(a => a.seat == 0);
            Assert.IsInstanceOf<Call>(act.cmd);
            _rig.Idle(1.5);
            Assert.AreEqual(2000, _rig.Game.DisplayedTotalChips, "chips on the table add up after the change animation");
        }

        [Test]
        public void KnockOnTable_Checks()
        {
            _rig = new GameTestRig(PipFirst, g => g.AiOverride = st => new Call(1));
            Assert.IsTrue(_rig.WaitPlayerTurn());
            Assert.IsTrue(_rig.Game.Session.Legal(0).CanCheck);
            _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Check), "check applied");
        }

        [Test]
        public void Knock_WhenFacingABet_IsInvalid_StateUnchanged_WithFeedback()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            var before = _rig.Game.Session.State;
            _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            Assert.AreSame(before, _rig.Game.Session.State, "Core state untouched");
            Assert.Contains(InvalidReason.CannotCheck, _rig.Invalids);
            Assert.IsFalse(_rig.Actions.Any(a => a.seat == 0));
            Assert.AreEqual(GameFlow.PlayerTurn, _rig.Game.Flow, "still the player's turn");
            Assert.IsFalse(string.IsNullOrEmpty(_rig.Game.Dealer.Subtitle.Text), "dealer says something");
        }

        [Test]
        public void PushCardsForward_Folds_AndPipRevealsHisHand()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 10));
            Assert.IsTrue(_rig.Game.AiCard(0).FaceUp && _rig.Game.AiCard(1).FaceUp, "Pip's cards shown");
            Assert.IsFalse(string.IsNullOrEmpty(_rig.Game.RevealHeadline), "headline like 'Was bluffing'");
            Assert.AreEqual(2000, _rig.Game.DisplayedTotalChips);
        }

        [Test]
        public void PeekAmount_LiftsThePlayersCardCorners()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Play(GestureScripts.Peek(new SyntheticHands(), Hand.Right, hold: 1.0));
            var peeks = _rig.Gestures.Where(g => g.Kind == GestureKind.PeekChanged).ToList();
            Assert.IsNotEmpty(peeks);
            float maxAmount = peeks.Max(p => p.Amount);
            Assert.Greater(maxAmount, 0.2f);
            // 最後一個 PeekChanged 的 amount 對應牌角角度
            var last = peeks.Last();
            float max = _rig.Game.PlayerCard(0).MaxPeekDegrees;
            Assert.AreEqual(last.Amount * max, _rig.Game.PlayerCard(0).PeekDegrees, 0.5f);
            Assert.AreEqual(last.Amount * max, _rig.Game.PlayerCard(1).PeekDegrees, 0.5f);
        }

        [Test]
        public void FullHand_ByGestures_EndsWithChipsConserved_AndNextHandStarts()
        {
            _rig = new GameTestRig(PipFirst, g => g.AiOverride = st => Engine.GetLegalActions(st, 1).CanCheck ? (PlayerCommand)new Check(1) : new Call(1));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 1), "first hand dealt");
            int startHands = _rig.Game.Session.HandsPlayed;
            for (int guard = 0; guard < 8; guard++)
            {
                Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.PlayerTurn || _rig.Game.Session.IsHandOver, 20), "stalled");
                if (_rig.Game.Session.IsHandOver) break;
                _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
                Assert.AreEqual(2000, _rig.Game.Session.TotalChips);
            }
            Assert.AreEqual(startHands + 1, _rig.Game.Session.HandsPlayed, "hand finished by check-down\n" + _rig.Dump());
            Assert.AreEqual(1, _rig.Game.Session.HandsStarted);
            // 攤牌：荷官請玩家亮牌，玩家用翻牌手勢翻開
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.ShowdownWait, 5), "showdown waits for the flip");
            Assert.IsFalse(_rig.Game.PlayerCard(0).FaceUp);
            _rig.Play(GestureScripts.Flip(new SyntheticHands()));
            Assert.IsTrue(_rig.Gestures.Any(g => g.Kind == GestureKind.ShowdownFlip), "flip recognized");
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 15));
            Assert.IsTrue(_rig.Game.PlayerCard(0).FaceUp && _rig.Game.PlayerCard(1).FaceUp, "player's cards flipped");
            Assert.IsTrue(_rig.Game.AiCard(0).FaceUp, "Pip's cards shown");
            Assert.AreEqual(2000, _rig.Game.DisplayedTotalChips);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 2, 8), "next hand starts by itself");
        }

        [Test]
        public void TwentyHands_AutoPlayerVsPip_NeverStall_AndConserveChips()
        {
            var auto = new Poker.AI.AiPlayer(Poker.AI.Personality.Rookie, 0, 77, new Poker.AI.AiSettings { MonteCarloTrials = 40, ReactionTrials = 20 });
            _rig = new GameTestRig(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 9, MonteCarloTrials = 40, NextHandSeconds = 0.5f, SummarySeconds = 1f }, g =>
            {
                g.AutoPlayer = st => auto.Decide(st, 0, auto.Observations).Command;
                g.AutoPlayerObserver = ev => auto.OnEvents(PlayerView.Filter(ev, 0));
                g.AiThinkScale = 0.05f;
            });
            int steps = 0;
            while (_rig.Game.Session.HandsPlayed < 20 && steps++ < 72 * 60 * 6)
            {
                _rig.StepIdle();
                Assert.AreEqual(2000, _rig.Game.Session.TotalChips);
            }
            Assert.GreaterOrEqual(_rig.Game.Session.HandsPlayed, 20, $"stalled in {_rig.Game.Flow}");
        }

        [Test]
        public void Gaze_OneSecondOnPip_SetsGazedAt_AndSignalsOnce()
        {
            _rig = new GameTestRig(PlayerFirst);
            _rig.LookFromSeat(new Vector3(0.4f, 0f, 0.1f));
            _rig.Idle(0.5);
            Assert.IsFalse(_rig.Game.Pip.IsGazedAt);
            var head = _rig.Game.Pip.Rig.Head.position;
            _rig.LookAt(head);
            _rig.Idle(0.6);
            Assert.IsFalse(_rig.Game.Pip.IsGazedAt, "not yet");
            Assert.AreEqual(0, _rig.Game.GazeSignalsSent);
            _rig.Idle(0.6);
            Assert.IsTrue(_rig.Game.Pip.IsGazedAt, "after ~1.2 s");
            Assert.AreEqual(1, _rig.Game.GazeSignalsSent);
            Assert.IsTrue(_rig.Signals.Any(x => x.s.GazeSeat == 1));
            _rig.Idle(2.0);
            Assert.AreEqual(1, _rig.Game.GazeSignalsSent, "only once while staring");
            _rig.LookFromSeat(new Vector3(0.4f, 0f, 0.1f));
            _rig.Idle(1.0);
            Assert.IsFalse(_rig.Game.Pip.IsGazedAt, "looked away");
        }

        [Test]
        public void Setup_PipAcross_DotFarRight_PropsHidden_NoShadows()
        {
            _rig = new GameTestRig(PlayerFirst);
            var pip = _rig.Game.Pip.transform.localPosition;
            Assert.AreEqual(0f, pip.x, 0.02f);
            Assert.Greater(pip.z, 0.6f);
            Assert.Greater(_rig.Game.Dealer.transform.localPosition.x, 0.25f);
            Assert.IsFalse(_rig.Game.Pip.Rig.Props.gameObject.activeSelf, "Pip's own props hidden");
            foreach (var r in _rig.Game.GetComponentsInChildren<Renderer>(true))
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name);
            foreach (var r in _rig.Root.GetComponentsInChildren<Renderer>(true))
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name);
            Assert.AreEqual(0, _rig.Root.GetComponentsInChildren<Rigidbody>(true).Length, "no rigidbodies");
        }
    }
}
