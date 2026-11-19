using System.Collections;
using System.IO;
using NUnit.Framework;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Poker.Tests.Runtime
{
    public class RecordingTests
    {
        static HandRecording MakeRecording(int frames, double dt)
        {
            var rec = new HandRecording { Note = "label=test" };
            for (int i = 0; i < frames; i++)
            {
                var f = new HandsFrame { Time = 10.0 + i * dt };
                f.Left.Tracked = i % 2 == 0;
                f.Left.Confidence = 0.5f;
                f.Right.Tracked = true;
                f.Right.IndexPinch = i / (float)frames;
                for (int j = 0; j < HandPose.JointCount; j++)
                {
                    f.Right.Positions[j] = new NVector3(i, j, 0.01f * i);
                    f.Right.Rotations[j] = NQuaternion.CreateFromYawPitchRoll(0.1f * i, 0f, 0f);
                }
                rec.Frames.Add(f);
            }
            return rec;
        }

        [Test]
        public void Player_SamplesLatestFrameAtOrBeforeTime()
        {
            var player = new HandRecordingPlayer(MakeRecording(5, 0.1), loop: false);
            var frame = new HandsFrame();
            player.Sample(0.25, frame);
            Assert.That(frame.Right.Positions[3].X, Is.EqualTo(2f));   // 第 2 幀（t=0.2）
            Assert.That(frame.Left.Tracked, Is.True);
            Assert.That(frame.Right.IndexPinch, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(frame.Time, Is.EqualTo(0.2).Within(1e-9));
        }

        [Test]
        public void Player_Loops_AndKeepsTimeMonotonic()
        {
            var player = new HandRecordingPlayer(MakeRecording(4, 0.1), loop: true);   // 長度 0.3 s，循環週期 0.4 s
            var a = new HandsFrame();
            var b = new HandsFrame();
            player.Sample(0.35, a);
            player.Sample(0.45, b);
            Assert.That(b.Right.Positions[0].X, Is.EqualTo(0f), "繞回第一幀");
            Assert.That(b.Time, Is.GreaterThan(a.Time));
        }

        [Test]
        public void Player_NoLoop_HoldsLastFrame_AndReportsFinished()
        {
            var player = new HandRecordingPlayer(MakeRecording(3, 0.1), loop: false);
            var f = new HandsFrame();
            player.Sample(5.0, f);
            Assert.That(f.Right.Positions[0].X, Is.EqualTo(2f));
            Assert.That(player.IsFinished(5.0), Is.True);
        }

        [UnityTest]
        public IEnumerator RecordingSource_DrivesHandsFrame()
        {
            var go = new GameObject("Playback");
            try
            {
                var source = go.AddComponent<RecordingHandsSource>();
                var rec = MakeRecording(30, 0.01);
                int updates = 0;
                HandsFrame last = null;
                source.FrameUpdated += f => { updates++; last = f; };
                source.Play(rec, loop: true);
                yield return null;
                yield return null;
                Assert.That(updates, Is.GreaterThanOrEqualTo(1));
                Assert.That(last, Is.SameAs(source.Current));
                Assert.That(last.Right.Tracked, Is.True);
                // 位置格式：(幀號, 關節號, 0.01 × 幀號)，驗證是錄製檔裡某一幀的完整資料
                int i = (int)last.Right.Positions[0].X;
                Assert.That(last.Right.Positions[25], Is.EqualTo(new NVector3(i, 25, 0.01f * i)));
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [Test]
        public void Session_WritesReadableHandsFile()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "rig-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var session = new RecordingSession(dir);
                string path = session.Start("pick_bet", new System.DateTime(2026, 10, 12, 9, 5, 7), "dev");
                Assert.That(Path.GetFileName(path), Is.EqualTo("pick_bet_20261012_090507.hands"));
                Assert.That(session.IsRecording, Is.True);
                foreach (var f in MakeRecording(6, 0.02).Frames) session.Write(f);
                Assert.That(session.FrameCount, Is.EqualTo(6));
                session.Stop();
                Assert.That(session.IsRecording, Is.False);

                var back = HandRecording.Load(path);
                Assert.That(back.Frames.Count, Is.EqualTo(6));
                StringAssert.Contains("label=pick_bet", back.Note);
                Assert.That(back.Frames[5].Right.Positions[7], Is.EqualTo(new NVector3(5, 7, 0.01f * 5)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Session_StopWithoutStart_IsSafe_AndWriteIgnoredWhenIdle()
        {
            var session = new RecordingSession(Path.Combine(Application.temporaryCachePath, "unused"));
            Assert.DoesNotThrow(() => session.Stop());
            Assert.DoesNotThrow(() => session.Write(new HandsFrame()));
            Assert.That(session.FrameCount, Is.EqualTo(0));
        }
    }
}
