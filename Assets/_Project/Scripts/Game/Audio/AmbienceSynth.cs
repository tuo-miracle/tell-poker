using System;

namespace Poker.Game
{
    /// <summary>
    /// 程式合成的環境音床（無版權問題：沒有用到任何外部取樣）：遠處低聲交談＋偶爾遠處一聲籌碼（沒有低頻底噪）。
    /// 輸出單聲道、可無縫循環（尾端交叉淡入開頭）、固定種子、RMS 正規化到 −24 dBFS（沒有底噪後波形較尖，留峰值餘量；實際音量由 AudioMix.AmbienceDb 決定）。
    /// </summary>
    public static class AmbienceSynth
    {
        public const int SampleRate = 22050, Seconds = 12;
        const double TwoPi = Math.PI * 2.0;

        public static float[] Render(int seconds, int rate, int seed)
        {
            int n = seconds * rate, fade = rate;          // 1 秒交叉淡化
            var s = new float[n + fade];
            var rng = new Random(seed);

            // 1. （2026-11-03 拿掉房間底噪：棕噪音的低頻「嗡」讓使用者覺得「一直有聲音」，只留交談與籌碼聲）

            // 2. 遠處低聲交談：三條帶通雜訊（300–900 Hz），各自用很慢的隨機包絡起伏（像有人在講、停、再講）
            double[] centers = { 330, 520, 780 };
            for (int v = 0; v < centers.Length; v++)
            {
                var bp = new Biquad(centers[v], 1.2, rate);
                double env = 0, target = 0;
                int next = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    if (i >= next) { target = rng.NextDouble() < 0.6 ? 0.4 + 0.6 * rng.NextDouble() : 0.05; next = i + (int)(rate * (0.25 + 0.9 * rng.NextDouble())); }
                    env += (target - env) * (8.0 / rate);
                    // 音節般的抖動（約 4–6 Hz）
                    double syl = 0.6 + 0.4 * Math.Sin(TwoPi * (4.3 + v * 0.7) * i / rate + v);
                    s[i] += (float)(bp.Step(rng.NextDouble() * 2 - 1) * env * syl * 0.6);
                }
            }

            // 3. 偶爾遠處的籌碼聲：很小聲、悶一點
            int clinks = seconds / 3;
            for (int c = 0; c < clinks; c++)
            {
                int at = (int)(rng.NextDouble() * (n - rate * 0.2));
                double f0 = 2400 + rng.NextDouble() * 900;
                for (int i = 0; i < rate * 0.12 && at + i < s.Length; i++)
                {
                    double t = (double)i / rate;
                    s[at + i] += (float)((Math.Sin(TwoPi * f0 * t) + 0.5 * Math.Sin(TwoPi * f0 * 1.52 * t)) * Math.Exp(-t * 55) * 0.05);
                }
            }

            // 無縫循環：最後 fade 個樣本交叉淡入到開頭
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = s[i];
            for (int i = 0; i < fade; i++)
            {
                double g = (double)i / fade;
                double a = Math.Sqrt(g), b = Math.Sqrt(1 - g);   // 等功率
                o[i] = (float)(s[i] * a + s[n + i] * b);
            }

            // 去直流、RMS 正規化到 −24 dBFS
            double mean = 0;
            foreach (var v in o) mean += v;
            mean /= n;
            double sum = 0;
            for (int i = 0; i < n; i++) { o[i] -= (float)mean; sum += o[i] * o[i]; }
            double rms = Math.Sqrt(sum / n);
            if (rms > 0)
            {
                float k = (float)(0.063 / rms);
                for (int i = 0; i < n; i++) o[i] *= k;
            }
            return o;
        }

        /// <summary>RBJ 帶通（常數 0 dB 峰值）。</summary>
        sealed class Biquad
        {
            readonly double _b0, _b2, _a1, _a2;
            double _x1, _x2, _y1, _y2;

            public Biquad(double f, double q, int rate)
            {
                double w = TwoPi * f / rate, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
                _b0 = alpha / a0; _b2 = -alpha / a0;
                _a1 = -2 * Math.Cos(w) / a0; _a2 = (1 - alpha) / a0;
            }

            public double Step(double x)
            {
                double y = _b0 * x + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }
    }
}
