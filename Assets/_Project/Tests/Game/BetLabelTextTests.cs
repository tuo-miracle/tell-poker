using NUnit.Framework;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>下注標籤的文字：金額全部讀 Core 的狀態（本街下注、跟注水位、剩餘籌碼）。四人桌：0 玩家、1 Pip、2 Rocco、3 Vex。</summary>
    public class BetLabelTextTests
    {
        /// <summary>按鈕在 3 號 → 0 號小盲 5、1 號大盲 10、翻牌前 2 號先說話。</summary>
        static HandState Start(params int[] stacks)
        {
            var r = Engine.Apply(HandState.NewTable(stacks), new StartHand(1, 3, 5, 10));
            Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
            return r.State;
        }

        static HandState Do(HandState s, PlayerCommand c)
        {
            var r = Engine.Apply(s, c);
            Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
            return r.State;
        }

        [Test]
        public void NormalCall_ShowsTheAmountToAdd()
        {
            var s = Start(1000, 1000, 1000, 1000);
            s = Do(s, new Raise(2, 120));
            s = Do(s, new Fold(3));
            Assert.AreEqual(0, s.ToAct);
            Assert.AreEqual(115, BetLabelText.AmountToCall(s, 0), "120 − 小盲 5");
            Assert.AreEqual("To call: 115", BetLabelText.ToCallLine(s, 0));
            Assert.AreEqual("Rocco bet 120", BetLabelText.BetLine("Rocco", s.Seat(2).StreetBet));
        }

        [Test]
        public void NothingToAdd_OnYourTurn_SaysCheck()
        {
            // 按鈕在 2 號 → 3 號小盲、0 號（玩家）大盲；大家平跟，輪到玩家可以過牌
            var s = Do(Do(Do(Engine.Apply(HandState.NewTable(new[] { 1000, 1000, 1000, 1000 }), new StartHand(1, 2, 5, 10)).State,
                new Call(1)), new Call(2)), new Call(3));
            Assert.AreEqual(0, s.ToAct);
            Assert.AreEqual(0, BetLabelText.AmountToCall(s, 0));
            Assert.AreEqual("Check", BetLabelText.ToCallLine(s, 0));
        }

        [Test]
        public void CannotCover_ShowsAllInWithTheRemainingStack()
        {
            var s = Start(50, 1000, 1000, 1000);   // 小盲 5 之後剩 45
            s = Do(s, new Raise(2, 120));
            s = Do(s, new Call(3));
            Assert.AreEqual(45, BetLabelText.AmountToCall(s, 0));
            Assert.AreEqual("All-in 45", BetLabelText.ToCallLine(s, 0));
        }

        [Test]
        public void MultipleRaisers_ToCallFollowsTheHighestBet_NotThePileYouLookAt()
        {
            var s = Start(1000, 1000, 1000, 1000);
            s = Do(s, new Raise(2, 40));
            s = Do(s, new Raise(3, 120));
            Assert.AreEqual("Rocco bet 40", BetLabelText.BetLine("Rocco", s.Seat(2).StreetBet));
            Assert.AreEqual("Vex bet 120", BetLabelText.BetLine("Vex", s.Seat(3).StreetBet));
            Assert.AreEqual("To call: 115", BetLabelText.ToCallLine(s, 0));
        }

        [Test]
        public void NotYourTurnYet_StillShowsWhatYouWillFace()
        {
            // 2 號加注後輪到 3 號，玩家（小盲）還沒輪到：跟注額照樣算（Core 的 LegalActions 只在輪到時有值）
            var s = Start(1000, 1000, 1000, 1000);
            s = Do(s, new Raise(2, 120));
            Assert.AreEqual(3, s.ToAct);
            Assert.AreEqual("To call: 115", BetLabelText.ToCallLine(s, 0));
        }

        [Test]
        public void FoldedOrAllIn_NoToCallLine()
        {
            var s = Start(1000, 1000, 1000, 1000);
            s = Do(s, new Raise(2, 120));
            s = Do(s, new Call(3));
            var folded = Do(s, new Fold(0));
            Assert.IsNull(BetLabelText.ToCallLine(folded, 0));
            Assert.AreEqual(0, BetLabelText.AmountToCall(folded, 0));
            var allIn = Do(s, new AllIn(0));
            Assert.IsNull(BetLabelText.ToCallLine(allIn, 0));
        }

        [Test]
        public void AlreadyMatched_OffTurn_NoLine()
        {
            var s = Start(1000, 1000, 1000, 1000);
            s = Do(s, new Raise(2, 120));
            s = Do(s, new Call(3));
            s = Do(s, new Call(0));   // 玩家跟到 120，輪到 Pip
            Assert.AreEqual(1, s.ToAct);
            Assert.IsNull(BetLabelText.ToCallLine(s, 0));
        }

        [Test]
        public void PotLine()
        {
            Assert.AreEqual("Pot 340", BetLabelText.PotLine(340));
        }
    }
}
