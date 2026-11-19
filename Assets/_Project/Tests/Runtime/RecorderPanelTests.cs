using System.Collections;
using System.IO;
using NUnit.Framework;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using NVector3 = System.Numerics.Vector3;

namespace Poker.Tests.Runtime
{
    public class RecorderPanelTests
    {
        static HandsFrame Frame(Vector3 rightTip)
        {
            var f = new HandsFrame();
            f.Right.Tracked = true;
            f.Right.Confidence = 1f;
            for (int j = 0; j < HandPose.JointCount; j++) f.Right.Rotations[j] = System.Numerics.Quaternion.Identity;
            f.Right.Positions[(int)HandJoint.IndexTip] = new NVector3(rightTip.x, rightTip.y, rightTip.z);
            return f;
        }

        [UnityTest]
        public IEnumerator FingerTapsRecordThenStop_WritesHandsFile()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "panel-" + System.Guid.NewGuid().ToString("N"));
            var root = new GameObject("TableRoot");
            try
            {
                var table = root.AddComponent<TableFrame>();
                table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.Virtual);
                var source = root.AddComponent<RecordingHandsSource>();
                var panel = new GameObject("Panel").AddComponent<RecorderPanel>();
                panel.transform.SetParent(root.transform, false);
                panel.Configure(table, source, dir);

                var rec = panel.RecordButtonCenter;
                var up = rec + Vector3.up * 0.06f;
                var down = rec + Vector3.up * 0.01f;
                var recording = new HandRecording();
                // 上方懸停 → 按下（開始）→ 抬起停一下 → 再按（停止）→ 抬起；每段 0.6 s，超過按鈕冷卻 0.5 s
                var path = new[] { up, down, up, up, down, up };
                for (int i = 0; i < path.Length * 6; i++)
                {
                    var f = Frame(path[i / 6]);
                    f.Time = i * 0.1;
                    recording.Frames.Add(f);
                }
                source.Play(recording, loop: false);

                float until = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < until && !(Directory.Exists(dir) && Directory.GetFiles(dir, "*.hands").Length > 0 && !panel.IsRecording))
                    yield return null;

                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.hands") : new string[0];
                Assert.That(files.Length, Is.EqualTo(1), "按 REC 再按 STOP 要產生一個檔");
                Assert.That(panel.IsRecording, Is.False);
                StringAssert.StartsWith(RecorderPanel.DefaultLabels[0] + "_", Path.GetFileName(files[0]));
                var back = HandRecording.Load(files[0]);
                Assert.That(back.Frames.Count, Is.GreaterThan(5));
            }
            finally
            {
                Object.Destroy(root);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [UnityTest]
        public IEnumerator TableMovedWhileRecording_StopsAndKeepsFile()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "panel-" + System.Guid.NewGuid().ToString("N"));
            var root = new GameObject("TableRoot");
            try
            {
                var table = root.AddComponent<TableFrame>();
                table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.Virtual);
                var source = root.AddComponent<RecordingHandsSource>();
                var panel = new GameObject("Panel").AddComponent<RecorderPanel>();
                panel.transform.SetParent(root.transform, false);
                panel.Configure(table, source, dir);

                var rec = panel.RecordButtonCenter;
                var recording = new HandRecording();
                var path = new[] { rec + Vector3.up * 0.06f, rec + Vector3.up * 0.01f, rec + Vector3.up * 0.06f };
                for (int i = 0; i < 18; i++)
                {
                    var f = Frame(path[System.Math.Min(i / 6, 2)]);
                    f.Time = i * 0.1;
                    recording.Frames.Add(f);
                }
                source.Play(recording, loop: false);

                float until = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < until && !panel.IsRecording) yield return null;
                Assert.That(panel.IsRecording, Is.True, "先開始錄製");
                yield return null;

                table.Place(new Pose(Vector3.one, Quaternion.identity), TablePlacementSource.MrukTable);
                Assert.That(panel.IsRecording, Is.False, "牌桌移動就結束錄製");
                Assert.That(Directory.GetFiles(dir, "*.hands").Length, Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(root);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
