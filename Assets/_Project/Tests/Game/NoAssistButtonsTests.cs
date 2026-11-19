using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 2026-11-03 使用者：「左下角還有 Fold, Call 啥的按鈕全部移除」——手勢備援按鈕（桌緣的 Check／Call／Fold／Bet）整個拿掉：
    /// 手勢失敗、卡住很久都不會出現；設定裡也沒有這一項，舊存檔裡的欄位照樣讀得進來。
    /// </summary>
    public class NoAssistButtonsTests
    {
        GameTestRig _rig;
        readonly List<string> _logs = new List<string>();
        void OnLog(string m, string s, LogType t) => _logs.Add(m);

        [SetUp] public void SetUp() => Application.logMessageReceived += OnLog;
        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig?.Dispose();
            _rig = null;
        }

        [Test]
        public void FailedGestures_AndAStuckPlayer_NeverBringUpButtons()
        {
            _rig = new GameTestRig(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 40 }, g =>
            {
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
                foreach (JitTip t in System.Enum.GetValues(typeof(JitTip))) g.Tips.TryUse(t);
            });
            Assert.IsTrue(_rig.WaitPlayerTurn(10), _rig.Dump());
            for (int i = 0; i < 2; i++)
            {
                var s = GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right), taps: 1);
                s.Wait(1.5);
                _rig.Play(s);
            }
            _rig.Idle(30);   // 手放在桌上不動，超過舊的「卡住 20 秒」
            Assert.AreEqual(GameFlow.PlayerTurn, _rig.Game.Flow, "still the player's turn: nothing acted for them");
            Assert.IsFalse(_logs.Exists(l => l.StartsWith("[Assist]")), "no assist log: " + string.Join(" | ", _logs.FindAll(l => l.StartsWith("[Assist]"))));
            foreach (var t in _rig.Root.GetComponentsInChildren<Transform>(true))
                Assert.IsFalse(t.name.StartsWith("AssistButton"), "no assist button objects: " + t.name);
        }
    }
}
