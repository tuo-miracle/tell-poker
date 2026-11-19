using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>OpenXR 手部關節順序（XR_HAND_JOINT_*），26 個。</summary>
    public enum HandJoint
    {
        Palm, Wrist,
        ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleMetacarpal, MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingMetacarpal, RingProximal, RingIntermediate, RingDistal, RingTip,
        LittleMetacarpal, LittleProximal, LittleIntermediate, LittleDistal, LittleTip,
    }

    public enum Hand { Left, Right }

    /// <summary>
    /// 一隻手在某一刻的姿勢，座標一律是「牌桌座標」：原點在玩家側桌緣中央、+Y 向上（桌面法線）、+Z 指向牌桌中央、單位公尺。
    /// 用牌桌座標，手勢邏輯就與房間、頭盔位置無關，錄製的資料也能在任何牌桌上回放。
    /// </summary>
    public sealed class HandPose
    {
        public const int JointCount = 26;

        public bool Tracked;
        /// <summary>追蹤信心 0–1（低信心時手勢判斷應該保守）。</summary>
        public float Confidence = 1f;
        /// <summary>食指與拇指捏合強度 0–1（平台提供）。</summary>
        public float IndexPinch;
        /// <summary>平台的「正在捏合」布林值（OVRHand.GetFingerIsPinching，經 ISDK IHand.GetFingerIsPinching）。
        /// 第 1 版錄製沒有這個欄位，讀進來一律 false；辨識時與強度門檻取「或」。</summary>
        public bool IndexPinching;
        public readonly Vector3[] Positions = new Vector3[JointCount];
        public readonly Quaternion[] Rotations = new Quaternion[JointCount];

        public Vector3 Position(HandJoint j) => Positions[(int)j];
        public Quaternion Rotation(HandJoint j) => Rotations[(int)j];

        /// <summary>掌心法線（手掌朝外的方向）。OpenXR 慣例：Palm 關節的 −Y 指向掌心外側。</summary>
        public Vector3 PalmNormal => Vector3.Transform(-Vector3.UnitY, Rotations[(int)HandJoint.Palm]);

        public HandPose Clone()
        {
            var c = new HandPose { Tracked = Tracked, Confidence = Confidence, IndexPinch = IndexPinch, IndexPinching = IndexPinching };
            System.Array.Copy(Positions, c.Positions, JointCount);
            System.Array.Copy(Rotations, c.Rotations, JointCount);
            return c;
        }
    }

    /// <summary>某一刻的雙手資料。Time 是秒（單調遞增）。</summary>
    public sealed class HandsFrame
    {
        public double Time;
        public HandPose Left = new HandPose();
        public HandPose Right = new HandPose();
        public HandPose Get(Hand h) => h == Hand.Left ? Left : Right;
    }
}
