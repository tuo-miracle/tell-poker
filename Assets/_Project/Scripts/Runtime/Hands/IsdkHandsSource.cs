using System;
using Oculus.Interaction.Input;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 實機手部來源：每幀從 ISDK 的左右 IHand 讀 26 個 OpenXR 關節世界姿勢，轉成牌桌座標 HandsFrame。
    /// 信心值讀 OVRHand：ISDK rig 裡的 LastKnownGoodHand 會在低信心時偽造 IsHighConfidence（LastKnownGoodHand.cs:30-56）。
    /// 沒有 XR 時 IHand 不會連線（IsConnected = false），兩手輸出 Tracked = false，不丟例外。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class IsdkHandsSource : MonoBehaviour, IHandsFrameSource
    {
        [SerializeField] TableFrame table;
        [Tooltip("左手 IHand（ISDK rig 中 OVRHands 物件上的原始 Hand，不是 poke 限制過的合成手）")]
        [SerializeField] MonoBehaviour leftHand;
        [SerializeField] MonoBehaviour rightHand;
        [Tooltip("可選：提供真實追蹤信心的 OVRHand")]
        [SerializeField] OVRHand leftOvrHand;
        [SerializeField] OVRHand rightOvrHand;
        [Tooltip("OVRHand 回報 Low 信心時寫入的 Confidence 值")]
        [SerializeField] float lowConfidenceValue = 0.3f;

        readonly HandsFrame _current = new HandsFrame();
        readonly Pose[] _world = new Pose[HandPose.JointCount];
        IHand _left, _right;
        bool _loggedFirst;

        public HandsFrame Current => _current;
        public event Action<HandsFrame> FrameUpdated;

        public void Configure(TableFrame tableFrame, IHand left, IHand right, OVRHand leftOvr = null, OVRHand rightOvr = null)
        {
            table = tableFrame;
            _left = left;
            _right = right;
            leftOvrHand = leftOvr;
            rightOvrHand = rightOvr;
        }

        void Awake()
        {
            _left ??= leftHand as IHand;
            _right ??= rightHand as IHand;
            if (_left == null || _right == null) Debug.LogWarning("[IsdkHandsSource] left/right IHand not assigned; hands will stay untracked");
        }

        // LateUpdate：OVRCameraRig 在 Update 更新錨點並觸發 ISDK 資料更新，這時讀到的是本幀的資料
        void LateUpdate()
        {
            if (table == null) return;
            var origin = table.WorldPose;
            Read(_left, leftOvrHand, origin, _current.Left);
            Read(_right, rightOvrHand, origin, _current.Right);
            _current.Time = Time.unscaledTimeAsDouble;
            FrameUpdated?.Invoke(_current);
        }

        void Read(IHand hand, OVRHand ovr, Pose origin, HandPose dst)
        {
            dst.Tracked = false;
            if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid) return;
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                if (!hand.GetJointPose(HandJointMap.ToIsdk((HandJoint)j), out _world[j])) return;
            }
            HandPoseConverter.FromWorld(_world, origin, dst);
            dst.Tracked = true;
            dst.IndexPinch = hand.GetFingerPinchStrength(HandFinger.Index);
            // runtime 的「正在捏合」布林值：IHand.GetFingerIsPinching（Hand.cs:139-142）← FromOVRHandDataSource.cs:240-241
            // ← OVRHand.GetFingerIsPinching 讀原生 Pinches 位元（OVRHand.cs:342-345）。強度在 Quest 3 實測封頂 0.70，這個值給辨識器取「或」。
            dst.IndexPinching = hand.GetFingerIsPinching(HandFinger.Index);
            if (ovr != null) dst.Confidence = ovr.IsTracked && ovr.HandConfidence == OVRHand.TrackingConfidence.High ? 1f : lowConfidenceValue;
            else dst.Confidence = hand.IsHighConfidence ? 1f : lowConfidenceValue;

            if (!_loggedFirst)
            {
                _loggedFirst = true;
                // 實機自我檢查：Palm 與 Wrist 朝向應接近（OpenXR 預設骨架兩者同為 identity，OpenXRHandPrimitives.cs:720-721）
                float angle = Quaternion.Angle(_world[(int)HandJoint.Palm].rotation, _world[(int)HandJoint.Wrist].rotation);
                Debug.Log($"[IsdkHandsSource] first tracked {hand.Handedness}: palm-wrist angle={angle:F1}deg palmNormal(table)={dst.PalmNormal}");
            }
        }
    }
}
