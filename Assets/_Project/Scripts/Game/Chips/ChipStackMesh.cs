using System.Collections.Generic;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 整堆籌碼合成一個網格（不用剛體、一堆一個 draw call）。每柱最多 MaxPerColumn 枚，多的往旁邊排。
    /// 原點在堆底中央，柱子在 XZ 平面上緊密排列。odd = 多一枚不足面額的「零頭」籌碼（藍色、單獨一柱）。
    /// 貼圖是 2×2 圖集：左半是正面（紅／藍），右半是側邊條紋（紅／藍）。
    /// </summary>
    public static class ChipStackMesh
    {
        public const float Radius = 0.02f;
        public const float Thickness = 0.0035f;
        public const int MaxPerColumn = 20;
        const int Segments = 18;
        const float Gap = 0.003f;

        static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();
        static Texture2D _atlas;
        static Material _material;

        public static int Columns(int chips) => chips <= 0 ? 0 : (chips + MaxPerColumn - 1) / MaxPerColumn;

        /// <summary>第 i 柱相對於堆中心的位置（緊密的蜂巢排列，先排一列再往後排）。</summary>
        public static Vector3 ColumnOffset(int i)
        {
            float step = Radius * 2f + Gap;
            int row = i / 3, col = i % 3;
            float x = (col - 1) * step + (row % 2 == 1 ? step * 0.5f : 0f);
            float z = -row * step * 0.87f;
            return new Vector3(x, 0f, z);
        }

        public static Mesh Get(int chips, bool odd = false)
        {
            chips = Mathf.Max(0, chips);
            int key = chips * 2 + (odd ? 1 : 0);
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = Build(chips, odd);
            Cache[key] = m;
            return m;
        }

        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                _material = new Material(Poker.Characters.MaterialCache.Lit(Color.white, 0.55f)) { name = "Chips" };
                _material.SetTexture("_BaseMap", Atlas);
                _material.SetColor("_BaseColor", Color.white);
                return _material;
            }
        }

        static Mesh Build(int chips, bool odd)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            int cols = Columns(chips);
            for (int c = 0; c < cols; c++)
            {
                int count = Mathf.Min(MaxPerColumn, chips - c * MaxPerColumn);
                AddColumn(v, n, uv, tri, ColumnOffset(c), count, false);
            }
            if (odd) AddColumn(v, n, uv, tri, ColumnOffset(cols), 1, true);

            var m = new Mesh { name = $"Chips{chips}{(odd ? "+odd" : "")}" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        static void AddColumn(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 at, int count, bool blue)
        {
            float faceU0 = blue ? 0.25f : 0f, sideU0 = blue ? 0.75f : 0.5f;
            // 每枚一圈側面（條紋貼圖轉一圈）；只畫最上面那枚的頂面
            for (int k = 0; k < count; k++)
            {
                float y0 = k * Thickness, y1 = y0 + Thickness;
                int baseIdx = v.Count;
                float twist = (k * 37 % 360) / 360f; // 每枚稍微轉一下，條紋不會排成直線
                for (int s = 0; s <= Segments; s++)
                {
                    float a = (s / (float)Segments) * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    float u = sideU0 + 0.25f * Mathf.Repeat(s / (float)Segments + twist, 1f);
                    if (s == Segments) u = sideU0 + 0.25f * (twist > 0 ? twist : 1f);
                    v.Add(at + dir * Radius + Vector3.up * y0); n.Add(dir); uv.Add(new Vector2(u, 0.02f));
                    v.Add(at + dir * Radius + Vector3.up * y1); n.Add(dir); uv.Add(new Vector2(u, 0.48f));
                }
                for (int s = 0; s < Segments; s++)
                {
                    int i0 = baseIdx + s * 2;
                    tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2);
                    tri.Add(i0 + 1); tri.Add(i0 + 3); tri.Add(i0 + 2);
                }
            }
            float top = count * Thickness;
            int center = v.Count;
            v.Add(at + Vector3.up * top); n.Add(Vector3.up); uv.Add(new Vector2(faceU0 + 0.125f, 0.75f));
            for (int s = 0; s <= Segments; s++)
            {
                float a = (s / (float)Segments) * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(at + dir * Radius + Vector3.up * top); n.Add(Vector3.up);
                uv.Add(new Vector2(faceU0 + 0.125f + dir.x * 0.12f, 0.75f + dir.z * 0.24f));
            }
            for (int s = 0; s < Segments; s++)
            {
                tri.Add(center); tri.Add(center + s + 2); tri.Add(center + s + 1);
            }
        }

        /// <summary>圖集 256×128：左上半正面（紅、藍），右半側邊條紋。</summary>
        static Texture2D Atlas
        {
            get
            {
                if (_atlas != null) return _atlas;
                const int W = 256, H = 128;
                var px = new Color32[W * H];
                Color red = new Color(0.78f, 0.12f, 0.12f), blue = new Color(0.16f, 0.32f, 0.78f), white = new Color(0.96f, 0.95f, 0.92f);
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    Color c;
                    if (v >= 0.5f && u < 0.5f)
                    {
                        // 正面：外圈白色方塊、內圈細白環
                        bool isBlue = u >= 0.25f;
                        var body = isBlue ? blue : red;
                        float lu = (u - (isBlue ? 0.25f : 0f)) / 0.25f * 2f - 1f, lv = (v - 0.5f) / 0.5f * 2f - 1f;
                        float r = Mathf.Sqrt(lu * lu + lv * lv), ang = Mathf.Atan2(lv, lu);
                        bool notch = r > 0.78f && Mathf.Repeat(ang / (Mathf.PI * 2f) * 8f, 1f) < 0.45f;
                        bool ring = r > 0.5f && r < 0.57f;
                        c = notch || ring ? white : body;
                    }
                    else if (v < 0.5f && u >= 0.5f)
                    {
                        bool isBlue = u >= 0.75f;
                        var body = isBlue ? blue : red;
                        float lu = (u - (isBlue ? 0.75f : 0.5f)) / 0.25f;
                        c = Mathf.Repeat(lu * 8f, 1f) < 0.4f ? white : body;
                    }
                    else c = red;
                    px[y * W + x] = c;
                }
                _atlas = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "ChipAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _atlas.SetPixels32(px);
                _atlas.Apply(true, false);
                return _atlas;
            }
        }
    }
}
