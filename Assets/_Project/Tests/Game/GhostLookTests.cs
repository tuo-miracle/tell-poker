using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 示範手的外觀與字幕位置（2026-10-14 打磨）：
    /// 手指是圓管、手掌圓角、有法線（給邊緣光）、半透明材質有 rim；字幕貼著示範的那隻手，從座位看出去不蓋住牌、籌碼與手的動作。
    /// </summary>
    public class GhostLookTests
    {
        GameObject _table;
        GhostHands _ghost;

        [SetUp]
        public void SetUp()
        {
            _table = new GameObject("GhostLookTable");
            _table.AddComponent<TableFrame>().Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.EnvironmentRaycast);
            _ghost = new GhostHands(_table.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _ghost?.Dispose();
            Object.DestroyImmediate(_table);
        }

        static IEnumerable<GhostGesture> WithHands() => GhostCatalog.All.Where(g => GhostCatalog.ClipName(g) != null);

        /// <summary>手大部分時間所在的範圍（牌桌座標；GhostHands.Measure：中間 80% 的時間、所有關節的 10%–90% 分位）。</summary>
        static Bounds HandPath(GhostGesture g) => GhostHands.Measure(GhostCatalog.Load(g)).region;

        AngleRect CaptionRect()
        {
            var t = _ghost.Caption.Go.transform;
            var plate = _ghost.Caption.Plate.transform.localScale;
            return GhostCaptionPlacement.Caption(GhostCaptionPlacement.SeatEye, t.localPosition, plate.x * t.localScale.x, plate.y * t.localScale.y, _ghost.CaptionFlat);
        }

        [Test]
        public void Caption_SitsNextToTheDemonstratedHand_AndCoversNothing([Values(1f, 1.5f)] float textScale)
        {
            var eye = GhostCaptionPlacement.SeatEye;
            var obstacles = GhostCaptionPlacement.DefaultObstacles(TableDimensions.Layout);
            _ghost.CaptionScale = textScale;
            foreach (var g in WithHands())
            {
                _ghost.Hide();
                _ghost.Play(g);
                _ghost.Tick(0.3f, _table.transform.TransformPoint(eye));
                var cap = CaptionRect();
                var hand = GhostCaptionPlacement.Of(eye, HandPath(g));
                string at = $"{g} x{textScale}: caption {cap}{(_ghost.CaptionFlat ? " flat" : "")} hand {hand}";
                // 看起來挨著手（字放大時，放得下的位置在牌的另一邊，角度上會離遠一點；那時只看實際距離）
                if (textScale <= 1f) Assert.LessOrEqual(cap.Gap(hand), 4f, "next to the hand — " + at);
                var clip = GhostCatalog.Load(g);
                if (clip.Frames.Any(f => f.Left.Tracked) && clip.Frames.Any(f => f.Right.Tracked))
                {
                    // 雙手（All-in）：兩手掃過整個桌緣，貼桌的字幕一定會被手經過；只要求大部分時刻沒蓋到手
                    var samples = GhostHands.SampleHands(clip);
                    int covered = samples.Count(b => cap.Overlaps(GhostCaptionPlacement.Of(eye, b)));
                    Debug.Log($"[GhostCaption] {g} x{textScale}: covers the hands at {covered}/{samples.Length} sampled moments");
                    Assert.LessOrEqual(covered, samples.Length / 4, "mostly clear of the hands — " + at);
                }
                else
                    foreach (var each in GhostHands.MeasureHands(clip))
                        Assert.IsFalse(cap.Overlaps(GhostCaptionPlacement.Of(eye, each)), "does not cover the hand's motion — " + at);
                Assert.LessOrEqual(Mathf.Sqrt(HandPath(g).SqrDistance(_ghost.Caption.Go.transform.localPosition)), 0.10f * textScale + 1e-3f, "physically close to the hand (≤ 10 cm at text size 1), not deep in the table — " + at);
                Assert.LessOrEqual(_ghost.Caption.Go.transform.localPosition.y, 0.08f, "lies on the table — " + at);
                foreach (var o in obstacles)
                    Assert.IsFalse(cap.Overlaps(GhostCaptionPlacement.Of(eye, o)), $"covers {o.center} — {at}");
                Assert.LessOrEqual(cap.MaxPitch, 5f, "no looking up — " + at);
                Assert.GreaterOrEqual(cap.MinPitch, GhostCaptionPlacement.MinPitch, "not too low — " + at);
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(cap.MinYaw), Mathf.Abs(cap.MaxYaw)), GhostCaptionPlacement.MaxYaw, "inside ±35° — " + at);
                // 字至少 1.2° 高（坐著看得清楚；平躺的字算壓扁後的高度）
                float lineH = _ghost.Caption.LineHeight * textScale;
                float deg = GhostCaptionPlacement.LineDegrees(eye, _ghost.Caption.Go.transform.localPosition, lineH, _ghost.CaptionFlat);
                Assert.GreaterOrEqual(deg, 1.2f, "legible — " + at);
                Debug.Log($"[GhostCaption] {at} gap {cap.Gap(hand):0.0}° region {HandPath(g).min:F2}-{HandPath(g).max:F2} caption at {_ghost.Caption.Go.transform.localPosition:F2}");
            }
        }

        [Test]
        public void Placement_LiesOnTheTable_NextToTheHand_ClearOfTheSeats()
        {
            var eye = GhostCaptionPlacement.SeatEye;
            var zones = GhostCaptionPlacement.SeatZones(TableDimensions.Layout);
            // 桌面上一隻手（右邊，敲桌的位置），以及舉到面前的手（拿起來看）：字幕都貼桌、在手旁邊，不蓋住手
            var knock = new Bounds(new Vector3(0.3f, 0.04f, 0.24f), new Vector3(0.09f, 0.08f, 0.12f));
            var look = new Bounds(new Vector3(0.0f, 0.25f, 0.1f), new Vector3(0.12f, 0.4f, 0.15f));
            foreach (var hand in new[] { knock, look })
            {
                var p = GhostCaptionPlacement.Place(eye, hand, 0.14f, 0.04f, null, zones);
                var cap = GhostCaptionPlacement.Caption(eye, p, 0.14f, 0.04f);
                string at = $"hand {hand.center:F2} caption {p:F3} {cap}";
                Assert.LessOrEqual(p.y, 0.04f, "lies on the table — " + at);
                Assert.LessOrEqual(Mathf.Sqrt(hand.SqrDistance(p)), GhostCaptionPlacement.MaxDistance + 1e-3f, "next to the hand — " + at);
                Assert.IsFalse(cap.Overlaps(GhostCaptionPlacement.Of(eye, hand)), "does not cover the hand — " + at);
                Assert.AreEqual(0, GhostCaptionPlacement.SeatHits(p, 0.14f, 0.04f, zones), "no eye height sees it over a seat — " + at);
            }
            // 一個浮在牌桌中間偏高的字幕（舊的擺法）：低眼位看過去蓋到對面的座位
            Assert.Greater(GhostCaptionPlacement.SeatHits(new Vector3(0.17f, 0.20f, 0.21f), 0.2f, 0.04f, zones), 0, "the old spot is caught");
        }

        [Test]
        public void LongCaptions_WrapIntoTwoBalancedLines()
        {
            Assert.AreEqual("Knock twice to check", GhostHands.Wrap("Knock twice to check", 24));
            var w = GhostHands.Wrap("Cup your cards and lift your wrist to peek", 24);
            var lines = w.Split('\n');
            Assert.AreEqual(2, lines.Length, w);
            Assert.LessOrEqual(Mathf.Abs(lines[0].Length - lines[1].Length), 8, w);
            _ghost.Play(GhostGesture.Peek);
            Assert.AreEqual("Cup your cards and lift your wrist to peek", _ghost.CaptionText, "the text itself stays one sentence");
        }

        [Test]
        public void AngleRect_OverlapAndGap()
        {
            var a = new AngleRect { MinYaw = 0, MaxYaw = 2, MinPitch = 0, MaxPitch = 1 };
            var b = new AngleRect { MinYaw = 3, MaxYaw = 4, MinPitch = 0, MaxPitch = 1 };
            Assert.IsFalse(a.Overlaps(b));
            Assert.IsTrue(a.Overlaps(b, 1.5f));
            Assert.AreEqual(1f, a.Gap(b), 1e-5f);
            Assert.AreEqual(0f, a.Gap(a), 1e-5f);
        }

        [Test]
        public void Fingers_AreRoundTubes_WithNormals()
        {
            _ghost.Play(GhostGesture.Knock);
            for (int i = 0; i < 60; i++) _ghost.Tick(1f / 72f);
            var mesh = _ghost.Renderer.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices;
            var normals = mesh.normals;
            Assert.AreEqual(verts.Length, normals.Length, "normals for the rim light");
            Assert.IsTrue(_ghost.TryGetJointLocal(Hand.Right, HandJoint.IndexIntermediate, out var a));
            Assert.IsTrue(_ghost.TryGetJointLocal(Hand.Right, HandJoint.IndexDistal, out var b));
            var axis = (b - a).normalized;
            // 中節附近、離骨頭 1.5 cm 內的頂點，繞骨頭的方向要有 8 個以上（方塊只有 4 個）
            var refDir = Vector3.Cross(axis, Vector3.up).normalized;
            var angles = new HashSet<int>();
            for (int i = 0; i < verts.Length; i++)
            {
                var d = verts[i] - a;
                float along = Vector3.Dot(d, axis);
                if (along < -0.012f || along > (b - a).magnitude + 0.012f) continue;
                var radial = d - axis * along;
                if (radial.magnitude > 0.015f || radial.magnitude < 0.002f) continue;
                float ang = Vector3.SignedAngle(refDir, radial, axis);
                angles.Add(Mathf.RoundToInt((ang + 180f) / 15f));
                Assert.AreEqual(1f, normals[i].magnitude, 0.02f, "unit normals");
            }
            Assert.GreaterOrEqual(angles.Count, 8, "round cross-section around the finger bone");
        }

        [Test]
        public void Material_IsSoftTranslucentWithRim()
        {
            var m = _ghost.Renderer.sharedMaterial;
            Assert.AreEqual("Poker/GhostHand", m.shader.name);
            Assert.IsTrue(m.HasProperty("_RimColor") && m.HasProperty("_RimPower"));
            Assert.LessOrEqual(m.passCount, 2, "depth pre-pass + color: at most 2 draw calls for both hands");
            Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, m.renderQueue);
        }

        [Test]
        public void LoopEnds_FadeSmoothly_NoPop()
        {
            _ghost.Play(GhostGesture.Bet);
            float prev = 0f, maxStep = 0f, minNearWrap = 1f;
            string stepAt = "";
            double dur = 0;
            for (int i = 0; i < 72 * 8; i++)
            {
                _ghost.Tick(1f / 72f);
                dur = _ghost.ClipDuration;
                if (Mathf.Abs(_ghost.Alpha - prev) > maxStep) { maxStep = Mathf.Abs(_ghost.Alpha - prev); stepAt = $"frame {i} t={_ghost.PlaybackTime:0.000}/{_ghost.ClipDuration:0.000} {prev:0.000}->{_ghost.Alpha:0.000}"; }
                prev = _ghost.Alpha;
                if (_ghost.LoopsCompleted >= 1 && (_ghost.PlaybackTime < 0.02 || _ghost.PlaybackTime > dur - 0.02)) minNearWrap = Mathf.Min(minNearWrap, _ghost.Alpha);
            }
            Assert.Less(minNearWrap, 0.03f, "invisible at the loop seam");
            Assert.Less(maxStep, 0.05f, "no popping between frames: " + maxStep + " at " + stepAt);
        }
    }
}
