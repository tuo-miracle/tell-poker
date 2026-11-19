using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// Fool Rocco 原型（2026-10-14）：從手部資料＋手勢事件抽出 Rocco 讀的三個訊號——
    /// 伸手延遲（輪到你到第一次有目的地伸向牌／籌碼／下注區）、這一手看牌次數、在自己籌碼上方逗留沒下注的時間。
    /// 辨識失敗（差一點、沒捏到）的時間不算：伸手延遲看的是第一次伸手，不是第一次成功；逗留扣掉失敗嘗試的時段。
    /// </summary>
    public class PlayerHabitsTests
    {
        static readonly TableLayout L = new TableLayout();
        static Vector3 OverChips => SyntheticHands.PalmForPinch(Hand.Right, L.ChipStackCenter + new Vector3(0, 0.02f, 0)) + new Vector3(0, 0.04f, 0);
        static Vector3 OverCards => L.CardsCenter + new Vector3(0, 0.05f, -0.02f);

        /// <summary>把合成手的每一幀餵進去，events 依時間插在對應的幀之後。</summary>
        static void Feed(PlayerHabits h, SyntheticHands s, double offset = 0, IList<GestureEvent> events = null, IList<(double t, string trace)> traces = null)
        {
            int ei = 0, ti = 0;
            foreach (var f in s.Frames)
            {
                f.Time += offset;
                h.Observe(f);
                while (events != null && ei < events.Count && events[ei].Time <= f.Time) h.Observe(events[ei++]);
                while (traces != null && ti < traces.Count && traces[ti].t <= f.Time) h.NoteTrace(traces[ti++].trace);
            }
        }

        static PlayerHabits Turn(double start = 0)
        {
            var h = new PlayerHabits(L);
            h.BeginHand();
            h.BeginTurn(start);
            return h;
        }

        // ───────────── 伸手延遲 ─────────────

        [Test]
        public void ReachDelay_IsTheTimeFromTurnStartToTheFirstMoveTowardTheChips()
        {
            var s = new SyntheticHands().Wait(2.0);
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.5);
            var h = Turn();
            Feed(h, s);
            Assert.AreEqual(ReachTarget.Chips, h.ReachTarget);
            Assert.AreEqual(2.0, h.ReachDelay, 0.15, "the move started at 2.0 s");
        }

        [Test]
        public void ReachDelay_WithTrackingNoise_StillFindsTheReach([Values(1, 2, 3)] int seed)
        {
            var s = new SyntheticHands(seed: seed).WithNoise(0.004f).Wait(3.0);
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.8);
            var h = Turn();
            Feed(h, s);
            Assert.AreEqual(ReachTarget.Chips, h.ReachTarget);
            Assert.AreEqual(3.0, h.ReachDelay, 0.25);
        }

        [Test]
        public void TrackingJitterAtRest_IsNotAReach_UntilTheDecisionItself()
        {
            var s = new SyntheticHands(seed: 3).WithNoise(0.003f).Wait(4.0);
            var h = Turn();
            Feed(h, s);
            Assert.Less(h.ReachDelay, 0f, "no reach while the hands rest (jitter only)");
            Assert.AreEqual(ReachTarget.None, h.ReachTarget);
            h.EndTurn(4.2);
            Assert.AreEqual(4.2, h.ReachDelay, 1e-3, "no reach seen: the decision time is the reach");
            Assert.AreEqual(ReachTarget.Action, h.ReachTarget);
        }

        [Test]
        public void AReachTowardTheCards_ThatBecomesAPeek_IsAPeekNotAReach()
        {
            var s = new SyntheticHands().Wait(0.5);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverCards)).Wait(1.0);
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(1.0);   // 3.4 s：回到桌邊
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.4);                  // 3.4 s 起伸向籌碼
            var ev = new List<GestureEvent>
            {
                new GestureEvent(GestureKind.PeekChanged, 1.1, Hand.Right, amount: 0.5f),
                new GestureEvent(GestureKind.PeekChanged, 1.7, Hand.Right, amount: 0f),
            };
            var h = Turn();
            Feed(h, s, events: ev);
            Assert.AreEqual(1, h.Peeks);
            Assert.AreEqual(ReachTarget.Chips, h.ReachTarget);
            Assert.AreEqual(3.4, h.ReachDelay, 0.15, "the peek's approach is not the reach");
        }

        [Test]
        public void AReachTowardTheCards_ThatFolds_IsTheReach()
        {
            var s = new SyntheticHands().Wait(1.0);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverCards)).Wait(0.8);
            var ev = new List<GestureEvent> { new GestureEvent(GestureKind.FoldPushed, 2.1, Hand.Right) };
            var h = Turn();
            Feed(h, s, events: ev);
            Assert.AreEqual(ReachTarget.Cards, h.ReachTarget);
            Assert.AreEqual(1.0, h.ReachDelay, 0.15);
        }

        [Test]
        public void FailedPinchAttempts_DoNotDelayTheReach()
        {
            var s = new SyntheticHands().Wait(1.0);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverChips)).Wait(3.5);
            var traces = new List<(double, string)>
            {
                (1.6, "1.600 chip near-miss Right: strength 0.31 (start ≥0.42), reason pinch too weak"),
                (2.6, "2.600 chip near-miss Right: strength 0.33 (start ≥0.42), reason pinch too weak"),
            };
            var ev = new List<GestureEvent> { new GestureEvent(GestureKind.ChipsPicked, 4.0, Hand.Right, count: 2) };
            var h = Turn();
            Feed(h, s, events: ev, traces: traces);
            Assert.AreEqual(1.0, h.ReachDelay, 0.15, "the first reach counts, not the first successful pinch");
        }

        [Test]
        public void ReachDelay_IsMeasuredFromTheTurnStart_NotTheHandStart()
        {
            // 幀時間 10–16.8 s；14 s 才輪到玩家
            var h2 = new PlayerHabits(L);
            h2.BeginHand();
            h2.BeginTurn(14.0);
            var s2 = new SyntheticHands().Wait(6.0);
            s2.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.3);
            Feed(h2, s2, offset: 10);
            Assert.AreEqual(2.0, h2.ReachDelay, 0.15, "14 s turn start → 16 s reach");
        }

        [Test]
        public void MovementBeforeTheTurn_IsNotAReach()
        {
            var s = new SyntheticHands().Wait(0.2);
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.3);
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(3.0);
            var h = new PlayerHabits(L);
            h.BeginHand();
            int split = s.Frames.FindIndex(f => f.Time >= 2.0);
            foreach (var f in s.Frames.GetRange(0, split)) h.Observe(f);
            h.BeginTurn(2.0);
            foreach (var f in s.Frames.GetRange(split, s.Frames.Count - split)) h.Observe(f);
            Assert.Less(h.ReachDelay, 0f);
        }

        // ───────────── 看牌次數 ─────────────

        [Test]
        public void Peeks_CountStarts_MergeFlicker_AndALiftRightAfterAPeek()
        {
            var h = new PlayerHabits(L);
            h.BeginHand();
            void E(GestureKind k, double t, float a = 0f) => h.Observe(new GestureEvent(k, t, Hand.Right, amount: a));
            E(GestureKind.PeekChanged, 1.0, 0.3f);
            E(GestureKind.PeekChanged, 1.3, 0.0f);
            E(GestureKind.PeekChanged, 1.5, 0.4f);   // 0.2 s 後又開始：追蹤閃爍，同一次
            E(GestureKind.PeekChanged, 2.0, 0.0f);
            E(GestureKind.CardsLifted, 2.3);        // 咪完馬上拿起來看：同一次
            E(GestureKind.CardsReturned, 3.5);
            Assert.AreEqual(1, h.Peeks);
            E(GestureKind.PeekChanged, 6.0, 0.5f);   // 隔一陣子再看：第二次
            E(GestureKind.PeekChanged, 6.5, 0.0f);
            E(GestureKind.CardsLifted, 9.0);        // 第三次（拿起來看）
            Assert.AreEqual(3, h.Peeks);
            h.BeginHand();
            Assert.AreEqual(0, h.Peeks, "per hand");
        }

        [Test]
        public void APeekNearMiss_OrATinyCornerLift_IsNotAPeek()
        {
            var h = new PlayerHabits(L);
            h.BeginHand();
            h.NoteTrace("1.000 peek near-miss Right: palm h 0.090 (max 0.080), tip h 0.050, reason palm too high");
            h.Observe(new GestureEvent(GestureKind.PeekChanged, 2.0, Hand.Right, amount: 0.05f));
            h.Observe(new GestureEvent(GestureKind.PeekChanged, 2.2, Hand.Right, amount: 0f));
            Assert.AreEqual(0, h.Peeks);
        }

        // ───────────── 籌碼上方逗留 ─────────────

        [Test]
        public void HoveringOverTheChips_WithoutBetting_Counts()
        {
            var s = new SyntheticHands().Wait(0.5);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverChips)).Wait(2.0);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest)).Wait(0.5);
            var h = Turn();
            Feed(h, s);
            Assert.AreEqual(2.2, h.ChipHover, 0.3);
        }

        [Test]
        public void ANormalPickAndBet_IsNotHovering()
        {
            var s = new SyntheticHands().Wait(0.5);
            GestureScripts.PickChips(s, Hand.Right, lift: 0.04f);
            double pickAt = s.Time - 0.7;
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0f, 0.02f, 0.40f));
            var ev = new List<GestureEvent>
            {
                new GestureEvent(GestureKind.ChipsPicked, pickAt, Hand.Right, count: 2),
                new GestureEvent(GestureKind.ChipsReleased, s.Time - 0.3, Hand.Right, count: 2, inBetZone: true),
            };
            var h = Turn();
            Feed(h, s, events: ev);
            Assert.Less(h.ChipHover, 0.3f, "approach + pinch + lift of a real bet is purposeful");
        }

        [Test]
        public void PickingChipsAndPuttingThemBack_IsFiddling()
        {
            var s = new SyntheticHands().Wait(0.5);
            GestureScripts.PickChips(s, Hand.Right, lift: 0.04f);
            double pickAt = s.Time - 0.7;
            s.Wait(1.5);
            var ev = new List<GestureEvent>
            {
                new GestureEvent(GestureKind.ChipsPicked, pickAt, Hand.Right, count: 2),
                new GestureEvent(GestureKind.ChipsReleased, s.Time - 0.1, Hand.Right, count: 2, inBetZone: false),
            };
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest));
            var h = Turn();
            Feed(h, s, events: ev);
            Assert.Greater(h.ChipHover, 1.8f);
        }

        [Test]
        public void FailedPinchAttempts_AreNotCountedAsHovering()
        {
            var s = new SyntheticHands().Wait(0.5);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverChips)).Wait(3.0);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest));
            var traces = new List<(double, string)>
            {
                (1.2, "1.200 chip near-miss Right: strength 0.30, reason pinch too weak"),
                (2.0, "2.000 chip near-miss Right: strength 0.30, reason pinch too weak"),
                (2.8, "2.800 chip near-miss Right: strength 0.30, reason pinch too weak"),
                (3.5, "3.500 chip near-miss Right: strength 0.30, reason pinch too weak"),
            };
            var h = Turn();
            Feed(h, s, traces: traces);
            Assert.Less(h.ChipHover, 0.6f, "the player was trying to grab, not fiddling");
        }

        [Test]
        public void Hovering_OnlyCountsDuringTheTurn_AndFreezesAtTheDecision()
        {
            var s = new SyntheticHands().Wait(0.2);
            s.Move(Hand.Right, 0.3, HandTarget.At(OverChips)).Wait(5.0);
            var h = new PlayerHabits(L);
            h.BeginHand();
            int a = s.Frames.FindIndex(f => f.Time >= 2.0), b = s.Frames.FindIndex(f => f.Time >= 3.0);
            foreach (var f in s.Frames.GetRange(0, a)) h.Observe(f);
            h.BeginTurn(2.0);
            foreach (var f in s.Frames.GetRange(a, b - a)) h.Observe(f);
            h.EndTurn(3.0);
            foreach (var f in s.Frames.GetRange(b, s.Frames.Count - b)) h.Observe(f);
            Assert.AreEqual(1.0, h.ChipHover, 0.1);
            var snap = h.Snapshot();
            Assert.AreEqual(h.ChipHover, snap.ChipHover);
            Assert.AreEqual(h.ReachDelay, snap.ReachDelay);
            Assert.AreEqual(h.Peeks, snap.Peeks);
        }

        [Test]
        public void TraceParsing_ReadsTimeAndHand_OnlyForFailedAttempts()
        {
            Assert.IsTrue(PlayerHabits.TryParseFailedAttempt("12.345 chip near-miss Right: strength 0.3", out double t, out var hand, out bool peek));
            Assert.AreEqual(12.345, t, 1e-6);
            Assert.AreEqual(Hand.Right, hand);
            Assert.IsFalse(peek);
            Assert.IsTrue(PlayerHabits.TryParseFailedAttempt("3.000 peek near-miss Left: palm h 0.1", out _, out hand, out peek));
            Assert.AreEqual(Hand.Left, hand);
            Assert.IsTrue(peek);
            Assert.IsTrue(PlayerHabits.TryParseFailedAttempt("4.500 knock-tap Right: no second stroke within 0.40 s", out _, out _, out _));
            Assert.IsTrue(PlayerHabits.TryParseFailedAttempt("5.000 fold-push reset Right: lifted (palm z 0.20)", out _, out _, out _));
            Assert.IsTrue(PlayerHabits.TryParseFailedAttempt("6.000 all-in not armed: hands too far apart (L …)", out _, out _, out _));
            Assert.IsFalse(PlayerHabits.TryParseFailedAttempt("7.000 peek start Right: palm h 0.05", out _, out _, out _));
            Assert.IsFalse(PlayerHabits.TryParseFailedAttempt("8.000 chip release Right: n 2", out _, out _, out _));
            Assert.IsFalse(PlayerHabits.TryParseFailedAttempt("garbage", out _, out _, out _));
        }

        /// <summary>真的辨識器：太輕的捏（差一點）之後才捏起來下注——逗留不算失敗的那段，伸手是第一次伸手。</summary>
        [Test]
        public void WithTheRealRecognizer_AWeakPinchThenABet_GivesAnEarlyReachAndLittleHover()
        {
            var s = new SyntheticHands().Wait(1.0);
            var at = L.ChipStackCenter + new Vector3(0, 0.02f, 0);
            GestureScripts.WeakPinchAt(s, Hand.Right, at, pinch: 0.45f, lift: 0.02f);
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0f)).Wait(0.6);
            GestureScripts.WeakPinchAt(s, Hand.Right, at, pinch: 0.45f, lift: 0.02f);
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(0f)).Wait(0.3);
            GestureScripts.PickChips(s, Hand.Right, lift: 0.04f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0f, 0.02f, 0.40f));
            s.Wait(2.0);

            var r = new GestureRecognizer(L);
            var h = Turn();
            var traces = new List<string>();
            r.Trace = m => { traces.Add(m); h.NoteTrace(m); };
            var all = new List<GestureEvent>();
            foreach (var f in s.Frames)
            {
                h.Observe(f);
                foreach (var e in r.Update(f, G.Ctx(chips: 1000, denom: 10))) { all.Add(e); h.Observe(e); }
            }
            Assert.IsTrue(traces.Exists(m => m.Contains("chip near-miss")), "precondition: the weak pinches were near-misses\n" + string.Join("\n", traces));
            Assert.AreEqual(1, all.Count(GestureKind.BetCommitted), all.Dump());
            Assert.AreEqual(1.0, h.ReachDelay, 0.2, string.Join("\n", traces));
            Assert.Less(h.ChipHover, 0.8f, "failed attempts + the real bet are not fiddling");
        }
    }
}
