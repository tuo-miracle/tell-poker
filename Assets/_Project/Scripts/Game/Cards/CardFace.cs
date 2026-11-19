using System.Collections.Generic;
using Poker.Core;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 執行期畫牌面貼圖：花色用幾何形狀（♠♥♦♣），點數用簡單的筆畫字型，四個角都有點數＋花色。
    /// 上半兩角正立、下半兩角轉 180°（和真牌一樣），所以掀起靠近玩家那端時看到的角標是正的。
    /// 每張牌畫一次並快取。
    /// </summary>
    public static class CardFace
    {
        public const int TextureWidth = 200, TextureHeight = 280;
        public static readonly Color Paper = new Color(0.97f, 0.96f, 0.93f);
        public static readonly Color Red = new Color(0.80f, 0.08f, 0.12f);
        public static readonly Color Black = new Color(0.08f, 0.08f, 0.10f);

        static readonly Dictionary<int, Texture2D> Cache = new Dictionary<int, Texture2D>();
        static Texture2D _back;

        public static string SuitSymbol(Suit s)
        {
            switch (s)
            {
                case Suit.Spades: return "♠";
                case Suit.Hearts: return "♥";
                case Suit.Diamonds: return "♦";
                default: return "♣";
            }
        }

        public static string RankLabel(int rank)
        {
            switch (rank)
            {
                case 14: return "A";
                case 13: return "K";
                case 12: return "Q";
                case 11: return "J";
                default: return rank.ToString();
            }
        }

        public static string Label(Card c) => RankLabel(c.Rank) + SuitSymbol(c.Suit);

        public static bool IsRed(Suit s) => s == Suit.Hearts || s == Suit.Diamonds;

        /// <summary>花色形狀在單位方框內的覆蓋（u 往右、v 往上，0–1）。回傳 0 或 1。</summary>
        public static float SuitCoverage(Suit s, float u, float v)
        {
            switch (s)
            {
                case Suit.Diamonds:
                    return Mathf.Abs(u - 0.5f) / 0.36f + Mathf.Abs(v - 0.5f) / 0.48f <= 1f ? 1f : 0f;
                case Suit.Hearts:
                    return Circle(u, v, 0.30f, 0.66f, 0.205f) || Circle(u, v, 0.70f, 0.66f, 0.205f) ||
                           Tri(u, v, 0.105f, 0.60f, 0.895f, 0.60f, 0.5f, 0.06f) ? 1f : 0f;
                case Suit.Spades:
                    return Circle(u, v, 0.31f, 0.42f, 0.195f) || Circle(u, v, 0.69f, 0.42f, 0.195f) ||
                           Tri(u, v, 0.12f, 0.47f, 0.88f, 0.47f, 0.5f, 0.96f) ||
                           Tri(u, v, 0.5f, 0.42f, 0.35f, 0.04f, 0.65f, 0.04f) ? 1f : 0f;
                default: // Clubs
                    return Circle(u, v, 0.5f, 0.71f, 0.185f) || Circle(u, v, 0.29f, 0.43f, 0.185f) ||
                           Circle(u, v, 0.71f, 0.43f, 0.185f) || Circle(u, v, 0.5f, 0.5f, 0.11f) ||
                           Tri(u, v, 0.5f, 0.5f, 0.35f, 0.04f, 0.65f, 0.04f) ? 1f : 0f;
            }
        }

        public static Texture2D Get(Card c)
        {
            if (Cache.TryGetValue(c.Index, out var t) && t != null) return t;
            t = Render(c);
            Cache[c.Index] = t;
            return t;
        }

        /// <summary>牌背：深色底＋細框＋菱格紋。</summary>
        public static Texture2D Back
        {
            get
            {
                if (_back != null) return _back;
                var px = new Color32[TextureWidth * TextureHeight];
                var bg = new Color(0.55f, 0.10f, 0.16f);
                var line = new Color(0.95f, 0.88f, 0.78f);
                for (int y = 0; y < TextureHeight; y++)
                for (int x = 0; x < TextureWidth; x++)
                {
                    float u = x / (float)TextureWidth, v = y / (float)TextureHeight;
                    bool border = u < 0.05f || u > 0.95f || v < 0.035f || v > 0.965f;
                    bool frame = !border && (u < 0.075f || u > 0.925f || v < 0.055f || v > 0.945f);
                    float d = Mathf.Repeat((x + y) / 14f, 1f), e = Mathf.Repeat((x - y) / 14f, 1f);
                    bool lattice = d < 0.16f || e < 0.16f;
                    Color col = border ? Paper : frame ? bg : lattice ? Color.Lerp(bg, line, 0.45f) : bg;
                    px[y * TextureWidth + x] = col;
                }
                _back = MakeTexture("CardBack", px);
                return _back;
            }
        }

        /// <summary>矩形區域（UV）裡非紙色的比例，測試用。</summary>
        public static float InkFraction(Texture2D t, Rect uv)
        {
            int n = 0, ink = 0;
            for (float v = uv.yMin; v < uv.yMax; v += 0.01f)
            for (float u = uv.xMin; u < uv.xMax; u += 0.01f)
            {
                var c = t.GetPixelBilinear(u, v);
                n++;
                if (Mathf.Abs(c.r - Paper.r) + Mathf.Abs(c.g - Paper.g) + Mathf.Abs(c.b - Paper.b) > 0.3f) ink++;
            }
            return n == 0 ? 0f : (float)ink / n;
        }

        static Texture2D Render(Card c)
        {
            const int W = TextureWidth, H = TextureHeight;
            var ink = IsRed(c.Suit) ? Red : Black;
            var cov = new float[W * H];
            string rank = RankLabel(c.Rank);

            // 角標：點數高 36 px、花色 28 px；四個角（下半兩個轉 180°）
            float rankH = 36f, suitH = 28f, margin = 9f;
            foreach (bool top in new[] { true, false })
            foreach (bool left in new[] { true, false })
            {
                bool rot = !top;
                float cx = left ? margin + 16f : W - margin - 16f;
                // 正立時 rank 在上、suit 在下；轉 180° 時整組上下左右顛倒
                float rankCy = top ? H - margin - rankH * 0.5f : margin + rankH * 0.5f;
                float suitCy = top ? rankCy - rankH * 0.5f - 4f - suitH * 0.5f : rankCy + rankH * 0.5f + 4f + suitH * 0.5f;
                DrawRank(cov, rank, cx, rankCy, rankH, rot);
                DrawSuit(cov, c.Suit, cx, suitCy, suitH, rot);
            }

            // 中央大花色（人頭牌另加一個小框，簡化處理）
            DrawSuit(cov, c.Suit, W * 0.5f, H * 0.5f, 92f, false);
            if (c.Rank >= 11 && c.Rank <= 13)
                DrawRank(cov, rank, W * 0.5f, H * 0.5f - 72f, 30f, false);

            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = Color.Lerp(Paper, ink, cov[i]);
            // 細灰邊，讓白牌在淺色桌面上也看得出輪廓
            for (int x = 0; x < W; x++) { px[x] = Color.gray; px[(H - 1) * W + x] = Color.gray; }
            for (int y = 0; y < H; y++) { px[y * W] = Color.gray; px[y * W + W - 1] = Color.gray; }
            return MakeTexture("Card_" + c, px);
        }

        static Texture2D MakeTexture(string name, Color32[] px)
        {
            var t = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        // ---- 花色與字型的光柵化（3×3 超取樣抗鋸齒） ----

        // 字形覆蓋圖快取：同一個花色／點數在同一尺寸只算一次，之後直接蓋章（發牌時不卡頓）
        static readonly Dictionary<string, Stamp> Stamps = new Dictionary<string, Stamp>();

        sealed class Stamp { public int W, H; public float[] Cov; }

        static void DrawSuit(float[] cov, Suit s, float cx, float cy, float size, bool rot)
        {
            var key = "s" + (int)s + "_" + size;
            if (!Stamps.TryGetValue(key, out var st))
            {
                int n = Mathf.CeilToInt(size) + 2;
                st = new Stamp { W = n, H = n, Cov = new float[n * n] };
                RasterLocal(st, (x, y) => SuitCoverage(s, (x - 1f) / size, (y - 1f) / size) > 0.5f);
                Stamps[key] = st;
            }
            Blit(cov, st, cx, cy, rot);
        }

        static void DrawRank(float[] cov, string rank, float cx, float cy, float height, bool rot)
        {
            var key = "r" + rank + "_" + height;
            if (!Stamps.TryGetValue(key, out var st))
            {
                var strokes = new List<Vector2[]>();
                float advance = 0f;
                foreach (char ch in rank)
                {
                    foreach (var g in Glyph(ch))
                    {
                        var moved = new Vector2[g.Length];
                        for (int i = 0; i < g.Length; i++) moved[i] = g[i] + new Vector2(advance, 0);
                        strokes.Add(moved);
                    }
                    advance += ch == '1' ? 0.42f : 0.78f;
                }
                float glyphW = advance - 0.18f;
                float squeeze = rank.Length > 1 ? 0.78f : 1f; // 「10」橫向壓扁，寬度和單字差不多
                float scale = height, stroke = 0.075f * scale;
                int pad = Mathf.CeilToInt(stroke) + 2;
                int w = Mathf.CeilToInt(glyphW * scale * squeeze) + 2 * pad, h = Mathf.CeilToInt(height) + 2 * pad;
                st = new Stamp { W = w, H = h, Cov = new float[w * h] };
                RasterLocal(st, (x, y) =>
                {
                    var p = new Vector2((x - pad) / (scale * squeeze), (y - pad) / scale);
                    foreach (var sg in strokes)
                        for (int i = 0; i + 1 < sg.Length; i++)
                            if (SegDist(p, sg[i], sg[i + 1]) * scale * squeeze <= stroke) return true;
                    return false;
                });
                Stamps[key] = st;
            }
            Blit(cov, st, cx, cy, rot);
        }

        static void RasterLocal(Stamp st, System.Func<float, float, bool> inside)
        {
            for (int y = 0; y < st.H; y++)
            for (int x = 0; x < st.W; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                    if (inside(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hit++;
                st.Cov[y * st.W + x] = hit / 9f;
            }
        }

        /// <summary>以 (cx, cy) 為中心蓋章；rot 時轉 180°。</summary>
        static void Blit(float[] cov, Stamp st, float cx, float cy, bool rot)
        {
            int ox = Mathf.RoundToInt(cx - st.W * 0.5f), oy = Mathf.RoundToInt(cy - st.H * 0.5f);
            for (int y = 0; y < st.H; y++)
            for (int x = 0; x < st.W; x++)
            {
                float c = rot ? st.Cov[(st.H - 1 - y) * st.W + (st.W - 1 - x)] : st.Cov[y * st.W + x];
                if (c <= 0f) continue;
                int tx = ox + x, ty = oy + y;
                if (tx < 0 || ty < 0 || tx >= TextureWidth || ty >= TextureHeight) continue;
                int i = ty * TextureWidth + tx;
                cov[i] = Mathf.Max(cov[i], c);
            }
        }

        static bool Circle(float u, float v, float cx, float cy, float r) => (u - cx) * (u - cx) + (v - cy) * (v - cy) <= r * r;

        static bool Tri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ---- 筆畫字型：字框寬 0.6、高 1（y 往上） ----

        static Vector2[] Arc(float cx, float cy, float rx, float ry, float a0, float a1, int steps = 14)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                pts[i] = new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a));
            }
            return pts;
        }

        static Vector2[] P(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            return pts;
        }

        static Vector2[] Join(params Vector2[][] parts)
        {
            var list = new List<Vector2>();
            foreach (var p in parts) list.AddRange(p);
            return list.ToArray();
        }

        static Vector2[][] Glyph(char ch)
        {
            switch (ch)
            {
                case 'A': return new[] { P(0.02f, 0f, 0.3f, 1f, 0.58f, 0f), P(0.12f, 0.33f, 0.48f, 0.33f) };
                case 'K': return new[] { P(0.04f, 0f, 0.04f, 1f), P(0.56f, 1f, 0.04f, 0.42f), P(0.2f, 0.58f, 0.58f, 0f) };
                case 'Q': return new[] { Arc(0.3f, 0.5f, 0.27f, 0.47f, 0f, 360f, 28), P(0.34f, 0.24f, 0.6f, -0.04f) };
                case 'J': return new[] { P(0.12f, 1f, 0.56f, 1f), Join(P(0.44f, 1f, 0.44f, 0.28f), Arc(0.24f, 0.28f, 0.2f, 0.24f, 0f, -180f)) };
                case '1': return new[] { P(0.0f, 0.8f, 0.2f, 1f, 0.2f, 0f) };
                case '0': return new[] { Arc(0.3f, 0.5f, 0.26f, 0.47f, 0f, 360f, 28) };
                case '2': return new[] { Join(Arc(0.3f, 0.71f, 0.26f, 0.26f, 160f, -35f), P(0.03f, 0f, 0.58f, 0f)) };
                case '3': return new[] { Arc(0.28f, 0.75f, 0.25f, 0.24f, 150f, -90f), Arc(0.28f, 0.27f, 0.29f, 0.27f, 90f, -150f) };
                case '4': return new[] { P(0.46f, 0f, 0.46f, 1f, 0.02f, 0.3f, 0.6f, 0.3f) };
                case '5': return new[] { Join(P(0.54f, 1f, 0.1f, 1f, 0.06f, 0.56f), Arc(0.29f, 0.32f, 0.28f, 0.31f, 125f, -150f)) };
                case '6': return new[] { Join(P(0.5f, 0.98f), Arc(0.56f, 0.3f, 0.52f, 0.68f, 100f, 180f, 10)), Arc(0.31f, 0.29f, 0.27f, 0.29f, 180f, -180f, 26) };
                case '7': return new[] { P(0.02f, 1f, 0.58f, 1f, 0.2f, 0f) };
                case '8': return new[] { Arc(0.3f, 0.76f, 0.22f, 0.23f, 0f, 360f, 24), Arc(0.3f, 0.27f, 0.28f, 0.27f, 0f, 360f, 24) };
                case '9':
                {
                    var six = Glyph('6');
                    var res = new Vector2[six.Length][];
                    for (int k = 0; k < six.Length; k++)
                    {
                        res[k] = new Vector2[six[k].Length];
                        for (int i = 0; i < six[k].Length; i++) res[k][i] = new Vector2(0.6f - six[k][i].x, 1f - six[k][i].y);
                    }
                    return res;
                }
                default: return new Vector2[0][];
            }
        }
    }
}
