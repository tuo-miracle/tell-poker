using System;
using NUnit.Framework;
using Poker.AI;

namespace Poker.Tests.AI
{
    public class PersonalityTests
    {
        [Test]
        public void Presets_FollowArchetypes()
        {
            var r = Personality.Rookie; var l = Personality.Loudmouth; var c = Personality.Cold;
            Assert.Greater(l.Vpip, c.Vpip); Assert.Greater(c.Vpip, r.Vpip);
            Assert.Greater(l.Pfr, c.Pfr); Assert.Greater(c.Pfr, r.Pfr);
            Assert.Greater(l.Aggression, r.Aggression);
            Assert.Less(r.Pfr / r.Vpip, 0.5f, "新手被動：多半跟注不加注");
            Assert.Greater(r.TellCredibility, l.TellCredibility); Assert.Greater(l.TellCredibility, c.TellCredibility);
            Assert.GreaterOrEqual(r.TellCredibility, 0.85f, "預設要好讀");
            Assert.Greater(c.ReverseActing, 0.5f, "冷面會反向演出");
            Assert.Greater(c.BluffFrequency, r.BluffFrequency);
            Assert.Less(l.FoldToBet, r.FoldToBet);
        }

        [Test]
        public void Presets_TargetsSitInsideTheirBands()
        {
            foreach (var p in new[] { Personality.Rookie, Personality.Loudmouth, Personality.Cold })
            {
                Assert.That(p.Vpip, Is.InRange(p.Targets.VpipMin, p.Targets.VpipMax), p.Name);
                Assert.That(p.Pfr, Is.InRange(p.Targets.PfrMin, p.Targets.PfrMax), p.Name);
                Assert.Less(p.Targets.AfMin, p.Targets.AfMax);
            }
        }

        [Test]
        public void Presets_AreFreshCopies()
        {
            var a = Personality.Rookie; a.Vpip = 0.9f;
            Assert.AreNotEqual(0.9f, Personality.Rookie.Vpip);
        }
    }

    public class PlayerReadTests
    {
        static PlayerReadSettings On() => new PlayerReadSettings();

        static PlayerSignals Tank(double t) => new PlayerSignals { HesitationSeconds = 12, PeekSeconds = 8, Delivery = ChipDelivery.Thrown, ThrowSpeed = 3f, Time = t };
        static PlayerSignals Snap(double t) => new PlayerSignals { HesitationSeconds = 0.3f, Delivery = ChipDelivery.Placed, ThrowSpeed = 0.1f, Time = t };

        [Test]
        public void NoSignal_NoShift()
        {
            var r = new PlayerRead(1, 1f, On());
            Assert.AreEqual(0, r.CurrentShift(0));
            r.Observe(new PlayerSignals { HesitationSeconds = 3, Time = 1 });
            Assert.AreEqual(0, r.CurrentShift(1), 1e-9);
        }

        [Test]
        public void Shift_IsCappedAtMaxShift_EvenWithExtremeSignals()
        {
            var r = new PlayerRead(1, 1f, On());
            for (int i = 0; i < 200; i++) r.Observe(Tank(i * 0.1));
            Assert.LessOrEqual(Math.Abs(r.CurrentShift(20)), 0.08 + 1e-9);
            Assert.Greater(r.CurrentShift(20), 0.05, "連續猶豫應該接近上限");
            var r2 = new PlayerRead(1, 1f, On());
            for (int i = 0; i < 200; i++) r2.Observe(Snap(i * 0.1));
            Assert.GreaterOrEqual(r2.CurrentShift(20), -0.08 - 1e-9);
            Assert.Less(r2.CurrentShift(20), 0);
        }

        [Test]
        public void MaxShift_CannotBeConfiguredAbove008()
        {
            var s = new PlayerReadSettings { MaxShift = 0.5f };
            Assert.AreEqual(0.08f, s.MaxShift, 1e-6);
        }

        [Test]
        public void Shift_DecaysOverTime()
        {
            var r = new PlayerRead(1, 1f, On());
            r.Observe(Tank(0));
            double s0 = r.CurrentShift(0), s30 = r.CurrentShift(30), s60 = r.CurrentShift(60), s600 = r.CurrentShift(600);
            Assert.Greater(s0, 0);
            Assert.Less(s30, s0); Assert.Less(s60, s30);
            Assert.Less(s60, s0 * 0.5);
            Assert.AreEqual(0, s600, 1e-4);
        }

        [Test]
        public void GlobalSwitchAndIntensity_RemoveInfluence()
        {
            var off = new PlayerReadSettings { Enabled = false };
            var r = new PlayerRead(1, 1f, off);
            r.Observe(Tank(0));
            Assert.AreEqual(0, r.CurrentShift(0));
            var zero = new PlayerReadSettings { Intensity = 0 };
            var r2 = new PlayerRead(1, 1f, zero);
            r2.Observe(Tank(0));
            Assert.AreEqual(0, r2.CurrentShift(0));
        }

        [Test]
        public void Sensitivity_ScalesShift()
        {
            var a = new PlayerRead(1, 1f, On()); a.Observe(Tank(0));
            var b = new PlayerRead(1, 0.5f, On()); b.Observe(Tank(0));
            Assert.AreEqual(a.CurrentShift(0) * 0.5, b.CurrentShift(0), 1e-6);
        }

        [Test]
        public void Slam_Signal_IsGone()
        {
            // 摔牌手勢已移除：AI 不再收到摔牌速度，也沒有「被摔牌嚇到」的注意事項
            Assert.IsNull(typeof(PlayerSignals).GetField("SlamSpeed"));
            Assert.IsNull(typeof(PlayerReadSettings).GetField("SlamSpeed"));
            Assert.IsFalse(System.Enum.IsDefined(typeof(PlayerNotice), "Slam"));
        }

        [Test]
        public void Notices_DescribeWhatWasRead()
        {
            var r = new PlayerRead(2, 1f, On());
            var n = r.Observe(new PlayerSignals { HesitationSeconds = 9, Time = 0 });
            Assert.IsTrue(n.HasFlag(PlayerNotice.LongHesitation));
            n = r.Observe(new PlayerSignals { GazeSeat = 2, GazeSeconds = 1.5f, Time = 2 });
            Assert.IsTrue(n.HasFlag(PlayerNotice.StaredAtMe));
            n = r.Observe(new PlayerSignals { GazeSeat = 3, GazeSeconds = 1.5f, Time = 2 });
            Assert.IsFalse(n.HasFlag(PlayerNotice.StaredAtMe), "盯別人不算");
        }
    }
}
