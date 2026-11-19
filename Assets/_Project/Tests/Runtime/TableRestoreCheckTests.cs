using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    /// <summary>
    /// 錨點放回的桌子要「在玩家面前、搆得到、高度像桌子」才用（2026-10-15 實機：轉了方向或換了房間，校準過的桌子留在原地改不了）。
    /// 不合理就這次不用（走放置流程＋顯示雙手提示）。
    /// </summary>
    public class TableRestoreCheckTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);

        /// <summary>玩家側桌緣在頭的水平方向 angle 度（正＝右）、距離 dist、桌面低於眼睛 below；牌桌 +Z 背對玩家（facing＝額外的 yaw）。</summary>
        static Pose Table(float angle, float dist, float below = 0.45f, float facing = 0f)
        {
            var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            var origin = Head + dir * dist;
            origin.y = Head.y - below;
            return new Pose(origin, Quaternion.LookRotation(Quaternion.Euler(0f, facing, 0f) * dir, Vector3.up));
        }

        static string Check(Pose p, Vector3? forward = null) => TableRestoreCheck.Reject(p, Head, forward ?? Vector3.forward);

        [Test]
        public void Table_in_front_within_reach_at_desk_height_is_kept()
        {
            Assert.IsNull(Check(Table(0f, 0.3f)));
            Assert.IsNull(Check(Table(-40f, 0.5f)));
            Assert.IsNull(Check(Table(0f, 0.3f), new Vector3(0.3f, -0.8f, 1f)), "looking down at the table");
        }

        [TestCase(30f, true)]
        [TestCase(-55f, true)]
        [TestCase(65f, false)]
        [TestCase(-90f, false)]
        [TestCase(180f, false)]
        public void Table_must_be_within_60_degrees_of_where_the_player_looks(float angle, bool kept)
        {
            var why = Check(Table(angle, 0.5f));
            Assert.That(why == null, Is.EqualTo(kept), why);
            if (!kept) StringAssert.Contains("angle=", why);
        }

        [TestCase(1.4f, true)]
        [TestCase(1.6f, false)]
        [TestCase(4.0f, false)]
        public void Player_edge_must_be_within_1_5_m(float dist, bool kept)
        {
            var why = Check(Table(0f, dist));
            Assert.That(why == null, Is.EqualTo(kept), why);
            if (!kept) StringAssert.Contains("dist=", why);
        }

        [TestCase(0.85f, true)]
        [TestCase(0.95f, false)]   // 地板上
        [TestCase(0.20f, true)]
        [TestCase(0.10f, false)]   // 比眼睛低不到 15 cm
        [TestCase(-0.2f, false)]   // 在頭上
        public void Surface_height_must_be_plausible_relative_to_the_eyes(float below, bool kept)
        {
            var why = Check(Table(0f, 0.4f, below));
            Assert.That(why == null, Is.EqualTo(kept), why);
            if (!kept) StringAssert.Contains("below eye=", why);
        }

        [Test]
        public void Edge_right_under_the_head_uses_the_table_center_for_the_direction()
        {
            // 桌緣幾乎就在頭的正下方：方向用桌面中心判斷
            Assert.IsNull(Check(Table(170f, 0.05f, facing: 180f)), "felt extends in front of the player");
            var why = Check(Table(0f, 0.05f, facing: 180f));
            Assert.IsNotNull(why, "felt extends behind the player");
            StringAssert.Contains("angle=", why);
        }
    }
}
