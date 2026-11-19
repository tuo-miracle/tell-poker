using System.Collections.Generic;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>牌桌音效：首次使用時把 PokerSfx 的取樣轉成 AudioClip；幾個 3D AudioSource 輪流播。每種音效乘上 AudioMix 的修正，整體乘上 Gain（壓低）。</summary>
    public sealed class GameAudio
    {
        static readonly Dictionary<SfxKind, AudioClip> Clips = new Dictionary<SfxKind, AudioClip>();
        readonly AudioSource[] _sources;
        int _next;
        public int PlayedCount { get; private set; }
        public SfxKind LastPlayed { get; private set; }
        float _gain = 1f;

        /// <summary>整體增益（有人講話時由 GameController 壓低；正在播的聲音也跟著變）。</summary>
        public float Gain
        {
            get => _gain;
            set
            {
                if (Mathf.Abs(value - _gain) < 1e-4f) return;
                _gain = value;
                foreach (var s in _sources) s.volume = value;
            }
        }

        public GameAudio(Transform parent, int voices = 6)
        {
            _sources = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(parent, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0.85f;
                s.minDistance = 0.3f;
                s.maxDistance = 4f;
                s.rolloffMode = AudioRolloffMode.Linear;
                _sources[i] = s;
            }
        }

        public static AudioClip Clip(SfxKind k)
        {
            if (Clips.TryGetValue(k, out var c) && c != null) return c;
            var samples = PokerSfx.Render(k);
            c = AudioClip.Create("sfx_" + k, samples.Length, 1, PokerSfx.SampleRate, false);
            c.SetData(samples, 0);
            Clips[k] = c;
            return c;
        }

        public void Play(SfxKind k, Vector3 worldPos, float volume = 1f, float pitch = 1f)
        {
            PlayedCount++;
            LastPlayed = k;
            var s = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            s.transform.position = worldPos;
            s.pitch = pitch;
            s.volume = _gain;
            s.PlayOneShot(Clip(k), Mathf.Clamp01(volume * AudioMix.Linear(AudioMix.TrimDb(k))));
        }
    }
}
