using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 真實房間裡會擋到角色的東西，換算成牌桌座標的俯視 2D（Vector2.x = 牌桌 X、Vector2.y = 牌桌 Z）：
    /// 牆面（線段）、地板輪廓（多邊形，外面就是牆後）、家具量體（凸多邊形加上下高度，高度是相對桌面的牌桌 Y）。
    /// 由 TablePlacer 從 MRUK 房間建立（FromWorld），SeatingPlanner 用它判斷座位能不能坐。純計算，不碰 SDK。
    /// </summary>
    public sealed class RoomObstacles
    {
        struct Volume
        {
            public Vector2[] Hull;
            public float Bottom, Top;
            public string Label;
        }

        readonly List<(Vector2 a, Vector2 b)> _walls = new List<(Vector2, Vector2)>();
        readonly List<Volume> _volumes = new List<Volume>();
        Vector2[] _outline;

        public int WallCount => _walls.Count;
        public int VolumeCount => _volumes.Count;
        public bool HasOutline => _outline != null;

        /// <summary>地板輪廓（任意方向的簡單多邊形）；少於 3 點等於不知道。</summary>
        public void SetOutline(IReadOnlyList<Vector2> outline)
        {
            if (outline == null || outline.Count < 3) { _outline = null; return; }
            _outline = new Vector2[outline.Count];
            for (int i = 0; i < outline.Count; i++) _outline[i] = outline[i];
        }

        public void AddWall(Vector2 a, Vector2 b) => _walls.Add((a, b));

        /// <summary>
        /// 家具量體：俯視的角點（取凸包）與上下高度（牌桌 Y，桌面 = 0）。
        /// 蓋住絨布中心（feltCenter）的量體就是牌桌底下那張真實桌子，不算障礙。
        /// </summary>
        public void AddVolume(IReadOnlyList<Vector2> footprint, float bottom, float top, string label, Vector2? feltCenter = null)
        {
            if (footprint == null || footprint.Count < 3) return;
            var hull = ConvexHull(footprint);
            if (hull.Length < 3) return;
            if (feltCenter.HasValue && InsidePolygon(hull, feltCenter.Value)) return;
            _volumes.Add(new Volume { Hull = hull, Bottom = Mathf.Min(bottom, top), Top = Mathf.Max(bottom, top), Label = string.IsNullOrEmpty(label) ? "volume" : label });
        }

        /// <summary>
        /// 半徑 radius 的圓（角色身體的俯視範圍，高度 bodyBottom–bodyTop）撞到什麼：
        /// "outside"（在地板輪廓外＝牆後）、"wall"、家具標籤；沒撞到回 null。
        /// </summary>
        public string BlockedBy(Vector2 p, float radius, float bodyBottom, float bodyTop)
        {
            if (_outline != null)
            {
                if (!InsidePolygon(_outline, p)) return "outside";
                for (int i = 0; i < _outline.Length; i++)
                    if (DistanceToSegment(p, _outline[i], _outline[(i + 1) % _outline.Length]) < radius) return "wall";
            }
            foreach (var w in _walls)
                if (DistanceToSegment(p, w.a, w.b) < radius) return "wall";
            foreach (var v in _volumes)
            {
                if (v.Top < bodyBottom || v.Bottom > bodyTop) continue;   // 比桌面低的床、矮櫃：角色的身體在桌面以上，不會穿模
                if (InsidePolygon(v.Hull, p)) return v.Label;
                for (int i = 0; i < v.Hull.Length; i++)
                    if (DistanceToSegment(p, v.Hull[i], v.Hull[(i + 1) % v.Hull.Length]) < radius) return v.Label;
            }
            return null;
        }

        /// <summary>
        /// 從世界座標建立：地板輪廓角點、牆面底邊線段、量體的 8 個角點（含標籤），用 worldToTable 換成牌桌座標。
        /// 高度只看量體，牆一律視為從地板到天花板。
        /// </summary>
        public static RoomObstacles FromWorld(IReadOnlyList<Vector3> outlineWorld, IEnumerable<(Vector3 a, Vector3 b)> wallsWorld,
            IEnumerable<(IReadOnlyList<Vector3> corners, string label)> volumesWorld, Func<Vector3, Vector3> worldToTable, Vector2 feltCenter)
        {
            var room = new RoomObstacles();
            Vector2 Flat(Vector3 w) { var t = worldToTable(w); return new Vector2(t.x, t.z); }
            if (outlineWorld != null && outlineWorld.Count >= 3)
            {
                var o = new Vector2[outlineWorld.Count];
                for (int i = 0; i < o.Length; i++) o[i] = Flat(outlineWorld[i]);
                room.SetOutline(o);
            }
            if (wallsWorld != null)
                foreach (var (a, b) in wallsWorld) room.AddWall(Flat(a), Flat(b));
            if (volumesWorld != null)
                foreach (var (corners, label) in volumesWorld)
                {
                    if (corners == null || corners.Count < 3) continue;
                    var foot = new Vector2[corners.Count];
                    float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                    for (int i = 0; i < corners.Count; i++)
                    {
                        var t = worldToTable(corners[i]);
                        foot[i] = new Vector2(t.x, t.z);
                        lo = Mathf.Min(lo, t.y);
                        hi = Mathf.Max(hi, t.y);
                    }
                    room.AddVolume(foot, lo, hi, label, feltCenter);
                }
            return room;
        }

        // ---- 2D 幾何 ----

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>射線法（任意簡單多邊形，順逆時針皆可）。</summary>
        static bool InsidePolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Andrew monotone chain；回傳逆時針凸包。</summary>
        static Vector2[] ConvexHull(IReadOnlyList<Vector2> points)
        {
            var pts = new List<Vector2>(points);
            pts.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-9f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull.ToArray();
        }
    }
}
