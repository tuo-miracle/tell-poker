using System;
using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>花色。</summary>
    public enum Suit : byte
    {
        /// <summary>梅花（c）</summary>
        Clubs = 0,
        /// <summary>方塊（d）</summary>
        Diamonds = 1,
        /// <summary>紅心（h）</summary>
        Hearts = 2,
        /// <summary>黑桃（s）</summary>
        Spades = 3,
    }

    /// <summary>
    /// 一張撲克牌（值型別）。點數 2–14（14 = A）。<c>default(Card)</c> 為無效牌（<see cref="IsValid"/> = false）。
    /// </summary>
    public readonly struct Card : IEquatable<Card>
    {
        private const string RankChars = "23456789TJQKA";
        private const string SuitChars = "cdhs";

        // 0 = 無效；否則 = Index + 1。
        private readonly byte _code;

        /// <summary>以點數（2–14）與花色建立一張牌。</summary>
        public Card(int rank, Suit suit)
        {
            if (rank < 2 || rank > 14) throw new ArgumentOutOfRangeException(nameof(rank), rank, "點數必須介於 2 到 14");
            if ((int)suit > 3) throw new ArgumentOutOfRangeException(nameof(suit));
            _code = (byte)((rank - 2) * 4 + (int)suit + 1);
        }

        /// <summary>點數，2–14（11=J、12=Q、13=K、14=A）。</summary>
        public int Rank => (_code - 1) / 4 + 2;

        /// <summary>花色。</summary>
        public Suit Suit => (Suit)((_code - 1) % 4);

        /// <summary>0–51 的索引：(點數-2)*4 + 花色。</summary>
        public int Index => _code - 1;

        /// <summary>是否為有效牌（非 default）。</summary>
        public bool IsValid => _code != 0;

        /// <summary>評估器用的 64 位元遮罩：位元 = 花色*16 + (點數-2)。</summary>
        public ulong Mask => MaskByCode[_code];

        // 以 _code 查表（0 = 無效牌 → 0），避免評估熱路徑上的除法。
        private static readonly ulong[] MaskByCode = BuildMasks();
        private static ulong[] BuildMasks()
        {
            var t = new ulong[53];
            for (int i = 0; i < 52; i++) t[i + 1] = 1UL << ((i % 4) * 16 + i / 4);
            return t;
        }

        /// <summary>由 0–51 的索引建立。</summary>
        public static Card FromIndex(int index)
        {
            if (index < 0 || index > 51) throw new ArgumentOutOfRangeException(nameof(index));
            return new Card(index / 4 + 2, (Suit)(index % 4));
        }

        /// <summary>解析兩字元表示法，例如 "As"、"Td"、"2c"。</summary>
        public static Card Parse(string text)
        {
            if (text == null || text.Length != 2) throw new FormatException("牌面必須是兩個字元，例如 \"As\"：" + text);
            int r = RankChars.IndexOf(char.ToUpperInvariant(text[0]));
            int s = SuitChars.IndexOf(char.ToLowerInvariant(text[1]));
            if (r < 0 || s < 0) throw new FormatException("無法解析的牌面：" + text);
            return new Card(r + 2, (Suit)s);
        }

        /// <summary>解析連續的多張牌，可含空白，例如 "AsKd 7c"。</summary>
        public static Card[] ParseMany(string text)
        {
            var list = new List<Card>();
            var compact = (text ?? "").Replace(" ", "");
            if (compact.Length % 2 != 0) throw new FormatException("牌面字串長度必須是偶數：" + text);
            for (int i = 0; i < compact.Length; i += 2) list.Add(Parse(compact.Substring(i, 2)));
            return list.ToArray();
        }

        /// <summary>是否為同一張牌。</summary>
        public bool Equals(Card other) => _code == other._code;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Card c && Equals(c);
        /// <inheritdoc/>
        public override int GetHashCode() => _code;
        /// <summary>是否為同一張牌。</summary>
        public static bool operator ==(Card a, Card b) => a._code == b._code;
        /// <summary>是否為不同的牌。</summary>
        public static bool operator !=(Card a, Card b) => a._code != b._code;

        /// <summary>兩字元表示法，例如 "As"、"Td"；無效牌為 "??"。</summary>
        public override string ToString() => IsValid ? new string(new[] { RankChars[Rank - 2], SuitChars[(int)Suit] }) : "??";
    }
}
