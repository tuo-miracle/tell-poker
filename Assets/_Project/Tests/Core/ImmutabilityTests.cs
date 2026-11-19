using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>審查發現：指令與事件不可被外部轉型回陣列竄改；總籌碼不可溢位。</summary>
    public class ImmutabilityTests
    {
        [Test]
        public void EventCollections_CannotBeCastBackToMutableArrays()
        {
            var t = Table.New(1000, 1000).Start(0).Call(0).Check(1).CheckDown();
            foreach (var e in t.Events)
                foreach (var p in e.GetType().GetProperties())
                {
                    var v = p.GetValue(e);
                    if (v is string || v == null) continue;
                    Assert.IsFalse(v is System.Array, $"{e.GetType().Name}.{p.Name} 是可變陣列");
                    if (v is IEnumerable<object> items)
                        foreach (var item in items)
                            foreach (var q in item.GetType().GetProperties())
                                Assert.IsFalse(q.GetValue(item) is System.Array, $"{e.GetType().Name}.{p.Name}[].{q.Name} 是可變陣列");
                }
        }

        [Test]
        public void CardPreset_CopiesInputAndExposesReadOnlyData()
        {
            var hole = new Dictionary<int, Card[]> { { 0, Card.ParseMany("AsKs") } };
            var board = Card.ParseMany("2c3d4h");
            var preset = new CardPreset(hole, board);
            hole[0][0] = Card.Parse("2s");
            board[0] = Card.Parse("9c");
            Assert.AreEqual("As", preset.HoleCards[0][0].ToString());
            Assert.AreEqual("2c", preset.Board[0].ToString());
            Assert.IsFalse(preset.Board is System.Array);
            Assert.IsFalse(preset.HoleCards[0] is System.Array);
        }

        [Test]
        public void NewTable_RejectsTotalChipsBeyondIntRange()
        {
            Assert.Throws<System.ArgumentException>(() => HandState.NewTable(new[] { 1500000000, 1500000000 }));
        }
    }
}
