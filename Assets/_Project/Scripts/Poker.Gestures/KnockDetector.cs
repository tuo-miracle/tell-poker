using System;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>一個敲擊頻道的參數（手掌敲桌、手指敲桌各一組）。</summary>
    internal struct KnockParams
    {
        /// <summary>往下速度超過這個值開始一次下敲。</summary>
        public float MinPeak;
        /// <summary>兩下至少一下（間隔 ≤ QuickGap）或兩下都要（間隔較長）達到的峰值速度。</summary>
        public float StrongPeak;
        public float QuickGap;
        /// <summary>已有清楚的第一下時，第二下的起始速度（可低於 MinPeak；等於 MinPeak＝不放寬）。放寬時第二下也不再套 StrongPeak 規則。</summary>
        public float SecondMinPeak;
        /// <summary>放寬的那一組（第二下只到 SecondMinPeak）兩下間隔上限。</summary>
        public float SoftPairMaxGap;
        /// <summary>第一下落差至少這麼大才算「清楚的第一下」，第二下才可放寬。</summary>
        public float SoftFirstMinDrop;
        /// <summary>放寬的那一組，第二下的落差下限（一般的 MinDrop 之外）。</summary>
        public float SoftSecondMinDrop;
        public float EndSpeed, MaxHeight, MinDrop, MinRise, MinGap, Window, MaxSpread;
    }

    /// <summary>
    /// 敲兩下的狀態機（單一訊號：高度、往下速度）。純計算、不配置記憶體。
    /// 一次下敲＝速度超過 MinPeak 到降回 EndSpeed；結束時夠低、落差夠大才算一下。
    /// 兩下：間隔在 [MinGap, Window]、中間有回升 MinRise、水平位置相差 ≤ MaxSpread、峰值符合 StrongPeak 規則。
    /// </summary>
    internal sealed class KnockDetector
    {
        bool _stroke, _hasFirst, _rose;
        float _peak, _low, _firstPeak, _firstDrop, _maxRise, _gapMaxDown;
        double _peakTime, _first;
        Vector3 _firstPos;

        public bool HasFirst => _hasFirst;

        // ── 診斷：最近一次下敲結束時的數值與判定（字串都是常數，不配置記憶體）──
        public bool StrokeEnded;
        /// <summary>LastRise＝規則用的量：第一下之後從最低點最多回升了多少（第二下開始前）。</summary>
        public float LastPeak, LastHeight, LastDrop, LastGap, LastRise, LastSpread;
        public string LastResult = "";
        /// <summary>這一幀第一下等不到第二下就逾時（診斷）：GapMaxDown＝等待期間沒達到起始速度的最大往下速度，LastRise 同上。</summary>
        public bool FirstExpired;
        public float GapMaxDown;

        public void Reset()
        {
            _stroke = false;
            _hasFirst = false;
        }

        /// <param name="down">往下速度（m/s，正值往下）。</param>
        /// <param name="y">訊號目前的高度（公尺，用來量回升）。</param>
        /// <param name="height">結束時判斷「夠低」用的離桌高度。</param>
        /// <param name="drop">撞擊前一小段時間的落差（時間窗內最高點 − 目前）。</param>
        /// <param name="pos">判斷兩下是否敲在同一點的位置。</param>
        /// <param name="canStart">這一幀能不能開始一次下敲（例如手指敲擊要求手掌貼桌不動）。</param>
        /// <returns>這一幀完成了一組敲兩下。</returns>
        public bool Update(double time, float down, float y, float height, float drop, Vector3 pos, bool canStart, in KnockParams k)
        {
            StrokeEnded = false;
            FirstExpired = false;
            // 逾時只在沒有下敲進行中時判斷；進行中的那一下在結束時用它的峰值時間比
            if (_hasFirst && !_stroke && time - _first > k.Window)
            {
                _hasFirst = false;
                FirstExpired = true;
                GapMaxDown = _gapMaxDown;
                LastRise = _maxRise;
            }
            if (_hasFirst && !_stroke)
            {
                // 第一下之後要真的抬起來（連續往下放的手不會回升）
                _low = Math.Min(_low, y);
                _maxRise = Math.Max(_maxRise, y - _low);
                if (y - _low >= k.MinRise) _rose = true;
                if (!(canStart && down >= StartSpeed(k))) _gapMaxDown = Math.Max(_gapMaxDown, down);
            }

            if (!_stroke)
            {
                if (canStart && down >= StartSpeed(k))
                {
                    _stroke = true;
                    _peak = down;
                    _peakTime = time;
                }
                return false;
            }
            if (down > _peak)
            {
                _peak = down;
                _peakTime = time;
            }
            if (down > k.EndSpeed) return false;

            // 一次下敲結束
            _stroke = false;
            StrokeEnded = true;
            LastPeak = _peak; LastHeight = height; LastDrop = drop;
            LastGap = _hasFirst ? (float)(_peakTime - _first) : 0f;
            LastRise = _hasFirst ? _maxRise : 0f;
            LastSpread = _hasFirst ? HorizDist(pos, _firstPos) : 0f;
            if (height > k.MaxHeight) { LastResult = "too-high"; return false; }
            if (drop < k.MinDrop) { LastResult = "small-drop"; return false; }
            double gap = _peakTime - _first;
            if (_hasFirst)
            {
                LastResult = !_rose ? "no-rise" : gap < k.MinGap ? "gap-short" : gap > k.Window ? "gap-long" :
                    !StrongEnough(gap, k) ? "weak" : HorizDist(pos, _firstPos) > k.MaxSpread ? "spread" : null;
                if (LastResult == null)
                {
                    LastResult = "PAIR";
                    _hasFirst = false;
                    return true;
                }
            }
            else LastResult = "first";
            _hasFirst = true;
            _first = _peakTime;
            _firstPeak = _peak;
            _firstDrop = drop;
            _firstPos = pos;
            _rose = false;
            _low = y;
            _maxRise = 0f;
            _gapMaxDown = 0f;
            return false;
        }

        /// <summary>開始一次下敲的速度：第一下成立後、等第二下時用 SecondMinPeak。</summary>
        float StartSpeed(in KnockParams k) => SoftSecondAllowed(k) ? Math.Min(k.MinPeak, k.SecondMinPeak) : k.MinPeak;

        // 第一下夠清楚，而且之後手已經抬起來過（MinRise）：剛敲完時手掌的抖動不會被當成第二下
        bool SoftSecondAllowed(in KnockParams k) => _hasFirst && _rose && k.SecondMinPeak < k.MinPeak && _firstDrop >= k.SoftFirstMinDrop;

        // 快節奏（≤ QuickGap）時一下夠快就好：真人第二下常比較輕；慢節奏時兩下都要夠快。
        // 第二下放寬（SecondMinPeak < MinPeak）時：第一下已達 MinPeak、第二下達 SecondMinPeak 就夠
        // （2026-10-13 實機 3599.065：0.36／0.39 m/s、間隔 0.49 s 是真的敲兩下）
        bool StrongEnough(double gap, in KnockParams k) =>
            (gap <= k.QuickGap ? Math.Max(_firstPeak, _peak) : Math.Min(_firstPeak, _peak)) >= k.StrongPeak ||
            (SoftSecondAllowed(k) && gap <= k.SoftPairMaxGap && _peak >= k.SecondMinPeak && LastDrop >= k.SoftSecondMinDrop);

        static float HorizDist(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
