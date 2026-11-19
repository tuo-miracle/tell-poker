using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 端到端（2026-10-14 實機遙測）：頭盔放著、教學 v4 看牌那一步，Dot 每 12／19 秒輪流重講與給建議，講了 2 小時（一隻手都沒追蹤到）。
    /// 改成：提示最多兩輪就安靜（示範手、發光照舊）；玩家又有動作才再給一輪；沒有手 ≥ 5 s 或 App 失焦就暫停（不算卡住），
    /// 手回來從 0 重新計時、重講一次提示。正式牌局的即時台詞（想了 3／7／12 秒）與備援按鈕的「卡住」也要有手才算。
    /// </summary>
    public class IdleQuietFlowTests
    {
        GameTestRig _rig;
        string _path;
        readonly List<string> _logs = new List<string>();
        readonly List<string> _playtest = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "tell_idle_" + System.Guid.NewGuid().ToString("N") + ".json");
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig?.Dispose();
            _rig = null;
            _logs.Clear();
            _playtest.Clear();
            foreach (var f in new[] { _path, _path + ".tmp", _path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
        }

        void OnLog(string msg, string stack, LogType type)
        {
            _logs.Add(msg);
            if (msg.StartsWith("[Playtest]")) _playtest.Add(msg);
        }

        GameTestRig TutorialRig(System.Action<GameController> extra = null)
        {
            var rig = new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 40 }, g =>
            {
                g.ProgressPath = _path;
                g.TutorialEnabled = true;
                extra?.Invoke(g);
            });
            rig.Game.PlaytestClock = () => rig.Game.Now;
            return rig;
        }

        int Count(string text) => _rig.Game.Voice.History.Count(t => t == text);
        int Logged(string exact) => _logs.Count(l => l == exact);
        int LoggedMatching(string regex) => _logs.Count(l => Regex.IsMatch(l, regex));
        int Stuck => _playtest.Count(l => l.Contains(" event=stuck("));

        static double PlaytestTime(string line) =>
            double.Parse(Regex.Match(line, @"t=(\d+\.\d)").Groups[1].Value, CultureInfo.InvariantCulture);

        static readonly string Peek = Tutorial.Prompt(TutorialStep.Peek);
        static readonly string PeekTip = Tutorial.Tip(TutorialStep.Peek, null);

        /// <summary>右手在桌面上方滑過去再回來（玩家回來了、在動）。</summary>
        static SyntheticHands Wave()
        {
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest + new NV3(-0.15f, 0.06f, 0.15f)));
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.RightRest)).Wait(0.3);
            return s;
        }

        void WaitForPeekPrompt()
        {
            var g = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g.Voice.LastText == Peek, 10), _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => !g.Dealer.Speech.IsSpeaking && g.Dealer.Speech.QueuedCount == 0, 20));
        }

        [Test]
        public void Tutorial_HandsStillOnTheTable_TwoCyclesThenQuiet_GhostAndGlowStay_ActivityGivesOneMore()
        {
            _rig = TutorialRig();
            var g = _rig.Game;
            WaitForPeekPrompt();
            _rig.Idle(240);   // 手放著不動（有追蹤到）
            Assert.AreEqual(3, Count(Peek), "prompt + two replays\n" + string.Join(" | ", g.Voice.History));
            Assert.AreEqual(2, Count(PeekTip), "two tips");
            Assert.AreEqual(1, Logged("[Tutorial] hints quiet (Peek)"));
            Assert.AreEqual(4, Stuck, string.Join("\n", _playtest));
            Assert.AreEqual(GhostGesture.Peek, g.GhostShown, "ghost hands keep looping silently");
            Assert.AreEqual(TutorialGlow.Cards, g.Props.TutorialGlowShown, "the glow stays");
            Assert.AreEqual(TutorialStep.Peek, g.Tutorial.Step, "never skipped");

            // 玩家動了：再一輪（10 s 重講、25 s 建議），然後又安靜
            _rig.Play(Wave());
            _rig.Idle(45);
            Assert.AreEqual(4, Count(Peek), string.Join(" | ", g.Voice.History));
            Assert.AreEqual(3, Count(PeekTip));
            Assert.AreEqual(2, Logged("[Tutorial] hints quiet (Peek)"));
            _rig.Idle(120);
            Assert.AreEqual(4, Count(Peek), "quiet again");
            Assert.AreEqual(3, Count(PeekTip));
        }

        [Test]
        public void Tutorial_NoHands_PausesAfter5s_NoHintsOrStuck_BackReplaysThePromptOnce_TimerFromZero()
        {
            _rig = TutorialRig();
            var g = _rig.Game;
            WaitForPeekPrompt();
            double gone = g.Now;
            _rig.IdleNoHands(300);   // 頭盔放著
            Assert.AreEqual(1, Count(Peek), "no replays while nobody is there\n" + string.Join(" | ", g.Voice.History));
            Assert.AreEqual(0, Count(PeekTip));
            Assert.AreEqual(0, Stuck, string.Join("\n", _playtest));
            Assert.AreEqual(1, Logged("[Tutorial] paused (no hands)"));
            var pause = _playtest.Single(l => l.Contains(" event=idle_pause"));
            StringAssert.Contains("reason=no_hands", pause);
            Assert.That(PlaytestTime(pause) - gone, Is.InRange(4.9, 5.6), "≈5 s without hands");
            Assert.IsTrue(g.IdlePaused);

            double back = g.Now;
            _rig.Idle(0.5);
            Assert.IsFalse(g.IdlePaused);
            Assert.AreEqual(1, Logged("[Tutorial] resumed"));
            Assert.AreEqual(1, _playtest.Count(l => l.Contains(" event=idle_resume")), string.Join("\n", _playtest));
            Assert.AreEqual(2, Count(Peek), "the prompt is said once when the hands come back");
            Assert.AreEqual(0, Count(PeekTip), "not the whole escalation");

            // 從 0 重新計時：第一次重講在回來後 ≈10 s（不算 Dot 自己講話的時間）
            Assert.IsTrue(_rig.IdleUntil(() => Count(Peek) == 3, 30), string.Join(" | ", g.Voice.History));
            Assert.That(g.Now - back, Is.InRange(9.5, 20.0));
            Assert.AreEqual(0, Count(PeekTip));
            Assert.AreEqual(1, Stuck, "stuck counting only while hands are present");
        }

        [Test]
        public void Tutorial_Unfocused_Pauses_RefocusResumes()
        {
            _rig = TutorialRig();
            var g = _rig.Game;
            WaitForPeekPrompt();
            g.AppFocused = false;
            _rig.Idle(120);
            Assert.AreEqual(1, Logged("[Tutorial] paused (unfocused)"));
            Assert.AreEqual(1, Count(Peek), string.Join(" | ", g.Voice.History));
            Assert.AreEqual(0, Stuck);
            Assert.IsTrue(_playtest.Any(l => l.Contains(" event=idle_pause") && l.Contains("reason=unfocused")), string.Join("\n", _playtest));
            g.AppFocused = true;
            _rig.Idle(0.5);
            Assert.AreEqual(1, Logged("[Tutorial] resumed"));
            Assert.AreEqual(2, Count(Peek));
        }

        [Test]
        public void Tutorial_ABriefTrackingDropout_DoesNotPause()
        {
            _rig = TutorialRig();
            var g = _rig.Game;
            WaitForPeekPrompt();
            _rig.IdleNoHands(3);
            _rig.Idle(1);
            Assert.AreEqual(0, LoggedMatching(@"^\[Tutorial\] paused"));
            Assert.IsFalse(_playtest.Any(l => l.Contains("idle_pause")));
            Assert.IsTrue(_rig.IdleUntil(() => Count(Peek) == 2, 20), "the normal 10 s replay still comes");
        }

        // ================= 正式牌局 =================

        [Test]
        public void LiveTalk_ThinkLines_DoNotFireWithoutHands()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 41, MonteCarloTrials = 40 });
            var g = _rig.Game;
            g.LiveTalkBoost = 3f;
            var decisions = new List<LiveDecision>();
            g.LiveTalkDecided += d => decisions.Add(d);
            int dotLive = 0;
            g.Dealer.Speech.Started += u => { if (u.LineId != null && u.LineId.Contains(".live.")) dotLive++; };
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.IdleNoHands(20);
            var think = decisions.Where(d => d.Signal.Cue == LiveCue.Think3 || d.Signal.Cue == LiveCue.Think7 || d.Signal.Cue == LiveCue.Think12).ToList();
            Assert.AreEqual(3, think.Count, "the 3/7/12 s marks still reach the director: " + string.Join(", ", decisions.Select(d => d.Signal + ":" + d.Skip)));
            Assert.IsTrue(think.All(d => !d.Spoke && d.Skip == "no hands"), string.Join(", ", think.Select(d => d.Signal + ":" + (d.Spoke ? d.Who : d.Skip))));
            Assert.AreEqual(0, dotLive, "no 'Take your time' to an empty chair");
            Assert.IsTrue(_logs.Any(l => Regex.IsMatch(l, @"^\[LiveTalk\] skipped Think12\(12\) \(no hands\) t=")), string.Join("\n", _logs.Where(l => l.StartsWith("[LiveTalk]"))));
        }

        [Test]
        public void LiveTalk_ALongIdleTurnWithHands_GetsAtMostOneDealerNudge()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 41, MonteCarloTrials = 40 });
            var g = _rig.Game;
            g.LiveTalkBoost = 3f;
            int dotLive = 0;
            g.Dealer.Speech.Started += u => { if (u.LineId != null && u.LineId.Contains(".live.")) dotLive++; };
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.Idle(90);
            Assert.AreEqual(GameFlow.PlayerTurn, g.Flow);
            Assert.LessOrEqual(dotLive, 1);
        }
    }
}
