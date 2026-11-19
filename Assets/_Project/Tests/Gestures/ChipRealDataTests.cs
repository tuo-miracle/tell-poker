using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 依 bet_v2 真實錄製（桌上畫出籌碼堆與下注區）修正的捏籌碼：
    /// 籌碼堆畫成多柱（柱距 4.3 cm，範圍約中心 ±6.3 cm），使用者在 6.7–7.5 cm 處捏；
    /// 拇指食指已碰在一起（1 cm）但強度只有 0.46；放手時常剛好在下注線上（z 0.29–0.30）。
    /// </summary>
    public class ChipRealDataTests
    {
        static SyntheticHands PinchAt(Vector3 pinchPoint, SyntheticHands s = null)
        {
            s = s ?? new SyntheticHands();
            var palm = SyntheticHands.PalmForPinch(Hand.Right, pinchPoint);
            s.Move(Hand.Right, 0.4, HandTarget.At(palm + new Vector3(0, 0.06f, 0)));
            s.Move(Hand.Right, 0.2, HandTarget.At(palm));
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(1f));
            s.Wait(0.2);
            return s;
        }

        [Test]
        public void Pinching_the_outer_chip_column_picks()
        {
            var ev = G.Run(PinchAt(G.Stack + new Vector3(0.07f, 0.02f, -0.02f)));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Pinching_well_away_from_the_stack_does_not_pick() =>
            Assert.That(G.Run(PinchAt(G.Stack + new Vector3(-0.12f, 0.02f, 0))).Count(GestureKind.ChipsPicked), Is.Zero);

        [Test]
        public void Touching_thumb_and_index_with_low_strength_picks()
        {
            var s = PinchAt(G.Stack + new Vector3(0, 0.02f, 0));
            foreach (var f in s.Frames) f.Right.IndexPinch = System.Math.Min(f.Right.IndexPinch, 0.45f);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Chips_released_on_the_bet_line_count_as_a_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.0f, 0.02f, G.Layout.BetLineZ - 0.015f));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Chips_released_well_short_of_the_bet_line_do_not_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.0f, 0.02f, G.Layout.BetLineZ - 0.06f));
            s.Wait(2.0);
            Assert.That(G.Run(s).Count(GestureKind.BetCommitted), Is.Zero);
        }
    }
}
