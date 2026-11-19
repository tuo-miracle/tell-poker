using System.Collections.Generic;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>語音的音量包絡（每 20 ms 一格 RMS，以整段的第 95 百分位正規化成 0–1），給寫實角色的嘴巴用。</summary>
    public static class ClipEnvelope
    {
        public const float Window = 0.02f;
        const int CacheSize = 16;
        static readonly List<(AudioClip clip, float[] env)> Cache = new List<(AudioClip, float[])>();

        /// <summary>從 PCM 樣本建包絡。</summary>
        public static float[] Build(float[] samples, int channels, int frequency)
        {
            channels = Mathf.Max(1, channels);
            int frames = samples.Length / channels;
            int win = Mathf.Max(1, Mathf.RoundToInt(Window * frequency));
            int n = (frames + win - 1) / win;
            var env = new float[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                int a = i * win, b = Mathf.Min(frames, a + win);
                for (int f = a; f < b; f++)
                for (int c = 0; c < channels; c++)
                {
                    float s = samples[f * channels + c];
                    sum += s * s;
                }
                env[i] = (float)System.Math.Sqrt(sum / Mathf.Max(1, (b - a) * channels));
            }
            var sorted = (float[])env.Clone();
            System.Array.Sort(sorted);
            float peak = sorted.Length > 0 ? sorted[Mathf.Clamp(Mathf.FloorToInt(sorted.Length * 0.95f), 0, sorted.Length - 1)] : 0;
            if (peak < 1e-4f) peak = 1e-4f;
            for (int i = 0; i < n; i++) env[i] = Mathf.Clamp01(env[i] / peak);
            return env;
        }

        /// <summary>第 time 秒的音量（0–1，線性內插；範圍外 0）。</summary>
        public static float Sample(float[] env, float time)
        {
            if (env == null || env.Length == 0 || time < 0) return 0;
            float x = time / Window;
            int i = Mathf.FloorToInt(x);
            if (i >= env.Length) return 0;
            float b = i + 1 < env.Length ? env[i + 1] : 0;
            return Mathf.Lerp(env[i], b, x - i);
        }

        /// <summary>某個音檔的包絡（快取最近 16 個，讀不到的也記住、不每幀重試）；null、串流或讀不到樣本（壓縮載入）就回 null。</summary>
        public static float[] For(AudioClip clip)
        {
            if (clip == null) return null;
            for (int i = Cache.Count - 1; i >= 0; i--)
            {
                if (Cache[i].clip == null) { Cache.RemoveAt(i); continue; }   // 已被 Destroy 的拼接音檔
                if (Cache[i].clip == clip) return Cache[i].env;
            }
            float[] env = null;
            if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.samples > 0
                && (clip.loadState == AudioDataLoadState.Loaded || clip.LoadAudioData()) && clip.loadState == AudioDataLoadState.Loaded)
            {
                var s = new float[clip.samples * clip.channels];
                if (clip.GetData(s, 0)) env = Build(s, clip.channels, clip.frequency);
            }
            if (Cache.Count >= CacheSize) Cache.RemoveAt(0);
            Cache.Add((clip, env));
            return env;
        }
    }
}
