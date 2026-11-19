using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>審查回饋的回歸測試。</summary>
    public class ReviewFixTests
    {
        static readonly Vector3 InZone = new Vector3(0.05f, 0.02f, 0.42f);

        [Test]
        public void Second_knock_late_in_the_window_still_counts()
        {
            // 兩次峰值相隔約 0.57 s（< 0.6），第二下的下敲在 0.6 s 之後才結束
            var ev = G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right), gap: 0.385));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void All_in_countdown_cancels_when_turn_ends()
        {
            var s = GestureScripts.AllIn(new SyntheticHands(), holdAfter: 0.0);
            double t = s.Time;
            s.Wait(1.5);
            var ev = G.Run(s, x => x < t ? G.Ctx() : G.Ctx(turn: false));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInCancelled), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void All_in_countdown_cancels_after_long_tracking_loss()
        {
            var s = GestureScripts.AllIn(new SyntheticHands(), holdAfter: 0.0);
            s.Track(Hand.Left, false).Wait(3.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.AllInCancelled), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero);
        }

        [Test]
        public void Wide_all_in_pushed_without_pausing_is_not_a_fold()
        {
            // 雙手一到位（距離 32 cm，左手落在牌的推牌起點範圍內）就立刻往前推，來不及進入 All-in 預備
            var c = G.Stack + new Vector3(0, 0.03f, -0.01f);
            var l = c - new Vector3(0.16f, 0, 0);
            var r = c + new Vector3(0.16f, 0, 0);
            var s = new SyntheticHands().Set(Hand.Left, l).Set(Hand.Right, r);
            var fwd = new Vector3(0, 0, 0.26f);
            s.Move(0.55, HandTarget.At(r + fwd), HandTarget.At(l + fwd));
            s.Wait(1.4);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Only_one_action_when_a_slap_on_the_cards_and_a_knock_land_together()
        {
            var s = new SyntheticHands();
            var spot = GestureScripts.KnockSpot(Hand.Right) + new Vector3(0, 0.025f, 0);
            var top = G.Cards + new Vector3(-0.02f, 0.25f, 0);
            var low = G.Cards + new Vector3(-0.02f, 0.03f, 0);
            s.Move(0.5, HandTarget.At(spot + new Vector3(0, 0.05f, 0)), HandTarget.At(top));
            s.Move(0.25, HandTarget.At(spot), null);
            s.Wait(0.3);
            s.Move(Hand.Right, 0.12, HandTarget.At(spot + new Vector3(0, 0.05f, 0)), Ease.Out);
            s.Move(Hand.Right, 0.09, HandTarget.At(spot), Ease.In);
            s.Wait(0.05);
            s.Move(0.12, HandTarget.At(spot + new Vector3(0, 0.05f, 0)), null, Ease.Out);
            s.Move(0.12, HandTarget.At(spot), HandTarget.At(low), Ease.In);
            s.Wait(0.5);
            var ev = G.Run(s);
            Assert.That(ev.Count(e => e.IsAction), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Pinching_far_from_the_dropped_chips_does_not_retract()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone);
            GestureScripts.Retract(s, Hand.Right, new Vector3(-0.25f, 0.02f, 0.62f));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.BetRetracted), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Reset_clears_a_pending_bet()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, InZone);
            double resetAt = s.Time;
            s.Wait(2.0);
            var r = new GestureRecognizer(G.Layout);
            var ctx = G.Ctx();
            bool done = false;
            int commits = 0;
            foreach (var f in s.Frames)
            {
                if (!done && f.Time >= resetAt) { r.Reset(); done = true; }
                commits += r.Update(f, ctx).Count(e => e.Kind == GestureKind.BetCommitted);
            }
            Assert.That(commits, Is.Zero);
            Assert.That(r.PendingBetChips, Is.Zero);
        }
    }
}
