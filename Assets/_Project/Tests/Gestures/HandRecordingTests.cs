using System.IO;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class HandRecordingTests
    {
        static HandsFrame Frame(double t, float x, bool rightTracked)
        {
            var f = new HandsFrame { Time = t };
            f.Left.Tracked = true;
            f.Left.IndexPinch = 0.25f;
            f.Left.Confidence = 0.9f;
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                f.Left.Positions[j] = new Vector3(x + j * 0.01f, 0.05f, 0.1f);
                f.Left.Rotations[j] = Quaternion.CreateFromAxisAngle(Vector3.UnitY, j * 0.1f);
            }
            f.Right.Tracked = rightTracked;
            return f;
        }

        [Test]
        public void Round_trips_frames_exactly()
        {
            var ms = new MemoryStream();
            using (var w = new HandRecordingWriter(ms, "label: slam", leaveOpen: true))
            {
                w.Write(Frame(0.0, 0.1f, false));
                w.Write(Frame(0.0139, 0.2f, true));
            }
            ms.Position = 0;
            var rec = HandRecording.Read(ms);
            Assert.That(rec.Note, Is.EqualTo("label: slam"));
            Assert.That(rec.Frames.Count, Is.EqualTo(2));
            var f = rec.Frames[1];
            Assert.That(f.Time, Is.EqualTo(0.0139).Within(1e-9));
            Assert.That(f.Left.Tracked, Is.True);
            Assert.That(f.Right.Tracked, Is.True);
            Assert.That(f.Left.IndexPinch, Is.EqualTo(0.25f));
            Assert.That(f.Left.Confidence, Is.EqualTo(0.9f));
            Assert.That(f.Left.Position(HandJoint.LittleTip).X, Is.EqualTo(0.2f + 25 * 0.01f).Within(1e-6f));
            Assert.That(f.Left.Rotation(HandJoint.Wrist).Y, Is.EqualTo(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f).Y).Within(1e-6f));
        }

        [Test]
        public void Rejects_files_that_are_not_recordings()
        {
            Assert.Throws<InvalidDataException>(() => HandRecording.Read(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));
        }

        [Test]
        public void Writes_version_2_and_round_trips_the_system_pinching_flag()
        {
            var ms = new MemoryStream();
            var f = Frame(0.0, 0.1f, true);
            f.Left.IndexPinching = true;
            using (var w = new HandRecordingWriter(ms, "", leaveOpen: true)) w.Write(f);
            Assert.That(System.BitConverter.ToInt32(ms.ToArray(), 4), Is.EqualTo(2));
            ms.Position = 0;
            var rec = HandRecording.Read(ms);
            Assert.That(rec.Frames[0].Left.IndexPinching, Is.True);
            Assert.That(rec.Frames[0].Right.IndexPinching, Is.False);
            Assert.That(rec.Frames[0].Left.Tracked, Is.True);
            Assert.That(rec.Frames[0].Right.Tracked, Is.True);
        }

        /// <summary>依第 1 版格式手寫位元組（沒有 IndexPinching）：舊錄製必須照樣讀得到。</summary>
        static byte[] Version1Bytes()
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("HNDS"));
                w.Write(1);
                w.Write("v1 note");
                w.Write(0.5);
                for (int hand = 0; hand < 2; hand++)
                {
                    w.Write((byte)1);
                    w.Write(0.8f);
                    w.Write(hand == 0 ? 0.7f : 0.1f);
                    for (int j = 0; j < HandPose.JointCount; j++)
                    {
                        w.Write(0.01f * j); w.Write(0.02f); w.Write(0.03f);
                        w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f);
                    }
                }
            }
            return ms.ToArray();
        }

        [Test]
        public void Reads_version_1_files_without_the_pinching_flag()
        {
            var rec = HandRecording.Read(new MemoryStream(Version1Bytes()));
            Assert.That(rec.Note, Is.EqualTo("v1 note"));
            Assert.That(rec.Frames.Count, Is.EqualTo(1));
            var l = rec.Frames[0].Left;
            Assert.That(l.Tracked, Is.True);
            Assert.That(l.IndexPinch, Is.EqualTo(0.7f));
            Assert.That(l.IndexPinching, Is.False, "第 1 版沒有這個欄位，一律 false（辨識改用強度判斷）");
            Assert.That(l.Position(HandJoint.LittleTip).X, Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void Committed_quest_recordings_version_1_still_load()
        {
            var dir = RecordingCheck.FindRecordingsDir();
            var files = dir == null ? new string[0] : Directory.GetFiles(dir, "*.hands");
            if (files.Length == 0) Assert.Ignore("沒有錄製檔");
            foreach (var path in files)
            {
                var rec = HandRecording.Load(path);
                Assert.That(rec.Frames.Count, Is.GreaterThan(0), path);
            }
        }

        [Test]
        public void Clone_copies_the_pinching_flag()
        {
            var p = new HandPose { IndexPinching = true };
            Assert.That(p.Clone().IndexPinching, Is.True);
        }
    }
}
