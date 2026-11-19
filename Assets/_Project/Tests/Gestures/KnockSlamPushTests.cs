using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class KnockTests
    {
        static SyntheticHands Knock(SyntheticHands s = null, int taps = 2, float height = 0.04f, double gap = 0.05, double down = 0.09, Vector3? at = null) =>
            GestureScripts.Knock(s ?? new SyntheticHands(), Hand.Right, at ?? GestureScripts.KnockSpot(Hand.Right), taps, height, down: down, gap: gap);

        [Test]
        public void Double_knock_on_table_checks_once()
        {
            var ev = G.Run(Knock());
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.KnockCheck).Hand, Is.EqualTo(Hand.Right));
            Assert.That(ev.Count(e => e.IsAction), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Left_hand_double_knock_checks()
        {
            var ev = G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Left, GestureScripts.KnockSpot(Hand.Left)));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Near_miss_single_knock_does_not_check() =>
            Assert.That(G.Run(Knock(taps: 1)).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Near_miss_two_knocks_too_far_apart_do_not_check() =>
            Assert.That(G.Run(Knock(gap: 0.6)).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Near_miss_double_tap_in_the_air_does_not_check() =>
            Assert.That(G.Run(Knock(at: GestureScripts.KnockSpot(Hand.Right) + new Vector3(0, 0.15f, 0))).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Near_miss_slow_double_lowering_does_not_check() =>
            Assert.That(G.Run(Knock(down: 0.4, height: 0.04f)).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Knock_is_ignored_when_check_not_allowed() =>
            Assert.That(G.Run(Knock(), G.Ctx(canCheck: false)).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Knock_survives_1cm_noise()
        {
            var ev = G.Run(Knock(new SyntheticHands(seed: 5).WithNoise(0.01f)));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }
    }

    /// <summary>
    /// 摔牌（拍桌棄牌）已移除（2026-10-13 開發者回饋）：往牌上拍下去不再是任何行動。
    /// 原本的摔牌腳本留著當反例：重拍、輕拍、捏著牌拍、加雜訊，都不能變成棄牌、過牌或其他行動。
    /// </summary>
    public class SlamRemovedTests
    {
        [Test]
        public void Slam_api_is_gone()
        {
            Assert.That(System.Enum.GetNames(typeof(GestureKind)).Where(n => n.Contains("Slam")), Is.Empty);
            Assert.That(typeof(GestureTuning).GetFields().Select(f => f.Name).Where(n => n.StartsWith("Slam")), Is.Empty);
            Assert.That(typeof(GestureEvent).GetField("Level"), Is.Null, "SlamLevel 只給摔牌用");
            Assert.That(typeof(GestureKind).Assembly.GetType("Poker.Gestures.SlamLevel"), Is.Null);
        }

        [TestCase(0.25f, 0.15, false, TestName = "Slam_motion_default_is_not_an_action")]
        [TestCase(0.32f, 0.13, false, TestName = "Slam_motion_heavy_is_not_an_action")]
        [TestCase(0.22f, 0.20, false, TestName = "Slam_motion_light_is_not_an_action")]
        [TestCase(0.25f, 0.15, true, TestName = "Slam_motion_while_pinching_cards_is_not_an_action")]
        public void Slam_motion_over_cards_does_nothing(float height, double duration, bool pinchCards)
        {
            var ev = G.Run(GestureScripts.Slam(new SyntheticHands(), height: height, duration: duration, pinchCards: pinchCards));
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
        }

        [Test]
        public void Slam_motion_with_1cm_noise_does_nothing()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var ev = G.Run(GestureScripts.Slam(new SyntheticHands(seed: seed).WithNoise(0.01f)));
                Assert.That(ev.AnyAction(), Is.False, $"seed {seed}\n" + ev.Dump());
            }
        }

        [Test]
        public void Double_knock_right_after_a_slap_on_the_cards_still_checks()
        {
            // 以前摔牌後 0.3 s 不接受敲桌；摔牌移除後，拍完牌馬上在桌上敲兩下就是過牌
            var s = GestureScripts.Slam(new SyntheticHands(), Hand.Left, dx: -0.02f);
            GestureScripts.Knock(s, Hand.Right, GestureScripts.KnockSpot(Hand.Right));
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }
    }

    public class FoldPushTests
    {
        [Test]
        public void Pushing_cards_past_fold_line_folds()
        {
            var ev = G.Run(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(e => e.IsAction), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Near_miss_short_push_does_not_fold() =>
            Assert.That(G.Run(GestureScripts.FoldPush(new SyntheticHands(), distance: 0.06f)).Count(GestureKind.FoldPushed), Is.Zero);

        [Test]
        public void Near_miss_hand_moving_forward_above_table_does_not_fold() =>
            Assert.That(G.Run(GestureScripts.FoldPush(new SyntheticHands(), height: 0.13f)).Count(GestureKind.FoldPushed), Is.Zero);

        [Test]
        public void Near_miss_sliding_hand_forward_away_from_cards_does_not_fold() =>
            Assert.That(G.Run(GestureScripts.FoldPush(new SyntheticHands(), dx: -0.25f)).Count(GestureKind.FoldPushed), Is.Zero);

        [Test]
        public void Fold_push_survives_1cm_noise()
        {
            var ev = G.Run(GestureScripts.FoldPush(new SyntheticHands(seed: 13).WithNoise(0.01f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }
    }
}
