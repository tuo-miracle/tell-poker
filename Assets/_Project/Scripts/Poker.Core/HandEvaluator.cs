using System;
using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>牌型類別，數值越大越強。皇家同花順屬於 <see cref="StraightFlush"/>。</summary>
    public enum HandCategory
    {
        /// <summary>高牌</summary>
        HighCard,
        /// <summary>一對</summary>
        Pair,
        /// <summary>兩對</summary>
        TwoPair,
        /// <summary>三條</summary>
        ThreeOfAKind,
        /// <summary>順子</summary>
        Straight,
        /// <summary>同花</summary>
        Flush,
        /// <summary>葫蘆</summary>
        FullHouse,
        /// <summary>四條</summary>
        FourOfAKind,
        /// <summary>同花順（含皇家同花順）</summary>
        StraightFlush,
    }

    /// <summary>
    /// 手牌強度。<see cref="Value"/> 可直接比大小（越大越好，相等即平手）。
    /// 編碼：類別 &lt;&lt; 26 | 主要點數遮罩 &lt;&lt; 13 | 踢腳遮罩（13 位元點數遮罩，A 在最高位）。
    /// </summary>
    public readonly struct HandValue : IComparable<HandValue>, IEquatable<HandValue>
    {
        /// <summary>類別在 <see cref="Value"/> 中的位移量。</summary>
        public const int CategoryShift = 26;

        /// <summary>以原始整數建立（通常由 <see cref="HandEvaluator"/> 產生）。</summary>
        public HandValue(int value) { Value = value; }

        /// <summary>可直接比較的整數值，越大越好。</summary>
        public int Value { get; }

        /// <summary>牌型類別。</summary>
        public HandCategory Category => (HandCategory)(Value >> CategoryShift);

        /// <summary>比較強弱：正數表示本手較強。</summary>
        public int CompareTo(HandValue other) => Value.CompareTo(other.Value);
        /// <summary>是否同樣強（平手）。</summary>
        public bool Equals(HandValue other) => Value == other.Value;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is HandValue h && h.Value == Value;
        /// <inheritdoc/>
        public override int GetHashCode() => Value;
        /// <summary>左邊較強。</summary>
        public static bool operator >(HandValue a, HandValue b) => a.Value > b.Value;
        /// <summary>左邊較弱。</summary>
        public static bool operator <(HandValue a, HandValue b) => a.Value < b.Value;
        /// <summary>左邊不弱於右邊。</summary>
        public static bool operator >=(HandValue a, HandValue b) => a.Value >= b.Value;
        /// <summary>左邊不強於右邊。</summary>
        public static bool operator <=(HandValue a, HandValue b) => a.Value <= b.Value;
        /// <summary>平手。</summary>
        public static bool operator ==(HandValue a, HandValue b) => a.Value == b.Value;
        /// <summary>不平手。</summary>
        public static bool operator !=(HandValue a, HandValue b) => a.Value != b.Value;
        /// <summary>除錯用字串，例如 "Flush(0x14001F00)"。</summary>
        public override string ToString() => Category + "(0x" + Value.ToString("X8") + ")";
    }

    /// <summary>
    /// 5–7 張牌評估器。無大表：4 個花色的 13 位元點數遮罩 ＋ 數個 8192 項查表（約 100 KB），執行緒安全、無配置。
    /// 前提：同花（≥5 張同花色）在 ≤7 張中不可能與葫蘆或四條並存，所以同花分支可直接查表。
    /// </summary>
    public static class HandEvaluator
    {
        private const int Shift = HandValue.CategoryShift;
        private static readonly byte[] PopCount = new byte[8192];
        private static readonly ushort[] Top1 = new ushort[8192];
        private static readonly ushort[] Top2 = new ushort[8192];
        private static readonly ushort[] Top3 = new ushort[8192];
        private static readonly ushort[] Top5 = new ushort[8192];
        private static readonly ushort[] StraightTop = new ushort[8192];
        private static readonly int[] FlushValue = new int[8192];

        static HandEvaluator()
        {
            for (int m = 0; m < 8192; m++)
            {
                int n = 0, t1 = 0, t2 = 0, t3 = 0, t5 = 0;
                for (int b = 12; b >= 0; b--)
                {
                    if ((m & (1 << b)) == 0) continue;
                    n++;
                    if (n <= 1) t1 |= 1 << b;
                    if (n <= 2) t2 |= 1 << b;
                    if (n <= 3) t3 |= 1 << b;
                    if (n <= 5) t5 |= 1 << b;
                }
                PopCount[m] = (byte)n;
                Top1[m] = (ushort)t1; Top2[m] = (ushort)t2; Top3[m] = (ushort)t3; Top5[m] = (ushort)t5;

                int st = 0;
                for (int top = 12; top >= 4; top--)
                {
                    int need = 0x1F << (top - 4);
                    if ((m & need) == need) { st = 1 << top; break; }
                }
                if (st == 0 && (m & 0x100F) == 0x100F) st = 1 << 3; // A-2-3-4-5：最高張是 5
                StraightTop[m] = (ushort)st;

                if (n >= 5)
                    FlushValue[m] = st != 0
                        ? ((int)HandCategory.StraightFlush << Shift) | (st << 13)
                        : ((int)HandCategory.Flush << Shift) | t5;
            }
        }

        /// <summary>評估 7 張牌（2 張底牌＋5 張公共牌的常用路徑，無配置）。有重複牌時丟出 <see cref="ArgumentException"/>。</summary>
        public static HandValue Evaluate(Card c1, Card c2, Card c3, Card c4, Card c5, Card c6, Card c7)
        {
            ulong m = c1.Mask | c2.Mask | c3.Mask | c4.Mask | c5.Mask | c6.Mask | c7.Mask;
            if (PopCount64(m) != 7) throw new ArgumentException("含重複或無效的牌");
            return EvaluateMask(m);
        }

        /// <summary>評估 5–7 張牌（陣列版，比 <see cref="IReadOnlyList{T}"/> 版快）。張數不對或有重複牌時丟出 <see cref="ArgumentException"/>。</summary>
        public static HandValue Evaluate(Card[] cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (cards.Length < 5 || cards.Length > 7) throw new ArgumentException("必須是 5 到 7 張牌，實際 " + cards.Length, nameof(cards));
            ulong m = 0;
            for (int i = 0; i < cards.Length; i++) m |= cards[i].Mask;
            if (PopCount64(m) != cards.Length) throw new ArgumentException("含重複或無效的牌", nameof(cards));
            return EvaluateMask(m);
        }

        private static int PopCount64(ulong m)
        {
            return PopCount[(int)(m & 0x1FFF)] + PopCount[(int)((m >> 16) & 0x1FFF)]
                 + PopCount[(int)((m >> 32) & 0x1FFF)] + PopCount[(int)((m >> 48) & 0x1FFF)];
        }

        /// <summary>評估 5–7 張牌。張數不對或有重複牌時丟出 <see cref="ArgumentException"/>。</summary>
        public static HandValue Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (cards.Count < 5 || cards.Count > 7) throw new ArgumentException("必須是 5 到 7 張牌，實際 " + cards.Count, nameof(cards));
            ulong m = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c.IsValid) throw new ArgumentException("含無效牌", nameof(cards));
                ulong bit = c.Mask;
                if ((m & bit) != 0) throw new ArgumentException("重複的牌：" + c, nameof(cards));
                m |= bit;
            }
            return EvaluateMask(m);
        }

        /// <summary>
        /// 最快路徑：以 <see cref="Card.Mask"/> 的 OR 組合評估。呼叫端須保證 5–7 張（不檢查）。
        /// </summary>
        public static HandValue EvaluateMask(ulong mask)
        {
            uint c = (uint)mask & 0x1FFF;
            uint d = (uint)(mask >> 16) & 0x1FFF;
            uint h = (uint)(mask >> 32) & 0x1FFF;
            uint s = (uint)(mask >> 48) & 0x1FFF;

            if (PopCount[c] >= 5) return new HandValue(FlushValue[c]);
            if (PopCount[d] >= 5) return new HandValue(FlushValue[d]);
            if (PopCount[h] >= 5) return new HandValue(FlushValue[h]);
            if (PopCount[s] >= 5) return new HandValue(FlushValue[s]);

            uint ranks = c | d | h | s;
            uint quads = c & d & h & s;
            if (quads != 0)
                return new HandValue(((int)HandCategory.FourOfAKind << Shift) | (int)(quads << 13) | Top1[ranks ^ quads]);

            uint two = (c & d) | (c & h) | (c & s) | (d & h) | (d & s) | (h & s);      // 至少 2 張的點數
            uint three = ((c & d) & (h | s)) | ((h & s) & (c | d));                    // 至少 3 張的點數
            if (three != 0)
            {
                uint t = Top1[three];
                uint pairRest = two ^ t; // 其餘至少一對（含第二組三條）
                if (pairRest != 0)
                    return new HandValue(((int)HandCategory.FullHouse << Shift) | (int)(t << 13) | Top1[pairRest]);
                uint st3 = StraightTop[ranks];
                if (st3 != 0) return new HandValue(((int)HandCategory.Straight << Shift) | (int)(st3 << 13));
                return new HandValue(((int)HandCategory.ThreeOfAKind << Shift) | (int)(t << 13) | Top2[ranks ^ t]);
            }

            uint st = StraightTop[ranks];
            if (st != 0) return new HandValue(((int)HandCategory.Straight << Shift) | (int)(st << 13));

            if (two != 0)
            {
                if (PopCount[two] >= 2)
                {
                    uint tp = Top2[two];
                    return new HandValue(((int)HandCategory.TwoPair << Shift) | (int)(tp << 13) | Top1[ranks ^ tp]);
                }
                return new HandValue(((int)HandCategory.Pair << Shift) | (int)(two << 13) | Top3[ranks ^ two]);
            }
            return new HandValue(Top5[ranks]);
        }
    }
}
