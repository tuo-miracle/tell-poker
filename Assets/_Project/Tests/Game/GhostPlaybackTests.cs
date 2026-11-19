using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>示範手（ghost hands）：從 Resources 讀烘焙好的錄製片段、在牌桌座標循環播放、字幕、淡入淡出、繪製成本。</summary>
    public class GhostPlaybackTests
    {
        GameObject _table;
        GhostHands _ghost;

        [SetUp]
        public void SetUp()
        {
            _table = new GameObject("GhostTable");
            var frame = _table.AddComponent<TableFrame>();
            // 牌桌放在一個轉過、移過的位置：示範手要跟著牌桌座標走
            frame.Place(new Pose(new Vector3(0.4f, 0.75f, 1.2f), Quaternion.Euler(0f, 35f, 0f)), TablePlacementSource.EnvironmentRaycast);
            _ghost = new GhostHands(_table.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _ghost?.Dispose();
            Object.DestroyImmediate(_table);
        }

        /// <summary>教學讀 Rocco 那一步的字幕（沒有手）放在桌面上方低處：不能擋住要讀的那張臉（2026-10-13 截圖：放在 Talk 的高度會蓋住 Rocco 的嘴）。</summary>
        [Test]
        public void GazeCaption_SitsLowOverTheTable_NotOverRoccosFace()
        {
            _ghost.Play(GhostGesture.Gaze);
            Assert.IsNull(GhostCatalog.Load(GhostGesture.Gaze));
            Assert.Less(_ghost.ClipCenter.y, 0.15f, "below the characters' faces");
            Assert.Less(_ghost.ClipCenter.z, 0.45f, "in front of the board, toward the player");
            _ghost.Play(GhostGesture.Talk);
            Assert.AreEqual(_ghost.TalkAnchor, _ghost.ClipCenter, "talk keeps its spot");
        }

        [Test]
        public void EveryGestureWithHands_HasABakedClip()
        {
            foreach (var g in GhostCatalog.All)
            {
                var clip = GhostCatalog.Load(g);
                if (g == GhostGesture.Talk) { Assert.IsNull(clip, "talking has no hands"); continue; }
                Assert.IsNotNull(clip, g + ": Resources/Ghosts/" + GhostCatalog.ClipName(g));
                Assert.Greater(clip.Frames.Count, 30, g.ToString());
                Assert.IsTrue(clip.Frames.Any(f => f.Left.Tracked || f.Right.Tracked), g.ToString());
                StringAssert.StartsWith("ghost " + GhostCatalog.ClipName(g), clip.Note);
            }
            Assert.AreSame(GhostCatalog.Load(GhostGesture.Knock), GhostCatalog.Load(GhostGesture.Knock), "loaded once, cached");
        }

        [Test]
        public void EveryGesture_HasAShortEnglishCaption_AndAMenuLabel()
        {
            Assert.AreEqual("Knock twice to check", GhostCatalog.Caption(GhostGesture.Knock));
            Assert.AreEqual("Pinch chips, lift for more", GhostCatalog.Caption(GhostGesture.PickChips));
            Assert.AreEqual("Toss into the bet zone", GhostCatalog.Caption(GhostGesture.Bet));
            Assert.AreEqual("Push your cards forward to fold", GhostCatalog.Caption(GhostGesture.Fold));
            Assert.AreEqual("Both hands push to go all-in", GhostCatalog.Caption(GhostGesture.AllIn));
            Assert.AreEqual("Cup your cards and lift your wrist to peek", GhostCatalog.Caption(GhostGesture.Peek));
            Assert.AreEqual("Pick up your cards to look", GhostCatalog.Caption(GhostGesture.LookAtCards));
            Assert.AreEqual("Look at a player and talk", GhostCatalog.Caption(GhostGesture.Talk));
            foreach (var g in GhostCatalog.All)
            {
                Assert.LessOrEqual(GhostCatalog.Caption(g).Split(' ').Length, 9, g.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(GhostCatalog.MenuLabel(g)), g.ToString());
                Assert.LessOrEqual(GhostCatalog.MenuLabel(g).Length, 9, "fits a third of the menu: " + GhostCatalog.MenuLabel(g));
            }
        }

        [Test]
        public void Play_LoopsTheClip()
        {
            _ghost.Play(GhostGesture.Knock);
            Assert.AreEqual(GhostGesture.Knock, _ghost.Current);
            double dur = _ghost.ClipDuration;
            Assert.Greater(dur, 1.0);
            for (float t = 0; t < dur * 2.5; t += 1f / 72f) _ghost.Tick(1f / 72f);
            Assert.AreEqual(2, _ghost.LoopsCompleted);
            Assert.Less(_ghost.PlaybackTime, dur + 0.05);
            Assert.IsTrue(_ghost.IsVisible);
        }

        [Test]
        public void Joints_AreDrawnInTableCoordinates()
        {
            _ghost.Play(GhostGesture.Knock);
            for (int i = 0; i < 60; i++) _ghost.Tick(1f / 72f);
            var clip = GhostCatalog.Load(GhostGesture.Knock);
            var sample = new HandsFrame();
            new HandRecordingPlayer(clip, true).Sample(_ghost.PlaybackTime, sample);
            Assert.IsTrue(_ghost.TryGetJointLocal(Hand.Right, HandJoint.Palm, out var local));
            var expected = sample.Right.Position(HandJoint.Palm);
            Assert.AreEqual(new Vector3(expected.X, expected.Y, expected.Z), local, "the sampled frame");
            Assert.IsFalse(_ghost.TryGetJointLocal(Hand.Left, HandJoint.Palm, out _), "the knock clip only shows the knocking hand");

            // 畫出來的網格在世界座標裡包住「牌桌座標 → 世界」換算後的手掌
            var table = _table.GetComponent<TableFrame>();
            var palmWorld = table.TableToWorld(local);
            var mesh = _ghost.Renderer.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices.Select(v => _ghost.Renderer.transform.TransformPoint(v)).ToArray();
            var near = verts.Where(v => (v - palmWorld).magnitude < 0.12f).Count();
            Assert.Greater(near, 20, "hand geometry around the palm at " + palmWorld);
        }

        [Test]
        public void Caption_LiesOnTheTable_AndFollowsTheTextSizeSetting()
        {
            _ghost.Play(GhostGesture.Fold);
            _ghost.Tick(0.5f);
            Assert.AreEqual("Push your cards forward to fold", _ghost.CaptionText);
            var cap = _ghost.Caption.Go.transform.localPosition;
            // 2026-10-14 實機：字幕改成貼著桌面（浮起來的字從低眼位看會蓋到對面的角色）
            Assert.Greater(_ghost.CaptionFlat ? cap.y : cap.y - _ghost.Caption.Plate.transform.localScale.y * 0.5f, 0f, "above the table surface");
            Assert.LessOrEqual(cap.y, 0.08f, "lies on the table");
            _ghost.CaptionScale = 1.5f;
            Assert.AreEqual(1.5f, _ghost.Caption.Go.transform.localScale.x, 1e-5f);

            _ghost.Play(GhostGesture.Talk);
            _ghost.Tick(0.5f);
            Assert.AreEqual("Look at a player and talk", _ghost.CaptionText);
            Assert.IsFalse(_ghost.Renderer.enabled, "talking: caption only");
        }

        [Test]
        public void Stop_FadesOut_Hide_IsImmediate()
        {
            _ghost.Play(GhostGesture.Bet);
            for (int i = 0; i < 40; i++) _ghost.Tick(1f / 72f);
            Assert.Greater(_ghost.Alpha, 0.2f);
            _ghost.Stop();
            _ghost.Tick(0.1f);
            Assert.IsTrue(_ghost.IsVisible, "still fading");
            for (int i = 0; i < 40; i++) _ghost.Tick(1f / 72f);
            Assert.IsFalse(_ghost.IsVisible);
            Assert.AreEqual("", _ghost.CaptionText);

            _ghost.Play(GhostGesture.Bet);
            _ghost.Tick(0.4f);
            _ghost.Hide();
            Assert.IsFalse(_ghost.IsVisible);
            Assert.IsFalse(_ghost.Renderer.enabled);
            Assert.IsNull(_ghost.Current);
        }

        [Test]
        public void IsCheap_OneRendererForBothHands_NoPerFrameAllocations()
        {
            _ghost.Play(GhostGesture.AllIn);
            for (int i = 0; i < 10; i++) _ghost.Tick(1f / 72f, Vector3.zero);
            var handRenderers = _ghost.Root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.GetComponent<TextMesh>() == null && r.name != "Plate").ToArray();
            Assert.AreEqual(1, handRenderers.Length, "both hands in one mesh = one draw call");
            // 圓管手指＋圓角手掌（2026-10-14 打磨）約 1,150 頂點：每幀只改頂點與法線，CPU 成本仍很小
            Assert.LessOrEqual(_ghost.Renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount, 1400);
            Assert.That(() => { for (int i = 0; i < 200; i++) _ghost.Tick(1f / 72f, new Vector3(0f, 1.2f, 0f)); },
                Is.Not.AllocatingGCMemory());
        }
    }
}
