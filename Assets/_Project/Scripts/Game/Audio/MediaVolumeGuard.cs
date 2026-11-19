using System;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>系統媒體音量的來源（實機：Android STREAM_MUSIC）。回傳 false＝不知道（Editor、batchmode、讀取失敗）。</summary>
    public interface IMediaVolumeSource
    {
        bool TryRead(out int volume, out int max);
    }

    /// <summary>
    /// Android 媒體音量：AudioManager.getStreamVolume／getStreamMaxVolume(STREAM_MUSIC＝3)。
    /// 只在 Android 實機讀；Editor、batchmode 一律回傳「不知道」。讀取失敗一次就不再試（記一次警告）。
    /// </summary>
    public sealed class AndroidMediaVolumeSource : IMediaVolumeSource
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const int StreamMusic = 3;
        AndroidJavaObject _audio;
        bool _failed;
#endif

        public bool TryRead(out int volume, out int max)
        {
            volume = max = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_failed || Application.isBatchMode) return false;
            try
            {
                if (_audio == null)
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        _audio = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                volume = _audio.Call<int>("getStreamVolume", StreamMusic);
                max = _audio.Call<int>("getStreamMaxVolume", StreamMusic);
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Debug.LogWarning("[Audio] media volume unavailable: " + e.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 媒體音量守門：開始時與之後每 PollSeconds 讀一次；音量是 0 時 Muted（遊戲在牌桌旁顯示 Notice）。
    /// 2026-10-13 實機：遊戲音訊在播（AAudio started），但媒體音量 0，Android 把它靜音（mutedState:streamVolume），玩家什麼都聽不到。
    /// 音量改變時記 log：<c>[Audio] media volume v/max</c>，0 時加上「— speech muted」；不知道時記一次 <c>[Audio] media volume unknown</c>。
    /// </summary>
    public sealed class MediaVolumeGuard
    {
        public const float PollSeconds = 5f;
        /// <summary>聲音被系統關掉，只能用字提示（不是角色說的話，不走 SpeechChannel）。</summary>
        public const string Notice = "Sound is off — press the volume + button on your headset";

        readonly IMediaVolumeSource _source;
        readonly Action<string> _log;
        float _untilPoll;
        bool _started, _unknownLogged;

        /// <summary>讀得到音量。</summary>
        public bool Known { get; private set; }
        public int Volume { get; private set; } = -1;
        public int Max { get; private set; } = -1;
        /// <summary>媒體音量是 0（讀不到時不算）。</summary>
        public bool Muted => Known && Volume <= 0;

        public MediaVolumeGuard(IMediaVolumeSource source, Action<string> log)
        {
            _source = source ?? new AndroidMediaVolumeSource();
            _log = log ?? (_ => { });
        }

        public void Tick(float dt)
        {
            _untilPoll -= dt;
            if (_started && _untilPoll > 1e-4f) return;
            _started = true;
            _untilPoll = PollSeconds;
            Poll();
        }

        void Poll()
        {
            if (!_source.TryRead(out int v, out int max))
            {
                Known = false;
                if (!_unknownLogged) _log("[Audio] media volume unknown");
                _unknownLogged = true;
                return;
            }
            if (Known && v == Volume && max == Max) return;
            Known = true;
            Volume = v;
            Max = max;
            _log(v <= 0 ? $"[Audio] media volume {v}/{max} — speech muted" : $"[Audio] media volume {v}/{max}");
        }
    }
}
