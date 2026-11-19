using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>給玩家看的牌力提示（只看玩家自己的牌，AI 拿不到）。</summary>
    public enum HandHint { Strong, Decent, Weak, Drawing }

    /// <summary>
    /// 給不會打牌的人的牌力提示（2026-10-14）：玩家「自己的兩張底牌＋公共牌」對 opponents 個隨機手牌的 Monte Carlo 勝率（Equity.Estimate，
    /// 和 AI 用的是同一套），除以公平份額 1/(opponents+1)：
    /// - ≥ StrongShare（1.3 倍）→ Strong。單挑時＝勝率 ≥ 65%，翻牌前大約是起手牌的前 12%（77+、AT+、KQs）；四人桌 AA 是 2.6 倍、KK 2.3 倍。
    /// - &lt; WeakShare（0.95 倍）→ Weak。單挑時＝勝率 &lt; 47.5%，翻牌前大約是一半的起手牌；比「平均」還差一點就叫弱，鼓勵新手棄掉爛牌。
    /// - 中間 → Decent。
    /// - 翻牌、轉牌：不是 Strong、而且有 ≥ DrawOuts（8）張牌能讓自己變成順子或同花（要用到自己的底牌；牌面自己成順不算）→ Drawing。
    ///   8 張＝兩頭順或同花聽牌，一張牌約 17–19% 中、兩張牌約 32–35%；比這少的（卡順）不提，免得新手追。
    /// 確定性：種子由牌（與人數）決定，同樣的牌永遠同樣的答案。Trials 次約 1–3 毫秒，每街算一次。
    /// </summary>
    public static class HandStrength
    {
        public const int Trials = 1200;
        public const double StrongShare = 1.3, WeakShare = 0.95;
        public const int DrawOuts = 8;

        public static string Label(HandHint h)
        {
            switch (h)
            {
                case HandHint.Strong: return "Strong hand";
                case HandHint.Decent: return "Decent hand";
                case HandHint.Weak: return "Weak hand";
                default: return "Drawing — could improve";
            }
        }

        /// <summary>對 opponents 個隨機手牌的勝率（平手均分）；種子由牌決定。</summary>
        public static double Equity(Card h1, Card h2, IReadOnlyList<Card> board, int opponents)
        {
            opponents = Math.Max(1, Math.Min(5, opponents));
            ulong mask = h1.Mask | h2.Mask;
            ulong b = 0;
            if (board != null) foreach (var c in board) b |= c.Mask;
            // 牌的遮罩當種子（底牌與公共牌分開混，順序無關）
            ulong seed = unchecked(mask * 0x9E3779B97F4A7C15UL ^ b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)opponents);
            return Poker.AI.Equity.Estimate(h1, h2, board ?? Array.Empty<Card>(), opponents, Trials, new Pcg32(seed, 99));
        }

        /// <summary>
        /// 下一張牌能讓自己變成順子以上的張數（翻牌、轉牌才有；已經是順子以上、或河牌 → 0）。
        /// 只算用到自己底牌的：那張牌加進公共牌後、公共牌自己就成順子／同花的不算。
        /// </summary>
        public static int Outs(Card h1, Card h2, IReadOnlyList<Card> board)
        {
            int n = board?.Count ?? 0;
            if (n < 3 || n > 4) return 0;
            ulong boardMask = 0;
            foreach (var c in board) boardMask |= c.Mask;
            ulong mine = h1.Mask | h2.Mask | boardMask;
            if (HandEvaluator.EvaluateMask(mine).Category >= HandCategory.Straight) return 0;
            int outs = 0;
            for (int i = 0; i < 52; i++)
            {
                var c = Card.FromIndex(i);
                if ((mine & c.Mask) != 0) continue;
                if (HandEvaluator.EvaluateMask(mine | c.Mask).Category < HandCategory.Straight) continue;
                // 公共牌五張（轉牌＋這張）自己就是順子以上：大家都有，不算
                if (n == 4 && HandEvaluator.EvaluateMask(boardMask | c.Mask).Category >= HandCategory.Straight) continue;
                outs++;
            }
            return outs;
        }

        public static HandHint Classify(Card h1, Card h2, IReadOnlyList<Card> board, int opponents)
        {
            opponents = Math.Max(1, opponents);
            double share = Equity(h1, h2, board, opponents) * (Math.Min(5, opponents) + 1);
            if (share >= StrongShare) return HandHint.Strong;
            if (Outs(h1, h2, board) >= DrawOuts) return HandHint.Drawing;
            return share >= WeakShare ? HandHint.Decent : HandHint.Weak;
        }
    }
}
