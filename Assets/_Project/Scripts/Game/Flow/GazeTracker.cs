using System;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>注視目標來源（實機為 ISDK 注視元件）。回傳 false 表示目前不可用，GazeTracker 改用呼叫端傳入的頭部射線命中。</summary>
    public interface IGazeTargetSource
    {
        /// <summary>target：目前注視的角色索引，沒有則 -1。</summary>
        bool TryGetTarget(out int target);
    }

    /// <summary>
    /// 注視判定：注視目標連續命中同一個目標 DwellSeconds 秒就算「盯著」，離開 ReleaseSeconds 秒才算移開。
    /// 目標來自 ISDK 注視元件（Source／DefaultSource，見 Poker.UX.IsdkGazeRig）；不可用時（Editor 沒有 XR）退回呼叫端的頭部射線。
    /// GazeStarted 在開始盯的那一刻觸發一次（同一段注視不會重複）。
    /// </summary>
    public sealed class GazeTracker
    {
        /// <summary>所有 GazeTracker 預設使用的注視來源（Poker.UX 在有 XR 時掛上 ISDK 注視）。null＝只用頭部射線。</summary>
        public static IGazeTargetSource DefaultSource { get; set; }
        /// <summary>這個實例專用的注視來源；null 時用 DefaultSource。</summary>
        public IGazeTargetSource Source { get; set; }

        public float DwellSeconds = 1f;
        public float ReleaseSeconds = 0.35f;
        public int Current { get; private set; } = -1;
        public float DwellTime => _dwell;
        /// <summary>目前命中、還在累積注視時間的目標（-1＝沒有）；DwellTime 是它的累積秒數。</summary>
        public int Candidate => _candidate;
        public event Action<int> GazeStarted;
        public event Action<int> GazeEnded;

        int _candidate = -1;
        float _dwell, _off;

        /// <summary>
        /// 每幀呼叫。hit 是呼叫端算出的頭部射線命中（-1＝沒有）；若注視來源（ISDK）可用，改用它的結果。
        /// </summary>
        public void Update(float dt, int hit)
        {
            var src = Source ?? DefaultSource;
            if (src != null && src.TryGetTarget(out int gazed)) hit = gazed;

            if (hit >= 0 && hit == _candidate) _dwell += dt;
            else if (hit >= 0) { _candidate = hit; _dwell = dt; }
            else { _candidate = -1; _dwell = 0f; }

            if (Current >= 0)
            {
                _off = hit == Current ? 0f : _off + dt;
                if (_off >= ReleaseSeconds)
                {
                    int was = Current;
                    Current = -1;
                    _off = 0f;
                    GazeEnded?.Invoke(was);
                }
            }
            if (Current < 0 && _candidate >= 0 && _dwell >= DwellSeconds)
            {
                Current = _candidate;
                _off = 0f;
                GazeStarted?.Invoke(Current);
            }
        }

        /// <summary>射線（方向需正規化）是否穿過球體，回傳沿射線的距離。</summary>
        public static bool RayHitsSphere(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float distance)
        {
            var oc = center - origin;
            distance = Vector3.Dot(oc, dir);
            if (distance < 0f) return false;
            return (oc - dir * distance).sqrMagnitude <= radius * radius;
        }
    }
}
