using NUnit.Framework;
using Poker.Characters;
using UnityEngine;

namespace Poker.Tests.Characters
{
    /// <summary>字幕泡泡留在玩家的舒適視野內（水平 ±MaxYaw、不高過 MaxPitch），整個泡泡（含寬高）都算。</summary>
    public class BubblePlacementTests
    {
        static readonly Vector3 Viewer = new Vector3(0f, 0.45f, -0.3f);
        static readonly Vector3 Center = new Vector3(0f, 0f, 0.4f);

        static Vector2 Angles(Vector3 p)
        {
            var d = p - Viewer;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }

        [Test]
        public void InsideTheCone_IsLeftAlone()
        {
            var p = new Vector3(0.1f, 0.55f, 0.7f);
            Assert.AreEqual(p, BubblePlacement.Clamp(p, Viewer, Center, 0.08f, 0.04f));
        }

        [Test]
        public void FarToTheSide_IsSwungInward_KeepingDistanceAndHeight()
        {
            var p = new Vector3(0.75f, 0.6f, 0.5f);   // 右側座位的頭頂（約 43°）
            var q = BubblePlacement.Clamp(p, Viewer, Center, 0.08f, 0.04f);
            float dist = new Vector2(p.x - Viewer.x, p.z - Viewer.z).magnitude;
            Assert.AreEqual(dist, new Vector2(q.x - Viewer.x, q.z - Viewer.z).magnitude, 1e-4f, "same horizontal distance");
            Assert.AreEqual(p.y, q.y, 1e-4f);
            float halfW = Mathf.Atan2(0.08f, dist) * Mathf.Rad2Deg;
            Assert.AreEqual(BubblePlacement.MaxYaw - halfW, Angles(q).x, 0.05f, "outer edge exactly on the cone");
            var left = BubblePlacement.Clamp(new Vector3(-p.x, p.y, p.z), Viewer, Center, 0.08f, 0.04f);
            Assert.AreEqual(-q.x, left.x, 1e-4f, "mirror on the left");
        }

        [Test]
        public void TooHigh_IsLowered_SoTheTopEdgeStaysBelowMaxPitch()
        {
            var p = new Vector3(0f, 0.9f, 0.6f);
            var q = BubblePlacement.Clamp(p, Viewer, Center, 0.08f, 0.04f);
            var d = q - Viewer;
            float top = Angles(q).y + Mathf.Atan2(0.04f, d.magnitude) * Mathf.Rad2Deg;
            Assert.AreEqual(BubblePlacement.MaxPitch, top, 0.1f);
            Assert.AreEqual(p.x, q.x, 1e-5f);
            Assert.AreEqual(p.z, q.z, 1e-5f);
        }

        [Test]
        public void ForwardFollowsTheTable_NotWorldZ()
        {
            // 牌桌轉了 90°：中心在玩家的 +X 方向
            var viewer = Vector3.zero;
            var center = new Vector3(1f, -0.4f, 0f);
            var p = new Vector3(1f, 0.1f, 0.9f);    // 偏左約 42°
            var q = BubblePlacement.Clamp(p, viewer, center, 0.05f, 0.03f);
            float yaw = Vector3.SignedAngle(Vector3.right, new Vector3(q.x, 0f, q.z), Vector3.up);
            Assert.Less(Mathf.Abs(yaw), BubblePlacement.MaxYaw);
        }
    }
}
