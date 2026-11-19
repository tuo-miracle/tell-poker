using System;

namespace Poker.Voice
{
    /// <summary>PCM 16-bit WAV 編解碼與簡單的線性重取樣（麥克風取樣率不是 16 kHz 時用）。</summary>
    public static class WavCodec
    {
        public static byte[] EncodePcm16(float[] samples, int offset, int count, int sampleRate)
        {
            var b = new byte[44 + count * 2];
            void Str(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
            void U32(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
            void U16(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
            Str(0, "RIFF"); U32(4, 36 + count * 2); Str(8, "WAVE");
            Str(12, "fmt "); U32(16, 16); U16(20, 1); U16(22, 1); U32(24, sampleRate); U32(28, sampleRate * 2); U16(32, 2); U16(34, 16);
            Str(36, "data"); U32(40, count * 2);
            for (int i = 0; i < count; i++)
            {
                float f = samples[offset + i];
                if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                short s = (short)Math.Round(f * 32767f);
                b[44 + i * 2] = (byte)s;
                b[45 + i * 2] = (byte)(s >> 8);
            }
            return b;
        }

        /// <summary>float → PCM16 little-endian（不含標頭，給 /live 串流用）。回傳寫入的位元組數。</summary>
        public static int ToPcm16(float[] samples, int count, byte[] dst)
        {
            for (int i = 0; i < count; i++)
            {
                float f = samples[i];
                if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                short s = (short)Math.Round(f * 32767f);
                dst[i * 2] = (byte)s;
                dst[i * 2 + 1] = (byte)(s >> 8);
            }
            return count * 2;
        }

        /// <summary>解出 PCM 16-bit WAV（多聲道時交錯存放）。不是 RIFF/WAVE 或不是 PCM 16-bit 就回傳 false。</summary>
        public static bool TryDecode(byte[] wav, out float[] samples, out int sampleRate, out int channels)
        {
            samples = null; sampleRate = 0; channels = 0;
            if (wav == null || wav.Length < 44) return false;
            if (Ascii(wav, 0) != "RIFF" || Ascii(wav, 8) != "WAVE") return false;
            int p = 12, bits = 0, format = 0;
            bool fmt = false;
            while (p + 8 <= wav.Length)
            {
                string id = Ascii(wav, p);
                int size = BitConverter.ToInt32(wav, p + 4);
                int body = p + 8;
                if (size < 0) return false;
                if (id == "fmt " && body + 16 <= wav.Length)
                {
                    format = BitConverter.ToUInt16(wav, body);
                    channels = BitConverter.ToUInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToUInt16(wav, body + 14);
                    fmt = true;
                }
                else if (id == "data")
                {
                    if (!fmt || format != 1 || bits != 16 || channels < 1 || sampleRate <= 0) return false;
                    int len = Math.Min(size, wav.Length - body) / 2;
                    samples = new float[len];
                    for (int i = 0; i < len; i++) samples[i] = BitConverter.ToInt16(wav, body + i * 2) / 32768f;
                    return true;
                }
                p = body + size + (size & 1);
            }
            return false;
        }

        public static float[] Resample(float[] src, int count, int fromRate, int toRate)
        {
            if (fromRate == toRate) { var copy = new float[count]; Array.Copy(src, copy, count); return copy; }
            int n = (int)Math.Round((long)count * toRate / (double)fromRate);
            var dst = new float[n];
            double step = fromRate / (double)toRate;
            for (int i = 0; i < n; i++)
            {
                double x = i * step;
                int a = (int)x;
                if (a >= count - 1) { dst[i] = src[count - 1]; continue; }
                float t = (float)(x - a);
                dst[i] = src[a] + (src[a + 1] - src[a]) * t;
            }
            return dst;
        }

        static string Ascii(byte[] b, int at) => at + 4 <= b.Length ? new string(new[] { (char)b[at], (char)b[at + 1], (char)b[at + 2], (char)b[at + 3] }) : "";
    }
}
