using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class ChipBetTests
    {
        static readonly Vector3 InZone = new Vector3(0.05f, 0.02f, 0.42f);
        /// <summary>牌的右前方、還沒過牌前緣（z 0.18）：不算下注。2026-10-13 起 z ≥ 0.21 的落點已算下注（ChipTossLandingTests）。</summary>
        static readonly Vector3 OutOfZone = new Vector3(0.10f, 0.02f, 0.18f);

        [Test]
        public void Pinching_the_stack_picks_one_chip_and_lifting_adds_one_per_step()
        {
            var ev = G.Run(GestureScripts.PickChips(new SyntheticHands(), lift: 0.08f));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            var picked = ev.First(GestureKind.ChipsPicked);
            Assert.That(picked.Hand, Is.EqualTo(Hand.Right));
            Assert.That(picked.Count, Is.EqualTo(1));
            // 抬 8 cm、一階 2.5 cm → 1 + 3 = 4 枚，數量只增不跳
            var counts = ev.Where(e => e.Kind == GestureKind.ChipCountChanged).Select(e => e.Count).ToList();
            Assert.That(counts, Is.EqualTo(new[] { 2, 3, 4 }), ev.Dump());
        }

        [Test]
        public void Chip_count_is_capped_by_remaining_chips()
        {
            var ev = G.Run(GestureScripts.PickChips(new SyntheticHands(), lift: 0.15f), G.Ctx(chips: 120, denom: 50));
            Assert.That(ev.Where(e => e.Kind == GestureKind.ChipCountChanged).Max(e => e.Count), Is.EqualTo(2));
        }

        [Test]
        public void Placing_chips_in_bet_zone_commits_after_retract_window()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.06f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone);
            double releaseTime = s.Time;
            s.Wait(1.0);
            double beforeWindow = s.Time;
            s.Wait(1.0);
            var ev = G.Run(s);
            var rel = ev.First(GestureKind.ChipsReleased);
            Assert.That(rel.InBetZone, Is.True, ev.Dump());
            Assert.That(rel.Count, Is.EqualTo(3));
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
            var commit = ev.First(GestureKind.BetCommitted);
            Assert.That(commit.Count, Is.EqualTo(3));
            Assert.That(commit.Time - rel.Time, Is.EqualTo(1.5).Within(0.05));
            Assert.That(commit.Time, Is.GreaterThan(beforeWindow - 1.0 + 0.4));
        }

        [Test]
        public void Throwing_chips_extrapolates_landing_into_the_bet_zone()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            // 在下注線前（z≈0.2）就放開，但往前丟 1.2 m/s → 落點在線後
            GestureScripts.Throw(s, Hand.Right, new Vector3(0.12f, 0.15f, 0.12f), new Vector3(0, 0, 1.2f));
            s.Wait(1.7);
            var ev = G.Run(s);
            var rel = ev.First(GestureKind.ChipsReleased);
            Assert.That(rel.InBetZone, Is.True, ev.Dump());
            Assert.That(rel.Position.Z, Is.GreaterThan(Layout.BetLineZ + 0.03f));
            Assert.That(rel.Position.Y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1));
        }

        static TableLayout Layout => G.Layout;

        [Test]
        public void Near_miss_releasing_outside_bet_zone_is_not_a_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands());
            GestureScripts.PlaceAt(s, Hand.Right, OutOfZone);
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.First(GestureKind.ChipsReleased).InBetZone, Is.False);
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero);
        }

        [Test]
        public void Near_miss_gentle_toss_that_falls_short_is_not_a_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.Throw(s, Hand.Right, new Vector3(0.12f, 0.06f, 0.12f), new Vector3(0, 0, 0.35f));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.First(GestureKind.ChipsReleased).InBetZone, Is.False, ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero);
        }

        [Test]
        public void Near_miss_retracting_within_window_cancels_the_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.06f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone);
            GestureScripts.Retract(s, Hand.Right, InZone);
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetRetracted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.BetRetracted).Count, Is.EqualTo(3));
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero);
            // 撈回後手上拿的就是那 3 枚
            GestureScripts.PlaceAt(s, Hand.Right, OutOfZone);
            ev = G.Run(s);
            var lastRel = ev.Last(GestureKind.ChipsReleased);
            Assert.That(lastRel.Count, Is.EqualTo(3));
            Assert.That(lastRel.InBetZone, Is.False);
        }

        [Test]
        public void Two_drops_add_up_and_commit_once()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.0f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone);
            GestureScripts.PickChips(s, lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone + new Vector3(0.05f, 0, 0));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.BetCommitted).Count, Is.EqualTo(1 + 2));
        }

        [Test]
        public void Bet_survives_1cm_noise()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(seed: 11).WithNoise(0.01f), lift: 0.06f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone);
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.BetCommitted).Count, Is.InRange(2, 4));
        }
    }
}
