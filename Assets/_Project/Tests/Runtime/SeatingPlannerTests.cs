using System.Collections.Generic;
using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    /// <summary>
    /// 依真實房間（牆、家具）調整 4 個 AI＋荷官的座位：被擋住就往空的地方挪或擠一點，而且一律在玩家視線 ±60° 內。
    /// 2026-11-02 起是真實尺寸的長圓桌（絨布 1.89×0.83 m，兩端半圓），預設座位見 TableSeats／RealTableSeatingTests。
    /// 座標：牌桌座標的 XZ（原點在玩家側桌緣中央，+Z 指向牌桌中央）。
    /// </summary>
    public class SeatingPlannerTests
    {
        static readonly Vector2 Felt = new Vector2(TableDimensions.Width, TableDimensions.Depth);
        static readonly Vector3 Head = new Vector3(0f, 0.45f, -0.3f);
        static float HW => Felt.x * 0.5f;
        static float D => Felt.y;
        static float R => D * 0.5f;
        static float Cx => HW - R;

        /// <summary>半圓上的桌緣點：side −1 左端、+1 右端；角度從遠端 +Z 量起往外轉。</summary>
        static Vector3 Arc(int side, float deg) =>
            new Vector3(side * (Cx + R * Mathf.Sin(deg * Mathf.Deg2Rad)), 0f, R + R * Mathf.Cos(deg * Mathf.Deg2Rad));

        /// <summary>和 TableSeats 一樣的預設座位：左端 Pip、荷官左邊 Rocco、荷官（遠端中央）、荷官右邊老手、右端 Vex。</summary>
        static List<Pose> Defaults()
        {
            var list = new List<Pose>();
            foreach (var p in new[] { Arc(-1, 80f), Arc(-1, 16f), new Vector3(0f, 0f, D), Arc(1, 16f), Arc(1, 80f) })
                list.Add(SeatingPlanner.PoseAt(Felt, SeatingPlanner.ParamOf(Felt, p)));
            return list;
        }

        /// <summary>大房間：玩家與牌桌都在中間，四面牆離很遠。</summary>
        static RoomObstacles BigRoom()
        {
            var r = new RoomObstacles();
            r.SetOutline(Rect(-3f, -3f, 3f, 4f));
            return r;
        }

        static Vector2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };

        static RoomObstacles RoomWithWalls(float minX, float minZ, float maxX, float maxZ)
        {
            var r = new RoomObstacles();
            var o = Rect(minX, minZ, maxX, maxZ);
            r.SetOutline(o);
            for (int i = 0; i < 4; i++) r.AddWall(o[i], o[(i + 1) % 4]);
            return r;
        }

        static void AssertAllFreeAndVisible(SeatingPlan plan, RoomObstacles room)
        {
            foreach (var seat in plan.Seats)
            {
                var body = SeatingPlanner.BodyOf(seat);
                Assert.That(room.BlockedBy(new Vector2(body.x, body.z), SeatingPlanner.BodyRadius, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.Null,
                    $"座位 {seat.position} 的身體 {body} 撞到東西：{plan.Describe()}");
                Assert.That(SeatingPlanner.InView(Head, body), Is.True, $"座位 {seat.position} 在玩家視線 ±60° 外：{plan.Describe()}");
            }
        }

        static void AssertOrderedAndSpaced(SeatingPlan plan, float minSpacing)
        {
            for (int i = 1; i < plan.Seats.Length; i++)
            {
                float a = SeatingPlanner.ParamOf(Felt, plan.Seats[i - 1].position), b = SeatingPlanner.ParamOf(Felt, plan.Seats[i].position);
                Assert.That(b, Is.GreaterThan(a), "座位順序（左 → 對面 → 右）不能變，行動順序才會和看到的一致");
                float gap = Vector3.Distance(SeatingPlanner.BodyOf(plan.Seats[i - 1]), SeatingPlanner.BodyOf(plan.Seats[i]));
                Assert.That(gap, Is.GreaterThanOrEqualTo(minSpacing - 1e-4f), $"座位 {i - 1} 與 {i} 太擠：{plan.Describe()}");
            }
        }

        [Test]
        public void Default_seats_are_on_the_perimeter_and_round_trip()
        {
            var d = Defaults();
            var want = new[] { Arc(-1, 80f), Arc(-1, 16f), new Vector3(0f, 0f, D), Arc(1, 16f), Arc(1, 80f) };
            for (int i = 0; i < want.Length; i++) Assert.That(Vector3.Distance(d[i].position, want[i]), Is.LessThan(1e-4f), "seat " + i);
            Assert.That(Vector3.Angle(d[2].rotation * Vector3.forward, Vector3.back), Is.LessThan(0.01f), "遠端直邊的人（荷官）直接面向玩家側");
            var toArcCenter = new Vector3(-Cx, 0f, R) - d[0].position;
            Assert.That(Vector3.Angle(d[0].rotation * Vector3.forward, toArcCenter), Is.LessThan(0.05f), "半圓上的人面向那個半圓的圓心");
            var toArcCenterR = new Vector3(Cx, 0f, R) - d[3].position;
            Assert.That(Vector3.Angle(d[3].rotation * Vector3.forward, toArcCenterR), Is.LessThan(0.05f));
            for (int i = 1; i < d.Count; i++)
                Assert.That(SeatingPlanner.ParamOf(Felt, d[i].position), Is.GreaterThan(SeatingPlanner.ParamOf(Felt, d[i - 1].position)), "左 → 遠端 → 右");
        }

        [Test]
        public void Path_covers_both_round_ends_and_the_far_side_but_not_the_players_side()
        {
            float len = SeatingPlanner.ParamOf(Felt, new Vector3(HW + 1f, 0f, -1f));   // 夾到右端的盡頭
            Assert.That(SeatingPlanner.PoseAt(Felt, 0f).position.z, Is.EqualTo(SeatingPlanner.SideMinZ).Within(1e-3f), "左端最靠近玩家的座位");
            Assert.That(SeatingPlanner.PoseAt(Felt, len).position.z, Is.EqualTo(SeatingPlanner.SideMinZ).Within(1e-3f), "右端也是");
            for (float s = 0f; s <= len; s += 0.05f)
            {
                var p = SeatingPlanner.PoseAt(Felt, s);
                float dx = Mathf.Max(0f, Mathf.Abs(p.position.x) - Cx), dz = p.position.z - R;
                Assert.That(Mathf.Sqrt(dx * dx + dz * dz), Is.EqualTo(R).Within(1e-4f), "on the round felt edge at s=" + s);
                Assert.That(p.position.z, Is.GreaterThanOrEqualTo(SeatingPlanner.SideMinZ - 1e-3f));
                Assert.That(SeatingPlanner.ParamOf(Felt, p.position), Is.EqualTo(s).Within(1e-3f), "round trip at s=" + s);
            }
        }

        [Test]
        public void No_scene_data_keeps_the_default_seats()
        {
            var plan = SeatingPlanner.Plan(Felt, Defaults(), null, Head);
            Assert.That(plan.Layout, Is.EqualTo("default"));
            Assert.That(plan.Describe(), Does.Contain("no scene"));
            for (int i = 0; i < 5; i++) Assert.That(Vector3.Distance(plan.Seats[i].position, Defaults()[i].position), Is.LessThan(1e-5f));
        }

        [Test]
        public void Open_room_keeps_the_default_seats_one_seat_width_apart()
        {
            var room = BigRoom();
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.Layout, Is.EqualTo("default"), plan.Describe());
            Assert.That(plan.BlockedSeats, Is.EqualTo(0));
            for (int i = 0; i < 5; i++) Assert.That(Vector3.Distance(plan.Seats[i].position, Defaults()[i].position), Is.LessThan(1e-5f));
            AssertAllFreeAndVisible(plan, room);
            AssertOrderedAndSpaced(plan, SeatingPlanner.Spacing);
        }

        [Test]
        public void Wall_close_behind_the_far_side_only_the_dealer_stays_stuck()
        {
            // 牆在遠端桌緣後 30 cm：遠端直邊的身體（桌緣後 26 cm）全卡進牆；荷官兩側的人往兩端半圓挪，只剩荷官沒地方去
            var room = RoomWithWalls(-3f, -3f, 3f, D + 0.3f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.Layout, Is.Not.EqualTo("default"));
            Assert.That(plan.BlockedSeats, Is.EqualTo(1), plan.Describe());
            Assert.That(plan.Describe(), Does.Contain("far"));
            foreach (var i in new[] { 0, 1, 3, 4 })
            {
                var body = SeatingPlanner.BodyOf(plan.Seats[i]);
                Assert.That(room.BlockedBy(new Vector2(body.x, body.z), SeatingPlanner.BodyRadius, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.Null, plan.Describe());
            }
            foreach (var s in plan.Seats) Assert.That(SeatingPlanner.InView(Head, SeatingPlanner.BodyOf(s)), Is.True, plan.Describe());
            AssertOrderedAndSpaced(plan, SeatingPlanner.CompressedSpacing);
        }

        [Test]
        public void Wall_on_the_left_moves_the_left_seats_toward_the_far_side()
        {
            var room = RoomWithWalls(-HW - 0.1f, -3f, 3f, 4f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.BlockedSeats, Is.EqualTo(0), plan.Describe());
            Assert.That(plan.Describe(), Does.Contain("left"));
            AssertAllFreeAndVisible(plan, room);
            AssertOrderedAndSpaced(plan, SeatingPlanner.CompressedSpacing);
            Assert.That(plan.Seats[0].position.x, Is.GreaterThan(Defaults()[0].position.x + 0.1f), "Pip 往右挪：" + plan.Describe());
        }

        [Test]
        public void Wall_on_the_right_moves_the_right_seats_toward_the_far_side()
        {
            var room = RoomWithWalls(-3f, -3f, HW + 0.1f, 4f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.BlockedSeats, Is.EqualTo(0), plan.Describe());
            AssertAllFreeAndVisible(plan, room);
            AssertOrderedAndSpaced(plan, SeatingPlanner.CompressedSpacing);
            Assert.That(plan.Seats[4].position.x, Is.LessThan(Defaults()[4].position.x - 0.1f), "Vex 往左挪：" + plan.Describe());
        }

        [Test]
        public void Wall_behind_the_player_changes_nothing()
        {
            var room = RoomWithWalls(-3f, -0.6f, 3f, 4f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.Layout, Is.EqualTo("default"), plan.Describe());
            Assert.That(plan.Describe(), Does.Contain("blocked: none"));
        }

        [Test]
        public void Narrow_room_squeezes_toward_the_far_side_and_blocks_as_few_as_possible()
        {
            // 走道型的小房間：左右牆離桌子兩端只有 25 cm，兩端的身體（扶手外）卡進牆；5 個人擠在剩下的桌緣，最多只有 1 個人卡住
            var room = RoomWithWalls(-HW - 0.25f, -2f, HW + 0.25f, 2f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.BlockedSeats, Is.LessThanOrEqualTo(1), plan.Describe());
            Assert.That(plan.Layout, Is.EqualTo("compressed"), plan.Describe());
            foreach (var s in plan.Seats) Assert.That(SeatingPlanner.InView(Head, SeatingPlanner.BodyOf(s)), Is.True, plan.Describe());
            AssertOrderedAndSpaced(plan, SeatingPlanner.CompressedSpacing);
        }

        [Test]
        public void Desk_in_a_corner_does_its_best_and_never_leaves_the_view()
        {
            // 牆在遠端後 30 cm 與左邊：只剩右端半圓，5 個人坐不下，被擋住的人數要最少，而且每個人都在視線內
            var room = RoomWithWalls(-HW - 0.1f, -3f, 3f, D + 0.3f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.BlockedSeats, Is.InRange(1, 3), plan.Describe());
            Assert.That(plan.Describe(), Does.Contain("blocked: "));
            foreach (var s in plan.Seats) Assert.That(SeatingPlanner.InView(Head, SeatingPlanner.BodyOf(s)), Is.True, plan.Describe());
            AssertOrderedAndSpaced(plan, SeatingPlanner.CompressedSpacing);
        }

        [Test]
        public void Tall_cabinet_behind_a_seat_blocks_it_but_a_low_bed_does_not()
        {
            // 櫃子（比桌面高）在遠端左後方：Rocco 那個位置要讓開
            var room = BigRoom();
            room.AddVolume(Rect(-0.9f, D + 0.1f, -0.4f, D + 0.6f), bottom: -0.75f, top: 0.6f, label: "STORAGE");
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, Head);
            Assert.That(plan.BlockedSeats, Is.EqualTo(0), plan.Describe());
            Assert.That(plan.Layout, Is.Not.EqualTo("default"));
            Assert.That(plan.Describe(), Does.Contain("STORAGE"));
            AssertAllFreeAndVisible(plan, room);

            // 床（頂面比桌面低 30 cm）在同一個地方：角色的身體在桌面以上，不算擋住
            var low = BigRoom();
            low.AddVolume(Rect(-0.9f, D + 0.1f, -0.4f, D + 0.6f), bottom: -0.75f, top: -0.3f, label: "BED");
            Assert.That(SeatingPlanner.Plan(Felt, Defaults(), low, Head).Layout, Is.EqualTo("default"));
        }

        [Test]
        public void The_desk_the_table_sits_on_is_not_an_obstacle()
        {
            // 真實的桌子（TABLE 量體）蓋住絨布中心：牌桌底下那張桌子要忽略
            var room = BigRoom();
            room.AddVolume(Rect(-0.8f, -0.1f, 0.8f, 1.2f), bottom: -0.75f, top: 0f, label: "TABLE", feltCenter: new Vector2(0f, D * 0.5f));
            room.AddVolume(Rect(-0.8f, -0.1f, 0.8f, 1.2f), bottom: -0.75f, top: 0.02f, label: "TABLE", feltCenter: new Vector2(0f, D * 0.5f));
            Assert.That(room.VolumeCount, Is.EqualTo(0));
            Assert.That(SeatingPlanner.Plan(Felt, Defaults(), room, Head).Layout, Is.EqualTo("default"));
        }

        [Test]
        public void Seat_outside_the_room_outline_counts_as_blocked_even_without_wall_planes()
        {
            var room = new RoomObstacles();
            room.SetOutline(Rect(-3f, -3f, 3f, D + 0.05f));   // 只有地板輪廓，沒有牆面
            Assert.That(room.BlockedBy(new Vector2(0f, D + SeatingPlanner.BodyBehind), SeatingPlanner.BodyRadius, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.EqualTo("outside"));
            Assert.That(room.BlockedBy(new Vector2(0f, 0.2f), SeatingPlanner.BodyRadius, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.Null);
        }

        [Test]
        public void InView_is_about_plus_minus_60_degrees_from_the_players_forward()
        {
            Assert.That(SeatingPlanner.InView(Head, Head + Quaternion.Euler(0f, 55f, 0f) * Vector3.forward), Is.True);
            Assert.That(SeatingPlanner.InView(Head, Head + Quaternion.Euler(0f, -55f, 0f) * Vector3.forward), Is.True);
            Assert.That(SeatingPlanner.InView(Head, Head + Quaternion.Euler(0f, 65f, 0f) * Vector3.forward), Is.False);
            Assert.That(SeatingPlanner.InView(Head, Head + Vector3.back), Is.False, "背後永遠看不到");
        }

        [Test]
        public void Never_plans_a_seat_outside_the_view_even_when_the_player_sits_close()
        {
            // 玩家貼著桌緣坐（頭在桌緣上方）：兩端靠近玩家的位置角度很大，要排除
            var head = new Vector3(0f, 0.45f, 0.05f);
            var room = RoomWithWalls(-3f, -3f, 3f, D + 0.3f);
            var plan = SeatingPlanner.Plan(Felt, Defaults(), room, head);
            foreach (var s in plan.Seats) Assert.That(SeatingPlanner.InView(head, SeatingPlanner.BodyOf(s)), Is.True, plan.Describe());
        }

        [Test]
        public void FromWorld_converts_walls_and_volumes_into_table_coordinates()
        {
            // 牌桌原點在世界 (1, 0.75, 2)、轉 90°（牌桌 +Z = 世界 +X）
            var pose = new Pose(new Vector3(1f, 0.75f, 2f), Quaternion.Euler(0f, 90f, 0f));
            Vector3 ToTable(Vector3 w) => Quaternion.Inverse(pose.rotation) * (w - pose.position);
            // 世界 x = 1.85 的牆＝牌桌 z = 0.85（桌子遠端後 15 cm）
            var walls = new List<(Vector3, Vector3)> { (new Vector3(1.85f, 0f, 0f), new Vector3(1.85f, 0f, 4f)) };
            var cube = new List<Vector3>();
            for (int i = 0; i < 8; i++) cube.Add(new Vector3(i % 2 == 0 ? 0.9f : 1.3f, (i / 2) % 2 == 0 ? 0f : 0.9f, i / 4 == 0 ? 2.6f : 3.0f));
            var room = RoomObstacles.FromWorld(null, walls, new[] { (cube as IReadOnlyList<Vector3>, "STORAGE") }, ToTable, new Vector2(0f, D * 0.5f));
            Assert.That(room.WallCount, Is.EqualTo(1));
            Assert.That(room.VolumeCount, Is.EqualTo(1));
            Assert.That(room.BlockedBy(new Vector2(0f, 0.8f), 0.1f, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.EqualTo("wall"));
            Assert.That(room.BlockedBy(new Vector2(0f, 0.6f), 0.1f, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.Null);
            // 量體在世界 z 2.6–3.0（牌桌 x = −(z−2) → −1.0…−0.6）、世界 x 0.9–1.3（牌桌 z −0.1…0.3），高度到桌面上 15 cm
            Assert.That(room.BlockedBy(new Vector2(-0.8f, 0.1f), 0.05f, SeatingPlanner.BodyBottom, SeatingPlanner.BodyTop), Is.EqualTo("STORAGE"));
        }
    }
}
