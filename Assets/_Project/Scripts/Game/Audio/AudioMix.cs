using Poker.Characters;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 混音表（dB）：台詞永遠在最上面。
    /// - 台詞：預錄音檔（匯入時正規化峰值），角色頭上的 3D AudioSource，音量 1。
    /// - 音效：每種一個修正值（TrimDb，乘在呼叫處的音量上），讓最響的那一下也比台詞小 SfxHeadroomDb 以上；
    ///   有人講話時再壓 SfxDuckDb。
    /// - 環境音床（AmbienceSynth）：2D 循環，AmbienceDb，有人講話時再壓 AmbienceDuckDb。
    /// 數字由 AudioMixTests 量測（ITU-R BS.1770 響度：音效取最響 momentary，台詞與環境音取 integrated）並印出表格。
    /// </summary>
    public static class AudioMix
    {
        public const float SfxDuckDb = -7f, AmbienceDuckDb = -9f;
        /// <summary>環境音床的音量（AudioSource.volume，dB）。2026-11-03 使用者覺得「一直有聲音」：從 −16 壓到 −30（約比台詞小 32 dB）。</summary>
        public const float AmbienceDb = -30f;
        /// <summary>音效最響的時候也至少比台詞小這麼多（3 dB，加上台詞在 ~1.1 m 外、音效在手邊 0.3–0.5 m 的距離差約 1 dB）。</summary>
        public const float SfxHeadroomDb = 4f;

        public static float Linear(float db) => Mathf.Pow(10f, db / 20f);

        /// <summary>每種音效的修正（dB）。</summary>
        public static float TrimDb(SfxKind k)
        {
            switch (k)
            {
                case SfxKind.ChipClack: return -3.5f;
                case SfxKind.ChipSlide: return -5f;
                case SfxKind.CardSlide: return -7.5f;
                case SfxKind.Knock: return -6f;
                case SfxKind.Invalid: return -6.5f;
                case SfxKind.Deal: return -3.5f;
                case SfxKind.Win: return -10.5f;   // 三和弦 0.7 s，原本比台詞還響 5.7 dB
                case SfxKind.Flip: return 0f;
                default: return 0f;
            }
        }

        /// <summary>遊戲裡呼叫這種音效時用到的最大音量（GameController 的呼叫處；量測混音用）。</summary>
        public static float LoudestCallVolume(SfxKind k)
        {
            switch (k)
            {
                case SfxKind.ChipClack: return 0.8f;
                case SfxKind.ChipSlide: return 0.6f;
                case SfxKind.CardSlide: return 0.7f;
                case SfxKind.Knock: return 0.8f;
                case SfxKind.Invalid: return 0.7f;
                case SfxKind.Deal: return 0.6f;
                case SfxKind.Win: return 0.8f;
                case SfxKind.Flip: return 0.6f;
                default: return 1f;
            }
        }

        /// <summary>有人在講話：整桌發言權有人在講，或玩家自己正在講（壓低也讓喇叭聲少漏進麥克風）。</summary>
        public static bool AnyoneSpeaking(SpeechFloor floor, bool playerSpeaking) => playerSpeaking || (floor != null && floor.Speaker != null);
    }

    /// <summary>
    /// 壓低（ducking）：有人講話時增益在 AttackSeconds 內線性（dB）降到 DepthDb；講完先撐 HoldSeconds
    /// （SpeechFloor 換人的停頓不會讓背景彈回來），再用 ReleaseSeconds 慢慢回到 0 dB。不配置記憶體。
    /// </summary>
    public sealed class Ducker
    {
        public const float AttackSeconds = 0.08f, HoldSeconds = 0.4f, ReleaseSeconds = 0.6f;
        public readonly float DepthDb;
        float _hold;

        public Ducker(float depthDb) { DepthDb = Mathf.Min(0f, depthDb); }

        public float GainDb { get; private set; }
        public float Gain => AudioMix.Linear(GainDb);

        public void Tick(float dt, bool speaking)
        {
            if (dt <= 0f) return;
            bool duck = speaking || _hold > 0f;
            if (speaking) _hold = HoldSeconds;
            else if (_hold > 0f) _hold -= dt;
            float depth = -DepthDb;
            if (depth <= 0f) { GainDb = 0f; return; }
            GainDb = duck
                ? Mathf.MoveTowards(GainDb, DepthDb, depth / AttackSeconds * dt)
                : Mathf.MoveTowards(GainDb, 0f, depth / ReleaseSeconds * dt);
        }
    }
}
