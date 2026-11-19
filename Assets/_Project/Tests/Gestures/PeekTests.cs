using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class PeekTests
    {
        static float MaxPeek(System.Collections.Generic.List<GestureEvent> ev) =>
            ev.Where(e => e.Kind == GestureKind.PeekChanged).Select(e => e.Amount).DefaultIfEmpty(0f).Max();

        static float LastPeek(System.Collections.Generic.List<GestureEvent> ev) =>
            ev.Where(e => e.Kind == GestureKind.PeekChanged).Select(e => e.Amount).DefaultIfEmpty(0f).Last();

        [Test]
        public void Lifting_wrist_over_cards_with_palm_down_peeks_then_returns_to_zero_when_hand_leaves()
        {
            var s = GestureScripts.Peek(new SyntheticHands());
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest));
            var ev = G.Run(s);
            Assert.That(MaxPeek(ev), Is.GreaterThan(0.8f), ev.Dump());
            Assert.That(LastPeek(ev), Is.EqualTo(0f));
            Assert.That(ev.All(e => e.Kind != GestureKind.PeekChanged || e.Hand == Hand.Right));
        }

        [Test]
        public void Peek_amount_follows_wrist_height_continuously()
        {
            // 2026-10-12 起手腕高出指尖 3→7 cm 對到 0→1（舊值 3→10 cm）：俯仰 10° ≈ 4.6 cm，落在中間
            var s = GestureScripts.Peek(new SyntheticHands(), pitch: 10f, lift: 0f);
            var ev = G.Run(s);
            Assert.That(MaxPeek(ev), Is.InRange(0.1f, 0.7f));
        }

        [Test]
        public void Turning_palm_up_drops_peek_to_zero()
        {
            var s = GestureScripts.Peek(new SyntheticHands(), hold: 0.3);
            s.Move(Hand.Right, 0.3, HandTarget.Roll(180f));
            s.Wait(0.3);
            var ev = G.Run(s);
            Assert.That(MaxPeek(ev), Is.GreaterThan(0.5f));
            Assert.That(LastPeek(ev), Is.EqualTo(0f));
        }

        [Test]
        public void Near_miss_palm_up_over_cards_does_not_peek()
        {
            var s = new SyntheticHands();
            var over = G.Cards + new Vector3(0, 0.05f, 0);
            s.Move(Hand.Right, 0.5, HandTarget.At(over).WithRoll(180f));
            s.Wait(0.4);
            s.Move(Hand.Right, 0.4, HandTarget.At(over + new Vector3(0, 0.03f, 0)).WithPitch(-30f));
            s.Wait(0.5);
            Assert.That(MaxPeek(G.Run(s)), Is.EqualTo(0f));
        }

        [Test]
        public void Near_miss_palm_down_lifting_beside_the_cards_does_not_peek()
        {
            var s = GestureScripts.Peek(new SyntheticHands(), dx: 0.22f);
            Assert.That(MaxPeek(G.Run(s)), Is.EqualTo(0f));
        }

        [Test]
        public void Near_miss_hand_high_above_cards_does_not_peek()
        {
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(G.Cards + new Vector3(0, 0.22f, 0)));
            s.Wait(0.5);
            s.Move(Hand.Right, 0.4, HandTarget.Pitch(35f));
            s.Wait(0.5);
            Assert.That(MaxPeek(G.Run(s)), Is.EqualTo(0f));
        }

        [Test]
        public void Peek_works_when_not_players_turn()
        {
            var ev = G.Run(GestureScripts.Peek(new SyntheticHands()), G.Ctx(turn: false));
            Assert.That(MaxPeek(ev), Is.GreaterThan(0.8f));
        }

        [Test]
        public void Peek_survives_1cm_noise()
        {
            var s = GestureScripts.Peek(new SyntheticHands(seed: 7).WithNoise(0.01f));
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest));
            s.Wait(0.3);
            var ev = G.Run(s);
            Assert.That(MaxPeek(ev), Is.GreaterThan(0.7f));
            Assert.That(LastPeek(ev), Is.EqualTo(0f));
        }
    }
}
