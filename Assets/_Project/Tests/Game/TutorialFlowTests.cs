using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using UnityEngine.TestTools;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 端到端（教學 v4，2026-10-14 改寫）：短教學，高潮是核心玩法「雙向讀心」——
    /// 第 1 手：看牌（咪牌或拿起來）→ 牌力提示＋Dot 第一次小聲說 → Rocco 加注 → 玩家還在猶豫／伸手時 Rocco 就用即時系統嗆（保證至少一句）
    ///         → 跟或棄（都算）→ 一手結束揭曉「Rocco read you: …」；
    /// 第 2 手：拿到 AA，Dot 教「等幾秒再下注」→ 等了：Rocco 跟、輸、「You played me!」＋揭曉；沒等：Rocco 棄、Dot「Try waiting next time.」，照樣教完。
    /// 時間用合成手勢＋真實的 AI 思考時間（AiThinkScale 1）量，每個提示講完後玩家再反應 1.2 秒，報告在 log「[Tutorial] test: …」。
    /// v3 的測試（每個手勢各做一次、讀破綻那一步、加注要比跟注多、語音那一步、攤牌要自己翻）都隨 v3 的步驟刪掉；
    /// 不自動跳過、10／25 秒升級提示、辨識器 trace 的具體建議、選單跳過、重看教學、壞存檔、一對一桌不教，照舊測。
    /// </summary>
    public class TutorialFlowTests
    {
        GameTestRig _rig;
        string _path;
        readonly List<(double t, string who, string text, string id)> _said = new List<(double, string, string, string)>();
        readonly List<string> _playtest = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "tell_tutorial_" + System.Guid.NewGuid().ToString("N") + ".json");
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig?.Dispose();
            _rig = null;
            _said.Clear();
            _playtest.Clear();
            foreach (var f in new[] { _path, _path + ".tmp", _path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("[Playtest]")) _playtest.Add(msg);
        }

        /// <summary>fiveHanded：寫實預覽的 5 人桌（荷官右側多一位老手，2026-11-02），試玩者會用這一版。</summary>
        GameTestRig NewRig(System.Action<GameController> extra = null, bool fiveHanded = false)
        {
            var cfg = new GameConfig { Seed = 31, MonteCarloTrials = 40 };
            if (fiveHanded) cfg.Opponents = GameConfig.FiveHanded;
            var rig = new GameTestRig(cfg, g =>
            {
                g.ProgressPath = _path;
                g.TutorialEnabled = true;
                extra?.Invoke(g);
            });
            var game = rig.Game;
            game.PlaytestClock = () => game.Now;
            game.Dealer.Speech.Started += u => _said.Add((game.Now, "dot", u.Text, u.LineId));
            game.Character(TutorialScript.ReadSeat).Speech.Started += u => _said.Add((game.Now, "rocco", u.Text, u.LineId));
            return rig;
        }

        bool DotSaid(string text) => _said.Any(s => s.who == "dot" && s.text == text);
        bool RoccoSaid(string text) => _said.Any(s => s.who == "rocco" && s.text == text);

        static Regex Log(string step, string what) => new Regex(@"^\[Tutorial\] version=4 step=" + step + " " + what + "$");

        /// <summary>捏 n 枚籌碼丟進下注區（每 2.5 cm 一階），等 1.5 s 撈回時間過去。</summary>
        void TossChips(int n)
        {
            float lift = n <= 1 ? 0.005f : 0.025f * (n - 1) + 0.012f;
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: lift);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.0f, 0.02f, 0.40f));
            s.Wait(2.0);
            _rig.Play(s);
            var picked = _rig.Gestures.LastOrDefault(e => e.Kind == GestureKind.ChipsReleased);
            Assert.AreEqual(n, picked.Count, "synthetic pinch picked the intended count\n" + _rig.Dump());
        }

        /// <summary>Dot 講完（加上真人反應時間）。</summary>
        void WaitDotThenReact(GameController g, double react = 1.2)
        {
            Assert.IsTrue(_rig.IdleUntil(() => !g.Dealer.Speech.IsSpeaking && g.Dealer.Speech.QueuedCount == 0, 20), "Dot finishes talking");
            _rig.Idle(react);
        }

        bool DecideTurn(GameController g, double max = 30) =>
            _rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Tutorial.Step == TutorialStep.Decide && DotSaid(Tutorial.Prompt(TutorialStep.Decide)), max);

        bool BetTurn(GameController g, double max = 40) =>
            _rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Tutorial.Step == TutorialStep.Bet && g.Session.HandsStarted == 2, max);

        /// <summary>第 1 手：從發牌到輪到玩家決定。look＝true 咪牌、false 拿起來看。</summary>
        void Hand1_LookAndFaceRoccosRaise(GameController g, bool peek)
        {
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Peek), 10), "peek prompt\n" + _rig.Dump());
            CollectionAssert.AreEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), g.Session.PlayerHole, "fixed deck");
            Assert.AreEqual(GameFlow.AiThinking == g.Flow || g.Flow == GameFlow.Presenting, true, "the table waits while the player looks");
            Assert.IsFalse(_rig.Actions.Any(), "no AI acted yet");
            Assert.IsNull(g.HandHintText, "no hint before the first look");
            WaitDotThenReact(g);
            if (peek) _rig.Play(GestureScripts.Peek(new SyntheticHands(), Hand.Right, hold: 1.0));
            else _rig.Play(GestureScripts.LiftCards(new SyntheticHands(), Hand.Right, hold: 0.8));
            Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step, (peek ? "peeked" : "picked up") + "\n" + _rig.Dump());
            Assert.AreEqual("Decent hand", g.HandHintText, "the hand-strength label appears at the first look");
            Assert.IsTrue(_rig.IdleUntil(() => DotSaid(DealerLines.HandHintWhisper(HandHint.Decent)), 4), "Dot whispers it the first time\n" + string.Join(" | ", _said.Select(s => s.text)));

            Assert.IsTrue(DecideTurn(g), "decide prompt at the player's turn\n" + _rig.Dump());
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == TutorialScript.PipSeat && a.cmd is Fold), "Pip folds");
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == TutorialScript.ReadSeat && a.cmd is Raise r && r.ToAmount == 30), "Rocco raises\n" + _rig.Dump());
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == TutorialScript.VexSeatFor(g.Session.SeatCount) && a.cmd is Fold), "Vex folds");
            if (g.Session.SeatCount == 5) Assert.IsTrue(_rig.Actions.Any(a => a.seat == 3 && a.cmd is Fold), "the veteran folds");
            Assert.That(g.GhostShown, Is.EqualTo(GhostGesture.Bet).Or.EqualTo(GhostGesture.Fold), "call-or-fold ghost");
        }

        [TestCase(false), TestCase(true)]
        public void Waited_FoolsRocco_TheWholeLessonTakesUnderThreeMinutes(bool fiveHanded)
        {
            foreach (var step in new[] { "Peek", "Decide", "Bet" })
            {
                LogAssert.Expect(LogType.Log, Log(step, "start"));
                LogAssert.Expect(LogType.Log, Log(step, "done"));
            }
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] finished, saved$"));
            _rig = NewRig(c => c.AiThinkScale = 1f, fiveHanded);
            var g = _rig.Game;
            double t0 = g.Now;
            Assert.IsNotNull(g.Tutorial);

            Hand1_LookAndFaceRoccosRaise(g, peek: true);
            // 玩家聽完 Dot、想一下：Rocco 在你還沒動手之前就開口（即時讀人，教學這一手保證有；Dot 講話時先等，不搶話）
            double turnAt = g.Now;
            WaitDotThenReact(g, 0.5);
            _rig.Idle(3.0);
            var live = _said.Where(s => s.who == "rocco" && s.id != null && s.id.StartsWith("rocco.live.") && s.t >= turnAt).ToList();
            Assert.IsNotEmpty(live, "a live read line before the player acts\n" + string.Join(" | ", _said.Select(s => s.who + ": " + s.text)));
            TossChips(2);
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Call), "called\n" + _rig.Dump());
            Assert.AreEqual(TutorialStep.Bet, g.Tutorial.Step);
            // 之後的街由遊戲替玩家過牌（Rocco 也只過牌）；翻牌後牌力提示更新
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.HandOver, 40), "hand 1 checks down\n" + _rig.Dump());
            Assert.AreEqual(new[] { HandHint.Decent, HandHint.Strong }, g.HandHintHistory.Take(2).ToArray(), "top pair on the flop: the label updates on the new street");
            // 2026-11-03 揭曉卡縮成一行：「Rocco read you: weak — right」（拿掉推理鏈）
            StringAssert.StartsWith("Rocco read you: ", g.ReadReveal, "end-of-hand reveal");
            Assert.Greater(g.Session.StackOf(0), 1000, "pair of nines wins hand 1");

            // 第 2 手：Dot 先教，教完 Rocco 才動
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 2, 20), "hand 2\n" + _rig.Dump());
            Assert.IsNull(g.ReadReveal, "the reveal card is gone");
            CollectionAssert.AreEqual(Card.ParseMany(TutorialScript.Hand2PlayerHole), g.Session.PlayerHole, "a monster");
            Assert.IsTrue(_rig.IdleUntil(() => DotSaid(FoolRocco.DotCoach.Text), 8), "Dot coaches the fake tell");
            Assert.IsFalse(_rig.Actions.Any(a => a.seat == TutorialScript.ReadSeat && a.cmd is Call), "Rocco waits for the coaching");
            Assert.IsTrue(BetTurn(g), _rig.Dump());
            Assert.AreEqual(1, _said.Count(s => s.text == FoolRocco.DotCoach.Text), "the coaching isn't repeated at the turn");
            _rig.Idle(3.6);   // 等幾秒（Dot 教的招）
            TossChips(4);
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Raise), "bet\n" + _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => RoccoSaid(FoolRocco.RoccoCall.Text), 5), "Rocco reads a bluff\n" + string.Join(" | ", _said.Select(s => s.text)));
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.HandOver, 40), _rig.Dump());
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == TutorialScript.ReadSeat && a.cmd is Call && g.Session.HandsStarted == 2), "Rocco called");
            Assert.AreEqual(FoolRocco.RevealFooled, g.ReadReveal);
            Assert.IsTrue(_rig.IdleUntil(() => RoccoSaid(FoolRocco.RoccoPlayed.Text) && DotSaid(FoolRocco.DotFooled.Text), 10), "You played me! / You fooled him.");
            Assert.IsTrue(_rig.IdleUntil(() => DotSaid(Tutorial.Finished), 15), "finished");
            double took = g.Now - t0;
            Debug.Log($"[Tutorial] test: v4 lesson (waited → fooled) took {took:0.0} s of game time");
            Assert.Less(took, 180.0, "≤ 3 minutes for a competent player");

            // 存檔：v4、教學的兩手不算戰績
            var disk = new ProgressStore(_path).Load();
            Assert.AreEqual(4, disk.TutorialVersion);
            Assert.AreEqual(0, disk.HandsPlayed, "tutorial hands aren't stats");
            Assert.AreEqual(0, disk.GamesPlayed);
            CollectionAssert.IsEmpty(disk.Gestures, "lesson gestures don't count toward hint suppression");
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 3, 15), "then the real game starts");
            for (int s = 0; s < g.Session.SeatCount; s++)
                Assert.AreEqual(1000, g.Session.State.Seat(s).Stack + g.Session.State.Seat(s).Committed, "fresh stacks for seat " + s);
            Assert.AreEqual(0, g.Session.Stats.Hands.Count);

            // 評估用的 log
            Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"^\[Playtest\] t=\d+\.\d event=tutorial_step step=Peek state=start")), string.Join("\n", _playtest));
            Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"^\[Playtest\] t=\d+\.\d event=hint label=decent street=preflop")));
            Assert.AreEqual(1, _playtest.Count(l => l.Contains("event=first_bet")), "first bet logged once");
            Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"event=first_bet tutorial=1 action=call")));
            Assert.IsTrue(_playtest.Any(l => l.Contains("event=read_reveal who=Rocco")));
            Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"event=fooled who=Rocco where=tutorial hand=2")));
            Assert.IsTrue(_playtest.Any(l => l.Contains("event=tutorial_step step=Done state=finished")));
        }

        [Test]
        public void Fast_RoccoFolds_TryWaitingNextTime_PickUpAndFold_StillFinishes()
        {
            _rig = NewRig(c => c.AiThinkScale = 1f);
            var g = _rig.Game;
            double t0 = g.Now;
            Hand1_LookAndFaceRoccosRaise(g, peek: false);
            WaitDotThenReact(g, 0.3);
            int before = _said.Count(s => s.who == "rocco");
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold), "folding is accepted\n" + _rig.Dump());
            Assert.AreEqual(TutorialStep.Bet, g.Tutorial.Step);
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.HandOver, 10));
            Assert.IsTrue(_rig.IdleUntil(() => _said.Count(s => s.who == "rocco") > before, 6), "Rocco still reads you this hand (guaranteed)\n" + string.Join(" | ", _said.Select(s => s.who + ": " + s.text)));
            StringAssert.StartsWith("Rocco read you: ", g.ReadReveal);
            Assert.IsNull(g.HandHintText, "the label goes away after folding");

            Assert.IsTrue(BetTurn(g), _rig.Dump());
            TossChips(4);   // 馬上下注（沒等）
            Assert.IsTrue(_rig.IdleUntil(() => RoccoSaid(FoolRocco.RoccoFold.Text), 5), "Quick bet. You've got it. I'm out.");
            Assert.IsTrue(_rig.IdleUntil(() => DotSaid(FoolRocco.DotRetry.Text), 15), "Try waiting next time.");
            Assert.AreEqual(FoolRocco.RevealQuick, g.ReadReveal);
            Assert.IsFalse(RoccoSaid(FoolRocco.RoccoPlayed.Text));
            Assert.IsTrue(_rig.IdleUntil(() => DotSaid(Tutorial.Finished), 15), "never trapped: the lesson still ends");
            Debug.Log($"[Tutorial] test: v4 lesson (fast → Rocco folds) took {g.Now - t0:0.0} s of game time");
            Assert.Less(g.Now - t0, 150.0);
            Assert.AreEqual(4, new ProgressStore(_path).Load().TutorialVersion);
            Assert.IsFalse(_playtest.Any(l => l.Contains("event=fooled")), "nobody fooled Rocco");
        }

        /// <summary>
        /// 實機（2026-11-05）：手一開始碰巧移到牌附近，等了 16～39 秒才下注，Rocco 還是說「Quick bet」。
        /// 「等了沒」看的是輪到你到下注落定，不是第一次伸手。
        /// </summary>
        [Test]
        public void Waited_AfterAnEarlyHandMoveNearTheCards_RoccoStillCalls()
        {
            _rig = NewRig(c => c.AiThinkScale = 1f);
            var g = _rig.Game;
            Hand1_LookAndFaceRoccosRaise(g, peek: false);
            WaitDotThenReact(g, 0.3);
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsTrue(BetTurn(g), _rig.Dump());

            var layout = new TableLayout();
            var drift = new SyntheticHands().Wait(0.3);
            drift.Move(Hand.Right, 0.4, HandTarget.At(layout.CardsCenter + new NV3(0, 0.05f, -0.02f))).Wait(1.5);
            drift.Wait(5.0);
            _rig.Play(drift);
            Assert.Less(g.Habits.ReachDelay, FoolRocco.SlowReachSeconds, "the early move latched the reach\n" + _rig.Dump());
            Assert.GreaterOrEqual(g.Habits.ReachDelay, 0f, "the early move latched the reach\n" + _rig.Dump());

            TossChips(4);
            Assert.IsTrue(_rig.IdleUntil(() => RoccoSaid(FoolRocco.RoccoCall.Text), 5), "Rocco reads the wait as weakness\n" + string.Join(" | ", _said.Select(s => s.text)));
            Assert.IsFalse(RoccoSaid(FoolRocco.RoccoFold.Text), "not a quick bet");
        }

        [Test]
        public void Hand1_ASnapCall_StillGetsARoccoReadLine()
        {
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Peek), 10));
            g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0.8f));
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Tutorial.Step == TutorialStep.Decide, 30), _rig.Dump());
            // 一輪到就丟（Dot 還在講）：即時台詞來不及，下注之後 Rocco 說他讀到什麼
            g.HandleGesture(new GestureEvent(GestureKind.BetCommitted, 0, Hand.Right, 2, 0f, new NV3(0f, 0f, 0.4f), true));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Call), _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => _said.Any(s => s.who == "rocco"), 8), "a read line from Rocco\n" + string.Join(" | ", _said.Select(s => s.text)));
        }

        [Test]
        public void StuckPlayer_GetsAReplayAt10s_ATipAt25s_AndIsNeverWalkedForward()
        {
            LogAssert.Expect(LogType.Log, Log("Peek", "start"));
            LogAssert.Expect(LogType.Log, Log("Peek", "hint1"));
            LogAssert.Expect(LogType.Log, Log(@"Peek", @"hint2 \(no attempt\)"));
            _rig = NewRig();
            var g = _rig.Game;
            var peek = Tutorial.Prompt(TutorialStep.Peek);
            var tip = Tutorial.Tip(TutorialStep.Peek, null);
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == peek, 10));
            double promptAt = g.Now;
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.History.Count(t => t == peek) == 2, 30), "the line is repeated\n" + string.Join(" | ", g.Voice.History));
            Assert.That(g.Now - promptAt, Is.InRange(9.5, 20.0), "≈10 s after the prompt (not counting Dot's own speech)");
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.Said(tip), 30), "then a specific tip");
            Assert.That(g.Now - promptAt, Is.InRange(24.5, 40.0));
            _rig.Idle(120);
            Assert.AreEqual(TutorialStep.Peek, g.Tutorial.Step, "never skipped");
            Assert.IsFalse(_rig.Actions.Any(), "nobody acts for the player");
            Assert.AreEqual(GhostGesture.Peek, g.GhostShown, "ghost hands keep showing");
            Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"^\[Playtest\] t=\d+\.\d event=stuck\(Peek,10\)$")), string.Join("\n", _playtest));
            Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"event=stuck\(Peek,25\)$")));
        }

        [Test]
        public void NearMiss_FromTheRecognizerTrace_PicksTheTip()
        {
            LogAssert.Expect(LogType.Log, Log("Peek", @"hint2 \(palm not down\)"));
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Peek), 10));
            g.Recognizer.Trace("3.210 peek near-miss Right: palm h 0.050 (max 0.090), tip h 0.010 (max 0.030), wrist-tip 0.010 (0 at 0.015, 1 at 0.050), angle 40 (down ≥55, hold ≥35), normal.y 0.77, to cards center 0.020 (tip 0.030), armed False, reason palm not down");
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.Said("Turn your palm down over your cards."), 45), string.Join(" | ", g.Voice.History));

            // 決定那一步：捏錯地方 → 「Pinch right on the chips.」
            g.HandleGesture(new GestureEvent(GestureKind.CardsLifted, 0, Hand.Right));
            g.HandleGesture(new GestureEvent(GestureKind.CardsReturned, 0, Hand.Right));
            Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step);
            LogAssert.Expect(LogType.Log, Log("Decide", @"hint2 \(pinch began away from the chips\)"));
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Voice.LastText == Tutorial.Prompt(TutorialStep.Decide), 30), _rig.Dump());
            g.Recognizer.Trace("9.000 chip near-miss Right: strength 0.60 (start ≥0.70 or ≥0.40 with gap ≤0.020), tip gap 0.030 (touch ≤0.015), flag False, pinching True, nearest column 0.050 (limit 0.040), zone 0.070 (limit 0.060), height 0.030 (max 0.080), reason pinch began away from the chips");
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.Said("Pinch right on the chips."), 45), string.Join(" | ", g.Voice.History));
            Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step, "still the player's decision");
        }

        [Test]
        public void OutOfStepActions_AreBlockedAndExplained_LookingStillWorks()
        {
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Peek), 10));
            g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0.8f));
            Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step);
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Voice.LastText == Tutorial.Prompt(TutorialStep.Decide), 30), _rig.Dump());

            var before = g.Session.State;
            foreach (var k in new[] { GestureKind.KnockCheck, GestureKind.AllInConfirmed })
            {
                g.HandleGesture(new GestureEvent(k, 0, Hand.Right));
                Assert.AreSame(before, g.Session.State, k + " blocked");
                Assert.AreEqual(Tutorial.Prompt(TutorialStep.Decide), g.Voice.LastText, k + " explained");
                _rig.Idle(1.2);
            }
            g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0.6f));
            Assert.Greater(g.PlayerCard(0).PeekDegrees, 1f, "peek allowed any time");
            g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0f));
            // 教學不能一手輸太多：太大的注退回
            g.HandleGesture(new GestureEvent(GestureKind.BetCommitted, 0, Hand.Right, 90, 0f, new NV3(0f, 0f, 0.4f), true));
            Assert.AreSame(before, g.Session.State, "a huge raise is refused");
            Assert.AreEqual(DealerLines.SmallBetOnly, g.Voice.LastText);
            g.HandleGesture(new GestureEvent(GestureKind.BetCommitted, 0, Hand.Right, 5, 0f, new NV3(0f, 0f, 0.4f), true));
            Assert.AreEqual(TutorialStep.Bet, g.Tutorial.Step, "a raise is a fine decision too");
        }

        [Test]
        public void SkipTutorial_EndsTheLessonMidHand_SavesV4_AndDealsARealHand()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] skipped by player, saved$"));
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Peek), 10));
            g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0.8f));
            Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step);
            g.SkipTutorial();
            Assert.IsFalse(g.Tutorial.IsActive);
            Assert.IsTrue(g.Voice.Said(Tutorial.SkipLine));
            Assert.AreEqual(4, new ProgressStore(_path).Load().TutorialVersion, "saved right away");
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 2 && g.Flow != GameFlow.Presenting, 10), "a real hand\n" + _rig.Dump());
            CollectionAssert.AreNotEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), g.Session.PlayerHole);
            Assert.AreEqual(TutorialGlow.None, g.Props.TutorialGlowShown);
            for (int s = 0; s < g.Session.SeatCount; s++)
                Assert.AreEqual(1000, g.Session.State.Seat(s).Stack + g.Session.State.Seat(s).Committed, "fresh stacks for seat " + s);
            Assert.AreEqual(g.Session.TotalChips, g.DisplayedTotalChips);
            Assert.IsTrue(_playtest.Any(l => l.Contains("event=tutorial_step step=Done state=skipped")));
        }

        [Test]
        public void ReplayTutorial_TeachesAgainOnAFreshTable()
        {
            new ProgressStore(_path).Save(new ProgressData { TutorialDone = true, TutorialVersion = Tutorial.CurrentVersion });
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsNull(g.Tutorial);
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 1, 2));
            g.ReplayTutorial();
            Assert.IsNotNull(g.Tutorial);
            Assert.AreEqual(TutorialStep.Peek, g.Tutorial.Step);
            g.AutoPlayer = st => new Fold(0);
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 2, 60), "next hand\n" + _rig.Dump());
            g.AutoPlayer = null;
            CollectionAssert.AreEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), g.Session.PlayerHole, "the lesson hand again");
            for (int s = 0; s < g.Session.SeatCount; s++)
                Assert.AreEqual(1000, g.Session.State.Seat(s).Stack + g.Session.State.Seat(s).Committed, "the lesson always starts from 1000 each");
            Assert.AreEqual(10, g.Session.State.BigBlind, "and the first blind level (Rocco's raise is 30)");
            Assert.IsTrue(_rig.IdleUntil(() => g.GhostShown == GhostGesture.Peek, 10));
            Assert.IsTrue(g.Progress.TutorialDone, "replaying doesn't forget the first completion");
        }

        [Test]
        public void CorruptSave_StillStartsTheTutorial_NoException()
        {
            File.WriteAllText(_path, "{{{ not json");
            _rig = NewRig();
            Assert.IsNotNull(_rig.Game.Tutorial);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Props.TutorialGlowShown == TutorialGlow.Cards, 10));
        }

        [Test]
        public void HeadsUpTable_HasNoScenario_SoNoTutorial()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 40, Opponents = GameConfig.HeadsUp }, g =>
            {
                g.ProgressPath = _path;
                g.TutorialEnabled = true;
            });
            Assert.IsNull(_rig.Game.Tutorial, "the lesson needs Pip, Rocco and Vex");
        }
    }
}
