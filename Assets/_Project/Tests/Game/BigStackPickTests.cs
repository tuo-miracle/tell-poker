using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 2026-10-12 第二次實機：贏了很多籌碼後，籌碼堆往後多排好幾排，原本固定的捏起範圍（堆中心 9 cm 內）捏不到後排。
    /// 遊戲層要把玩家籌碼堆每一柱的位置餵給辨識器。
    /// </summary>
    public class BigStackPickTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        [Test]
        public void AfterWinningABigPot_PinchingTheBackRowOfTheGrownPile_PicksChips()
        {
            // 第一手：Pip（按鈕）加注到 900，玩家全下，Pip 棄牌 → 玩家贏 900，籌碼 1900（190 枚 = 10 柱）
            var cfg = new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 80 };
            _rig = new GameTestRig(cfg, g =>
            {
                g.AiOverride = st => st.Seat(0).Status == SeatStatus.AllIn ? (PlayerCommand)new Fold(st.ToAct) : new Raise(st.ToAct, 900);
                g.AutoPlayer = st => new AllIn(0);
            });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsPlayed == 1 && _rig.Game.Flow == GameFlow.HandOver, 60), _rig.Dump());
            Assert.AreEqual(1900, _rig.Game.Session.StackOf(0), "player won the pot");

            // 第二手輪到玩家（小盲先行動）時，改由手勢操作
            _rig.Game.AutoPlayer = null;
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.Idle(0.3);

            var pile = _rig.Game.Props.Stack[0];
            int cols = ChipStackMesh.Columns(pile.Chips);
            Assert.GreaterOrEqual(cols, 9, "the won chips grew the pile into several rows");
            var spots = _rig.Game.Props.Spots;
            var back = spots.StackOf(0) + ChipStackMesh.ColumnOffset(cols - 1);
            var center = spots.PlayerStack;
            Assert.Greater(Vector2.Distance(new Vector2(back.x, back.z), new Vector2(center.x, center.z)),
                new GestureTuning().ChipPickRadius, "the back row is outside the old fixed pick zone");

            int before = _rig.Gestures.Count;
            var s = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, 0.03f, new NV3(back.x, 0f, back.z) - G.Stack);
            _rig.Play(s);
            var picked = _rig.Gestures.Skip(before).Where(g => g.Kind == GestureKind.ChipsPicked).ToList();
            Assert.AreEqual(1, picked.Count, _rig.Dump());
            Assert.Greater(_rig.Game.Recognizer.HeldChips(Hand.Right), 0, "chips are in hand");
        }
    }
}
