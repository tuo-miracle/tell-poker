using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 講話時的下巴開合（寫實角色用）。有聲音就跟著音量包絡（ClipEnvelope）走；沒有可量的聲音（離線備案、串流）
    /// 就照字幕的字母節奏開合（TextVisemes）。張開快、闔上稍慢，像真的在講話。
    /// </summary>
    public sealed class MouthDriver
    {
        const float Attack = 28f, Release = 20f, ShapeRate = 20f;
        /// <summary>音量低於這個算沒聲音；到 Floor + Span 就全開。</summary>
        const float Floor = 0.06f, Span = 0.55f;

        public float Jaw { get; private set; }
        public float Round { get; private set; }
        public float Close { get; private set; }

        /// <param name="level">音量包絡 0–1；null = 沒有可量的聲音，改用 text。</param>
        /// <param name="t01">這句講到哪裡（0–1，沒有聲音時用）。</param>
        public void Step(float dt, bool talking, float? level, string text, float t01)
        {
            if (dt <= 0) return;
            float target = 0, round = 0, close = 0;
            if (talking)
            {
                if (level.HasValue) target = Mathf.Clamp01((level.Value - Floor) / Span);
                else if (!string.IsNullOrEmpty(text)) target = TextVisemes.Sample(text, t01, out round, out close);
            }
            Jaw = Mathf.Lerp(Jaw, target, 1f - Mathf.Exp(-(target > Jaw ? Attack : Release) * dt));
            float s = 1f - Mathf.Exp(-ShapeRate * dt);
            Round = Mathf.Lerp(Round, round, s);
            Close = Mathf.Lerp(Close, close, s);
        }

        public void Reset() { Jaw = Round = Close = 0; }
    }

    /// <summary>把字幕的字母粗略換成嘴型（母音開、子音小、m/b/p 閉唇、o/u/w 圓唇、空白與標點停頓）。</summary>
    public static class TextVisemes
    {
        /// <param name="t01">這句講到哪裡；0 以前與 1 以後＝閉嘴。</param>
        /// <returns>下巴開口 0–1。</returns>
        public static float Sample(string text, float t01, out float round, out float close)
        {
            round = close = 0;
            if (string.IsNullOrEmpty(text) || t01 <= 0 || t01 >= 1) return 0;
            float total = 0;
            foreach (var c in text) total += Weight(c);
            float x = t01 * total;
            foreach (var c in text)
            {
                float wc = Weight(c);
                if (x < wc) return Open(c, out round, out close);
                x -= wc;
            }
            return 0;
        }

        /// <summary>每個字元佔多少時間（空白與標點是停頓，比字母長）。</summary>
        static float Weight(char c)
        {
            if (c == ' ') return 1.3f;
            if (",;:.!?".IndexOf(c) >= 0) return 3f;
            return char.IsLetter(c) ? 1f : 0.5f;
        }

        static float Open(char c, out float round, out float close)
        {
            round = close = 0;
            switch (char.ToLowerInvariant(c))
            {
                case 'a': return 1f;
                case 'e': return 0.75f;
                case 'i': case 'y': return 0.55f;
                case 'o': round = 1f; return 0.8f;
                case 'u': round = 1f; return 0.55f;
                case 'w': round = 1f; return 0.3f;
                case 'm': case 'b': case 'p': close = 1f; return 0f;
                case 'f': case 'v': return 0.12f;
            }
            return char.IsLetter(c) ? 0.2f : 0f;
        }
    }
}
