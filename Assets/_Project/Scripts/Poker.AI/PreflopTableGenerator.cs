using System.Text;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>離線產生 <see cref="PreflopTable"/> 常數表：每類起手牌對 1 個隨機對手的 Monte Carlo 勝率。</summary>
    public static class PreflopTableGenerator
    {
        /// <summary>回傳 169 類的勝率（索引同 <see cref="PreflopTable.ClassIndex"/>）。</summary>
        public static float[] Generate(int trialsPerClass, ulong seed)
        {
            var r = new float[PreflopTable.ClassCount];
            for (int k = 0; k < r.Length; k++)
            {
                PreflopTable.Representative(k, out var a, out var b);
                r[k] = (float)Equity.Estimate(a, b, null, 1, trialsPerClass, new Pcg32(seed, (ulong)k));
            }
            return r;
        }

        /// <summary>輸出成可貼進 PreflopTable.cs 的 C# 陣列（每列 13 類）。</summary>
        public static string ToCSharp(float[] t)
        {
            var sb = new StringBuilder();
            for (int row = 0; row < 13; row++)
            {
                sb.Append("            ");
                for (int col = 0; col < 13; col++) sb.Append(t[row * 13 + col].ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append("f, ");
                sb.Append("// ");
                for (int col = 0; col < 13; col++) sb.Append(PreflopTable.ClassName(row * 13 + col)).Append(' ');
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
