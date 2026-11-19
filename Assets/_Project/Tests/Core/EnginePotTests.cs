using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>街的推進、只剩一人、all-in 直接發完、邊池、退還、平分與零頭、攤牌順序。</summary>
    public class EnginePotTests
    {
        private const string Royal = "As Ks Qs Js Ts";

        [Test]
        public void Streets_AreDealtInOrder_WithPotsUpdatedAtEachRoundEnd()
        {
            var t = Table.New(1000, 1000).Start(0, board: "2c 3d 4h 5s 9c").Call(0).Check(1);
            Assert.AreEqual(3, t.S.Board.Count);
            Assert.IsInstanceOf<PotsUpdated>(t.Last[1]); // PlayerActed, PotsUpdated, StreetDealt
            Assert.IsInstanceOf<StreetDealt>(t.Last[2]);
            Assert.AreEqual(200, ((PotsUpdated)t.Last[1]).Pots.Single().Amount);
            t.CheckAround();
            Assert.AreEqual(Street.Turn, t.S.Street);
            Assert.AreEqual(4, t.S.Board.Count);
            t.CheckAround();
            Assert.AreEqual(Street.River, t.S.Street);
            Assert.AreEqual(5, t.S.Board.Count);
            var dealt = t.Of<StreetDealt>().ToArray();
            CollectionAssert.AreEqual(new[] { Street.Flop, Street.Turn, Street.River }, dealt.Select(d => d.Street));
            CollectionAssert.AreEqual(new[] { 3, 1, 1 }, dealt.Select(d => d.NewCards.Count));
            Assert.AreEqual("2c3d4h5s9c", string.Concat(dealt[2].Board.Select(c => c.ToString())));
        }

        [Test]
        public void PostflopOrder_StartsLeftOfButton_EvenAfterButtonFolds()
        {
            var t = Table.New(1000, 1000, 1000, 1000).Start(0).Call(3).Fold(0).Call(1).Check(2);
            Assert.AreEqual(Street.Flop, t.S.Street);
            Assert.AreEqual(1, t.S.ToAct);
            t.Check(1).Check(2);
            Assert.AreEqual(3, t.S.ToAct);
            t.Check(3);
            Assert.AreEqual(Street.Turn, t.S.Street);
            Assert.AreEqual(1, t.S.ToAct);
        }

        [Test]
        public void OnlyOnePlayerLeft_EndsImmediately_NoShowdown_UncalledReturned()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Raise(0, 300).Fold(1).Fold(2);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            Assert.AreEqual(-1, t.S.ToAct);
            Assert.IsFalse(t.Of<ShowdownRevealed>().Any());
            Assert.IsFalse(t.Of<StreetDealt>().Any());
            var refund = t.Of<UncalledBetReturned>().Single();
            Assert.AreEqual((0, 200), (refund.Seat, refund.Amount));
            var award = t.Of<PotAwarded>().Single();
            Assert.AreEqual(250, award.Amount);
            Assert.AreEqual(0, award.Shares.Single().Seat);
            Assert.IsNull(award.WinningHand);
            CollectionAssert.AreEqual(new[] { 1150, 950, 900 }, t.Of<HandEnded>().Single().FinalStacks);
            Assert.IsInstanceOf<HandEnded>(t.Events.Last());
        }

        [Test]
        public void RiverBetUncalled_IsReturned()
        {
            var t = Table.New(1000, 1000).Start(0).Call(0).Check(1).CheckAround().CheckAround();
            Assert.AreEqual(Street.River, t.S.Street);
            t.Bet(1, 500).Fold(0);
            Assert.AreEqual(500, t.Of<UncalledBetReturned>().Single().Amount);
            CollectionAssert.AreEqual(new[] { 900, 1100 }, t.Stacks);
        }

        [Test]
        public void PreflopAllInAndCall_TablesUpThenRunsOutBoard()
        {
            var t = Table.New(1000, 1000).Start(0, holes: "0:AsAh 1:KsKh", board: "2c 3d 7h 8s Jc").AllIn(0).Call(1);
            var ev = t.Last.ToList();
            int reveal = ev.FindIndex(e => e is ShowdownRevealed);
            int flop = ev.FindIndex(e => e is StreetDealt);
            Assert.That(reveal, Is.GreaterThanOrEqualTo(0));
            Assert.Less(reveal, flop, "TDA Rule 17：all-in 且下注結束時先亮牌再發完");
            Assert.IsTrue(((ShowdownRevealed)ev[reveal]).IsAllInTableUp);
            Assert.AreEqual(3, ev.OfType<StreetDealt>().Count());
            CollectionAssert.AreEqual(new[] { 2000, 0 }, t.Stacks);
        }

        [Test]
        public void AllInOnTurn_CallerWithChipsLeft_IsNotAskedToActAgain()
        {
            var t = Table.New(500, 1000).Start(0, holes: "0:AsAh 1:KsKh", board: "2c 3d 7h 8s Jc").Call(0).Check(1);
            t.CheckAround();
            Assert.AreEqual(Street.Turn, t.S.Street);
            t.Bet(1, 200).AllIn(0).Call(1);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            CollectionAssert.AreEqual(new[] { 1000, 500 }, t.Stacks);
        }

        [Test]
        public void AllInPlayerInPot_OthersKeepBetting_TableUpOnlyWhenNoMoreActionPossible_Tda17Example2()
        {
            // A（短碼）翻牌前 all-in，B、C 跟；B、C 還有籌碼 → 先不亮牌。轉牌 B all-in、C 跟 → 這時才亮牌。
            var t = Table.New(200, 1000, 1000).Start(2, holes: "0:AsAh 1:KsKh 2:QsQh", board: "2c 3d 7h 8s Jc");
            t.Call(2).AllIn(0).Call(1).Call(2);
            Assert.AreEqual(Street.Flop, t.S.Street);
            Assert.IsFalse(t.Of<ShowdownRevealed>().Any());
            Assert.AreEqual(1, t.S.ToAct);
            t.Check(1).Check(2);
            Assert.AreEqual(Street.Turn, t.S.Street);
            Assert.IsFalse(t.Of<ShowdownRevealed>().Any());
            t.AllIn(1).Call(2);
            var reveal = t.Of<ShowdownRevealed>().Single();
            Assert.IsTrue(reveal.IsAllInTableUp);
            Assert.AreEqual(3, reveal.Hands.Count);
            CollectionAssert.AreEqual(new[] { 600, 1600, 0 }, t.Stacks);
        }

        [Test]
        public void SidePots_LayeredByAllInLevels_UncalledTopReturned()
        {
            // 籌碼 100／300／1000 全 all-in：主池 300（三人）、邊池 400（兩人）、多出的 700 退還。
            var t = Table.New(100, 300, 1000).Start(2, holes: "0:AsAh 1:KsKh 2:QsQh", board: "2c 3d 7h 8s Jc");
            t.AllIn(2).AllIn(0).AllIn(1);
            var refund = t.Of<UncalledBetReturned>().Single();
            Assert.AreEqual((2, 700), (refund.Seat, refund.Amount));
            var pots = t.Of<PotsUpdated>().Last().Pots;
            Assert.AreEqual(2, pots.Count);
            Assert.AreEqual(300, pots[0].Amount);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, pots[0].EligibleSeats);
            Assert.AreEqual(400, pots[1].Amount);
            CollectionAssert.AreEqual(new[] { 1, 2 }, pots[1].EligibleSeats);
            var awards = t.Of<PotAwarded>().ToArray();
            Assert.AreEqual(0, awards[0].Shares.Single().Seat);
            Assert.AreEqual(1, awards[1].Shares.Single().Seat);
            CollectionAssert.AreEqual(new[] { 300, 400, 700 }, t.Stacks);
        }

        [Test]
        public void FoldedPlayersChipsStayInPot_FoldedPlayerCannotWin()
        {
            var t = Table.New(1000, 1000, 1000).Start(0, holes: "0:KsKh 1:7c2d 2:AsAh", board: "3c 4d 8h 9s Jc");
            t.Raise(0, 300).Call(1).Fold(2);
            var pot = t.Of<PotsUpdated>().First().Pots.Single();
            Assert.AreEqual(700, pot.Amount);
            CollectionAssert.AreEqual(new[] { 0, 1 }, pot.EligibleSeats);
            t.CheckDown();
            CollectionAssert.AreEqual(new[] { 1400, 700, 900 }, t.Stacks);
        }

        [Test]
        public void SplitPot_OddChipGoesToFirstWinnerLeftOfButton()
        {
            // 盲注 5/10；按鈕 0、SB 1（棄牌）、BB 2。池 25，0 與 2 平分 → 12／13，零頭給按鈕左邊第一位贏家＝座位 2。
            var t = Table.New(1000, 1000, 1000).Start(0, 5, 10, holes: "0:2c3c 1:2d3d 2:2h3h", board: Royal);
            t.Call(0).Fold(1).Check(2).CheckDown();
            var award = t.Of<PotAwarded>().Single();
            Assert.AreEqual(25, award.Amount);
            CollectionAssert.AreEqual(new[] { 2 }, award.OddChipSeats);
            CollectionAssert.AreEqual(new[] { 2, 0 }, award.Shares.Select(s => s.Seat), "依按鈕左邊起的順序");
            CollectionAssert.AreEqual(new[] { 1002, 995, 1003 }, t.Stacks);
        }

        [Test]
        public void HeadsUp_SplitPot_EqualShares_ExcessReturned()
        {
            // 單挑無前注時池必為偶數（多出的部分會先退還），所以只驗證平分與退還。
            var t = Table.New(1000, 1005).Start(0, 5, 10, holes: "0:2c3c 1:2d3d", board: Royal);
            t.Raise(0, 1000);
            Assert.IsFalse(t.Legal(1).CanAllIn, "對手已 all-in，不能再加注");
            t.Call(1);
            var award = t.Of<PotAwarded>().Single();
            Assert.AreEqual(2000, award.Amount);
            CollectionAssert.AreEqual(new[] { 1000, 1000 }, award.Shares.Select(x => x.Amount));
            Assert.AreEqual(0, award.OddChipSeats.Count);
            CollectionAssert.AreEqual(new[] { 1000, 1005 }, t.Stacks);
        }

        [Test]
        public void SidePotsEachSplitSeparately_EachWithItsOwnOddChip()
        {
            // 盲注 5/10，按鈕 3、SB 0（A，31）、BB 1。C 加到 40、D 跟、A all-in 31、B 加到 80、C 跟、D 棄。
            // 主池 124（A,B,C 三分 → 41 r1，零頭給 A）、邊池 107（B,C 二分 → 53 r1，零頭給 B）。
            var t = Table.New(31, 1000, 1000, 1000).Start(3, 5, 10, holes: "0:2c3c 1:2d3d 2:2h3h 3:4c4d", board: Royal);
            t.Raise(2, 40).Call(3).AllIn(0).Raise(1, 80).Call(2).Fold(3);
            t.CheckDown();
            var awards = t.Of<PotAwarded>().ToArray();
            Assert.AreEqual(2, awards.Length);
            Assert.AreEqual(124, awards[0].Amount);
            CollectionAssert.AreEqual(new[] { 0 }, awards[0].OddChipSeats);
            Assert.AreEqual(107, awards[1].Amount);
            CollectionAssert.AreEqual(new[] { 1 }, awards[1].OddChipSeats);
            CollectionAssert.AreEqual(new[] { 42, 1015, 1014, 960 }, t.Stacks);
        }

        [Test]
        public void ThreeWaySplit_TwoOddChips_GoToFirstTwoWinnersLeftOfButton()
        {
            // 盲注 5/10，按鈕 0、SB 1（棄）、BB 2、UTG 3。池 35 三分 → 11 r2：座位 2、3 各多 1。
            var t = Table.New(1000, 1000, 1000, 1000).Start(0, 5, 10, holes: "0:2c3c 1:2d3d 2:2h3h 3:4c4d", board: Royal);
            t.Call(3).Call(0).Fold(1).Check(2).CheckDown();
            var award = t.Of<PotAwarded>().Single();
            Assert.AreEqual(35, award.Amount);
            CollectionAssert.AreEqual(new[] { 2, 3 }, award.OddChipSeats);
            CollectionAssert.AreEqual(new[] { 1001, 995, 1002, 1002 }, t.Stacks);
        }

        [Test]
        public void ShowdownOrder_LastRiverAggressorShowsFirst()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Call(0).Call(1).Check(2);
            t.CheckAround().CheckAround();
            Assert.AreEqual(Street.River, t.S.Street);
            t.Check(1).Bet(2, 100).Call(0).Call(1);
            var reveal = t.Of<ShowdownRevealed>().Single();
            Assert.IsFalse(reveal.IsAllInTableUp);
            CollectionAssert.AreEqual(new[] { 2, 0, 1 }, reveal.Hands.Select(h => h.Seat));
        }

        [Test]
        public void ShowdownOrder_NoRiverBet_FirstLeftOfButtonShowsFirst()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Call(0).Call(1).Check(2).CheckDown();
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, t.Of<ShowdownRevealed>().Single().Hands.Select(h => h.Seat));
        }

        [Test]
        public void Showdown_WinningHandIsReported()
        {
            var t = Table.New(1000, 1000).Start(0, holes: "0:AsAh 1:KsKh", board: "Ac 3d 7h 8s Jc").Call(0).Check(1).CheckDown();
            var award = t.Of<PotAwarded>().Single();
            Assert.AreEqual(HandCategory.ThreeOfAKind, award.WinningHand.Value.Category);
            Assert.AreEqual(0, award.Shares.Single().Seat);
        }

        [Test]
        public void AfterHandEnds_BustedPlayerIsOut_AndNextHandCanStart()
        {
            var t = Table.New(100, 300, 1000).Start(2, holes: "0:2c3c 1:KsKh 2:AsAh", board: "2d 5d 7h 8s Jc");
            t.AllIn(2).AllIn(0).AllIn(1);
            CollectionAssert.AreEqual(new[] { 0, 0, 1400 }, t.Stacks);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            var r = Engine.Apply(t.S, new StartHand(9, null, 50, 100));
            Assert.AreEqual(EngineErrorCode.NotEnoughPlayers, r.Error.Code);
        }
    }
}
