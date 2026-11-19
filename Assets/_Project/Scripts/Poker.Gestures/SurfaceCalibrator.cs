using System;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>校準結果（目前牌桌座標）：真實桌面高度、雙手中心，以及推算牌桌朝向用的掌心位置與手指方向（都是停留期間的平均）。</summary>
    public struct SurfaceCalibration
    {
        public float SurfaceY;
        public Vector3 HandsCenter;
        public Vector3 LeftPalm, RightPalm;
        /// <summary>兩手「手腕→中指指尖」水平向量的平均（未正規化；所有關節疊在一起的假資料會是零向量）。</summary>
        public Vector3 FingerDirection;
    }

    /// <summary>由雙手推算的牌桌 yaw（相對目前牌桌，繞 +Y，正值＝+Z 往 +X 轉）。Apply 為 false 時保留原本朝向，Reason 說明原因。</summary>
    public struct TableYawEstimate
    {
        public bool Apply;
        public float YawDegrees;
        public string Reason;
    }

    /// <summary>
    /// 「雙手平放在桌上」校準真實桌面：雙手被追蹤、掌心朝下、兩手同高、在玩家前方、穩定不動 DwellSeconds，
    /// 就回報桌面高度（手掌關節高度減掉手掌厚度）。手真的碰到桌面，所以不需要房間掃描，也不怕桌上有雜物。
    /// 輸入是目前牌桌座標下的雙手資料；成功回報一次後停止，要再校準就呼叫 Reset。
    /// </summary>
    public sealed class SurfaceCalibrator
    {
        public const float DwellSeconds = 1.0f;
        /// <summary>Palm 關節到手掌下緣（貼桌面）的距離。</summary>
        public const float PalmThickness = 0.012f;
        const float MaxPalmUpDot = -0.8f;       // PalmNormal.Y 要小於這個值（掌心朝下）
        const float MaxHeightDifference = 0.025f;
        const float StillTolerance = 0.012f;
        const float MinConfidence = 0.5f;
        const float MaxSideOffset = 0.6f, MinForward = -0.05f, MaxForward = 0.65f;

        bool _done, _dwelling;
        double _startTime;
        Vector3 _startLeft, _startRight;
        double _sumY;
        Vector3 _sumCenter, _sumLeft, _sumRight, _sumFinger;
        int _samples;

        public bool IsCalibrated => _done;
        /// <summary>目前穩定停留的進度 0–1（給畫面上的手印發光用）。</summary>
        public float Progress { get; private set; }

        public void Reset()
        {
            _done = false;
            _dwelling = false;
            Progress = 0f;
        }

        public SurfaceCalibration? Update(HandsFrame frame)
        {
            if (_done) return null;
            if (!Valid(frame.Left) || !Valid(frame.Right))
            {
                _dwelling = false;
                Progress = 0f;
                return null;
            }

            Vector3 l = frame.Left.Position(HandJoint.Palm), r = frame.Right.Position(HandJoint.Palm);
            if (Math.Abs(l.Y - r.Y) > MaxHeightDifference)
            {
                _dwelling = false;
                Progress = 0f;
                return null;
            }

            if (!_dwelling || Vector3.Distance(l, _startLeft) > StillTolerance || Vector3.Distance(r, _startRight) > StillTolerance)
            {
                _dwelling = true;
                _startTime = frame.Time;
                _startLeft = l;
                _startRight = r;
                _sumY = 0;
                _sumCenter = Vector3.Zero;
                _sumLeft = _sumRight = _sumFinger = Vector3.Zero;
                _samples = 0;
            }

            _sumY += (l.Y + r.Y) * 0.5;
            _sumCenter += (l + r) * 0.5f;
            _sumLeft += l;
            _sumRight += r;
            _sumFinger += (Finger(frame.Left) + Finger(frame.Right)) * 0.5f;
            _samples++;
            Progress = (float)Math.Min(1.0, (frame.Time - _startTime) / DwellSeconds);
            if (frame.Time - _startTime < DwellSeconds) return null;

            _done = true;
            return new SurfaceCalibration
            {
                SurfaceY = (float)(_sumY / _samples) - PalmThickness,
                HandsCenter = _sumCenter / _samples,
                LeftPalm = _sumLeft / _samples,
                RightPalm = _sumRight / _samples,
                FingerDirection = _sumFinger / _samples,
            };
        }

        static Vector3 Finger(HandPose h)
        {
            var d = h.Position(HandJoint.MiddleTip) - h.Position(HandJoint.Wrist);
            return new Vector3(d.X, 0f, d.Z);
        }

        /// <summary>兩掌距離小於這個值時連線方向不可靠，不改朝向。</summary>
        public const float MinHandSpacing = 0.15f;
        /// <summary>兩掌連線推出的前方與手指方向相差超過這個角度，就不改朝向。</summary>
        public const float MaxYawDisagreementDegrees = 35f;
        /// <summary>前方與「頭→雙手」水平方向夾角超過 120° 才算指向頭（只擋明顯翻轉；頭在側邊不算）。</summary>
        const float MaxTowardHeadCos = -0.5f;
        const float MinHeadDistance = 0.05f;
        /// <summary>平均手指向量短於這個值視為沒有手指資料，只用兩掌連線。</summary>
        const float MinFingerLength = 0.03f;
        /// <summary>混合時兩掌連線的權重（手指方向權重 1）：手指會自然外張，連線比較可靠。</summary>
        const float PalmLineWeight = 2f;

        /// <summary>
        /// 由校準結果推算牌桌 yaw：右方＝左掌→右掌的水平方向，前方＝右方 × 上方（遠離玩家）。
        /// 有手指資料時與手指方向比對，一致就加權混合，不一致就不改。head（目前牌桌座標，可省略）用來確認前方不是指向玩家。
        /// </summary>
        public static TableYawEstimate EstimateYaw(SurfaceCalibration c, Vector3? head = null)
        {
            var right = c.RightPalm - c.LeftPalm;
            right.Y = 0f;
            float spacing = right.Length();
            if (spacing < MinHandSpacing)
                return Keep($"hands too close ({spacing * 100f:F1} cm < {MinHandSpacing * 100f:F0} cm)");
            right /= spacing;
            var forward = new Vector3(-right.Z, 0f, right.X);   // right × up

            var finger = new Vector3(c.FingerDirection.X, 0f, c.FingerDirection.Z);
            bool useFingers = finger.Length() >= MinFingerLength;
            if (useFingers)
            {
                finger = Vector3.Normalize(finger);
                float diff = AngleDegrees(forward, finger);
                if (diff > MaxYawDisagreementDegrees)
                    return Keep($"hand line and fingers disagree by {diff:F0} deg");
                forward = Vector3.Normalize(forward * PalmLineWeight + finger);
            }

            if (head.HasValue)
            {
                var center = (c.LeftPalm + c.RightPalm) * 0.5f;
                var away = new Vector3(center.X - head.Value.X, 0f, center.Z - head.Value.Z);
                float d = away.Length();
                if (d > MinHeadDistance && Vector3.Dot(forward, away / d) < MaxTowardHeadCos)
                    return Keep("forward would point toward the head");
            }

            return new TableYawEstimate
            {
                Apply = true,
                YawDegrees = (float)(Math.Atan2(forward.X, forward.Z) * 180.0 / Math.PI),
                Reason = useFingers ? "hand line + fingers" : "hand line only",
            };
        }

        static TableYawEstimate Keep(string reason) => new TableYawEstimate { Apply = false, Reason = reason };

        static float AngleDegrees(Vector3 a, Vector3 b)
        {
            double cross = a.Z * b.X - a.X * b.Z, dot = a.X * b.X + a.Z * b.Z;
            return (float)Math.Abs(Math.Atan2(cross, dot) * 180.0 / Math.PI);
        }

        static bool Valid(HandPose h)
        {
            if (!h.Tracked || h.Confidence < MinConfidence) return false;
            if (h.PalmNormal.Y > MaxPalmUpDot) return false;
            var p = h.Position(HandJoint.Palm);
            return Math.Abs(p.X) < MaxSideOffset && p.Z > MinForward && p.Z < MaxForward;
        }
    }
}
