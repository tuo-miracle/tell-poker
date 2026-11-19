using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class SyntheticHandsTests
    {
        [Test]
        public void Emits_frames_at_72_hz_with_monotonic_time()
        {
            var s = new SyntheticHands().Wait(1.0).Wait(1.0);
            Assert.That(s.Frames.Count, Is.EqualTo(145));
            for (int i = 1; i < s.Frames.Count; i++)
                Assert.That(s.Frames[i].Time - s.Frames[i - 1].Time, Is.EqualTo(1.0 / 72).Within(1e-9));
        }

        [Test]
        public void Palm_down_and_palm_up_set_palm_normal()
        {
            var s = new SyntheticHands().Set(Hand.Right, new Vector3(0, 0.05f, 0.15f)).Wait(0.1)
                .Move(Hand.Right, 0.2, HandTarget.Roll(180f)).Wait(0.05);
            Vector3 down = s.Frames[0].Right.PalmNormal, up = s.Frames.Last().Right.PalmNormal;
            Assert.That(down.Y, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(up.Y, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Palm_for_pinch_puts_pinch_point_on_target()
        {
            var target = new Vector3(0.2f, 0.02f, 0.12f);
            foreach (Hand h in new[] { Hand.Left, Hand.Right })
            {
                var s = new SyntheticHands().Set(h, SyntheticHands.PalmForPinch(h, target), pinch: 1f).Wait(0);
                var pose = s.Frames[0].Get(h);
                var mid = (pose.Position(HandJoint.ThumbTip) + pose.Position(HandJoint.IndexTip)) * 0.5f;
                Assert.That(Vector3.Distance(mid, target), Is.LessThan(1e-4f), h.ToString());
                Assert.That(pose.IndexPinch, Is.EqualTo(SyntheticHands.PinchStrengthCap));   // 合成手刻意封頂在真機的 0.70
            }
        }

        [Test]
        public void Linear_move_interpolates_position_and_keeps_other_hand()
        {
            var s = new SyntheticHands().Set(Hand.Right, new Vector3(0, 0.1f, 0)).Set(Hand.Left, new Vector3(-0.3f, 0.02f, 0))
                .Move(Hand.Right, 1.0, HandTarget.At(0.72f, 0.1f, 0), Ease.Linear);
            var mid = s.Frames[36];
            Assert.That(mid.Right.Position(HandJoint.Palm).X, Is.EqualTo(0.36f).Within(1e-4f));
            Assert.That(mid.Left.Position(HandJoint.Palm).X, Is.EqualTo(-0.3f).Within(1e-6f));
        }

        [Test]
        public void Wrist_rises_when_pitched_up()
        {
            var s = new SyntheticHands().Set(Hand.Right, new Vector3(0, 0.03f, 0.15f)).Wait(0)
                .Move(Hand.Right, 0.1, HandTarget.Pitch(30f)).Wait(0);
            float before = s.Frames[0].Right.Position(HandJoint.Wrist).Y, after = s.Frames.Last().Right.Position(HandJoint.Wrist).Y;
            Assert.That(after - before, Is.GreaterThan(0.03f));
        }

        [Test]
        public void Noise_has_about_one_centimetre_spread()
        {
            var still = new Vector3(0, 0.1f, 0.2f);
            var s = new SyntheticHands(seed: 3).WithNoise(0.01f).Set(Hand.Right, still).Wait(60);
            double sum = 0;
            foreach (var f in s.Frames) sum += (f.Right.Position(HandJoint.Palm) - still).LengthSquared();
            double rms3d = Math.Sqrt(sum / s.Frames.Count);
            // 三軸各約 1 cm → 3D RMS 約 1.7 cm
            Assert.That(rms3d / Math.Sqrt(3), Is.InRange(0.007, 0.013));
        }

        [Test]
        public void Untracked_hand_reports_tracked_false()
        {
            var s = new SyntheticHands().Track(Hand.Left, false).Wait(0.1);
            Assert.That(s.Frames.All(f => !f.Left.Tracked && f.Right.Tracked));
        }
    }
}
