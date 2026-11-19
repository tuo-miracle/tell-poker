using System.Collections.Generic;
using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    public class TablePlacementTests
    {
        [Test]
        public void Fallback_IsInFrontOfPlayer_AndBelowEyes()
        {
            var head = new Vector3(0.5f, 1.2f, -0.3f);
            var forward = Quaternion.Euler(20f, 60f, 0f) * Vector3.forward;   // 低頭 20°、轉 60°
            var pose = TablePlacement.Fallback(head, forward);

            var flatFwd = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            // 2026-11-02 真實尺寸的桌子（深 0.83 m）：以玩家側桌緣為基準放，不是桌子中心（中心在 0.45 m 會讓桌緣跑到玩家身上）
            Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(pose.position - head, Vector3.up), flatFwd), Is.EqualTo(TablePlacement.FallbackEdgeForward).Within(1e-4f), "玩家側桌緣在頭前 30 cm");
            Assert.That(TablePlacement.FallbackEdgeForward, Is.EqualTo(-SeatingPlanner.DefaultHead.z).Within(1e-4f), "和座位規劃假設的頭（桌緣後 30 cm）一致");
            Assert.That(head.y - pose.position.y, Is.EqualTo(TablePlacement.FallbackBelowEye).Within(1e-4f));
            Assert.That(Vector3.Dot(pose.position - head, flatFwd), Is.GreaterThan(0f), "玩家側桌緣也要在玩家前方");
            Assert.That(Vector3.Angle(pose.rotation * Vector3.forward, flatFwd), Is.LessThan(0.01f), "+Z 朝向玩家前方（牌桌中央）");
            Assert.That(Vector3.Angle(pose.rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f), "+Y 向上");
        }

        [Test]
        public void Fallback_height_suits_seated_play_and_is_clamped_when_standing()
        {
            // 追蹤原點在地板（OVRManager FloorLevel）：頭的 y 就是眼睛離地高度
            float Top(float eye) => TablePlacement.Fallback(new Vector3(0f, eye, 0f), Vector3.forward).position.y;
            Assert.That(Top(1.2f), Is.EqualTo(1.2f - TablePlacement.FallbackBelowEye).Within(1e-4f), "坐著：眼睛下方 45 cm（約一般書桌 74 cm）");
            Assert.That(Top(1.2f), Is.InRange(0.70f, 0.80f));
            Assert.That(Top(1.65f), Is.EqualTo(TablePlacement.FallbackMaxHeight).Within(1e-4f), "站著也不要變成胸口高的桌子");
            Assert.That(Top(1.0f), Is.EqualTo(TablePlacement.FallbackMinHeight).Within(1e-4f), "坐沙發也不要低到要彎腰");
            // 追蹤原點不在地板（頭的高度不像離地高度）：只能相對眼睛
            Assert.That(Top(0.1f), Is.EqualTo(0.1f - TablePlacement.FallbackBelowEye).Within(1e-4f));
        }

        [Test]
        public void Fallback_LookingStraightDown_StillUsesAFiniteForward()
        {
            var pose = TablePlacement.Fallback(new Vector3(0f, 1.2f, 0f), Vector3.down);
            Assert.That(float.IsNaN(pose.position.x), Is.False);
            Assert.That(Vector3.Angle(pose.rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        }

        [Test]
        public void FromRect_OriginIsPlayerSideEdgeCenter_ZTowardCenter()
        {
            // 桌面 1.2 × 0.8，中心在 (0, 0.75, 1)，長邊沿 X；玩家在 -Z 那側
            var rect = new TableRect(new Vector3(0f, 0.75f, 1f), Vector3.up, Vector3.right, Vector3.forward, new Vector2(0.6f, 0.4f));
            var head = new Vector3(0.1f, 1.2f, 0.2f);
            var pose = TablePlacement.FromRect(rect, head);

            Assert.That(Vector3.Distance(pose.position, new Vector3(0f, 0.75f, 0.6f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        }

        [Test]
        public void FromRect_PlayerOnShortSide_PicksThatEdge()
        {
            var rect = new TableRect(new Vector3(0f, 0.75f, 1f), Vector3.up, Vector3.right, Vector3.forward, new Vector2(0.6f, 0.4f));
            var head = new Vector3(1.2f, 1.2f, 1.05f);   // 坐在 +X 那一端
            var pose = TablePlacement.FromRect(rect, head);

            Assert.That(Vector3.Distance(pose.position, new Vector3(0.6f, 0.75f, 1f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.forward, Vector3.left), Is.LessThan(0.01f));
        }

        [Test]
        public void FromRect_DownwardNormal_IsFlippedUp()
        {
            var rect = new TableRect(new Vector3(0f, 0.75f, 1f), Vector3.down, Vector3.right, Vector3.forward, new Vector2(0.6f, 0.4f));
            var pose = TablePlacement.FromRect(rect, new Vector3(0f, 1.2f, 0f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        }

        [Test]
        public void PickTable_PrefersNearestInFront()
        {
            var head = Vector3.up * 1.2f;
            var fwd = Vector3.forward;
            var list = new List<TableRect>
            {
                new TableRect(new Vector3(0f, 0.7f, -0.6f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f), // 背後、最近
                new TableRect(new Vector3(0f, 0.7f, 2.0f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f),  // 前方、遠
                new TableRect(new Vector3(0.3f, 0.7f, 0.9f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f), // 前方、近
            };
            Assert.That(TablePlacement.PickTable(list, head, fwd), Is.EqualTo(2));
        }

        [Test]
        public void PickTable_NoneInFront_FallsBackToNearest_EmptyReturnsMinusOne()
        {
            var head = Vector3.up * 1.2f;
            var list = new List<TableRect>
            {
                new TableRect(new Vector3(0f, 0.7f, -2f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f),
                new TableRect(new Vector3(0f, 0.7f, -0.7f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f),
            };
            Assert.That(TablePlacement.PickTable(list, head, Vector3.forward), Is.EqualTo(1));
            Assert.That(TablePlacement.PickTable(new List<TableRect>(), head, Vector3.forward), Is.EqualTo(-1));
        }

        [Test]
        public void PickTable_FrontOnly_IgnoresDesksBehind()
        {
            var head = Vector3.up * 1.2f;
            var behind = new TableRect(new Vector3(0f, 0.7f, -0.7f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f);
            var front = new TableRect(new Vector3(0.2f, 0.7f, 1.5f), Vector3.up, Vector3.right, Vector3.forward, Vector2.one * 0.4f);
            Assert.That(TablePlacement.PickTable(new List<TableRect> { behind }, head, Vector3.forward, frontOnly: true), Is.EqualTo(-1));
            Assert.That(TablePlacement.PickTable(new List<TableRect> { behind, front }, head, Vector3.forward, frontOnly: true), Is.EqualTo(1));
        }

        [Test]
        public void FromSurfacePoint_UsesHitHeight_AndFacesPlayer()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            var hit = new Vector3(0.05f, 0.72f, 0.4f);
            var pose = TablePlacement.FromSurfacePoint(hit, Vector3.up, head, Vector3.forward);
            Assert.That(pose.position.y, Is.EqualTo(0.72f).Within(1e-4f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(pose.position.z, Is.EqualTo(TablePlacement.FallbackEdgeForward).Within(1e-4f), "與虛擬桌同樣的前後距離（桌緣在頭前 30 cm），只換高度");
        }
    }
}

namespace Poker.Tests
{
    /// <summary>2026-10-15 實機：長按重新置中，桌子跟著錨點留在原地，沒有到玩家面前。重新置中＝「把桌子放到我面前」。</summary>
    public class RecenterPlanTests
    {
        [NUnit.Framework.Test] public void RecenterAlwaysBringsTheTableInFront_EvenWhenCalibratedOrRestored()
        {
            foreach (Poker.Runtime.TablePlacementSource src in System.Enum.GetValues(typeof(Poker.Runtime.TablePlacementSource)))
            {
                var p = Poker.Runtime.RecenterPlan.For(src);
                NUnit.Framework.Assert.IsTrue(p.PlaceInFront, src + ": table comes in front of the player");
                NUnit.Framework.Assert.IsTrue(p.LookForDeskInFront, src + ": snap to a real desk in front if MRUK has one");
                NUnit.Framework.Assert.IsTrue(p.PromptCalibration, src + ": show the two-hands prompt so the player can align to their desk");
                NUnit.Framework.Assert.IsFalse(p.FollowOldAnchor, src + ": never stay at the old anchor");
            }
        }
    }
}
