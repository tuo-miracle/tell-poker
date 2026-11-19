using System;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 遊戲層與工具只引用這一個來源：預設轉發實機（ISDK）手部，Editor 模擬時改轉發 .hands 回放。
    /// </summary>
    public sealed class HandsSourceSelector : MonoBehaviour, IHandsFrameSource
    {
        [SerializeField] MonoBehaviour live;       // IsdkHandsSource
        [SerializeField] MonoBehaviour playback;   // RecordingHandsSource

        IHandsFrameSource _active;
        readonly HandsFrame _empty = new HandsFrame();

        public HandsFrame Current => _active != null ? _active.Current : _empty;
        public event Action<HandsFrame> FrameUpdated;
        public bool IsPlayback => _active != null && ReferenceEquals(_active, playback);

        void Awake()
        {
            if (_active == null) UseLive();
        }

        public void UseLive() => Switch(live as IHandsFrameSource, playback as IHandsFrameSource);
        public void UsePlayback() => Switch(playback as IHandsFrameSource, live as IHandsFrameSource);

        void Switch(IHandsFrameSource on, IHandsFrameSource off)
        {
            if (_active != null) _active.FrameUpdated -= Forward;
            _active = on;
            if (_active != null) _active.FrameUpdated += Forward;
            if (off is Behaviour b) b.enabled = false;
            if (on is Behaviour a) a.enabled = true;
        }

        void OnDestroy()
        {
            if (_active != null) _active.FrameUpdated -= Forward;
        }

        void Forward(HandsFrame f) => FrameUpdated?.Invoke(f);
    }
}
