using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-13 實機回報 All-in 不靈敏：玩家準備時雙手在桌緣（手掌 z≈0.07），要推約 23 cm 才到下注線、再等 1 秒。
    /// 改成：雙手一起從準備位置往前推夠遠（AllInConfirmPushDistance）也開始倒數（越線仍可），倒數縮短；
    /// 準備時不小心捏起的 1–2 枚籌碼（沒抬起）在雙手一起起推時放回籌碼堆，不擋 All-in。
    /// </summary>
    public class AllInDistanceTests
    {
        static bool AnyAllIn(System.Collections.Generic.List<GestureEvent> ev) =>
            ev.Any(e => e.Kind == GestureKind.AllInPushStarted || e.Kind == GestureKind.AllInConfirmed);

        [Test]
        public void Pushing_13cm_from_the_table_edge_without_reaching_the_bet_line_confirms()
        {
            // 手掌 0.04 → 0.17，中指指尖停在 z≈0.27（下注線 0.30 前）
            var ev = G.Run(GestureScripts.AllInFromEdge(new SyntheticHands(seed: 3).WithNoise(0.01f), startZ: 0.04f, distance: 0.13f));
            Assert.That(ev.Count(GestureKind.AllInPushStarted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Where(e => e.IsAction).All(e => e.Kind == GestureKind.AllInPushStarted || e.Kind == GestureKind.AllInConfirmed), ev.Dump());
        }

        [Test]
        public void Edge_push_confirms_about_a_second_after_the_push_starts()
        {
            var ev = G.Run(GestureScripts.AllInFromEdge(new SyntheticHands(seed: 5).WithNoise(0.01f), startZ: 0.04f, distance: 0.14f, duration: 0.45));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
            double latency = ev.First(GestureKind.AllInConfirmed).Time - GestureScripts.EdgePushStart;
            Assert.That(latency, Is.LessThanOrEqualTo(1.0), ev.Dump());
        }

        [Test]
        public void Confirm_delay_is_half_a_second() =>
            Assert.That(new GestureTuning().AllInConfirmDelay, Is.EqualTo(0.5f).Within(0.05f));

        [Test]
        public void Pulling_back_right_after_an_edge_push_still_cancels()
        {
            var s = GestureScripts.AllInFromEdge(new SyntheticHands(seed: 9).WithNoise(0.01f), startZ: 0.04f, distance: 0.14f, duration: 0.4, holdAfter: 0.0);
            // 推完立刻貼著桌面拉回 10 cm
            float x = -0.02f, gap = 0.30f;
            s.Move(0.25, HandTarget.At(new Vector3(x + gap / 2, 0.025f, 0.08f)), HandTarget.At(new Vector3(x - gap / 2, 0.025f, 0.08f)));
            s.Wait(1.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.AllInCancelled), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Hands_drifting_forward_12cm_over_2s_while_resting_is_not_all_in([Values(Ease.Smooth, Ease.Linear)] Ease ease)
        {
            int bad = 0;
            string sample = null;
            for (int i = 0; i < 100; i++)
            {
                var ev = G.Run(GestureScripts.RestingDrift(new SyntheticHands(seed: 300 + i).WithNoise(0.01f).WithPinchCap(), 0.12f, 2.0, ease: ease));
                if (AnyAllIn(ev)) { bad++; sample ??= ev.Dump(); }
            }
            Assert.That(bad, Is.Zero, sample);
        }

        [Test]
        public void Sliding_cards_and_chips_with_separate_hands_is_not_all_in([Values(0.4, 0.6, 1.0, -0.6)] double lag)
        {
            int bad = 0;
            string sample = null;
            for (int i = 0; i < 40; i++)
            {
                // 只看 AllInConfirmed：兩手先後移動時偶爾會先發 AllInPushStarted 再取消（改動前就有：0.4／0.6／−0.6 s 各 2／12／13 次），不會成為 All-in
                var ev = G.Run(GestureScripts.SlideCardsAndChips(new SyntheticHands(seed: 400 + i).WithNoise(0.01f).WithPinchCap(), lag));
                if (ev.Count(GestureKind.AllInConfirmed) > 0) { bad++; sample ??= ev.Dump(); }
            }
            Assert.That(bad, Is.Zero, sample);
        }

        // 準備姿勢：右手捏合點在籌碼堆邊緣（手放下時不小心捏到），左手在牌左邊
        static SyntheticHands ReadyWithIncidentalPick(int seed, float pinch, float lift)
        {
            var s = new SyntheticHands(seed: seed).WithNoise(0.01f).WithPinchCap();
            var pinchAt = G.Stack + new Vector3(-0.05f, 0.02f, -0.04f);
            var rPalm = SyntheticHands.PalmForPinch(Hand.Right, pinchAt);
            var lPalm = new Vector3(rPalm.X - 0.30f, 0.025f, rPalm.Z);
            s.Set(Hand.Left, lPalm).Set(Hand.Right, rPalm + new Vector3(0, 0.04f, 0));
            s.Move(Hand.Right, 0.3, HandTarget.At(rPalm));
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(pinch));
            if (lift > 0) s.Move(Hand.Right, 0.4, HandTarget.At(rPalm + new Vector3(0, lift, 0)));
            s.Wait(0.5);
            var fwd = new Vector3(0, 0, 0.14f);
            var rNow = rPalm + new Vector3(0, lift, 0);
            s.Move(0.45, HandTarget.At(rNow + fwd), HandTarget.At(lPalm + fwd));
            s.Wait(1.2);
            return s;
        }

        [Test]
        public void Incidental_one_chip_pick_in_ready_pose_is_put_back_and_does_not_block_all_in()
        {
            var ev = G.Run(ReadyWithIncidentalPick(21, 0.75f, 0f));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), "前提：準備時真的捏起了籌碼\n" + ev.Dump());
            var rel = ev.Where(e => e.Kind == GestureKind.ChipsReleased).ToList();
            Assert.That(rel.Count, Is.EqualTo(1), ev.Dump());
            Assert.That(rel[0].InBetZone, Is.False, "放回籌碼堆，不是下注\n" + ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Chips_lifted_three_high_still_block_all_in()
        {
            // 抬起 6 cm＝3 枚：是在拿籌碼，不放回、不 All-in
            var ev = G.Run(ReadyWithIncidentalPick(22, 1f, 0.06f));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Where(e => e.Kind == GestureKind.ChipCountChanged).Select(e => e.Count).DefaultIfEmpty(1).Max(), Is.GreaterThanOrEqualTo(3), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero, ev.Dump());
        }
    }
}
