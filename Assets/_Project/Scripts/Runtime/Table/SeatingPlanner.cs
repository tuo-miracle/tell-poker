using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>SeatingPlanner 的結果：座位（與輸入同順序）、配置名稱、還被擋住的人數與說明。</summary>
    public sealed class SeatingPlan
    {
        public Pose[] Seats;
        /// <summary>"default"（照預設）、"shifted"（換位置）、"compressed"（擠一點才坐得下）。</summary>
        public string Layout;
        public int BlockedSeats;
        /// <summary>哪幾邊被擋住（例如 "far 100% wall, left 40% STORAGE"）；"none"／"no scene"。</summary>
        public string Blocked;
        /// <summary>每個座位在哪一邊、離該邊起點多遠（log 用）。</summary>
        public string SeatList;

        public string Describe() => Layout + " (blocked: " + Blocked + ")" + (string.IsNullOrEmpty(SeatList) ? "" : " seats=" + SeatList);

        public bool SameSeatsAs(SeatingPlan other, float tolerance = 0.01f)
        {
            if (other == null || other.Seats.Length != Seats.Length) return false;
            for (int i = 0; i < Seats.Length; i++)
                if (Vector3.Distance(Seats[i].position, other.Seats[i].position) > tolerance) return false;
            return true;
        }
    }

    /// <summary>
    /// 依真實房間安排 AI 與荷官的座位（牌桌座標）。絨布是長圓形（兩端半徑＝半個桌深的半圓，TableLayout.InFelt），
    /// 座位只能在玩家以外的那一圈桌緣上：左端半圓 → 遠端直邊 → 右端半圓（玩家那條直邊不坐人），用沿著桌緣的弧長 s 表示：
    /// 左端從靠玩家的 SideMinZ 往遠端走 → 遠端由左到右 → 右端從遠端走回 SideMinZ。
    /// 角色的身體在桌緣後方 BodyBehind（扶手外），半徑 BodyRadius；身體撞到牆、在房間外、撞到比桌面高的家具，就是被擋住。
    /// 規劃：每 2 cm 一個候選點（加上預設位置），排除玩家視線 ±MaxYaw 以外的點，再用動態規劃找
    /// 「順序不變（行動順序和看到的一致）、相鄰身體至少 Spacing、被擋住的人最少、離預設位置最近」的組合；
    /// 正常間距排不下時改用 CompressedSpacing（擠一點）。沒有房間資料就照預設。
    /// </summary>
    public static class SeatingPlanner
    {
        /// <summary>身體中心在桌緣後方多遠：扶手 0.12 m 外再 0.14 m（同 TableSeats.BodyBehind；荷官在缺口裡其實近一點，這裡一律用扶手外的距離，保守）。</summary>
        public const float BodyBehind = 0.26f;
        public const float BodyRadius = 0.17f;
        /// <summary>角色身體的高度範圍（牌桌 Y）：臀部略低於桌面，頭頂約桌面上 0.7 m。</summary>
        public const float BodyBottom = -0.1f, BodyTop = 0.7f;
        /// <summary>兩端半圓的座位最靠近玩家的位置（牌桌 Z）：再近就擠到玩家的手邊。</summary>
        public const float SideMinZ = 0.3f;
        /// <summary>角色要在玩家正前方左右這個角度內（FoV-aware：比賽也看窄視角的眼鏡）。</summary>
        public const float MaxYaw = 60f;
        /// <summary>相鄰兩人身體中心的最小距離：一般＝真實牌桌一個座位寬（0.7 m）；擠一點＝真人肩寬約 0.45 m 再留 10 cm，再小就肩膀疊在一起。</summary>
        public const float Spacing = 0.70f, CompressedSpacing = 0.55f;
        public const float Step = 0.02f;
        /// <summary>不知道頭在哪時的假設：桌緣後 30 cm、桌面上 45 cm。</summary>
        public static readonly Vector3 DefaultHead = new Vector3(0f, 0.45f, -0.3f);
        const float BlockedCost = 1000f;

        enum Edge { Left, Far, Right }

        static float Radius(Vector2 felt) => felt.y * 0.5f;
        /// <summary>兩端半圓圓心的 |X|（遠端直邊的半長）。</summary>
        static float Cx(Vector2 felt) => Mathf.Max(0f, felt.x * 0.5f - Radius(felt));
        /// <summary>兩端半圓上能坐的最大角度（從遠端 +Z 量起往外轉，弧度）：根節點不比 SideMinZ 更靠近玩家。</summary>
        static float MaxArc(Vector2 felt) => Mathf.Acos(Mathf.Clamp((SideMinZ - Radius(felt)) / Radius(felt), -1f, 1f));
        static float ArcLength(Vector2 felt) => Radius(felt) * MaxArc(felt);
        static float PathLength(Vector2 felt) => 2f * ArcLength(felt) + 2f * Cx(felt);

        static Edge EdgeOf(Vector2 felt, float s)
        {
            float l1 = ArcLength(felt);
            if (s < l1 - 1e-5f) return Edge.Left;
            if (s <= l1 + 2f * Cx(felt) + 1e-5f) return Edge.Far;
            return Edge.Right;
        }

        /// <summary>弧長 s 的座位姿勢：根節點在桌緣；遠端直邊的人直接面向玩家側，兩端半圓的人面向自己那個半圓的圓心（同 TableSeats）。</summary>
        public static Pose PoseAt(Vector2 felt, float s)
        {
            float r = Radius(felt), cx = Cx(felt), l1 = ArcLength(felt), max = MaxArc(felt);
            s = Mathf.Clamp(s, 0f, PathLength(felt));
            Vector3 p, fwd;
            switch (EdgeOf(felt, s))
            {
                case Edge.Left:
                {
                    float th = max - s / r;
                    var dir = new Vector3(-Mathf.Sin(th), 0f, Mathf.Cos(th));
                    p = new Vector3(-cx, 0f, r) + dir * r; fwd = -dir;
                    break;
                }
                case Edge.Far:
                    p = new Vector3(-cx + (s - l1), 0f, felt.y); fwd = Vector3.back;
                    break;
                default:
                {
                    float th = (s - l1 - 2f * cx) / r;
                    var dir = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                    p = new Vector3(cx, 0f, r) + dir * r; fwd = -dir;
                    break;
                }
            }
            return new Pose(p, Quaternion.LookRotation(fwd, Vector3.up));
        }

        /// <summary>任一根節點位置在路徑上的弧長（投影到最近的那段桌緣）。</summary>
        public static float ParamOf(Vector2 felt, Vector3 root)
        {
            float r = Radius(felt), cx = Cx(felt), l1 = ArcLength(felt), max = MaxArc(felt);
            if (root.x < -cx) return r * (max - Mathf.Clamp(Mathf.Atan2(-(root.x + cx), root.z - r), 0f, max));
            if (root.x > cx) return l1 + 2f * cx + r * Mathf.Clamp(Mathf.Atan2(root.x - cx, root.z - r), 0f, max);
            return l1 + (root.x + cx);
        }

        /// <summary>角色身體中心（桌緣後方 BodyBehind）。</summary>
        public static Vector3 BodyOf(Pose seat) => seat.position - seat.rotation * Vector3.forward * BodyBehind;

        /// <summary>從玩家的頭看過去，水平角度在牌桌 +Z（玩家面向牌桌）左右 MaxYaw 內。</summary>
        public static bool InView(Vector3 head, Vector3 body)
        {
            var to = new Vector3(body.x - head.x, 0f, body.z - head.z);
            if (to.sqrMagnitude < 1e-8f) return false;
            return Vector3.Angle(Vector3.forward, to) <= MaxYaw;
        }

        struct Candidate
        {
            public float S;
            public Pose Pose;
            public Vector3 Body;
            public string BlockedBy;
            public Edge Edge;
        }

        public static SeatingPlan Plan(Vector2 felt, IReadOnlyList<Pose> defaults, RoomObstacles room, Vector3 head)
        {
            int n = defaults.Count;
            var plan = new SeatingPlan { Seats = new Pose[n], Layout = "default", Blocked = "no scene" };
            for (int i = 0; i < n; i++) plan.Seats[i] = defaults[i];
            if (room == null || n == 0) return plan;

            // 依預設位置沿路徑排序（順序＝座位順序）
            var defS = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) { defS[i] = ParamOf(felt, defaults[i].position); order[i] = i; }
            System.Array.Sort(order, (a, b) => defS[a].CompareTo(defS[b]));

            // 候選點：格點＋預設位置；視線外的直接不考慮
            var svals = new List<float>();
            float len = PathLength(felt);
            for (float s = 0f; s <= len + 1e-4f; s += Step) svals.Add(Mathf.Min(s, len));
            foreach (var s in defS) svals.Add(s);
            svals.Sort();
            var cands = new List<Candidate>();
            var edgeTotal = new int[3];
            var edgeBlocked = new int[3];
            var edgeReason = new string[3];
            foreach (var s in svals)
            {
                if (cands.Count > 0 && Mathf.Abs(cands[cands.Count - 1].S - s) < 1e-5f) continue;
                var pose = PoseAt(felt, s);
                var body = BodyOf(pose);
                if (!InView(head, body)) continue;
                var c = new Candidate { S = s, Pose = pose, Body = body, Edge = EdgeOf(felt, s) };
                c.BlockedBy = room.BlockedBy(new Vector2(body.x, body.z), BodyRadius, BodyBottom, BodyTop);
                edgeTotal[(int)c.Edge]++;
                if (c.BlockedBy != null) { edgeBlocked[(int)c.Edge]++; edgeReason[(int)c.Edge] ??= c.BlockedBy; }
                cands.Add(c);
            }

            var sortedDef = new float[n];
            for (int j = 0; j < n; j++) sortedDef[j] = defS[order[j]];
            var best = Solve(cands, sortedDef, Spacing, out int blocked);
            bool compressed = false;
            if (best == null || blocked > 0)
            {
                var tight = Solve(cands, sortedDef, CompressedSpacing, out int blockedTight);
                if (tight != null && (best == null || blockedTight < blocked)) { best = tight; blocked = blockedTight; compressed = true; }
            }

            plan.Blocked = Summary(edgeTotal, edgeBlocked, edgeReason);
            if (best == null)
            {
                // 視線內連一個合法組合都沒有（頭的位置很怪）：照預設，至少不比原本差
                plan.BlockedSeats = n;
                return plan;
            }

            bool moved = false;
            var list = new StringBuilder();
            for (int j = 0; j < n; j++)
            {
                var c = cands[best[j]];
                plan.Seats[order[j]] = c.Pose;
                if (Mathf.Abs(c.S - sortedDef[j]) > 1e-4f) moved = true;
                if (j > 0) list.Append(',');
                list.Append(c.Edge.ToString().ToLowerInvariant()).Append('@').Append(c.S.ToString("F2"));
                if (c.BlockedBy != null) list.Append("!").Append(c.BlockedBy);
            }
            plan.BlockedSeats = blocked;
            plan.Layout = !moved ? "default" : compressed ? "compressed" : "shifted";
            plan.SeatList = moved ? list.ToString() : null;
            return plan;
        }

        /// <summary>動態規劃：回傳每個座位（依路徑順序）選的候選索引；沒有合法組合回 null。</summary>
        static int[] Solve(List<Candidate> cands, float[] def, float spacing, out int blocked)
        {
            blocked = 0;
            int n = def.Length, m = cands.Count;
            if (m < n) return null;
            var cost = new float[n, m];
            var prev = new int[n, m];
            for (int j = 0; j < n; j++)
                for (int k = 0; k < m; k++)
                {
                    cost[j, k] = float.PositiveInfinity;
                    float own = (cands[k].BlockedBy != null ? BlockedCost : 0f) + Mathf.Abs(cands[k].S - def[j]);
                    if (j == 0) { cost[j, k] = own; prev[j, k] = -1; continue; }
                    for (int q = 0; q < k; q++)
                    {
                        if (float.IsPositiveInfinity(cost[j - 1, q])) continue;
                        if (Vector3.Distance(cands[q].Body, cands[k].Body) < spacing - 1e-4f) continue;
                        float c = cost[j - 1, q] + own;
                        if (c < cost[j, k]) { cost[j, k] = c; prev[j, k] = q; }
                    }
                }
            int end = -1;
            float bestCost = float.PositiveInfinity;
            for (int k = 0; k < m; k++) if (cost[n - 1, k] < bestCost) { bestCost = cost[n - 1, k]; end = k; }
            if (end < 0) return null;
            var pick = new int[n];
            for (int j = n - 1, k = end; j >= 0; k = prev[j, k], j--) pick[j] = k;
            foreach (var k in pick) if (cands[k].BlockedBy != null) blocked++;
            return pick;
        }

        static string Summary(int[] total, int[] blocked, string[] reason)
        {
            var parts = new List<string>();
            foreach (Edge e in new[] { Edge.Far, Edge.Left, Edge.Right })
            {
                int i = (int)e;
                if (blocked[i] == 0) continue;
                parts.Add(e.ToString().ToLowerInvariant() + " " + Mathf.RoundToInt(100f * blocked[i] / Mathf.Max(1, total[i])) + "% " + reason[i]);
            }
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}
