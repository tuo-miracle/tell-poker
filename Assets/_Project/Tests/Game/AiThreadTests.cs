using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>AI 的 Monte Carlo 決策不在主執行緒跑（Quest 上一次可能數十毫秒，會掉幀）；期間送來的讀人訊號排隊、不遺失。</summary>
    public class AiThreadTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        [Test]
        public void AiDecision_RunsOffTheMainThread()
        {
            int main = System.Environment.CurrentManagedThreadId;
            _rig = new GameTestRig(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 200 });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.LastDecideThread != 0, 10), "Pip decided");
            Assert.AreNotEqual(main, _rig.Game.Session.LastDecideThread);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Actions.Any(a => a.seat == 1), 10), "Pip's action applied");
        }

        [Test]
        public void GazeSignal_DuringAiThinking_IsDeliveredOnce()
        {
            _rig = new GameTestRig(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 200 }, g => g.AiThinkScale = 3f);
            _rig.LookFromSeat(new UnityEngine.Vector3(0.4f, 0f, 0.1f));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.AiThinking, 5));
            _rig.LookAt(_rig.Game.Pip.Rig.Head.position);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Signals.Any(x => x.s.GazeSeat == 1), 5), "gaze signal reached the AI");
            Assert.AreEqual(1, _rig.Signals.Count(x => x.s.GazeSeat == 1));
            Assert.AreEqual(1, _rig.Game.GazeSignalsSent);
        }
    }
}
