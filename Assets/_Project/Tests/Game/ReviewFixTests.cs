using NUnit.Framework;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>審查發現的回歸測試：荷官搖頭會回正、分批丟籌碼會累加、兩手各拿籌碼、全下台詞只說一次、出局的牌不能咪。</summary>
    public class ReviewFixTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static GameConfig PlayerFirst => new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 0, MonteCarloTrials = 80 };
        static GestureEvent E(GestureKind k, Hand h = Hand.Right, int n = 0, float z = 0.4f, bool zone = true) =>
            new GestureEvent(k, 0, h, n, 0f, new NV3(0f, 0f, z), zone);

        int StackChips => _rig.Game.Props.Stack[0].Chips;

        [Test]
        public void DealerHeadShake_ReturnsToRest()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            var head = _rig.Game.Dealer.Rig.Head;
            var rest = head.localRotation;
            for (int i = 0; i < 3; i++) { _rig.Game.HandleGesture(E(GestureKind.KnockCheck)); _rig.Idle(1.0); }
            Assert.AreEqual(3, _rig.Invalids.Count);
            Assert.Less(Quaternion.Angle(rest, head.localRotation), 0.5f, "head back to rest");
        }

        [Test]
        public void TwoDropsIntoTheBetZone_Accumulate()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.StepIdle();
            int start = StackChips;
            for (int i = 0; i < 2; i++)
            {
                _rig.Game.HandleGesture(E(GestureKind.ChipsPicked, n: 1));
                _rig.Game.HandleGesture(E(GestureKind.ChipsReleased, n: 1));
                _rig.Idle(0.5);
            }
            Assert.AreEqual(2, _rig.Game.Props.Pending.Chips, "both chips wait in the bet zone");
            Assert.AreEqual(start - 2, StackChips);
        }

        [Test]
        public void ChipsInBothHands_AreBothReservedFromTheStack()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.StepIdle();
            int start = StackChips;
            _rig.Game.HandleGesture(E(GestureKind.ChipsPicked, Hand.Right, 2));
            _rig.Game.HandleGesture(E(GestureKind.ChipsPicked, Hand.Left, 1));
            _rig.StepIdle();
            Assert.AreEqual(start - 3, StackChips);
            _rig.Game.HandleGesture(E(GestureKind.ChipsReleased, Hand.Right, 2, zone: false));
            _rig.Idle(0.6);
            Assert.AreEqual(start - 1, StackChips, "left hand still holds one");
        }

        [Test]
        public void AllInByGesture_DealerSaysItOnce()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            int said = 0;
            _rig.Game.Dealer.LineStarted += (p, id) => { if (id.StartsWith("dot.allin")) said++; };
            _rig.Game.HandleGesture(E(GestureKind.AllInConfirmed));
            _rig.Idle(1.0);
            Assert.AreEqual(1, said);
        }

        [Test]
        public void Peek_IsIgnored_AfterTheHandIsOver()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Game.HandleGesture(E(GestureKind.FoldPushed));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 10));
            _rig.Game.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 1f));
            Assert.AreEqual(0f, _rig.Game.PlayerCard(0).PeekDegrees, 1e-3f);
        }
    }
}
