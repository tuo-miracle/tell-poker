using System;

namespace Poker.Voice
{
    /// <summary>
    /// 串流播放的環形緩衝：網路執行緒（主執行緒）寫入 PCM16 位元組，音訊執行緒（PCMReaderCallback）讀出 float。
    /// 讀不到資料時補靜音；Complete 之後全部讀完就算 Drained。滿了就丟掉新來的（記在 Dropped）。執行緒安全。
    /// </summary>
    public sealed class PcmStream
    {
        readonly object _lock = new object();
        readonly float[] _ring;
        int _head, _count;
        int _carry = -1;
        bool _complete;

        public int SampleRate { get; }
        public long Written { get; private set; }
        public long Played { get; private set; }
        public int Dropped { get; private set; }

        public PcmStream(int sampleRate, float capacitySeconds = 30f)
        {
            SampleRate = sampleRate;
            _ring = new float[Math.Max(1, (int)Math.Round(sampleRate * (double)capacitySeconds))];
        }

        public int Buffered { get { lock (_lock) return _count; } }
        public bool IsComplete { get { lock (_lock) return _complete; } }
        public bool Drained { get { lock (_lock) return _complete && _count == 0; } }
        /// <summary>目前寫入的總長度（秒）。</summary>
        public float DurationSeconds { get { lock (_lock) return Written / (float)SampleRate; } }

        public void Write(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                int i = offset, end = offset + count;
                if (_carry >= 0 && i < end) { Push((short)(_carry | data[i] << 8)); _carry = -1; i++; }
                for (; i + 1 < end; i += 2) Push((short)(data[i] | data[i + 1] << 8));
                if (i < end) _carry = data[i];
            }
        }

        void Push(short v)
        {
            if (_count == _ring.Length) { Dropped++; return; }
            _ring[(_head + _count) % _ring.Length] = v / 32768f;
            _count++;
            Written++;
        }

        public void Complete() { lock (_lock) _complete = true; }

        /// <summary>讀出最多 count 個樣本到 dst；不夠的部分補 0。回傳真正讀到的樣本數。</summary>
        public int Read(float[] dst, int offset, int count)
        {
            lock (_lock)
            {
                int n = Math.Min(count, _count);
                for (int i = 0; i < n; i++)
                {
                    dst[offset + i] = _ring[_head];
                    _head = (_head + 1) % _ring.Length;
                }
                _count -= n;
                Played += n;
                for (int i = n; i < count; i++) dst[offset + i] = 0f;
                return n;
            }
        }
    }
}
