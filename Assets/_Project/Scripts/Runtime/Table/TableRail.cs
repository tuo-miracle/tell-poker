using System.Collections.Generic;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 備案虛擬桌的桌緣（牌室桌：皮革軟墊扶手、底下一條米色飾條、深色桌邊）：一個網格、三個子網格（三種材質）。
    /// 斷面沿著長圓形絨布的邊繞一圈：內緣就是絨布邊（不蓋到絨布），外緣往外 VirtualBorder。
    /// 遠端長邊中央是荷官的缺口（寬 2 × CutoutHalfWidth）：那一段沒有扶手，只有一條往下的深色桌邊，絨布不會露出紙一樣薄的邊。
    /// 座標同牌桌：絨布 X ∈ [−w/2, w/2]、Z ∈ [0, d]，桌面 y = 0。
    /// </summary>
    public static class TableRail
    {
        public const int Leather = 0, Trim = 1, Sides = 2, SubmeshCount = 3;
        /// <summary>扶手高出桌面多少（公尺）：籌碼和牌仍落在絨布上，扶手只是一圈矮矮的軟墊。</summary>
        public const float PadHeight = 0.03f;
        /// <summary>荷官缺口的半寬（公尺）：約一個人的肩寬。</summary>
        public const float CutoutHalfWidth = 0.25f;
        const float TrimHeight = 0.006f, TrimInset = 0.002f, SideInset = 0.004f;
        const int ArcSteps = 10, EndSteps = 24;

        struct P
        {
            public float S, Y, Ns, Ny;   // 往外距離、高度、斷面法線（往外、往上）
            public P(float s, float y, float ns, float ny) { S = s; Y = y; var l = Mathf.Sqrt(ns * ns + ny * ny); Ns = ns / l; Ny = ny / l; }
        }

        public static Mesh Build(Vector2 felt)
        {
            float b = TableDimensions.VirtualBorder, y0 = TableDimensions.FeltLift, bottom = -TableDimensions.VirtualTopThickness;
            var parts = new List<(int sub, P[] pts)>();

            // 軟墊：半橢圓（內緣在絨布邊、外緣在最外側）
            var pad = new P[ArcSteps + 1];
            for (int i = 0; i <= ArcSteps; i++)
            {
                float th = Mathf.PI * (1f - (float)i / ArcSteps);
                float c = Mathf.Cos(th), s = Mathf.Sin(th);
                pad[i] = new P(b * 0.5f * (1f + c), y0 + PadHeight * s, c * PadHeight, s * b * 0.5f);
            }
            parts.Add((Leather, pad));
            // 軟墊下緣往內收到飾條（朝下）
            parts.Add((Sides, new[] { new P(b, y0, 0, -1), new P(b - TrimInset, y0, 0, -1) }));
            // 米色飾條（朝外）
            parts.Add((Trim, new[] { new P(b - TrimInset, y0, 1, 0), new P(b - TrimInset, y0 - TrimHeight, 1, 0) }));
            parts.Add((Sides, new[] { new P(b - TrimInset, y0 - TrimHeight, 0, -1), new P(b - SideInset, y0 - TrimHeight, 0, -1) }));
            // 桌邊（朝外）與底面（朝下，收到絨布邊）
            parts.Add((Sides, new[] { new P(b - SideInset, y0 - TrimHeight, 1, 0), new P(b - SideInset, bottom, 1, 0) }));
            parts.Add((Sides, new[] { new P(b - SideInset, bottom, 0, -1), new P(0, bottom, 0, -1) }));
            // 荷官缺口：只有絨布邊往下的深色桌邊
            var cut = new[] { new P(0, y0, 1, 0), new P(0, bottom, 1, 0) };

            float half = Mathf.Min(CutoutHalfWidth, Mathf.Max(0f, felt.x * 0.5f - felt.y * 0.5f));
            var rail = RailPath(felt, half);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>[SubmeshCount];
            for (int i = 0; i < SubmeshCount; i++) tris[i] = new List<int>();
            foreach (var (sub, pts) in parts) Sweep(rail, pts, sub, verts, normals, tris);
            if (half > 0f)
            {
                Sweep(Subdivide(new List<(Vector3, Vector3)> { (new Vector3(half, 0, felt.y), Vector3.forward), (new Vector3(-half, 0, felt.y), Vector3.forward) }), cut, Sides, verts, normals, tris);
                // 扶手在缺口兩端的斷面（封口，面向缺口）
                var profile = new List<P>(pad);
                for (int k = 1; k < parts.Count; k++) profile.Add(parts[k].pts[parts[k].pts.Length - 1]);
                Cap(rail[0].corner, Vector3.right, Vector3.forward, profile, verts, normals, tris[Sides]);
                Cap(rail[rail.Count - 1].corner, Vector3.left, Vector3.forward, profile, verts, normals, tris[Sides]);
            }

            var mesh = new Mesh { name = "TableRail" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 扶手沿著絨布邊走的路徑（絨布邊上的點、往外的法線），由上往下看逆時針：
        /// 從缺口左端沿遠端直邊往左 → 左端半圓 → 近端直邊 → 右端半圓 → 遠端直邊回到缺口右端（沒有缺口就繞完一圈）。
        /// </summary>
        static List<(Vector3 corner, Vector3 n)> RailPath(Vector2 felt, float half)
        {
            float r = felt.y * 0.5f, cx = Mathf.Max(0f, felt.x * 0.5f - r);
            var path = new List<(Vector3, Vector3)>();
            if (half > 0f) path.Add((new Vector3(-half, 0, felt.y), Vector3.forward));
            foreach (var (c, start) in new[] { (-cx, 90f), (cx, -90f) })
                for (int i = 0; i <= EndSteps; i++)
                {
                    float a = (start + 180f * i / EndSteps) * Mathf.Deg2Rad;
                    var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    path.Add((new Vector3(c, 0, r) + n * r, n));
                }
            path.Add(half > 0f ? (new Vector3(half, 0, felt.y), Vector3.forward) : path[0]);
            return Subdivide(path);
        }

        /// <summary>直邊每 StraightStep 一個點（燈光內插、俯視時扶手頂的頂點分布比較平均）。</summary>
        static List<(Vector3 corner, Vector3 n)> Subdivide(List<(Vector3 corner, Vector3 n)> path)
        {
            var fine = new List<(Vector3, Vector3)> { path[0] };
            for (int i = 1; i < path.Count; i++)
            {
                var (p0, n0) = path[i - 1];
                var (p1, n1) = path[i];
                int k = n0 == n1 ? Mathf.CeilToInt(Vector3.Distance(p0, p1) / StraightStep) : 1;
                if (k > 1 && k % 2 == 1) k++;   // 偶數段：直邊正中間（玩家正前方、荷官正前方）一定有頂點
                for (int j = 1; j <= k; j++) fine.Add((Vector3.Lerp(p0, p1, (float)j / k), n1));
            }
            return fine;
        }

        const float StraightStep = 0.2f;

        /// <summary>斷面沿路徑掃出一條帶子；三角形繞向依頂點法線決定（正面朝外，不會被背面剔除）。</summary>
        static void Sweep(List<(Vector3 corner, Vector3 n)> path, P[] pts, int sub, List<Vector3> verts, List<Vector3> normals, List<int>[] tris)
        {
            int start = verts.Count, m = pts.Length;
            foreach (var (corner, dir) in path)
                foreach (var p in pts)
                {
                    verts.Add(corner + dir * p.S + Vector3.up * p.Y);
                    normals.Add(dir * p.Ns + Vector3.up * p.Ny);
                }
            for (int a = 0; a + 1 < path.Count; a++)
                for (int j = 0; j < m - 1; j++)
                {
                    int aj = start + a * m + j, bj = start + (a + 1) * m + j;
                    AddQuad(verts, normals, tris[sub], aj, bj, aj + 1, bj + 1);
                }
        }

        static void AddQuad(List<Vector3> v, List<Vector3> n, List<int> t, int a, int b, int a1, int b1)
        {
            var face = Vector3.Cross(v[b] - v[a], v[a1] - v[a]);
            if (Vector3.Dot(face, n[a] + n[b] + n[a1]) >= 0f) t.AddRange(new[] { a, b, a1, a1, b, b1 });
            else t.AddRange(new[] { a, a1, b, a1, b1, b });
        }

        /// <summary>在路徑端點 corner 封住斷面（法線 facing），斷面的「往外」是 outward。</summary>
        static void Cap(Vector3 corner, Vector3 facing, Vector3 outward, List<P> profile, List<Vector3> verts, List<Vector3> normals, List<int> tris)
        {
            float b = TableDimensions.VirtualBorder;
            int c = verts.Count;
            verts.Add(corner + outward * (b * 0.5f) + Vector3.up * ((TableDimensions.FeltLift - TableDimensions.VirtualTopThickness) * 0.5f));
            normals.Add(facing);
            foreach (var p in profile) { verts.Add(corner + outward * p.S + Vector3.up * p.Y); normals.Add(facing); }
            for (int i = 0; i < profile.Count; i++)
            {
                int a = c + 1 + i, d = c + 1 + (i + 1) % profile.Count;
                var face = Vector3.Cross(verts[a] - verts[c], verts[d] - verts[c]);
                if (Vector3.Dot(face, facing) >= 0f) tris.AddRange(new[] { c, a, d });
                else tris.AddRange(new[] { c, d, a });
            }
        }
    }

    /// <summary>絨布的細毛感：可無縫平鋪的小張灰階雜訊（乘上絨布色），執行期產生，不用貼圖資產。</summary>
    public static class FeltTexture
    {
        public static Texture2D Create(int size, int seed)
        {
            var rnd = new System.Random(seed);
            const int cells = 8;   // 週期性 value noise：格點在邊界繞回，所以左右、上下接得起來
            var lattice = new float[cells, cells];
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++) lattice[x, y] = (float)rnd.NextDouble();
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = (float)x / size * cells, fy = (float)y / size * cells;
                    int x0 = (int)fx, y0 = (int)fy;
                    float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
                    float a = Mathf.Lerp(lattice[x0 % cells, y0 % cells], lattice[(x0 + 1) % cells, y0 % cells], tx);
                    float c = Mathf.Lerp(lattice[x0 % cells, (y0 + 1) % cells], lattice[(x0 + 1) % cells, (y0 + 1) % cells], tx);
                    float grain = (float)rnd.NextDouble();
                    float v = 0.9f + 0.06f * Mathf.Lerp(a, c, ty) + 0.04f * grain;
                    px[y * size + x] = new Color(v, v, v, 1f);
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "FeltNap", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
