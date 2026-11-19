using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 荷官教學 v4 的狀態機與兩手腳本（2026-10-14 改寫）。
    /// v3（兩手、9 個步驟、每個手勢都教）太長：評審只玩一次、約 3 分鐘，很多人不會打牌。v4 只留核心玩法——
    /// 第 1 手：看牌（咪牌或拿起來都算）→ 牌力提示 → Rocco 加注 → 跟或棄（都算）→ 揭曉「Rocco read you: …」；
    /// 第 2 手：拿到怪物牌，Dot 教「等幾秒再下注」騙 Rocco → 下注（等了 Rocco 跟、輸；沒等 Rocco 棄）。
    /// 敲桌過牌、All-in、攤牌翻牌、盯人讀破綻、語音對話改在正式牌局第一次用得到時教（JustInTimeTips）。
    /// 所以 v3 的 Look／Call／Check／Read／Raise／Fold／AllIn／Flip／Voice 步驟與相關測試（逐步順序、語音那一步略過、
    /// 讀破綻點評、加注要比跟注多、四條 10 的河牌全下…）都刪掉，換成下面 v4 的測試；不自動跳過、升級提示（10 s／25 s）、
    /// 依辨識器 trace 給具體建議、只能從選單跳過，這些規則照舊。
    /// </summary>
    public class TutorialTests
    {
        static readonly TutorialStep[] AllSteps = { TutorialStep.Peek, TutorialStep.Decide, TutorialStep.Bet };

        [Test]
        public void Version_Is4()
        {
            Assert.AreEqual(4, Tutorial.CurrentVersion);
            Assert.IsTrue(Tutorial.IsNeeded(new ProgressData { TutorialDone = true, TutorialVersion = 3 }), "v3 的玩家要再教一次（v4 比較短）");
            Assert.IsFalse(Tutorial.IsNeeded(new ProgressData { TutorialDone = true, TutorialVersion = 4 }));
        }

        [Test]
        public void Steps_AdvanceInOrder_EitherChoiceCountsForTheDecision()
        {
            var t = new Tutorial();
            var done = new List<TutorialStep>();
            t.StepCompleted += s => done.Add(s);
            Assert.AreEqual(TutorialStep.Peek, t.Step);
            Assert.IsFalse(t.Report(TutorialAction.Called), "can't call before looking");
            Assert.IsTrue(t.Report(TutorialAction.Peeked));
            Assert.AreEqual(TutorialStep.Decide, t.Step);
            Assert.IsFalse(t.Report(TutorialAction.Peeked), "looking again isn't a decision");
            Assert.IsTrue(t.Report(TutorialAction.Folded), "folding is a fine decision");
            Assert.AreEqual(TutorialStep.Bet, t.Step);
            Assert.IsFalse(t.Report(TutorialAction.Called), "hand 2 needs a bet");
            Assert.IsFalse(t.Report(TutorialAction.Folded), "never fold the monster");
            Assert.IsTrue(t.Report(TutorialAction.Raised));
            Assert.AreEqual(TutorialStep.Done, t.Step);
            Assert.IsFalse(t.IsActive);
            CollectionAssert.AreEqual(AllSteps, done);

            foreach (var a in new[] { TutorialAction.Called, TutorialAction.Raised, TutorialAction.Folded })
            {
                var u = new Tutorial();
                u.Report(TutorialAction.Peeked);
                Assert.IsTrue(u.Report(a), a + " completes the hand-1 decision");
            }
        }

        [Test]
        public void PickingTheCardsUp_CountsAsLooking()
        {
            var t = new Tutorial();
            Assert.IsTrue(t.Report(TutorialAction.Looked));
            Assert.AreEqual(TutorialStep.Decide, t.Step);
        }

        [Test]
        public void NoAutoSkip_EvenAfterFiveMinutes()
        {
            var t = new Tutorial();
            for (int i = 0; i < 300 * 10; i++) t.Tick(0.1f);
            Assert.AreEqual(TutorialStep.Peek, t.Step);
            Assert.IsTrue(t.IsActive);
            Assert.That(t.TotalStepTime, Is.EqualTo(300f).Within(0.5f), "total stuck time keeps counting (for the playtest log)");
        }

        [Test]
        public void Hints_EscalateAt10And25Seconds_ThenRepeat()
        {
            var t = new Tutorial();
            var hints = new List<(TutorialStep s, int level, float at)>();
            float now = 0f;
            t.HintDue += (s, l, _) => hints.Add((s, l, now));
            for (int i = 0; i < 580; i++) { now += 0.1f; t.Tick(0.1f); }
            Assert.AreEqual(4, hints.Count, string.Join(", ", hints));
            Assert.AreEqual(1, hints[0].level);
            Assert.That(hints[0].at, Is.EqualTo(Tutorial.Hint1Seconds).Within(0.15f));
            Assert.AreEqual(2, hints[1].level);
            Assert.That(hints[1].at, Is.EqualTo(Tutorial.Hint2Seconds).Within(0.15f));
            Assert.That(hints[2].at, Is.EqualTo(Tutorial.Hint2Seconds + Tutorial.Hint1Seconds).Within(0.15f), "then the cycle repeats");
            Assert.AreEqual(10f, Tutorial.Hint1Seconds);
            Assert.AreEqual(25f, Tutorial.Hint2Seconds);
        }

        // ===== 2026-10-14 實機：頭盔放著、教學看牌那一步，Dot 每 12／19 秒輪流重講與給建議，講了 2 小時 =====

        static List<(int level, float at)> Record(Tutorial t, System.Func<float> now)
        {
            var hints = new List<(int, float)>();
            t.HintDue += (s, l, _) => hints.Add((l, now()));
            return hints;
        }

        [Test]
        public void Hints_StopAfterTwoFullCycles_ThenStayQuiet()
        {
            var t = new Tutorial();
            float now = 0f;
            var hints = Record(t, () => now);
            var quiet = new List<TutorialStep>();
            t.HintsQuieted += s => quiet.Add(s);
            for (int i = 0; i < 3000; i++) { now += 0.1f; t.Tick(0.1f); }   // 5 分鐘
            Assert.AreEqual(2, Tutorial.MaxHintCycles);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, hints.Select(h => h.level).ToArray(), string.Join(", ", hints));
            Assert.That(hints[3].at, Is.EqualTo(2 * Tutorial.Hint2Seconds).Within(0.15f));
            Assert.IsTrue(t.HintsQuiet);
            CollectionAssert.AreEqual(new[] { TutorialStep.Peek }, quiet, "quiet is announced once");
            Assert.That(t.TotalStepTime, Is.EqualTo(300f).Within(0.5f), "the step is still running (never skipped)");
            Assert.AreEqual(TutorialStep.Peek, t.Step);
        }

        [Test]
        public void Activity_AfterGoingQuiet_GivesExactlyOneMoreCycle()
        {
            var t = new Tutorial();
            float now = 0f;
            var hints = Record(t, () => now);
            int quiet = 0;
            t.HintsQuieted += _ => quiet++;
            for (int i = 0; i < 600; i++) { now += 0.1f; t.Tick(0.1f); }
            Assert.IsTrue(t.HintsQuiet);
            hints.Clear();
            float woke = now;
            t.NoteActivity();
            Assert.IsFalse(t.HintsQuiet);
            for (int i = 0; i < 1200; i++) { now += 0.1f; t.Tick(0.1f); }
            CollectionAssert.AreEqual(new[] { 1, 2 }, hints.Select(h => h.level).ToArray(), string.Join(", ", hints));
            Assert.That(hints[0].at - woke, Is.EqualTo(Tutorial.Hint1Seconds).Within(0.15f), "the timer starts over at the activity");
            Assert.That(hints[1].at - woke, Is.EqualTo(Tutorial.Hint2Seconds).Within(0.15f));
            Assert.IsTrue(t.HintsQuiet, "quiet again after one cycle");
            Assert.AreEqual(2, quiet);
        }

        [Test]
        public void Activity_BeforeGoingQuiet_DoesNotRestartTheTimer()
        {
            var t = new Tutorial();
            float now = 0f;
            var hints = Record(t, () => now);
            for (int i = 0; i < 80; i++) { now += 0.1f; t.Tick(0.1f); }
            t.NoteActivity();
            for (int i = 0; i < 25; i++) { now += 0.1f; t.Tick(0.1f); }
            Assert.AreEqual(1, hints.Count, "still stuck: the 10 s hint comes on time");
            Assert.That(hints[0].at, Is.EqualTo(Tutorial.Hint1Seconds).Within(0.15f));
        }

        [Test]
        public void NearMiss_AfterGoingQuiet_WakesTheHints_WithTheReason()
        {
            var t = new Tutorial();
            string reason = "?";
            int n = 0;
            t.HintDue += (s, l, r) => { n++; if (l == 2) reason = r; };
            for (int i = 0; i < 600; i++) t.Tick(0.1f);
            Assert.IsTrue(t.HintsQuiet);
            n = 0;
            t.NoteNearMiss("palm not down");
            Assert.IsFalse(t.HintsQuiet, "a near-miss attempt is player activity");
            for (int i = 0; i < 260; i++) t.Tick(0.1f);
            Assert.AreEqual(2, n);
            Assert.AreEqual("palm not down", reason);
        }

        [Test]
        public void RestartIdle_StartsTheHintTimerFromZero_AndAllowsOneCycle()
        {
            var t = new Tutorial();
            float now = 0f;
            var hints = Record(t, () => now);
            for (int i = 0; i < 90; i++) { now += 0.1f; t.Tick(0.1f); }
            t.RestartIdle();
            float back = now;
            for (int i = 0; i < 90; i++) { now += 0.1f; t.Tick(0.1f); }
            Assert.IsEmpty(hints, "9 s + 9 s: the clock restarted");
            for (int i = 0; i < 600; i++) { now += 0.1f; t.Tick(0.1f); }
            CollectionAssert.AreEqual(new[] { 1, 2 }, hints.Select(h => h.level).ToArray(), "one cycle after coming back, not the whole escalation");
            Assert.That(hints[0].at - back, Is.EqualTo(Tutorial.Hint1Seconds).Within(0.15f));
            Assert.IsTrue(t.HintsQuiet);
        }

        [Test]
        public void ANewStep_GetsTheFullHintBudgetAgain()
        {
            var t = new Tutorial();
            var levels = new List<int>();
            t.HintDue += (s, l, _) => levels.Add(l);
            for (int i = 0; i < 600; i++) t.Tick(0.1f);
            Assert.IsTrue(t.HintsQuiet);
            t.Report(TutorialAction.Peeked);
            Assert.IsFalse(t.HintsQuiet);
            levels.Clear();
            for (int i = 0; i < 600; i++) t.Tick(0.1f);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, levels);
        }

        [Test]
        public void Hint2_UsesTheLatestNearMissReason_AndANewStepStartsFresh()
        {
            var t = new Tutorial();
            string reason = "?";
            t.HintDue += (s, l, r) => { if (l == 2) reason = r; };
            t.NoteNearMiss("palm too high");
            t.NoteNearMiss("palm not down");
            for (int i = 0; i < 260; i++) t.Tick(0.1f);
            Assert.AreEqual("palm not down", reason);
            t.Report(TutorialAction.Peeked);
            Assert.IsNull(t.LastNearMiss);
            Assert.AreEqual(0f, t.StepTime);
            Assert.AreEqual(0f, t.TotalStepTime);
            Assert.AreEqual(0, t.HintLevel);
        }

        [Test]
        public void Tips_AreSpecificToTheGestureAndReason()
        {
            Assert.AreEqual("Move your hand right over your cards.", Tutorial.Tip(TutorialStep.Peek, null));
            Assert.AreEqual("Rest your fingertips on the cards, then lift your wrist.", Tutorial.Tip(TutorialStep.Peek, "palm too high"));
            Assert.AreEqual("Turn your palm down over your cards.", Tutorial.Tip(TutorialStep.Peek, "palm not down"));
            Assert.AreEqual("Lift your wrist a little higher.", Tutorial.Tip(TutorialStep.Peek, "wrist not above fingertips"));
            Assert.AreEqual("Pinch right on the chips.", Tutorial.Tip(TutorialStep.Decide, null));
            Assert.AreEqual("Pinch right on the chips.", Tutorial.Tip(TutorialStep.Decide, "pinch began away from the chips"));
            Assert.AreEqual("Toss them onto the glowing zone.", Tutorial.Tip(TutorialStep.Decide, Tutorial.ReasonOutsideZone));
            Assert.AreEqual("Toss your cards toward the middle.", Tutorial.Tip(TutorialStep.Decide, "fold: too slow"), "a near-miss fold gets the fold tip");
            Assert.AreEqual("Pinch right on the chips.", Tutorial.Tip(TutorialStep.Bet, null));
            Assert.AreEqual("Toss them onto the glowing zone.", Tutorial.Tip(TutorialStep.Bet, Tutorial.ReasonOutsideZone));
        }

        [Test]
        public void ReasonFromTrace_ReadsTheRecognizersNearMissLines()
        {
            Assert.AreEqual("palm not down", Tutorial.ReasonFromTrace(TutorialStep.Peek,
                "3.210 peek near-miss Right: palm h 0.050 (max 0.090), tip h 0.010 (max 0.030), wrist-tip 0.010 (0 at 0.015, 1 at 0.050), angle 40 (down ≥55, hold ≥35), normal.y 0.77, to cards center 0.020 (tip 0.030), armed False, reason palm not down"));
            Assert.AreEqual("pinch began away from the chips", Tutorial.ReasonFromTrace(TutorialStep.Decide,
                "4.000 chip near-miss Right: strength 0.60 (start ≥0.70 or ≥0.40 with gap ≤0.020), tip gap 0.030 (touch ≤0.015), flag False, pinching True, nearest column 0.050 (limit 0.040), zone 0.070 (limit 0.060), height 0.030 (max 0.080), reason pinch began away from the chips (pinch started 0.30 s ago)"));
            Assert.AreEqual("pinch below start", Tutorial.ReasonFromTrace(TutorialStep.Bet,
                "4.000 chip near-miss Left: strength 0.30 (start ≥0.70 or ≥0.40 with gap ≤0.020), tip gap 0.030 (touch ≤0.015), flag False, pinching False, nearest column none, zone 0.010 (limit 0.060), height 0.030 (max 0.080), reason pinch below start"));
            Assert.AreEqual("fold: lifted", Tutorial.ReasonFromTrace(TutorialStep.Decide,
                "6.000 fold-push reset Right: lifted (palm 0.00,0.120,0.10 tip 0.090, from 0.00,0.05)"));
            Assert.IsNull(Tutorial.ReasonFromTrace(TutorialStep.Bet, "6.000 fold-push reset Left: too slow (palm z 0.10, from 0.05)"), "hand 2 never folds");
            Assert.IsNull(Tutorial.ReasonFromTrace(TutorialStep.Peek, "4.000 chip near-miss Right: reason pinch below start"), "a chip miss says nothing about peeking");
            Assert.IsNull(Tutorial.ReasonFromTrace(TutorialStep.Decide, null));
        }

        [Test]
        public void SkipAll_EndsTheWholeTutorial_FromAnyStep()
        {
            var t = new Tutorial();
            t.Report(TutorialAction.Peeked);
            t.SkipAll();
            Assert.IsFalse(t.IsActive);
            Assert.IsTrue(t.SkippedByPlayer);
            Assert.IsFalse(t.Report(TutorialAction.Called));
        }

        [Test]
        public void EveryLine_IsListedForBaking_WithUniqueIds_AndReusesBakedIds()
        {
            var lines = Tutorial.Lines().ToList();
            CollectionAssert.AllItemsAreUnique(lines.Select(l => l.id));
            foreach (var s in AllSteps)
                Assert.IsTrue(lines.Any(l => l.text == Tutorial.Prompt(s)) || FoolRocco.Lines().Any(l => l.Text == Tutorial.Prompt(s)), s + " prompt is baked");
            Assert.AreEqual(FoolRocco.DotCoach.Text, Tutorial.Prompt(TutorialStep.Bet), "hand 2 reuses the Fool Rocco coaching line (already baked)");
            Assert.IsFalse(lines.Any(l => l.text == FoolRocco.DotCoach.Text), "listed once, under its Fool Rocco id");
            Assert.IsTrue(lines.Any(l => l.id == "dot.tutorial.peek" && l.text == Tutorial.Prompt(TutorialStep.Peek)), "the v3 peek line keeps its id (no re-bake)");
            foreach (var r in new[] { null, "palm too high", "palm not down", "hand beside the cards", "wrist not above fingertips", Tutorial.ReasonOutsideZone, "fold: lifted", "pinch below start" })
                foreach (var s in AllSteps)
                    Assert.IsTrue(lines.Any(l => l.text == Tutorial.Tip(s, r)), s + "/" + r);
            Assert.IsTrue(lines.Any(l => l.text == Tutorial.Finished));
            Assert.IsTrue(lines.Any(l => l.text == Tutorial.SkipLine));
        }

        [Test]
        public void EveryStep_HasAGhostAndAGlow()
        {
            CollectionAssert.AreEqual(new[] { GhostGesture.Peek }, Tutorial.GhostsFor(TutorialStep.Peek));
            CollectionAssert.AreEqual(new[] { GhostGesture.Bet, GhostGesture.Fold }, Tutorial.GhostsFor(TutorialStep.Decide), "call or fold: both are shown");
            CollectionAssert.AreEqual(new[] { GhostGesture.Bet }, Tutorial.GhostsFor(TutorialStep.Bet));
            Assert.AreEqual(TutorialGlow.Cards, Tutorial.GlowFor(TutorialStep.Peek));
            Assert.AreEqual(TutorialGlow.ChipsAndBetZone, Tutorial.GlowFor(TutorialStep.Decide));
            Assert.AreEqual(TutorialGlow.ChipsAndBetZone, Tutorial.GlowFor(TutorialStep.Bet));
        }

        [Test]
        public void Gate_OnlyTheCurrentStepsAction_ButLookingAlways()
        {
            var t = new Tutorial();
            foreach (var k in new[] { GestureKind.PeekChanged, GestureKind.CardsLifted, GestureKind.CardsReturned }) Assert.IsTrue(t.Allows(k), k.ToString());
            foreach (var k in new[] { GestureKind.BetCommitted, GestureKind.KnockCheck, GestureKind.FoldPushed, GestureKind.AllInConfirmed, GestureKind.ShowdownFlip })
                Assert.IsFalse(t.Allows(k), "peek step: " + k);
            t.Report(TutorialAction.Peeked);
            foreach (var k in new[] { GestureKind.ChipsPicked, GestureKind.ChipsReleased, GestureKind.BetCommitted, GestureKind.BetRetracted, GestureKind.FoldPushed })
                Assert.IsTrue(t.Allows(k), "decide: " + k);
            foreach (var k in new[] { GestureKind.KnockCheck, GestureKind.AllInConfirmed, GestureKind.ShowdownFlip }) Assert.IsFalse(t.Allows(k), "decide: " + k);
            t.Report(TutorialAction.Called);
            Assert.IsTrue(t.Allows(GestureKind.BetCommitted));
            Assert.IsFalse(t.Allows(GestureKind.FoldPushed), "hand 2: bet only");
            Assert.IsFalse(t.Allows(GestureKind.KnockCheck));
        }

        [Test]
        public void HandOf_SplitsTheLessonIntoTwoHands()
        {
            Assert.AreEqual(1, Tutorial.HandOf(TutorialStep.Peek));
            Assert.AreEqual(1, Tutorial.HandOf(TutorialStep.Decide));
            Assert.AreEqual(2, Tutorial.HandOf(TutorialStep.Bet));
        }

        // ================= 腳本兩手（Core 直接跑，不經畫面） =================

        static HandState Start(int hand, int[] stacks)
        {
            var r = Engine.Apply(HandState.NewTable(stacks), new StartHand(7, TutorialScript.ButtonFor(stacks.Length), 5, 10, TutorialScript.Preset(hand)));
            Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
            return r.State;
        }

        /// <summary>跑完一手：玩家第一次輪到時做 first，之後一律過牌（或跟）。回傳最後的狀態與每個人的動作。</summary>
        /// <summary>seats：4 人桌（正式版）或 5 人桌（寫實預覽，荷官右側多一位老手，2026-11-02）。</summary>
        static HandState Run(int hand, PlayerCommand first, bool roccoCalls, List<(int seat, PlayerCommand c, Street st)> log, int seats = 4)
        {
            var s = Start(hand, Enumerable.Repeat(1000, seats).ToArray());
            bool decided = false;
            int guard = 0;
            while (s.Phase == HandPhase.Betting && guard++ < 60)
            {
                PlayerCommand c;
                if (s.ToAct == 0)
                {
                    var la = Engine.GetLegalActions(s, 0);
                    c = !decided ? first : la.CanCheck ? new Check(0) : (PlayerCommand)new Call(0);
                    decided = true;
                }
                else c = TutorialScript.Ai(hand, s, s.ToAct, roccoCalls);
                var r = Engine.Apply(s, c);
                Assert.IsTrue(r.IsSuccess, c + " " + r.Error);
                log.Add((s.ToAct, c, s.Street));
                s = r.State;
            }
            Assert.AreEqual(HandPhase.Idle, s.Phase);
            return s;
        }

        [TestCase(4), TestCase(5)]
        public void Hand1_PipAndVexFold_RoccoRaisesTo30_YouCallAndWin_OrFold(int seats)
        {
            Assert.IsTrue(TutorialScript.Supports(seats));
            var log = new List<(int seat, PlayerCommand c, Street st)>();
            var s = Run(1, new Call(0), false, log, seats);
            Assert.AreEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), new[] { s.Seat(0).Hole1, s.Seat(0).Hole2 });
            // 按鈕在 Rocco 座（4 人）／老手座（5 人）：Vex 小盲、玩家大盲，玩家和 Rocco 的行動順序兩種桌一樣
            Assert.AreEqual(TutorialScript.ButtonFor(seats), s.Button);
            Assert.AreEqual(seats - 1, TutorialScript.VexSeatFor(seats));
            for (int seat = 1; seat < seats; seat++)
                if (seat != TutorialScript.ReadSeat) Assert.IsTrue(log.Any(x => x.seat == seat && x.c is Fold), "seat " + seat + " folds (Pip, the veteran, Vex)");
            Assert.IsTrue(log.Any(x => x.seat == TutorialScript.PipSeat && x.c is Fold), "Pip folds");
            Assert.IsTrue(log.Any(x => x.seat == TutorialScript.VexSeatFor(seats) && x.c is Fold), "Vex folds");
            Assert.IsTrue(log.Any(x => x.seat == TutorialScript.ReadSeat && x.c is Raise r && r.ToAmount == 30), "Rocco raises to 30 (two chips for the big blind to call)");
            Assert.IsFalse(log.Any(x => x.seat == TutorialScript.ReadSeat && x.st != Street.Preflop && !(x.c is Check)), "Rocco only checks after the flop");
            Assert.AreEqual(1000 + 30 + 5, s.Seat(0).Stack, "pair of nines beats king high: the player wins Rocco's 30 and Vex's small blind");

            var fold = Run(1, new Fold(0), false, new List<(int, PlayerCommand, Street)>(), seats);
            Assert.AreEqual(1000 - 10, fold.Seat(0).Stack, "folding costs only the big blind");
        }

        [TestCase(4), TestCase(5)]
        public void Hand1_ARaiseIsCalled_ThenItChecksDown(int seats)
        {
            var log = new List<(int seat, PlayerCommand c, Street st)>();
            var s = Run(1, new Raise(0, 60), false, log, seats);
            Assert.IsTrue(log.Any(x => x.seat == TutorialScript.ReadSeat && x.c is Call), "Rocco calls the re-raise");
            Assert.Greater(s.Seat(0).Stack, 1000);
        }

        [TestCase(4), TestCase(5)]
        public void Hand2_RoccoLimps_YouBet_HeCallsIfYouWaited_AndLoses_OrFolds(int seats)
        {
            var log = new List<(int seat, PlayerCommand c, Street st)>();
            var s = Run(2, new Raise(0, 40), true, log, seats);
            Assert.AreEqual(Card.ParseMany(TutorialScript.Hand2PlayerHole), new[] { s.Seat(0).Hole1, s.Seat(0).Hole2 });
            Assert.IsInstanceOf<Call>(log.First(x => x.seat == TutorialScript.ReadSeat).c, "Rocco limps first");
            Assert.IsTrue(log.Count(x => x.seat == TutorialScript.ReadSeat && x.c is Call) == 2, "…and calls the bet when he reads it as a bluff");
            Assert.AreEqual(1000 + 40 + 5, s.Seat(0).Stack, "aces beat Rocco's pair of kings");

            var log2 = new List<(int seat, PlayerCommand c, Street st)>();
            var f = Run(2, new Raise(0, 40), false, log2, seats);
            Assert.IsTrue(log2.Any(x => x.seat == TutorialScript.ReadSeat && x.c is Fold), "a quick bet: Rocco folds");
            Assert.AreEqual(1000 + 10 + 5, f.Seat(0).Stack);
        }

        [Test]
        public void Script_UsesTheFoolRoccoMonster_AndOnlyRunsAtAFourSeatTable()
        {
            Assert.AreEqual(FoolRocco.Hand2PlayerHole, TutorialScript.Hand2PlayerHole);
            Assert.AreEqual(FoolRocco.Hand2Board, TutorialScript.Hand2Board);
            Assert.IsTrue(TutorialScript.Supports(4));
            Assert.IsFalse(TutorialScript.Supports(2));
            Assert.AreEqual(HandHint.Decent, HandStrength.Classify(Card.Parse("Ah"), Card.Parse("9c"), new Card[0], 3), "hand 1 starts as a decent hand");
            Assert.AreEqual(HandHint.Strong, HandStrength.Classify(Card.Parse("As"), Card.Parse("Ah"), new Card[0], 3), "hand 2 is a monster");
        }
    }
}
