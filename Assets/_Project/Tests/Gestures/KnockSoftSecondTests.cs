using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-13 實機 log（遊戲時間 3586–3599）：開發者張開手敲兩下時，第一下都有判到（峰值 0.39–0.85 m/s、高度 2.6–4.6 cm、落差 4.1–6.1 cm），
    /// 第二下只有 0.24–0.34 m/s（低於開始一下的 0.35），全部「等不到第二下」；3599.065 兩下 0.36／0.39 m/s、間隔 0.49 s 被判 weak。
    /// 第一下成立後，第二下用較低的起始速度；單敲一下後手放鬆落回桌面（0.06–0.12 m/s）仍不算。
    /// </summary>
    public class KnockSoftSecondTests
    {
        const float Rest = 0.025f;
        static Vector3 Spot => GestureScripts.KnockSpot(Hand.Right) + new Vector3(0, Rest, 0);

        static SyntheticHands Approach(int seed = 0)
        {
            var s = new SyntheticHands(seed: seed);
            if (seed != 0) s.WithNoise(0.01f).WithPinchCap();
            s.Move(Hand.Right, 0.4, HandTarget.At(Spot + new Vector3(0, 0.05f, 0)));
            s.Move(Hand.Right, 0.25, HandTarget.At(Spot));
            s.Wait(0.3);
            return s;
        }

        /// <summary>一下：抬起 height（up 秒）、等加速落下（down 秒；末速 ≈ 2·height/down），停 pause。</summary>
        static void Tap(SyntheticHands s, float height, double down, double up = 0.1, double pause = 0.03)
        {
            s.Move(Hand.Right, up, HandTarget.At(Spot + new Vector3(0, height, 0)), Ease.Out);
            s.Move(Hand.Right, down, HandTarget.At(Spot), Ease.In);
            s.Wait(pause);
        }

        [Test]
        public void Clear_first_tap_then_slow_second_tap_checks([Values(0.24f, 0.28f, 0.33f)] float secondSpeed)
        {
            // 第二下：抬 4 cm，末速 secondSpeed（平滑後的峰值約再低 10%）
            var s = Approach();
            Tap(s, 0.045f, 0.09);
            Tap(s, 0.04f, 2 * 0.04 / (secondSpeed * 1.1));
            s.Wait(0.6);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Two_moderate_taps_half_a_second_apart_check()
        {
            // 3599.065：0.36／0.39 m/s、間隔 0.49 s、落差 3.6–4.1 cm
            var s = Approach();
            Tap(s, 0.05f, 0.24);
            Tap(s, 0.05f, 0.24, up: 0.22, pause: 0.0);
            s.Wait(0.6);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Single_tap_then_hand_settles_does_not_check([Values(0.06f, 0.09f, 0.12f)] float settleSpeed)
        {
            var s = Approach();
            Tap(s, 0.045f, 0.09);
            // 敲完手放鬆：微微浮起 1.5 cm 再慢慢落回（末速 settleSpeed）
            s.Move(Hand.Right, 0.15, HandTarget.At(Spot + new Vector3(0, 0.015f, 0)), Ease.Out);
            s.Move(Hand.Right, 2 * 0.015 / settleSpeed, HandTarget.At(Spot), Ease.In);
            s.Wait(0.8);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
        }

        [Test]
        public void Single_tap_then_hand_lifts_away_does_not_check()
        {
            var s = Approach();
            Tap(s, 0.045f, 0.09);
            s.Move(Hand.Right, 0.4, HandTarget.At(Spot + new Vector3(-0.05f, 0.12f, -0.1f)));
            s.Wait(0.5);
            Assert.That(G.Run(s).Count(GestureKind.KnockCheck), Is.Zero);
        }

        [Test]
        public void Slow_second_tap_alone_is_not_a_first_tap()
        {
            // 兩下都只有 0.28 m/s：沒有清楚的第一下，不算
            var s = Approach();
            Tap(s, 0.04f, 2 * 0.04 / (0.28 * 1.1));
            Tap(s, 0.04f, 2 * 0.04 / (0.28 * 1.1));
            s.Wait(0.6);
            Assert.That(G.Run(s).Count(GestureKind.KnockCheck), Is.Zero);
        }
    }
}
