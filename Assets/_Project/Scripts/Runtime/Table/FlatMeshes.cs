using System.Collections.Generic;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>平放在 XZ 平面、朝 +Y 的簡單網格（牌桌標線用）。</summary>
    public static class FlatMeshes
    {
        public static Mesh Rect(Vector2 size)
        {
            var mesh = new Mesh { name = "FlatRect" };
            float x = size.x * 0.5f, z = size.y * 0.5f;
            mesh.SetVertices(new List<Vector3> { new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, 0, -z) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 長圓形（兩端是半徑 size.y/2 的半圓，中間直邊），中心在原點、朝 +Y；UV 同 Rect（整塊 0–1）。
        /// stepsPerEnd：每個半圓切幾段。
        /// </summary>
        public static Mesh Stadium(Vector2 size, int stepsPerEnd = 24)
        {
            float r = size.y * 0.5f, cx = Mathf.Max(0f, size.x * 0.5f - r);
            var verts = new List<Vector3> { Vector3.zero };
            // 由上往下看順時針（＝朝 +Y，同 Ring）：右端從遠端（+Z）轉到近端，再接左端從近端轉回遠端
            for (int end = 0; end < 2; end++)
            {
                float c = end == 0 ? cx : -cx, start = end == 0 ? 90f : -90f;
                for (int i = 0; i <= stepsPerEnd; i++)
                {
                    float a = (start - 180f * i / stepsPerEnd) * Mathf.Deg2Rad;
                    verts.Add(new Vector3(c + r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)));
                }
            }
            var tris = new List<int>();
            int n = verts.Count - 1;
            for (int i = 1; i <= n; i++) tris.AddRange(new[] { 0, i, i % n + 1 });
            var uv = new List<Vector2>();
            foreach (var v in verts) uv.Add(new Vector2(v.x / size.x + 0.5f, v.z / size.y + 0.5f));
            var mesh = new Mesh { name = "FlatStadium" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Ring(float radius, float width, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float r0 = radius - width * 0.5f, r1 = radius + width * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * r0);
                verts.Add(d * r1);
                if (i == segments) break;
                int k = i * 2;
                // 由上往下看順時針 → 朝 +Y
                tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            var mesh = new Mesh { name = "FlatRing" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh RectOutline(Vector2 size, float width)
        {
            var combine = new List<CombineInstance>();
            float x = size.x * 0.5f, z = size.y * 0.5f;
            void Add(Vector3 c, Vector2 s) => combine.Add(new CombineInstance { mesh = Rect(s), transform = Matrix4x4.Translate(c) });
            Add(new Vector3(0, 0, -z), new Vector2(size.x + width, width));
            Add(new Vector3(0, 0, z), new Vector2(size.x + width, width));
            Add(new Vector3(-x, 0, 0), new Vector2(width, size.y - width));
            Add(new Vector3(x, 0, 0), new Vector2(width, size.y - width));
            var mesh = new Mesh { name = "FlatRectOutline" };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (var ci in combine) Object.Destroy(ci.mesh);
            return mesh;
        }
    }

    /// <summary>執行期建立 URP Unlit／Lit 材質（shader 已列在 Always Included，見 ProjectConfigurator）。</summary>
    public static class RuntimeMaterials
    {
        static Shader _unlit, _lit;

        public static Material Lit(Color color, float smoothness, Texture texture = null, Vector2 tiling = default)
        {
            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(_lit != null ? _lit : Shader.Find("Standard"));
            m.SetColor("_BaseColor", color);
            m.color = color;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (texture != null)
            {
                m.SetTexture("_BaseMap", texture);
                m.SetTextureScale("_BaseMap", tiling == default ? Vector2.one : tiling);
            }
            return m;
        }

        public static Material Unlit(Color color)
        {
            if (_unlit == null) _unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(_unlit != null ? _unlit : Shader.Find("Unlit/Color"));
            m.SetColor("_BaseColor", color);
            m.color = color;
            return m;
        }
    }
}
