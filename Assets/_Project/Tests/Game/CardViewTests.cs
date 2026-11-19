using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using UnityEngine;

namespace Poker.Tests.Game
{
    public class CardViewTests
    {
        GameObject _root;

        [SetUp] public void SetUp() => _root = new GameObject("root");
        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void PeekAmount_MapsLinearlyToCornerAngle_CappedAtMax()
        {
            var c = CardView.Create(_root.transform, new Card(12, Suit.Hearts));
            float max = c.MaxPeekDegrees;
            c.SetPeek(0f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, c.Flap.localRotation), 0.01f);
            c.SetPeek(0.5f);
            Assert.AreEqual(max * 0.5f, c.PeekDegrees, 0.01f);
            Assert.AreEqual(max * 0.5f, Quaternion.Angle(Quaternion.identity, c.Flap.localRotation), 0.05f);
            c.SetPeek(2f);
            Assert.AreEqual(max, Quaternion.Angle(Quaternion.identity, c.Flap.localRotation), 0.05f);
        }

        [Test]
        public void FullPeek_ShowsTheFaceCornerToASeatedPlayer()
        {
            // 坐著的眼睛：桌面上 42 cm、桌緣後 22 cm；牌在玩家牌位
            var c = CardView.Create(_root.transform, new Card(12, Suit.Hearts));
            c.transform.localPosition = new Vector3(0.036f, 0.0035f, 0.15f);
            var eye = new Vector3(0f, 0.42f, -0.22f);
            c.SetPeek(1f);
            var faceNormal = c.Flap.rotation * Vector3.down;            // 牌面朝下（本地 −Y）
            var flapCenter = c.Flap.TransformPoint(new Vector3(0f, 0f, -CardView.FlapDepth * 0.5f));
            float facing = Vector3.Dot(faceNormal, (eye - flapCenter).normalized);
            Assert.Greater(facing, 0.3f, $"face corner must turn toward the eye (max {c.MaxPeekDegrees} deg)");
            c.SetPeek(0f);
            faceNormal = c.Flap.rotation * Vector3.down;
            Assert.Less(Vector3.Dot(faceNormal, (eye - flapCenter).normalized), 0f, "flat card hides its face");
        }

        [Test]
        public void PeekLiftsTheNearCorner_Upward()
        {
            var c = CardView.Create(_root.transform, new Card(12, Suit.Hearts));
            var tipBefore = c.FlapTipLocal;
            c.SetPeek(1f);
            var tipAfter = c.FlapTipLocal;
            Assert.Greater(tipAfter.y, tipBefore.y + 0.005f, "corner goes up");
            Assert.Less(tipBefore.z, 0f, "flap is on the near (player) side");
        }

        [Test]
        public void FaceUp_FlipsTheCard_AndClearsPeek()
        {
            var c = CardView.Create(_root.transform, new Card(12, Suit.Hearts));
            c.SetPeek(1f);
            c.SetFaceUp(true);
            Assert.IsTrue(c.FaceUp);
            Assert.AreEqual(0f, c.PeekDegrees, 1e-3f);
            Assert.Greater(Vector3.Dot(c.transform.up, Vector3.down), 0.99f, "face up means the back side points down");
        }

        [Test]
        public void Renderers_DoNotCastShadows()
        {
            var c = CardView.Create(_root.transform, new Card(12, Suit.Hearts));
            foreach (var r in c.GetComponentsInChildren<Renderer>())
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode);
        }
    }
}
