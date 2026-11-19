using System.Collections.Generic;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>從眼睛看出去的角度矩形（度；yaw 右正、pitch 上正）。</summary>
    public struct AngleRect
    {
        public float MinYaw, MaxYaw, MinPitch, MaxPitch;

        public static AngleRect Empty => new AngleRect { MinYaw = float.MaxValue, MaxYaw = float.MinValue, MinPitch = float.MaxValue, MaxPitch = float.MinValue };
        public bool IsEmpty => MinYaw > MaxYaw;
        public float Width => MaxYaw - MinYaw;
        public float Height => MaxPitch - MinPitch;
        public Vector2 Center => new Vector2((MinYaw + MaxYaw) * 0.5f, (MinPitch + MaxPitch) * 0.5f);

        public void Add(Vector2 a)
        {
            MinYaw = Mathf.Min(MinYaw, a.x); MaxYaw = Mathf.Max(MaxYaw, a.x);
            MinPitch = Mathf.Min(MinPitch, a.y); MaxPitch = Mathf.Max(MaxPitch, a.y);
        }

        /// <summary>兩個矩形重疊（margin 度以內也算）。</summary>
        public bool Overlaps(AngleRect o, float margin = 0f) =>
            !IsEmpty && !o.IsEmpty &&
            MinYaw - margin < o.MaxYaw && o.MinYaw - margin < MaxYaw &&
            MinPitch - margin < o.MaxPitch && o.MinPitch - margin < MaxPitch;

        /// <summary>兩個矩形之間的空隙（度；重疊 = 0）。</summary>
        public float Gap(AngleRect o)
        {
            float dx = Mathf.Max(0f, Mathf.Max(o.MinYaw - MaxYaw, MinYaw - o.MaxYaw));
            float dy = Mathf.Max(0f, Mathf.Max(o.MinPitch - MaxPitch, MinPitch - o.MaxPitch));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString() => $"yaw[{MinYaw:0.0},{MaxYaw:0.0}] pitch[{MinPitch:0.0},{MaxPitch:0.0}]";
    }

    /// <summary>
    /// 示範手字幕擺在哪（牌桌座標）：貼在桌面上、示範的那隻手前面（靠玩家）或旁邊，離手 ≤ MaxDistance，
    /// 從座位上的眼睛看出去不蓋住自己的牌、籌碼堆、公共牌與底池，也不蓋住手的動作，而且在舒適視野內。
    /// 2026-10-14 截圖：舊的擺法把字往牌桌深處推，「Knock twice to check」浮在牌桌中間、離手很遠。
    /// 2026-10-14 Quest 3 實機：眼睛只比桌面高 0.2 m（裝置放在桌上）時，「在手上方」的字（照 45 cm 眼高算的）浮到 Dot 和 Vex 的身上。
    /// 所以字幕改成貼桌，並且對一整組眼睛位置（桌面上 0.15–0.55 m、桌緣後 0.22–0.40 m、左右 ±3 cm＝轉頭 ±20°）
    /// 檢查「從眼睛穿過字幕的視線」不會打到角色坐的區域（SeatZones：左、對面、右三邊的桌緣往外，含頭上的字幕泡泡）。
    /// </summary>
    public static class GhostCaptionPlacement
    {
        /// <summary>坐著的玩家眼睛（牌桌座標：桌緣後 30 cm、桌面上 45 cm）。</summary>
        public static readonly Vector3 SeatEye = new Vector3(0f, 0.45f, -0.30f);
        /// <summary>字幕中心不高過這個仰角（度；不要抬頭看），也不低於這個俯角。</summary>
        public const float MaxPitch = 0f, MinPitch = -55f, MaxYaw = 35f;
        /// <summary>字幕和手、和障礙物之間至少留的空隙（度）。</summary>
        public const float Margin = 0.6f;
        /// <summary>字幕中心離手的範圍最遠多少（公尺）。</summary>
        public const float MaxDistance = 0.10f;
        /// <summary>字幕底邊離桌面（公尺）。</summary>
        public const float Lift = 0.004f;
        /// <summary>檢查角色區域用的眼睛：高度（桌面上）、前後（桌緣後）、左右（轉頭 ±20° 時眼睛的位移）。</summary>
        public static readonly float[] EyeHeights = { 0.15f, 0.25f, 0.35f, 0.45f, 0.55f };
        public static readonly float[] EyeBacks = { -0.22f, -0.30f, -0.40f };
        public static readonly float[] EyeSides = { -0.03f, 0f, 0.03f };
        /// <summary>角色的手伸進桌面多遠（公尺；俯視截圖量的：側邊的手離桌緣約 1–2 cm、對面約 6–8 cm）。</summary>
        public const float SeatReach = 0.08f;

        /// <summary>點相對眼睛的（yaw, pitch），正前方是牌桌 +Z。</summary>
        public static Vector2 Angles(Vector3 eye, Vector3 p)
        {
            var d = p - eye;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }

        /// <summary>一個盒子（牌桌座標）的八個角看出去的角度矩形。</summary>
        public static AngleRect Of(Vector3 eye, Bounds b)
        {
            var r = AngleRect.Empty;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                r.Add(Angles(eye, c));
            }
            return r;
        }

        /// <summary>字幕板（寬 w、高 h 公尺，中心 center；flat＝平躺在桌上，否則面向眼睛）看出去的角度矩形。</summary>
        public static AngleRect Caption(Vector3 eye, Vector3 center, float w, float h, bool flat = false)
        {
            var r = AngleRect.Empty;
            for (int k = 0; k < 4; k++) r.Add(Angles(eye, Corner(eye, center, w, h, k, flat)));
            return r;
        }

        /// <summary>字幕板的「上」方向：面向眼睛時垂直於視線，平躺時是水平往遠離眼睛的方向（字頭朝牌桌中央）。</summary>
        public static Vector3 PlateUp(Vector3 eye, Vector3 center, bool flat)
        {
            var fwd = center - eye;
            if (flat) { fwd.y = 0f; return fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward; }
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            return Vector3.Cross(fwd, right).normalized;
        }

        /// <summary>字幕板的第 k 個角（0–3）。</summary>
        static Vector3 Corner(Vector3 eye, Vector3 center, float w, float h, int k, bool flat)
        {
            var fwd = center - eye;
            var right = Vector3.Cross(Vector3.up, fwd);
            right.y = 0f;
            right.Normalize();
            var up = PlateUp(eye, center, flat);
            float sx = (k & 1) == 0 ? -1f : 1f, sy = (k & 2) == 0 ? -1f : 1f;
            return center + right * (sx * w * 0.5f) + up * (sy * h * 0.5f);
        }

        /// <summary>一行字（高 lineHeight 公尺，中心 center）從眼睛看的高度（度；平躺的字會被壓扁）。</summary>
        public static float LineDegrees(Vector3 eye, Vector3 center, float lineHeight, bool flat)
        {
            var up = PlateUp(eye, center, flat) * (lineHeight * 0.5f);
            return Vector3.Angle(center + up - eye, center - up - eye);
        }

        /// <summary>預設的障礙物：玩家的兩張牌、籌碼堆、公共牌、底池（牌桌座標）。</summary>
        public static List<Bounds> DefaultObstacles(Poker.Gestures.TableLayout l)
        {
            var cards = l.CardsRect;
            var stack = new Vector3(l.ChipStackCenter.X, 0.035f, l.ChipStackCenter.Z);
            float boardZ = l.BetLineZ + 0.14f;
            return new List<Bounds>
            {
                new Bounds(new Vector3((cards.MinX + cards.MaxX) * 0.5f, 0.005f, (cards.MinZ + cards.MaxZ) * 0.5f), new Vector3(cards.MaxX - cards.MinX, 0.01f, cards.MaxZ - cards.MinZ)),
                new Bounds(stack, new Vector3(0.15f, 0.07f, 0.13f)),
                new Bounds(new Vector3(0f, 0.005f, boardZ), new Vector3(0.36f, 0.01f, 0.09f)),
                new Bounds(new Vector3(l.PotCenter.X, 0.025f, l.PotCenter.Z + 0.02f), new Vector3(0.12f, 0.05f, 0.12f)),
            };
        }

        /// <summary>
        /// 角色可能坐的區域（牌桌座標）：左、對面、右三邊的桌緣往內 SeatReach、往外 0.8 m，桌面到 1.2 m 高（頭與字幕泡泡）。
        /// 側邊從 SeatingPlanner.SideMinZ 再往玩家這邊留一個身體半徑（換座位後最靠近玩家的位置）。
        /// </summary>
        public static List<Bounds> SeatZones(Poker.Gestures.TableLayout l)
        {
            float hw = l.FeltSize.X * 0.5f, d = l.FeltSize.Y, outer = 0.8f, top = 1.2f;
            float sideMin = Poker.Runtime.SeatingPlanner.SideMinZ - Poker.Runtime.SeatingPlanner.BodyRadius;
            Bounds Box(float x0, float x1, float z0, float z1)
            {
                var b = new Bounds();
                b.SetMinMax(new Vector3(x0, 0f, z0), new Vector3(x1, top, z1));
                return b;
            }
            return new List<Bounds>
            {
                Box(-hw - outer, -hw + SeatReach, sideMin, d + outer),
                Box(-hw - outer, hw + outer, d - SeatReach, d + outer),
                Box(hw - SeatReach, hw + outer, sideMin, d + outer),
            };
        }

        /// <summary>擺放結果：中心（牌桌座標）與要不要平躺。</summary>
        public struct Spot
        {
            public Vector3 At;
            public bool Flat;
        }

        /// <summary>平躺字幕的高度（桌面上，公尺）。</summary>
        public const float FlatY = 0.006f;
        /// <summary>字至少多高（度；同 FoV 檢查）。</summary>
        public const float MinLineDegrees = 1.2f;

        /// <summary>
        /// 字幕中心（牌桌座標）。hand：示範片段裡手（所有關節、整段動作）的範圍；w、h：字幕板大小（公尺，含字幕倍率）。
        /// 在手的前面（靠玩家）與左右、貼著桌面找一圈位置（立著面向眼睛，或平躺在桌上）：「在舒適視野內、不蓋住手與障礙物、
        /// 從每個眼睛位置看都不蓋住角色區域、離手 ≤ MaxDistance、字夠大」的裡面挑離手最近的；都不行就挑違規最少的（角色區域的違規最重）。
        /// </summary>
        public static Vector3 Place(Bounds hand, float w, float h, IList<Bounds> obstacles, IList<Bounds> seatZones = null) =>
            Place(SeatEye, hand, w, h, obstacles, seatZones);

        public static Vector3 Place(Vector3 eye, Bounds hand, float w, float h, IList<Bounds> obstacles, IList<Bounds> seatZones = null)
        {
            TryPlace(eye, new[] { hand }, w, h, 0f, obstacles, seatZones, out var spot, out _);
            return spot.At;
        }

        /// <summary>
        /// 同 Place；hands：每隻手各自的範圍（雙手手勢分開算）；samples：手在各個時刻的樣子（沒有完全乾淨的位置時，挑蓋到最少時刻的）；
        /// lineHeight：一行字高（公尺，0＝不檢查字的大小）；maxDistance：離手最遠（字幕放大時跟著放大）。回傳有沒有找到完全不違規的位置，score 越小越好（沒找到時是違規程度）。
        /// 角色區域的檢查（每個候選位置 45 個眼睛 × 5 條視線）最花時間：先用便宜的條件排好，照順序檢查到第一個過的為止。
        /// </summary>
        public static bool TryPlace(Vector3 eye, IList<Bounds> hands, float w, float h, float lineHeight, IList<Bounds> obstacles, IList<Bounds> seatZones,
                                    out Spot spot, out float score, IList<Bounds> samples = null, float maxDistance = MaxDistance)
        {
            var all = hands[0];
            for (int i = 1; i < hands.Count; i++) all.Encapsulate(hands[i]);
            var c = all.center;
            var handRects = new AngleRect[hands.Count];
            var union = AngleRect.Empty;
            for (int i = 0; i < hands.Count; i++)
            {
                handRects[i] = Of(eye, hands[i]);
                union.Add(new Vector2(handRects[i].MinYaw, handRects[i].MinPitch));
                union.Add(new Vector2(handRects[i].MaxYaw, handRects[i].MaxPitch));
            }
            var obstacleRects = new List<AngleRect>();
            if (obstacles != null) foreach (var o in obstacles) obstacleRects.Add(Of(eye, o));
            AngleRect[] sampleRects = null;
            if (samples != null)
            {
                sampleRects = new AngleRect[samples.Count];
                for (int i = 0; i < samples.Count; i++) sampleRects[i] = Of(eye, samples[i]);
            }

            float y0 = h * 0.5f + Lift;
            // 深度：靠桌緣、手前面（靠玩家，看起來在手下方）、手的範圍內（放旁邊）、手後面（看起來在手上方，仍貼桌）
            float[] zs =
            {
                0.03f, 0.045f, all.min.z - h * 0.5f - 0.01f, all.min.z - 0.02f, all.min.z + 0.02f, c.z, all.max.z,
                all.max.z + 0.03f, all.max.z + 0.06f, all.max.z + 0.09f,
            };
            var cands = new List<(Spot spot, float cost, float score)>();
            for (int mode = 0; mode < 3; mode++)
            {
                // 0：立著貼桌、1：立著稍微抬高、2：平躺
                bool flat = mode == 2;
                foreach (var z0 in zs)
                {
                    // 平躺的字幕：z 是中心，靠玩家那一邊大約在 z0
                    float z = flat ? z0 + h * 0.5f - 0.01f : z0;
                    if (z - (flat ? h * 0.5f : 0f) < 0.02f) continue;   // 不要懸在桌緣外
                    // 左右：從手的左邊再過去一個字幕寬到右邊再過去一個字幕寬（寬的字幕要繞過牌）
                    for (float x = all.min.x - w * 0.5f - 0.10f; x <= all.max.x + w * 0.5f + 0.10f; x += 0.01f)
                    {
                        var p = new Vector3(x, flat ? FlatY : y0 + mode * 0.025f, z);
                        var r = Caption(eye, p, w, h, flat);
                        // 有逐時刻的手：蓋到手的動作範圍只算「蓋到幾成的時刻」（雙手 All-in 的範圍大到哪裡都會碰到，面積沒有意義）
                        float cost = Cost(r, sampleRects == null ? handRects : null, obstacleRects);
                        if (sampleRects != null)
                        {
                            bool overHand = false;
                            foreach (var hr in handRects) if (r.Overlaps(hr, Margin)) { overHand = true; break; }
                            if (overHand)
                            {
                                int covered = 0;
                                foreach (var sr in sampleRects) if (r.Overlaps(sr, Margin)) covered++;
                                cost += 5f + 20f * covered / sampleRects.Length;
                            }
                        }
                        float dist = float.MaxValue;
                        foreach (var hb in hands) dist = Mathf.Min(dist, Mathf.Sqrt(hb.SqrDistance(p)));
                        if (dist > maxDistance) cost += (dist - maxDistance) * 100f;
                        if (lineHeight > 0f)
                        {
                            float deg = LineDegrees(eye, p, lineHeight, flat);
                            if (deg < MinLineDegrees) cost += (MinLineDegrees - deg) * 60f;   // 讀不到的字幕沒有用
                        }
                        // 同樣近時：立著貼桌 > 平躺 > 抬高
                        float sc = dist * 25f + r.Gap(union) + Mathf.Abs(r.Center.x - union.Center.x) * 0.02f + (mode == 1 ? 0.5f : mode == 2 ? 0.25f : 0f);
                        cands.Add((new Spot { At = p, Flat = flat }, cost, sc));
                    }
                }
            }
            // 1) 便宜條件都過的，照分數由好到壞，第一個角色區域也過的就是答案
            cands.Sort((x, y) =>
            {
                bool xo = x.cost <= 0f, yo = y.cost <= 0f;
                if (xo != yo) return xo ? -1 : 1;
                return xo ? x.score.CompareTo(y.score) : x.cost.CompareTo(y.cost);
            });
            foreach (var cd in cands)
            {
                if (cd.cost > 0f) break;
                if (SeatHits(cd.spot.At, w, h, seatZones, cd.spot.Flat) == 0) { spot = cd.spot; score = cd.score; return true; }
            }
            // 2) 都不行：違規最少的（角色區域每個眼睛 20 分）；依便宜的違規由小到大，算到不可能更好為止
            spot = cands[0].spot;
            score = float.MaxValue;
            foreach (var cd in cands)
            {
                if (cd.cost >= score) break;
                float total = cd.cost + 20f * SeatHits(cd.spot.At, w, h, seatZones, cd.spot.Flat);
                if (total < score) { score = total; spot = cd.spot; }
            }
            return false;
        }

        /// <summary>有幾個檢查用的眼睛，從那裡穿過字幕（四角與中心）的視線會打到角色區域。</summary>
        public static int SeatHits(Vector3 p, float w, float h, IList<Bounds> seatZones, bool flat = false)
        {
            if (seatZones == null || seatZones.Count == 0) return 0;
            int hits = 0;
            foreach (var ey in EyeHeights)
                foreach (var ez in EyeBacks)
                    foreach (var ex in EyeSides)
                    {
                        var eye = new Vector3(ex, ey, ez);
                        if (SightHits(eye, p, seatZones)) { hits++; continue; }
                        for (int k = 0; k < 4; k++)
                            if (SightHits(eye, Corner(eye, p, w, h, k, flat), seatZones)) { hits++; break; }
                    }
            return hits;
        }

        /// <summary>從眼睛穿過 point 一直延伸的視線（到桌面為止）有沒有碰到任何一個盒子。</summary>
        static bool SightHits(Vector3 eye, Vector3 point, IList<Bounds> boxes)
        {
            var d = point - eye;
            // 視線往下時到桌面（y = 0）就停；往上或水平就看很遠
            float maxT = d.y < -1e-5f ? eye.y / -d.y : 50f;
            var ray = new Ray(eye, d);
            float len = d.magnitude;
            foreach (var b in boxes)
                if (b.IntersectRay(ray, out float t) && t / len <= maxT + 1e-4f) return true;
            return false;
        }

        /// <summary>違規程度（0 = 都沒違規）：超出舒適視野的度數＋和手、障礙物的重疊。</summary>
        /// <summary>log 用：某個位置違反了什麼（視野、手、障礙物、距離、字太小、從幾個眼睛看會蓋到角色區域）。</summary>
        public static string Explain(Vector3 eye, Spot spot, IList<Bounds> hands, float w, float h, float lineHeight, IList<Bounds> obstacles, IList<Bounds> seatZones, float maxDistance)
        {
            var r = Caption(eye, spot.At, w, h, spot.Flat);
            var sb = new System.Text.StringBuilder();
            if (r.MaxPitch > MaxPitch || r.MinPitch < MinPitch) sb.Append($" pitch[{r.MinPitch:0.0},{r.MaxPitch:0.0}]");
            if (Mathf.Max(Mathf.Abs(r.MinYaw), Mathf.Abs(r.MaxYaw)) > MaxYaw) sb.Append($" yaw[{r.MinYaw:0.0},{r.MaxYaw:0.0}]");
            for (int i = 0; i < hands.Count; i++) if (r.Overlaps(Of(eye, hands[i]), Margin)) sb.Append(" hand" + i);
            if (obstacles != null) for (int i = 0; i < obstacles.Count; i++) if (r.Overlaps(Of(eye, obstacles[i]), Margin)) sb.Append(" obstacle" + i);
            float dist = float.MaxValue;
            foreach (var hb in hands) dist = Mathf.Min(dist, Mathf.Sqrt(hb.SqrDistance(spot.At)));
            if (dist > maxDistance) sb.Append($" dist {dist:0.000}");
            if (lineHeight > 0f && LineDegrees(eye, spot.At, lineHeight, spot.Flat) < MinLineDegrees) sb.Append($" text {LineDegrees(eye, spot.At, lineHeight, spot.Flat):0.00}°");
            int seats = SeatHits(spot.At, w, h, seatZones, spot.Flat);
            if (seats > 0) sb.Append($" seats x{seats}");
            return sb.Length == 0 ? "clean" : sb.ToString().Trim();
        }

        static float Cost(AngleRect r, IList<AngleRect> handRects, IList<AngleRect> obstacleRects)
        {
            float cost = 0f;
            cost += Mathf.Max(0f, r.MaxPitch - MaxPitch) * 3f + Mathf.Max(0f, MinPitch - r.MinPitch);
            cost += Mathf.Max(0f, Mathf.Max(Mathf.Abs(r.MinYaw), Mathf.Abs(r.MaxYaw)) - MaxYaw);
            if (handRects != null)
                foreach (var handRect in handRects)
                    if (r.Overlaps(handRect, Margin)) cost += 5f + Overlap(r, handRect);
            // 牌、籌碼、公共牌、底池要看得到：沒有乾淨位置時，寧可蓋到手經過的地方也不要蓋到它們
            foreach (var o in obstacleRects)
                if (r.Overlaps(o, Margin)) cost += 10f + Overlap(r, o);
            return cost;
        }

        static float Overlap(AngleRect a, AngleRect b)
        {
            float x = Mathf.Max(0f, Mathf.Min(a.MaxYaw, b.MaxYaw) - Mathf.Max(a.MinYaw, b.MinYaw));
            float y = Mathf.Max(0f, Mathf.Min(a.MaxPitch, b.MaxPitch) - Mathf.Max(a.MinPitch, b.MinPitch));
            return x * y;
        }
    }
}
