using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class SurfaceCalibratorTests
    {
        const double Dt = 1.0 / 72.0;

        /// <summary>雙手平放（掌心朝下）在牌桌座標高度 palmY、左右各 15 cm、距桌緣 z。</summary>
        static HandsFrame Flat(double t, float palmY, float z = 0.2f, float jitter = 0f, bool palmDown = true)
        {
            var f = new HandsFrame { Time = t };
            Set(f.Left, new Vector3(-0.15f + jitter, palmY, z), palmDown);
            Set(f.Right, new Vector3(0.15f - jitter, palmY, z), palmDown);
            return f;
        }

        static void Set(HandPose h, Vector3 palm, bool palmDown)
        {
            h.Tracked = true;
            h.Confidence = 1f;
            // OpenXR Palm 關節的 −Y 指向掌心外側：掌心朝下＝Palm 的 −Y 指向世界 −Y＝不旋轉
            var rot = palmDown ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)System.Math.PI);
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                h.Positions[j] = palm;
                h.Rotations[j] = rot;
            }
        }

        static SurfaceCalibration? Run(SurfaceCalibrator c, System.Func<double, HandsFrame> make, double seconds)
        {
            SurfaceCalibration? result = null;
            for (double t = 0; t < seconds; t += Dt)
            {
                var r = c.Update(make(t));
                if (r.HasValue) result = r;
            }
            return result;
        }

        [Test]
        public void Both_palms_resting_flat_calibrate_the_table_height_and_center()
        {
            var c = new SurfaceCalibrator();
            var r = Run(c, t => Flat(t, palmY: -0.083f), 1.5);
            Assert.That(r.HasValue, Is.True);
            Assert.That(r.Value.SurfaceY, Is.EqualTo(-0.083f - SurfaceCalibrator.PalmThickness).Within(0.002f));
            Assert.That(r.Value.HandsCenter.X, Is.EqualTo(0f).Within(0.002f));
            Assert.That(r.Value.HandsCenter.Z, Is.EqualTo(0.2f).Within(0.002f));
        }

        [Test]
        public void Needs_both_hands_held_still_for_the_dwell_time()
        {
            var c = new SurfaceCalibrator();
            Assert.That(Run(c, t => Flat(t, -0.08f), SurfaceCalibrator.DwellSeconds * 0.6), Is.Null);
        }

        [Test]
        public void Moving_hands_do_not_calibrate()
        {
            var c = new SurfaceCalibrator();
            Assert.That(Run(c, t => Flat(t, -0.08f + 0.1f * (float)System.Math.Sin(t * 6)), 2.0), Is.Null);
        }

        [Test]
        public void Palms_facing_up_do_not_calibrate()
        {
            var c = new SurfaceCalibrator();
            Assert.That(Run(c, t => Flat(t, -0.08f, palmDown: false), 2.0), Is.Null);
        }

        [Test]
        public void Hands_at_different_heights_do_not_calibrate()
        {
            var c = new SurfaceCalibrator();
            Assert.That(Run(c, t =>
            {
                var f = Flat(t, -0.08f);
                for (int j = 0; j < HandPose.JointCount; j++) f.Right.Positions[j] += new Vector3(0, 0.06f, 0);
                return f;
            }, 2.0), Is.Null);
        }

        [Test]
        public void Small_tracking_jitter_still_calibrates()
        {
            var c = new SurfaceCalibrator();
            var rng = new System.Random(3);
            var r = Run(c, t => Flat(t, -0.08f + (float)(rng.NextDouble() - 0.5) * 0.006f), 2.0);
            Assert.That(r.HasValue, Is.True);
            Assert.That(r.Value.SurfaceY, Is.EqualTo(-0.08f - SurfaceCalibrator.PalmThickness).Within(0.004f));
        }

        [Test]
        public void Untracked_hand_resets_the_dwell()
        {
            var c = new SurfaceCalibrator();
            Assert.That(Run(c, t =>
            {
                var f = Flat(t, -0.08f);
                if (t > 0.5 && t < 0.55) f.Left.Tracked = false;
                return f;
            }, SurfaceCalibrator.DwellSeconds + 0.3), Is.Null);
        }
    

        // ---- 牌桌朝向（yaw）----

        static Vector3 RotY(Vector3 v, float deg) => Vector3.Transform(v, Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg * (float)System.Math.PI / 180f));

        /// <summary>雙手平放，整組手相對目前牌桌轉 yawDeg（正值：+Z 往 +X 轉）；手指方向可以另外偏 fingerOffsetDeg。</summary>
        static HandsFrame Turned(double t, float yawDeg, float halfSpacing = 0.15f, float fingerOffsetDeg = 0f, bool swapHands = false)
        {
            var f = new HandsFrame { Time = t };
            var center = new Vector3(0f, -0.08f, 0.2f);
            var l = center + RotY(new Vector3(-halfSpacing, 0, 0), yawDeg);
            var r = center + RotY(new Vector3(halfSpacing, 0, 0), yawDeg);
            if (swapHands) (l, r) = (r, l);
            SetTurned(f.Left, l, yawDeg + fingerOffsetDeg);
            SetTurned(f.Right, r, yawDeg + fingerOffsetDeg);
            return f;
        }

        static void SetTurned(HandPose h, Vector3 palm, float fingerYawDeg)
        {
            Set(h, palm, palmDown: true);
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, fingerYawDeg * (float)System.Math.PI / 180f);
            for (int j = 0; j < HandPose.JointCount; j++) h.Rotations[j] = rot;
            h.Positions[(int)HandJoint.Wrist] = palm + RotY(new Vector3(0, 0.01f, -0.075f), fingerYawDeg);
            h.Positions[(int)HandJoint.MiddleTip] = palm + RotY(new Vector3(0, 0, 0.1f), fingerYawDeg);
        }

        static TableYawEstimate YawOf(System.Func<double, HandsFrame> make, Vector3? head = null)
        {
            var r = Run(new SurfaceCalibrator(), make, 1.5);
            Assert.That(r.HasValue, Is.True, "calibration completes");
            return SurfaceCalibrator.EstimateYaw(r.Value, head);
        }

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(-20f)]
        [TestCase(40f)]
        [TestCase(-40f)]
        public void Table_yaw_follows_the_line_between_the_flat_hands(float yaw)
        {
            var e = YawOf(t => Turned(t, yaw));
            Assert.That(e.Apply, Is.True, e.Reason);
            Assert.That(e.YawDegrees, Is.EqualTo(yaw).Within(0.5f));
        }

        [Test]
        public void Calibration_reports_palm_positions_and_finger_direction_averaged_over_the_dwell()
        {
            var r = Run(new SurfaceCalibrator(), t => Turned(t, 30f), 1.5);
            Assert.That(r.HasValue, Is.True);
            var expectRight = new Vector3(0f, -0.08f, 0.2f) + RotY(new Vector3(0.15f, 0, 0), 30f);
            Assert.That(Vector3.Distance(r.Value.RightPalm, expectRight), Is.LessThan(0.002f));
            var fd = Vector3.Normalize(r.Value.FingerDirection);
            Assert.That(fd.X, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(fd.Z, Is.EqualTo(0.866f).Within(0.01f));
        }

        [Test]
        public void Jittering_hand_angle_is_averaged_over_the_dwell()
        {
            var rng = new System.Random(7);
            // 每格 ±1.5° 的角度抖動（掌心位移約 ±4 mm，小於 StillTolerance，不會打斷停留）
            var e = YawOf(t => Turned(t, 25f + (float)(rng.NextDouble() - 0.5) * 3f));
            Assert.That(e.Apply, Is.True, e.Reason);
            Assert.That(e.YawDegrees, Is.EqualTo(25f).Within(1f));
        }

        [Test]
        public void Forward_points_away_from_the_player_not_toward_them()
        {
            // 左手在左、右手在右，手指朝前：yaw ≈ 0，而不是 180
            var e = YawOf(t => Turned(t, 0f), head: new Vector3(0f, 0.45f, -0.25f));
            Assert.That(e.Apply, Is.True, e.Reason);
            Assert.That(e.YawDegrees, Is.EqualTo(0f).Within(0.5f));
        }

        [Test]
        public void Crossed_hands_with_fingers_forward_keep_the_old_yaw()
        {
            // 左右手互換：手連線推出的前方朝向玩家，與手指方向相反
            var e = YawOf(t => Turned(t, 10f, swapHands: true));
            Assert.That(e.Apply, Is.False);
            Assert.That(e.Reason, Does.Contain("disagree"));
        }

        [Test]
        public void Forward_pointing_at_the_head_keeps_the_old_yaw()
        {
            // 頭在雙手前方（玩家其實站在對面）：推出的前方指向頭，不採用
            var e = YawOf(t => Turned(t, 0f), head: new Vector3(0f, 0.45f, 0.7f));
            Assert.That(e.Apply, Is.False);
            Assert.That(e.Reason, Does.Contain("head"));
        }

        [Test]
        public void Head_off_to_the_side_does_not_block_the_yaw()
        {
            // 頭在雙手正側邊（與雙手中心同深度）：不算「前方指向頭」，只有明顯翻轉才擋
            var e = YawOf(t => Turned(t, 15f), head: new Vector3(0.3f, 0.45f, 0.2f));
            Assert.That(e.Apply, Is.True, e.Reason);
            Assert.That(e.YawDegrees, Is.EqualTo(15f).Within(0.5f));
        }

        [Test]
        public void Hands_too_close_keep_the_old_yaw()
        {
            var e = YawOf(t => Turned(t, 20f, halfSpacing: 0.06f));
            Assert.That(e.Apply, Is.False);
            Assert.That(e.Reason, Does.Contain("close"));
        }

        [Test]
        public void Fingers_disagreeing_with_the_hand_line_keep_the_old_yaw()
        {
            var e = YawOf(t => Turned(t, 0f, fingerOffsetDeg: 50f));
            Assert.That(e.Apply, Is.False);
            Assert.That(e.Reason, Does.Contain("disagree"));
        }

        [Test]
        public void Slightly_angled_fingers_are_blended_but_the_hand_line_dominates()
        {
            var e = YawOf(t => Turned(t, 20f, fingerOffsetDeg: 15f));
            Assert.That(e.Apply, Is.True, e.Reason);
            Assert.That(e.YawDegrees, Is.GreaterThan(20.5f));
            Assert.That(e.YawDegrees, Is.LessThan(27.5f));
        }

        [Test]
        public void Without_finger_data_the_hand_line_alone_sets_the_yaw()
        {
            // 所有關節都在掌心（舊的假資料）：手指方向退化，只用兩掌連線
            var e = YawOf(t =>
            {
                var f = new HandsFrame { Time = t };
                Set(f.Left, new Vector3(-0.15f, -0.08f, 0.25f), true);
                Set(f.Right, new Vector3(0.15f, -0.08f, 0.15f), true);
                return f;
            });
            Assert.That(e.Apply, Is.True, e.Reason);
            Assert.That(e.YawDegrees, Is.EqualTo(18.43f).Within(0.5f));   // 右手較靠近玩家 → 前方往右轉，atan2(0.1, 0.3)
        }
    }
}
