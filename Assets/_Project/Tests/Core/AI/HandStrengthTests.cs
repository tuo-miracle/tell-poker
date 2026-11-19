using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>
    /// 給不會打牌的人的牌力提示（2026-10-14）：玩家自己的底牌＋公共牌 → 對「還在局裡的人數」個隨機手牌的 Monte Carlo 勝率，
    /// 除以公平份額 1/(人數+1) → Strong／Decent／Weak；翻牌、轉牌有 8 張以上的順子／同花 outs（要用到自己的底牌）且不是 Strong → Drawing。
    /// 種子由牌決定（同樣的牌永遠同樣的答案），夠便宜可以在主執行緒每街算一次。
    /// </summary>
    public class HandStrengthTests
    {
        static HandHint Hint(string hole, string board, int opponents)
        {
            var h = Card.ParseMany(hole);
            return HandStrength.Classify(h[0], h[1], Card.ParseMany(board), opponents);
        }

        [Test]
        public void KnownHands_GetTheExpectedLabel()
        {
            // 翻牌前、四人桌（3 個對手）
            Assert.AreEqual(HandHint.Strong, Hint("AsAh", "", 3), "aces");
            Assert.AreEqual(HandHint.Strong, Hint("KdKc", "", 3), "kings");
            Assert.AreEqual(HandHint.Weak, Hint("7c2d", "", 3), "the worst hand");
            Assert.AreEqual(HandHint.Weak, Hint("9d3c", "", 3));
            Assert.AreEqual(HandHint.Decent, Hint("Ah9c", "", 3), "tutorial hand 1");
            // 翻牌後、單挑
            Assert.AreEqual(HandHint.Strong, Hint("Ah9c", "9s6d2c", 1), "top pair, ace kicker");
            Assert.AreEqual(HandHint.Strong, Hint("9c9d", "9s6h2c", 1), "a set");
            Assert.AreEqual(HandHint.Strong, Hint("AsAh", "9d7c2hKs4d", 1), "tutorial hand 2 at the river");
            Assert.AreEqual(HandHint.Weak, Hint("Kc2s", "Ah9d4s", 1), "missed everything (king high)");
        }

        [Test]
        public void Draws_AreCalledDrawing_OnlyBeforeTheRiver_AndOnlyWithYourOwnCards()
        {
            Assert.AreEqual(HandHint.Drawing, Hint("7h6h", "Kh2h9c", 1), "flush draw (9 outs)");
            Assert.AreEqual(HandHint.Drawing, Hint("8c7d", "9s6h2c", 1), "open-ended straight draw (8 outs)");
            Assert.AreEqual(9, HandStrength.Outs(Card.Parse("7h"), Card.Parse("6h"), Card.ParseMany("Kh2h9c")));
            Assert.AreEqual(8, HandStrength.Outs(Card.Parse("8c"), Card.Parse("7d"), Card.ParseMany("9s6h2c")));
            Assert.AreEqual(HandHint.Weak, Hint("8c7d", "9s6h2cKdAs", 1), "the draw missed: no more cards to come");
            Assert.AreEqual(0, HandStrength.Outs(Card.Parse("8c"), Card.Parse("7d"), Card.ParseMany("9s6h2cKdAs")), "no outs at the river");
            // 牌面自己就差一張成順（4 或 9 讓公共牌五張就是順子）：不是玩家的 outs
            Assert.AreEqual(0, HandStrength.Outs(Card.Parse("Kc"), Card.Parse("2s"), Card.ParseMany("5h6h7c8d")));
            Assert.AreNotEqual(HandHint.Drawing, Hint("Kc2s", "5h6h7c8d", 1));
            // 已經是順子以上：不算聽牌
            Assert.AreEqual(0, HandStrength.Outs(Card.Parse("Ts"), Card.Parse("9s"), Card.ParseMany("8h7c6d")));
            Assert.AreEqual(HandHint.Strong, Hint("Ts9s", "8h7c6d", 1), "made straight");
            // 有對子又聽同花，夠強就說 Strong（Strong 優先於 Drawing）
            Assert.AreEqual(HandHint.Strong, Hint("AhKh", "Ad7h2h", 1));
        }

        [Test]
        public void Deterministic_AndCheap()
        {
            var h = Card.ParseMany("QsJs");
            var board = Card.ParseMany("Ts4d2h");
            double a = HandStrength.Equity(h[0], h[1], board, 2);
            double b = HandStrength.Equity(h[0], h[1], board, 2);
            Assert.AreEqual(a, b, "same cards → same seed → same answer");
            Assert.AreEqual(a, HandStrength.Equity(h[1], h[0], board, 2), "card order does not matter");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) HandStrength.Classify(h[0], h[1], board, 3);
            sw.Stop();
            Assert.Less(sw.Elapsed.TotalMilliseconds / 20, 15.0, "one street's hint costs a few milliseconds at most");
        }

        [Test]
        public void Equity_AgreesWithThePreflopTable()
        {
            // PreflopTable：每類 200,000 次、對 1 個隨機對手；這裡 HandStrength.Trials 次，誤差 < 3.5%
            foreach (var hole in new[] { "AsAh", "AhKs", "7c2d", "Td9d", "5c5d" })
            {
                var h = Card.ParseMany(hole);
                Assert.AreEqual(PreflopTable.Equity(h[0], h[1]), HandStrength.Equity(h[0], h[1], Array.Empty<Card>(), 1), 0.035, hole);
            }
        }

        /// <summary>
        /// 分布合理：隨機發 4,000 手（種子固定），翻牌前四人桌 Strong 約 1/8、Weak 約一半；翻牌單挑時 Drawing 不常見但會出現。
        /// 門檻的理由見 HandStrength 的註解（公平份額的 1.3 倍＝單挑約 65% 勝率＝起手牌前 ~12%）。
        /// </summary>
        [Test]
        public void Distribution_IsSane()
        {
            var pre = Distribution(4000, 0, 3, 11);
            Assert.That(pre[HandHint.Strong], Is.InRange(0.07, 0.20), Fmt(pre));
            Assert.That(pre[HandHint.Weak], Is.InRange(0.40, 0.65), Fmt(pre));
            Assert.That(pre[HandHint.Decent], Is.InRange(0.20, 0.45), Fmt(pre));
            Assert.AreEqual(0.0, pre[HandHint.Drawing], "no draws before the flop");

            var flop = Distribution(4000, 3, 1, 12);
            Assert.That(flop[HandHint.Drawing], Is.InRange(0.03, 0.20), Fmt(flop));
            Assert.That(flop[HandHint.Strong], Is.InRange(0.15, 0.45), Fmt(flop));
            Assert.That(flop[HandHint.Weak], Is.InRange(0.15, 0.55), Fmt(flop));

            var river = Distribution(2000, 5, 1, 13);
            Assert.AreEqual(0.0, river[HandHint.Drawing], "nothing to draw at the river");
            TestContext.WriteLine("preflop 4-handed " + Fmt(pre) + " | flop heads-up " + Fmt(flop) + " | river heads-up " + Fmt(river));
        }

        static Dictionary<HandHint, double> Distribution(int n, int boardCards, int opponents, ulong seed)
        {
            var count = Enum.GetValues(typeof(HandHint)).Cast<HandHint>().ToDictionary(h => h, h => 0);
            var rng = new Pcg32(seed, 7);
            for (int i = 0; i < n; i++)
            {
                var deck = Enumerable.Range(0, 52).Select(Card.FromIndex).ToList();
                var pick = new List<Card>();
                for (int k = 0; k < 2 + boardCards; k++)
                {
                    int j = (int)rng.NextBounded((uint)deck.Count);
                    pick.Add(deck[j]);
                    deck.RemoveAt(j);
                }
                count[HandStrength.Classify(pick[0], pick[1], pick.Skip(2).ToList(), opponents)]++;
            }
            return count.ToDictionary(kv => kv.Key, kv => kv.Value / (double)n);
        }

        static string Fmt(Dictionary<HandHint, double> d) => string.Join(", ", d.Select(kv => kv.Key + " " + kv.Value.ToString("P1")));

        [Test]
        public void Labels_AreShortEnglish()
        {
            Assert.AreEqual("Strong hand", HandStrength.Label(HandHint.Strong));
            Assert.AreEqual("Decent hand", HandStrength.Label(HandHint.Decent));
            Assert.AreEqual("Weak hand", HandStrength.Label(HandHint.Weak));
            Assert.AreEqual("Drawing — could improve", HandStrength.Label(HandHint.Drawing));
        }
    }
}
