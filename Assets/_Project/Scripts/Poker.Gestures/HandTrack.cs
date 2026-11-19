using System;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>
    /// 單手的特徵層：保留最近約 0.9 秒的樣本，算平滑位置、最小平方法速度、掌心角度、捏合遲滯。
    /// 追蹤不可靠（Tracked=false 或信心低）的幀不寫入，並清空歷史，避免回來時算出假的大速度。
    /// </summary>
    internal sealed class HandTrack
    {
        const int Cap = 64;
        readonly double[] _t = new double[Cap];
        readonly Vector3[] _palm = new Vector3[Cap];
        readonly Vector3[] _wrist = new Vector3[Cap];
        readonly Vector3[] _pinch = new Vector3[Cap];
        readonly Vector3[] _mid = new Vector3[Cap];
        // 抬得較高的指尖（食指、中指取 Y 較大者）：手指敲桌用
        readonly Vector3[] _tip = new Vector3[Cap];
        readonly float[] _palmSmoothY = new float[Cap], _palmSmoothZ = new float[Cap], _tipSmoothY = new float[Cap];
        int _head = -1, _count;

        /// <summary>這一幀可靠。</summary>
        public bool Ok;
        /// <summary>最近一次變成不可靠的時間。</summary>
        public double LostAt = double.NegativeInfinity;
        /// <summary>平滑後的手掌、手腕、捏合點（拇指尖與食指尖中點）。</summary>
        public Vector3 Palm, Wrist, PinchPoint;
        /// <summary>平滑後的中指指尖（咪牌、翻牌時手指在牌上而手掌在牌前）。</summary>
        public Vector3 MiddleTip;
        /// <summary>平滑後「抬得較高的指尖」（食指或中指）高度，與它的速度（手指敲桌用）。</summary>
        public float TipY;
        public Vector3 TipVel;
        /// <summary>平滑後食指尖與中指尖較低者的高度（咪牌時指尖要壓在牌上）。</summary>
        public float LowTipY;
        /// <summary>這一幀原始的捏合點（放開時算落點用，不要平滑造成的延遲）。</summary>
        public Vector3 PinchPointRaw;
        /// <summary>最後一次可靠時的位置（追蹤遺失時判斷手在哪裡）。</summary>
        public Vector3 LastSeenPalm, LastSeenPinch;
        /// <summary>手掌速度。</summary>
        public Vector3 Vel;
        /// <summary>這一幀手掌關節的旋轉（牌桌座標；拿著牌時牌跟著手轉）。</summary>
        public Quaternion PalmRotation = Quaternion.Identity;
        /// <summary>掌心法線與 +Y 的夾角（度）：180 = 掌心朝下、0 = 朝上。</summary>
        public float PalmAngle;
        public bool Pinching, PinchStarted, PinchEnded;
        /// <summary>這一幀拇指尖與食指尖的距離（公尺）。</summary>
        public float ThumbIndexGap;
        /// <summary>這一幀平台給的捏合強度與布林值（診斷用）。</summary>
        public float PinchStrength;
        public bool PinchFlag;
        /// <summary>最近一次開始捏合的時間與捏合點（診斷：捏合是不是在籌碼外就開始了）。</summary>
        public double PinchStartedAt = double.NegativeInfinity;

        public void Update(HandPose p, double t, GestureTuning k)
        {
            PinchStarted = PinchEnded = false;
            bool ok = p.Tracked && p.Confidence >= k.MinConfidence;
            if (!ok)
            {
                if (Ok) LostAt = t;
                Ok = false;
                _count = 0;
                return;
            }
            Ok = true;

            _head = (_head + 1) % Cap;
            if (_count < Cap) _count++;
            _t[_head] = t;
            _palm[_head] = p.Position(HandJoint.Palm);
            _wrist[_head] = p.Position(HandJoint.Wrist);
            PinchPointRaw = (p.Position(HandJoint.ThumbTip) + p.Position(HandJoint.IndexTip)) * 0.5f;
            _pinch[_head] = PinchPointRaw;
            var it = p.Position(HandJoint.IndexTip);
            var mt = p.Position(HandJoint.MiddleTip);
            _mid[_head] = mt;
            _tip[_head] = it.Y >= mt.Y ? it : mt;

            Palm = Mean(_palm, t, k.HeightSmoothWindow);
            _palmSmoothY[_head] = Palm.Y;
            _palmSmoothZ[_head] = Palm.Z;
            Wrist = Mean(_wrist, t, k.HeightSmoothWindow);
            PinchPoint = Mean(_pinch, t, k.HeightSmoothWindow);
            MiddleTip = Mean(_mid, t, k.HeightSmoothWindow);
            TipY = Mean(_tip, t, k.HeightSmoothWindow).Y;
            _tipSmoothY[_head] = TipY;
            TipVel = Slope(_tip, t, k.VelocityWindow);
            LowTipY = Math.Min(it.Y, mt.Y);
            LastSeenPalm = Palm;
            LastSeenPinch = PinchPoint;
            Vel = Slope(_palm, t, k.VelocityWindow);

            PalmRotation = p.Rotation(HandJoint.Palm);
            var n = p.PalmNormal;
            float len = n.Length();
            float cos = len > 1e-6f ? Math.Max(-1f, Math.Min(1f, n.Y / len)) : 0f;
            PalmAngle = (float)(Math.Acos(cos) * 180.0 / Math.PI);

            // 捏合＝runtime 布林值（第 2 版錄製起才有）或強度門檻或拇指食指碰在一起；三者都放開才算放開
            // 輕捏（強度 ≥ PinchAssistStrength 且指尖距 ≤ PinchAssistGap）也算開始：Quest 輕捏常停在強度 0.4–0.55、指尖距 2 cm
            ThumbIndexGap = Vector3.Distance(p.Position(HandJoint.ThumbTip), p.Position(HandJoint.IndexTip));
            PinchStrength = p.IndexPinch;
            PinchFlag = p.IndexPinching;
            if (!Pinching && (p.IndexPinching || p.IndexPinch >= k.PinchStart || ThumbIndexGap <= k.PinchTouchGap ||
                              (p.IndexPinch >= k.PinchAssistStrength && ThumbIndexGap <= k.PinchAssistGap)))
            {
                Pinching = true;
                PinchStarted = true;
                PinchStartedAt = t;
            }
            else if (Pinching && !p.IndexPinching && p.IndexPinch < k.PinchRelease && ThumbIndexGap > k.PinchReleaseGap) { Pinching = false; PinchEnded = true; }
        }

        public Vector3 PinchVelocity(double t, float window) => Slope(_pinch, t, window);

        /// <summary>時間窗內「平滑後」手掌高度的最大值（用原始值取最大會被雜訊系統性墊高）。</summary>
        public float MaxPalmY(double t, float window) => MaxOf(_palmSmoothY, t, window);

        /// <summary>時間窗內平滑後指尖高度（抬得較高者）的最大值。</summary>
        public float MaxTipY(double t, float window) => MaxOf(_tipSmoothY, t, window);

        /// <summary>時間窗內平滑後手掌 z 的最小值（往前推了多遠：現在的 z 減掉它）。</summary>
        public float MinPalmZ(double t, float window) => -MaxOf(_palmSmoothZ, t, window, -1f);

        float MaxOf(float[] buf, double t, float window, float sign = 1f)
        {
            float m = float.NegativeInfinity;
            for (int i = 0, idx = _head; i < _count; i++, idx = (idx - 1 + Cap) % Cap)
            {
                if (i > 0 && t - _t[idx] > window + 1e-6) break;
                m = Math.Max(m, sign * buf[idx]);
            }
            return m;
        }

        Vector3 Mean(Vector3[] buf, double t, float window)
        {
            Vector3 sum = Vector3.Zero;
            int n = 0;
            for (int i = 0, idx = _head; i < _count; i++, idx = (idx - 1 + Cap) % Cap)
            {
                if (i > 0 && t - _t[idx] > window + 1e-6) break;
                sum += buf[idx];
                n++;
            }
            return sum / n;
        }

        /// <summary>時間窗內樣本的最小平方法斜率（速度）。樣本不足兩個回傳 0。</summary>
        Vector3 Slope(Vector3[] buf, double t, float window)
        {
            int n = 0;
            double st = 0;
            Vector3 sp = Vector3.Zero;
            for (int i = 0, idx = _head; i < _count; i++, idx = (idx - 1 + Cap) % Cap)
            {
                double dt = _t[idx] - t;
                if (-dt > window + 1e-6) break;
                st += dt;
                sp += buf[idx];
                n++;
            }
            if (n < 2) return Vector3.Zero;
            float mt = (float)(st / n);
            Vector3 mp = sp / n;
            float stt = 0;
            Vector3 stp = Vector3.Zero;
            for (int i = 0, idx = _head; i < n; i++, idx = (idx - 1 + Cap) % Cap)
            {
                float d = (float)(_t[idx] - t) - mt;
                stt += d * d;
                stp += d * (buf[idx] - mp);
            }
            return stt > 1e-12f ? stp / stt : Vector3.Zero;
        }
    }
}
