using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 依 Quest 3 真實錄製修正的敲桌規則（資料見 tasks 回報）：
    /// 真人第二下常比第一下輕（0.33–0.43 m/s）、兩下幾乎敲在同一點（水平差 ≤ 1.7 cm）、
    /// 手下來時往前漂 3 cm 會被當成推牌而擋掉敲桌。
    /// </summary>
    public class KnockRealDataTests
    {
        const float Rest = 0.025f;

        /// <summary>一下敲擊：抬起 height（up 秒）、等加速落下（down 秒），之後停 pause 秒。</summary>
        static void Tap(SyntheticHands s, Hand h, Vector3 low, float height, double down, double up = 0.1, double pause = 0.03)
        {
            s.Move(h, up, HandTarget.At(low + new Vector3(0, height, 0)), Ease.Out);
            s.Move(h, down, HandTarget.At(low), Ease.In);
            s.Wait(pause);
        }

        static SyntheticHands Approach(Vector3 low, Hand h = Hand.Right)
        {
            var s = new SyntheticHands();
            s.Move(h, 0.4, HandTarget.At(low + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(low));
            s.Wait(0.3);
            return s;
        }

        static Vector3 Spot => GestureScripts.KnockSpot(Hand.Right) + new Vector3(0, Rest, 0);

        [Test]
        public void Gentle_second_tap_in_a_quick_double_knock_checks()
        {
            var s = Approach(Spot);
            Tap(s, Hand.Right, Spot, 0.045f, 0.09);
            Tap(s, Hand.Right, Spot, 0.03f, 0.13);
            s.Wait(0.5);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Two_gentle_taps_do_not_check()
        {
            var s = Approach(Spot);
            Tap(s, Hand.Right, Spot, 0.03f, 0.13);
            Tap(s, Hand.Right, Spot, 0.03f, 0.13);
            s.Wait(0.5);
            Assert.That(G.Run(s).Count(GestureKind.KnockCheck), Is.Zero);
        }

        [Test]
        public void Gentle_tap_late_in_the_window_does_not_check()
        {
            var s = Approach(Spot);
            Tap(s, Hand.Right, Spot, 0.045f, 0.09, pause: 0.25);
            Tap(s, Hand.Right, Spot, 0.03f, 0.13);
            s.Wait(0.5);
            Assert.That(G.Run(s).Count(GestureKind.KnockCheck), Is.Zero, "0.45 s 以上的慢節奏，兩下都要夠快");
        }

        [Test]
        public void Taps_at_two_different_spots_do_not_pair()
        {
            var s = Approach(Spot);
            Tap(s, Hand.Right, Spot, 0.045f, 0.09);
            Tap(s, Hand.Right, Spot + new Vector3(0.06f, 0, 0), 0.045f, 0.09);
            s.Wait(0.5);
            Assert.That(G.Run(s).Count(GestureKind.KnockCheck), Is.Zero);
        }

        [Test]
        public void Knock_beside_the_cards_is_not_blocked_by_the_hand_drifting_forward()
        {
            // 真實錄製：手在牌右前方 (0.10, 0.06) 落到桌上時往前漂 3–4 cm，推牌先佔住行動，敲桌就被擋掉
            var start = new Vector3(0.10f, Rest + 0.05f, 0.03f);
            var low = new Vector3(0.10f, Rest, 0.08f);
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.4, HandTarget.At(start));
            s.Move(Hand.Right, 0.3, HandTarget.At(low));
            s.Wait(0.1);
            Tap(s, Hand.Right, low, 0.045f, 0.09);
            Tap(s, Hand.Right, low, 0.045f, 0.09);
            s.Wait(0.5);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }
    

        // ── 手指敲擊（真實錄製最後 4 段：手掌貼桌幾乎不動、食指中指抬起 9–15 cm 以 1.2–1.7 m/s 敲下）──
        [Test]
        public void Finger_double_tap_with_resting_palm_checks()
        {
            var ev = G.Run(GestureScripts.FingerTap(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Single_finger_tap_does_not_check() =>
            Assert.That(G.Run(GestureScripts.FingerTap(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right), taps: 1)).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Finger_taps_with_the_hand_in_the_air_do_not_check() =>
            Assert.That(G.Run(GestureScripts.FingerTap(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right) + new Vector3(0, 0.12f, 0))).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Slow_finger_wiggles_do_not_check() =>
            Assert.That(G.Run(GestureScripts.FingerTap(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right), lift: 0.6f, down: 0.3)).Count(GestureKind.KnockCheck), Is.Zero);
    }
}
