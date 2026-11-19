using System;

namespace Poker.Game
{
    public enum SfxKind { ChipClack, ChipSlide, CardSlide, Knock, Invalid, Deal, Win, Flip }

    /// <summary>
    /// 程式合成的牌桌音效（44.1 kHz 單聲道），做法參考 claw-hand 的 SfxSynth：固定種子、首尾淡出、正規化峰值。
    /// </summary>
    public static class PokerSfx
    {
        public const int SampleRate = 44100;
        const double TwoPi = Math.PI * 2.0;

        public static float[] Render(SfxKind kind)
        {
            switch (kind)
            {
                case SfxKind.ChipClack: return Finish(Clack(0.09f, 3, 1f), 0.85f);
                case SfxKind.ChipSlide: return Finish(Slide(0.28f, 7, 2600, 0.5f, 4), 0.6f);
                case SfxKind.CardSlide: return Finish(Slide(0.22f, 13, 5200, 0.25f, 0), 0.5f);
                case SfxKind.Knock: return Finish(Knock(), 0.8f);
                case SfxKind.Invalid: return Finish(Buzz(), 0.5f);
                case SfxKind.Deal: return Finish(Slide(0.12f, 31, 6500, 0.15f, 0), 0.45f);
                case SfxKind.Win: return Finish(Chime(), 0.6f);
                case SfxKind.Flip: return Finish(Slap(0.08f, 41, 0.15f), 0.45f);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        // 陶瓷籌碼互撞：幾個高頻衰減共振加一點點擊雜訊。
        static float[] Clack(float dur, int seed, float bright)
        {
            var s = new float[(int)(dur * SampleRate)];
            var rng = new Random(seed);
            double[] f = { 2650, 4120, 5830 * bright, 7400 };
            double[] a = { 1, 0.7, 0.45, 0.25 };
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / SampleRate, v = 0;
                for (int k = 0; k < f.Length; k++) v += a[k] * Math.Sin(TwoPi * f[k] * t) * Math.Exp(-t * (60 + 25 * k));
                v += (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 400) * 0.8;
                s[i] = (float)v;
            }
            return s;
        }

        // 滑行：帶通雜訊（簡易一階濾波），可疊上幾下零星的喀啦聲。
        static float[] Slide(float dur, int seed, double cutoff, float grit, int clicks)
        {
            var s = new float[(int)(dur * SampleRate)];
            var rng = new Random(seed);
            double lp = 0, lp2 = 0, alpha = 1 - Math.Exp(-TwoPi * cutoff / SampleRate);
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / SampleRate;
                double n = rng.NextDouble() * 2 - 1;
                lp += alpha * (n - lp);
                lp2 += 0.02 * (lp - lp2);
                double env = Math.Sin(Math.PI * t / dur);
                s[i] = (float)((lp - lp2) * env * (1 + grit * (rng.NextDouble() - 0.5)));
            }
            if (clicks > 0)
            {
                var c = Clack(0.06f, seed + 1, 0.8f);
                for (int k = 0; k < clicks; k++)
                {
                    int at = (int)((0.1 + 0.8 * rng.NextDouble()) * (s.Length - c.Length));
                    for (int j = 0; j < c.Length && at + j < s.Length; j++) s[at + j] += c[j] * 0.35f;
                }
            }
            return s;
        }

        // 拍牌：低頻重擊加寬頻雜訊，weight 越大越低沉。
        static float[] Slap(float dur, int seed, float weight)
        {
            var s = new float[(int)(dur * SampleRate)];
            var rng = new Random(seed);
            double lp = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / SampleRate;
                double n = rng.NextDouble() * 2 - 1;
                lp += 0.35 * (n - lp);
                double thump = Math.Sin(TwoPi * (90 + 60 * (1 - weight)) * t) * Math.Exp(-t * 28) * weight;
                double crack = lp * Math.Exp(-t * (90 - 40 * weight));
                s[i] = (float)(thump * 1.2 + crack);
            }
            return s;
        }

        // 指節敲桌：兩下木頭聲。
        static float[] Knock()
        {
            var s = new float[(int)(0.32f * SampleRate)];
            foreach (double start in new[] { 0.0, 0.17 })
            {
                int o = (int)(start * SampleRate);
                for (int i = 0; o + i < s.Length && i < 0.12 * SampleRate; i++)
                {
                    double t = (double)i / SampleRate;
                    double v = Math.Sin(TwoPi * 210 * t) * Math.Exp(-t * 45) + 0.5 * Math.Sin(TwoPi * 540 * t) * Math.Exp(-t * 80);
                    s[o + i] += (float)v;
                }
            }
            return s;
        }

        // 無效動作：兩聲下行的柔和嗶聲。
        static float[] Buzz()
        {
            var s = new float[(int)(0.36f * SampleRate)];
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / SampleRate;
                double f = t < 0.16 ? 440 : 330;
                double local = t < 0.16 ? t : t - 0.18;
                double env = local < 0 ? 0 : Math.Exp(-local * 14) * Math.Min(1, local * 200);
                s[i] = (float)((Math.Sin(TwoPi * f * t) + 0.3 * Math.Sin(TwoPi * f * 2 * t)) * env);
            }
            return s;
        }

        // 贏得底池：上行三和弦。
        static float[] Chime()
        {
            var s = new float[(int)(0.7f * SampleRate)];
            double[] notes = { 784, 988, 1175 };
            for (int k = 0; k < notes.Length; k++)
            {
                int o = (int)(k * 0.08 * SampleRate);
                for (int i = 0; o + i < s.Length; i++)
                {
                    double t = (double)i / SampleRate;
                    s[o + i] += (float)(Math.Sin(TwoPi * notes[k] * t) * Math.Exp(-t * 6) * Math.Min(1, t * 300));
                }
            }
            return s;
        }

        /// <summary>首尾各 3 ms 淡入淡出，再正規化到指定峰值。</summary>
        static float[] Finish(float[] s, float peak)
        {
            int fade = Math.Min(s.Length / 4, (int)(0.003 * SampleRate));
            for (int i = 0; i < fade; i++)
            {
                float g = (float)i / fade;
                s[i] *= g;
                s[s.Length - 1 - i] *= g;
            }
            float max = 0f;
            foreach (var v in s) max = Math.Max(max, Math.Abs(v));
            if (max > 0f)
                for (int i = 0; i < s.Length; i++) s[i] *= peak / max;
            return s;
        }
    }
}
