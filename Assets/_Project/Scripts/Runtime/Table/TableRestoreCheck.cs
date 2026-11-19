using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 錨點放回的牌桌合不合理（2026-10-15 實機：轉了方向或換了房間，校準過的桌子留在原地改不了）。
    /// 要在玩家面前（頭的水平朝向與「頭→玩家側桌緣」夾角 ≤ 60°）、搆得到（桌緣水平距離 ≤ 1.5 m）、
    /// 高度像桌子（桌面低於眼睛 0.15–0.9 m）。不合理就這次不用：TablePlacer 改放到面前並顯示雙手提示。
    /// 桌緣幾乎在頭正下方時方向不可靠，改用桌面中心的方向（也擋掉「桌面往玩家背後延伸」的反向桌）。
    /// </summary>
    public static class TableRestoreCheck
    {
        public const float MaxAngleDegrees = 60f;
        public const float MaxEdgeDistance = 1.5f;
        public const float MaxBelowEye = 0.9f, MinBelowEye = 0.15f;
        /// <summary>桌緣離頭的水平距離小於這個值時，方向改用桌面中心。</summary>
        const float NearEdge = 0.2f;

        /// <summary>合理回傳 null；不合理回傳原因（給 log）。</summary>
        public static string Reject(Pose tableOrigin, Vector3 headPosition, Vector3 headForward)
        {
            var toEdge = Vector3.ProjectOnPlane(tableOrigin.position - headPosition, Vector3.up);
            float dist = toEdge.magnitude;
            if (dist > MaxEdgeDistance) return $"too far, dist={dist:F2}m";

            var dir = toEdge;
            if (dist < NearEdge)
            {
                var center = tableOrigin.position + tableOrigin.rotation * (Vector3.forward * TableDimensions.Depth * 0.5f);
                dir = Vector3.ProjectOnPlane(center - headPosition, Vector3.up);
            }
            if (dir.sqrMagnitude > 1e-6f)
            {
                float angle = Vector3.Angle(TablePlacement.FlatForward(headForward), dir);
                if (angle > MaxAngleDegrees) return $"not in front, angle={angle:F0}°";
            }

            float below = headPosition.y - tableOrigin.position.y;
            if (below > MaxBelowEye) return $"too low, below eye={below:F2}m";
            if (below < MinBelowEye) return $"too high, below eye={below:F2}m";
            return null;
        }
    }
}
