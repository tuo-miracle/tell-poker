using System;
using System.Collections.Generic;
using Poker.Characters;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Game
{
    /// <summary>
    /// 半透明的示範手：循環播放一段烘焙好的手勢片段（牌桌座標），旁邊一行英文字幕。
    /// Root 掛在牌桌座標系底下（本地座標 = 牌桌座標），所以片段在哪張桌子上都對得到牌與籌碼堆。
    /// 外觀（2026-10-14 打磨，舊版每節骨頭一個方塊，「拿起牌看」看起來像一根柱子）：手指是兩端圓頭的八角管、手掌是圓角厚片、
    /// 手腕往前臂短短一截淡出；Poker/GhostHand 材質＝柔和半透明＋邊緣光（fresnel），先寫深度再上色，重疊處不疊色。
    /// 便宜：兩隻手合成一個網格（約 1,150 頂點）＝ 1 個 renderer、2 個 pass；字幕 2 個；每幀只改頂點與法線，不配置記憶體。
    /// 片段頭尾用 smoothstep 淡入淡出（循環接縫看不到跳動），Stop 淡出、Hide 立刻消失。
    /// 字幕貼著示範的那隻手（GhostCaptionPlacement：貼桌、在手前面或旁邊，不蓋住牌、籌碼、手的動作與角色）。
    /// </summary>
    public sealed class GhostHands : IDisposable
    {
        public const float BaseAlpha = 0.62f, FadeSeconds = 0.3f;
        const float EdgeIn = 0.4f, EdgeOut = 0.45f, CaptionCharSize = 0.0026f;
        static readonly Color Tint = new Color(0.55f, 0.92f, 1f);          // 和教學發光同一個青色系
        static readonly Color Rim = new Color(0.86f, 1f, 1f);
        static readonly Color CaptionPlate = new Color(0.98f, 0.95f, 0.86f); // 和下注標籤第二行同一個底色
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public const string ShaderName = "Poker/GhostHand";

        /// <summary>手指骨頭（起點、終點、兩端半徑）。掌骨由手掌代表；最後一節是手腕往前臂的一小截（淡出）。</summary>
        static readonly (HandJoint a, HandJoint b, float r0, float r1)[] Bones =
        {
            (HandJoint.ThumbMetacarpal, HandJoint.ThumbProximal, 0.0115f, 0.010f),
            (HandJoint.ThumbProximal, HandJoint.ThumbDistal, 0.010f, 0.009f),
            (HandJoint.ThumbDistal, HandJoint.ThumbTip, 0.009f, 0.0075f),
            (HandJoint.IndexProximal, HandJoint.IndexIntermediate, 0.0095f, 0.0085f),
            (HandJoint.IndexIntermediate, HandJoint.IndexDistal, 0.0085f, 0.0075f),
            (HandJoint.IndexDistal, HandJoint.IndexTip, 0.0075f, 0.0065f),
            (HandJoint.MiddleProximal, HandJoint.MiddleIntermediate, 0.0095f, 0.0085f),
            (HandJoint.MiddleIntermediate, HandJoint.MiddleDistal, 0.0085f, 0.0075f),
            (HandJoint.MiddleDistal, HandJoint.MiddleTip, 0.0075f, 0.0065f),
            (HandJoint.RingProximal, HandJoint.RingIntermediate, 0.009f, 0.008f),
            (HandJoint.RingIntermediate, HandJoint.RingDistal, 0.008f, 0.007f),
            (HandJoint.RingDistal, HandJoint.RingTip, 0.007f, 0.006f),
            (HandJoint.LittleProximal, HandJoint.LittleIntermediate, 0.008f, 0.007f),
            (HandJoint.LittleIntermediate, HandJoint.LittleDistal, 0.007f, 0.006f),
            (HandJoint.LittleDistal, HandJoint.LittleTip, 0.006f, 0.0055f),
        };

        // 管子：S 邊，四圈（起點圓頭、起點、終點、終點圓頭）＋兩個尖端
        const int Sides = 8;
        const int TubeVerts = Sides * 4 + 2;
        const int Tubes = 15 + 1;                 // 骨頭＋前臂
        // 手掌：外框 PalmRim 點；上內圈、外框、下內圈＋上下中心
        const int PalmRim = 10;
        const int PalmVerts = PalmRim * 3 + 2;
        const int VertsPerHand = Tubes * TubeVerts + PalmVerts;
        const float ForearmLength = 0.05f, ForearmRadius = 0.021f, PalmHalfThickness = 0.011f;
        static readonly float[] Cos = new float[Sides], Sin = new float[Sides];

        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly Material _mat;
        readonly Vector3[] _verts = new Vector3[VertsPerHand * 2];
        readonly Vector3[] _normals = new Vector3[VertsPerHand * 2];
        readonly Vector3[] _rim = new Vector3[PalmRim];
        readonly HandsFrame _frame = new HandsFrame();
        readonly bool[] _drawn = new bool[2];
        HandRecording _clip;
        HandRecordingPlayer _player;
        double _t, _period;
        float _fade, _alphaShown = -1f;
        bool _stopping;
        Vector3 _captionAt;
        Bounds _handRegion;
        Bounds[] _handRegions, _handSamples;

        static GhostHands()
        {
            for (int k = 0; k < Sides; k++)
            {
                float a = k * Mathf.PI * 2f / Sides;
                Cos[k] = Mathf.Cos(a);
                Sin[k] = Mathf.Sin(a);
            }
        }

        public GhostHands(Transform parent, string name = "GhostHands")
        {
            _go = new GameObject(name);
            _go.transform.SetParent(parent, false);
            Root = _go.transform;

            var meshGo = new GameObject("GhostMesh");
            meshGo.transform.SetParent(Root, false);
            _mesh = new Mesh { name = "GhostHandsMesh" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.colors32 = BuildColors();
            _mesh.triangles = BuildTriangles();
            // 牌桌附近一個固定的大範圍：不用每幀重算邊界
            _mesh.bounds = new Bounds(new Vector3(0f, 0.3f, 0.3f), new Vector3(2f, 1.2f, 2f));
            meshGo.AddComponent<MeshFilter>().sharedMesh = _mesh;
            Renderer = meshGo.AddComponent<MeshRenderer>();
            _mat = CreateMaterial();
            Renderer.sharedMaterial = _mat;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.enabled = false;

            Caption = new WorldLabel(Root, "GhostCaption", CaptionCharSize, CaptionPlate);
        }

        static Material CreateMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                // 找不到（不該發生：shader 在 Resources）就退回字幕的半透明 Unlit
                Debug.LogWarning("[Ghost] missing shader " + ShaderName);
                return new Material(MaterialCache.Text(Texture2D.whiteTexture, Tint)) { name = "GhostHands" };
            }
            var m = new Material(shader) { name = "GhostHands" };
            m.SetColor(BaseColorId, Tint);
            m.SetColor("_RimColor", Rim);
            return m;
        }

        public Transform Root { get; }
        public MeshRenderer Renderer { get; }
        public WorldLabel Caption { get; }
        /// <summary>正在示範（含淡出中）的手勢；沒有 = null。</summary>
        public GhostGesture? Current { get; private set; }
        public bool IsVisible => Current != null && _fade > 0f;
        public float Alpha { get; private set; }
        /// <summary>從 Play 起播完了幾輪。</summary>
        public int LoopsCompleted => _period > 0 ? (int)Math.Floor(_t / _period) : 0;
        /// <summary>這一輪播到第幾秒。</summary>
        public double PlaybackTime => _period > 0 ? _t - LoopsCompleted * _period : _t;
        /// <summary>一輪的長度（秒）。Talk 沒有片段 = 0。</summary>
        public double ClipDuration => _period;
        /// <summary>片段裡畫出來的手掌平均位置（牌桌座標）；選單示範時用來擺到玩家面前。</summary>
        public Vector3 ClipCenter { get; private set; }
        /// <summary>
        /// Talk（沒有手）的字幕位置（牌桌座標）：貼桌、在玩家的牌和下注線之間（2026-10-14 實機：浮在半空的字從低眼位看會蓋到對面的角色）；字高約 1.4°。
        /// </summary>
        public Vector3 TalkAnchor = new Vector3(0f, 0.03f, 0.25f);
        /// <summary>教學讀 Rocco（Gaze，沒有手）的字幕位置：桌面上方低處，不擋住要讀的那張臉。</summary>
        public Vector3 GazeAnchor = new Vector3(0f, 0.03f, 0.25f);
        /// <summary>字幕原文（畫的時候長句會折成兩行）。</summary>
        public string CaptionText => Current != null && Caption.Go.activeSelf ? _captionRaw : "";
        /// <summary>一行最多幾個字（長句折成兩行：字幕不會寬到超出舒適視野）。示範手的字幕會再試多折一兩行（PlaceCaption）。</summary>
        public const int CaptionWrap = 24;
        string _captionRaw = "";
        /// <summary>字幕旁邊要避開的東西（牌桌座標）：預設是玩家的牌、籌碼堆、公共牌、底池。</summary>
        public IList<Bounds> CaptionObstacles
        {
            get => _captionObstacles;
            set
            {
                if (ReferenceEquals(_captionObstacles, value)) return;
                _captionObstacles = value;
                if (Current != null && _clip != null) PlaceCaption();   // 正在示範：馬上換位置
            }
        }
        IList<Bounds> _captionObstacles = GhostCaptionPlacement.DefaultObstacles(TableDimensions.Layout);
        /// <summary>角色可能坐的區域（牌桌座標）：字幕從任何眼睛位置看都不能蓋到這裡（2026-10-14 實機：字幕浮在 Dot、Vex 身上）。</summary>
        public IList<Bounds> CaptionSeatZones = GhostCaptionPlacement.SeatZones(TableDimensions.Layout);

        float _captionScale = 1f;
        /// <summary>字幕大小倍率（UX 的 Text size）。字變大時重新找位置（才不會蓋到手或牌）。</summary>
        public float CaptionScale
        {
            get => _captionScale;
            set
            {
                _captionScale = Mathf.Max(0.1f, value);
                Caption.Go.transform.localScale = Vector3.one * _captionScale;
                if (Current == null) return;
                Caption.Show(Wrap(_captionRaw, WrapWidth), _captionAt);
                if (_clip != null) PlaceCaption();
            }
        }

        /// <summary>開始（或換成）示範某個手勢；同一個手勢正在播就只取消淡出。</summary>
        public void Play(GhostGesture g)
        {
            if (Current == g && !_stopping) return;
            bool same = Current == g;
            Current = g;
            _stopping = false;
            if (same) return;
            _t = 0;
            _clip = GhostCatalog.Load(g);
            if (_clip != null && _clip.Frames.Count > 1)
            {
                _player = new HandRecordingPlayer(_clip, loop: true);
                int n = _clip.Frames.Count;
                _period = _player.Duration + _player.Duration / (n - 1);
                var m = Measure(_clip);
                ClipCenter = m.center;
                _handRegion = m.region;
                _handRegions = MeasureHands(_clip);
                _handSamples = SampleHands(_clip);
                _captionRaw = GhostCatalog.Caption(g);
                Caption.Show(Wrap(_captionRaw, WrapWidth), m.center);
                PlaceCaption();
            }
            else
            {
                _clip = null;
                _player = null;
                _period = 0;
                ClipCenter = g == GhostGesture.Gaze ? GazeAnchor : TalkAnchor;
                _captionAt = ClipCenter;
                CaptionFlat = false;
                _captionRaw = GhostCatalog.Caption(g);
                Caption.Show(Wrap(_captionRaw, WrapWidth), _captionAt);
            }
        }

        /// <summary>
        /// 字幕貼著手（字幕大小已含倍率）：一行、兩行、三行各試一次（窄高的字幕放得進牌旁邊、寬扁的放得進牌前面），
        /// 挑「沒有違規、分數最好」的那個；同分時行數少的優先。
        /// </summary>
        void PlaceCaption()
        {
            // 同一段片段、同一句、同一個字幕大小、同一組障礙物：結果一樣（第一次約 20 ms，之後不用再算）
            var key = (_clip, _captionRaw, _captionScale, CaptionObstacles, CaptionSeatZones);
            if (PlacedCache.TryGetValue(key, out var cached))
            {
                _captionAt = cached.spot.At;
                CaptionFlat = cached.spot.Flat;
                CaptionFits = cached.ok;
                Caption.Show(cached.text, _captionAt);
                return;
            }
            string bestText = null;
            var best = default(GhostCaptionPlacement.Spot);
            float bestScore = float.MaxValue;
            bool bestOk = false;
            int len = _captionRaw.Length, minLines = Mathf.Max(1, Mathf.CeilToInt(len / (float)CaptionWrap));
            string prev = null;
            for (int n = minLines; n <= minLines + 2; n++)
            {
                var text = WrapLines(_captionRaw, n);
                if (text == prev) continue;
                prev = text;
                var size = WorldLabel.PlateSize(text, Caption.LineHeight) * _captionScale;
                bool ok = GhostCaptionPlacement.TryPlace(GhostCaptionPlacement.SeatEye, _handRegions, size.x, size.y, Caption.LineHeight * _captionScale,
                                                         CaptionObstacles, CaptionSeatZones, out var spot, out float score, _handSamples,
                                                         GhostCaptionPlacement.MaxDistance * Mathf.Max(1f, _captionScale));
                if (bestText == null || (ok && !bestOk) || (ok == bestOk && score < bestScore - 1e-4f))
                {
                    bestText = text; best = spot; bestScore = score; bestOk = ok;
                }
            }
            _captionAt = best.At;
            CaptionFlat = best.Flat;
            CaptionFits = bestOk;
            PlacedCache[key] = (bestText, best, bestOk);
            if (!bestOk)
            {
                var size = WorldLabel.PlateSize(bestText, Caption.LineHeight) * _captionScale;
                Debug.LogWarning($"[Ghost] caption \"{_captionRaw}\" x{_captionScale:0.##}: no clean spot, least-bad at {best.At:F3}{(best.Flat ? " flat" : "")}: " +
                                 GhostCaptionPlacement.Explain(GhostCaptionPlacement.SeatEye, best, _handRegions, size.x, size.y, Caption.LineHeight * _captionScale,
                                                               CaptionObstacles, CaptionSeatZones, GhostCaptionPlacement.MaxDistance * Mathf.Max(1f, _captionScale)));
            }
            Caption.Show(bestText, _captionAt);
        }

        static readonly Dictionary<(HandRecording, string, float, IList<Bounds>, IList<Bounds>), (string text, GhostCaptionPlacement.Spot spot, bool ok)> PlacedCache =
            new Dictionary<(HandRecording, string, float, IList<Bounds>, IList<Bounds>), (string, GhostCaptionPlacement.Spot, bool)>();

        /// <summary>字幕找到了完全不違規的位置（false＝用違規最少的位置，log 會有一行 [Ghost] 警告）。</summary>
        public bool CaptionFits { get; private set; }

        /// <summary>字幕平躺在桌上（不是立著面向玩家）。</summary>
        public bool CaptionFlat { get; private set; }

        /// <summary>超過 width 個字就折行：行數 = ceil(長度 / width)，在最接近等分點的空白折，各行長度差不多。</summary>
        public static string Wrap(string s, int width)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= width) return s;
            int lines = (s.Length + width - 1) / Mathf.Max(1, width);
            var chars = s.ToCharArray();
            int from = 0;
            for (int i = 1; i < lines; i++)
            {
                int target = s.Length * i / lines, best = -1;
                for (int k = from; k < s.Length; k++)
                    if (s[k] == ' ' && (best < 0 || Mathf.Abs(k - target) < Mathf.Abs(best - target))) best = k;
                if (best < 0) break;
                chars[best] = '\n';
                from = best + 1;
            }
            return new string(chars);
        }

        /// <summary>折成最多 lines 行、最長的一行越短越好（逐字排，行寬從平均值往上試）。</summary>
        public static string WrapLines(string s, int lines)
        {
            if (string.IsNullOrEmpty(s) || lines <= 1) return s;
            var words = s.Split(' ');
            for (int width = Mathf.CeilToInt(s.Length / (float)lines); width <= s.Length; width++)
            {
                var sb = new System.Text.StringBuilder();
                int used = 1, run = 0;
                bool fits = true;
                foreach (var w in words)
                {
                    if (w.Length > width) { fits = false; break; }
                    if (run == 0) { sb.Append(w); run = w.Length; continue; }
                    if (run + 1 + w.Length <= width) { sb.Append(' ').Append(w); run += 1 + w.Length; continue; }
                    sb.Append('\n').Append(w);
                    run = w.Length;
                    used++;
                }
                if (fits && used <= lines) return sb.ToString();
            }
            return s;
        }

        /// <summary>字越大一行越少字（字幕寬度大致不變，才放得進舒適視野）。</summary>
        int WrapWidth => Mathf.Max(10, Mathf.RoundToInt(CaptionWrap / Mathf.Max(1f, _captionScale)));

        /// <summary>淡出後停止。</summary>
        public void Stop()
        {
            if (Current != null) _stopping = true;
        }

        /// <summary>立刻消失（例如選單打開）。</summary>
        public void Hide()
        {
            Current = null;
            _stopping = false;
            _fade = 0f;
            Alpha = 0f;
            Renderer.enabled = false;
            Caption.Hide();
        }

        public void Tick(float dt, Vector3? viewerWorld = null)
        {
            if (Current == null) return;
            _fade = Mathf.MoveTowards(_fade, _stopping ? 0f : 1f, dt / FadeSeconds);
            if (_stopping && _fade <= 0f) { Hide(); return; }
            _t += dt;
            if (viewerWorld.HasValue)
            {
                if (CaptionFlat && _player != null) Caption.Lay(viewerWorld.Value);
                else Caption.Face(viewerWorld.Value);
            }
            if (_player == null)
            {
                Renderer.enabled = false;
                Alpha = 0f;
                return;
            }
            double local = PlaybackTime;
            float env = Mathf.SmoothStep(0f, 1f, (float)local / EdgeIn) * Mathf.SmoothStep(0f, 1f, (float)(_period - local) / EdgeOut);
            Alpha = BaseAlpha * Mathf.SmoothStep(0f, 1f, _fade) * env;
            _player.Sample(local, _frame);
            Build();
            if (Mathf.Abs(Alpha - _alphaShown) > 0.004f)
            {
                _alphaShown = Alpha;
                var c = Tint;
                c.a = Alpha;
                _mat.SetColor(BaseColorId, c);
            }
            Renderer.enabled = Alpha > 0.002f;
        }

        /// <summary>目前這一幀某個關節的位置（牌桌座標）；這隻手沒畫出來就回傳 false。</summary>
        public bool TryGetJointLocal(Hand h, HandJoint j, out Vector3 local)
        {
            local = default;
            if (_player == null || Current == null) return false;
            var p = _frame.Get(h);
            if (!p.Tracked) return false;
            local = ToUnity(p.Position(j));
            return true;
        }

        // ───────────── 網格 ─────────────

        static int[] BuildTriangles()
        {
            var tris = new List<int>();
            for (int h = 0; h < 2; h++)
            {
                int v0 = h * VertsPerHand;
                for (int t = 0; t < Tubes; t++) TubeTriangles(tris, v0 + t * TubeVerts);
                PalmTriangles(tris, v0 + Tubes * TubeVerts);
            }
            return tris.ToArray();
        }

        /// <summary>管子：四圈之間三段側面，兩端尖點各一圈扇形。外側朝外（Cull Back）。</summary>
        static void TubeTriangles(List<int> t, int at)
        {
            for (int ring = 0; ring < 3; ring++)
                for (int k = 0; k < Sides; k++)
                {
                    int k1 = (k + 1) % Sides;
                    int a = at + ring * Sides + k, b = at + ring * Sides + k1;
                    int c = at + (ring + 1) * Sides + k, d = at + (ring + 1) * Sides + k1;
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(b); t.Add(c); t.Add(d);
                }
            int tipA = at + Sides * 4, tipB = tipA + 1;
            for (int k = 0; k < Sides; k++)
            {
                int k1 = (k + 1) % Sides;
                t.Add(tipA); t.Add(at + k); t.Add(at + k1);
                t.Add(tipB); t.Add(at + 3 * Sides + k1); t.Add(at + 3 * Sides + k);
            }
        }

        /// <summary>手掌：上內圈扇形、上內圈到外框、外框到下內圈、下內圈扇形。</summary>
        static void PalmTriangles(List<int> t, int at)
        {
            int top = at, mid = at + PalmRim, bot = at + PalmRim * 2, cTop = at + PalmRim * 3, cBot = cTop + 1;
            for (int k = 0; k < PalmRim; k++)
            {
                int k1 = (k + 1) % PalmRim;
                t.Add(cTop); t.Add(top + k1); t.Add(top + k);
                t.Add(top + k); t.Add(top + k1); t.Add(mid + k);
                t.Add(mid + k); t.Add(top + k1); t.Add(mid + k1);
                t.Add(mid + k); t.Add(mid + k1); t.Add(bot + k);
                t.Add(bot + k); t.Add(mid + k1); t.Add(bot + k1);
                t.Add(cBot); t.Add(bot + k); t.Add(bot + k1);
            }
        }

        /// <summary>頂點色：只有 alpha 有用——前臂那一截從手腕往外淡出。</summary>
        static Color32[] BuildColors()
        {
            var c = new Color32[VertsPerHand * 2];
            for (int i = 0; i < c.Length; i++) c[i] = new Color32(255, 255, 255, 255);
            for (int h = 0; h < 2; h++)
            {
                int at = h * VertsPerHand + (Tubes - 1) * TubeVerts;
                for (int ring = 0; ring < 4; ring++)
                {
                    byte a = ring == 0 ? (byte)255 : ring == 1 ? (byte)235 : ring == 2 ? (byte)40 : (byte)0;
                    for (int k = 0; k < Sides; k++) c[at + ring * Sides + k].a = a;
                }
                c[at + Sides * 4].a = 255;
                c[at + Sides * 4 + 1].a = 0;
            }
            return c;
        }

        void Build()
        {
            for (int h = 0; h < 2; h++)
            {
                var pose = _frame.Get((Hand)h);
                int v0 = h * VertsPerHand;
                _drawn[h] = pose.Tracked;
                if (!pose.Tracked)
                {
                    for (int i = 0; i < VertsPerHand; i++) { _verts[v0 + i] = Vector3.zero; _normals[v0 + i] = Vector3.up; } // 退化成一點：不畫
                    continue;
                }
                var n = ToUnity(pose.PalmNormal);
                for (int b = 0; b < Bones.Length; b++)
                {
                    var bone = Bones[b];
                    Tube(v0 + b * TubeVerts, ToUnity(pose.Position(bone.a)), ToUnity(pose.Position(bone.b)), bone.r0, bone.r1, n);
                }
                var wrist = ToUnity(pose.Position(HandJoint.Wrist));
                var back = wrist - ToUnity(pose.Position(HandJoint.MiddleProximal));
                if (back.sqrMagnitude < 1e-10f) back = Vector3.back;
                Tube(v0 + Bones.Length * TubeVerts, wrist, wrist + back.normalized * ForearmLength, ForearmRadius, ForearmRadius * 0.9f, n);
                Palm(v0 + Tubes * TubeVerts, pose, n);
            }
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
        }

        /// <summary>a→b 的圓管，兩端是圓頭（截面方向用掌心法線定，指節不會扭）。</summary>
        void Tube(int at, Vector3 a, Vector3 b, float r0, float r1, Vector3 normal)
        {
            var d = b - a;
            float len = d.magnitude;
            var axis = len > 1e-6f ? d / len : Vector3.forward;
            var u = Vector3.Cross(axis, normal);
            if (u.sqrMagnitude < 1e-10f) u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-10f) u = Vector3.right;
            u.Normalize();
            var v = Vector3.Cross(u, axis);
            // 圓頭：往外 0.55r 的地方收成 0.8r，尖端在 0.95r
            var capA = a - axis * (r0 * 0.55f);
            var capB = b + axis * (r1 * 0.55f);
            for (int k = 0; k < Sides; k++)
            {
                var dir = u * Cos[k] + v * Sin[k];
                _verts[at + k] = capA + dir * (r0 * 0.8f);
                _normals[at + k] = (dir * 0.75f - axis * 0.66f).normalized;
                _verts[at + Sides + k] = a + dir * r0;
                _normals[at + Sides + k] = dir;
                _verts[at + Sides * 2 + k] = b + dir * r1;
                _normals[at + Sides * 2 + k] = dir;
                _verts[at + Sides * 3 + k] = capB + dir * (r1 * 0.8f);
                _normals[at + Sides * 3 + k] = (dir * 0.75f + axis * 0.66f).normalized;
            }
            _verts[at + Sides * 4] = a - axis * (r0 * 0.95f);
            _normals[at + Sides * 4] = -axis;
            _verts[at + Sides * 4 + 1] = b + axis * (r1 * 0.95f);
            _normals[at + Sides * 4 + 1] = axis;
        }

        /// <summary>手掌：手腕、各指根部、虎口圍成的外框，上下兩面往中間收成圓角厚片。</summary>
        void Palm(int at, HandPose pose, Vector3 normal)
        {
            var wrist = ToUnity(pose.Position(HandJoint.Wrist));
            var index = ToUnity(pose.Position(HandJoint.IndexProximal));
            var middle = ToUnity(pose.Position(HandJoint.MiddleProximal));
            var ring = ToUnity(pose.Position(HandJoint.RingProximal));
            var little = ToUnity(pose.Position(HandJoint.LittleProximal));
            var thumb = ToUnity(pose.Position(HandJoint.ThumbMetacarpal));
            var across = index - little;
            if (across.sqrMagnitude < 1e-10f) across = Vector3.right;
            across.Normalize();
            // 外框投影到手掌平面上（不讓厚度方向的抖動把邊扭歪）
            var w0 = wrist + across * 0.020f;
            var w1 = wrist - across * 0.019f;
            _rim[0] = w0;
            _rim[1] = Vector3.Lerp(w0, index, 0.45f) + across * 0.006f;   // 虎口那側
            _rim[2] = index + across * 0.006f;
            _rim[3] = Vector3.Lerp(index, middle, 0.5f) + (middle - wrist).normalized * 0.004f;
            _rim[4] = middle + (middle - wrist).normalized * 0.003f;
            _rim[5] = Vector3.Lerp(middle, ring, 0.5f) + (ring - wrist).normalized * 0.003f;
            _rim[6] = ring;
            _rim[7] = little - across * 0.005f;
            _rim[8] = Vector3.Lerp(little, w1, 0.5f) - across * 0.008f;   // 掌緣微微外鼓
            _rim[9] = w1;
            // 虎口往拇指根部拉一點，拇指才接得上手掌
            _rim[1] = Vector3.Lerp(_rim[1], thumb, 0.35f);
            var center = Vector3.zero;
            for (int k = 0; k < PalmRim; k++) center += _rim[k];
            center /= PalmRim;
            // 左右手鏡像：外框繞掌心法線的方向要和三角形的繞序一致，反了就倒過來排
            float turn = 0f;
            for (int k = 0; k < PalmRim; k++)
                turn += Vector3.Dot(Vector3.Cross(_rim[(k + 1) % PalmRim] - center, _rim[k] - center), normal);
            if (turn < 0f) Array.Reverse(_rim);
            float dn = 0f;
            for (int k = 0; k < PalmRim; k++) dn += Vector3.Dot(_rim[k] - center, normal);
            center += normal * (dn / PalmRim);
            var h = normal * PalmHalfThickness;
            for (int k = 0; k < PalmRim; k++)
            {
                var p = _rim[k] - normal * Vector3.Dot(_rim[k] - center, normal);
                var outward = p - center;
                outward = outward.sqrMagnitude > 1e-10f ? outward.normalized : across;
                var inner = Vector3.Lerp(center, p, 0.82f);
                _verts[at + k] = inner + h;
                _normals[at + k] = (normal * 0.8f + outward * 0.6f).normalized;
                _verts[at + PalmRim + k] = p;
                _normals[at + PalmRim + k] = outward;
                _verts[at + PalmRim * 2 + k] = inner - h;
                _normals[at + PalmRim * 2 + k] = (-normal * 0.8f + outward * 0.6f).normalized;
            }
            _verts[at + PalmRim * 3] = center + h * 1.15f;
            _normals[at + PalmRim * 3] = normal;
            _verts[at + PalmRim * 3 + 1] = center - h * 1.15f;
            _normals[at + PalmRim * 3 + 1] = -normal;
        }

        static Vector3 ToUnity(NV3 v) => new Vector3(v.X, v.Y, v.Z);

        static readonly Dictionary<HandRecording, (Vector3 center, Bounds region)> Measured = new Dictionary<HandRecording, (Vector3, Bounds)>();

        /// <summary>
        /// 片段裡畫出來的手（只在 Play 換片段時算一次）：手掌平均位置，以及手大部分時間所在的範圍
        /// （中間 80% 的時間、所有關節，每軸取 10%–90% 分位，再往外留 1 cm）——開頭伸手過來的那一小段不算。
        /// </summary>
        public static (Vector3 center, Bounds region) Measure(HandRecording clip)
        {
            if (Measured.TryGetValue(clip, out var m)) return m;
            var sum = Vector3.zero;
            int n = 0;
            var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
            int count = clip.Frames.Count;
            for (int i = 0; i < count; i++)
            {
                var f = clip.Frames[i];
                bool core = i >= count / 10 && i <= count - 1 - count / 10;
                for (int h = 0; h < 2; h++)
                {
                    var p = f.Get((Hand)h);
                    if (!p.Tracked) continue;
                    sum += ToUnity(p.Position(HandJoint.Palm));
                    n++;
                    if (!core) continue;
                    for (int j = 0; j < HandPose.JointCount; j++)
                    {
                        var v = p.Position((HandJoint)j);
                        xs.Add(v.X); ys.Add(v.Y); zs.Add(v.Z);
                    }
                }
            }
            var center = n > 0 ? sum / n : Vector3.zero;
            var region = new Bounds(center, Vector3.one * 0.08f);
            if (xs.Count > 0)
            {
                xs.Sort(); ys.Sort(); zs.Sort();
                var lo = new Vector3(Pct(xs, 0.1f), Pct(ys, 0.1f), Pct(zs, 0.1f)) - Vector3.one * 0.01f;
                var hi = new Vector3(Pct(xs, 0.9f), Pct(ys, 0.9f), Pct(zs, 0.9f)) + Vector3.one * 0.01f;
                region = new Bounds((lo + hi) * 0.5f, hi - lo);
            }
            m = (center, region);
            Measured[clip] = m;
            return m;
        }

        static readonly Dictionary<HandRecording, Bounds[]> MeasuredHands = new Dictionary<HandRecording, Bounds[]>();

        /// <summary>同 Measure 的範圍，但每隻畫出來的手各算一個（雙手手勢分開算）。</summary>
        public static Bounds[] MeasureHands(HandRecording clip)
        {
            if (MeasuredHands.TryGetValue(clip, out var r)) return r;
            var list = new List<Bounds>();
            int count = clip.Frames.Count;
            for (int h = 0; h < 2; h++)
            {
                var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
                for (int i = count / 10; i <= count - 1 - count / 10; i++)
                {
                    var p = clip.Frames[i].Get((Hand)h);
                    if (!p.Tracked) continue;
                    for (int j = 0; j < HandPose.JointCount; j++)
                    {
                        var v = p.Position((HandJoint)j);
                        xs.Add(v.X); ys.Add(v.Y); zs.Add(v.Z);
                    }
                }
                if (xs.Count == 0) continue;
                xs.Sort(); ys.Sort(); zs.Sort();
                var lo = new Vector3(Pct(xs, 0.1f), Pct(ys, 0.1f), Pct(zs, 0.1f)) - Vector3.one * 0.01f;
                var hi = new Vector3(Pct(xs, 0.9f), Pct(ys, 0.9f), Pct(zs, 0.9f)) + Vector3.one * 0.01f;
                list.Add(new Bounds((lo + hi) * 0.5f, hi - lo));
            }
            r = list.Count > 0 ? list.ToArray() : new[] { Measure(clip).region };
            MeasuredHands[clip] = r;
            return r;
        }

        static readonly Dictionary<HandRecording, Bounds[]> Sampled = new Dictionary<HandRecording, Bounds[]>();
        /// <summary>SampleHands 取幾個時間點。</summary>
        public const int HandSamples = 24;

        /// <summary>
        /// 手在片段裡各個時刻的樣子（中間 80% 的時間取 HandSamples 個時間點，每隻畫出來的手一個盒子：所有關節＋1 cm）。
        /// 字幕不能蓋住任何一個時刻的手；比整段動作的大範圍準（雙手推 All-in 時兩手掃過整個桌緣，大範圍裡根本放不下字）。
        /// </summary>
        public static Bounds[] SampleHands(HandRecording clip)
        {
            if (Sampled.TryGetValue(clip, out var r)) return r;
            var list = new List<Bounds>();
            int count = clip.Frames.Count, from = count / 10, to = count - 1 - count / 10;
            for (int k = 0; k < HandSamples; k++)
            {
                var f = clip.Frames[from + (to - from) * k / Mathf.Max(1, HandSamples - 1)];
                for (int h = 0; h < 2; h++)
                {
                    var p = f.Get((Hand)h);
                    if (!p.Tracked) continue;
                    var b = new Bounds(ToUnity(p.Position(HandJoint.Palm)), Vector3.zero);
                    for (int j = 0; j < HandPose.JointCount; j++) b.Encapsulate(ToUnity(p.Position((HandJoint)j)));
                    b.Expand(0.02f);
                    list.Add(b);
                }
            }
            r = list.Count > 0 ? list.ToArray() : new[] { Measure(clip).region };
            Sampled[clip] = r;
            return r;
        }

        static float Pct(List<float> sorted, float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];

        public void Dispose()
        {
            Kill(_go);
            Kill(_mesh);
            Kill(_mat);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
