using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class AllInTests
    {
        [Test]
        public void Two_hands_pushing_stack_past_line_confirms_after_countdown()
        {
            var ev = G.Run(GestureScripts.AllIn(new SyntheticHands()));
            Assert.That(ev.Count(GestureKind.AllInPushStarted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInCancelled), Is.Zero);
            var started = ev.First(GestureKind.AllInPushStarted);
            var confirmed = ev.First(GestureKind.AllInConfirmed);
            Assert.That(confirmed.Time - started.Time, Is.GreaterThanOrEqualTo(new GestureTuning().AllInConfirmDelay));
            Assert.That(ev.Count(e => e.IsAction && e.Kind != GestureKind.AllInPushStarted && e.Kind != GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Pulling_hands_back_during_countdown_cancels()
        {
            // 倒數 0.5 s（2026-10-13 起）：推完立刻拉回
            var s = GestureScripts.AllIn(new SyntheticHands(), holdAfter: 0.0);
            var back = G.Stack + new Vector3(0, 0.03f, -0.01f);
            s.Move(0.3, HandTarget.At(back + new Vector3(0.09f, 0, 0)), HandTarget.At(back - new Vector3(0.09f, 0, 0)));
            s.Wait(1.5);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.AllInPushStarted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInCancelled), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero);
        }

        [Test]
        public void Near_miss_one_hand_push_is_not_all_in()
        {
            var ev = G.Run(GestureScripts.OneHandPush(new SyntheticHands()));
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
        }

        [Test]
        public void Near_miss_hands_too_far_apart_is_not_all_in()
        {
            var ev = G.Run(GestureScripts.AllIn(new SyntheticHands(), gap: 0.55f, center: new Vector3(0.05f, 0, 0.05f)));
            Assert.That(ev.Count(GestureKind.AllInPushStarted) + ev.Count(GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Near_miss_hands_out_of_sync_is_not_all_in()
        {
            var ev = G.Run(GestureScripts.AllIn(new SyntheticHands(), lag: 0.5));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Near_miss_short_push_that_stops_before_the_line_cancels_without_confirming()
        {
            // 2026-10-12 第二次實機後 All-in 只要推 8 cm、指尖過下注線就算：原本從堆旁推 10 cm（指尖到 z 0.31）已經算越線，
            // 起點往後移 4 cm，讓指尖停在 z 0.27（下注線前 3 cm），維持「推不到線」的原意
            var ev = G.Run(GestureScripts.AllIn(new SyntheticHands(), distance: 0.10f, holdAfter: 2.0, center: G.Stack - new Vector3(0, 0, 0.04f)));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInPushStarted), Is.EqualTo(ev.Count(GestureKind.AllInCancelled)), ev.Dump());
        }

        [Test]
        public void All_in_survives_1cm_noise()
        {
            var ev = G.Run(GestureScripts.AllIn(new SyntheticHands(seed: 17).WithNoise(0.01f)));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }
    }

    public class ShowdownFlipTests
    {
        static TableContext Showdown => G.Ctx(phase: TablePhase.Showdown, canCheck: false);

        [Test]
        public void Turning_palm_up_over_cards_at_showdown_flips()
        {
            var ev = G.Run(GestureScripts.Flip(new SyntheticHands()), Showdown);
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.ShowdownFlip).Hand, Is.EqualTo(Hand.Right));
        }

        [Test]
        public void Near_miss_flip_before_showdown_does_nothing() =>
            Assert.That(G.Run(GestureScripts.Flip(new SyntheticHands()), G.Ctx(phase: TablePhase.River)).Count(GestureKind.ShowdownFlip), Is.Zero);

        [Test]
        public void Near_miss_flip_away_from_cards_does_nothing() =>
            Assert.That(G.Run(GestureScripts.Flip(new SyntheticHands(), at: new Vector3(-0.3f, 0, 0.3f)), Showdown).Count(GestureKind.ShowdownFlip), Is.Zero);

        [Test]
        public void Near_miss_half_turn_does_nothing() =>
            Assert.That(G.Run(GestureScripts.Flip(new SyntheticHands(), roll: 90f), Showdown).Count(GestureKind.ShowdownFlip), Is.Zero);

        [Test]
        public void Near_miss_very_slow_turn_does_nothing() =>
            Assert.That(G.Run(GestureScripts.Flip(new SyntheticHands(), duration: 2.0), Showdown).Count(GestureKind.ShowdownFlip), Is.Zero);

        [Test]
        public void Flip_survives_1cm_noise()
        {
            var ev = G.Run(GestureScripts.Flip(new SyntheticHands(seed: 19).WithNoise(0.01f)), Showdown);
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), ev.Dump());
        }
    }
}
