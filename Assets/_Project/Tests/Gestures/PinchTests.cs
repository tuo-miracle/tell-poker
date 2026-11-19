using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 捏合判斷要符合真機範圍：Quest 3 的捏合強度封頂 0.70（92 段錄製從未超過），
    /// 另有 runtime 的布林值 IndexPinching（OVRHand.GetFingerIsPinching）。
    /// </summary>
    public class PinchTests
    {
        static SyntheticHands AtStack(SyntheticHands s, Hand h = Hand.Right)
        {
            var palm = SyntheticHands.PalmForPinch(h, G.Stack + new Vector3(0, 0.02f, 0));
            s.Move(h, 0.4, HandTarget.At(palm + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.2, HandTarget.At(palm));
            return s;
        }

        [Test]
        public void Pinch_strength_below_the_quest_cap_picks_chips()
        {
            var s = AtStack(new SyntheticHands().WithPinchCap());
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0.62f / SyntheticHands.PinchStrengthCap));
            s.Wait(0.3);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Release_needs_strength_below_0_40()
        {
            var s = AtStack(new SyntheticHands().WithPinchCap());
            float cap = SyntheticHands.PinchStrengthCap;
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0.68f / cap));
            s.Wait(0.2);
            s.Move(Hand.Right, 0.1, HandTarget.Pinch(0.45f / cap));
            s.Wait(0.3);
            var held = G.Run(s);
            Assert.That(held.Count(GestureKind.ChipsReleased), Is.EqualTo(0), "0.45 還在遲滯帶內，不算放開\n" + held.Dump());
            s.Move(Hand.Right, 0.1, HandTarget.Pinch(0.35f / cap));
            s.Wait(0.2);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsReleased), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void System_pinching_flag_starts_and_holds_a_pinch_even_when_strength_is_low()
        {
            var s = AtStack(new SyntheticHands().WithPinchCap());
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0.3f / SyntheticHands.PinchStrengthCap));
            double flagOn = s.Time;
            s.Wait(0.5);
            double flagOff = s.Time;
            s.Wait(0.3);
            foreach (var f in s.Frames) f.Right.IndexPinching = f.Time >= flagOn && f.Time < flagOff;
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            var rel = ev.Where(e => e.Kind == GestureKind.ChipsReleased).ToList();
            Assert.That(rel.Count, Is.EqualTo(1), ev.Dump());
            Assert.That(rel[0].Time, Is.GreaterThanOrEqualTo(flagOff - 1e-6), "旗標還在時不能放開");
        }

        [Test]
        public void Synthetic_full_pinch_tops_out_at_the_quest_cap()
        {
            var s = new SyntheticHands().WithPinchCap();
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(1f));
            s.Wait(0.1);
            Assert.That(s.Frames.Max(f => f.Right.IndexPinch), Is.EqualTo(0.70f).Within(1e-6f));
        }
    }
}
