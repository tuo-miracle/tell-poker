using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
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
    /// 端到端（2026-10-14 live talk）：玩家的回合中，用合成手勢想一下、伸手又縮回、再下注——
    /// AI／荷官在下注「之前」就開口（一次一個人、不重疊、每句都有預錄聲音＋同一句字幕）；玩家講話時不接話；教學 v4 只有決定那一刻 Rocco 會讀你；
    /// 一手結束顯示「Rocco read you: …」，[Read] log 寫出判斷與影響。
    /// </summary>
    public class LiveTalkFlowTests
    {
        GameTestRig _rig;
        readonly List<(double t, string who, Utterance u)> _started = new List<(double, string, Utterance)>();
        readonly List<LiveDecision> _decisions = new List<LiveDecision>();
        int _overlaps;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            _started.Clear();
            _decisions.Clear();
            _overlaps = 0;
        }

        static readonly TableLayout L = new TableLayout();
        static NV3 OverChips => SyntheticHands.PalmForPinch(Hand.Right, L.ChipStackCenter + new NV3(0, 0.02f, 0)) + new NV3(0, 0.04f, 0);

        GameController NewGame(System.Action<GameController> extra = null, float boost = 3f)
        {
            _rig = new GameTestRig(new GameConfig { Seed = 41, MonteCarloTrials = 40 }, extra);
            var g = _rig.Game;
            g.LiveTalkBoost = boost;
            foreach (var c in g.AllCharacters())
            {
                var ch = c.Speech;
                ch.Started += u =>
                {
                    foreach (var o in g.AllCharacters())
                        if (o.Speech != ch && o.Speech.IsSpeaking && !u.IsStream) _overlaps++;
                    _started.Add((g.Now, ch.Who, u));
                };
            }
            g.LiveTalkDecided += d => _decisions.Add(d);
            return g;
        }

        /// <summary>伸手到籌碼上、停一下、沒捏就縮回桌邊。</summary>
        static SyntheticHands ReachAndRetreat()
        {
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(OverChips)).Wait(0.7);
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest + new NV3(0.04f, 0f, -0.03f))).Wait(0.8);
            return s;
        }

        void TossChips(int n)
        {
            float lift = n <= 1 ? 0.005f : 0.025f * (n - 1) + 0.012f;
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: lift);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.0f, 0.02f, 0.40f));
            s.Wait(2.0);
            _rig.Play(s);
        }

        IEnumerable<(double t, string who, Utterance u)> LiveLinesSpoken =>
            _started.Where(x => x.u.LineId != null && x.u.LineId.Contains(".live."));

        [Test]
        public void DuringThePlayersTurn_LiveLinesFireBeforeTheBet_OneSpeakerAtATime_AllWithAudio()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[LiveTalk\] t=\d+\.\d signal=\w+(\(\d+\))?(@\d+)? -> (pip|rocco|vex|dot) line=(pip|rocco|vex|dot)\.live\.\S+$"));
            var g = NewGame();
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            int handNo = g.Session.State.HandNumber;
            int actionsBefore = _rig.Actions.Count(a => a.seat == 0);
            _rig.Idle(4.0);                 // 想了 3 秒
            _rig.Play(ReachAndRetreat());   // 伸手又縮回
            _rig.Idle(4.5);                 // 想了 7 秒
            Assert.AreEqual(GameFlow.PlayerTurn, g.Flow, "still the player's turn\n" + _rig.Dump());
            var beforeBet = LiveLinesSpoken.ToList();
            Assert.IsNotEmpty(beforeBet, "AIs talk while the player is still deciding: " + string.Join(" | ", _decisions.Select(d => d.Signal + ":" + (d.Spoke ? d.Who : d.Skip))));
            CollectionAssert.IsSupersetOf(_decisions.Select(d => d.Signal.Cue).Distinct().ToList(), new[] { LiveCue.Think3, LiveCue.Reach, LiveCue.PullBack, LiveCue.Think7 }, "every signal reached the director");
            Assert.AreEqual(actionsBefore, _rig.Actions.Count(a => a.seat == 0), "nothing was bet yet");

            double betAt = g.Now;
            if (g.Session.Legal(0).CanCheck) _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            else TossChips(Mathf.Max(1, g.Session.Legal(0).CallAmount / g.Config.Denomination));
            Assert.IsTrue(_rig.Actions.Count(a => a.seat == 0) > actionsBefore, "the player acted\n" + _rig.Dump());
            Assert.IsTrue(beforeBet.All(x => x.t < betAt), "the lines came before the action");

            Assert.AreEqual(0, _overlaps, "never two characters talking at once");
            var times = _decisions.Where(d => d.Spoke).Select(d => d.Signal.Time).ToList();
            for (int i = 1; i < times.Count; i++) Assert.GreaterOrEqual(times[i] - times[i - 1], LiveTalkDirector.TableGap - 1e-6, "table gap");
            foreach (var x in LiveLinesSpoken)
            {
                Assert.AreNotEqual("none", x.u.ClipId, x.who + " \"" + x.u.Text + "\" has baked audio");
                Assert.IsTrue(LiveLines.All().Any(l => l.Id == x.u.LineId && l.Text == x.u.Text && l.Voice == x.who), "subtitle is exactly the line: " + x.u.Text);
            }
            Assert.AreEqual(handNo, g.Session.State.HandNumber);
        }

        [Test]
        public void WhileThePlayerIsTalking_NobodyTalksOverThem()
        {
            var g = NewGame();
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            g.PlayerSpeaking = true;
            _rig.Idle(8.0);
            _rig.Play(ReachAndRetreat());
            Assert.IsEmpty(LiveLinesSpoken.ToList(), string.Join(" | ", LiveLinesSpoken.Select(x => x.u.Text)));
            Assert.IsTrue(_decisions.Any(d => d.Skip == "player speaking"));
            Assert.IsTrue(_decisions.Any(d => d.Signal.Cue == LiveCue.PlayerSpoke), "speaking is itself a signal");
            g.PlayerSpeaking = false;
            _rig.Idle(5.0);
            Assert.IsNotEmpty(_decisions.Where(d => d.Signal.Cue == LiveCue.Think12).ToList(), "the 12 s mark still arrives after they stop");
        }

        /// <summary>
        /// 教學 v4（2026-10-14 改）：v3 的教學那一手完全不講（教學的話優先）；v4 的高潮就是「Rocco 讀你」，
        /// 所以第 1 手玩家在決定時只有 Rocco 會開口（保證至少一句），Dot 的教學句講完才接；看牌那一步、決定之後都不講。
        /// </summary>
        [Test]
        public void TheTutorial_OnlyRoccoReadsYou_AndOnlyDuringTheDecision()
        {
            var path = Path.Combine(Path.GetTempPath(), "tell_live_tut_" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                _rig = null;
                var g = NewGame(c => { c.ProgressPath = path; c.TutorialEnabled = true; c.Config = new GameConfig { Seed = 31, MonteCarloTrials = 40 }; }, boost: 1f);
                Assert.IsTrue(g.Tutorial.IsActive);
                Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Peek), 10), "peek prompt\n" + _rig.Dump());
                _rig.Play(GestureScripts.Peek(new SyntheticHands(), Hand.Right, hold: 1.0));
                Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step, _rig.Dump());
                Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn, 30), _rig.Dump());
                // Dot 講完決定那一句之前 Rocco 不搶話；講完之後他就讀你剛剛的猶豫
                Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Tutorial.Prompt(TutorialStep.Decide) && !g.Dealer.Speech.IsSpeaking && g.Dealer.Speech.QueuedCount == 0, 20), _rig.Dump());
                _rig.Idle(3.0);
                _rig.Play(ReachAndRetreat());
                var live = LiveLinesSpoken.ToList();
                Assert.IsNotEmpty(live, "Rocco reads you during the tutorial decision\n" + string.Join(",", _decisions.Select(d => d.Signal + ":" + d.Skip)));
                Assert.IsTrue(live.All(x => x.who == "rocco"), "only Rocco: " + string.Join(" | ", live.Select(x => x.who + ": " + x.u.Text)));
                Assert.AreEqual(0, _overlaps, "never over Dot's lesson line");
                Assert.AreEqual(TutorialStep.Decide, g.Tutorial.Step, "talk doesn't move the lesson");
                TossChips(2);
                Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Call), "called\n" + _rig.Dump());
                bool Closing((double t, string who, Utterance u) x) => x.u.LineId.Contains(".live.right.") || x.u.LineId.Contains(".live.fooled.");
                int after = LiveLinesSpoken.Count(x => !Closing(x));
                Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.HandOver, 40), _rig.Dump());
                Assert.AreEqual(after, LiveLinesSpoken.Count(x => !Closing(x)), "no live needling after the decision (the game checks for you; only the closing line at the showdown)");
            }
            finally
            {
                foreach (var f in new[] { path, path + ".tmp", path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
            }
        }

        [Test]
        public void AfterTheHand_TheAiShowsWhatItReadFromYou()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Read\] (Pip|Rocco|Vex) belief=weak from (LongThink|ReLook|ReachRetreat|Fiddle|TowardCards)(\+\w+)* effect=\+0\.\d{3} "));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Read\] reveal (Pip|Rocco|Vex): (Pip|Rocco|Vex) read you: weak"));   // 2026-11-03 揭曉卡縮成一行
            var g = NewGame(boost: 1f);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.Idle(7.5);
            _rig.Play(ReachAndRetreat());
            // 跟注（或過牌）之後交給自動玩家打完：只看 AI 怎麼讀這一次
            if (g.Session.Legal(0).CanCheck) _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            else TossChips(Mathf.Max(1, g.Session.Legal(0).CallAmount / g.Config.Denomination));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0), _rig.Dump());
            var sent = _rig.Signals.Last().s;
            Assert.IsTrue(sent.Live, "the turn's live signals went to the AIs");
            Assert.GreaterOrEqual(sent.PullBacks, 1);
            Assert.GreaterOrEqual(sent.ThinkSeconds, 7f);
            g.AutoPlayer = st => { var la = Engine.GetLegalActions(st, 0); return la.CanCheck ? (PlayerCommand)new Check(0) : new Fold(0); };
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.HandOver, 60), _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => g.ReadReveal != null, 2), "reveal card shown");
            StringAssert.Contains(" read you: ", g.ReadReveal);
        }

        /// <summary>
        /// 2026-11-04 實機錄影：一局最後一手，「Rocco read you」揭曉卡和結算看板同時出現（都在一手結束那一步）。
        /// 揭曉卡跟攤牌牌子一起出來，荷官講完分池，最後才出結算看板。
        /// </summary>
        [Test]
        public void LastHand_TheReadRevealComesBeforeTheSummary()
        {
            var g = NewGame(x => x.Config.Schedule.MaxHands = 1, boost: 1f);   // 這一手就是這一局的最後一手
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.Idle(7.5);
            _rig.Play(ReachAndRetreat());
            if (g.Session.Legal(0).CanCheck) _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            else TossChips(Mathf.Max(1, g.Session.Legal(0).CallAmount / g.Config.Denomination));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0), _rig.Dump());
            // 下一次輪到就 all-in；AI 照常決定（用得上剛剛的判斷）。MaxHands = 1：不管誰贏，這一手打完就出結算
            g.AutoPlayer = st => new AllIn(0);
            int frame = 0, revealAt = -1, summaryAt = -1, handOverAt = -1;
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                frame++;
                if (revealAt < 0 && g.ReadReveal != null) revealAt = frame;
                if (summaryAt < 0 && g.Props.SummaryVisible) summaryAt = frame;
                if (handOverAt < 0 && (g.Flow == GameFlow.HandOver || g.Flow == GameFlow.GameOver)) handOverAt = frame;
                return handOverAt >= 0 && frame > handOverAt + 72;
            }, 90), _rig.Dump());
            Debug.Log($"[test] reveal@{revealAt} summary@{summaryAt} handover@{handOverAt} flow={g.Flow} stacks={string.Join(",", g.Session.State.Stacks())}");
            Assume.That(revealAt >= 0 && summaryAt >= 0, "this seed must end the game with a read reveal");
            Assert.Greater(summaryAt, revealAt, "reveal first, summary last");
        }
    }
}
