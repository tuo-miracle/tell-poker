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
    /// <summary>
    /// 提前動作（遊戲層，2026-10-13 開發者決定「先做就先記下，輪到時自動執行」）：
    /// 還沒輪到時做棄牌手勢 → 牌馬上進棄牌堆、荷官說「Fold noted.」，輪到時自動棄牌（可以過牌也照樣棄）；輪到之前這手就結束 → 什麼都不做。
    /// 還沒輪到時把籌碼丟進下注區 → 籌碼退回、荷官說「Wait for your turn.」（10 秒冷卻），不自動下注。
    /// </summary>
    public class PreActionGameTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static GameConfig PipFirst => new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 80 };
        static PlayerCommand PipChecksOrCalls(HandState st) => Engine.GetLegalActions(st, 1).CanCheck ? (PlayerCommand)new Check(1) : new Call(1);

        /// <summary>Pip 先行動、想很久（AiThinkScale 大），等到 Pip 開始想。</summary>
        void StartWithPipThinking(System.Func<HandState, PlayerCommand> pip, float thinkScale = 8f)
        {
            _rig = new GameTestRig(PipFirst, g => { g.AiOverride = pip; g.AiThinkScale = thinkScale; });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.AiThinking, 5), _rig.Dump());
        }

        bool HumanFolded => _rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold);

        void AssertCardsInMuck(string why)
        {
            for (int i = 0; i < 2; i++)
            {
                var p = _rig.Game.PlayerCard(i).transform.localPosition;
                var m = _rig.Game.Spots.Muck;
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(m.x, m.z)), 0.03f, $"{why}: card {i} in the muck ({p})");
            }
        }

        [Test]
        public void FoldGesture_BeforeTheTurn_MucksNow_SaysFoldNoted_AndFoldsOnTheTurn_EvenThoughCheckIsPossible()
        {
            StartWithPipThinking(PipChecksOrCalls);
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));

            Assert.IsTrue(_rig.Gestures.Any(g => g.Kind == GestureKind.FoldPushed), "early fold recognized\n" + _rig.Dump());
            Assert.IsTrue(_rig.Game.PreFoldQueued, "remembered");
            Assert.IsFalse(HumanFolded, "the Fold command is only sent on the player's turn\n" + _rig.Dump());
            Assert.AreEqual(GameFlow.AiThinking, _rig.Game.Flow, "Pip is still thinking");
            Assert.IsTrue(_rig.Game.Voice.Said(DealerLines.FoldNoted), "dealer acknowledges");
            _rig.Idle(0.6);
            AssertCardsInMuck("mucked right away");

            Assert.IsTrue(_rig.IdleUntil(() => HumanFolded, 15), "fold applied on the turn\n" + _rig.Dump());
            var order = _rig.Actions.Select(a => a.seat + ":" + a.cmd.GetType().Name).ToList();
            CollectionAssert.AreEqual(new[] { "1:Call", "0:Fold" }, order, "Pip completed the small blind (player could have checked), then the queued fold");
            Assert.IsFalse(_rig.Game.PreFoldQueued);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 10), _rig.Dump());
            AssertCardsInMuck("still in the muck");
            Assert.AreEqual(2000, _rig.Game.Session.TotalChips);
        }

        [Test]
        public void FoldGesture_BeforeTheTurn_ThenTheHandEnds_NothingIsApplied_AndTheNextHandIsNormal()
        {
            StartWithPipThinking(st => new Fold(1));
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsTrue(_rig.Game.PreFoldQueued, _rig.Dump());

            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 15), _rig.Dump());
            Assert.IsFalse(HumanFolded, "hand ended before the player's turn: nothing to apply\n" + _rig.Dump());
            Assert.IsFalse(_rig.Game.PreFoldQueued, "dropped when the hand ended");

            // 下一手：玩家是按鈕先行動，輪到時不會自動棄牌
            _rig.Game.AiThinkScale = 0.25f;
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 2 && _rig.Game.Flow == GameFlow.PlayerTurn, 20), _rig.Dump());
            int before = _rig.Actions.Count;
            _rig.Idle(1.5);
            Assert.AreEqual(before, _rig.Actions.Count, "no automatic action in the next hand\n" + _rig.Dump());
            Assert.AreEqual(GameFlow.PlayerTurn, _rig.Game.Flow);
        }

        [Test]
        public void LiftedCardsReleasedPastTheFoldLine_BeforeTheTurn_QueueAFold()
        {
            // 實機 log 641.034–641.507：拿起牌、往前在 (0.13, 0.068, 0.33) 放開，那時還沒輪到玩家
            StartWithPipThinking(PipChecksOrCalls);
            var s = GestureScripts.GrabCards(new SyntheticHands());
            s.Move(Hand.Right, 0.2, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new NV3(-0.08f, 0.089f, 0.03f))));
            s.Move(Hand.Right, 0.28, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new NV3(0.13f, 0.068f, 0.33f))));
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest));
            _rig.Play(s);
            Assert.IsTrue(_rig.Game.PreFoldQueued, _rig.Dump());
            Assert.IsFalse(_rig.Game.CardsInHand);
            _rig.Idle(0.6);
            AssertCardsInMuck("released cards go to the muck, not back to the spot");
            Assert.IsTrue(_rig.IdleUntil(() => HumanFolded, 15), _rig.Dump());
        }

        [Test]
        public void ChipsTossedIntoTheBetZone_BeforeTheTurn_GoBack_WithWaitForYourTurn_OncePerCooldown()
        {
            StartWithPipThinking(PipChecksOrCalls, thinkScale: 30f);
            var state = _rig.Game.Session.State;
            int stack = _rig.Game.Session.StackOf(0);
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.05f, 0.03f, 0.42f));
            s.Wait(1.0);
            _rig.Play(s);

            Assert.IsTrue(_rig.Gestures.Any(g => g.Kind == GestureKind.ChipsPicked), "chips can be picked up any time\n" + _rig.Dump());
            Assert.IsTrue(_rig.Gestures.Any(g => g.Kind == GestureKind.ActionTooEarly), _rig.Dump());
            Assert.IsTrue(_rig.Game.Voice.Said(DealerLines.WaitForTurn), "dealer says wait\n" + string.Join(" | ", _rig.Game.Voice.History));
            Assert.AreSame(state, _rig.Game.Session.State, "no bet in Core");
            Assert.IsFalse(_rig.Actions.Any(a => a.seat == 0));
            _rig.Idle(1.0);
            Assert.AreEqual(2000, _rig.Game.DisplayedTotalChips, "chips flew back to the stack");
            Assert.AreEqual(stack, _rig.Game.Session.StackOf(0));

            // 10 秒內再丟一次：籌碼照樣退回，荷官不重講
            _rig.Play(s);
            Assert.AreEqual(2, _rig.Gestures.Count(g => g.Kind == GestureKind.ActionTooEarly), _rig.Dump());
            Assert.AreEqual(1, _rig.Game.Voice.History.Count(t => t == DealerLines.WaitForTurn), "cooldown");
            Assert.AreEqual(GameFlow.AiThinking, _rig.Game.Flow);
        }

        [Test]
        public void ChipsPickedBeforeTheTurn_AndTossedOnTheTurn_Bet()
        {
            StartWithPipThinking(PipChecksOrCalls, thinkScale: 4f);
            GameFlow flowAtPick = GameFlow.NotStarted;
            _rig.Game.GestureSeen += e => { if (e.Kind == GestureKind.ChipsPicked) flowAtPick = _rig.Game.Flow; };
            // 捏起籌碼、拿著等到輪到自己（Pip 想 2.4 s），再放進下注區
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            s.Wait(3.0);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.05f, 0.03f, 0.42f));
            s.Wait(2.0);
            _rig.Play(s);
            Assert.AreEqual(GameFlow.AiThinking, flowAtPick, "picked before the turn\n" + _rig.Dump());
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && !(a.cmd is Fold) && !(a.cmd is Check)), "the toss on the turn is a bet\n" + _rig.Dump());
            Assert.IsFalse(_rig.Game.Voice.Said(DealerLines.WaitForTurn));
        }
    }

    /// <summary>GameSession.PlayerCanActEarly：還沒輪到，但這一手之後一定還會輪到玩家（才收提前棄牌、提前拿籌碼）。</summary>
    public class PreActionSessionTests
    {
        static GameSession HeadsUp(int firstButton) =>
            new GameSession(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = firstButton, MonteCarloTrials = 40 });

        static void Ok(GameSession s, PlayerCommand c) => Assert.IsTrue(s.Apply(c).IsSuccess, c.ToString());

        [Test]
        public void PipToActPreflop_PlayerCanActEarly_ButNotOnItsOwnTurn()
        {
            var s = HeadsUp(1);
            s.StartHand();
            Assert.AreEqual(1, s.State.ToAct);
            Assert.IsTrue(s.PlayerCanActEarly);
            Ok(s, new Call(1));
            Assert.IsTrue(s.IsPlayerTurn);
            Assert.IsFalse(s.PlayerCanActEarly, "on the turn it is a normal action");
        }

        [Test]
        public void AfterCheckingOnTheFlop_TheNextStreetStillComes_ButAfterCheckingTheRiverNot()
        {
            var s = HeadsUp(1);
            s.StartHand();
            Ok(s, new Call(1));
            Ok(s, new Check(0));                     // 翻牌前 BB 過牌 → 翻牌，玩家先
            Assert.AreEqual(Street.Flop, s.State.Street);
            Ok(s, new Check(0));
            Assert.AreEqual(1, s.State.ToAct);
            Assert.IsTrue(s.PlayerCanActEarly, "flop: Pip may check (then the turn street) or bet (then the player faces it)");
            Ok(s, new Check(1));
            Ok(s, new Check(0));
            Ok(s, new Check(1));                     // 轉牌
            Assert.AreEqual(Street.River, s.State.Street);
            Ok(s, new Check(0));
            Assert.AreEqual(1, s.State.ToAct);
            Assert.IsFalse(s.PlayerCanActEarly, "river, already checked: if Pip checks behind there is no more turn");
        }

        [Test]
        public void AllInOrFolded_CannotActEarly()
        {
            var s = HeadsUp(0);                      // 玩家是按鈕，翻牌前先
            s.StartHand();
            Ok(s, new AllIn(0));
            Assert.AreEqual(1, s.State.ToAct);
            Assert.IsFalse(s.PlayerCanActEarly, "all-in: nothing left to do");

            var m = new GameSession(new GameConfig { Seed = 5, FirstButton = 3, MonteCarloTrials = 40 });   // 四人桌
            m.StartHand();
            for (int guard = 0; guard < 4 && !m.IsPlayerTurn; guard++) Ok(m, new Call(m.State.ToAct));
            Assert.IsTrue(m.IsPlayerTurn, "player to act");
            Ok(m, new Fold(0));
            Assert.IsFalse(m.IsHandOver);
            Assert.IsFalse(m.PlayerCanActEarly, "folded");
        }

        [Test]
        public void BetweenHands_CannotActEarly()
        {
            var s = HeadsUp(1);
            Assert.IsFalse(s.PlayerCanActEarly, "no hand yet");
            s.StartHand();
            Ok(s, new Fold(1));
            Assert.IsTrue(s.IsHandOver);
            Assert.IsFalse(s.PlayerCanActEarly);
        }
    }
}
