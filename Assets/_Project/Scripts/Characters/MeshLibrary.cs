using System.Collections.Generic;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>程式產生的網格：內建幾何體快取、管狀曲線、旋轉體、組合多材質網格。全部執行期建立一次後重用。</summary>
    public static class MeshLibrary
    {
        static Mesh _sphere, _capsule, _cylinder, _cube;
        static readonly Dictionary<string, Mesh> Named = new Dictionary<string, Mesh>();

        public static Mesh Sphere => _sphere != null ? _sphere : (_sphere = Primitive(PrimitiveType.Sphere));
        public static Mesh Capsule => _capsule != null ? _capsule : (_capsule = Primitive(PrimitiveType.Capsule));
        public static Mesh Cylinder => _cylinder != null ? _cylinder : (_cylinder = Primitive(PrimitiveType.Cylinder));
        public static Mesh Cube => _cube != null ? _cube : (_cube = Primitive(PrimitiveType.Cube));

        static Mesh Primitive(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Util.SafeDestroy(go);
            return m;
        }

        /// <summary>以名稱快取：同一種形狀（例如嘴型）所有角色共用。</summary>
        public static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (Named.TryGetValue(key, out var m) && m != null) return m;
            m = make();
            m.name = key;
            Named[key] = m;
            return m;
        }

        /// <summary>沿折線的圓管，兩端是半球。radii 為 null 時用固定半徑。</summary>
        public static Mesh Tube(IList<Vector3> pts, float radius, float[] radii = null, int sides = 8)
        {
            var centers = new List<Vector3>();
            var rs = new List<float>();
            var tans = new List<Vector3>();
            int n = pts.Count;
            Vector3 Tan(int i) => (pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            float R(int i) => radii != null ? radii[i] : radius;

            // 起點半球
            var t0 = Tan(0);
            for (int k = 4; k >= 1; k--)
            {
                float a = k / 4f * 90f * Mathf.Deg2Rad;
                centers.Add(pts[0] - t0 * (R(0) * Mathf.Sin(a)));
                rs.Add(R(0) * Mathf.Cos(a));
                tans.Add(t0);
            }
            for (int i = 0; i < n; i++) { centers.Add(pts[i]); rs.Add(R(i)); tans.Add(Tan(i)); }
            var t1 = Tan(n - 1);
            for (int k = 1; k <= 4; k++)
            {
                float a = k / 4f * 90f * Mathf.Deg2Rad;
                centers.Add(pts[n - 1] + t1 * (R(n - 1) * Mathf.Sin(a)));
                rs.Add(R(n - 1) * Mathf.Cos(a));
                tans.Add(t1);
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 prevN = Vector3.zero;
            for (int i = 0; i < centers.Count; i++)
            {
                var t = tans[i];
                var refAxis = Mathf.Abs(Vector3.Dot(t, Vector3.forward)) < 0.9f ? Vector3.forward : Vector3.up;
                var nrm = i == 0 ? Vector3.Cross(t, refAxis).normalized : Vector3.ProjectOnPlane(prevN, t).normalized;
                if (nrm.sqrMagnitude < 0.5f) nrm = Vector3.Cross(t, refAxis).normalized;
                prevN = nrm;
                var bin = Vector3.Cross(t, nrm);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    verts.Add(centers[i] + (nrm * Mathf.Cos(a) + bin * Mathf.Sin(a)) * rs[i]);
                }
                if (i == 0) continue;
                int b0 = (i - 1) * sides, b1 = i * sides;
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    tris.Add(b0 + s); tris.Add(b1 + s); tris.Add(b1 + s1);
                    tris.Add(b0 + s); tris.Add(b1 + s1); tris.Add(b0 + s1);
                }
            }
            return Finish(verts, tris);
        }

        /// <summary>繞 Y 軸的旋轉體；profile 每點 x = 半徑、y = 高度，由上到下。</summary>
        public static Mesh Lathe(IList<Vector2> profile, int sides = 14)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < profile.Count; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    verts.Add(new Vector3(Mathf.Cos(a) * profile[i].x, profile[i].y, Mathf.Sin(a) * profile[i].x));
                }
                if (i == 0) continue;
                int b0 = (i - 1) * sides, b1 = i * sides;
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    tris.Add(b0 + s); tris.Add(b0 + s1); tris.Add(b1 + s1);
                    tris.Add(b0 + s); tris.Add(b1 + s1); tris.Add(b1 + s);
                }
            }
            return Finish(verts, tris);
        }

        /// <summary>水滴（尖端朝上），高約 2、寬約 1.3，中心在原點。</summary>
        public static Mesh Teardrop() => Cached("Teardrop", () =>
        {
            var prof = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float th = i / 16f * Mathf.PI;
                prof.Add(new Vector2(Mathf.Sin(th) * Mathf.Pow(Mathf.Sin(th / 2f), 1.3f) * 0.75f, Mathf.Cos(th)));
            }
            return Lathe(prof);
        });

        /// <summary>平面多邊形擠出成薄片（正面朝 −Z）。輪廓需對中心呈星形。</summary>
        public static Mesh Slab(IList<Vector2> outline, float depth)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n = outline.Count;
            Vector2 c = Vector2.zero;
            foreach (var p in outline) c += p;
            c /= n;
            // 正面
            verts.Add(new Vector3(c.x, c.y, 0));
            for (int i = 0; i < n; i++) verts.Add(new Vector3(outline[i].x, outline[i].y, 0));
            for (int i = 0; i < n; i++) { tris.Add(0); tris.Add(1 + (i + 1) % n); tris.Add(1 + i); }
            // 側面
            int baseF = 1, baseB = verts.Count;
            for (int i = 0; i < n; i++) verts.Add(new Vector3(outline[i].x, outline[i].y, depth));
            for (int i = 0; i < n; i++)
            {
                int i1 = (i + 1) % n;
                tris.Add(baseF + i); tris.Add(baseF + i1); tris.Add(baseB + i1);
                tris.Add(baseF + i); tris.Add(baseB + i1); tris.Add(baseB + i);
            }
            return Finish(verts, tris);
        }

        static Mesh Finish(List<Vector3> verts, List<int> tris)
        {
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>把多個零件依材質合成一個多 submesh 網格：一個 renderer 就能畫整組。</summary>
        public sealed class Composer
        {
            readonly List<Material> _mats = new List<Material>();
            readonly List<List<CombineInstance>> _groups = new List<List<CombineInstance>>();

            public Composer Add(Mesh mesh, Matrix4x4 m, Material mat)
            {
                int i = _mats.IndexOf(mat);
                if (i < 0) { _mats.Add(mat); _groups.Add(new List<CombineInstance>()); i = _mats.Count - 1; }
                _groups[i].Add(new CombineInstance { mesh = mesh, transform = m });
                return this;
            }

            public Composer Add(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material mat) =>
                Add(mesh, Matrix4x4.TRS(pos, rot, scale), mat);

            public bool IsEmpty => _mats.Count == 0;

            public Mesh Build(out Material[] materials)
            {
                var parts = new CombineInstance[_groups.Count];
                for (int g = 0; g < _groups.Count; g++)
                {
                    var gm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    gm.CombineMeshes(_groups[g].ToArray(), true, true);
                    parts[g] = new CombineInstance { mesh = gm, transform = Matrix4x4.identity };
                }
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(parts, false, true);
                foreach (var p in parts) Util.SafeDestroy(p.mesh);
                mesh.RecalculateBounds();
                materials = _mats.ToArray();
                return mesh;
            }
        }
    }

    static class Util
    {
        public static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>與幀率無關的指數趨近。</summary>
        public static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(target, current, Mathf.Exp(-rate * dt));

        public static Vector3 Damp(Vector3 current, Vector3 target, float rate, float dt) =>
            Vector3.Lerp(target, current, Mathf.Exp(-rate * dt));

        public static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        /// <summary>0→1→0 的鐘形，用在動作的起落。</summary>
        public static float Bell(float t, float rise, float fall)
        {
            if (t <= 0 || t >= 1) return 0;
            if (t < rise) return Smooth01(t / rise);
            if (t > 1 - fall) return Smooth01((1 - t) / fall);
            return 1;
        }
    }
}
