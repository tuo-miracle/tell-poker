using System;
using System.Collections.Generic;
using NUnit.Framework;
using Poker.UX;

namespace Poker.Tests.UX
{
    /// <summary>
    /// 教學的語音那一步只在「同意過、打開、麥克風開著，而且中繼服務連得到」時出現（2026-10-13 教學 v3）。
    /// 連不連得到：語音開著時定期打 GET /health；還沒回來或失敗都當作連不到（那一步就安靜跳過，不提）。
    /// </summary>
    public class VoiceTutorialStateTests
    {
        sealed class Rig
        {
            public bool On = true;
            public readonly List<Action<bool>> Probes = new List<Action<bool>>();
            public VoiceTutorialState State;
            public Rig() { State = new VoiceTutorialState(() => On, () => false, () => 0, done => Probes.Add(done)); }
        }

        [Test]
        public void NotAvailable_UntilTheHealthCheckSucceeds()
        {
            var r = new Rig();
            Assert.IsFalse(r.State.Available, "never probed");
            r.State.Tick(0);
            Assert.AreEqual(1, r.Probes.Count);
            Assert.IsFalse(r.State.Available, "probe pending");
            r.Probes[0](true);
            Assert.IsTrue(r.State.Available);
            r.On = false;
            Assert.IsFalse(r.State.Available, "voice turned off");
        }

        [Test]
        public void ReprobesPeriodically_AndAFailureMakesItUnavailable()
        {
            var r = new Rig();
            r.State.Tick(0);
            r.Probes[0](true);
            r.State.Tick(VoiceTutorialState.ProbeEverySeconds * 0.5);
            Assert.AreEqual(1, r.Probes.Count, "not every frame");
            r.State.Tick(VoiceTutorialState.ProbeEverySeconds + 0.1);
            Assert.AreEqual(2, r.Probes.Count);
            r.State.Tick(VoiceTutorialState.ProbeEverySeconds + 0.2);
            Assert.AreEqual(2, r.Probes.Count, "one probe in flight at a time");
            r.Probes[1](false);
            Assert.IsFalse(r.State.Available, "proxy went away");
        }

        [Test]
        public void DoesNotProbe_WhileVoiceIsOff()
        {
            var r = new Rig { On = false };
            r.State.Tick(0);
            r.State.Tick(100);
            Assert.AreEqual(0, r.Probes.Count);
        }
    }
}
