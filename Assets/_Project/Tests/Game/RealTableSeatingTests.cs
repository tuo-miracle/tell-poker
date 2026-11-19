using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 真實撲克桌的座位（2026-11-02 使用者決定，docs/research/2026-11-02-real-poker-table-seating.md）：
    /// 家用 84×42 吋長圓桌；玩家在近端長邊中央、荷官在遠端長邊中央（缺口）面對面；荷官左右各一位（Rocco 座、新的老手座），
    /// 兩端半圓各一位（左 Pip 座、右 Vex 座），左右對稱；人與人的身體至少隔一個真實座位寬（0.7 m）。
    /// </summary>
    public class RealTableSeatingTests
    {
        static TableLayout Layout => TableDimensions.Layout;
        Vector2 _oldSize;

        [SetUp] public void SetUp() { _oldSize = TableSeats.TableSize; TableSeats.TableSize = new Vector2(Layout.FeltSize.X, Layout.FeltSize.Y); }
        [TearDown] public void TearDown() => TableSeats.TableSize = _oldSize;

        static Vector3 Fwd(Pose p) => p.rotation * Vector3.forward;
        static System.Numerics.Vector3 N(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
        static float R => Layout.FeltEndRadius;
        static float Cx => Layout.FeltSize.X * 0.5f - R;

        /// <summary>點離長圓形絨布邊的距離（外正內負）。</summary>
        static float EdgeDistance(Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(p.x) - Cx), dz = p.z - R;
            return Mathf.Sqrt(dx * dx + dz * dz) - R;
        }

        [Test]
        public void RailAndOuterSize_AreTheHomeTable_2_13x1_07()
        {
            Assert.AreEqual(TableDimensions.VirtualBorder, TableSeats.RailWidth, 1e-6f, "the seats know how wide the armrest is");
            Assert.AreEqual(2.13f, Layout.FeltSize.X + 2 * TableDimensions.VirtualBorder, 0.005f, "84 in");
            Assert.AreEqual(1.07f, Layout.FeltSize.Y + 2 * TableDimensions.VirtualBorder, 0.005f, "42 in");
        }

        [Test]
        public void Seats_PlayerFacesTheDealer_RoccoAndVeteranFlankTheDealer_PipAndVexOnTheEnds_Symmetric()
        {
            var pip = TableSeats.For(CharacterKind.Rookie);
            var rocco = TableSeats.For(CharacterKind.Loudmouth);
            var dealer = TableSeats.For(CharacterKind.Dealer);
            var vet = TableSeats.For(CharacterKind.Veteran);
            var vex = TableSeats.For(CharacterKind.Cold);

            Assert.Less(Vector3.Distance(dealer.position, new Vector3(0f, 0f, Layout.FeltSize.Y)), 1e-4f, "dealer at the middle of the far long side");
            Assert.Less(Vector3.Angle(Fwd(dealer), Vector3.back), 0.01f, "dealer faces the player");
            Assert.Less(rocco.position.x, -0.3f, "Rocco on the dealer's left (player's view)");
            Assert.Greater(vet.position.x, 0.3f, "new seat on the dealer's right");
            Assert.Less(pip.position.x, -Cx, "Pip on the left round end");
            Assert.Greater(vex.position.x, Cx, "Vex on the right round end");
            foreach (var (a, b) in new[] { (pip, vex), (rocco, vet) })
            {
                Assert.AreEqual(-a.position.x, b.position.x, 1e-4f, "mirror image");
                Assert.AreEqual(a.position.z, b.position.z, 1e-4f, "mirror image");
                Assert.AreEqual(-Fwd(a).x, Fwd(b).x, 1e-4f);
            }
            foreach (var p in new[] { pip, rocco, dealer, vet, vex })
            {
                Assert.AreEqual(0f, EdgeDistance(p.position), 1e-4f, p.position + " root on the felt edge");
                Assert.IsTrue(Layout.InFelt(N(p.position + Fwd(p) * 0.1f)), p.position + " faces into the table");
            }
            // 兩端的人面向自己那個半圓的圓心（坐在弧上的人自然的朝向）
            var arcCenter = new Vector3(-Cx, 0f, R);
            Assert.Less(Vector3.Angle(Fwd(pip), arcCenter - pip.position), 0.05f);
        }

        [Test]
        public void Bodies_AtLeastOneSeatWidthApart_AndBehindTheArmrest()
        {
            // 身體中心：玩家座位在扶手後（TableSeats.BodyBehind），荷官在缺口裡（TableSeats.DealerBodyBehind）
            Assert.GreaterOrEqual(TableSeats.BodyBehind, TableSeats.RailWidth + 0.12f, "torso clears the 12 cm armrest");
            var order = new[] { CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Dealer, CharacterKind.Veteran, CharacterKind.Cold };
            var bodies = new List<Vector3>();
            foreach (var k in order) bodies.Add(TableSeats.BodyOf(k));
            for (int i = 1; i < bodies.Count; i++)
                Assert.GreaterOrEqual(Vector3.Distance(bodies[i - 1], bodies[i]), 0.70f - 1e-3f, order[i - 1] + " vs " + order[i]);
            Assert.AreEqual(TableSeats.DealerBodyBehind, Vector3.Distance(bodies[2], TableSeats.For(CharacterKind.Dealer).position), 1e-4f);
        }

        [Test]
        public void EveryoneWithinASlightHeadTurn_FromThePlayersEye()
        {
            // 使用者 2026-11-02 接受兩端「稍微轉頭」：身體中心在 ±57° 內（Quest 3 水平視野約 ±55°），荷官兩側的人在 ±35° 內
            var eye = SeatingPlanner.DefaultHead;
            foreach (var k in new[] { CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Dealer, CharacterKind.Veteran, CharacterKind.Cold })
            {
                var b = TableSeats.BodyOf(k);
                float yaw = Mathf.Abs(Mathf.Atan2(b.x - eye.x, b.z - eye.z) * Mathf.Rad2Deg);
                Assert.LessOrEqual(yaw, k == CharacterKind.Rookie || k == CharacterKind.Cold ? 57f : 35f, k.ToString());
                Assert.IsTrue(SeatingPlanner.InView(eye, SeatingPlanner.BodyOf(TableSeats.For(k))), k + " inside the planner's view");
            }
        }

        [Test]
        public void FiveHandedSpots_StayOnTheFelt_OutOfThePlayersZone_AndApart()
        {
            var kinds = new[] { CharacterKind.Rookie, CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Veteran, CharacterKind.Cold };
            var s = new TableSpots(Layout, kinds);
            Assert.AreEqual(5, s.SeatCount);
            Assert.IsTrue(s.IsMultiway);
            for (int seat = 1; seat < 5; seat++)
            {
                Assert.Less(Vector3.Distance(s.SeatPose(seat).position, TableSeats.For(kinds[seat]).position), 1e-4f, "seat " + seat);
                foreach (var p in new[] { s.CardOf(seat, 0), s.CardOf(seat, 1), s.StackOf(seat), s.BetOf(seat), s.MuckOf(seat, 0) })
                {
                    Assert.IsTrue(Layout.InFelt(N(p), 0.02f), $"seat {seat} {p} on the felt");
                    Assert.IsFalse(Layout.PlayerZone.Contains(N(p), 0.02f), $"seat {seat} {p} out of the player's zone");
                    for (int b = 0; b < 5; b++) Assert.Greater(Vector3.Distance(p, s.Board(b)), 0.07f, $"seat {seat} clear of board card {b}");
                }
                for (int other = seat + 1; other < 5; other++)
                    Assert.Greater(Vector3.Distance(s.StackOf(seat), s.StackOf(other)), 0.25f, $"stacks {seat}/{other}");
            }
            Assert.IsTrue(Layout.InFelt(N(s.Deck), 0.02f), "deck on the felt");
            Assert.IsTrue(Layout.InFelt(N(s.PotPile), 0.1f), "pot in the middle");
        }

        [Test]
        public void HeadsUp_PipStraightAcross_DealerBesideHimOnTheFarSide()
        {
            // 一對一（M1 的配置，測試用）：Pip 正對面，荷官坐他右手邊的座位（老手座）
            var s = new TableSpots(Layout);
            Assert.AreEqual(0f, s.OpponentSeat.position.x, 1e-4f);
            Assert.AreEqual(Layout.FeltSize.Y, s.OpponentSeat.position.z, 1e-4f);
            Assert.Less(Vector3.Distance(s.DealerSeat.position, TableSeats.For(CharacterKind.Veteran).position), 1e-4f);
            Assert.IsTrue(Layout.InFelt(N(s.OpponentSeat.position + Fwd(s.OpponentSeat) * 0.1f)));
            Assert.IsTrue(Layout.InFelt(N(s.DealerSeat.position + Fwd(s.DealerSeat) * 0.1f)));
            Assert.AreEqual(0f, EdgeDistance(s.DealerSeat.position), 1e-4f, "heads-up dealer still sits at the table edge");
            // 一對一的荷官坐在有扶手的老手座（不是缺口），身體一樣在扶手外
            Assert.Greater(Vector3.Distance(TableSeats.BodyOf(CharacterKind.Rookie, s.OpponentSeat), TableSeats.BodyOf(CharacterKind.Veteran, s.DealerSeat)), 0.70f - 1e-3f);
            foreach (var p in new[] { s.AiCard(0), s.AiStack, s.AiBet, s.Deck }) Assert.IsTrue(Layout.InFelt(N(p), 0.02f), p.ToString());
        }

        [Test]
        public void Dealer_NamesEveryWinner_IncludingSilas_ButFallsBackForUnbakedNames()
        {
            // 每句話都要有聲音（使用者規則）：名字有預錄才唸名字，否則說已錄好的「Pot goes to the winner!」（2026-11-02 Silas 已錄）
            Assert.AreEqual(DealerLines.PotTo("Pip", false), DealerLines.WinnerLine("Pip", false));
            Assert.AreEqual(DealerLines.PotTo("Silas", true), DealerLines.WinnerLine("Silas", true));
            var generic = Poker.AI.LineBook.ForDealer(Poker.AI.LineTrigger.DealerWinner).First().Text;
            Assert.AreEqual(generic, DealerLines.WinnerLine("Nobody", false));
        }

        [Test]
        public void Archetypes_MapToSeats_InSeatOrder()
        {
            // GameConfig.Opponents 依序坐 1、2、3、4 號座位（玩家左手邊起順時針）＝ Pip、Rocco、老手、Vex
            CollectionAssert.AreEqual(new[] { Archetype.Rookie, Archetype.Loudmouth, Archetype.Veteran, Archetype.Cold }, GameConfig.FiveHanded);
            Assert.AreEqual(CharacterKind.Veteran, GameController.KindOf(Archetype.Veteran));
            Assert.AreEqual(CharacterKind.Cold, GameController.KindOf(Archetype.Cold));
            var spec = CharacterSpec.For(CharacterKind.Veteran);
            Assert.AreEqual(CharacterKind.Veteran, spec.Kind, "never falls back to the dealer");
            Assert.AreEqual("Silas", spec.DisplayName);
        }
    }
}
