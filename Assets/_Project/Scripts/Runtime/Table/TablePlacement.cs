using System.Collections.Generic;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>世界座標中的矩形桌面：中心、法線、兩個邊方向（單位向量）與半長。</summary>
    public readonly struct TableRect
    {
        public readonly Vector3 Center;
        public readonly Vector3 Normal;
        public readonly Vector3 AxisA;
        public readonly Vector3 AxisB;
        public readonly Vector2 HalfExtents;   // x 沿 AxisA，y 沿 AxisB

        public TableRect(Vector3 center, Vector3 normal, Vector3 axisA, Vector3 axisB, Vector2 halfExtents)
        {
            Center = center;
            Normal = normal;
            AxisA = axisA;
            AxisB = axisB;
            HalfExtents = halfExtents;
        }
    }

    /// <summary>牌桌放置的純計算（不碰 SDK，方便測試）。回傳的 Pose 是牌桌原點（玩家側桌緣中央）。</summary>
    public static class TablePlacement
    {
        /// <summary>
        /// 虛擬桌：玩家側桌緣（牌桌原點）在頭前方的水平距離（2026-11-02 起以桌緣為基準：真實尺寸的桌子深 0.83 m，
        /// 用桌子中心放會讓桌緣跑到玩家身上）。同 SeatingPlanner.DefaultHead 的假設（頭在桌緣後 30 cm）。
        /// </summary>
        public const float FallbackEdgeForward = 0.30f;
        /// <summary>找真實桌面時往哪裡打射線：玩家擺牌的地方（桌緣前 15 cm，底牌的位置）。</summary>
        public const float SurfaceProbeForward = FallbackEdgeForward + 0.15f;
        /// <summary>虛擬桌：桌面低於視線的距離（坐著眼睛離地約 1.2 m → 桌面約 0.75 m，一般書桌高）。</summary>
        public const float FallbackBelowEye = 0.45f;
        /// <summary>
        /// 虛擬桌的桌面離地高度範圍。追蹤原點在地板（MainSceneBuilder 設 FloorLevel），頭的 y 就是眼睛離地高度：
        /// 站著玩不要變成胸口高的桌子，坐沙發也不要低到要彎腰。頭的高度不像離地高度（例如原點在眼睛）時不夾。
        /// </summary>
        public const float FallbackMinHeight = 0.62f, FallbackMaxHeight = 0.95f;
        const float FloorEyeMin = 0.9f, FloorEyeMax = 2.1f;

        /// <summary>頭部前方的水平方向；低頭看正下方時退回世界 +Z。</summary>
        public static Vector3 FlatForward(Vector3 forward)
        {
            var f = Vector3.ProjectOnPlane(forward, Vector3.up);
            return f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
        }

        /// <summary>找不到真實桌面時的虛擬桌：玩家側桌緣在前方 FallbackEdgeForward、桌面低於視線 FallbackBelowEye、+Z 朝玩家前方。</summary>
        public static Pose Fallback(Vector3 headPosition, Vector3 headForward)
        {
            var f = FlatForward(headForward);
            var origin = headPosition + f * FallbackEdgeForward;
            origin.y = FallbackHeight(headPosition.y);
            return new Pose(origin, Quaternion.LookRotation(f, Vector3.up));
        }

        /// <summary>虛擬桌桌面的世界 y。</summary>
        public static float FallbackHeight(float eyeY)
        {
            float y = eyeY - FallbackBelowEye;
            if (eyeY < FloorEyeMin || eyeY > FloorEyeMax) return y;
            return Mathf.Clamp(y, FallbackMinHeight, FallbackMaxHeight);
        }

        /// <summary>
        /// EnvironmentRaycast 命中桌面某一點：水平位置同虛擬桌（離玩家的距離固定），高度與法線用命中點。
        /// 這種方式不知道真實桌緣在哪，只對齊高度。
        /// </summary>
        public static Pose FromSurfacePoint(Vector3 hitPoint, Vector3 hitNormal, Vector3 headPosition, Vector3 headForward)
        {
            var up = hitNormal.y < 0f ? -hitNormal : hitNormal;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            var f = FlatForward(headForward);
            var origin = headPosition + f * FallbackEdgeForward;
            origin.y = hitPoint.y;
            var fwdOnPlane = Vector3.ProjectOnPlane(f, up);
            if (fwdOnPlane.sqrMagnitude < 1e-6f) fwdOnPlane = f;
            return new Pose(origin, Quaternion.LookRotation(fwdOnPlane.normalized, up));
        }

        /// <summary>
        /// 真實桌面矩形：原點放在玩家那一側桌緣的中點，+Z 指向桌面中心，+Y 為桌面法線（朝上）。
        /// 玩家那一側 = 玩家頭部投影到桌面後，「超出該邊最多」的那條邊。
        /// </summary>
        public static Pose FromRect(TableRect rect, Vector3 headPosition)
        {
            var n = rect.Normal.y < 0f ? -rect.Normal : rect.Normal;
            n.Normalize();
            var a = Vector3.ProjectOnPlane(rect.AxisA, n).normalized;
            var b = Vector3.ProjectOnPlane(rect.AxisB, n).normalized;
            var toHead = Vector3.ProjectOnPlane(headPosition - rect.Center, n);

            Vector3 bestDir = -b;
            float bestHalf = rect.HalfExtents.y;
            float bestScore = float.NegativeInfinity;
            void Consider(Vector3 dir, float half)
            {
                float score = Vector3.Dot(toHead, dir) - half;
                if (score > bestScore) { bestScore = score; bestDir = dir; bestHalf = half; }
            }
            Consider(a, rect.HalfExtents.x);
            Consider(-a, rect.HalfExtents.x);
            Consider(b, rect.HalfExtents.y);
            Consider(-b, rect.HalfExtents.y);

            var origin = rect.Center + bestDir * bestHalf;
            return new Pose(origin, Quaternion.LookRotation(-bestDir, n));
        }

        /// <summary>選桌子：玩家前方（水平）最近的一張；前方沒有就選最近的（frontOnly：前方沒有就回 −1）；清單空回 −1。</summary>
        public static int PickTable(IReadOnlyList<TableRect> tables, Vector3 headPosition, Vector3 headForward, bool frontOnly = false)
        {
            var f = FlatForward(headForward);
            int bestFront = -1, bestAny = -1;
            float dFront = float.PositiveInfinity, dAny = float.PositiveInfinity;
            for (int i = 0; i < tables.Count; i++)
            {
                var to = Vector3.ProjectOnPlane(tables[i].Center - headPosition, Vector3.up);
                float d = to.magnitude;
                if (d < dAny) { dAny = d; bestAny = i; }
                if (Vector3.Dot(to, f) > 0f && d < dFront) { dFront = d; bestFront = i; }
            }
            return bestFront >= 0 || frontOnly ? bestFront : bestAny;
        }
    }
}
