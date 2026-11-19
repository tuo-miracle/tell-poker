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
    /// 端到端（Fool Rocco 原型，2026-10-14）：從手腕選單開始的短情境，用合成手勢打完——
    /// 第 1 手 Rocco 說出玩家真的習慣；第 2 手照 Dot 教的「等幾秒再下注」→ Rocco 跟注、輸、「You played me!」＋揭曉卡；
    /// 沒等 → Rocco 棄牌、重玩一次；第 3 手同一招 → Rocco 不信、棄牌。結束後牌桌還原，不寫存檔、不算戰績、教學不受影響。
    /// 每一句都經過角色的 SpeechChannel（聲音＋同一句字幕）。
    /// </summary>
    public class FoolRoccoFlowTests
    {
        GameTestRig _rig;
        readonly List<string> _rocco = new List<string>(), _dot = new List<string>(), _playtest = new List<string>();
        void OnLog(string m, string st, LogType t) { if (m.StartsWith("[Playtest]")) _playtest.Add(m); }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _playtest.Clear();
            _rig?.Dispose();
            _rig = null;
            _rocco.Clear();
            _dot.Clear();
        }

        /// <summary>fiveHanded：寫實預覽的 5 人桌（荷官右側多一位老手，2026-11-02）。</summary>
        GameController NewGame(System.Action<GameController> extra = null, bool fiveHanded = false)
        {
            var cfg = new GameConfig { Seed = 41, MonteCarloTrials = 40 };
            if (fiveHanded) cfg.Opponents = GameConfig.FiveHanded;
            _rig = new GameTestRig(cfg, extra);
            var g = _rig.Game;
            Application.logMessageReceived += OnLog;
            g.Character(FoolRocco.RoccoSeat).Speech.Started += u => _rocco.Add(u.Text);
            g.Dealer.Speech.Started += u => _dot.Add(u.Text);
            return g;
        }

        static Regex Log(int hand, string callout, string player, string outcome) =>
            new Regex(@"^\[FoolRocco\] hand=" + hand + @" signals reach=\d+\.\d peeks=\d+ hover=\d+\.\d callout=" + Regex.Escape(callout) + " player=" + player + " outcome=" + outcome + "$");

        /// <summary>捏 n 枚籌碼丟進下注區，等 1.5 s 撈回時間過去（同教學測試）。</summary>
        void TossChips(int n)
        {
            float lift = n <= 1 ? 0.005f : 0.025f * (n - 1) + 0.012f;
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: lift);
            GestureScripts.PlaceAt(s, Hand.Right, new NV3(0.0f, 0.02f, 0.40f));
            s.Wait(2.0);
            _rig.Play(s);
            var released = _rig.Gestures.LastOrDefault(e => e.Kind == GestureKind.ChipsReleased);
            Assert.AreEqual(n, released.Count, "synthetic pinch picked the intended count\n" + _rig.Dump());
        }

        bool PlayerTurnInHand(GameController g, int hand, double max = 40) =>
            _rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.FoolRocco != null && g.FoolRocco.Hand == hand && !g.FoolRocco.Decided, max);

        bool NextScenarioHand(GameController g, int hand, double max = 40) =>
            _rig.IdleUntil(() => g.FoolRocco != null && g.FoolRocco.Hand == hand && g.Flow != GameFlow.HandOver, max);

        /// <summary>第 1 手：很快地跟注（丟 2 枚＝跟到 30）。</summary>
        void PlayHand1FastCall(GameController g)
        {
            Assert.IsTrue(PlayerTurnInHand(g, 1), "hand 1: player's turn\n" + _rig.Dump());
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == FoolRocco.PipSeat && a.cmd is Fold), "Pip folds");
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == g.Session.SeatCount - 1 && a.cmd is Fold), "Vex folds");
            if (g.Session.SeatCount == 5) Assert.IsTrue(_rig.Actions.Any(a => a.seat == 3 && a.cmd is Fold), "the veteran folds");
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Raise), "Rocco bets");
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand1PlayerHole), g.Session.PlayerHole, "fixed deck");
            TossChips(2);
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Call), "called\n" + _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.CalloutQuick.Text), 6), "Rocco calls out the quick decision\n" + string.Join(" | ", _rocco));
        }

        void WaitThenBet(GameController g, int hand)
        {
            Assert.IsTrue(PlayerTurnInHand(g, hand), "hand " + hand + ": player's turn\n" + _rig.Dump());
            _rig.Idle(3.6);   // 伸手前等 3 秒以上（Dot 教的招）
            TossChips(4);     // 加注到 40
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Raise), "bet\n" + _rig.Dump());
        }

        [TestCase(false), TestCase(true)]
        public void WaitedThenBet_FoolsRocco_Hand3HeDistrustsIt_ThenTheNormalGameResumes(bool fiveHanded)
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[FoolRocco\] start"));
            LogAssert.Expect(LogType.Log, Log(1, "quick", "fast", "rocco-won"));
            LogAssert.Expect(LogType.Log, Log(2, "call", "waited", "fooled"));
            LogAssert.Expect(LogType.Log, Log(3, "distrust", "waited", "folded"));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[FoolRocco\] end"));
            var g = NewGame(fiveHanded: fiveHanded);
            Assert.IsTrue(g.CanStartFoolRocco);
            double t0 = g.Now;
            g.StartFoolRocco();
            Assert.IsTrue(g.FoolRoccoActive);

            PlayHand1FastCall(g);
            Assert.IsTrue(_dot.Contains(FoolRocco.DotIntro.Text), "Dot introduces it");

            // 第 2 手：Dot 先教，教完 AI 才行動
            Assert.IsTrue(NextScenarioHand(g, 2), "hand 2\n" + _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => _dot.Contains(FoolRocco.DotCoach.Text), 8), "Dot coaches");
            Assert.AreNotEqual(GameFlow.PlayerTurn, g.Flow, "the player's turn waits for the coaching");
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand2PlayerHole), g.Session.PlayerHole, "a monster");
            WaitThenBet(g, 2);
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.RoccoCall.Text), 4), "Rocco reads a bluff\n" + string.Join(" | ", _rocco));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Actions.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Call && g.FoolRocco.Hand == 2 && g.FoolRocco.Decided), 8), "Rocco calls\n" + _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => g.FoolRoccoReveal == FoolRocco.RevealFooled, 40), "reveal card after the showdown\n" + _rig.Dump());
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.RoccoPlayed.Text) && _dot.Contains(FoolRocco.DotFooled.Text), 10), "You played me! / You fooled him.");
            Assert.Greater(g.Session.StackOf(0), g.Config.StartStack, "the player won hand 2");
            Assert.IsTrue(_playtest.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\[Playtest\] t=\d+\.\d event=fooled who=Rocco where=fool_rocco hand=2$")), "playtest log: fooled\n" + string.Join("\n", _playtest));
            Assert.IsTrue(_playtest.Any(l => l.Contains("event=read_reveal who=Rocco text=\"" + FoolRocco.RevealFooled + "\"")), "playtest log: reveal");

            // 第 3 手：同一招 → 不信、棄牌
            Assert.IsTrue(NextScenarioHand(g, 3, 30), "hand 3\n" + _rig.Dump());
            Assert.IsNull(g.FoolRoccoReveal, "the reveal card is gone");
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand3PlayerHole), g.Session.PlayerHole);
            WaitThenBet(g, 3);
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.RoccoDistrust.Text), 4), "Not this time.");
            Assert.IsTrue(_rig.IdleUntil(() => _dot.Contains(FoolRocco.DotLearns.Text), 15), "He learns. Mix it up.");

            // 結束 → 回到一般牌局（牌桌還原、不算戰績、不寫存檔）
            Assert.IsTrue(_rig.IdleUntil(() => !g.FoolRoccoActive && g.Flow != GameFlow.HandOver && g.Session.State.Phase == HandPhase.Betting, 30), "back to the normal game\n" + _rig.Dump());
            Debug.Log($"[FoolRocco] test: whole scenario took {g.Now - t0:0.0} s of game time");
            Assert.Less(g.Now - t0, 180.0, "the whole scenario stays under ~3 minutes");
            Assert.IsTrue(g.Session.RecordStats);
            Assert.AreEqual(0, g.Session.Stats.Hands.Count, "scenario hands are not in the stats");
            Assert.AreEqual(1, g.Session.HandsThisGame, "the normal game is at its first hand");
            Assert.IsFalse(File.Exists(_rig.ProgressPath), "no progress written");
            Assert.AreEqual(0, g.Progress.GamesPlayed);
            Assert.AreEqual(0, g.Progress.CompletedTutorialVersion, "tutorial state untouched");
            AssertEveryLineSpokenWithSubtitle(g);
        }

        [Test]
        public void FastBet_RoccoFolds_TheHandIsReplayedOnce_LaunchedMidHand_TableRestored()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[FoolRocco\] requested"));
            LogAssert.Expect(LogType.Log, Log(1, "quick", "fast", "player-folded"));
            LogAssert.Expect(LogType.Log, Log(2, "fold", "fast", "folded"));
            LogAssert.Expect(LogType.Log, Log(2, "fold", "fast", "folded"));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[FoolRocco\] end"));
            var g = NewGame();
            // 一般牌局的第一手：輪到玩家時棄牌，牌局繼續
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            g.StartFoolRocco();
            Assert.IsTrue(_rig.IdleUntil(() => _dot.Contains(FoolRocco.DotNextHand.Text), 4), "Fool Rocco next hand!");
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsTrue(_rig.IdleUntil(() => g.FoolRocco != null && g.FoolRocco.Hand == 1, 30), "scenario starts at the next hand\n" + _rig.Dump());
            int statHands = g.Session.Stats.Hands.Count;
            Assert.AreEqual(1, statHands, "the normal hand was recorded");

            // 等到這一手之前的牌桌（籌碼）——還原時要回到這裡
            var before = SnapshotOfNormalTable;
            Assert.IsNotNull(before);

            Assert.IsTrue(PlayerTurnInHand(g, 1), _rig.Dump());
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold && g.FoolRocco.Hand == 1), "folded hand 1\n" + _rig.Dump());

            Assert.IsTrue(NextScenarioHand(g, 2), _rig.Dump());
            Assert.IsTrue(PlayerTurnInHand(g, 2), _rig.Dump());
            TossChips(4);   // 馬上下注（沒等）
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.RoccoFold.Text), 4), "Quick bet. You've got it. I'm out.");
            Assert.IsTrue(_rig.IdleUntil(() => _dot.Contains(FoolRocco.DotRetry.Text), 15), "Try waiting next time.");
            Assert.AreEqual(FoolRocco.RevealQuick, g.FoolRoccoReveal);

            // 重玩一次（同一手牌、Dot 再教一次）
            Assert.IsTrue(_rig.IdleUntil(() => g.FoolRocco != null && g.FoolRocco.Replaying && g.Flow != GameFlow.HandOver, 30), "replay\n" + _rig.Dump());
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand2PlayerHole), g.Session.PlayerHole);
            Assert.IsTrue(PlayerTurnInHand(g, 2), _rig.Dump());
            Assert.AreEqual(2, _dot.Count(t => t == FoolRocco.DotCoach.Text), "coached again");
            TossChips(4);
            Assert.IsTrue(_rig.IdleUntil(() => !g.FoolRoccoActive && g.Session.State.Phase == HandPhase.Betting && g.Flow != GameFlow.HandOver, 40), "ends after the one replay\n" + _rig.Dump());
            Assert.IsFalse(_dot.Contains(FoolRocco.DotLearns.Text), "no hand 3 when the trick never worked");

            CollectionAssert.AreEqual(before, _restoredStacks, "chips back to where the normal game was");
            Assert.AreEqual(statHands, g.Session.Stats.Hands.Count, "stats untouched by the scenario");
            Assert.AreEqual(2, g.Session.HandsThisGame, "the normal game continues with its second hand");
            AssertEveryLineSpokenWithSubtitle(g);
        }

        /// <summary>2026-10-14 live talk：Rocco 在你還在等、還沒下注的時候就嗆（不只下注之後）；劇情照舊（跟注、You played me!）。</summary>
        [Test]
        public void Hand2_RoccoNeedlesWhileYouWait_BeforeTheBet_AndTheArcStillWorks()
        {
            var g = NewGame(c => c.LiveTalkBoost = 2f);
            var live = new List<(double t, string text, string id)>();
            g.Character(FoolRocco.RoccoSeat).Speech.Started += u => { if (u.LineId != null && u.LineId.StartsWith("rocco.live.")) live.Add((g.Now, u.Text, u.LineId)); };
            bool otherLive = false;
            foreach (var seat in new[] { FoolRocco.PipSeat, FoolRocco.VexSeat })
                g.Character(seat).Speech.Started += u => { if (u.LineId != null && u.LineId.Contains(".live.")) otherLive = true; };
            g.Dealer.Speech.Started += u => { if (u.LineId != null && u.LineId.Contains(".live.")) otherLive = true; };
            g.StartFoolRocco();
            PlayHand1FastCall(g);
            Assert.IsTrue(NextScenarioHand(g, 2), _rig.Dump());
            Assert.IsTrue(PlayerTurnInHand(g, 2), _rig.Dump());
            int before = live.Count;
            _rig.Idle(3.6);
            Assert.Greater(live.Count, before, "Rocco needles during the wait: " + string.Join(" | ", live.Select(x => x.text)));
            double betAt = g.Now;
            TossChips(4);
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.RoccoCall.Text), 4), "Rocco still reads the slow reach as a bluff\n" + string.Join(" | ", _rocco));
            Assert.IsTrue(live.Take(before + 1).Last().t < betAt, "the needle came before the bet");
            Assert.IsTrue(_rig.IdleUntil(() => _rocco.Contains(FoolRocco.RoccoPlayed.Text), 40), "You played me!");
            Assert.IsFalse(otherLive, "in the scenario only Rocco reads you");
            foreach (var x in live) Assert.IsTrue(Poker.Characters.SpeechBank.CanSay("rocco", x.text, out var why), x.id + ": " + why);
        }

        [Test]
        public void NotOffered_WhileTheTutorialRuns_AndTheTutorialIsUnaffected()
        {
            var path = Path.Combine(Path.GetTempPath(), "tell_fool_tut_" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var g = NewGame(c => { c.ProgressPath = path; c.TutorialEnabled = true; });
                Assert.IsNotNull(g.Tutorial);
                Assert.IsTrue(g.Tutorial.IsActive);
                Assert.IsFalse(g.CanStartFoolRocco, "the tutorial owns the table");
                g.StartFoolRocco();
                Assert.IsFalse(g.FoolRoccoActive);
                _rig.Idle(3);
                Assert.IsNull(g.FoolRocco);
                CollectionAssert.AreEqual(Card.ParseMany(TutorialScript.Hand1PlayerHole), g.Session.PlayerHole, "the tutorial's own deck");
            }
            finally
            {
                foreach (var f in new[] { path, path + ".tmp", path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
            }
        }

        [Test]
        public void ReplayTutorial_IsIgnored_WhileFoolRoccoRuns()
        {
            var g = NewGame();
            g.StartFoolRocco();
            _rig.Idle(1);
            g.ReplayTutorial();
            Assert.IsTrue(g.Tutorial == null || !g.Tutorial.IsActive, "the scenario keeps the table");
            Assert.IsTrue(g.FoolRoccoActive);
        }

        // 還原檢查：情境開始那一刻的籌碼（GameController 記下）、結束後第一手開始時的籌碼
        int[] SnapshotOfNormalTable => _rig.Game.FoolRoccoSavedStacks;
        int[] _restoredStacks => _rig.Game.FoolRoccoRestoredStacks;

        /// <summary>情境裡 Rocco 與 Dot 說的每一句：字幕就是那句、有預錄聲音（clip 不是 none）。</summary>
        void AssertEveryLineSpokenWithSubtitle(GameController g)
        {
            foreach (var t in _rocco.Concat(_dot)) Assert.IsFalse(string.IsNullOrEmpty(t));
            var lines = FoolRocco.Lines().Select(l => l.Text).ToList();
            Assert.IsTrue(_rocco.Concat(_dot).Any(lines.Contains), "scenario lines were spoken through SpeechChannel");
            foreach (var l in FoolRocco.Lines())
                if (_rocco.Contains(l.Text) || _dot.Contains(l.Text))
                    Assert.IsTrue(Poker.Characters.SpeechBank.CanSay(l.Voice, l.Text, out var why), l + ": " + why);
        }
    }
}
