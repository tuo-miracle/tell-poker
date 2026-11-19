using System.Collections.Generic;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 字幕泡泡留在玩家的舒適視野內（2026-10-14 視野檢查：坐在側邊的 Vex 泡泡在 44°、頭上的泡泡要抬頭看）。
    /// 「正前方」是玩家看向牌桌中央的水平方向；整個泡泡（含半寬、半高）都要在水平 ±MaxYaw、仰角 MaxPitch 以內。
    /// 超出時：繞玩家的垂直軸把泡泡轉回來（距離與高度不變），太高就往下壓（水平位置不變）。
    /// </summary>
    public static class BubblePlacement
    {
        public const float MaxYaw = 33f, MaxPitch = 14f;

        /// <param name="desired">泡泡原本的位置（世界座標）。</param>
        /// <param name="viewer">玩家的頭。</param>
        /// <param name="forwardPoint">玩家正前方的參考點（牌桌中央）。</param>
        /// <param name="halfWidth">泡泡半寬（公尺）。</param>
        /// <param name="halfHeight">泡泡半高（公尺）。</param>
        public static Vector3 Clamp(Vector3 desired, Vector3 viewer, Vector3 forwardPoint, float halfWidth, float halfHeight) =>
            Clamp(desired, viewer, forwardPoint, halfWidth, halfHeight, MaxYaw);

        /// <param name="maxYaw">水平上限（度）：一般是 MaxYaw；坐在舒適區外的人（真實尺寸桌的兩端）放寬到他本人的方位附近。</param>
        public static Vector3 Clamp(Vector3 desired, Vector3 viewer, Vector3 forwardPoint, float halfWidth, float halfHeight, float maxYaw)
        {
            var fwd = forwardPoint - viewer;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-8f) return desired;
            var d = desired - viewer;
            var flat = new Vector3(d.x, 0f, d.z);
            float horiz = flat.magnitude;
            if (horiz < 1e-4f) return desired;
            bool changed = false;

            float yaw = Vector3.SignedAngle(fwd, flat, Vector3.up);
            float limit = Mathf.Max(0f, maxYaw - Mathf.Atan2(halfWidth, horiz) * Mathf.Rad2Deg);
            if (Mathf.Abs(yaw) > limit)
            {
                flat = Quaternion.AngleAxis(Mathf.Sign(yaw) * limit - yaw, Vector3.up) * flat;
                changed = true;
            }

            float y = d.y;
            float halfH = Mathf.Atan2(halfHeight, new Vector2(horiz, y).magnitude) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(y, horiz) * Mathf.Rad2Deg;
            if (pitch + halfH > MaxPitch)
            {
                // 壓低後距離變了、半高的角度也跟著變：迭代兩次就夠準
                for (int i = 0; i < 3; i++)
                {
                    y = Mathf.Tan((MaxPitch - halfH) * Mathf.Deg2Rad) * horiz;
                    halfH = Mathf.Atan2(halfHeight, new Vector2(horiz, y).magnitude) * Mathf.Rad2Deg;
                }
                changed = true;
            }
            return changed ? viewer + new Vector3(flat.x, y, flat.z) : desired;
        }

        /// <summary>泡泡、頭之間至少隔幾度（從玩家看）。</summary>
        public const float Gap = 1f;

        /// <summary>坐在舒適區外的人，泡泡的水平上限是他頭的方位再往外這麼多度（看他本來就要轉頭；泡泡要貼著他才看得出是誰講的）。</summary>
        public const float OwnerYawMargin = 6f;
        /// <summary>說話者上身的半寬（公尺）：泡泡不能放在他身後被自己擋住。</summary>
        public const float OwnTorsoHalfWidth = 0.24f;

        /// <summary>
        /// 從候選位置（依偏好排序，世界座標）挑泡泡的位置（2026-11-02 真實尺寸長圓桌：坐約 30° 的 Reginald 泡泡被夾回視野時壓到自己的頭）：
        /// 每個候選先 Clamp 進舒適視野，再看從玩家看過去會不會蓋到任何人的頭（圓）、別人正顯示的泡泡（矩形），或躲在別人的身體後面，
        /// 都不蓋到的第一個就用；全部都會蓋到時用蓋得最少的。
        /// heads[0] 是說話者自己的頭：泡泡中心一定要離他的頭比離其他人的頭近（2026-11-02 實機：Pip 座的泡泡落在 Hank 胸口，看起來是 Hank 在講）；
        /// 他坐在 ±MaxYaw 外時，水平上限放寬到他頭的方位＋OwnerYawMargin。別人的上身前後都不能蓋（前面像是他在講，後面被擋住）。
        /// </summary>
        public static Vector3 Place(IReadOnlyList<Vector3> candidates, Vector3 viewer, Vector3 forwardPoint, float halfWidth, float halfHeight,
            IReadOnlyList<(Vector3 center, float radius)> heads, IReadOnlyList<(Vector3 center, float halfWidth, float halfHeight)> bubbles,
            IReadOnlyList<(Vector3 center, float radius)> bodies = null, float ownFloorY = float.NaN)
        {
            var own = heads.Count > 0 ? Angles(heads[0].center, viewer, forwardPoint) : Vector2.zero;
            float maxYaw = Mathf.Max(MaxYaw, Mathf.Abs(own.x) + OwnerYawMargin);
            Vector3 best = candidates.Count > 0 ? Clamp(candidates[0], viewer, forwardPoint, halfWidth, halfHeight, maxYaw) : viewer;
            float bestCost = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                var p = Clamp(candidates[i], viewer, forwardPoint, halfWidth, halfHeight, maxYaw);
                var r = AngleRect(p, viewer, forwardPoint, halfWidth, halfHeight);
                float cost = 0f;
                // 歸屬：泡泡中心離說話者的頭最近
                float ownDist = Vector2.Distance(r.center, own);
                for (int h = 1; h < heads.Count; h++)
                {
                    float d = Vector2.Distance(r.center, Angles(heads[h].center, viewer, forwardPoint));
                    if (d <= ownDist + Gap) cost += 100f + ownDist - d;
                }
                for (int h = 0; h < heads.Count; h++)
                {
                    var (center, radius) = heads[h];
                    var hc = Angles(center, viewer, forwardPoint);
                    float hr = Mathf.Asin(Mathf.Clamp01(radius / Mathf.Max(1e-4f, Vector3.Distance(center, viewer)))) * Mathf.Rad2Deg + Gap;
                    float dx = Mathf.Max(r.xMin - hc.x, 0f, hc.x - r.xMax), dy = Mathf.Max(r.yMin - hc.y, 0f, hc.y - r.yMax);
                    cost += Mathf.Max(0f, hr - Mathf.Sqrt(dx * dx + dy * dy));
                }
                for (int b = 0; b < bubbles.Count; b++)
                {
                    var (center, hw, hh) = bubbles[b];
                    var o = AngleRect(center, viewer, forwardPoint, hw, hh);
                    float ox = Mathf.Min(r.xMax, o.xMax) - Mathf.Max(r.xMin, o.xMin) + Gap, oy = Mathf.Min(r.yMax, o.yMax) - Mathf.Max(r.yMin, o.yMin) + Gap;
                    if (ox > 0f && oy > 0f) cost += Mathf.Min(ox, oy);
                }
                // 別人的上身（前後都不行）
                for (int k = 0; bodies != null && k < bodies.Count; k++)
                {
                    var (center, radius) = bodies[k];
                    float bd = Vector3.Distance(center, viewer);
                    var bc = Angles(center, viewer, forwardPoint);
                    float br = Mathf.Asin(Mathf.Clamp01(radius / Mathf.Max(1e-4f, bd))) * Mathf.Rad2Deg;
                    float dx = Mathf.Max(r.xMin - bc.x, 0f, bc.x - r.xMax), dy = Mathf.Max(r.yMin - bc.y, 0f, bc.y - r.yMax);
                    cost += Mathf.Max(0f, br - Mathf.Sqrt(dx * dx + dy * dy));
                }
                // 說話者自己的上身（肩膀到桌面、寬 ±OwnTorsoHalfWidth）擋在泡泡前面（2026-11-03：荷官的泡泡躲在她自己的背心後面）
                if (heads.Count > 0 && !float.IsNaN(ownFloorY))
                {
                    var head = heads[0].center;
                    float hd = Vector3.Distance(head, viewer);
                    if (Vector3.Distance(p, viewer) > hd - 0.05f)
                    {
                        var bottom = Angles(new Vector3(head.x, ownFloorY, head.z), viewer, forwardPoint);
                        var shoulders = Angles(head - Vector3.up * 0.15f, viewer, forwardPoint);
                        float half = Mathf.Atan2(OwnTorsoHalfWidth, hd) * Mathf.Rad2Deg;
                        var torso = Rect.MinMaxRect(own.x - half, bottom.y, own.x + half, shoulders.y);
                        float ox = Mathf.Min(r.xMax, torso.xMax) - Mathf.Max(r.xMin, torso.xMin), oy = Mathf.Min(r.yMax, torso.yMax) - Mathf.Max(r.yMin, torso.yMin);
                        if (ox > 0f && oy > 0f) cost += Mathf.Min(ox, oy) + 10f;
                    }
                }
                if (cost <= 0f) return p;
                if (cost < bestCost) { bestCost = cost; best = p; }
            }
            return best;
        }

        /// <summary>從玩家看一個點的（水平角、仰角），水平角從 viewer→forwardPoint 量起。</summary>
        static Vector2 Angles(Vector3 p, Vector3 viewer, Vector3 forwardPoint)
        {
            var fwd = forwardPoint - viewer; fwd.y = 0f;
            var d = p - viewer;
            var flat = new Vector3(d.x, 0f, d.z);
            float yaw = fwd.sqrMagnitude > 1e-8f && flat.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(fwd, flat, Vector3.up) : 0f;
            return new Vector2(yaw, Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        static Rect AngleRect(Vector3 p, Vector3 viewer, Vector3 forwardPoint, float halfWidth, float halfHeight)
        {
            var a = Angles(p, viewer, forwardPoint);
            var d = p - viewer;
            float horiz = Mathf.Max(1e-4f, new Vector2(d.x, d.z).magnitude);
            float hx = Mathf.Atan2(halfWidth, horiz) * Mathf.Rad2Deg, hy = Mathf.Atan2(halfHeight, Mathf.Max(1e-4f, d.magnitude)) * Mathf.Rad2Deg;
            return Rect.MinMaxRect(a.x - hx, a.y - hy, a.x + hx, a.y + hy);
        }
    }
}
