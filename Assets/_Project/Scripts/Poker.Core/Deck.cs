using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>
    /// PCG32（PCG-XSH-RR 64/32，M.E. O'Neill）。自寫實作，確保所有 runtime（Mono、IL2CPP、.NET）產生相同序列；
    /// 不使用 <c>System.Random</c>，因為其演算法隨 runtime 版本不同。非執行緒安全。
    /// </summary>
    public sealed class Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private ulong _state;
        private readonly ulong _inc;

        /// <summary>以種子與序列編號初始化（等同官方 <c>pcg32_srandom_r(seed, sequence)</c>）。</summary>
        public Pcg32(ulong seed, ulong sequence = 54)
        {
            unchecked
            {
                _state = 0;
                _inc = (sequence << 1) | 1UL;
                NextUInt();
                _state += seed;
                NextUInt();
            }
        }

        /// <summary>下一個 32 位元均勻亂數。</summary>
        public uint NextUInt()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * Multiplier + _inc;
                uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        /// <summary>[0, bound) 的無偏均勻整數（官方 <c>pcg32_boundedrand_r</c> 的拒絕取樣）。</summary>
        public uint NextBounded(uint bound)
        {
            if (bound == 0) throw new System.ArgumentOutOfRangeException(nameof(bound));
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }
    }

    /// <summary>種子化洗牌。給定相同種子，結果在任何平台都相同。</summary>
    public static class Deck
    {
        /// <summary>回傳以 <paramref name="seed"/> 洗好的 52 張牌（Fisher–Yates，PCG32）。</summary>
        public static IReadOnlyList<Card> Shuffled(ulong seed)
        {
            var cards = new Card[52];
            for (int i = 0; i < 52; i++) cards[i] = Card.FromIndex(i);
            var rng = new Pcg32(seed);
            for (int i = 51; i > 0; i--)
            {
                int j = (int)rng.NextBounded((uint)(i + 1));
                var t = cards[i]; cards[i] = cards[j]; cards[j] = t;
            }
            return cards;
        }
    }

    /// <summary>牌局種子來源。0 代表「每次開遊戲抽新種子」（正式場景）；非 0 照用，方便測試與重播。</summary>
    public static class SeedSource
    {
        /// <summary>設定的種子非 0 就照用；0 就用系統密碼學亂數抽一個非 0 的新種子。</summary>
        public static ulong ForSession(ulong configured)
        {
            if (configured != 0) return configured;
            var bytes = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                ulong seed;
                do { rng.GetBytes(bytes); seed = System.BitConverter.ToUInt64(bytes, 0); } while (seed == 0);
                return seed;
            }
        }
    }
}
