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
using Hand = Poker.Gestures.Hand;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 牌力提示（2026-10-14，給不會打牌的評審）：每一手玩家第一次看牌（咪牌或拿起來）時，牌旁邊出現只有自己看得到的小字
    /// （Strong hand／Decent hand／Weak hand／Drawing — could improve），之後每一條新的街更新；Dot 只在這次開機第一次小聲說
    /// （有聲音＋同一句字幕），之後只有字。設定可以關。提示只從玩家自己的牌算（HandStrength），不會走到 AI 那邊。
    /// </summary>
    public class HandHintFlowTests
    {
        GameTestRig _rig;
        readonly List<string> _playtest = new List<string>();
        readonly List<string> _dot = new List<string>();

        [SetUp] public void SetUp() => Application.logMessageReceived += OnLog;

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig?.Dispose();
            _rig = null;
            _playtest.Clear();
            _dot.Clear();
        }

        void OnLog(string msg, string stack, LogType type) { if (msg.StartsWith("[Playtest]")) _playtest.Add(msg); }

        static PlayerCommand CheckOrCall(HandState st)
        {
            var la = Engine.GetLegalActions(st, st.ToAct);
            return la.CanCheck ? new Check(st.ToAct) : (PlayerCommand)new Call(st.ToAct);
        }

        GameTestRig NewRig(System.Action<GameController> extra = null, ulong seed = 23)
        {
            var rig = new GameTestRig(new GameConfig { Seed = seed, MonteCarloTrials = 40 }, extra);
            var g = rig.Game;
            g.PlaytestClock = () => g.Now;
            g.Dealer.Speech.Started += u => _dot.Add(u.Text);
            return rig;
        }

        void Peek(GameController g) => g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0.8f));

        static readonly string[] Whispers = System.Enum.GetValues(typeof(HandHint)).Cast<HandHint>().Select(DealerLines.HandHintWhisper).ToArray();

        [Test]
        public void FirstLook_ShowsTheLabel_ItUpdatesEachStreet_AndDotWhispersOnlyTheFirstTime()
        {
            _rig = NewRig(c => c.AiOverride = CheckOrCall);
            var g = _rig.Game;
            g.Session.NextHandPreset = CardPreset.Parse(new Dictionary<int, string> { { 0, "Ah9c" } }, "9s6d2cJh3s");
            g.AutoPlayer = CheckOrCall;
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 1 && g.PlayerCard(0).gameObject.activeSelf && g.Flow != GameFlow.Presenting, 10), _rig.Dump());
            Assert.IsNull(g.HandHintText, "nothing until the player looks");
            Peek(g);
            Assert.AreEqual("Decent hand", g.HandHintText);
            Assert.AreEqual(HandHint.Decent, g.HandHintShown);
            Assert.IsTrue(_rig.IdleUntil(() => _dot.Contains(DealerLines.HandHintWhisper(HandHint.Decent)), 4), "whisper, voiced and subtitled\n" + string.Join(" | ", _dot));
            Assert.IsTrue(Poker.Characters.SpeechBank.CanSay("dot", DealerLines.HandHintWhisper(HandHint.Decent), out var why), why);
            Peek(g);
            Assert.AreEqual(1, g.HandHintHistory.Count, "looking again doesn't recompute");
            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.HandOver, 60), _rig.Dump());
            CollectionAssert.AreEqual(new[] { HandHint.Decent, HandHint.Strong, HandHint.Strong, HandHint.Strong }, g.HandHintHistory,
                "preflop, then flop (top pair), turn, river");
            Assert.IsNull(g.HandHintText, "hidden when the hand is over");
            foreach (var street in new[] { "preflop", "flop", "turn", "river" })
                Assert.IsTrue(_playtest.Any(l => Regex.IsMatch(l, @"^\[Playtest\] t=\d+\.\d event=hint label=\w+ street=" + street + "$")), street + "\n" + string.Join("\n", _playtest));

            // 第二手：只有字，不再說
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 2 && g.Flow != GameFlow.Presenting, 20), _rig.Dump());
            Assert.IsNull(g.HandHintText);
            Peek(g);
            Assert.IsNotNull(g.HandHintText, "the label still shows");
            _rig.Idle(3);
            Assert.AreEqual(1, _dot.Count(t => Whispers.Contains(t)), "Dot whispers once per session");
            Assert.AreEqual(1, g.HandHintWhispers);
        }

        [Test]
        public void PickingTheCardsUp_AlsoShowsIt_FoldingHidesIt()
        {
            _rig = NewRig(c => c.AiOverride = CheckOrCall);
            var g = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            g.HandleGesture(new GestureEvent(GestureKind.CardsLifted, 0, Hand.Right));
            Assert.IsNotNull(g.HandHintText, "picking the cards up is looking too");
            var expected = HandStrength.Classify(g.Session.PlayerHole[0], g.Session.PlayerHole[1], new Card[0], 3);
            Assert.AreEqual(HandStrength.Label(expected), g.HandHintText, "the label is HandStrength on the player's own cards");
            g.HandleGesture(new GestureEvent(GestureKind.CardsReturned, 0, Hand.Right));
            g.HandleGesture(new GestureEvent(GestureKind.FoldPushed, 0, Hand.Right));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold), _rig.Dump());
            Assert.IsNull(g.HandHintText, "hidden after folding");
        }

        [Test]
        public void SettingOff_NoLabel_NoWhisper_NoLog()
        {
            _rig = NewRig(c => { c.AiOverride = CheckOrCall; c.HandHintsEnabled = false; });
            var g = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            Peek(g);
            Assert.IsNull(g.HandHintText);
            _rig.Idle(3);
            Assert.IsFalse(_dot.Any(t => Whispers.Contains(t)));
            Assert.IsFalse(_playtest.Any(l => l.Contains("event=hint")));
            // 中途打開：下一次看牌（下一手）才出現；中途關掉：馬上收起來
            g.HandHintsEnabled = true;
            g.HandleGesture(new GestureEvent(GestureKind.CardsLifted, 0, Hand.Right));
            Assert.IsNotNull(g.HandHintText, "turned on: the next look shows it");
            g.HandHintsEnabled = false;
            Assert.IsNull(g.HandHintText, "turned off: hidden right away");
        }

        /// <summary>
        /// 隱私：提示只看玩家自己的牌，結果只畫在玩家面前、只有 Dot 說一次，不會變成任何 AI 的輸入。
        /// 同一個種子、同樣的手勢，開／關提示，AI 的每個決定與送給 AI 的每筆訊號都一模一樣。
        /// </summary>
        [Test]
        public void Privacy_AiDecisionsAndSignals_AreIdentical_WithHintsOnOrOff()
        {
            (List<string> actions, List<string> signals, int hints) Run(bool on)
            {
                var rig = NewRig(c => c.HandHintsEnabled = on, seed: 77);
                try
                {
                    var g = rig.Game;
                    g.AutoPlayer = CheckOrCall;
                    int hints = 0;
                    for (int hand = 1; hand <= 3; hand++)
                    {
                        Assert.IsTrue(rig.IdleUntil(() => g.Session.HandsStarted == hand && g.PlayerCard(0).gameObject.activeSelf && g.Flow != GameFlow.Presenting, 30), rig.Dump());
                        Peek(g);
                        if (g.HandHintText != null) hints++;
                        Assert.IsTrue(rig.IdleUntil(() => g.Flow == GameFlow.HandOver || g.Flow == GameFlow.GameOver, 90), rig.Dump());
                    }
                    var actions = rig.Actions.Select(a => a.seat + ":" + a.cmd).ToList();
                    var signals = rig.Signals.Select(s => $"{s.s.Time:0.000} {s.s.HesitationSeconds:0.000} {s.s.PeekSeconds:0.000} {s.s.Looks} {s.s.ThinkSeconds:0.000} {s.s.Aggressive} {s.s.GazeSeat}").ToList();
                    return (actions, signals, hints);
                }
                finally { rig.Dispose(); }
            }
            var a = Run(true);
            var b = Run(false);
            Assert.AreEqual(3, a.hints, "hints were really shown in the 'on' run");
            Assert.AreEqual(0, b.hints);
            Assert.Greater(a.actions.Count, 6);
            CollectionAssert.AreEqual(a.actions, b.actions, "every AI decision is the same");
            CollectionAssert.AreEqual(a.signals, b.signals, "every signal sent to the AIs is the same");
        }

        /// <summary>沒有任何管道：PlayerSignals 沒有牌力欄位；Poker.AI 裡除了 HandStrength 自己，沒有程式用到它（AI 不會呼叫它）。</summary>
        [Test]
        public void Privacy_NoPathFromTheHintToTheAi()
        {
            foreach (var f in typeof(PlayerSignals).GetFields())
            {
                Assert.AreNotEqual(typeof(HandHint), f.FieldType, f.Name);
                StringAssert.DoesNotContain("Hint", f.Name);
                StringAssert.DoesNotContain("Strength", f.Name);
            }
            var dir = Path.Combine(Application.dataPath, "_Project/Scripts/Poker.AI");
            foreach (var file in Directory.GetFiles(dir, "*.cs"))
            {
                if (file.EndsWith("HandStrength.cs")) continue;
                var src = File.ReadAllText(file);
                StringAssert.DoesNotContain("HandStrength", src, file);
                StringAssert.DoesNotContain("HandHint", src, file);
            }
            var voice = Path.Combine(Application.dataPath, "_Project/Scripts/Voice");
            foreach (var file in Directory.GetFiles(voice, "*.cs"))
                StringAssert.DoesNotContain("HandHint", File.ReadAllText(file), "the LLM-facing public state never sees it: " + file);
        }
    }
}
