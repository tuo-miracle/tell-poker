using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-13 實機 log（pid 8951）：33 次放開有 10 次 zone=False。落點偏短（z 0.22–0.25）、偏右（x 0.39–0.45）、
    /// 丟過頭（z 0.81–0.94）都沒算下注。規則改成「落點明顯在自己的牌與籌碼前方」就算下注，放回籌碼堆、放在牌上不算；
    /// 落在牌前緣與下注區之間的短區，要往前丟或從上方放下才算（貼桌放下是日常雜動）。
    /// </summary>
    public class ChipTossLandingTests
    {
        static TableLayout Layout => G.Layout;

        static System.Collections.Generic.List<GestureEvent> TossAndWait(Vector3 from, Vector3 velocity, float lift = 0.03f)
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: lift);
            GestureScripts.Throw(s, Hand.Right, from, velocity);
            s.Wait(2.0);
            return G.Run(s);
        }

        static System.Collections.Generic.List<GestureEvent> PlaceAndWait(Vector3 at)
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, at);
            s.Wait(2.0);
            return G.Run(s);
        }

        static void AssertBet(System.Collections.Generic.List<GestureEvent> ev)
        {
            Assert.That(ev.First(GestureKind.ChipsReleased).InBetZone, Is.True, ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
        }

        static void AssertNoBet(System.Collections.Generic.List<GestureEvent> ev)
        {
            Assert.That(ev.Count(GestureKind.ChipsReleased), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.ChipsReleased).InBetZone, Is.False, ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero, ev.Dump());
        }

        // ── 該算下注 ──

        [Test]
        public void Forward_toss_that_lands_short_of_the_bet_line_is_a_bet()
        {
            // 實機 (0.07, 0.25)、(0.17, 0.25)：往前丟但落在下注線前
            var ev = TossAndWait(new Vector3(0.07f, 0.06f, 0.18f), new Vector3(0f, 0f, 0.6f));
            var rel = ev.First(GestureKind.ChipsReleased);
            Assert.That(rel.Position.Z, Is.InRange(0.21f, Layout.BetLineZ - Tuning.BetZoneLandingMargin), "腳本要重現「偏短」的落點\n" + ev.Dump());
            AssertBet(ev);
        }

        [Test]
        public void Dropping_from_above_just_past_the_cards_is_a_bet()
        {
            // 實機 (0.14, 0.22)：牌前緣 z 0.194 之後、離籌碼堆 14 cm；手停在 8 cm 高放開（bet_v2 放開時離桌 5.6–10.9 cm）
            AssertBet(PlaceAndWait(new Vector3(0.14f, 0.08f, 0.225f)));
        }

        [Test]
        public void Toss_landing_right_of_the_bet_zone_is_a_bet()
        {
            // 實機 (0.45, 0.51)、(0.39, 0.47)：往右前方丟，落點超出下注區右緣（0.35）
            var ev = TossAndWait(new Vector3(0.24f, 0.12f, 0.16f), new Vector3(1.0f, 0.1f, 1.0f));
            var rel = ev.First(GestureKind.ChipsReleased);
            Assert.That(rel.Position.X, Is.GreaterThan(Layout.BetZone.MaxX + Tuning.BetZoneLandingMargin), "腳本要重現「偏右」的落點\n" + ev.Dump());
            Assert.That(rel.Position.X, Is.LessThanOrEqualTo(0.55f), ev.Dump());
            AssertBet(ev);
        }

        [Test]
        public void Toss_that_overshoots_the_far_edge_is_a_bet()
        {
            // 實機 (-0.09, 0.94)、(0.26, 0.81)：用力丟過頭
            var ev = TossAndWait(new Vector3(0.05f, 0.18f, 0.15f), new Vector3(-0.15f, 0.4f, 2.4f));
            var rel = ev.First(GestureKind.ChipsReleased);
            Assert.That(rel.Position.Z, Is.GreaterThan(Layout.BetZone.MaxZ + Tuning.BetZoneLandingMargin), "腳本要重現「丟過頭」的落點\n" + ev.Dump());
            AssertBet(ev);
        }

        [Test]
        public void Chips_dropped_into_the_muck_are_a_bet_not_a_fold()
        {
            var ev = PlaceAndWait(Layout.MuckCenter + new Vector3(0f, 0.02f, 0f));
            AssertBet(ev);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }

        // ── 不該算下注 ──

        [Test]
        public void Putting_chips_back_on_the_stack_is_not_a_bet()
        {
            // 實機 (0.21, 0.10)、(0.23, 0.12)
            AssertNoBet(PlaceAndWait(new Vector3(0.21f, 0.03f, 0.10f)));
            AssertNoBet(PlaceAndWait(new Vector3(0.24f, 0.03f, 0.21f)));
        }

        [Test]
        public void Dropping_chips_on_own_cards_is_not_a_bet()
        {
            AssertNoBet(PlaceAndWait(new Vector3(0.0f, 0.03f, 0.15f)));
            AssertNoBet(PlaceAndWait(new Vector3(0.03f, 0.03f, 0.20f)));
        }

        [Test]
        public void Releasing_while_pulling_the_chips_back_is_not_a_bet()
        {
            // 拿到前面又改變主意往回拉（0.4 m/s，不到丟出去的速度），在 z 0.21–0.30 放開
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            var fwd = new Vector3(0.12f, 0.03f, 0.32f);
            GestureScripts.PlaceAt(s, Hand.Right, fwd);
            s.Move(Hand.Right, 0.1, HandTarget.Pinch(1f)); // 又捏回（撈回）
            var palm = SyntheticHands.PalmForPinch(Hand.Right, fwd);
            s.Wait(0.1);
            s.Move(Hand.Right, 0.2, HandTarget.At(palm + new Vector3(0, 0, -0.08f)), Ease.Linear);
            s.Move(Hand.Right, 0.1, HandTarget.At(palm + new Vector3(0, 0, -0.12f)).WithPinch(0f), Ease.Linear);
            s.Move(Hand.Right, 0.2, HandTarget.At(palm + new Vector3(0, 0.05f, -0.16f)));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetRetracted), Is.EqualTo(1), ev.Dump());
            var rel = ev.Last(GestureKind.ChipsReleased);
            Assert.That(rel.Position.Z, Is.InRange(0.21f, 0.27f), "腳本要在短區放開\n" + ev.Dump());
            Assert.That(rel.InBetZone, Is.False, ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero, ev.Dump());
        }

        [Test]
        public void Setting_chips_down_on_the_felt_short_of_the_bet_zone_is_not_a_bet()
        {
            // 合成日常雜動：拿起籌碼、就近貼桌放下（離桌 ≤ 3 cm、幾乎不動）。和偏短的下注只差在「丟／從上方放」
            AssertNoBet(PlaceAndWait(new Vector3(0.14f, 0.02f, 0.225f)));
            AssertNoBet(PlaceAndWait(new Vector3(0.28f, 0.02f, 0.255f)));
        }

        [Test]
        public void Landing_far_off_to_the_side_is_not_a_bet()
        {
            AssertNoBet(PlaceAndWait(new Vector3(0.62f, 0.03f, 0.40f)));
        }

        [Test]
        public void Not_players_turn_is_never_a_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.14f, 0.08f, 0.225f));
            s.Wait(2.0);
            double picked = 1.2;
            var ev = G.Run(s, t => t < picked ? G.Ctx() : G.Ctx(turn: false));
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero, ev.Dump());
        }

        // ── 撈回：籌碼畫在夾進下注區的位置，在那裡撈也要撈得回來 ──

        [Test]
        public void Overshoot_can_be_retracted_where_the_chips_are_shown()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.Throw(s, Hand.Right, new Vector3(0.05f, 0.18f, 0.15f), new Vector3(-0.15f, 0.4f, 2.4f));
            var ev0 = G.Run(s);
            var landing = ev0.First(GestureKind.ChipsReleased).Position;
            var shown = Layout.BetDisplayPoint(landing);
            Assert.That(Vector3.Distance(shown, landing), Is.GreaterThan(Tuning.RetractRadius), "顯示位置要離預測落點夠遠，才測得到");
            GestureScripts.Retract(s, Hand.Right, shown + new Vector3(0, 0.02f, 0));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetRetracted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero, ev.Dump());
        }

        [Test]
        public void Bet_display_point_keeps_chips_inside_the_bet_zone()
        {
            var z = Layout.BetZone;
            var p = Layout.BetDisplayPoint(new Vector3(-0.09f, 0f, 0.94f));
            Assert.That(p.Z, Is.EqualTo(System.Math.Min(z.MaxZ, Layout.FeltSize.Y) - 0.12f).Within(1e-5f));
            Assert.That(p.X, Is.EqualTo(-0.09f).Within(1e-5f));
            var q = Layout.BetDisplayPoint(new Vector3(0.45f, 0f, 0.22f));
            Assert.That(q.X, Is.EqualTo(z.MaxX - 0.03f).Within(1e-5f));
            Assert.That(q.Z, Is.EqualTo(z.MinZ + 0.03f).Within(1e-5f));
        }

        static readonly GestureTuning Tuning = new GestureTuning();
    }
}
