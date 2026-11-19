using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 角色骨架的參照。座標：角色根節點在「角色面前的桌緣、桌面高度」，+Z 朝牌桌中央，+X 是角色的右手邊。
    /// </summary>
    public sealed class CharacterRig
    {
        public Transform Root, Hips, Chest, Neck, Head;
        public Renderer HeadRenderer;
        public Vector3 HipsRest, NeckRest, HeadRest;
        public Vector3 HeadRadii;

        // 臉
        public Transform EyeRootL, EyeRootR;
        /// <summary>眼球樞紐：forward 就是視線方向。</summary>
        public Transform EyeL, EyeR;
        public Transform PupilL, PupilR;
        public Transform LidL, LidR;
        public Renderer LidRendererL, LidRendererR;
        public Transform LashL, LashR;
        public Transform BrowL, BrowR;
        public Vector3 BrowRestL, BrowRestR;
        public Quaternion BrowRotL, BrowRotR;
        public Transform Mouth;
        public MeshFilter MouthLine;
        public Renderer MouthLineRenderer;
        public Transform MouthOpen;
        public MeshFilter MouthOpenFilter;
        public Renderer MouthOpenRenderer;
        public Transform CheekL, CheekR;
        public Vector3 CheekScale;
        public Transform[] SweatDrops;
        public Transform SymbolAnchor;
        /// <summary>頭頂符號（驚嘆號、問號、怒氣、汗滴）。</summary>
        public Transform Symbol;
        public MeshFilter SymbolFilter;
        public Renderer SymbolRenderer;

        // 手
        public Vector3 ShoulderL, ShoulderR; // Hips 空間
        public Transform UpperArmL, UpperArmR, ForeArmL, ForeArmR, HandL, HandR;
        public float UpperArmLength, ForeArmLength, ArmThickness;

        // 道具（角色自己的兩張蓋牌與籌碼，可關閉改用牌桌層的物件）
        public Transform Props, CardPivotL, CardPivotR, ChipStack, FidgetChip;
        public Vector3 ChipTop;

        // 字幕泡泡
        public Transform Bubble;
    }
}
