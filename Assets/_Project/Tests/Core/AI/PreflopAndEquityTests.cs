using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    public class PreflopTableTests
    {
        static IEnumerable<(Card, Card)> AllCombos()
        {
            for (int i = 0; i < 52; i++)
                for (int j = i + 1; j < 52; j++)
                    yield return (Card.FromIndex(i), Card.FromIndex(j));
        }

        [Test]
        public void ClassIndex_Covers169Classes_WithStandardComboCounts()
        {
            var counts = new int[PreflopTable.ClassCount];
            foreach (var (a, b) in AllCombos()) counts[PreflopTable.ClassIndex(a, b)]++;
            Assert.AreEqual(169, counts.Count(c => c > 0));
            for (int k = 0; k < 169; k++)
            {
                string n = PreflopTable.ClassName(k);
                int expect = n.Length == 2 ? 6 : n.EndsWith("s") ? 4 : 12;
                Assert.AreEqual(expect, counts[k], n);
            }
        }

        [Test]
        public void ClassIndex_IgnoresCardOrder()
        {
            var a = Card.Parse("As"); var k = Card.Parse("Kd");
            Assert.AreEqual(PreflopTable.ClassIndex(a, k), PreflopTable.ClassIndex(k, a));
            Assert.AreEqual("AKo", PreflopTable.ClassName(PreflopTable.ClassIndex(a, k)));
            Assert.AreEqual("AKs", PreflopTable.ClassName(PreflopTable.ClassIndex(Card.Parse("Ah"), Card.Parse("Kh"))));
            Assert.AreEqual("77", PreflopTable.ClassName(PreflopTable.ClassIndex(Card.Parse("7h"), Card.Parse("7c"))));
        }

        [Test]
        public void Equity_OrdersKnownHands()
        {
            float E(string s) { var c = Card.ParseMany(s); return PreflopTable.Equity(c[0], c[1]); }
            Assert.AreEqual(0.85, E("AsAd"), 0.01);
            Assert.Greater(E("AsKs"), E("AsKd"));
            Assert.Greater(E("2c2d"), E("7s2d"));
            float min = Enumerable.Range(0, 169).Min(PreflopTable.EquityOfClass);
            Assert.AreEqual(min, E("3c2d"), 1e-6, "32o 對隨機手牌最差");
        }

        [Test]
        public void Percentile_IsMonotoneInEquity_AndSpansAllCombos()
        {
            var aa = Card.ParseMany("AsAd");
            Assert.AreEqual(6.0 / 1326, PreflopTable.Percentile(aa[0], aa[1]), 1e-6);
            for (int i = 0; i < 169; i++)
                for (int j = 0; j < 169; j++)
                    if (PreflopTable.EquityOfClass(i) > PreflopTable.EquityOfClass(j))
                        Assert.Less(PreflopTable.PercentileOfClass(i), PreflopTable.PercentileOfClass(j));
            Assert.AreEqual(1.0, Enumerable.Range(0, 169).Max(PreflopTable.PercentileOfClass), 1e-6);
        }

        [Test]
        public void ConstantTable_MatchesFreshMonteCarlo()
        {
            // 常數表是用 PreflopTableGenerator 每類 200,000 次產生；這裡用 4,000 次重算，誤差 3σ ≈ 0.024
            var fresh = PreflopTableGenerator.Generate(4000, 12345);
            for (int k = 0; k < 169; k++)
                Assert.AreEqual(PreflopTable.EquityOfClass(k), fresh[k], 0.03, PreflopTable.ClassName(k));
        }

        [Test, Explicit("工具：重新產生 PreflopTable 常數表，輸出 C# 陣列到主控台")]
        public void Tool_PrintTable()
        {
            var t = PreflopTableGenerator.Generate(200000, 20261012);
            Console.WriteLine(PreflopTableGenerator.ToCSharp(t));
        }
    }

    public class EquityTests
    {
        static Card[] C(string s) => Card.ParseMany(s);

        [Test]
        public void AcesVsOneRandom_Preflop_About85Percent()
        {
            var h = C("AsAd");
            var r = Equity.Estimate(h[0], h[1], Array.Empty<Card>(), 1, 20000, new Pcg32(1));
            Assert.AreEqual(0.85, r, 0.015);
        }

        [Test]
        public void RoyalFlushHolder_HasEquityOne()
        {
            var h = C("AsKs");
            Assert.AreEqual(1.0, Equity.Estimate(h[0], h[1], C("QsJsTs2d3c"), 3, 500, new Pcg32(2)), 1e-9);
        }

        [Test]
        public void MoreOpponents_LowerEquity()
        {
            var h = C("QhJh");
            double one = Equity.Estimate(h[0], h[1], C("9h4c2d"), 1, 5000, new Pcg32(3));
            double three = Equity.Estimate(h[0], h[1], C("9h4c2d"), 3, 5000, new Pcg32(3));
            Assert.Less(three, one - 0.1);
        }

        [Test]
        public void SameSeed_SameResult()
        {
            var h = C("8c8d");
            double a = Equity.Estimate(h[0], h[1], C("Kd7s2h"), 2, 1000, new Pcg32(9));
            double b = Equity.Estimate(h[0], h[1], C("Kd7s2h"), 2, 1000, new Pcg32(9));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void TightOpponentRange_LowersEquityOfMediumHand()
        {
            var h = C("KdQc");
            double vsAny = Equity.Estimate(h[0], h[1], Array.Empty<Card>(), 1, 20000, new Pcg32(4));
            double vsTight = Equity.Estimate(h[0], h[1], Array.Empty<Card>(), 1, 20000, new Pcg32(4), new[] { 0.08f });
            Assert.Less(vsTight, vsAny - 0.08);
        }
    }
}
