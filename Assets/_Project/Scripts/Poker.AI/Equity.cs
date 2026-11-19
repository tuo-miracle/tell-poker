using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>
    /// Monte Carlo 勝率估計。只用「自己的底牌＋公共牌」排除已知牌，對手手牌一律從剩餘牌隨機抽
    /// （絕不讀真實牌堆或別人的底牌）。可依對手動作給「範圍」做軟加權（Loki 式再加權）。
    /// </summary>
    public static class Equity
    {
        /// <summary>範圍外手牌的最低權重（不完全排除，避免 AI 被詐唬時算出 0 勝率）。</summary>
        public const float RangeFloor = 0.05f;
        /// <summary>範圍邊界往外線性遞減的寬度（以百分位計）。</summary>
        public const float RangeFade = 0.15f;

        /// <summary>
        /// 估計勝率（平手按人數均分）。
        /// </summary>
        /// <param name="opponentTopFractions">每位對手的範圍：只玩前 X% 起手牌（1 = 任何牌）。null 表示全部隨機。長度不足的視為 1。</param>
        public static double Estimate(Card h1, Card h2, IReadOnlyList<Card> board, int opponents, int trials, Pcg32 rng,
            IReadOnlyList<float> opponentTopFractions = null)
        {
            if (opponents < 1) return 1.0;
            if (trials < 1) throw new ArgumentOutOfRangeException(nameof(trials));
            int nb = board?.Count ?? 0;
            if (nb > 5) throw new ArgumentException("公共牌最多 5 張", nameof(board));
            ulong hole = h1.Mask | h2.Mask;
            ulong boardMask = 0;
            for (int i = 0; i < nb; i++) boardMask |= board[i].Mask;
            ulong known = hole | boardMask;

            var deck = new Card[52];
            int n = 0;
            for (int i = 0; i < 52; i++)
            {
                var c = Card.FromIndex(i);
                if ((known & c.Mask) == 0) deck[n++] = c;
            }
            int need = 5 - nb;
            if (need + 2 * opponents > n) throw new ArgumentException("對手太多，牌不夠發");

            var oppMask = new ulong[opponents];
            double total = 0;
            for (int t = 0; t < trials; t++)
            {
                int pos = 0;
                ulong b = boardMask;
                for (int k = 0; k < need; k++) b |= Draw(deck, n, ref pos, rng).Mask;
                for (int o = 0; o < opponents; o++)
                {
                    float top = opponentTopFractions != null && o < opponentTopFractions.Count ? opponentTopFractions[o] : 1f;
                    // 拒絕取樣：最多試 16 次，之後接受（權重下限保證不會卡住）
                    for (int tries = 0; ; tries++)
                    {
                        var c1 = Draw(deck, n, ref pos, rng);
                        var c2 = Draw(deck, n, ref pos, rng);
                        if (top >= 1f || tries >= 15 || Next01(rng) < RangeWeight(top, PreflopTable.Percentile(c1, c2)))
                        {
                            oppMask[o] = c1.Mask | c2.Mask;
                            break;
                        }
                        pos -= 2; // 放回（兩張牌仍在陣列中未抽區）
                    }
                }
                int mine = HandEvaluator.EvaluateMask(b | hole).Value;
                int ties = 0;
                bool lost = false;
                for (int o = 0; o < opponents; o++)
                {
                    int v = HandEvaluator.EvaluateMask(b | oppMask[o]).Value;
                    if (v > mine) { lost = true; break; }
                    if (v == mine) ties++;
                }
                if (!lost) total += 1.0 / (ties + 1);
            }
            return total / trials;
        }

        /// <summary>範圍權重：前 <paramref name="top"/> 內為 1，之後在 <see cref="RangeFade"/> 內線性降到 <see cref="RangeFloor"/>。</summary>
        public static float RangeWeight(float top, float percentile)
        {
            if (top >= 1f || percentile <= top) return 1f;
            float x = (percentile - top) / RangeFade;
            return x >= 1f ? RangeFloor : 1f - x * (1f - RangeFloor);
        }

        static Card Draw(Card[] deck, int n, ref int pos, Pcg32 rng)
        {
            int j = pos + (int)rng.NextBounded((uint)(n - pos));
            var c = deck[j]; deck[j] = deck[pos]; deck[pos] = c;
            pos++;
            return c;
        }

        internal static double Next01(Pcg32 rng) => rng.NextUInt() / 4294967296.0;
    }
}
