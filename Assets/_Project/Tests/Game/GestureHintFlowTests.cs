using System.IO;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using Hand = Poker.Gestures.Hand;

namespace Poker.Tests.Game
{
    /// <summary>牌局中的提示接到 GameController：輪到玩家發呆 5 秒 → 示範手輪流示範合法動作；動手、開選單、語音收音就收起來；成功過牌 3 次後不再提示敲桌。</summary>
    public class GestureHintFlowTests
    {
        GameTestRig _rig;
        string _path;

        sealed class FakeVoice : ITutorialVoice
        {
            public bool Available { get; set; } = true;
            public bool Busy { get; set; }
            public int Utterances { get; set; }
        }

        [SetUp] public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "tell_hints_" + System.Guid.NewGuid().ToString("N") + ".json");

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            foreach (var f in new[] { _path, _path + ".tmp", _path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
        }

        /// <summary>一對一，AI 坐按鈕（小盲）且只跟／過：翻牌前 AI 跟注後玩家（大盲）可以過牌。</summary>
        GameTestRig NewRig(FakeVoice voice = null) => new GameTestRig(
            new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 40 }, g =>
            {
                g.ProgressPath = _path;
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
                g.TutorialVoice = voice;
                // 2026-10-14：第一次可以過牌時有「及時提示」（馬上示範敲桌，見 JustInTimeTipsTests）；
                // 這裡測的是發呆 5 秒後的提示，先當作及時提示都教過了
                foreach (JitTip t in System.Enum.GetValues(typeof(JitTip))) g.Tips.TryUse(t);
            });

        [Test]
        public void IdleOnTurn_ShowsGhostsForLegalActions_InTurn()
        {
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(10));
            Assert.IsTrue(g.Session.Legal(0).CanCheck);
            _rig.Idle(GestureHints.IdleSeconds - 0.5);
            Assert.IsNull(g.GhostShown, "not before 5 s");
            _rig.Idle(1.0);
            Assert.AreEqual(GhostGesture.Knock, g.GhostShown, "can check: knock first");
            // 示範完一輪換下一個合法動作
            Assert.IsTrue(_rig.IdleUntil(() => g.GhostShown == GhostGesture.Bet, 8), "then chips");
            Assert.IsTrue(_rig.IdleUntil(() => g.GhostShown == GhostGesture.AllIn, 8), "then all-in");
        }

        [Test]
        public void MovingTheHands_MenuOrVoiceCapture_HideTheHint()
        {
            var voice = new FakeVoice();
            _rig = NewRig(voice);
            var g = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(10));
            _rig.Idle(GestureHints.IdleSeconds + 0.5);
            Assert.IsNotNull(g.GhostShown);

            // 手開始往籌碼堆移動（還沒成為任何手勢）：淡出
            var reach = new SyntheticHands();
            reach.Move(Hand.Right, 0.4, HandTarget.At(G.Stack + new System.Numerics.Vector3(0.0f, 0.08f, 0f)));
            _rig.Play(reach);
            Assert.IsTrue(_rig.IdleUntil(() => g.GhostShown == null, 0.6), "fades out when the player moves");

            _rig.Idle(GestureHints.IdleSeconds + 0.5);
            Assert.IsNotNull(g.GhostShown);
            g.MenuOpen = true;
            Assert.IsNull(g.GhostShown, "menu open: hidden at once");
            g.MenuOpen = false;
            _rig.Idle(0.5);
            Assert.IsNull(g.GhostShown, "the idle clock restarts after the menu");

            _rig.Idle(GestureHints.IdleSeconds + 0.5);
            Assert.IsNotNull(g.GhostShown);
            voice.Busy = true;
            _rig.StepIdle();
            Assert.IsTrue(_rig.IdleUntil(() => g.GhostShown == null, 0.5), "talking to a character: no hints");
            _rig.Idle(GestureHints.IdleSeconds + 1);
            Assert.IsNull(g.GhostShown);

            voice.Busy = false;
            g.HintsEnabled = false;
            _rig.Idle(GestureHints.IdleSeconds + 1);
            Assert.IsNull(g.GhostShown, "Hints: Off");
        }

        [Test]
        public void ThreeSuccessfulKnocks_StopTheKnockHint_AndAreSaved()
        {
            _rig = NewRig();
            var g = _rig.Game;
            for (int i = 0; i < GestureHints.SuppressAfter; i++)
            {
                Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Session.Legal(0).CanCheck, 60), "turn " + i + "\n" + _rig.Dump());
                _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
                Assert.AreEqual(i + 1, g.Progress.Successes(GhostGesture.Knock), _rig.Dump());
            }
            Assert.AreEqual(GestureHints.SuppressAfter, new ProgressStore(_path).Load().Successes(GhostGesture.Knock), "persisted");

            Assert.IsTrue(_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn && g.Session.Legal(0).CanCheck, 60));
            Assert.IsTrue(_rig.IdleUntil(() => g.GhostShown != null, GestureHints.IdleSeconds + 1));
            Assert.AreEqual(GhostGesture.Bet, g.GhostShown, "knock is learned: start with chips");
            Assert.IsFalse(_rig.IdleUntil(() => g.GhostShown == GhostGesture.Knock, 12), "never the knock again");
        }
    }
}
