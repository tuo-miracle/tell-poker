using UnityEngine;

namespace Poker.UX
{
    /// <summary>
    /// 選單面板放在玩家正前方：沿頭部前方、略低於視線，面板邊角都落在水平 ±25°、垂直 ±20° 內
    /// （比賽的 FoV-aware 要求；Meta VR Glasses 視野較窄）。面板滾轉跟著頭，保證四角都在範圍內。
    /// </summary>
    public static class MenuPlacement
    {
        public const float MaxYaw = 25f, MaxPitch = 20f;
        public const float Distance = 0.45f;
        /// <summary>面板中心低於視線的角度。</summary>
        public const float DropDegrees = 4f;
        public static readonly Vector2 PanelSize = new Vector2(0.28f, 0.22f);

        public static Pose InFront(Vector3 headPos, Quaternion headRot)
        {
            var dir = headRot * Quaternion.Euler(DropDegrees, 0f, 0f) * Vector3.forward;
            var pos = headPos + dir * Distance;
            var rot = Quaternion.LookRotation(pos - headPos, headRot * Vector3.up);
            return new Pose(pos, rot);
        }

        /// <summary>選單示範手的中心：視線下方這個角度、離頭這麼遠（面板下緣約 −18°，示範手就在面板正下方、伸手可及）。</summary>
        public const float DemoDropDegrees = 30f, DemoDistance = 0.42f;

        /// <summary>
        /// 選單「Gestures」頁示範手的擺放：片段的中心（clipCenter，牌桌座標）落在面板正下方；
        /// 旋轉沿用牌桌（推牌、丟籌碼的方向仍是「往牌桌中央」）。回傳示範手根節點的世界姿勢。
        /// </summary>
        public static Pose GhostDemo(Vector3 headPos, Quaternion headRot, Quaternion tableRot, Vector3 clipCenter)
        {
            var dir = headRot * Quaternion.Euler(DemoDropDegrees, 0f, 0f) * Vector3.forward;
            var target = headPos + dir * DemoDistance;
            return new Pose(target - tableRot * clipCenter, tableRot);
        }

        /// <summary>點相對頭部視線的（水平, 垂直）角度，右、上為正。</summary>
        public static Vector2 AnglesFromHead(Vector3 headPos, Quaternion headRot, Vector3 point)
        {
            var local = Quaternion.Inverse(headRot) * (point - headPos);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }
    }
}
