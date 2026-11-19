using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using UnityEngine;

namespace Poker.Tests.Game
{
    public class CardFaceTests
    {
        [Test]
        public void SuitSymbols_AreTheStandardGlyphs()
        {
            Assert.AreEqual("♠", CardFace.SuitSymbol(Suit.Spades));   // ♠
            Assert.AreEqual("♥", CardFace.SuitSymbol(Suit.Hearts));   // ♥
            Assert.AreEqual("♦", CardFace.SuitSymbol(Suit.Diamonds)); // ♦
            Assert.AreEqual("♣", CardFace.SuitSymbol(Suit.Clubs));    // ♣
        }

        [Test]
        public void RankLabels()
        {
            Assert.AreEqual("A", CardFace.RankLabel(14));
            Assert.AreEqual("K", CardFace.RankLabel(13));
            Assert.AreEqual("10", CardFace.RankLabel(10));
            Assert.AreEqual("2", CardFace.RankLabel(2));
        }

        [Test]
        public void RedSuits_AreHeartsAndDiamonds()
        {
            Assert.IsTrue(CardFace.IsRed(Suit.Hearts));
            Assert.IsTrue(CardFace.IsRed(Suit.Diamonds));
            Assert.IsFalse(CardFace.IsRed(Suit.Spades));
            Assert.IsFalse(CardFace.IsRed(Suit.Clubs));
        }

        [Test]
        public void SuitShapes_AreDistinct_AndFillTheirCenter()
        {
            var suits = new[] { Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs };
            var masks = new bool[4][];
            for (int s = 0; s < 4; s++)
            {
                masks[s] = new bool[32 * 32];
                for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    masks[s][y * 32 + x] = CardFace.SuitCoverage(suits[s], (x + 0.5f) / 32f, (y + 0.5f) / 32f) > 0.5f;
                Assert.IsTrue(masks[s][16 * 32 + 16], suits[s] + " center filled");
            }
            for (int a = 0; a < 4; a++)
            for (int b = a + 1; b < 4; b++)
            {
                int diff = 0;
                for (int i = 0; i < masks[a].Length; i++) if (masks[a][i] != masks[b][i]) diff++;
                Assert.Greater(diff, 40, $"{suits[a]} vs {suits[b]}");
            }
            // 紅心上方兩個圓峰之間是凹口；黑桃頂端是尖的（頂端中央有填色）
            Assert.IsFalse(CardFace.SuitCoverage(Suit.Hearts, 0.5f, 0.86f) > 0.5f, "heart notch");
            Assert.IsTrue(CardFace.SuitCoverage(Suit.Spades, 0.5f, 0.86f) > 0.5f, "spade tip");
        }

        [Test]
        public void FaceTexture_HasRedPipForHearts_AndDarkPipForSpades()
        {
            var h = CardFace.Get(new Card(14, Suit.Hearts));
            var s = CardFace.Get(new Card(14, Suit.Spades));
            Assert.AreEqual(CardFace.TextureWidth, h.width);
            var hc = h.GetPixelBilinear(0.5f, 0.5f);
            var sc = s.GetPixelBilinear(0.5f, 0.5f);
            Assert.Greater(hc.r, 0.6f); Assert.Less(hc.g, 0.35f);
            Assert.Less(sc.r + sc.g + sc.b, 0.6f);
            // 角落的點數區不是全白
            Assert.Less(CardFace.InkFraction(h, new Rect(0.02f, 0.75f, 0.2f, 0.23f)), 1f);
            Assert.Greater(CardFace.InkFraction(h, new Rect(0.02f, 0.75f, 0.2f, 0.23f)), 0.05f);
            Assert.AreSame(h, CardFace.Get(new Card(14, Suit.Hearts)), "cached");
        }
    }
}
