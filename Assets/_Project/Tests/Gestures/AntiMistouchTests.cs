using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class AntiMistouchTests
    {
        static TableContext OffTurn(TablePhase phase = TablePhase.Flop) => G.Ctx(turn: false, phase: phase);

        [Test]
        public void Off_turn_actions_emit_nothing()
        {
            Assert.That(G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)), OffTurn()).AnyAction(), Is.False, "knock");
            Assert.That(G.Run(GestureScripts.Slam(new SyntheticHands()), OffTurn()).AnyAction(), Is.False, "slam");
            Assert.That(G.Run(GestureScripts.FoldPush(new SyntheticHands()), OffTurn()).AnyAction(), Is.False, "push");
            Assert.That(G.Run(GestureScripts.AllIn(new SyntheticHands()), OffTurn()).AnyAction(), Is.False, "all-in");
            Assert.That(G.Run(GestureScripts.Flip(new SyntheticHands()), OffTurn(TablePhase.Showdown)).AnyAction(), Is.False, "flip");
            var picks = G.Run(GestureScripts.PickChips(new SyntheticHands()), OffTurn());
            Assert.That(picks.Count(GestureKind.ChipsPicked), Is.Zero, "chips");
        }

        [Test]
        public void Off_turn_retract_is_still_allowed()
        {
            var inZone = new Vector3(0.05f, 0.02f, 0.42f);
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, inZone);
            double turnEnds = s.Time;
            GestureScripts.Retract(s, Hand.Right, inZone);
            s.Wait(2.0);
            var ev = G.Run(s, t => t < turnEnds ? G.Ctx() : OffTurn());
            Assert.That(ev.Count(GestureKind.BetRetracted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero);
        }

        [Test]
        public void Low_confidence_frames_do_not_start_gestures()
        {
            var s = new SyntheticHands().Track(Hand.Right, true, confidence: 0.2f);
            GestureScripts.Knock(s, Hand.Right, GestureScripts.KnockSpot(Hand.Right));
            GestureScripts.Slam(s);
            Assert.That(G.Run(s).AnyAction(), Is.False);
        }

        [Test]
        public void Untracked_hand_in_bet_zone_pauses_the_retract_timer()
        {
            var inZone = new Vector3(0.05f, 0.02f, 0.42f);
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, inZone, travel: 0.6);
            // PlaceAt 放開後手抬起 0.3 s；之後追蹤遺失 2 秒（手還在下注區上方）
            s.Track(Hand.Right, false).Wait(2.0).Track(Hand.Right, true);
            double back = s.Time;
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.BetCommitted).Time, Is.GreaterThan(back));
        }

        [Test]
        public void Tracking_loss_does_not_count_as_releasing_chips()
        {
            var s = GestureScripts.PickChips(new SyntheticHands());
            s.Track(Hand.Right, false).Wait(0.5).Track(Hand.Right, true).Wait(0.3);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsReleased), Is.Zero, ev.Dump());
        }

        [Test]
        public void Knock_with_other_hand_while_holding_chips_is_ignored()
        {
            var s = GestureScripts.PickChips(new SyntheticHands());
            GestureScripts.Knock(s, Hand.Left, GestureScripts.KnockSpot(Hand.Left));
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
        }

        [Test]
        public void Knock_during_all_in_countdown_is_ignored()
        {
            var s = GestureScripts.AllIn(new SyntheticHands(), holdAfter: 0.05);
            // 倒數中右手原地敲兩下（不往回收，所以 All-in 照常確認）
            var p = s.Frames.Last().Right.Position(HandJoint.Palm);
            for (int i = 0; i < 2; i++)
            {
                s.Move(Hand.Right, 0.12, HandTarget.At(p + new Vector3(0, 0.04f, 0)), Ease.Out);
                s.Move(Hand.Right, 0.09, HandTarget.At(p), Ease.In);
                s.Wait(0.05);
            }
            s.Wait(1.0);
            // 倒數 2026-10-13 起縮短為 0.5 s，推完再敲兩下（0.5 s）時倒數已結束；這裡測的是倒數期間的閘門，用 1 s 倒數
            var ev = G.Run(s, tuning: new GestureTuning { AllInConfirmDelay = 1.0f });
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Game_layer_chips_in_hand_blocks_knock()
        {
            var ctx = G.Ctx();
            ctx.ChipsInHand = 2;
            Assert.That(G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)), ctx).Count(GestureKind.KnockCheck), Is.Zero);
        }
    }
}
