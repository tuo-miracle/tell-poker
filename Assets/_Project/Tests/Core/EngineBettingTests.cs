using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>下注、最小加注（raise-to）、短 all-in 不重開（TDA Rule 45、49 的 Addendum 範例）、非法行動。</summary>
    public class EngineBettingTests
    {
        [Test]
        public void BigBlindOption_AfterLimps_CanCheckOrRaise()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Call(0).Call(1);
            Assert.AreEqual(2, t.S.ToAct);
            var la = t.Legal(2);
            Assert.IsTrue(la.CanCheck);
            Assert.IsFalse(la.CanCall);
            Assert.IsTrue(la.CanRaise);
            Assert.AreEqual(200, la.MinTo);
            t.Check(2);
            Assert.AreEqual(Street.Flop, t.S.Street);
            Assert.AreEqual(1, t.S.ToAct, "翻牌後由按鈕左邊第一位先行動");
        }

        [Test]
        public void MinRaise_IsLargestPriorFullIncrement_Tda45Example1()
        {
            // TDA 45 Addendum Ex.1：盲注 100-200，翻牌後 A 開 600、B 加到 1600、C 再加到 3600；D 最小加注到 5600。
            var t = Table.New(10000, 10000, 10000, 10000).Start(3, 100, 200);
            t.Call(2).Call(3).Call(0).Check(1);
            Assert.AreEqual(0, t.S.ToAct);
            t.Bet(0, 600).Raise(1, 1600);
            Assert.AreEqual(2600, t.Legal(2).MinTo);
            t.Reject(new Raise(2, 2599));
            t.Raise(2, 3600);
            Assert.AreEqual(5600, t.Legal(3).MinTo);
            Assert.AreEqual(2000, t.S.LastFullRaise);
            Assert.AreEqual(EngineErrorCode.InvalidAmount, t.Reject(new Raise(3, 5599)).Code);
            t.Raise(3, 5600);
        }

        [Test]
        public void BetAndRaise_AreDistinct_AndBetMustBeAtLeastBigBlind()
        {
            var t = Table.New(1000, 1000).Start(0).Call(0).Check(1);
            Assert.AreEqual(EngineErrorCode.InvalidAmount, t.Reject(new Bet(1, 99)).Code);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new Raise(1, 300)).Code, "沒人下注時不能 Raise");
            var la = t.Legal(1);
            Assert.IsTrue(la.CanBet);
            Assert.IsFalse(la.CanRaise);
            Assert.AreEqual(100, la.MinTo);
            Assert.AreEqual(900, la.MaxTo);
            t.Bet(1, 100);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new Bet(0, 300)).Code, "已有下注時不能 Bet");
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new Check(0)).Code);
        }

        [Test]
        public void CallWithNothingToCall_IsRejected()
        {
            var t = Table.New(1000, 1000).Start(0).Call(0);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new Call(1)).Code);
        }

        [Test]
        public void OutOfTurn_AndInvalidSeat_AreRejected()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            Assert.AreEqual(EngineErrorCode.NotYourTurn, t.Reject(new Fold(1)).Code);
            Assert.AreEqual(EngineErrorCode.InvalidSeat, t.Reject(new Fold(7)).Code);
            Assert.AreEqual(EngineErrorCode.InvalidSeat, t.Reject(new Fold(-1)).Code);
            Assert.IsFalse(t.Legal(1).IsTurn);
            var idle = Table.New(1000, 1000);
            Assert.AreEqual(EngineErrorCode.NoHandInProgress, idle.Reject(new Fold(0)).Code);
        }

        [Test]
        public void RaiseAboveStack_Rejected_RaiseToExactStack_IsAllIn()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            Assert.AreEqual(EngineErrorCode.InvalidAmount, t.Reject(new Raise(0, 1001)).Code);
            t.Raise(0, 1000);
            var acted = t.Of<PlayerActed>().Last();
            Assert.IsTrue(acted.IsAllIn);
            Assert.AreEqual(ActionType.Raise, acted.Action);
            Assert.AreEqual(SeatStatus.AllIn, t.S.Seat(0).Status);
        }

        [Test]
        public void ShortAllIn_DoesNotReopenForPlayerWhoActed_Tda49Example3A()
        {
            // 盲注 2000-4000。A 跟 4000；C all-in 7500（+3500 不足完整加注）；SB 棄；BB 只跟 → A 只能跟 3500 或棄。
            var t = StartTda49Ex3();
            t.Fold(0).Call(1);
            Assert.AreEqual(2, t.S.ToAct);
            var la = t.Legal(2);
            Assert.IsFalse(la.CanRaise);
            Assert.IsFalse(la.CanAllIn);
            Assert.IsTrue(la.CanCall);
            Assert.AreEqual(3500, la.CallAmount);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new Raise(2, 11500)).Code);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new AllIn(2)).Code);
            t.Call(2);
            Assert.AreEqual(Street.Flop, t.S.Street);
        }

        [Test]
        public void ShortAllIn_PlayerWhoHasNotActed_CanRaise_ThenBettingReopens_Tda49Example3B()
        {
            var t = StartTda49Ex3();
            t.Fold(0);
            var bb = t.Legal(1);
            Assert.IsTrue(bb.CanRaise);
            Assert.AreEqual(11500, bb.MinTo, "最小加注仍是 4000（最後一次完整加注）");
            t.Raise(1, 11500);
            var a = t.Legal(2);
            Assert.IsTrue(a.CanRaise, "A 面對 7500 ≥ 完整加注，重開");
            Assert.AreEqual(15500, a.MinTo);
        }

        private static Table StartTda49Ex3()
        {
            // 座位：0=SB、1=BB、2=A（UTG）、3=C（按鈕，7500）
            var t = Table.New(100000, 100000, 100000, 7500).Start(3, 2000, 4000);
            Assert.AreEqual(2, t.S.ToAct);
            t.Call(2).AllIn(3);
            var acted = t.Of<PlayerActed>().Last();
            Assert.AreEqual(7500, acted.StreetBet);
            Assert.IsFalse(acted.IsFullRaise);
            Assert.AreEqual(4000, t.S.LastFullRaise);
            return t;
        }

        [Test]
        public void CumulativeShortAllIns_ReopenBetting_Tda49Example1()
        {
            // 盲注 50-100。翻牌後 A 開 100、B all-in 125、C 跟 125、D all-in 200、E 跟 200：A 面對 100 = 完整加注 → 重開。
            var t = Table.New(10000, 225, 10000, 300, 10000).Start(4);
            t.Call(2).Call(3).Call(4).Call(0).Check(1);
            Assert.AreEqual(0, t.S.ToAct);
            t.Bet(0, 100).AllIn(1).Call(2).AllIn(3).Call(4);
            var a = t.Legal(0);
            Assert.IsTrue(a.CanRaise);
            Assert.AreEqual(300, a.MinTo);
            // Ex.1-A：A 平跟 200；C 先前跟 125，現在只面對 75（不足完整加注）→ 不能加注。
            t.Call(0);
            var c = t.Legal(2);
            Assert.IsFalse(c.CanRaise);
            Assert.AreEqual(75, c.CallAmount);
        }

        [Test]
        public void MultipleShortAllIns_MinRaiseStaysLastFullBet_Tda49Example2()
        {
            // 翻牌後 A 開 300、B all-in 500、C all-in 650、D all-in 800、E 跟 800 → F 最小加注到 1100。
            var t = Table.New(10000, 600, 750, 900, 10000, 10000).Start(5);
            t.Call(2).Call(3).Call(4).Call(5).Call(0).Check(1);
            t.Bet(0, 300).AllIn(1).AllIn(2).AllIn(3).Call(4);
            Assert.AreEqual(5, t.S.ToAct);
            Assert.AreEqual(300, t.S.LastFullRaise);
            var f = t.Legal(5);
            Assert.IsTrue(f.CanRaise);
            Assert.AreEqual(1100, f.MinTo);
        }

        [Test]
        public void FullRaiseAllIn_FollowedByShortAllIn_OnlyReopensForThoseFacingAFullRaise()
        {
            // PokerKit #45 類型：B 的 all-in 本身是完整加注，不能被算進「短 all-in 累計」。
            // A 開 100、B all-in 到 300（完整加注 200）、C 跟 300、D all-in 350（短 50）、E 棄。
            var t = Table.New(10000, 400, 10000, 450, 10000).Start(4);
            t.Call(2).Call(3).Call(4).Call(0).Check(1);
            t.Bet(0, 100).AllIn(1);
            Assert.IsTrue(t.Of<PlayerActed>().Last().IsFullRaise);
            t.Call(2).AllIn(3).Fold(4);
            var a = t.Legal(0);
            Assert.IsTrue(a.CanRaise, "A 自上次行動後面對 250 ≥ 200");
            Assert.AreEqual(550, a.MinTo);
            t.Call(0);
            Assert.IsFalse(t.Legal(2).CanRaise, "C 只面對 50");
            Assert.AreEqual(50, t.Legal(2).CallAmount);
        }

        [Test]
        public void NoRaiseAllowed_WhenEveryOpponentIsAllIn()
        {
            var t = Table.New(1000, 300).Start(0).Call(0).AllIn(1);
            var la = t.Legal(0);
            Assert.IsFalse(la.CanRaise);
            Assert.IsFalse(la.CanAllIn, "籌碼多於跟注額時 all-in 等於加注，沒有對手可回應");
            Assert.AreEqual(200, la.CallAmount);
            t.Call(0);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase, "直接發完並攤牌");
        }

        [Test]
        public void HeadsUp_ShortBigBlindAllIn_SmallBlindCallsOnlyTheDifference()
        {
            var t = Table.New(1000, 60).Start(0, holes: "0:AsAh 1:KsKh", board: "2c 3d 7h 8s Jc");
            var la = t.Legal(0);
            Assert.AreEqual(10, la.CallAmount);
            Assert.IsFalse(la.CanRaise);
            t.Call(0);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            CollectionAssert.AreEqual(new[] { 1060, 0 }, t.Stacks);
        }

        [Test]
        public void BigBlindAllInByPosting_SmallBlindStillGetsToAct()
        {
            // poker-ts #7：盲注後籌碼耗盡不可讓下注輪提前結束。
            var t = Table.New(1000, 1000, 80).Start(0);
            t.Fold(0);
            Assert.AreEqual(1, t.S.ToAct);
            Assert.AreEqual(30, t.Legal(1).CallAmount);
            Assert.IsFalse(t.Legal(1).CanRaise);
        }

        [Test]
        public void AllInCommand_ForLessThanCall_IsAnAllInCall()
        {
            var t = Table.New(1000, 1000, 250).Start(0).Raise(0, 400).Fold(1);
            var la = t.Legal(2);
            Assert.AreEqual(150, la.CallAmount, "籌碼不足時跟注額＝剩餘籌碼");
            Assert.IsTrue(la.CanAllIn);
            t.AllIn(2);
            var acted = t.Of<PlayerActed>().Last();
            Assert.AreEqual(ActionType.Call, acted.Action);
            Assert.IsTrue(acted.IsAllIn);
        }

        [Test]
        public void FoldIsAllowedEvenWhenCheckIsAvailable()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Call(0).Call(1);
            Assert.IsTrue(t.Legal(2).CanFold);
            t.Fold(2);
            Assert.AreEqual(Street.Flop, t.S.Street);
        }
    }
}
