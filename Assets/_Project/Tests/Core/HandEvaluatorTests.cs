using System;
using System.Diagnostics;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    public class HandEvaluatorTests
    {
        private static HandValue Eval(string cards) => HandEvaluator.Evaluate(Card.ParseMany(cards));

        [TestCase("AsKsQsJsTs9h2d", HandCategory.StraightFlush)]
        [TestCase("5d4d3d2dAd KcKh", HandCategory.StraightFlush)]
        [TestCase("9c9d9h9s2c3d4h", HandCategory.FourOfAKind)]
        [TestCase("9c9d9h2s2c3d4h", HandCategory.FullHouse)]
        [TestCase("9c9d9h2s2c2d4h", HandCategory.FullHouse)]
        [TestCase("Ac9c7c5c3c Kd Kh", HandCategory.Flush)]
        [TestCase("Ac2d3h4s5c Kd Kh", HandCategory.Straight)]
        [TestCase("Tc Jd Qh Ks Ac 2d 3h", HandCategory.Straight)]
        [TestCase("7c7d7h Ks 2c 4d 9h", HandCategory.ThreeOfAKind)]
        [TestCase("7c7d Kh Ks 2c 4d 9h", HandCategory.TwoPair)]
        [TestCase("7c7d Kh Qs 2c 4d 9h", HandCategory.Pair)]
        [TestCase("7c8d Kh Qs 2c 4d 9h", HandCategory.HighCard)]
        [TestCase("Qc Kd Ah 2s 3c 8d 9h", HandCategory.HighCard)] // 不可繞圈 Q-K-A-2-3
        [TestCase("AsKsQsJsTs", HandCategory.StraightFlush)]
        [TestCase("AsKsQsJs9s", HandCategory.Flush)]
        [TestCase("AsKsQsJs9s8d", HandCategory.Flush)]
        [TestCase("2c2d2h3c3d3h", HandCategory.FullHouse)]
        public void Category(string cards, HandCategory expected)
        {
            Assert.AreEqual(expected, Eval(cards).Category);
        }

        // 左邊 > 右邊
        [TestCase("6c2d3h4s5c Kd Kh", "Ac2d3h4s5c Kd Kh", TestName = "6-high straight beats wheel")]
        [TestCase("Ac2d3h4s5c 9d 9h", "AdAh 9c 9s 5h 2c 3c", TestName = "wheel beats two pair")]
        [TestCase("AsKsQsJsTs", "5d4d3d2dAd", TestName = "royal beats steel wheel")]
        [TestCase("6d5d4d3d2d", "5d4d3d2dAd", TestName = "6-high straight flush beats steel wheel")]
        [TestCase("5d4d3d2dAd", "AcAdAhAs Kc", TestName = "steel wheel beats quad aces")]
        [TestCase("AcAdAh2s2c", "KcKdKhAsAh", TestName = "aces full beats kings full of aces")]
        [TestCase("KcKdKh3s3c", "KcKdKh2s2c", TestName = "full house compares pair after trips")]
        [TestCase("2h2d2s Ac Ad Ah 3c", "3c3d3h2s2c 2d", TestName = "two trips: aces full beats threes full")]
        [TestCase("AcAdAh 2s 2c Kd Kh", "AcAdAh 2s 2c Qd Qh", TestName = "full house uses best pair from three")]
        [TestCase("9c9d9h9s Ac", "9c9d9h9s Kc", TestName = "quads kicker")]
        [TestCase("Ac9c7c5c4c", "Ac9c7c5c3c", TestName = "flush fifth card")]
        [TestCase("Ac Kc 9c 7c 5c 4c 3c", "Ac Qc Jc 9c 7c 5c 4c", TestName = "flush uses top five of seven suited")]
        [TestCase("Kc9c7c5c3c", "QcJc9c7c5c", TestName = "flush top card")]
        [TestCase("AcAd Kc Qd Jh 3c 2d", "AcAd Kc Qd Th 3c 2d", TestName = "pair third kicker")]
        [TestCase("AcAd Kc Qd 9h 3c 2d", "AcAd Kc Jd Th 3c 2d", TestName = "pair second kicker")]
        [TestCase("AcAd 3c 4d 2h 6c 9d", "KcKd Ac Qd Jh 9c 8d", TestName = "higher pair beats better kickers")]
        [TestCase("AcAd 3c 3d Kh 6c 9d", "AcAd 3c 3d Qh Jc Td", TestName = "two pair kicker")]
        [TestCase("AcAd 3c 3d 7h 6c 9d", "KcKd Qc Qd Jh 5c 2d", TestName = "two pair top pair decides")]
        [TestCase("AcAd 4c 4d 7h 6c 9d", "AcAd 3c 3d Kh Qc Jd", TestName = "two pair second pair decides")]
        [TestCase("AcAd KcKd 2h2c 3d", "AcAd KcKd 2h2c", TestName = "seventh card kicker beats six-card hand")]
        [TestCase("7c7d7h As Kd 2c 3d", "7c7d7h Kc Qd 2c 3d", TestName = "trips kicker")]
        [TestCase("8c8d8h 2s 3c 5d 6h", "7c7d7h Ac Kd Qh Js", TestName = "higher trips")]
        [TestCase("Ac Kd Qh Js 9c 3d 2h", "Ac Kd Qh Js 8c 7d 6h", TestName = "high card fifth kicker")]
        [TestCase("2c3d4h5s6c 8d Th", "Ac Kd Qh Js 9c 3d 2h", TestName = "straight beats high card")]
        [TestCase("9c9d 9h 2s 2c 3d 4h", "Ac Kc 9c 7c 5c 2h 3d", TestName = "full house beats flush")]
        [TestCase("Ac Kc 9c 7c 5c 2h 3d", "Ac Kd Qh Js Tc 2h 3d", TestName = "flush beats straight")]
        public void Comparison(string better, string worse)
        {
            Assert.Greater(Eval(better).Value, Eval(worse).Value);
        }

        // 平手（含 play the board、kicker 不計第六張）
        [TestCase("Ac Kd Qh Js 9c 3d 2h", "Ac Kd Qh Js 9c 4d 2h", TestName = "sixth card does not play")]
        [TestCase("2c 3d Th Jh Qh Kh Ah", "4c 5d Th Jh Qh Kh Ah", TestName = "board royal plays for both")]
        [TestCase("2c 3d 9h 9s 9c 9d Ah", "Kc 5d 9h 9s 9c 9d Ah", TestName = "board quads with ace kicker")]
        [TestCase("AcAd Kc Kd Qh 3c 2d", "AhAs Kh Ks Qd 5c 4d", TestName = "same two pair same kicker")]
        [TestCase("Ac2d3h4s5c", "Ad2c3s4h5d", TestName = "two wheels tie")]
        [TestCase("AcAd KcKd QhQd 2c", "AhAs KhKs QcQs 3c", TestName = "three pairs: Q kicker for both")]
        public void Tie(string a, string b)
        {
            Assert.AreEqual(Eval(a).Value, Eval(b).Value);
        }

        [Test]
        public void FiveSixSevenCardsAgreeOnBestFive()
        {
            var five = Eval("AcAdAhKsKc");
            Assert.AreEqual(five.Value, Eval("AcAdAhKsKc 2d").Value);
            Assert.AreEqual(five.Value, Eval("AcAdAhKsKc 2d 3h").Value);
        }

        [Test]
        public void RejectsWrongCountOrDuplicates()
        {
            Assert.Throws<ArgumentException>(() => HandEvaluator.Evaluate(Card.ParseMany("AcKc2d3h")));
            Assert.Throws<ArgumentException>(() => HandEvaluator.Evaluate(Card.ParseMany("AcKc2d3h4h5h6h7h")));
            Assert.Throws<ArgumentException>(() => HandEvaluator.Evaluate(Card.ParseMany("AcAc2d3h4h")));
        }

        [Test]
        public void MaskApiMatchesCardApi()
        {
            var cards = Card.ParseMany("7c7d Kh Ks 2c 4d 9h");
            ulong m = 0;
            foreach (var c in cards) m |= c.Mask;
            Assert.AreEqual(HandEvaluator.Evaluate(cards).Value, HandEvaluator.EvaluateMask(m).Value);
            Assert.AreEqual(HandEvaluator.Evaluate((System.Collections.Generic.IReadOnlyList<Card>)cards).Value, HandEvaluator.EvaluateMask(m).Value);
            Assert.AreEqual(HandEvaluator.Evaluate(cards[0], cards[1], cards[2], cards[3], cards[4], cards[5], cards[6]).Value, HandEvaluator.EvaluateMask(m).Value);
            Assert.Throws<ArgumentException>(() => HandEvaluator.Evaluate(cards[0], cards[0], cards[2], cards[3], cards[4], cards[5], cards[6]));
            Assert.Throws<ArgumentException>(() => HandEvaluator.Evaluate(cards[0], default(Card), cards[2], cards[3], cards[4], cards[5], cards[6]));
        }

        /// <summary>全窮舉 C(52,7) = 133,784,560 手，比對各牌型的已知精確數量。</summary>
        [Test, Category("Slow")]
        public void Exhaustive_AllSevenCardHands_CategoryCounts()
        {
            var masks = new ulong[52];
            for (int i = 0; i < 52; i++) masks[i] = Card.FromIndex(i).Mask;
            var counts = new long[9];
            var sw = Stopwatch.StartNew();
            for (int a = 0; a < 46; a++)
            for (int b = a + 1; b < 47; b++)
            {
                ulong mb = masks[a] | masks[b];
                for (int c = b + 1; c < 48; c++)
                {
                    ulong mc = mb | masks[c];
                    for (int d = c + 1; d < 49; d++)
                    {
                        ulong md = mc | masks[d];
                        for (int e = d + 1; e < 50; e++)
                        {
                            ulong me = md | masks[e];
                            for (int f = e + 1; f < 51; f++)
                            {
                                ulong mf = me | masks[f];
                                for (int g = f + 1; g < 52; g++)
                                    counts[HandEvaluator.EvaluateMask(mf | masks[g]).Value >> HandValue.CategoryShift]++;
                            }
                        }
                    }
                }
            }
            sw.Stop();
            long total = 0;
            for (int i = 8; i >= 0; i--)
            {
                TestContext.Progress.WriteLine($"  {(HandCategory)i,-14} {counts[i],12:N0}");
                total += counts[i];
            }
            TestContext.Progress.WriteLine($"  total          {total,12:N0}   elapsed {sw.Elapsed.TotalSeconds:F2}s  ({total / sw.Elapsed.TotalSeconds / 1e6:F1} M hands/s)");
            Assert.AreEqual(133784560L, total);
            Assert.AreEqual(41584L, counts[(int)HandCategory.StraightFlush]);
            Assert.AreEqual(224848L, counts[(int)HandCategory.FourOfAKind]);
            Assert.AreEqual(3473184L, counts[(int)HandCategory.FullHouse]);
            Assert.AreEqual(4047644L, counts[(int)HandCategory.Flush]);
            Assert.AreEqual(6180020L, counts[(int)HandCategory.Straight]);
            Assert.AreEqual(6461620L, counts[(int)HandCategory.ThreeOfAKind]);
            Assert.AreEqual(31433400L, counts[(int)HandCategory.TwoPair]);
            Assert.AreEqual(58627800L, counts[(int)HandCategory.Pair]);
            Assert.AreEqual(23294460L, counts[(int)HandCategory.HighCard]);
        }

        /// <summary>速度基準：隨機（預先產生）的 7 張牌遮罩，量測每秒評估次數。</summary>
        [Test, Category("Slow")]
        public void Benchmark_RandomSevenCardHands()
        {
            const int N = 1 << 20;
            var hands = new ulong[N];
            var rng = new Pcg32(1);
            for (int i = 0; i < N; i++)
            {
                ulong m = 0; int n = 0;
                while (n < 7) { ulong bit = Card.FromIndex((int)rng.NextBounded(52)).Mask; if ((m & bit) == 0) { m |= bit; n++; } }
                hands[i] = m;
            }
            long sink = 0;
            for (int i = 0; i < N; i++) sink += HandEvaluator.EvaluateMask(hands[i]).Value; // 暖機
            var sw = Stopwatch.StartNew();
            const int Rounds = 20;
            for (int r = 0; r < Rounds; r++)
                for (int i = 0; i < N; i++) sink += HandEvaluator.EvaluateMask(hands[i]).Value;
            sw.Stop();
            double perSec = (double)N * Rounds / sw.Elapsed.TotalSeconds;
            TestContext.Progress.WriteLine($"  EvaluateMask random 7-card: {perSec / 1e6:F1} M evals/s ({sw.Elapsed.TotalMilliseconds * 1e6 / ((double)N * Rounds):F1} ns/eval), sink={sink}");

            var cardHands = new Card[N / 16][];
            for (int i = 0; i < cardHands.Length; i++)
            {
                cardHands[i] = new Card[7]; int n = 0; ulong m = hands[i];
                for (int k = 0; k < 52 && n < 7; k++) if ((m & Card.FromIndex(k).Mask) != 0) cardHands[i][n++] = Card.FromIndex(k);
            }
            sw.Restart();
            for (int r = 0; r < Rounds; r++)
                for (int i = 0; i < cardHands.Length; i++) sink += HandEvaluator.Evaluate(cardHands[i]).Value;
            sw.Stop();
            TestContext.Progress.WriteLine($"  Evaluate(Card[7]) (with validation): {(double)cardHands.Length * Rounds / sw.Elapsed.TotalSeconds / 1e6:F1} M evals/s");
            sw.Restart();
            for (int r = 0; r < Rounds; r++)
                for (int i = 0; i < cardHands.Length; i++) { var h = cardHands[i]; sink += HandEvaluator.Evaluate(h[0], h[1], h[2], h[3], h[4], h[5], h[6]).Value; }
            sw.Stop();
            TestContext.Progress.WriteLine($"  Evaluate(c1..c7) (with validation): {(double)cardHands.Length * Rounds / sw.Elapsed.TotalSeconds / 1e6:F1} M evals/s");
        }
    }
}
