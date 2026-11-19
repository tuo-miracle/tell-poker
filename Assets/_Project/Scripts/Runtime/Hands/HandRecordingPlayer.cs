using Poker.Gestures;

namespace Poker.Runtime
{
    /// <summary>
    /// 依播放時間取樣 .hands 錄製檔（取「該時間或之前」最近的一幀，不內插）。
    /// 播放時間從 0 起算；循環時輸出的 Time 持續遞增（週期 = 長度 + 平均幀距）。
    /// </summary>
    public sealed class HandRecordingPlayer
    {
        readonly HandRecording _rec;
        readonly double _t0;
        readonly double _duration;
        readonly double _period;

        public bool Loop { get; }
        public double Duration => _duration;

        public HandRecordingPlayer(HandRecording recording, bool loop)
        {
            _rec = recording;
            Loop = loop;
            int n = recording.Frames.Count;
            if (n == 0) return;
            _t0 = recording.Frames[0].Time;
            _duration = recording.Frames[n - 1].Time - _t0;
            _period = n > 1 ? _duration + _duration / (n - 1) : 0.0;
        }

        public bool IsFinished(double playbackTime) => !Loop && playbackTime >= _duration;

        /// <summary>把 playbackTime 對應的幀複製到 dst；錄製檔是空的就把兩手設為未追蹤。</summary>
        public void Sample(double playbackTime, HandsFrame dst)
        {
            var frames = _rec.Frames;
            if (frames.Count == 0)
            {
                dst.Time = playbackTime;
                dst.Left.Tracked = false;
                dst.Right.Tracked = false;
                return;
            }

            double offset = 0.0;
            double local = playbackTime < 0 ? 0 : playbackTime;
            if (Loop && _period > 0.0)
            {
                double cycles = System.Math.Floor(local / _period);
                offset = cycles * _period;
                local -= offset;
            }

            int i = FindFrame(_t0 + local);
            HandsFrameUtil.Copy(frames[i], dst);
            dst.Time = frames[i].Time - _t0 + offset;
        }

        /// <summary>最後一個 Time ≤ t 的幀（二分搜尋）。</summary>
        int FindFrame(double t)
        {
            var frames = _rec.Frames;
            int lo = 0, hi = frames.Count - 1;
            if (t <= frames[0].Time) return 0;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (frames[mid].Time <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}
