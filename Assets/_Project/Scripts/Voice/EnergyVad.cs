using System;
using System.Collections.Generic;

namespace Poker.Voice
{
    /// <summary>
    /// 能量式語音偵測：20 ms 一格算 RMS，門檻＝max(StartRms, 背景噪音 × NoiseFactor)。
    /// 連續 StartSeconds 秒有聲才算開口（濾掉敲桌、碰撞聲），靜音 EndSilenceSeconds 秒或滿 MaxSeconds 秒結束；
    /// 開頭很短（有聲不到 ShortSpeechSeconds，例如只叫了名字）時結尾靜音放寬到 ShortSpeechEndSilenceSeconds（「Rocco…（想一下）…」）。
    /// 片段前面補 PreRollSeconds 秒（不吃掉第一個字）。CanStart = false 時不會開始新的片段（已在講的照常收完）。
    /// SpeechAudio：開口後（含前置）每一格音訊都即時送出，給串流用（/live 邊講邊送）。
    /// </summary>
    public sealed class EnergyVad
    {
        public float StartRms = 0.015f;
        public float NoiseFactor = 3f;
        public float StartSeconds = 0.1f;
        /// <summary>2026-10-13：0.6 → 0.4 s（講完到角色開口少等 0.2 s；句中 0.35 s 以內的停頓不會切斷）。</summary>
        public float EndSilenceSeconds = 0.4f;
        public float ShortSpeechSeconds = 0.6f;
        public float ShortSpeechEndSilenceSeconds = 0.7f;
        public float MaxSeconds = 8f;
        public float PreRollSeconds = 0.3f;
        /// <summary>結尾保留多少靜音（秒）。</summary>
        public float TailSeconds = 0.15f;
        public bool CanStart = true;

        public int SampleRate { get; }
        public bool IsSpeaking { get; private set; }
        public float NoiseFloor => _noise;

        public event Action SpeechStarted;
        public event Action<float[]> SegmentEnded;
        /// <summary>講話中的音訊（buffer 會被重複使用，只在回呼內有效）。</summary>
        public event Action<float[], int> SpeechAudio;

        readonly int _frame;
        readonly float[] _cur;
        int _curN;
        readonly LinkedList<float[]> _pre = new LinkedList<float[]>();
        readonly List<float> _seg = new List<float>();
        float _noise = 0.003f;
        int _voicedRun, _silentRun, _lastVoicedEnd, _voicedFrames;

        public EnergyVad(int sampleRate = 16000)
        {
            SampleRate = sampleRate;
            _frame = Math.Max(1, sampleRate / 50);
            _cur = new float[_frame];
        }

        public void Reset()
        {
            IsSpeaking = false;
            _seg.Clear();
            _pre.Clear();
            _curN = 0;
            _voicedRun = _silentRun = 0;
        }

        public void Feed(float[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _cur[_curN++] = buffer[i];
                if (_curN == _frame) { Frame(); _curN = 0; }
            }
        }

        float FrameSeconds => _frame / (float)SampleRate;

        void Frame()
        {
            double sum = 0;
            for (int i = 0; i < _frame; i++) sum += _cur[i] * _cur[i];
            float rms = (float)Math.Sqrt(sum / _frame);
            bool voiced = rms > Math.Max(StartRms, _noise * NoiseFactor);
            var copy = (float[])_cur.Clone();

            if (!IsSpeaking)
            {
                if (!voiced) _noise += (rms - _noise) * 0.05f; // 背景噪音慢慢追
                _pre.AddLast(copy);
                int keep = (int)Math.Ceiling((PreRollSeconds + StartSeconds) / FrameSeconds) + 1;
                while (_pre.Count > keep) _pre.RemoveFirst();
                _voicedRun = voiced ? _voicedRun + 1 : 0;
                if (CanStart && _voicedRun * FrameSeconds >= StartSeconds - 1e-4f) Begin();
                return;
            }

            _seg.AddRange(copy);
            SpeechAudio?.Invoke(copy, copy.Length);
            if (voiced) { _silentRun = 0; _lastVoicedEnd = _seg.Count; _voicedFrames++; }
            else _silentRun++;
            float endSilence = _voicedFrames * FrameSeconds < ShortSpeechSeconds ? ShortSpeechEndSilenceSeconds : EndSilenceSeconds;
            if (_silentRun * FrameSeconds >= endSilence - 1e-4f || _seg.Count >= MaxSeconds * SampleRate) End();
        }

        void Begin()
        {
            IsSpeaking = true;
            _seg.Clear();
            // 前置：開口偵測那幾格＋再往前 PreRollSeconds
            int want = (int)Math.Round((PreRollSeconds + _voicedRun * FrameSeconds) * SampleRate);
            var frames = new List<float[]>(_pre);
            int total = 0, start = frames.Count;
            while (start > 0 && total < want) { start--; total += frames[start].Length; }
            for (int i = start; i < frames.Count; i++) _seg.AddRange(frames[i]);
            _pre.Clear();
            _silentRun = 0;
            _voicedFrames = _voicedRun;
            _lastVoicedEnd = _seg.Count;
            SpeechStarted?.Invoke();
            if (SpeechAudio != null && _seg.Count > 0) { var pre = _seg.ToArray(); SpeechAudio(pre, pre.Length); }
        }

        void End()
        {
            IsSpeaking = false;
            int max = (int)(MaxSeconds * SampleRate);
            int len = Math.Min(max, Math.Min(_seg.Count, _lastVoicedEnd + (int)(TailSeconds * SampleRate)));
            var outSeg = _seg.GetRange(0, len).ToArray();
            _seg.Clear();
            _voicedRun = _silentRun = 0;
            SegmentEnded?.Invoke(outSeg);
        }
    }
}
