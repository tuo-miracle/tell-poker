using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 即時牌桌對話（2026-10-14 live talk）：玩家還在想的時候，PlayerHabits 就一個一個吐出 AI 可以「看到」的訊號（有時間戳）——
    /// 回合開始、想了 3／7／12 秒、又看一次牌、手伸向籌碼、沒下注又縮回來、捏著籌碼舉在空中、把籌碼放回去、手伸向牌（可能要棄）、
    /// 盯著某個 AI、玩家開口講話。只用位置與辨識器事件，辨識失敗（沒捏到、推牌中斷）也照樣看得出意圖。
    /// </summary>
    public class LiveSignalTests
    {
        static readonly TableLayout L = new TableLayout();
        static Vector3 OverChips => SyntheticHands.PalmForPinch(Hand.Right, L.ChipStackCenter + new Vector3(0, 0.02f, 0)) + new Vector3(0, 0.04f, 0);
        static Vector3 OverCards => L.CardsCenter + new Vector3(0, 0.05f, -0.02f);

        /// <summary>手部資料逐幀餵，events／traces 依時間插在對應的幀之後；吐出的訊號全部收集。</summary>
        static List<HabitSignal> Feed(PlayerHabits h, SyntheticHands s, IList<GestureEvent> events = null, IList<(double t, string trace)> traces = null)
        {
            var got = new List<HabitSignal>();
            int ei = 0, ti = 0;
            foreach (var f in s.Frames)
            {
                h.Observe(f);
                while (events != null && ei < events.Count && events[ei].Time <= f.Time) h.Observe(events[ei++]);
                while (traces != null && ti < traces.Count && traces[ti].t <= f.Time) h.NoteTrace(traces[ti++].trace);
                got.AddRange(h.DrainSignals());
            }
            return got;
        }

        /// <summary>用真的辨識器跑合成手（事件由辨識器產生），訊號全部收集。</summary>
        static List<HabitSignal> FeedWithRecognizer(PlayerHabits h, SyntheticHands s)
        {
            var r = new GestureRecognizer(L);
            var ctx = G.Ctx(turn: true);
            var got = new List<HabitSignal>();
            foreach (var f in s.Frames)
            {
                h.Observe(f);
                foreach (var e in r.Update(f, ctx)) h.Observe(e);
                got.AddRange(h.DrainSignals());
            }
            return got;
        }

        static PlayerHabits Turn(double start = 0)
        {
            var h = new PlayerHabits(L);
            h.BeginHand();
            h.BeginTurn(start);
            return h;
        }

        static string Kinds(IEnumerable<HabitSignal> s) => string.Join(", ", s.Select(x => x.ToString()));

        [Test]
        public void TurnStart_AndThinkingMilestones_AreEmittedOnceEach_WithTimestamps()
        {
            var h = Turn(0);
            var got = h.DrainSignals().ToList();
            got.AddRange(Feed(h, new SyntheticHands().Wait(13.0)));
            Assert.AreEqual(HabitSignalKind.TurnStarted, got[0].Kind);
            var think = got.Where(x => x.Kind == HabitSignalKind.Thinking).ToList();
            CollectionAssert.AreEqual(new[] { 3, 7, 12 }, think.Select(x => x.Count).ToArray(), Kinds(got));
            Assert.AreEqual(3.0, think[0].Time, 0.05);
            Assert.AreEqual(12.0, think[2].Time, 0.05);
            Assert.AreEqual(3, h.Turn.ThinkMilestones);
        }

        [Test]
        public void NothingIsEmitted_OutsideTheTurn_OrAfterTheDecision()
        {
            var h = new PlayerHabits(L);
            h.BeginHand();
            var s = new SyntheticHands().Wait(1.0);
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(4.0);
            Assert.IsEmpty(Feed(h, s), "not the player's turn");
            h.BeginTurn(10);
            h.EndTurn(10.5);
            h.DrainSignals();
            var s2 = new SyntheticHands().Wait(5.0);
            foreach (var f in s2.Frames) f.Time += 10.5;
            Assert.IsEmpty(Feed(h, s2), "after the decision: frozen");
        }

        [Test]
        public void ReachTowardTheChips_ThenPullBackWithoutBetting_IsReachThenPullBack()
        {
            var s = new SyntheticHands().Wait(1.0);
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.6);          // 1.0 s 伸過去，停在籌碼上
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(1.0);   // 2.1 s 縮回桌邊（沒捏、沒下注）
            var h = Turn();
            var got = Feed(h, s);
            var reach = got.Where(x => x.Kind == HabitSignalKind.ReachChips).ToList();
            var back = got.Where(x => x.Kind == HabitSignalKind.PullBack).ToList();
            Assert.AreEqual(1, reach.Count, Kinds(got));
            Assert.AreEqual(1, back.Count, Kinds(got));
            Assert.That(reach[0].Time, Is.InRange(1.2, 1.8), "while the hand arrives over the chips");
            Assert.That(back[0].Time, Is.InRange(2.1, 2.8), "as the hand leaves the chips");
            Assert.Less(reach[0].Time, back[0].Time);
            Assert.AreEqual(1, h.Turn.PullBacks);
            Assert.AreEqual(1, h.Turn.ChipReaches);
        }

        [Test]
        public void ReachRetreatTwice_CountsTwice_AndJitterAtRestIsNothing()
        {
            var s = new SyntheticHands(seed: 4).WithNoise(0.003f).Wait(1.0);
            for (int i = 0; i < 2; i++)
            {
                s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.5);
                s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(0.8);
            }
            var h = Turn();
            var got = Feed(h, s);
            Assert.AreEqual(2, got.Count(x => x.Kind == HabitSignalKind.ReachChips), Kinds(got));
            Assert.AreEqual(2, got.Count(x => x.Kind == HabitSignalKind.PullBack), Kinds(got));

            var rest = Turn();
            var quiet = Feed(rest, new SyntheticHands(seed: 5).WithNoise(0.004f).Wait(2.5));
            Assert.IsFalse(quiet.Any(x => x.Kind != HabitSignalKind.TurnStarted && x.Kind != HabitSignalKind.Thinking), "jitter at rest: " + Kinds(quiet));
        }

        [Test]
        public void PickAndBet_IsAReach_ButNotAPullBack()
        {
            var s = GestureScripts.PickChips(new SyntheticHands().Wait(0.5), Hand.Right, lift: 0.03f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.0f, 0.02f, 0.40f));
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(0.5);
            var h = Turn();
            var got = FeedWithRecognizer(h, s);
            Assert.AreEqual(1, got.Count(x => x.Kind == HabitSignalKind.ReachChips), Kinds(got));
            Assert.AreEqual(0, got.Count(x => x.Kind == HabitSignalKind.PullBack), "chips went into the bet zone: " + Kinds(got));
            Assert.AreEqual(0, got.Count(x => x.Kind == HabitSignalKind.ChipsPutBack), Kinds(got));
            Assert.AreEqual(0, h.Turn.ChipsHeldCount, "a bet is not fiddling");
        }

        [Test]
        public void ASlowBet_MayTriggerChipsHeldLive_ButIsNotCountedAsFiddling()
        {
            var ev = new List<GestureEvent>
            {
                new GestureEvent(GestureKind.ChipsPicked, 1.0, Hand.Right, count: 3),
                new GestureEvent(GestureKind.ChipsReleased, 3.2, Hand.Right, count: 3, inBetZone: true),
            };
            var h = Turn();
            var got = Feed(h, new SyntheticHands().Wait(4.0), events: ev);
            Assert.AreEqual(1, got.Count(x => x.Kind == HabitSignalKind.ChipsHeld), "held 2.2 s in the air: worth a needle");
            Assert.AreEqual(0, h.Turn.ChipsHeldCount, "but it ended as a bet");
            Assert.AreEqual(0, h.Turn.PutBacks);
        }

        [Test]
        public void ChipsPinchedAndHeldInTheAir_IsChipsHeld_WithTheCount_ThenPutBack()
        {
            // 捏起 3 枚、舉著 1.5 s 不動，再放回籌碼堆
            var s = GestureScripts.PickChips(new SyntheticHands().Wait(0.5), Hand.Right, lift: 0.025f * 2 + 0.012f);
            s.Wait(1.5);
            GestureScripts.PlaceAt(s, Hand.Right, L.ChipStackCenter + new Vector3(0, 0.03f, 0));
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(0.5);
            var h = Turn();
            var got = FeedWithRecognizer(h, s);
            var held = got.Where(x => x.Kind == HabitSignalKind.ChipsHeld).ToList();
            Assert.AreEqual(1, held.Count, Kinds(got));
            Assert.AreEqual(3, held[0].Count, "how many chips are in the air");
            var back = got.Where(x => x.Kind == HabitSignalKind.ChipsPutBack).ToList();
            Assert.AreEqual(1, back.Count, Kinds(got));
            Assert.Greater(back[0].Time, held[0].Time);
            Assert.AreEqual(1, h.Turn.PutBacks);
            Assert.Greater(h.Turn.HeldSeconds, 1.0f);
        }

        [Test]
        public void AQuickPickThatIsBetRightAway_IsNotChipsHeld()
        {
            var ev = new List<GestureEvent>
            {
                new GestureEvent(GestureKind.ChipsPicked, 1.0, Hand.Right, count: 2),
                new GestureEvent(GestureKind.ChipsReleased, 1.4, Hand.Right, count: 2, inBetZone: true),
            };
            var h = Turn();
            var got = Feed(h, new SyntheticHands().Wait(3.0), events: ev);
            Assert.AreEqual(0, got.Count(x => x.Kind == HabitSignalKind.ChipsHeld), Kinds(got));
        }

        [Test]
        public void LookingAtTheCardsAgain_EmitsLookWithTheHandCount()
        {
            var ev = new List<GestureEvent>
            {
                new GestureEvent(GestureKind.PeekChanged, 1.0, Hand.Right, amount: 0.5f),
                new GestureEvent(GestureKind.PeekChanged, 1.6, Hand.Right, amount: 0f),
                new GestureEvent(GestureKind.CardsLifted, 3.0, Hand.Right),
                new GestureEvent(GestureKind.CardsReturned, 4.0, Hand.Right),
                new GestureEvent(GestureKind.PeekChanged, 5.0, Hand.Right, amount: 0.4f),
                new GestureEvent(GestureKind.PeekChanged, 5.1, Hand.Right, amount: 0.02f),
                new GestureEvent(GestureKind.PeekChanged, 5.2, Hand.Right, amount: 0.4f),   // 閃爍：同一次
                new GestureEvent(GestureKind.PeekChanged, 5.6, Hand.Right, amount: 0.0f),
            };
            var h = Turn();
            var got = Feed(h, new SyntheticHands().Wait(6.0), events: ev);
            var looks = got.Where(x => x.Kind == HabitSignalKind.Look).ToList();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, looks.Select(x => x.Count).ToArray(), Kinds(got));
            Assert.AreEqual(3.0, looks[1].Time, 1e-6);
            Assert.AreEqual(3, h.Turn.Looks);
        }

        [Test]
        public void APeekNearMiss_IsNotALook()
        {
            var traces = new List<(double, string)> { (1.0, "1.000 peek near-miss Right: palm too high") };
            var h = Turn();
            var got = Feed(h, new SyntheticHands().Wait(2.0), traces: traces);
            Assert.AreEqual(0, got.Count(x => x.Kind == HabitSignalKind.Look));
        }

        [Test]
        public void HandHoveringOverTheCards_WithoutPeeking_IsTowardCards_ButAPeekIsNot()
        {
            var s = new SyntheticHands().Wait(1.0);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverCards)).Wait(1.2);
            var h = Turn();
            var got = Feed(h, s);
            var tc = got.Where(x => x.Kind == HabitSignalKind.TowardCards).ToList();
            Assert.AreEqual(1, tc.Count, Kinds(got));
            Assert.That(tc[0].Time, Is.InRange(1.8, 2.6), "about a second after the hand settles on the cards");
            Assert.IsTrue(h.Turn.TowardCards);

            // 同樣伸過去但馬上咪牌：那是看牌，不是要棄牌
            var s2 = new SyntheticHands().Wait(1.0);
            s2.Move(Hand.Right, 0.4, HandTarget.At(OverCards)).Wait(1.2);
            var ev = new List<GestureEvent> { new GestureEvent(GestureKind.PeekChanged, 1.5, Hand.Right, amount: 0.5f) };
            var h2 = Turn();
            var got2 = Feed(h2, s2, events: ev);
            Assert.AreEqual(0, got2.Count(x => x.Kind == HabitSignalKind.TowardCards), Kinds(got2));
            Assert.AreEqual(1, got2.Count(x => x.Kind == HabitSignalKind.Look));
        }

        [Test]
        public void FailedGestures_StillShowIntent_ChipNearMissIsAReach_FoldPushResetIsTowardCards()
        {
            // 手放到牌上 0.3 s 就推、推到一半中斷（辨識器「fold-push reset」）：不用等 1 秒也看得出要棄牌
            var s = new SyntheticHands().Wait(2.0);
            s.Move(Hand.Right, 0.4, HandTarget.At(OverCards)).Wait(1.5);
            var traces = new List<(double, string)>
            {
                (1.0, "1.000 chip near-miss Right: strength 0.31 (start ≥0.42), reason pinch too weak"),
                (2.7, "2.700 fold-push reset Right: lifted"),
            };
            var h = Turn();
            var got = Feed(h, s, traces: traces);
            Assert.AreEqual(1, got.Count(x => x.Kind == HabitSignalKind.ReachChips), Kinds(got));
            var tc = got.Where(x => x.Kind == HabitSignalKind.TowardCards).ToList();
            Assert.AreEqual(1, tc.Count, Kinds(got));
            Assert.AreEqual(2.7, tc[0].Time, 1e-6, "right at the failed push");
        }

        [Test]
        public void AFoldPushResetAwayFromTheCards_IsNotTowardCards()
        {
            // 真實 idle 錄製：手在桌邊滑動也會出現推牌重設
            var traces = new List<(double, string)> { (1.0, "1.000 fold-push reset Left: lifted (palm -0.15,0.02,0.07)") };
            var h = Turn();
            var got = Feed(h, new SyntheticHands().Wait(2.0), traces: traces);
            Assert.AreEqual(0, got.Count(x => x.Kind == HabitSignalKind.TowardCards), Kinds(got));
        }

        [Test]
        public void AHandRestingBesideTheChips_DriftingInAndOut_IsNotAReach()
        {
            // 手放在籌碼堆旁邊（手掌離籌碼 ~11–15 cm）來回晃：不是伸手
            var near = new Vector3(L.ChipStackCenter.X - 0.13f, 0.025f, L.ChipStackCenter.Z - 0.03f);
            var s = new SyntheticHands().Set(Hand.Right, near).Wait(0.5);
            for (int i = 0; i < 4; i++)
            {
                s.Move(Hand.Right, 0.4, HandTarget.At(near + new Vector3(0.03f, 0, 0.01f))).Wait(0.5);
                s.Move(Hand.Right, 0.4, HandTarget.At(near)).Wait(0.5);
            }
            var h = Turn();
            var got = Feed(h, s);
            Assert.AreEqual(0, got.Count(x => x.Kind == HabitSignalKind.ReachChips || x.Kind == HabitSignalKind.PullBack), Kinds(got));
        }

        [Test]
        public void GazeDwellAtAnAi_IsEmittedOncePerTargetPerTurn()
        {
            var h = Turn();
            h.DrainSignals();
            HabitSignal[] Gaze() => h.DrainSignals().Where(x => x.Kind == HabitSignalKind.GazeDwell).ToArray();
            h.ObserveGaze(1.0, 2, 1.0f);
            Assert.IsEmpty(Gaze(), "1 s is just looking");
            h.ObserveGaze(2.0, 2, 2.0f);
            var got = Gaze();
            Assert.AreEqual(1, got.Length);
            Assert.AreEqual(2, got[0].Target);
            Assert.AreEqual(2.0, got[0].Time, 1e-9);
            h.ObserveGaze(3.0, 2, 3.0f);
            Assert.IsEmpty(Gaze(), "once per target per turn");
            h.ObserveGaze(4.0, 3, 2.2f);
            Assert.AreEqual(3, Gaze().Single().Target);
            h.ObserveGaze(5.0, -1, 0f);
            Assert.IsEmpty(Gaze());
        }

        [Test]
        public void PlayerSpeaking_IsEmittedOnTheRisingEdge()
        {
            var h = Turn();
            h.DrainSignals();
            h.ObserveVoice(1.0, false);
            h.ObserveVoice(1.5, true);
            h.ObserveVoice(1.6, true);
            h.ObserveVoice(2.5, false);
            h.ObserveVoice(3.0, true);
            var got = h.DrainSignals().Where(x => x.Kind == HabitSignalKind.PlayerSpoke).ToList();
            Assert.AreEqual(2, got.Count);
            Assert.AreEqual(1.5, got[0].Time, 1e-9);
            Assert.IsTrue(h.PlayerSpeaking);
        }

        [Test]
        public void TheFoolRoccoHabitsStillWork_ReachDelayIsUnchangedByTheStream()
        {
            var s = new SyntheticHands().Wait(2.0);
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.5);
            var h = Turn();
            Feed(h, s);
            Assert.AreEqual(ReachTarget.Chips, h.ReachTarget);
            Assert.AreEqual(2.0, h.ReachDelay, 0.15);
        }
    
        /// <summary>真實錄製（Quest 3，*.hands）逐檔跑：辨識器事件＋手部資料 → 即時訊號；整段都當成玩家的回合。</summary>
        static Dictionary<HabitSignalKind, int> RunRecording(string path)
        {
            var rec = HandRecording.Load(path);
            var h = new PlayerHabits(L);
            h.BeginHand();
            var r = new GestureRecognizer(L);
            r.Trace = m => h.NoteTrace(m);
            var ctx = G.Ctx(turn: true);
            bool started = false;
            var got = new Dictionary<HabitSignalKind, int>();
            foreach (var f in rec.Frames)
            {
                if (!started) { h.BeginTurn(f.Time); started = true; }
                h.Observe(f);
                foreach (var e in r.Update(f, ctx)) h.Observe(e);
                foreach (var x in h.DrainSignals()) got[x.Kind] = got.TryGetValue(x.Kind, out var n) ? n + 1 : 1;
            }
            return got;
        }

        /// <summary>
        /// 真實資料的防誤報（v1 idle 錄製＝日常雜動，錄的時候桌上沒畫籌碼與牌，手會在籌碼區、牌旁邊晃）：
        /// 「手放到牌上（可能要棄）」幾乎不該出現（每 10 段 ≤ 1）；伸向籌碼／縮回／放回是真的有移到籌碼區才有，限制在每段平均 ≤ 1.5。
        /// （2026-10-14 調整前：每段 3.6 個，TowardCards 29 個——捏合點與推牌重設太敏感，見 PlayerHabits 的註解。）
        /// </summary>
        [Test]
        public void RealIdleRecordings_RarelyLookLikeReachingOrRetreating()
        {
            var dir = RecordingCheck.FindRecordingsDir();
            if (dir == null) Assert.Ignore("no recordings");
            var files = Directory.GetFiles(dir, "idle*.hands");
            Assert.Greater(files.Length, 10);
            int chips = 0, cards = 0;
            var lines = new List<string>();
            foreach (var f in files)
            {
                var got = RunRecording(f);
                int Get(HabitSignalKind k) => got.TryGetValue(k, out var c) ? c : 0;
                chips += Get(HabitSignalKind.ReachChips) + Get(HabitSignalKind.PullBack) + Get(HabitSignalKind.ChipsPutBack);
                cards += Get(HabitSignalKind.TowardCards);
                lines.Add(Path.GetFileName(f) + ": " + string.Join(", ", got.Select(kv => kv.Key + " " + kv.Value)));
            }
            Console.WriteLine($"[LiveTalk] idle recordings: {files.Length} files, chip signals {chips}, toward-cards {cards}");
            Assert.LessOrEqual(cards, files.Length / 10, string.Join("\n", lines));
            Assert.LessOrEqual(chips / (double)files.Length, 1.5, string.Join("\n", lines));
        }

        [Test, Explicit("工具：idle 錄製裡每個伸手／手放到牌上的位置")]
        public void Tool_IdleSignalPositions()
        {
            var dir = RecordingCheck.FindRecordingsDir();
            foreach (var path in Directory.GetFiles(dir, "idle*.hands"))
            {
                var rec = HandRecording.Load(path);
                var h = new PlayerHabits(L);
                h.BeginHand();
                var r = new GestureRecognizer(L);
                r.Trace = m => h.NoteTrace(m);
                bool started = false;
                foreach (var f in rec.Frames)
                {
                    if (!started) { h.BeginTurn(f.Time); started = true; }
                    h.Observe(f);
                    foreach (var e in r.Update(f, G.Ctx(turn: true))) h.Observe(e);
                    foreach (var x in h.DrainSignals())
                    {
                        if (x.Kind == HabitSignalKind.TurnStarted || x.Kind == HabitSignalKind.Thinking) continue;
                        var hp = f.Get(x.Hand);
                        var p = hp != null && hp.Tracked ? hp.Position(HandJoint.Palm) : default;
                        Console.WriteLine($"[LiveTalk] {Path.GetFileName(path)} {x} {x.Hand} palm=({p.X:0.00},{p.Y:0.00},{p.Z:0.00}) t={x.Time - rec.Frames[0].Time:0.0}/{rec.Frames[rec.Frames.Count - 1].Time - rec.Frames[0].Time:0.0}");
                    }
                }
            }
        }

        [Test, Explicit("工具：每類真實錄製的即時訊號分布")]
        public void Tool_RealRecordingSignals()
        {
            var dir = RecordingCheck.FindRecordingsDir();
            foreach (var group in Directory.GetFiles(dir, "*.hands").GroupBy(f => Path.GetFileName(f).Split('_')[0]))
            {
                var total = new Dictionary<HabitSignalKind, int>();
                int files = 0;
                foreach (var f in group)
                {
                    files++;
                    foreach (var kv in RunRecording(f)) total[kv.Key] = (total.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                }
                Console.WriteLine($"[LiveTalk] {group.Key} ({files}): " + string.Join(", ", total.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)));
            }
        }
    }
}
