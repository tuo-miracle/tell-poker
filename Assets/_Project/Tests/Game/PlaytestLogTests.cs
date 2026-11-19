using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.Game;
using UnityEngine;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 試玩分析用的 log（2026-10-14）：`[Playtest] t=&lt;開機後秒數&gt; event=&lt;tutorial_step|first_bet|hint|read_reveal|fooled|stuck(&lt;step&gt;,&lt;s&gt;)|tip|session_end&gt; …`，
    /// 夠算每個測試者的「第一次下注要多久」「多久騙到 Rocco」「卡在哪裡」。一行一件事、數字用小數點（不受語系影響）。
    /// </summary>
    public class PlaytestLogTests
    {
        readonly List<string> _lines = new List<string>();
        void OnLog(string m, string s, LogType t) { if (m.StartsWith("[Playtest]")) _lines.Add(m); }
        [SetUp] public void SetUp() => Application.logMessageReceived += OnLog;
        [TearDown] public void TearDown() { Application.logMessageReceived -= OnLog; _lines.Clear(); }

        [Test]
        public void Format_IsOneLinePerEvent_InvariantNumbers()
        {
            Assert.AreEqual("[Playtest] t=12.3 event=hint label=strong street=flop", PlaytestLog.Format(12.34, "hint", "label=strong street=flop"));
            Assert.AreEqual("[Playtest] t=0.0 event=stuck(Peek,10)", PlaytestLog.Format(0.04, PlaytestLog.Stuck("Peek", 10.2f)));
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                StringAssert.StartsWith("[Playtest] t=1234.5 ", PlaytestLog.Format(1234.5, "x"));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Test]
        public void FirstBet_AndSessionEnd_AreLoggedOnce_WithASummary()
        {
            double now = 5;
            var log = new PlaytestLog { Clock = () => now };
            log.FirstBet(true, "call");
            now = 8;
            log.FirstBet(false, "raise");
            log.StuckAt("Decide", 10f);
            log.HandPlayed();
            log.HandPlayed();
            now = 61.27;
            log.Fooled("Rocco", "tutorial", 2);
            now = 90;
            log.SessionEnd();
            log.SessionEnd();
            Assert.AreEqual(1, _lines.FindAll(l => l.Contains("event=first_bet")).Count);
            Assert.Contains("[Playtest] t=5.0 event=first_bet tutorial=1 action=call", _lines);
            Assert.Contains("[Playtest] t=8.0 event=stuck(Decide,10)", _lines);
            Assert.Contains("[Playtest] t=61.3 event=fooled who=Rocco where=tutorial hand=2", _lines);
            Assert.AreEqual(1, _lines.FindAll(l => l.Contains("event=session_end")).Count);
            Assert.Contains("[Playtest] t=90.0 event=session_end hands=2 first_bet=5.0 fooled=61.3 stuck=1", _lines);
            foreach (var l in _lines) StringAssert.IsMatch(@"^\[Playtest\] t=\d+\.\d event=\S+", l);
        }
    }
}
