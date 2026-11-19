using System;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>
    /// 169 類起手牌的強度表。索引是 13×13 格：row/col 0 = A … 12 = 2；對角線是對子，
    /// 右上（col &gt; row）是同花，左下是不同花。勝率＝對 1 個隨機對手打到河牌（平手算一半）。
    /// 常數由 <see cref="PreflopTableGenerator"/> 產生（每類 200,000 次，種子 20261012）。
    /// </summary>
    public static class PreflopTable
    {
        /// <summary>類別數。</summary>
        public const int ClassCount = 169;

        static readonly float[] EquityByClass =
        {
            0.8507f, 0.6697f, 0.6634f, 0.6540f, 0.6468f, 0.6289f, 0.6199f, 0.6113f, 0.5986f, 0.5998f, 0.5912f, 0.5820f, 0.5728f, // AA AKs AQs AJs ATs A9s A8s A7s A6s A5s A4s A3s A2s 
            0.6528f, 0.8250f, 0.6344f, 0.6247f, 0.6170f, 0.5995f, 0.5819f, 0.5768f, 0.5651f, 0.5576f, 0.5486f, 0.5423f, 0.5324f, // AKo KK KQs KJs KTs K9s K8s K7s K6s K5s K4s K3s K2s 
            0.6441f, 0.6137f, 0.7995f, 0.6020f, 0.5944f, 0.5753f, 0.5627f, 0.5429f, 0.5361f, 0.5280f, 0.5183f, 0.5097f, 0.5022f, // AQo KQo QQ QJs QTs Q9s Q8s Q7s Q6s Q5s Q4s Q3s Q2s 
            0.6348f, 0.6064f, 0.5826f, 0.7746f, 0.5743f, 0.5574f, 0.5385f, 0.5238f, 0.5052f, 0.4993f, 0.4933f, 0.4808f, 0.4726f, // AJo KJo QJo JJ JTs J9s J8s J7s J6s J5s J4s J3s J2s 
            0.6259f, 0.5967f, 0.5725f, 0.5541f, 0.7498f, 0.5401f, 0.5221f, 0.5073f, 0.4884f, 0.4747f, 0.4651f, 0.4545f, 0.4481f, // ATo KTo QTo JTo TT T9s T8s T7s T6s T5s T4s T3s T2s 
            0.6083f, 0.5796f, 0.5545f, 0.5319f, 0.5165f, 0.7198f, 0.5083f, 0.4913f, 0.4735f, 0.4578f, 0.4396f, 0.4327f, 0.4246f, // A9o K9o Q9o J9o T9o 99 98s 97s 96s 95s 94s 93s 92s 
            0.5989f, 0.5599f, 0.5355f, 0.5151f, 0.4973f, 0.4818f, 0.6910f, 0.4801f, 0.4629f, 0.4466f, 0.4265f, 0.4098f, 0.4044f, // A8o K8o Q8o J8o T8o 98o 88 87s 86s 85s 84s 83s 82s 
            0.5894f, 0.5516f, 0.5166f, 0.4966f, 0.4794f, 0.4628f, 0.4508f, 0.6633f, 0.4540f, 0.4382f, 0.4186f, 0.3989f, 0.3813f, // A7o K7o Q7o J7o T7o 97o 87o 77 76s 75s 74s 73s 72s 
            0.5785f, 0.5420f, 0.5096f, 0.4774f, 0.4626f, 0.4454f, 0.4312f, 0.4235f, 0.6325f, 0.4328f, 0.4116f, 0.3950f, 0.3754f, // A6o K6o Q6o J6o T6o 96o 86o 76o 66 65s 64s 63s 62s 
            0.5781f, 0.5304f, 0.5014f, 0.4712f, 0.4424f, 0.4264f, 0.4153f, 0.4062f, 0.3998f, 0.6047f, 0.4157f, 0.3964f, 0.3765f, // A5o K5o Q5o J5o T5o 95o 85o 75o 65o 55 54s 53s 52s 
            0.5662f, 0.5240f, 0.4918f, 0.4596f, 0.4351f, 0.4078f, 0.3941f, 0.3856f, 0.3781f, 0.3815f, 0.5716f, 0.3879f, 0.3674f, // A4o K4o Q4o J4o T4o 94o 84o 74o 64o 54o 44 43s 42s 
            0.5580f, 0.5132f, 0.4824f, 0.4536f, 0.4280f, 0.4002f, 0.3746f, 0.3674f, 0.3613f, 0.3630f, 0.3506f, 0.5371f, 0.3600f, // A3o K3o Q3o J3o T3o 93o 83o 73o 63o 53o 43o 33 32s 
            0.5506f, 0.5036f, 0.4738f, 0.4431f, 0.4159f, 0.3924f, 0.3691f, 0.3470f, 0.3418f, 0.3442f, 0.3318f, 0.3207f, 0.5034f, // A2o K2o Q2o J2o T2o 92o 82o 72o 62o 52o 42o 32o 22 
        };

        // 百分位：比此類「更強或一樣強」的組合數 / 1326（AA = 6/1326，最差的類 = 1）
        static readonly float[] PercentileByClass = BuildPercentiles();

        /// <summary>兩張底牌所屬的類別（與順序無關）。</summary>
        public static int ClassIndex(Card a, Card b)
        {
            int hi = Math.Max(a.Rank, b.Rank), lo = Math.Min(a.Rank, b.Rank);
            int rh = 14 - hi, rl = 14 - lo;
            if (hi == lo) return rh * 13 + rh;
            return a.Suit == b.Suit ? rh * 13 + rl : rl * 13 + rh;
        }

        /// <summary>類別名稱，例如 "AA"、"AKs"、"72o"。</summary>
        public static string ClassName(int index)
        {
            int row = index / 13, col = index % 13;
            const string R = "AKQJT98765432";
            if (row == col) return new string(R[row], 2);
            return col > row ? "" + R[row] + R[col] + "s" : "" + R[col] + R[row] + "o";
        }

        /// <summary>取該類的一組代表牌（給產生器用）。</summary>
        public static void Representative(int index, out Card a, out Card b)
        {
            int row = index / 13, col = index % 13;
            if (row == col) { a = new Card(14 - row, Suit.Spades); b = new Card(14 - row, Suit.Hearts); }
            else if (col > row) { a = new Card(14 - row, Suit.Spades); b = new Card(14 - col, Suit.Spades); }
            else { a = new Card(14 - col, Suit.Spades); b = new Card(14 - row, Suit.Hearts); }
        }

        /// <summary>某類對 1 個隨機對手的勝率。</summary>
        public static float EquityOfClass(int index) => EquityByClass[index];
        /// <summary>某類的百分位（0 最強附近，1 最弱）。</summary>
        public static float PercentileOfClass(int index) => PercentileByClass[index];
        /// <summary>兩張底牌對 1 個隨機對手的勝率。</summary>
        public static float Equity(Card a, Card b) => EquityByClass[ClassIndex(a, b)];
        /// <summary>兩張底牌在全部 1326 組合中的百分位（含同類，越小越強）。</summary>
        public static float Percentile(Card a, Card b) => PercentileByClass[ClassIndex(a, b)];

        static int Combos(int index) { int row = index / 13, col = index % 13; return row == col ? 6 : col > row ? 4 : 12; }

        static float[] BuildPercentiles()
        {
            var p = new float[ClassCount];
            if (EquityByClass.Length != ClassCount) return p; // 尚未產生表（產生器自身執行時）
            for (int i = 0; i < ClassCount; i++)
            {
                int better = 0;
                for (int j = 0; j < ClassCount; j++)
                    if (EquityByClass[j] >= EquityByClass[i]) better += Combos(j);
                p[i] = better / 1326f;
            }
            return p;
        }
    }
}
