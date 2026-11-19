using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 牌桌改成家用標準撲克桌（2026-11-02 使用者決定，docs/research/2026-11-02-real-poker-table-seating.md）：
    /// 外框 84×42 吋（2.13×1.07 m）長圓形，扶手 0.12 m → 絨布 1.89×0.83 m，兩端是半徑 = 半個桌深的半圓。
    /// 玩家這側的互動位置（底牌、籌碼、下注線）照舊在 60 cm 內。
    /// </summary>
    public class TableShapeTests
    {
        static readonly TableLayout L = new TableLayout();

        [Test]
        public void Felt_IsTheHomeTablePlayingSurface_84x42InchOuterMinusTheArmrest()
        {
            Assert.AreEqual(1.89f, L.FeltSize.X, 1e-4f);
            Assert.AreEqual(0.83f, L.FeltSize.Y, 1e-4f);
            Assert.AreEqual(L.FeltSize.Y * 0.5f, L.FeltEndRadius, 1e-6f, "stadium: the ends are half circles");
        }

        [Test]
        public void InFelt_IsAStadium_RoundEndsCutTheCorners()
        {
            float hw = L.FeltSize.X * 0.5f, d = L.FeltSize.Y;
            Assert.IsTrue(L.InFelt(new Vector3(0f, 0f, d * 0.5f)), "center");
            Assert.IsTrue(L.InFelt(new Vector3(0.5f, 0f, 0.01f)), "straight near edge");
            Assert.IsTrue(L.InFelt(new Vector3(-0.5f, 0f, d - 0.01f)), "straight far edge");
            Assert.IsTrue(L.InFelt(new Vector3(hw - 0.01f, 0f, d * 0.5f)), "tip of the right end");
            Assert.IsFalse(L.InFelt(new Vector3(hw - 0.02f, 0f, 0.02f)), "square corner is outside the round end");
            Assert.IsFalse(L.InFelt(new Vector3(-hw + 0.02f, 0f, d - 0.02f)), "far-left corner too");
            Assert.IsFalse(L.InFelt(new Vector3(0f, 0f, -0.01f)), "player side of the edge");
            Assert.IsFalse(L.InFelt(new Vector3(0f, 0f, d * 0.5f), d * 0.5f + 0.01f), "margin is an inset");
            Assert.IsTrue(L.InFelt(new Vector3(0.5f, 0f, 0.05f), 0.04f));
            Assert.IsFalse(L.InFelt(new Vector3(0.5f, 0f, 0.03f), 0.04f));
        }

        [Test]
        public void PlayerZones_StayWithinReach_AndOnTheFelt()
        {
            // 身體假設在桌緣後 0.3 m：牌、籌碼、下注線都要在手搆得到的 60 cm 內（水平距離，spec §3）
            foreach (var p in new[] { L.CardLeftCenter, L.CardRightCenter, L.ChipStackCenter, new Vector3(0f, 0f, L.BetLineZ) })
            {
                Assert.LessOrEqual(new Vector2(p.X, p.Z + 0.3f).Length(), 0.6f + 1e-4f, p.ToString());
                Assert.IsTrue(L.InFelt(p, 0.05f), p + " on the felt");
            }
            Assert.IsTrue(L.InFelt(L.MuckCenter, 0.05f), "muck on the felt");
            foreach (var c in new[] { new Vector3(L.BetZone.MinX, 0f, L.BetZone.MinZ), new Vector3(L.BetZone.MaxX, 0f, L.BetZone.MaxZ), L.PotCenter })
                Assert.IsTrue(L.InFelt(c), c + " bet zone / pot on the felt");
        }
    }
}
