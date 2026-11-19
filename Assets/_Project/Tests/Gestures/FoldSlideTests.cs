using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-12 第二次實機 log：一路貼桌滑牌從沒成功。348.9 s 手掌從 z 0.05（指尖壓在牌上）滑到 0.19、往前 14 cm，
    /// 1.46 秒後以「too slow」重設：棄牌線 0.27 是用手掌判斷，手掌在牌後方約 10 cm，要推 22 cm 手掌才會過線。
    /// </summary>
    public class FoldSlideTests
    {
        static SyntheticHands Noisy() => new SyntheticHands().WithNoise(0.01f);

        [Test]
        public void Slow_14cm_slide_with_the_fingers_carrying_the_cards_past_the_fold_line_folds()
        {
            var ev = G.Run(GestureScripts.FoldSlide(Noisy(), Hand.Right, 0.14f, 1.6, 0.05f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Where(e => e.IsAction).All(e => e.Kind == GestureKind.FoldPushed), ev.Dump());
        }

        [Test]
        public void A_two_second_slide_still_folds()
        {
            var ev = G.Run(GestureScripts.FoldSlide(Noisy(), Hand.Right, 0.14f, 2.0, 0.05f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Slide_that_stops_before_the_fingers_reach_the_fold_line_does_not_fold()
        {
            // 手掌從 z 0.0 往前 13 cm：指尖停在 0.23
            var ev = G.Run(GestureScripts.FoldSlide(Noisy(), Hand.Right, 0.13f, 1.4, 0.0f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void Sliding_only_5cm_forward_does_not_fold()
        {
            // 手放在牌上（指尖已在 z 0.25）往前挪 5 cm：指尖過線但沒推夠遠
            var ev = G.Run(GestureScripts.FoldSlide(Noisy(), Hand.Right, 0.05f, 1.0, 0.15f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void Pulling_the_cards_back_toward_the_body_does_not_fold()
        {
            var ev = G.Run(GestureScripts.FoldSlide(Noisy(), Hand.Right, -0.14f, 1.6, 0.12f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void Peek_lift_and_return_never_folds()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var h = seed % 2 == 0 ? Hand.Right : Hand.Left;
                var s = GestureScripts.Peek(new SyntheticHands(seed: seed).WithNoise(0.01f), h,
                    0.01f * (seed % 3 - 1), 0.02f * (seed % 3 - 1), 30f + seed, 0.02f + 0.001f * seed, 0.4 + 0.05 * seed);
                s.Move(h, 0.6, HandTarget.At(h == Hand.Right ? SyntheticHands.RightRest : SyntheticHands.LeftRest));
                s.Wait(0.5);
                var ev = G.Run(s);
                Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(0), $"seed {seed}\n" + ev.Dump());
            }
        }

        [Test]
        public void Resting_a_hand_on_the_cards_for_several_seconds_does_not_fold()
        {
            var s = Noisy();
            s.Move(Hand.Right, 0.6, HandTarget.At(new Vector3(G.Cards.X, 0.025f, 0.06f)));
            s.Wait(5.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(0), ev.Dump());
        }
    }
}
