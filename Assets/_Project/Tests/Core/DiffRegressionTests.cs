using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>差異測試（PokerKit 0.7.7）找到的案例，固定成測試。每個案例註明判定依據。</summary>
    public class DiffRegressionTests
    {
        [Test]
        public void NoRaise_WhenNoOtherPlayerCanPutInMoreThanTheCurrentBet_Diff39()
        {
            // 差異 id 39：座位 2 加到 175 後只剩 134，座位 0 all-in 1050。座位 1 雖然籌碼更多，但沒有任何人能跟超過 1050 的部分，
            // 加注毫無意義（多出的部分只會被退還）→ 只能跟注或棄牌。TDA 無明文；採 PokerKit 的判定（實務上等同只能跟注）。
            var t = Table.New(1050, 1411, 309).Start(2, 3, 7);
            t.Raise(2, 175).AllIn(0);
            var la = t.Legal(1);
            Assert.IsFalse(la.CanRaise);
            Assert.IsFalse(la.CanAllIn);
            Assert.AreEqual(1043, la.CallAmount);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new AllIn(1)).Code);
        }
    
        [Test]
        public void ShortAllInBySmallBlind_DoesNotReopenForLimper_Diff452()
        {
            // 差異 id 452（PokerKit 0.7.7 允許、本引擎拒絕）：盲注 5/10，座位 0 平跟 10，SB all-in 到 15（只多 5，短 all-in），BB 跟。
            // 座位 0 自上次行動後只面對 5 < 完整加注 10 → TDA 49-A：不重開，只能跟或棄。PokerKit 讓他加到 45，屬 PokerKit 錯誤。
            var t = Table.New(78, 10, 15, 59, 25).Start(1, 5, 10);
            t.Fold(4).Call(0).Call(1).AllIn(2).Call(3);
            Assert.AreEqual(0, t.S.ToAct);
            var la = t.Legal(0);
            Assert.IsFalse(la.CanRaise);
            Assert.AreEqual(5, la.CallAmount);
            Assert.AreEqual(EngineErrorCode.IllegalAction, t.Reject(new Raise(0, 45)).Code);
        }

        [Test]
        public void BigBlindIsNotAskedForAPointlessCheck_WhenEveryoneElseIsAllIn_Diff94()
        {
            // 差異 id 94：其餘玩家都 all-in 且不超過大盲，大盲沒有任何可做的事 → 直接亮牌發完（TDA 17）。PokerKit 會多要求一次 check。
            var t = Table.New(0, 5, 9, 26, 2440, 5).Start(1, 5, 10, holes: "1:6c9d 2:JcTc 3:QhTd 4:5h3s 5:QdQs", board: "6d8dQc7c7s");
            t.Fold(4).Call(5).Call(1).Call(2);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 25, 2440, 20 }, t.Stacks);
        }

        [Test]
        public void EachSidePotSplitSeparately_OddChipsPerLayer_Diff17()
        {
            // 差異 id 17：三人同為 6 高順子，跨 6 層池。TDA Rule 23「每個邊池各自分配」→ 每層各算零頭（共 5 枚，依按鈕左起）。
            // PokerKit 會在殺掉輸家後把「剩餘贏家集合相同」的池合併再分，零頭只剩 1 枚 → 座位 3/4 差 1。以 TDA 為準。
            var t = Table.New(19, 68, 283, 3257, 1281, 11, 2368, 2504).Start(3, 10, 25,
                holes: "0:QdTd 1:AsQh 2:8h7h 3:Qs3c 4:4c3h 5:JdTc 6:6c3s 7:9hTs", board: "4h8c6d2d5h");
            t.Raise(6, 36).Call(7).Call(0).AllIn(1).Call(2).Call(3).AllIn(4).AllIn(6).AllIn(7).Fold(2).Call(3);
            Assert.AreEqual(6, t.Of<PotAwarded>().Count());
            CollectionAssert.AreEqual(new[] { 0, 0, 215, 4417, 1765, 0, 3394, 0 }, t.Stacks);
        }

        [Test]
        public void TwoOddChips_GoOneEachClockwiseFromButton_Diff3()
        {
            // 差異 id 3：主池 8 三人平分 → 2 r2。本引擎：按鈕左起前兩位各 1 枚（座位 0、1）；PokerKit 兩枚都給第一位。
            // TDA 21 只寫「零頭給按鈕左邊第一位」，未規定多枚零頭；採逐枚分配（較公平、與 Rule 21 的順序一致）。
            var t = Table.New(2, 2643, 15, 27, 2643).Start(4, 5, 10, holes: "0:4sKc 1:8cAs 2:2cTd 3:2dJc 4:4d5h", board: "ThJsQcKdAc");
            t.Fold(2).Raise(3, 26).Raise(4, 58).Raise(1, 1127).Call(3).Fold(4);
            CollectionAssert.AreEqual(new[] { 3, 2688, 15, 39, 2585 }, t.Stacks);
        }

        [Test]
        public void HeadsUpEqualBlinds_ButtonStillActsFirstPreflop_Diff416()
        {
            // 差異 id 416：盲注 2/2 單挑。TDA 36-C：按鈕＝小盲、翻牌前先行動。PokerKit 在大小盲相等時讓大盲先行動（其 opener 判定以金額決定）。
            var t = Table.New(5, 54).Start(0, 2, 2);
            Assert.AreEqual(0, t.S.ToAct);
            Assert.IsTrue(t.Legal(0).CanCheck);
        }
    
        [Test]
        public void CumulativeShortAllInsReachingFullRaise_ReopenForEarlierRaiser_Diff2878()
        {
            // 差異 id 2878（20k 回合）：座位 8 在 775 行動（最後完整加注 525）。之後 1181、1470、1711 三個短 all-in，
            // 累計 936 ≥ 525 → TDA 49-A（Addendum Ex.1）：累計達完整加注即重開。PokerKit 0.7.7 拒絕座位 8 再加注，屬 PokerKit 錯誤。
            var t = Table.New(2180, 0, 1181, 1470, 8, 1711, 70, 40, 4685).Start(3, 10, 25);
            t.AllIn(6).Fold(7).Raise(8, 115).Raise(0, 160).Call(2).Raise(3, 205).Raise(5, 250).Raise(8, 775).Call(0)
             .AllIn(2).AllIn(3).AllIn(5);
            Assert.AreEqual(8, t.S.ToAct);
            Assert.AreEqual(525, t.S.LastFullRaise);
            var la = t.Legal(8);
            Assert.IsTrue(la.CanRaise);
            Assert.AreEqual(1711 + 525, la.MinTo);
            t.AllIn(8).Call(0);
            Assert.AreEqual(HandPhase.Idle, t.S.Phase);
        }
    }
}
