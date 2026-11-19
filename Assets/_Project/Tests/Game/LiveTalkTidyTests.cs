using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 即時對話收尾（2026-10-14）：
    /// (a) 這一回合已經講過即時台詞的角色，玩家出手後不再補一句舊的「長考」台詞（[LiveTalk] suppressed tank …）；
    /// (b) 即時台詞排隊超過 1.5 s、或玩家已經出手（觸發的情境不在了）就丟掉，不晚講（[LiveTalk] dropped …）。
    /// </summary>
    public class LiveTalkTidyTests
    {
        GameTestRig _rig;
        readonly List<string> _logs = new List<string>();
        readonly List<(double t, string who, Utterance u)> _started = new List<(double, string, Utterance)>();

        [SetUp] public void SetUp() => Application.logMessageReceived += OnLog;

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig?.Dispose();
            _rig = null;
            _logs.Clear();
            _started.Clear();
        }

        void OnLog(string msg, string stack, LogType type) { if (msg.StartsWith("[LiveTalk]") || msg.StartsWith("[Speech]")) _logs.Add(msg); }

        GameController NewGame(Archetype[] opponents, float boost, ulong seed = 41, System.Action<GameController> extra = null)
        {
            _rig = new GameTestRig(new GameConfig { Opponents = opponents, Seed = seed, FirstButton = 1, MonteCarloTrials = 40 }, g =>
            {
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
                foreach (JitTip t in System.Enum.GetValues(typeof(JitTip))) g.Tips.TryUse(t);
                extra?.Invoke(g);
            });
            var game = _rig.Game;
            game.LiveTalkBoost = boost;
            foreach (var c in game.AllCharacters())
            {
                var ch = c.Speech;
                ch.Started += u => _started.Add((game.Now, ch.Who, u));
            }
            return game;
        }

        void Act(GameController g)
        {
            var la = g.Session.Legal(0);
            if (la.CanCheck) _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            else
            {
                int n = Mathf.Max(1, la.CallAmount / g.Config.Denomination);
                var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: n <= 1 ? 0.005f : 0.025f * (n - 1) + 0.012f);
                GestureScripts.PlaceAt(s, Hand.Right, new System.Numerics.Vector3(0.0f, 0.02f, 0.40f));
                s.Wait(2.0);
                _rig.Play(s);
            }
        }

        [Test]
        public void AVoiceThatSpokeLive_DoesNotAddTheOldTankLineAfterTheAction()
        {
            var g = NewGame(new GameConfig().Opponents, boost: 10f);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            int me = _rig.Actions.Count(a => a.seat == 0);
            _rig.Idle(8.5);   // 想很久：即時台詞（3 s、7 s），而且超過長考門檻（PlayerReadSettings.TankSeconds）
            Assert.AreEqual(GameFlow.PlayerTurn, g.Flow, _rig.Dump());
            var live = _started.Where(x => x.u.LineId != null && x.u.LineId.Contains(".live.")).Select(x => x.who).Distinct().ToList();
            Assert.IsNotEmpty(live, "someone spoke live this turn: " + string.Join(" | ", _logs));
            double actedAt = g.Now;
            Act(g);
            Assert.Greater(_rig.Actions.Count(a => a.seat == 0), me, _rig.Dump());
            _rig.Idle(3.0);
            var tankAfter = _started.Where(x => x.t >= actedAt && x.u.LineId != null && x.u.LineId.Contains(".tank.")).ToList();
            foreach (var x in tankAfter) CollectionAssert.DoesNotContain(live, x.who, "tank line after a live line from the same voice: " + x.u.Text);
            Assert.IsTrue(_logs.Any(l => l.StartsWith("[LiveTalk] suppressed tank ") && l.EndsWith("(spoke live this turn)")),
                "the long think did trigger a tank offer that was vetoed:\n" + string.Join("\n", _logs));
        }

        /// <summary>讓即時台詞排隊：所有人的頻道暫時只講 Info 以上（像玩家在講話時），導演那邊照常決定要講。</summary>
        void HoldChatter(GameController g, bool hold)
        {
            foreach (var c in g.AllCharacters()) c.Speech.HoldBelow = hold ? SpeechPriority.Info : SpeechPriority.Filler;
        }

        Utterance FirstQueuedLiveLine(GameController g, double maxSeconds)
        {
            Utterance found = null;
            _rig.IdleUntil(() =>
            {
                foreach (var c in g.AllCharacters())
                {
                    var u = c.Speech.LastSubmitted;
                    if (u != null && u.State == UtteranceState.Queued && u.LineId != null && u.LineId.Contains(".live.")) { found = u; return true; }
                }
                return false;
            }, maxSeconds);
            return found;
        }

        [Test]
        public void ALiveLineQueuedTooLong_IsDropped_NotPlayedLate()
        {
            Assert.AreEqual(1.5f, LiveTalkDirector.MaxQueueSeconds, 1e-6f);
            var g = NewGame(GameConfig.HeadsUp, boost: 10f);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            HoldChatter(g, true);
            var u = FirstQueuedLiveLine(g, 9);
            Assert.IsNotNull(u, "a live line was queued: " + string.Join(" | ", _logs));
            _rig.Idle(LiveTalkDirector.MaxQueueSeconds + 0.2);
            Assert.AreEqual(UtteranceState.Dropped, u.State);
            HoldChatter(g, false);
            _rig.Idle(2);
            Assert.IsFalse(_started.Any(x => x.u == u), "never played late");
            Assert.IsTrue(_logs.Any(l => l.StartsWith("[LiveTalk] dropped ") && l.Contains("line=" + u.LineId) && l.Contains("(queued 1.5 s)")), string.Join("\n", _logs));
        }

        [Test]
        public void ALiveLineStillQueuedWhenThePlayerActs_IsDropped()
        {
            var g = NewGame(GameConfig.HeadsUp, boost: 10f);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            Assert.IsTrue(g.Session.Legal(0).CanCheck);
            HoldChatter(g, true);
            var u = FirstQueuedLiveLine(g, 9);
            Assert.IsNotNull(u, "a live line was queued: " + string.Join(" | ", _logs));
            Assert.AreEqual(GameFlow.PlayerTurn, g.Flow);
            Act(g);   // 敲桌約 0.4 s：排隊還不到 1.5 s
            Assert.AreEqual(UtteranceState.Dropped, u.State, _rig.Dump());
            HoldChatter(g, false);
            _rig.Idle(2);
            Assert.IsFalse(_started.Any(x => x.u == u));
            Assert.IsTrue(_logs.Any(l => l.StartsWith("[LiveTalk] dropped ") && l.Contains("line=" + u.LineId) && l.Contains("(player acted)")), string.Join("\n", _logs));
        }
    }
}
