using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 混音（AudioMix）：環境音床，以及有人講話時壓低音效與環境音（台詞永遠在最上面）。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>環境音床開關（要在 Init 之前設定）。</summary>
        public bool AmbienceEnabled = true;
        /// <summary>環境音床的 AudioSource（2D、循環）。</summary>
        public AudioSource Ambience { get; private set; }
        public MixState Mix { get; } = new MixState();

        static AudioClip _ambienceClip;

        /// <summary>壓低的狀態（測試與除錯看得到）。</summary>
        public sealed class MixState
        {
            public readonly Ducker Sfx = new Ducker(AudioMix.SfxDuckDb);
            public readonly Ducker Bed = new Ducker(AudioMix.AmbienceDuckDb);
            public float SfxGain => Sfx.Gain;
            public float BedGain => Bed.Gain;
        }

        void InitMix()
        {
            var go = new GameObject("Ambience");
            go.transform.SetParent(Props.Root, false);
            Ambience = go.AddComponent<AudioSource>();
            Ambience.playOnAwake = false;
            Ambience.loop = true;
            Ambience.spatialBlend = 0f;
            Ambience.priority = 200;   // 聲音不夠時先犧牲它
            Ambience.volume = AudioMix.Linear(AudioMix.AmbienceDb);
            if (!AmbienceEnabled) return;
            if (_ambienceClip == null)
            {
                var data = AmbienceSynth.Render(AmbienceSynth.Seconds, AmbienceSynth.SampleRate, 7);
                _ambienceClip = AudioClip.Create("ambience_bed", data.Length, 1, AmbienceSynth.SampleRate, false);
                _ambienceClip.SetData(data, 0);
            }
            Ambience.clip = _ambienceClip;
            Ambience.Play();
        }

        void TickMix(float dt)
        {
            bool speaking = AudioMix.AnyoneSpeaking(Floor, PlayerSpeaking);
            Mix.Sfx.Tick(dt, speaking);
            Mix.Bed.Tick(dt, speaking);
            Audio.Gain = Mix.SfxGain;
            if (Ambience != null) Ambience.volume = AudioMix.Linear(AudioMix.AmbienceDb) * Mix.BedGain;
        }
    }
}
