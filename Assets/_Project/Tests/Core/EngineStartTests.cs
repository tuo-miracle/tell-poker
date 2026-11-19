using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>開局、盲注、按鈕輪轉、跳過出局玩家、單挑。</summary>
    public class EngineStartTests
    {
        [Test]
        public void ThreeHanded_ExplicitButton_BlindsLeftOfButton_UtgActsFirst()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            Assert.AreEqual(0, t.S.Button);
            Assert.AreEqual(1, t.S.SmallBlindSeat);
            Assert.AreEqual(2, t.S.BigBlindSeat);
            CollectionAssert.AreEqual(new[] { 1000, 950, 900 }, t.Stacks);
            Assert.AreEqual(0, t.S.ToAct);
            Assert.AreEqual(100, t.S.CurrentBet);
            Assert.AreEqual(100, t.S.LastFullRaise);
            var types = t.Events.Select(e => e.GetType()).ToArray();
            CollectionAssert.AreEqual(new[] { typeof(HandStarted), typeof(BlindPosted), typeof(BlindPosted), typeof(HoleCardsDealt), typeof(HoleCardsDealt), typeof(HoleCardsDealt) }, types);
            var blinds = t.Of<BlindPosted>().ToArray();
            Assert.AreEqual((1, BlindType.Small, 50), (blinds[0].Seat, blinds[0].Type, blinds[0].Amount));
            Assert.AreEqual((2, BlindType.Big, 100), (blinds[1].Seat, blinds[1].Type, blinds[1].Amount));
        }

        [Test]
        public void HeadsUp_ButtonIsSmallBlind_ActsFirstPreflop_LastPostflop()
        {
            var t = Table.New(1000, 1000).Start(0);
            Assert.AreEqual(0, t.S.Button);
            Assert.AreEqual(0, t.S.SmallBlindSeat);
            Assert.AreEqual(1, t.S.BigBlindSeat);
            Assert.AreEqual(0, t.S.ToAct, "單挑翻牌前按鈕（小盲）先行動");
            t.Call(0);
            Assert.AreEqual(1, t.S.ToAct, "大盲有選擇權");
            Assert.IsTrue(t.Legal(1).CanCheck);
            t.Check(1);
            Assert.AreEqual(Street.Flop, t.S.Street);
            Assert.AreEqual(1, t.S.ToAct, "翻牌後大盲先行動");
            t.Check(1);
            Assert.AreEqual(0, t.S.ToAct, "按鈕最後行動");
        }

        [Test]
        public void FirstHand_NullButton_UsesLowestSeatWithChips()
        {
            var t = Table.New(0, 500, 500, 500).Start(null);
            Assert.AreEqual(1, t.S.Button);
            Assert.AreEqual(2, t.S.SmallBlindSeat);
            Assert.AreEqual(3, t.S.BigBlindSeat);
            Assert.AreEqual(SeatStatus.Out, t.S.Seat(0).Status);
        }

        [Test]
        public void AutoRotation_ButtonAndBlindsAdvanceOneSeat()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            t.Fold(0).Fold(1);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            t.Start(null);
            Assert.AreEqual((1, 2, 0), (t.S.Button, t.S.SmallBlindSeat, t.S.BigBlindSeat));
            Assert.AreEqual(1, t.S.ToAct);
            t.Fold(1).Fold(2);
            t.Start(null);
            Assert.AreEqual((2, 0, 1), (t.S.Button, t.S.SmallBlindSeat, t.S.BigBlindSeat));
            Assert.AreEqual(3, t.S.HandNumber);
        }

        [Test]
        public void AutoRotation_SkipsEmptySeat()
        {
            var t = Table.New(1000, 1000, 1000, 0).Start(0);
            t.Fold(0).Fold(1);
            t.Start(null);
            Assert.AreEqual((1, 2, 0), (t.S.Button, t.S.SmallBlindSeat, t.S.BigBlindSeat), "大盲跳過空位 3 到座位 0");
            Assert.AreEqual(SeatStatus.Out, t.S.Seat(3).Status);
            Assert.IsFalse(t.Of<HoleCardsDealt>().Any(e => e.Seat == 3));
        }

        [Test]
        public void DeadSmallBlind_WhenPreviousBigBlindBusts()
        {
            // 座位 2（大盲，100 籌碼）貼盲注即 all-in 並輸光。下一手：大盲前進到 3，小盲位置是已出局的 2 → 沒有小盲；按鈕移到 1。
            var t = Table.New(1000, 1000, 100, 1000).Start(0, holes: "0:4c4d 1:AsAh 2:7c2d 3:KsKh", board: "Ad Kd 9c 5h 3s");
            Assert.AreEqual(SeatStatus.AllIn, t.S.Seat(2).Status);
            t.Call(3).Fold(0).Call(1);
            Assert.AreEqual(Street.Flop, t.S.Street);
            t.CheckDown();
            Assert.AreEqual(0, t.Stacks[2]);
            t.Start(null);
            Assert.AreEqual(1, t.S.Button);
            Assert.AreEqual(-1, t.S.SmallBlindSeat);
            Assert.AreEqual(3, t.S.BigBlindSeat);
            var blinds = t.Last.OfType<BlindPosted>().ToArray();
            Assert.AreEqual(1, blinds.Length);
            Assert.AreEqual(BlindType.Big, blinds[0].Type);
            Assert.AreEqual(0, t.S.ToAct);
        }

        [Test]
        public void HeadsUpTransition_NoPlayerHasBigBlindTwiceInARow()
        {
            // 第一手：按鈕 0、小盲 1、大盲 2；座位 0 出局。單挑時座位 2 不能再當大盲（TDA Rule 36-C）。
            var t = Table.New(100, 1000, 1000).Start(0, holes: "0:7c2d 1:AsAh 2:KsKh", board: "Ad Kd 9c 5h 3s");
            t.AllIn(0).Call(1).Check(2);
            t.CheckDown();
            Assert.AreEqual(0, t.Stacks[0]);
            t.Start(null);
            Assert.AreEqual(1, t.S.BigBlindSeat);
            Assert.AreEqual(2, t.S.Button);
            Assert.AreEqual(2, t.S.SmallBlindSeat);
            Assert.AreEqual(2, t.S.ToAct);
            Assert.AreEqual(SeatStatus.Out, t.S.Seat(0).Status);
        }

        [Test]
        public void ShortBigBlind_PostsAllIn_CallLevelIsPostedAmount_MinRaiseStillOneBigBlind()
        {
            var t = Table.New(1000, 1000, 60).Start(0);
            var bb = t.Of<BlindPosted>().Single(b => b.Type == BlindType.Big);
            Assert.AreEqual(60, bb.Amount);
            Assert.IsTrue(bb.IsAllIn);
            Assert.AreEqual(SeatStatus.AllIn, t.S.Seat(2).Status);
            Assert.AreEqual(60, t.S.CurrentBet);
            var la = t.Legal(0);
            Assert.AreEqual(60, la.CallAmount);
            Assert.IsTrue(la.CanRaise);
            Assert.AreEqual(160, la.MinTo);
        }

        [Test]
        public void BothPlayersAllInFromBlinds_HandRunsOutImmediately()
        {
            var t = Table.New(30, 80).Start(0, holes: "0:AsAh 1:KsKh", board: "2c 3d 7h 8s Jc");
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            Assert.AreEqual(3, t.Of<StreetDealt>().Count());
            Assert.AreEqual(50, t.Of<UncalledBetReturned>().Single(u => u.Seat == 1).Amount);
            CollectionAssert.AreEqual(new[] { 60, 50 }, t.Of<HandEnded>().Single().FinalStacks);
        }

        [Test]
        public void StartHand_Errors_LeaveStateUnchanged()
        {
            var t = Table.New(1000, 0, 0);
            Assert.AreEqual(EngineErrorCode.NotEnoughPlayers, t.Reject(new StartHand(1, null, 50, 100)).Code);
            t = Table.New(1000, 1000, 0);
            Assert.AreEqual(EngineErrorCode.InvalidBlinds, t.Reject(new StartHand(1, 0, 100, 50)).Code);
            Assert.AreEqual(EngineErrorCode.InvalidBlinds, t.Reject(new StartHand(1, 0, 0, 100)).Code);
            Assert.AreEqual(EngineErrorCode.InvalidButton, t.Reject(new StartHand(1, 2, 50, 100)).Code);
            Assert.AreEqual(EngineErrorCode.InvalidButton, t.Reject(new StartHand(1, 5, 50, 100)).Code);
            Assert.AreEqual(EngineErrorCode.InvalidPreset, t.Reject(new StartHand(1, 0, 50, 100, CardPreset.Parse(new System.Collections.Generic.Dictionary<int, string> { { 0, "AsAs" } }, ""))).Code);
            Assert.AreEqual(EngineErrorCode.InvalidPreset, t.Reject(new StartHand(1, 0, 50, 100, CardPreset.Parse(new System.Collections.Generic.Dictionary<int, string> { { 2, "AsKs" } }, ""))).Code);
            t.Start(0);
            Assert.AreEqual(EngineErrorCode.HandInProgress, t.Reject(new StartHand(2, null, 50, 100)).Code);
        }

        [Test]
        public void SameSeedAndCommands_ReplayToIdenticalEvents()
        {
            string Run()
            {
                var t = Table.New(1000, 700, 1500, 300).Start(1, seed: 4242);
                t.Raise(0, 250).Call(1).Fold(2).AllIn(3).Call(0).Call(1);
                t.CheckDown();
                return string.Join("\n", t.Events.Select(Table.Describe));
            }
            var a = Run();
            Assert.AreEqual(a, Run());
            StringAssert.Contains("HandEnded", a);
        }

        [Test]
        public void DifferentSeeds_DealDifferentCards()
        {
            var a = Table.New(1000, 1000).Start(0, seed: 1).S.Seat(0);
            var b = Table.New(1000, 1000).Start(0, seed: 2).S.Seat(0);
            Assert.AreNotEqual(a.Hole1.ToString() + a.Hole2, b.Hole1.ToString() + b.Hole2);
        }

        [Test]
        public void Preset_HoleCardsAndBoardAreUsed_RestComesFromSeededDeck()
        {
            var t = Table.New(1000, 1000, 1000).Start(0, holes: "1:AsKs", board: "2c3d4h");
            Assert.AreEqual("AsKs", t.S.Seat(1).Hole1.ToString() + t.S.Seat(1).Hole2);
            t.Call(0).Call(1).Check(2);
            Assert.AreEqual("2c3d4h", string.Concat(t.S.Board.Select(c => c.ToString())));
            var all = Enumerable.Range(0, 3).SelectMany(i => new[] { t.S.Seat(i).Hole1, t.S.Seat(i).Hole2 }).Concat(t.S.Board).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), "不得重複發牌");
        }
    }
}
