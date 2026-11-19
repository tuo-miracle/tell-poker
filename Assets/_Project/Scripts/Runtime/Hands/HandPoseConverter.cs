using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 世界座標關節姿勢 → 牌桌座標 HandPose。
    /// 朝向慣例不需要修正：ISDK OpenXR 手在 Unity 世界中，掌心法線 = 關節旋轉 × down（−Y），左右手相同
    /// （OpenXRHandPrimitives.cs:77,107；TransformFeatureValueProvider.cs:128-139），與 HandPose.PalmNormal 的假設一致。
    /// 轉到牌桌座標只是左乘牌桌旋轉的反矩陣，軸慣例保留。
    /// </summary>
    public static class HandPoseConverter
    {
        /// <summary>worldJoints 以 HandJoint 為索引（26 個）。只寫位置與旋轉，不動 Tracked／Confidence／Pinch。</summary>
        public static void FromWorld(Pose[] worldJoints, Pose tableOrigin, HandPose dst)
        {
            var inv = Quaternion.Inverse(tableOrigin.rotation);
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                var w = worldJoints[j];
                dst.Positions[j] = (inv * (w.position - tableOrigin.position)).ToNumerics();
                dst.Rotations[j] = (inv * w.rotation).ToNumerics();
            }
        }
    }
}
