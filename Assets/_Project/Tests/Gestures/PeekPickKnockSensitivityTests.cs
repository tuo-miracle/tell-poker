using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-12 實機回報「咪牌、拿籌碼、敲桌過牌都不太靈敏」後放寬的規則與新增的診斷輸出。
    /// 證據：peek_v2_153106（整隻手抬起、掌心轉到 84–96°、指尖 3–5.7 cm）、使用者要 4–5 cm 的手腕抬起就看得到牌；
    /// Quest 捏合強度封頂 0.70，輕捏常只有 0.45–0.55、指尖距約 2 cm；實機 log pid 22510 的輕敲（峰值 0.4–0.6 m/s、
    /// 兩下之間手掌只回升 1.3–2 cm，被判 no-rise）。
    /// </summary>
    public class PeekPickKnockSensitivityTests
    {
        const float ChipRadius = 0.02f;

        static float MaxPeek(List<GestureEvent> ev) =>
            ev.Where(e => e.Kind == GestureKind.PeekChanged).Select(e => e.Amount).DefaultIfEmpty(0f).Max();

        /// <summary>逐幀跑，回傳事件與每幀右手咪牌量（時間, 量）。</summary>
        static (List<GestureEvent> ev, List<(double t, float a)> peek, List<string> trace) RunTraced(SyntheticHands s, TableContext? ctx = null,
            IReadOnlyList<Vector3> footprint = null, Hand h = Hand.Right)
        {
            var r = new GestureRecognizer(G.Layout);
            if (footprint != null) r.SetPlayerChipFootprint(footprint, ChipRadius);
            var trace = new List<string>();
            r.Trace = trace.Add;
            var ev = new List<GestureEvent>();
            var peek = new List<(double, float)>();
            var c = ctx ?? G.Ctx();
            foreach (var f in s.Frames)
            {
                ev.AddRange(r.Update(f, c));
                peek.Add((f.Time, r.PeekAmount(h)));
            }
            return (ev, peek, trace);
        }

        // ───────────── 咪牌 ─────────────
        [Test]
        public void Shallow_wrist_lift_of_5cm_already_counts_as_a_peek()
        {
            // 手腕高出指尖 5 cm 就要到教學判定的 0.45（舊比例只有 0.28）
            var s = GestureScripts.Peek(new SyntheticHands(), Hand.Right, pitch: GestureScripts.PitchForWristOverTip(0.05f), lift: 0.005f);
            var ev = G.Run(s);
            Assert.That(MaxPeek(ev), Is.GreaterThanOrEqualTo(0.45f), ev.Dump());
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
        }

        [Test]
        public void Whole_hand_lift_with_palm_turning_to_vertical_keeps_the_corner_up()
        {
            // peek_v2_153106：手掌抬到 9–10.6 cm、手腕高出指尖 6–8 cm、掌心角度掉到 84–96°，舊規則（< 90° 就放下）在抬到一半就把牌放平
            var s = GestureScripts.Peek(new SyntheticHands(), Hand.Right, pitch: 30f, lift: 0.06f, roll: 95f, hold: 0.6);
            double holdEnd = s.Time - 0.3; // 最後一段 0.3 s 是放下
            var (ev, peek, _) = RunTraced(s);
            float atHoldEnd = peek.Last(p => p.t <= holdEnd - 0.02).a;
            Assert.That(atHoldEnd, Is.GreaterThanOrEqualTo(0.6f), $"hold 結束時的咪牌量 {atHoldEnd:0.00}\n" + ev.Dump());
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
        }

        [Test]
        public void Hand_resting_flat_on_the_cards_does_not_noticeably_peek()
        {
            var s = new SyntheticHands(seed: 3).WithNoise(0.01f);
            s.Move(Hand.Right, 0.5, HandTarget.At(G.Cards + new Vector3(0, 0.025f, 0)));
            s.Wait(3.0);
            // 合成雜訊含 4° 旋轉漂移（手腕對指尖約 ±1.2 cm，比真機休息時的 ±1–3 mm 嚴苛）：牌角可能微微翹起，但不能到教學判定的 0.45
            Assert.That(MaxPeek(G.Run(s)), Is.LessThan(0.45f));
        }

        [Test]
        public void Flat_hand_hovering_8cm_over_the_cards_does_not_peek()
        {
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(G.Cards + new Vector3(0, 0.08f, 0)));
            s.Wait(0.5);
            s.Move(Hand.Right, 0.4, HandTarget.Pitch(12f));
            s.Wait(0.5);
            Assert.That(MaxPeek(G.Run(s)), Is.EqualTo(0f));
        }

        // ───────────── 捏籌碼 ─────────────
        static List<Vector3> Column(Vector3 at) => new List<Vector3> { at };

        [Test]
        public void Weak_pinch_at_a_chip_column_picks()
        {
            // 強度 0.51、指尖距 2.0 cm（舊門檻 0.60／1.2 cm 都不到）
            var col = G.Stack + new Vector3(-0.043f, 0f, -0.112f);
            var s = GestureScripts.WeakPinchAt(new SyntheticHands(), Hand.Right, col + new Vector3(0, 0.02f, 0));
            var (ev, _, _) = RunTraced(s, footprint: Column(col));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Weak_pinch_at_the_fixed_chip_zone_picks_and_opening_the_fingers_releases()
        {
            var s = GestureScripts.WeakPinchAt(new SyntheticHands(), Hand.Right, G.Stack + new Vector3(0.02f, 0.02f, 0.01f));
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Wait(0.3);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.ChipsReleased), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Pinching_3cm_outside_a_column_picks_with_the_wider_hover_margin()
        {
            var col = G.Stack + new Vector3(-0.043f, 0f, -0.112f);
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, 0f, col + new Vector3(-(ChipRadius + 0.028f), 0, 0) - G.Stack);
            var (ev, _, trace) = RunTraced(s, footprint: Column(col));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump() + "\n" + string.Join("\n", trace));
        }

        [Test]
        public void Relaxed_hand_resting_by_the_stack_does_not_pick()
        {
            // bet_v2_152604 休息時：強度 0.17–0.34、拇指食指相距 2.9–3.2 cm
            var s = new SyntheticHands().WithPinchCap(0.54f);
            var palm = SyntheticHands.PalmForPinch(Hand.Right, G.Stack + new Vector3(0, 0.0f, 0));
            s.Move(Hand.Right, 0.5, HandTarget.At(palm));
            s.Move(Hand.Right, 0.3, HandTarget.Pinch(0.616f)); // 強度 0.33、指尖距 2.9 cm
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.Zero, ev.Dump());
        }

        // ───────────── 敲桌 ─────────────
        static Vector3 KnockAt => GestureScripts.KnockSpot(Hand.Right);

        [Test]
        public void Knock_ending_6_5cm_above_the_calibrated_table_still_checks()
        {
            // 實機 log pid 22510：1845.340／1847.715／1857.007 s 手掌 6.3–6.7 cm 被判 too-high（桌面校準誤差 1–3 cm）
            var ev = G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, KnockAt + new Vector3(0, 0.04f, 0)));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Double_tap_with_the_hand_in_the_air_does_not_check() =>
            Assert.That(G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, KnockAt + new Vector3(0, 0.07f, 0))).Count(GestureKind.KnockCheck), Is.Zero);

        [Test]
        public void Gentle_double_knock_like_the_successful_device_pairs_checks()
        {
            // 2009.965／2022.673 s：峰值 0.52–0.61 m/s、落差 3.6–5 cm、間隔 0.26–0.28 s
            var ev = G.Run(GestureScripts.GentleKnock(new SyntheticHands(), Hand.Right, KnockAt, rise: 0.024f, sink: 0.012f));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(e => e.IsAction), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Gentle_knock_rising_only_1_5cm_between_taps_is_still_rejected_as_a_known_tradeoff()
        {
            // 2005.687 s 那種回升只有 1.3–2 cm 的輕敲：放寬 KnockMinRise 會讓日常雜動誤觸超過 1%（見 GestureTuning.KnockMinRise）。
            // 這個測試釘住目前的取捨；有了新的實機 trace（真正的回升量）再重新評估。
            var ev = G.Run(GestureScripts.GentleKnock(new SyntheticHands(), Hand.Right, KnockAt, rise: 0.015f, sink: 0.008f));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
        }

        [Test]
        public void Single_gentle_tap_does_not_check() =>
            Assert.That(G.Run(GestureScripts.GentleKnock(new SyntheticHands(), Hand.Right, KnockAt, second: false)).AnyAction(), Is.False);

        [Test]
        public void Hand_settling_down_without_lifting_between_does_not_check()
        {
            // 放下手時被雜訊切成兩段：中間沒有回升（< 1 cm），不能算兩下
            var ev = G.Run(GestureScripts.GentleKnock(new SyntheticHands(), Hand.Right, KnockAt, rise: 0.006f, sink: 0.01f));
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
        }

        [Test]
        public void Setting_chips_down_in_the_bet_zone_does_not_knock()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(seed: 5).WithNoise(0.01f), Hand.Right, 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.05f, 0.02f, 0.40f), 0.5);
            s.Move(Hand.Right, 0.2, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0.05f, 0.02f, 0.40f))));
            s.Wait(2.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
        }

        // ───────────── 診斷輸出 ─────────────
        [Test]
        public void Chip_near_miss_is_traced_with_pinch_values_and_reason_but_rate_limited()
        {
            // 手在籌碼堆上、手指半捏（強度 0.35、指尖距 3.8 cm）但沒到門檻
            var s = new SyntheticHands();
            var palm = SyntheticHands.PalmForPinch(Hand.Right, G.Stack + new Vector3(0, 0.02f, 0));
            s.Move(Hand.Right, 0.5, HandTarget.At(palm));
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0.5f));
            s.Wait(2.0);
            var (ev, _, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.Zero, "前提：真的沒捏起");
            var lines = trace.Where(t => t.Contains("chip near-miss")).ToList();
            Assert.That(lines, Is.Not.Empty, string.Join("\n", trace));
            foreach (var key in new[] { "strength", "tip gap", "flag", "nearest", "height", "reason" })
                Assert.That(lines[0], Does.Contain(key));
            Assert.That(lines.Count, Is.LessThanOrEqualTo(10), "2.6 秒內每個原因最多每 0.25 s 一行\n" + string.Join("\n", lines));
        }

        [Test]
        public void Chip_near_miss_explains_that_pick_is_blocked_when_not_players_turn()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, 0.02f);
            var (ev, _, trace) = RunTraced(s, G.Ctx(turn: false));
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.Zero);
            Assert.That(trace.Any(t => t.Contains("chip near-miss") && t.Contains("not player's turn")), Is.True, string.Join("\n", trace));
        }

        [Test]
        public void No_chip_trace_when_the_hand_is_away_from_the_chips()
        {
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(new Vector3(0.40f, 0.03f, 0.45f)));
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0.5f));
            s.Wait(1.0);
            var (_, _, trace) = RunTraced(s);
            Assert.That(trace.Where(t => t.Contains("chip near-miss")), Is.Empty, string.Join("\n", trace));
        }

        [Test]
        public void Peek_near_miss_over_the_cards_is_traced_with_hand_values_and_reason()
        {
            // 手在牌上方但側立（掌心角度 100°，不是掌心朝下）：沒進入咪牌
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(G.Cards + new Vector3(0, 0.04f, 0)).WithRoll(80f));
            s.Wait(1.5);
            var (ev, _, trace) = RunTraced(s);
            Assert.That(MaxPeek(ev), Is.EqualTo(0f), "前提：沒咪牌");
            var lines = trace.Where(t => t.Contains("peek near-miss")).ToList();
            Assert.That(lines, Is.Not.Empty, string.Join("\n", trace));
            foreach (var key in new[] { "palm h", "wrist-tip", "angle", "normal.y", "to cards", "reason" })
                Assert.That(lines[0], Does.Contain(key));
            Assert.That(lines.Any(t => t.Contains("reason palm not down")), Is.True, string.Join("\n", lines));
            Assert.That(lines.Count, Is.LessThanOrEqualTo(8), string.Join("\n", lines));
        }

        [Test]
        public void Peek_start_and_end_are_traced_with_the_max_amount()
        {
            var s = GestureScripts.Peek(new SyntheticHands());
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest));
            s.Wait(0.3);
            var (_, _, trace) = RunTraced(s);
            Assert.That(trace.Count(t => t.Contains("peek start")), Is.EqualTo(1), string.Join("\n", trace));
            var end = trace.Where(t => t.Contains("peek end")).ToList();
            Assert.That(end.Count, Is.EqualTo(1), string.Join("\n", trace));
            Assert.That(end[0], Does.Contain("max 1.00"));
        }

        [Test]
        public void Knock_trace_reports_the_real_rise_and_the_thresholds()
        {
            // 回升 0.9 cm（不到 min rise）、第二下夠快：會判 no-rise，trace 要說回升 0.009，不是第二下結束時比最低點低了多少
            var (_, _, trace) = RunTraced(GestureScripts.GentleKnock(new SyntheticHands(), Hand.Right, KnockAt, rise: 0.009f, sink: 0.015f, secondDown: 0.06));
            var pair = trace.FirstOrDefault(t => t.Contains("knock-palm") && t.Contains(" s, rise "));
            Assert.That(pair, Is.Not.Null, string.Join("\n", trace));
            // rise 是規則用的量（兩下之間最低點之後回升了多少），不是第二下結束時的高度差
            Assert.That(pair, Does.Contain("rise 0.00"));
            Assert.That(pair, Does.Contain("no-rise"));
            Assert.That(pair, Does.Contain("min rise"));
            Assert.That(pair, Does.Contain("max spread"));
        }

        [Test]
        public void Knock_trace_says_when_no_second_stroke_came()
        {
            var (_, _, trace) = RunTraced(GestureScripts.GentleKnock(new SyntheticHands(), Hand.Right, KnockAt, second: false));
            Assert.That(trace.Any(t => t.Contains("knock-palm") && t.Contains("no second stroke")), Is.True, string.Join("\n", trace));
        }
    }
}
