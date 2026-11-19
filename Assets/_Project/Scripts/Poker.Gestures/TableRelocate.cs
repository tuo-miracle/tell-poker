using System;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>換位置校準的結果（目前牌桌座標）：校準資料（同提示校準的算法）、雙手中心離目前手牌區的水平距離、推算的 yaw。</summary>
    public struct TableRelocation
    {
        public SurfaceCalibration Calibration;
        public float Distance;
        public float YawDegrees;
        public double Held;
    }

    /// <summary>雙手平放的位置算不算「另一個地方」。</summary>
    public enum RelocateSpot { TooClose, NewSpot, NoYaw, Implausible }

    /// <summary>
    /// 換位置校準（2026-10-15 實機回報：校準過的桌子轉向或換房間後就改不了）：桌子鎖定時不顯示提示，
    /// 玩家在「明顯不同的地方」把雙手平放（掌心朝下、兩手同高、靜止）HoldSeconds 秒，就回報新的桌面（算法同提示校準）。
    /// 「明顯不同」＝雙手中心離目前手牌區 &gt; MinDistance（而且不是放在目前的絨布上、同高度），
    /// 或兩掌連線與手指方向都轉了 &gt; MinYawDegrees（人轉了方向）。
    /// 在目前的牌桌上把手放著（手牌、籌碼旁、彩池前）不算：絨布上只能靠「轉身」觸發，而正常玩手指都朝前；
    /// 平放在大腿或扶手上（比桌面低、在身前）也不算。
    /// 觸發一次後要先抬手（不再平放靜止）才會再觸發。輸入是目前牌桌座標的雙手；head（牌桌座標，可省略）用來確認高度像桌面。
    /// </summary>
    public sealed class TableRelocateDetector
    {
        /// <summary>要停多久（比提示校準的 1 s 長：沒有提示，避免誤觸）。</summary>
        public const double HoldSeconds = 2.0;
        /// <summary>雙手中心離目前手牌區（手印的位置）超過這個水平距離才算換地方。</summary>
        public const float MinDistance = 0.30f;
        /// <summary>兩掌連線、手指方向與目前牌桌的 yaw 都差超過這個角度才算轉了方向。</summary>
        public const float MinYawDegrees = 25f;
        /// <summary>換地方的候選停到這麼久記一行 log（「still holding」）。</summary>
        public const double CandidateLogSeconds = 1.0;
        /// <summary>候選放開前至少停了這麼久才記「released」。</summary>
        const double ReleaseLogSeconds = 0.5;
        /// <summary>「too close」的 log 至少隔這麼久（手放在自己牌上很常見）。</summary>
        const double TooCloseLogInterval = 10.0;

        const float MaxPalmUpDot = -0.8f;       // 同 SurfaceCalibrator：掌心朝下
        const float MaxHeightDifference = 0.025f;
        const float StillTolerance = 0.012f;
        const float MinConfidence = 0.5f;
        const float MaxHandSpacing = 0.8f;
        /// <summary>桌面高度離目前牌桌這麼近、雙手中心又在絨布範圍內＝還是目前這張桌子。</summary>
        const float SameTableHeight = 0.04f, FeltNearMargin = 0.05f;
        /// <summary>桌面要低於眼睛 0.15–0.9 m（同錨點還原的高度檢查）。</summary>
        const float MinBelowHead = 0.15f, MaxBelowHead = 0.9f;
        const float MinFingerLength = 0.03f;
        /// <summary>
        /// 大腿／扶手：比目前桌面低、在玩家身前（桌緣後方到桌緣前 5 cm、左右 35 cm 內）。坐著等的時候雙手常平放在這裡，不是另一張桌子。
        /// </summary>
        const float LapHalfWidth = 0.35f, LapFront = 0.05f, LapBack = -0.7f;

        readonly TableLayout _layout;
        bool _dwelling, _needRelease, _candidateLogged, _closeLogged;
        double _startTime, _lastCloseLog = double.NegativeInfinity, _lastTime;
        Vector3 _startLeft, _startRight;
        double _sumY;
        Vector3 _sumCenter, _sumLeft, _sumRight, _sumFinger;
        int _samples;

        public Action<string> Log;

        public TableRelocateDetector(TableLayout layout) { _layout = layout ?? new TableLayout(); }

        /// <summary>結束目前的停留（桌子動了，座標換了）。剛觸發過的話仍要先抬手。</summary>
        public void Reset()
        {
            _dwelling = false;
            _candidateLogged = false;
            _closeLogged = false;
        }

        public TableRelocation? Step(HandsFrame frame, Vector3? head = null)
        {
            if (!Flat(frame, out var l, out var r)) { EndHold(); _needRelease = false; return null; }
            if (_dwelling && (Vector3.Distance(l, _startLeft) > StillTolerance || Vector3.Distance(r, _startRight) > StillTolerance))
            {
                EndHold();
                _needRelease = false;
            }
            if (!_dwelling)
            {
                _dwelling = true;
                _candidateLogged = _closeLogged = false;
                _startTime = frame.Time;
                _startLeft = l;
                _startRight = r;
                _sumY = 0;
                _sumCenter = _sumLeft = _sumRight = _sumFinger = Vector3.Zero;
                _samples = 0;
            }
            _lastTime = frame.Time;
            _sumY += (l.Y + r.Y) * 0.5;
            _sumCenter += (l + r) * 0.5f;
            _sumLeft += l;
            _sumRight += r;
            _sumFinger += (Finger(frame.Left) + Finger(frame.Right)) * 0.5f;
            _samples++;
            if (_needRelease) return null;

            double held = frame.Time - _startTime;
            if (held < CandidateLogSeconds) return null;
            var c = Current();
            var spot = Classify(c, head, _layout, out float dist, out float yaw, out string detail);
            if (spot == RelocateSpot.NewSpot)
            {
                if (held < HoldSeconds)
                {
                    if (!_candidateLogged) { _candidateLogged = true; Write($"[Table] relocate candidate held={held:F1}s (still holding) dist={dist:F2}m yaw={yaw:F0}°"); }
                    return null;
                }
                _needRelease = true;
                _dwelling = false;
                _candidateLogged = false;
                return new TableRelocation { Calibration = c, Distance = dist, YawDegrees = yaw, Held = held };
            }
            if (held >= HoldSeconds && !_closeLogged)
            {
                _closeLogged = true;
                if (frame.Time - _lastCloseLog >= TooCloseLogInterval)
                {
                    _lastCloseLog = frame.Time;
                    string why = spot == RelocateSpot.TooClose ? "too close" : spot == RelocateSpot.NoYaw ? "too close, no yaw" : "too close, not a desk";
                    Write($"[Table] relocate candidate held={held:F1}s ({why}) dist={dist:F2}m yaw={yaw:F0}° {detail}".TrimEnd());
                }
            }
            return null;
        }

        void EndHold()
        {
            if (_dwelling && _candidateLogged && !_needRelease && _lastTime - _startTime >= ReleaseLogSeconds)
                Write($"[Table] relocate candidate held={_lastTime - _startTime:F1}s (released)");
            Reset();
        }

        void Write(string m) => Log?.Invoke(m);

        SurfaceCalibration Current() => new SurfaceCalibration
        {
            SurfaceY = (float)(_sumY / _samples) - SurfaceCalibrator.PalmThickness,
            HandsCenter = _sumCenter / _samples,
            LeftPalm = _sumLeft / _samples,
            RightPalm = _sumRight / _samples,
            FingerDirection = _sumFinger / _samples,
        };

        /// <summary>
        /// 判斷平放的雙手是不是在另一個地方。distance：雙手中心到目前手牌區的水平距離；yawDegrees：推算的新牌桌 yaw（相對目前牌桌）。
        /// </summary>
        public static RelocateSpot Classify(SurfaceCalibration c, Vector3? head, TableLayout layout, out float distance, out float yawDegrees, out string detail)
        {
            layout = layout ?? new TableLayout();
            var area = layout.CardsCenter;
            distance = new Vector2(c.HandsCenter.X - area.X, c.HandsCenter.Z - area.Z).Length();
            yawDegrees = 0f;
            detail = "";

            if (head.HasValue)
            {
                float below = head.Value.Y - c.SurfaceY;
                if (below < MinBelowHead || below > MaxBelowHead) { detail = $"below eye={below:F2}m"; return RelocateSpot.Implausible; }
            }

            bool lap = c.SurfaceY < layout.TableY - SameTableHeight && Math.Abs(c.HandsCenter.X) <= LapHalfWidth
                && c.HandsCenter.Z <= LapFront && c.HandsCenter.Z >= LapBack;
            if (lap) { detail = "lap"; return RelocateSpot.TooClose; }

            var est = SurfaceCalibrator.EstimateYaw(c, head);
            if (!est.Apply) { detail = est.Reason; return RelocateSpot.NoYaw; }
            yawDegrees = est.YawDegrees;

            var right = c.RightPalm - c.LeftPalm;
            float lineYaw = Deg(Math.Atan2(-right.Z, right.X));   // forward = right × up = (−right.Z, right.X) → atan2(forward.X, forward.Z)
            var finger = new Vector2(c.FingerDirection.X, c.FingerDirection.Z);
            bool turned = finger.Length() >= MinFingerLength
                && Math.Abs(lineYaw) > MinYawDegrees
                && Math.Abs(Deg(Math.Atan2(finger.X, finger.Y))) > MinYawDegrees
                && Math.Abs(yawDegrees) > MinYawDegrees;

            float halfW = layout.FeltSize.X * 0.5f;
            bool onCurrentFelt = Math.Abs(c.SurfaceY - layout.TableY) <= SameTableHeight
                && Math.Abs(c.HandsCenter.X) <= halfW + FeltNearMargin
                && c.HandsCenter.Z >= -FeltNearMargin && c.HandsCenter.Z <= layout.FeltSize.Y + FeltNearMargin;
            bool moved = !onCurrentFelt && distance > MinDistance;
            if (onCurrentFelt && distance > MinDistance && !turned) detail = "on current felt";
            return moved || turned ? RelocateSpot.NewSpot : RelocateSpot.TooClose;
        }

        static float Deg(double rad) => (float)(rad * 180.0 / Math.PI);

        static Vector3 Finger(HandPose h)
        {
            var d = h.Position(HandJoint.MiddleTip) - h.Position(HandJoint.Wrist);
            return new Vector3(d.X, 0f, d.Z);
        }

        static bool Flat(HandsFrame f, out Vector3 l, out Vector3 r)
        {
            l = r = default;
            if (!Valid(f.Left) || !Valid(f.Right)) return false;
            l = f.Left.Position(HandJoint.Palm);
            r = f.Right.Position(HandJoint.Palm);
            if (Math.Abs(l.Y - r.Y) > MaxHeightDifference) return false;
            float spacing = new Vector2(r.X - l.X, r.Z - l.Z).Length();
            return spacing >= SurfaceCalibrator.MinHandSpacing && spacing <= MaxHandSpacing;
        }

        static bool Valid(HandPose h) => h.Tracked && h.Confidence >= MinConfidence && h.PalmNormal.Y <= MaxPalmUpDot;
    }
}
