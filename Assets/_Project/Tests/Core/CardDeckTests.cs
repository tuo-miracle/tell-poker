using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    public class CardTests
    {
        [Test]
        public void ToString_UsesRankAndSuitLetters()
        {
            Assert.AreEqual("As", new Card(14, Suit.Spades).ToString());
            Assert.AreEqual("Td", new Card(10, Suit.Diamonds).ToString());
            Assert.AreEqual("2c", new Card(2, Suit.Clubs).ToString());
            Assert.AreEqual("Kh", new Card(13, Suit.Hearts).ToString());
        }

        [Test]
        public void Parse_RoundTripsAll52Cards()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 52; i++)
            {
                var c = Card.FromIndex(i);
                Assert.AreEqual(i, c.Index);
                Assert.AreEqual(c, Card.Parse(c.ToString()));
                seen.Add(c.ToString());
            }
            Assert.AreEqual(52, seen.Count);
        }

        [Test]
        public void ParseMany_ReadsConcatenatedCards()
        {
            var cards = Card.ParseMany("AsKd 7c");
            CollectionAssert.AreEqual(new[] { Card.Parse("As"), Card.Parse("Kd"), Card.Parse("7c") }, cards);
        }

        [Test]
        public void Constructor_RejectsInvalidRank()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Card(1, Suit.Clubs));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Card(15, Suit.Clubs));
        }

        [Test]
        public void Default_IsNotValid()
        {
            Assert.IsFalse(default(Card).IsValid);
            Assert.IsTrue(Card.Parse("2c").IsValid);
        }

        [Test]
        public void Mask_IsUniquePerCard()
        {
            ulong all = 0;
            for (int i = 0; i < 52; i++)
            {
                ulong m = Card.FromIndex(i).Mask;
                Assert.AreEqual(0UL, all & m);
                all |= m;
            }
        }
    }

    public class Pcg32Tests
    {
        [Test]
        public void MatchesReferenceImplementationOutput()
        {
            // pcg32-demo（O'Neill 官方 C 實作）以 initstate=42, initseq=54 的前 6 個輸出。
            var rng = new Pcg32(42, 54);
            uint[] expected = { 0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu };
            foreach (var e in expected) Assert.AreEqual(e, rng.NextUInt());
        }

        [Test]
        public void NextBounded_StaysInRange()
        {
            var rng = new Pcg32(7);
            for (int i = 0; i < 10000; i++)
            {
                uint v = rng.NextBounded(52);
                Assert.Less(v, 52u);
            }
        }
    }

    public class DeckTests
    {
        [Test]
        public void Shuffle_IsAPermutationOf52Cards()
        {
            var deck = Deck.Shuffled(123);
            Assert.AreEqual(52, deck.Count);
            Assert.AreEqual(52, deck.Select(c => c.Index).Distinct().Count());
        }

        [Test]
        public void Shuffle_SameSeedSameOrder_DifferentSeedDifferentOrder()
        {
            CollectionAssert.AreEqual(Deck.Shuffled(99), Deck.Shuffled(99));
            CollectionAssert.AreNotEqual(Deck.Shuffled(99), Deck.Shuffled(100));
        }

        [Test]
        public void Shuffle_ResultIsLockedForSeed()
        {
            // 鎖定：任何 PRNG／洗牌演算法改動都會讓這個測試失敗（牌局重播會壞）。
            string first13 = string.Concat(Deck.Shuffled(20261012).Take(13).Select(c => c.ToString()));
            Assert.AreEqual(LockedSeed20261012, first13);
        }

        internal const string LockedSeed20261012 = "Jd8c2c4s4h8d7d7s9cQsAd7hJc";
    }

    public class SeedSourceTests
    {
        [Test]
        public void ForSession_NonZeroSeed_IsKeptForReplay()
        {
            Assert.AreEqual(20261015UL, SeedSource.ForSession(20261015));
        }

        [Test]
        public void ForSession_Zero_DrawsAFreshNonZeroSeedEachTime()
        {
            var seeds = Enumerable.Range(0, 200).Select(_ => SeedSource.ForSession(0)).ToList();
            Assert.IsFalse(seeds.Contains(0UL));
            Assert.AreEqual(200, seeds.Distinct().Count());
        }
    }
}
