using Oculus.Interaction.Input;
using Poker.Gestures;

namespace Poker.Runtime
{
    /// <summary>
    /// Poker.Gestures.HandJoint ↔ ISDK HandJointId。ISDK 207 定義了 ISDK_OPENXR_HAND，
    /// HandJointId 的數值就是 OpenXR XrHandJointEXT 順序（OpenXRHandPrimitives.cs:218-381），與 HandJoint 一一對應。
    /// 這裡仍逐一按名稱列出，萬一 SDK 改了數值，測試會抓到。
    /// </summary>
    public static class HandJointMap
    {
        static readonly HandJointId[] ToIsdkTable =
        {
            HandJointId.HandPalm, HandJointId.HandWristRoot,
            HandJointId.HandThumb1, HandJointId.HandThumb2, HandJointId.HandThumb3, HandJointId.HandThumbTip,
            HandJointId.HandIndex0, HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndex3, HandJointId.HandIndexTip,
            HandJointId.HandMiddle0, HandJointId.HandMiddle1, HandJointId.HandMiddle2, HandJointId.HandMiddle3, HandJointId.HandMiddleTip,
            HandJointId.HandRing0, HandJointId.HandRing1, HandJointId.HandRing2, HandJointId.HandRing3, HandJointId.HandRingTip,
            HandJointId.HandPinky0, HandJointId.HandPinky1, HandJointId.HandPinky2, HandJointId.HandPinky3, HandJointId.HandPinkyTip,
        };

        static readonly HandJoint[] FromIsdkTable = BuildInverse();

        public static HandJointId ToIsdk(HandJoint joint) => ToIsdkTable[(int)joint];

        public static HandJoint FromIsdk(HandJointId id) => FromIsdkTable[(int)id];

        static HandJoint[] BuildInverse()
        {
            var inv = new HandJoint[(int)HandJointId.HandEnd];
            for (int i = 0; i < ToIsdkTable.Length; i++) inv[(int)ToIsdkTable[i]] = (HandJoint)i;
            return inv;
        }
    }
}
