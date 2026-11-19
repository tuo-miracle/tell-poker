using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 籌碼贏多了會往旁邊、往後多排幾柱（ChipStackMesh），離原本的籌碼堆中心超過捏起半徑。
    /// 遊戲層每幀把玩家自己籌碼柱的位置餵給辨識器，捏在任何一柱上都能拿；AI 籌碼、底池、下注區不行。
    /// </summary>
    public class ChipFootprintTests
    {
        const float ChipRadius = 0.02f;
        // 10 柱籌碼最後一排（ChipStackMesh 第 9 柱）：堆中心往左 4.3 cm、往後 11.2 cm
        static readonly Vector3 FarColumn = G.Stack + new Vector3(-0.043f, 0f, -0.112f);

        static List<Vector3> BigStack()
        {
            var cols = new List<Vector3>();
            float step = ChipRadius * 2f + 0.003f;
            for (int i = 0; i < 10; i++)
            {
                int row = i / 3, col = i % 3;
                cols.Add(G.Stack + new Vector3((col - 1) * step + (row % 2 == 1 ? step * 0.5f : 0f), 0f, -row * step * 0.87f));
            }
            return cols;
        }

        static SyntheticHands PinchAt(Vector3 tableXZ, float lift = 0.0f) =>
            GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift, tableXZ - G.Stack);

        static List<GestureEvent> Run(SyntheticHands s, IReadOnlyList<Vector3> footprint)
        {
            var r = new GestureRecognizer(G.Layout);
            if (footprint != null) r.SetPlayerChipFootprint(footprint, ChipRadius);
            var all = new List<GestureEvent>();
            foreach (var f in s.Frames) all.AddRange(r.Update(f, G.Ctx()));
            return all;
        }

        [Test]
        public void Default_zone_does_not_reach_a_column_far_behind_the_stack_center()
        {
            // 前提：不餵籌碼位置時維持舊行為（錄製回放用），遠處那柱捏不到
            var ev = Run(PinchAt(FarColumn), null);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void Pinching_any_of_the_players_chip_columns_picks()
        {
            var ev = Run(PinchAt(FarColumn), BigStack());
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Pinching_just_outside_a_column_within_2cm_still_picks_but_6cm_away_does_not()
        {
            var cols = new List<Vector3> { new Vector3(-0.10f, 0f, 0.05f) };
            var near = Run(PinchAt(cols[0] + new Vector3(-0.035f, 0f, 0f)), cols);
            Assert.That(near.Count(GestureKind.ChipsPicked), Is.EqualTo(1), near.Dump());
            var far = Run(PinchAt(cols[0] + new Vector3(-0.065f, 0f, 0f)), cols);
            Assert.That(far.Count(GestureKind.ChipsPicked), Is.EqualTo(0), far.Dump());
        }

        [Test]
        public void Lifting_on_a_far_column_counts_chips_like_the_main_stack()
        {
            var ev = Run(PinchAt(FarColumn, 0.08f), BigStack());
            var counts = ev.Where(e => e.Kind == GestureKind.ChipCountChanged).Select(e => e.Count).ToList();
            Assert.That(counts, Is.EqualTo(new[] { 2, 3, 4 }), ev.Dump());
        }

        [Test]
        public void The_original_stack_area_still_picks_when_a_footprint_is_fed()
        {
            var ev = Run(GestureScripts.PickChips(new SyntheticHands(), lift: 0.08f), new List<Vector3> { FarColumn });
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Ai_stack_pot_and_bet_zone_never_pick()
        {
            // 一對一時 Pip 的籌碼堆在對面 (−0.2, 0.62)；底池 (0, 0.57)；玩家下注 (0.07, 0.37)
            foreach (var p in new[] { new Vector3(-0.2f, 0f, 0.62f), new Vector3(0f, 0f, 0.57f), new Vector3(0.07f, 0f, 0.37f) })
            {
                var ev = Run(PinchAt(p), BigStack());
                Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(0), $"at {p}\n" + ev.Dump());
            }
        }

        [Test]
        public void A_footprint_column_inside_the_bet_zone_is_ignored()
        {
            // 防呆：遊戲層餵錯（例如把已下注的籌碼也算進來）時，下注區裡的籌碼仍然不能拿
            var bet = new Vector3(0.07f, 0f, 0.37f);
            var ev = Run(PinchAt(bet), new List<Vector3> { bet });
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void Clearing_the_footprint_restores_the_fixed_zone()
        {
            var r = new GestureRecognizer(G.Layout);
            r.SetPlayerChipFootprint(BigStack(), ChipRadius);
            r.ClearPlayerChipFootprint();
            var all = new List<GestureEvent>();
            foreach (var f in PinchAt(FarColumn).Frames) all.AddRange(r.Update(f, G.Ctx()));
            Assert.That(all.Count(GestureKind.ChipsPicked), Is.EqualTo(0), all.Dump());
        }
    }
}
