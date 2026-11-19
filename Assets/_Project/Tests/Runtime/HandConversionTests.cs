using System;
using System.Linq;
using NUnit.Framework;
using Oculus.Interaction.Input;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    public class HandConversionTests
    {
        [Test]
        public void JointMap_Covers26Joints_OneToOne()
        {
            var all = (HandJoint[])Enum.GetValues(typeof(HandJoint));
            Assert.That(all.Length, Is.EqualTo(HandPose.JointCount));
            var mapped = all.Select(HandJointMap.ToIsdk).ToArray();
            Assert.That(mapped.Distinct().Count(), Is.EqualTo(26), "不能有兩個關節對到同一個 ISDK 關節");
            Assert.That(mapped.All(id => id != HandJointId.Invalid && (int)id < (int)HandJointId.HandEnd), Is.True);
            foreach (var j in all) Assert.That(HandJointMap.FromIsdk(HandJointMap.ToIsdk(j)), Is.EqualTo(j));
        }

        [TestCase(HandJoint.Palm, HandJointId.HandPalm)]
        [TestCase(HandJoint.Wrist, HandJointId.HandWristRoot)]
        [TestCase(HandJoint.ThumbMetacarpal, HandJointId.HandThumb1)]
        [TestCase(HandJoint.ThumbTip, HandJointId.HandThumbTip)]
        [TestCase(HandJoint.IndexMetacarpal, HandJointId.HandIndex0)]
        [TestCase(HandJoint.IndexTip, HandJointId.HandIndexTip)]
        [TestCase(HandJoint.MiddleIntermediate, HandJointId.HandMiddle2)]
        [TestCase(HandJoint.RingDistal, HandJointId.HandRing3)]
        [TestCase(HandJoint.LittleMetacarpal, HandJointId.HandPinky0)]
        [TestCase(HandJoint.LittleTip, HandJointId.HandPinkyTip)]
        public void JointMap_MatchesByName(HandJoint ours, HandJointId isdk)
        {
            Assert.That(HandJointMap.ToIsdk(ours), Is.EqualTo(isdk));
        }

        static Pose[] Flat(Quaternion worldRot, Vector3 origin)
        {
            var poses = new Pose[HandPose.JointCount];
            for (int i = 0; i < poses.Length; i++) poses[i] = new Pose(origin + worldRot * new Vector3(0, 0, 0.01f * i), worldRot);
            return poses;
        }

        // ISDK OpenXR 慣例（OpenXRHandPrimitives.cs:77,107）：Palmar = 關節旋轉 × down，左右手相同
        [Test]
        public void PalmDownOnRotatedTable_GivesPalmNormalMinusY()
        {
            var tableRot = Quaternion.Euler(0f, 50f, 0f);
            var tableOrigin = new Pose(new Vector3(0.2f, 0.72f, 0.9f), tableRot);
            // 手指沿牌桌 +Z、掌心朝下 → 世界旋轉 = 牌桌旋轉
            var pose = new HandPose();
            HandPoseConverter.FromWorld(Flat(tableRot, tableOrigin.position + Vector3.up * 0.03f), tableOrigin, pose);

            var n = pose.PalmNormal;
            Assert.That(n.Y, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(pose.Position(HandJoint.Palm).Y, Is.EqualTo(0.03f).Within(1e-5f));
            Assert.That(pose.Position(HandJoint.Wrist).Z, Is.EqualTo(0.01f).Within(1e-5f), "位置轉成牌桌座標");
        }

        [Test]
        public void PalmUp_GivesPalmNormalPlusY()
        {
            var tableOrigin = new Pose(Vector3.zero, Quaternion.Euler(0f, -30f, 0f));
            var world = tableOrigin.rotation * Quaternion.AngleAxis(180f, Vector3.forward);   // 繞手指方向翻面
            var pose = new HandPose();
            HandPoseConverter.FromWorld(Flat(world, Vector3.zero), tableOrigin, pose);
            Assert.That(pose.PalmNormal.Y, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void PalmNormal_MatchesUnityConventionVector()
        {
            // 任意姿勢：HandPose.PalmNormal 必須等於 (世界旋轉 × down) 轉到牌桌座標
            var tableOrigin = new Pose(new Vector3(1, 0.7f, 2), Quaternion.Euler(0f, 110f, 0f));
            var world = Quaternion.Euler(33f, -71f, 12f);
            var pose = new HandPose();
            HandPoseConverter.FromWorld(Flat(world, Vector3.one), tableOrigin, pose);
            var expected = Quaternion.Inverse(tableOrigin.rotation) * (world * Vector3.down);
            Assert.That(Vector3.Distance(pose.PalmNormal.ToUnity(), expected), Is.LessThan(1e-4f));
        }
    }
}
