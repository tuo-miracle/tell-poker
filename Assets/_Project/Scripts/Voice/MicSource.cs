using UnityEngine;

namespace Poker.Voice
{
    /// <summary>麥克風來源（實機 UnityEngine.Microphone；測試用假的）。</summary>
    public interface IMicSource
    {
        int SampleRate { get; }
        bool Start();
        void Stop();
        /// <summary>讀出上次之後新錄到的樣本（mono），回傳樣本數。</summary>
        int Read(float[] buffer);
    }

    /// <summary>UnityEngine.Microphone：預設裝置、循環 2 秒緩衝；裝置不支援 16 kHz 時用它支援的取樣率（VoiceSession 再重取樣）。</summary>
    public sealed class UnityMicSource : IMicSource
    {
        const int LoopSeconds = 2;
        AudioClip _clip;
        string _device;
        int _readPos;
        float[] _tmp = new float[0];

        public int SampleRate { get; private set; } = 16000;
        public bool IsRunning => _clip != null && Microphone.IsRecording(_device);

        public bool Start()
        {
            if (IsRunning) return true;
            if (Microphone.devices.Length == 0) { Debug.LogWarning("[Voice] no microphone device"); return false; }
            _device = null; // 預設裝置
            Microphone.GetDeviceCaps(_device, out int min, out int max);
            SampleRate = min == 0 && max == 0 ? 16000 : Mathf.Clamp(16000, min, max);
            _clip = Microphone.Start(_device, true, LoopSeconds, SampleRate);
            _readPos = 0;
            if (_clip == null) { Debug.LogWarning("[Voice] Microphone.Start failed"); return false; }
            Debug.Log($"[Voice] microphone started at {SampleRate} Hz (caps {min}-{max})");
            return true;
        }

        public void Stop()
        {
            if (_clip != null) Microphone.End(_device);
            _clip = null;
        }

        public int Read(float[] buffer)
        {
            if (_clip == null) return 0;
            int pos = Microphone.GetPosition(_device);
            int total = _clip.samples;
            int avail = (pos - _readPos + total) % total;
            int n = Mathf.Min(avail, buffer.Length);
            if (n <= 0) return 0;
            if (_tmp.Length < n) _tmp = new float[n];
            // 跨過緩衝尾端時分兩段讀
            int first = Mathf.Min(n, total - _readPos);
            ReadInto(_readPos, first, buffer, 0);
            if (n > first) ReadInto(0, n - first, buffer, first);
            _readPos = (_readPos + n) % total;
            return n;
        }

        void ReadInto(int offset, int count, float[] dst, int at)
        {
            var part = count == _tmp.Length ? _tmp : new float[count];
            _clip.GetData(part, offset);
            System.Array.Copy(part, 0, dst, at, count);
        }
    }
}

namespace Poker.Voice
{
    /// <summary>沒有聲音的麥克風（批次模式、測試、建置時用，不去碰真的 Microphone）。</summary>
    public sealed class SilentMic : IMicSource
    {
        public int SampleRate => 16000;
        public bool Start() => true;
        public void Stop() { }
        public int Read(float[] buffer) => 0;
    }
}
