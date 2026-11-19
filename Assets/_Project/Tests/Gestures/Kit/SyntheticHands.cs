using System;
using System.Collections.Generic;
using System.Numerics;
using Poker.Gestures;

namespace Poker.Tests
{
    public enum Ease { Linear, Smooth, In, Out }

    /// <summary>一段移動的目標；沒給的欄位維持原值。Roll 180 = 掌心朝上；Pitch 正值 = 手腕抬高（手指朝下）。</summary>
    public sealed class HandTarget
    {
        public Vector3? Pos;
        public float? PinchValue, RollDeg, PitchDeg, FingerLiftValue, CurlValue;

        public static HandTarget At(float x, float y, float z) => new HandTarget { Pos = new Vector3(x, y, z) };
        public static HandTarget At(Vector3 p) => new HandTarget { Pos = p };
        public static HandTarget Pinch(float v) => new HandTarget { PinchValue = v };
        public static HandTarget Roll(float deg) => new HandTarget { RollDeg = deg };
        public static HandTarget Pitch(float deg) => new HandTarget { PitchDeg = deg };
        /// <summary>食指與中指往上抬（0 = 平放、1 = 指尖抬高 8 cm），模擬用手指敲桌。</summary>
        public static HandTarget FingerLift(float v) => new HandTarget { FingerLiftValue = v };
        /// <summary>四指彎向掌心（0 = 張開、1 = 握拳：指尖離手掌約 3 cm）；拇指不動。</summary>
        public static HandTarget Curl(float v) => new HandTarget { CurlValue = v };

        public HandTarget WithPinch(float v) { PinchValue = v; return this; }
        public HandTarget WithRoll(float deg) { RollDeg = deg; return this; }
        public HandTarget WithPitch(float deg) { PitchDeg = deg; return this; }
        public HandTarget WithCurl(float v) { CurlValue = v; return this; }
    }

    /// <summary>
    /// 合成手部軌跡（測試用）。以手掌（Palm 關節）位置＋翻滾／俯仰＋捏合強度產生 26 個關節。
    /// 預設 72 Hz、雙手被追蹤、手掌朝下平放在桌上（不在牌、籌碼堆上）。
    /// 雜訊模型（WithNoise）：每隻手整體位移＝慢漂移（Gauss–Markov，相關時間 0.25 s）＋每幀白抖動，
    /// 合計每軸標準差約 sigma；旋轉同理（度）；捏合強度加白雜訊。理由：研究的 1 cm 是「位置誤差」，
    /// 不是每幀獨立的抖動，真實的手不會每幀跳 1 cm。純白雜訊壓力測試用 WithWhiteNoise。
    /// </summary>
    public sealed class SyntheticHands
    {
        public static readonly Vector3 RightRest = new Vector3(0.42f, 0.025f, 0.0f);
        public static readonly Vector3 LeftRest = new Vector3(-0.28f, 0.025f, 0.0f);
        /// <summary>Quest 3 實機的捏合強度上限（92 段錄製從未超過 0.70）。腳本的 Pinch(1) 預設就對到這個值，合成手才不會比真機好捏。</summary>
        public const float PinchStrengthCap = 0.70f;

        sealed class State
        {
            public Vector3 Pos;
            public float Pinch, Roll, Pitch, Lift, Curl;
            public bool Tracked = true;
            public float Confidence = 1f;
            // 雜訊狀態
            public Vector3 Drift, RotDrift;
        }

        readonly double _dt;
        readonly Random _rng;
        readonly State[] _hands = { new State { Pos = LeftRest }, new State { Pos = RightRest } };
        double _time, _nextFrame;
        float _driftSigma, _jitterSigma, _rotDriftSigma, _rotJitterSigma, _pinchSigma;
        float _pinchScale = PinchStrengthCap;
        const float DriftTau = 0.25f;

        public readonly List<HandsFrame> Frames = new List<HandsFrame>();
        public double Time => _time;

        public SyntheticHands(double rate = 72, int seed = 0)
        {
            _dt = 1.0 / rate;
            _rng = new Random(seed);
        }

        /// <summary>約 sigma（每軸）的位置誤差：90% 變異量給慢漂移、其餘給白抖動。</summary>
        public SyntheticHands WithNoise(float sigma = 0.01f, float rotSigmaDeg = 4f, float pinchSigma = 0.05f)
        {
            _driftSigma = sigma * 0.95f; _jitterSigma = sigma * 0.31f;
            _rotDriftSigma = rotSigmaDeg * 0.95f; _rotJitterSigma = rotSigmaDeg * 0.31f;
            _pinchSigma = pinchSigma;
            return this;
        }

        /// <summary>改變捏合強度上限（預設已是 Quest 3 的 0.70；傳 1 可模擬沒有封頂的平台）。</summary>
        public SyntheticHands WithPinchCap(float cap = PinchStrengthCap)
        {
            _pinchScale = cap;
            return this;
        }

        /// <summary>每幀獨立的白雜訊（壓力測試；比真實追蹤嚴苛很多）。</summary>
        public SyntheticHands WithWhiteNoise(float sigma = 0.01f, float rotSigmaDeg = 4f, float pinchSigma = 0.05f)
        {
            _driftSigma = 0; _jitterSigma = sigma;
            _rotDriftSigma = 0; _rotJitterSigma = rotSigmaDeg;
            _pinchSigma = pinchSigma;
            return this;
        }

        public SyntheticHands Set(Hand h, Vector3 palm, float pinch = 0f, float roll = 0f, float pitch = 0f)
        {
            var s = _hands[(int)h];
            s.Pos = palm; s.Pinch = pinch; s.Roll = roll; s.Pitch = pitch;
            return this;
        }

        /// <summary>這隻手目前的手掌位置（不含雜訊）。</summary>
        public Vector3 Palm(Hand h) => _hands[(int)h].Pos;

        public SyntheticHands Track(Hand h, bool tracked, float confidence = 1f)
        {
            _hands[(int)h].Tracked = tracked;
            _hands[(int)h].Confidence = confidence;
            return this;
        }

        public SyntheticHands Wait(double seconds) => Move(seconds, null, null);

        public SyntheticHands Move(Hand h, double seconds, HandTarget target, Ease ease = Ease.Smooth) =>
            h == Hand.Right ? Move(seconds, target, null, ease) : Move(seconds, null, target, ease);

        public SyntheticHands Move(double seconds, HandTarget right, HandTarget left, Ease ease = Ease.Smooth)
        {
            var from = new[] { Snapshot(_hands[0]), Snapshot(_hands[1]) };
            var to = new[] { Apply(from[0], left), Apply(from[1], right) };
            double start = _time, end = _time + seconds;
            while (_nextFrame <= end + 1e-9)
            {
                double u = seconds <= 0 ? 1 : Math.Min(1, Math.Max(0, (_nextFrame - start) / seconds));
                float e = Shape(u, ease);
                for (int i = 0; i < 2; i++) Lerp(_hands[i], from[i], to[i], e);
                Frames.Add(Emit(_nextFrame));
                _nextFrame = Math.Round((_nextFrame + _dt) / _dt) * _dt;
            }
            for (int i = 0; i < 2; i++) Lerp(_hands[i], from[i], to[i], 1f);
            _time = end;
            return this;
        }

        /// <summary>沿多個點移動（每段等時）。</summary>
        public SyntheticHands Path(Hand h, double seconds, Ease ease, params Vector3[] points)
        {
            foreach (var p in points) Move(h, seconds / points.Length, HandTarget.At(p), ease);
            return this;
        }

        static float Shape(double u, Ease ease)
        {
            switch (ease)
            {
                case Ease.Linear: return (float)u;
                case Ease.In: return (float)(u * u);
                case Ease.Out: return (float)(1 - (1 - u) * (1 - u));
                default: return (float)(u * u * (3 - 2 * u));
            }
        }

        static State Snapshot(State s) => new State { Pos = s.Pos, Pinch = s.Pinch, Roll = s.Roll, Pitch = s.Pitch, Lift = s.Lift, Curl = s.Curl };

        static State Apply(State s, HandTarget t)
        {
            var r = Snapshot(s);
            if (t == null) return r;
            if (t.Pos.HasValue) r.Pos = t.Pos.Value;
            if (t.PinchValue.HasValue) r.Pinch = t.PinchValue.Value;
            if (t.RollDeg.HasValue) r.Roll = t.RollDeg.Value;
            if (t.PitchDeg.HasValue) r.Pitch = t.PitchDeg.Value;
            if (t.FingerLiftValue.HasValue) r.Lift = t.FingerLiftValue.Value;
            if (t.CurlValue.HasValue) r.Curl = t.CurlValue.Value;
            return r;
        }

        static void Lerp(State dst, State a, State b, float e)
        {
            dst.Pos = Vector3.Lerp(a.Pos, b.Pos, e);
            dst.Pinch = a.Pinch + (b.Pinch - a.Pinch) * e;
            dst.Roll = a.Roll + (b.Roll - a.Roll) * e;
            dst.Pitch = a.Pitch + (b.Pitch - a.Pitch) * e;
            dst.Lift = a.Lift + (b.Lift - a.Lift) * e;
            dst.Curl = a.Curl + (b.Curl - a.Curl) * e;
        }

        HandsFrame Emit(double t)
        {
            var f = new HandsFrame { Time = t };
            Fill(f.Left, _hands[0], Hand.Left);
            Fill(f.Right, _hands[1], Hand.Right);
            return f;
        }

        float Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }

        Vector3 Gauss3() => new Vector3(Gauss(), Gauss(), Gauss());

        void StepNoise(State s)
        {
            // 離散 Gauss–Markov：x' = a x + sqrt(1-a²) σ n，穩態標準差 σ
            float a = (float)Math.Exp(-_dt / DriftTau), b = (float)Math.Sqrt(1 - a * a);
            s.Drift = a * s.Drift + b * _driftSigma * Gauss3();
            s.RotDrift = a * s.RotDrift + b * _rotDriftSigma * Gauss3();
        }

        void Fill(HandPose pose, State s, Hand h)
        {
            StepNoise(s);
            pose.Tracked = s.Tracked;
            pose.Confidence = s.Confidence;
            pose.IndexPinch = Clamp01(s.Pinch + (_pinchSigma > 0 ? _pinchSigma * Gauss() : 0)) * _pinchScale;
            Vector3 offset = s.Drift + (_jitterSigma > 0 ? _jitterSigma * Gauss3() : Vector3.Zero);
            Vector3 rotNoise = s.RotDrift + (_rotJitterSigma > 0 ? _rotJitterSigma * Gauss3() : Vector3.Zero);
            var q = Rotation(s.Roll + rotNoise.Z, s.Pitch + rotNoise.X, rotNoise.Y);
            WriteSkeleton(pose, h, s.Pos + offset, q, s.Pinch, s.Lift, s.Curl);
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        const float Deg = (float)(Math.PI / 180.0);

        static Quaternion Rotation(float rollDeg, float pitchDeg, float yawDeg) =>
            Quaternion.CreateFromYawPitchRoll(yawDeg * Deg, pitchDeg * Deg, rollDeg * Deg);

        // 右手、掌心朝下時的關節位置（手掌座標：+X 右、+Y 手背、+Z 指尖方向，公尺）。左手鏡射 X。
        static readonly Vector3[] Open = new Vector3[HandPose.JointCount];
        static readonly Vector3[] Pinched = new Vector3[HandPose.JointCount];
        // 手指抬起時各關節往手背方向（+Y）的位移（公尺／單位抬起量）：只有食指與中指
        static readonly float[] LiftY = new float[HandPose.JointCount];
        // 握拳時四指各關節的位置（只有 Intermediate／Distal／Tip 有值；其他關節 HasFist=false 不動）
        static readonly Vector3[] Fist = new Vector3[HandPose.JointCount];
        static readonly bool[] HasFist = new bool[HandPose.JointCount];

        static SyntheticHands()
        {
            void J(HandJoint j, Vector3 open, Vector3? pinched = null)
            {
                Open[(int)j] = open;
                Pinched[(int)j] = pinched ?? open;
            }
            var pinchPoint = new Vector3(-0.03f, -0.02f, 0.06f);
            J(HandJoint.Palm, Vector3.Zero);
            J(HandJoint.Wrist, new Vector3(0, 0.01f, -0.075f));
            J(HandJoint.ThumbMetacarpal, new Vector3(-0.025f, -0.005f, -0.045f));
            J(HandJoint.ThumbProximal, new Vector3(-0.045f, -0.01f, -0.02f), new Vector3(-0.04f, -0.012f, -0.01f));
            J(HandJoint.ThumbDistal, new Vector3(-0.055f, -0.012f, 0.005f), new Vector3(-0.038f, -0.016f, 0.025f));
            J(HandJoint.ThumbTip, new Vector3(-0.06f, -0.012f, 0.03f), pinchPoint);
            float[] fx = { -0.022f, 0f, 0.02f, 0.038f };
            float[] tipZ = { 0.095f, 0.10f, 0.095f, 0.08f };
            var bases = new[] { HandJoint.IndexMetacarpal, HandJoint.MiddleMetacarpal, HandJoint.RingMetacarpal, HandJoint.LittleMetacarpal };
            for (int f = 0; f < 4; f++)
            {
                int b = (int)bases[f];
                float x = fx[f], tz = tipZ[f];
                J((HandJoint)b, new Vector3(x, 0, -0.05f));
                J((HandJoint)(b + 1), new Vector3(x, 0, 0.035f));
                // 捏合時食指彎向拇指；其他手指也微彎
                var curl = f == 0 ? pinchPoint : new Vector3(x, -0.025f, 0.06f);
                J((HandJoint)(b + 2), new Vector3(x, -0.002f, 0.035f + (tz - 0.035f) * 0.4f), Vector3.Lerp(new Vector3(x, -0.005f, 0.05f), curl, 0.3f));
                J((HandJoint)(b + 3), new Vector3(x, -0.004f, 0.035f + (tz - 0.035f) * 0.75f), Vector3.Lerp(new Vector3(x, -0.01f, 0.06f), curl, 0.7f));
                J((HandJoint)(b + 4), new Vector3(x, -0.006f, tz), curl);
                Fist[b + 2] = new Vector3(x, -0.025f, 0.05f); HasFist[b + 2] = true;
                Fist[b + 3] = new Vector3(x, -0.035f, 0.03f); HasFist[b + 3] = true;
                Fist[b + 4] = new Vector3(x, -0.025f, 0.012f); HasFist[b + 4] = true;
                if (f < 2)
                {
                    LiftY[b + 2] = 0.03f;
                    LiftY[b + 3] = 0.055f;
                    LiftY[b + 4] = 0.08f;
                }
            }
        }

        static Vector3 Local(HandJoint j, Hand h, float pinch, float lift = 0f, float curl = 0f)
        {
            var v = Vector3.Lerp(Open[(int)j], Pinched[(int)j], Clamp01(pinch));
            if (HasFist[(int)j] && curl > 0f) v = Vector3.Lerp(v, Fist[(int)j], Clamp01(curl));
            v.Y += LiftY[(int)j] * Clamp01(lift);
            if (h == Hand.Left) v.X = -v.X;
            return v;
        }

        static void WriteSkeleton(HandPose pose, Hand h, Vector3 palm, Quaternion q, float pinch, float lift, float curl)
        {
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                pose.Positions[j] = palm + Vector3.Transform(Local((HandJoint)j, h, pinch, lift, curl), q);
                pose.Rotations[j] = q;
            }
        }

        /// <summary>手掌朝下、不俯仰、完全捏合時，從手掌到捏合點（拇指尖與食指尖中點）的位移。</summary>
        public static Vector3 PinchOffset(Hand h) => (Local(HandJoint.ThumbTip, h, 1f) + Local(HandJoint.IndexTip, h, 1f)) * 0.5f;

        /// <summary>手掌朝下、不俯仰、張開時，從手掌到某個關節的位移（例如食指尖：用來把指尖放到按鈕上）。</summary>
        public static Vector3 JointOffset(Hand h, HandJoint j) => Local(j, h, 0f);

        /// <summary>要讓捏合點落在 pinchPoint，手掌該放哪裡（掌心朝下）。</summary>
        public static Vector3 PalmForPinch(Hand h, Vector3 pinchPoint) => pinchPoint - PinchOffset(h);
    }
}
