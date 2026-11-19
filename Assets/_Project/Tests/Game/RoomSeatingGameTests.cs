using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 真實房間的座位：TablePlacer 把 MRUK 的牆與家具轉成牌桌座標交給 TableFrame.SetRoom，
    /// 遊戲在安全的時候（沒有動畫）把角色、牌、籌碼搬到不會卡進牆的位置。沒有房間資料時完全照舊。
    /// </summary>
    public class RoomSeatingGameTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static GameConfig Four() => new GameConfig { Seed = 5, FirstButton = 0, MonteCarloTrials = 40 };
        static float D => TableDimensions.Depth;
        /// <summary>牆在絨布遠端後 30 cm（扶手外 18 cm）：遠端直邊的身體（扶手外，桌緣後 26 cm）卡進牆，兩端還坐得下（真實尺寸的桌子，2026-11-02）。</summary>
        static float WallGap => 0.3f;

        static RoomObstacles WallInFront(float z)
        {
            var r = new RoomObstacles();
            var o = new[] { new Vector2(-3f, -3f), new Vector2(3f, -3f), new Vector2(3f, z), new Vector2(-3f, z) };
            r.SetOutline(o);
            for (int i = 0; i < 4; i++) r.AddWall(o[i], o[(i + 1) % 4]);
            return r;
        }

        List<CharacterPerformer> Everyone()
        {
            var g = _rig.Game;
            return new List<CharacterPerformer> { g.Character(1), g.Character(2), g.Character(3), g.Dealer };
        }

        [Test]
        public void Planner_defaults_match_the_existing_TableSeats()
        {
            var felt = TableDimensions.Layout.FeltSize;
            TableSeats.TableSize = new Vector2(felt.X, felt.Y);
            foreach (var k in new[] { CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Dealer, CharacterKind.Veteran, CharacterKind.Cold })
            {
                var seat = TableSeats.For(k);
                var planned = SeatingPlanner.PoseAt(new Vector2(felt.X, felt.Y), SeatingPlanner.ParamOf(new Vector2(felt.X, felt.Y), seat.position));
                Assert.That(Vector3.Distance(planned.position, seat.position), Is.LessThan(1e-4f), k.ToString());
                Assert.That(Quaternion.Angle(planned.rotation, seat.rotation), Is.LessThan(0.05f), k.ToString());
            }
        }

        [Test]
        public void Without_scene_data_the_seats_stay_where_they_were()
        {
            _rig = new GameTestRig(Four());
            var before = Everyone().ConvertAll(c => c.transform.localPosition);
            Assert.That(_rig.WaitPlayerTurn(), Is.True, _rig.Dump());
            _rig.Idle(1.0);
            var after = Everyone().ConvertAll(c => c.transform.localPosition);
            for (int i = 0; i < before.Count; i++) Assert.That(Vector3.Distance(before[i], after[i]), Is.LessThan(1e-5f));
        }

        [Test]
        public void Wall_behind_the_desk_moves_the_far_seats_and_their_cards_to_the_sides()
        {
            _rig = new GameTestRig(Four());
            var g = _rig.Game;
            Assert.That(_rig.WaitPlayerTurn(), Is.True, _rig.Dump());

            LogAssert.Expect(LogType.Log, new Regex(@"^\[Table\] seating (shifted|compressed) .*\(blocked: .*far"));
            var room = WallInFront(D + WallGap);
            _rig.Table.SetRoom(room);
            _rig.Idle(0.5);

            foreach (var c in Everyone())
            {
                var seat = new Pose(c.transform.localPosition, c.transform.localRotation);
                var body = SeatingPlanner.BodyOf(seat);
                Assert.That(room.BlockedBy(new Vector2(body.x, body.z), SeatingPlanner.BodyRadius, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.Null,
                    c.name + " still in the wall at " + seat.position);
                var head = c.Rig.Head.position - _rig.Camera.transform.position;
                Assert.That(Vector3.Angle(Vector3.forward, new Vector3(head.x, 0f, head.z)), Is.LessThan(60f), c.name + " stays in view");
            }
            // 牌、籌碼都跟著座位：AI 的底牌就在角色面前
            for (int seat = 1; seat <= 3; seat++)
            {
                var root = g.Character(seat).transform.localPosition;
                Assert.That(Vector3.Distance(g.Spots.CardOf(seat, 0), root), Is.LessThan(0.12f), "seat " + seat);
                var card = g.CardsOf(seat)[0];
                if (card.gameObject.activeSelf)
                {
                    // 還在手上的牌在牌位、已經棄的牌在棄牌處（都要跟著新座位）
                    var p = Flat(card.transform.localPosition);
                    float d = Mathf.Min(Vector3.Distance(p, Flat(g.Spots.CardOf(seat, 0))), Vector3.Distance(p, Flat(g.Spots.MuckOf(seat, 0))));
                    Assert.That(d, Is.LessThan(0.01f), "dealt card moved with seat " + seat);
                }
                var stack = g.Spots.StackOf(seat);
                Assert.That(TableDimensions.Layout.InFelt(new System.Numerics.Vector3(stack.x, 0f, stack.z)), Is.True, "stack on the felt, seat " + seat);
            }
            Assert.That(Vector3.Distance(Flat(g.Spots.Deck), Flat(g.Dealer.transform.localPosition)), Is.LessThan(0.15f), "deck in front of Dot");

            // 之後的牌局照常進行（下一手發到新位置）
            var aiCardsBefore = g.Spots.CardOf(2, 0);
            g.AutoPlayer = st => new Poker.Core.Fold(0);
            Assert.That(_rig.IdleUntil(() => g.Session.HandsStarted >= 2, 120), Is.True, _rig.Dump());
            g.AutoPlayer = null;
            Assert.That(_rig.WaitPlayerTurn(60), Is.True, _rig.Dump());
            var rocco = Flat(g.CardsOf(2)[0].transform.localPosition);
            Assert.That(Mathf.Min(Vector3.Distance(rocco, Flat(aiCardsBefore)), Vector3.Distance(rocco, Flat(g.Spots.MuckOf(2, 0)))), Is.LessThan(0.01f));
        }

        [Test]
        public void Tutorial_gaze_ring_follows_the_moved_seat()
        {
            _rig = new GameTestRig(Four());
            var g = _rig.Game;
            Assert.That(_rig.WaitPlayerTurn(), Is.True, _rig.Dump());
            _rig.Table.SetRoom(WallInFront(D + WallGap));
            _rig.Idle(0.3);
            var ring = g.Props.Root.Find("TutorialGaze/Ring");
            Assert.That(ring, Is.Not.Null);
            int seat = TutorialScript.ReadSeat;
            var cards = (g.Spots.CardOf(seat, 0) + g.Spots.CardOf(seat, 1)) * 0.5f;
            var local = g.Props.Root.InverseTransformPoint(ring.position);
            Assert.That(Vector3.Distance(Flat(local), Flat(cards)), Is.LessThan(0.01f), "教學「看 Rocco」的光圈要跟著他的新座位");
        }

        [Test]
        public void Same_room_again_does_not_reseat_or_log_twice()
        {
            _rig = new GameTestRig(Four());
            Assert.That(_rig.WaitPlayerTurn(), Is.True, _rig.Dump());
            int seatingLogs = 0;
            void Count(string msg, string stack, LogType type) { if (msg.StartsWith("[Table] seating ")) seatingLogs++; }
            Application.logMessageReceived += Count;
            try
            {
                _rig.Table.SetRoom(WallInFront(D + WallGap));
                _rig.Idle(0.3);
                var placed = Everyone().ConvertAll(c => c.transform.localPosition);
                _rig.Table.SetRoom(WallInFront(D + WallGap));
                _rig.Idle(0.3);
                Assert.That(seatingLogs, Is.EqualTo(1));
                var again = Everyone().ConvertAll(c => c.transform.localPosition);
                for (int i = 0; i < placed.Count; i++) Assert.That(Vector3.Distance(placed[i], again[i]), Is.LessThan(1e-5f));
            }
            finally { Application.logMessageReceived -= Count; }
        }

        [Test]
        public void Open_room_logs_the_default_layout_and_keeps_everyone()
        {
            _rig = new GameTestRig(Four());
            var before = Everyone().ConvertAll(c => c.transform.localPosition);
            Assert.That(_rig.WaitPlayerTurn(), Is.True, _rig.Dump());
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Table\] seating default \(blocked: none\)"));
            _rig.Table.SetRoom(WallInFront(4f));
            _rig.Idle(0.3);
            var after = Everyone().ConvertAll(c => c.transform.localPosition);
            for (int i = 0; i < before.Count; i++) Assert.That(Vector3.Distance(before[i], after[i]), Is.LessThan(1e-5f));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
