using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>InterpretChipCommit：玩家推出 N 枚籌碼 → 合法動作（TDA 2026 Rule 45-A、47、49-A、53-B、54-A）。</summary>
    public class ChipCommitTests
    {
        private static ChipCommitInterpretation I(Table t, int seat, int amount) => Engine.InterpretChipCommit(t.S, seat, amount);

        [Test]
        public void ExactCallAmount_IsCall()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            var r = I(t, 0, 100);
            Assert.AreEqual(ChipCommitKind.Call, r.Kind);
            Assert.IsInstanceOf<Call>(r.Command);
            Assert.AreEqual(0, r.Adjustment);
        }

        [Test]
        public void AtLeastMinRaise_IsRaiseToThatTotal()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            var r = I(t, 0, 250);
            Assert.AreEqual(ChipCommitKind.Raise, r.Kind);
            Assert.AreEqual(250, ((Raise)r.Command).ToAmount);
            Assert.AreEqual(0, r.Adjustment);
            t.Do(r.Command);
            Assert.AreEqual(250, t.S.CurrentBet);
        }

        [Test]
        public void FiftyPercentOfMinRaise_ForcesFullMinRaise_Tda47Example2()
        {
            // TDA 47 Addendum Ex.2：UTG 下注到 1100，最小加注 600，50% = 300 → 推出 ≥1400 須補成 1700。
            var t = Table.New(10000, 10000, 10000, 10000).Start(3, 250, 500).Raise(2, 1100);
            Assert.AreEqual(600, t.S.LastFullRaise);
            var r = I(t, 3, 1400);
            Assert.AreEqual(ChipCommitKind.Raise, r.Kind);
            Assert.AreEqual(1700, ((Raise)r.Command).ToAmount);
            Assert.AreEqual(300, r.Adjustment);
            StringAssert.Contains("45-A", r.Rule);
            t.Do(r.Command);
        }

        [Test]
        public void BelowFiftyPercent_IsCall_ExcessReturned_Tda47Example3()
        {
            var t = Table.New(10000, 10000, 10000, 10000).Start(3, 250, 500).Raise(2, 1100);
            var r = I(t, 3, 1399);
            Assert.AreEqual(ChipCommitKind.Call, r.Kind);
            Assert.AreEqual(-299, r.Adjustment);
        }

        [TestCase(1400, ChipCommitKind.Call, 1000, TestName = "45-A example: 1400 vs opening 1000 is a call")]
        [TestCase(1499, ChipCommitKind.Call, 1000, TestName = "just under 50% is a call")]
        [TestCase(1500, ChipCommitKind.Raise, 2000, TestName = "exactly 50% forces min raise")]
        [TestCase(2500, ChipCommitKind.Raise, 2500, TestName = "above min raise stays as pushed")]
        public void PostflopOpeningBet1000(int pushed, ChipCommitKind kind, int committed)
        {
            var t = Table.New(10000, 10000, 10000).Start(0, 100, 200).Call(0).Call(1).Check(2).Bet(1, 1000);
            var r = I(t, 2, pushed);
            Assert.AreEqual(kind, r.Kind);
            Assert.AreEqual(committed, r.ChipsCommitted);
            t.Do(r.Command);
        }

        [Test]
        public void LastChips_AreAllIn_EvenBelowFiftyPercent_Tda47B2()
        {
            var t = Table.New(10000, 10000, 1500).Start(0, 100, 200).Call(0).Call(1).Check(2).Bet(1, 1000);
            Assert.AreEqual(1300, t.S.Seat(2).Stack);
            var r = I(t, 2, 1300);
            Assert.AreEqual(ChipCommitKind.AllIn, r.Kind);
            Assert.IsInstanceOf<AllIn>(r.Command);
            Assert.AreEqual(0, r.Adjustment);
            t.Do(r.Command);
            Assert.AreEqual(1300, t.S.CurrentBet);
        }

        [Test]
        public void ForcedMinRaise_WithoutEnoughChips_BecomesAllIn()
        {
            var t = Table.New(10000, 10000, 1900).Start(0, 100, 200).Call(0).Call(1).Check(2).Bet(1, 1000);
            var r = I(t, 2, 1600); // 1700 籌碼，推 1600：多出 600 ≥ 500 → 須最小加注到 2000，不夠 → all-in 1700
            Assert.AreEqual(ChipCommitKind.AllIn, r.Kind);
            Assert.AreEqual(100, r.Adjustment);
        }

        [Test]
        public void OpeningBetBelowMinimum_IsCorrectedUpToMinimum_Tda54A()
        {
            var t = Table.New(10000, 10000, 10000).Start(0, 100, 200).Call(0).Call(1).Check(2);
            var r = I(t, 1, 50);
            Assert.AreEqual(ChipCommitKind.Bet, r.Kind);
            Assert.AreEqual(200, ((Bet)r.Command).Amount);
            Assert.AreEqual(150, r.Adjustment);
            StringAssert.Contains("54-A", r.Rule);
        }

        [Test]
        public void ZeroChips_IsCheckWhenNothingToCall_InvalidWhenFacingBet()
        {
            var t = Table.New(10000, 10000, 10000).Start(0, 100, 200).Call(0).Call(1).Check(2);
            Assert.AreEqual(ChipCommitKind.Check, I(t, 1, 0).Kind);
            t.Bet(1, 200);
            var r = I(t, 2, 0);
            Assert.AreEqual(ChipCommitKind.Invalid, r.Kind);
            Assert.IsNull(r.Command);
        }

        [Test]
        public void Undercall_HeadsUp_IsMandatoryFullCall_Tda53B()
        {
            var t = Table.New(1000, 1000).Start(0);
            var r = I(t, 0, 20);
            Assert.AreEqual(ChipCommitKind.Call, r.Kind);
            Assert.AreEqual(30, r.Adjustment);
            StringAssert.Contains("53-B", r.Rule);
        }

        [Test]
        public void Undercall_MultiwayFacingOpeningBet_IsMandatoryFullCall_Tda53B()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            var r = I(t, 0, 50);
            Assert.AreEqual(ChipCommitKind.Call, r.Kind);
            Assert.AreEqual(50, r.Adjustment);
        }

        [Test]
        public void Undercall_MultiwayFacingRaise_IsInvalid_TdDiscretion()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Raise(0, 300);
            var r = I(t, 1, 100);
            Assert.AreEqual(ChipCommitKind.Invalid, r.Kind);
            StringAssert.Contains("53-B", r.Rule);
        }

        [Test]
        public void RaiseClosedByShortAllIn_ExtraChipsAreReturned_Tda49A()
        {
            var t = Table.New(100000, 100000, 100000, 7500).Start(3, 2000, 4000).Call(2).AllIn(3).Fold(0).Call(1);
            var r = I(t, 2, 20000);
            Assert.AreEqual(ChipCommitKind.Call, r.Kind);
            Assert.AreEqual(3500, r.ChipsCommitted);
            StringAssert.Contains("49-A", r.Rule);
        }

        [Test]
        public void BigBlindOption_SmallPushIsCheck_FiftyPercentForcesMinRaise()
        {
            var t = Table.New(1000, 1000, 1000).Start(0).Call(0).Call(1);
            var small = I(t, 2, 40);
            Assert.AreEqual(ChipCommitKind.Check, small.Kind);
            Assert.AreEqual(-40, small.Adjustment);
            var half = I(t, 2, 50);
            Assert.AreEqual(ChipCommitKind.Raise, half.Kind);
            Assert.AreEqual(200, ((Raise)half.Command).ToAmount);
        }

        [Test]
        public void NotYourTurn_OrMoreThanStack_OrNegative_IsInvalid()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            Assert.AreEqual(ChipCommitKind.Invalid, I(t, 1, 100).Kind);
            Assert.AreEqual(ChipCommitKind.Invalid, I(t, 0, 1001).Kind);
            Assert.AreEqual(ChipCommitKind.Invalid, I(t, 0, -5).Kind);
            Assert.AreEqual(ChipCommitKind.Invalid, I(t, 9, 100).Kind);
        }

        [Test]
        public void EveryNonInvalidInterpretation_IsAcceptedByApply_AndCommitsTheStatedChips()
        {
            var t = Table.New(5000, 5000, 5000, 5000).Start(0, 100, 200).Raise(3, 600);
            for (int amount = 0; amount <= 5000; amount += 37)
            {
                var r = I(t, 0, amount);
                if (r.Kind == ChipCommitKind.Invalid) continue;
                var before = t.S.Seat(0).Stack;
                var applied = Engine.Apply(t.S, r.Command);
                Assert.IsTrue(applied.IsSuccess, $"amount={amount} kind={r.Kind} err={applied.Error}");
                Assert.AreEqual(r.ChipsCommitted, before - applied.State.Seat(0).Stack, $"amount={amount}");
            }
        }
    }
}
