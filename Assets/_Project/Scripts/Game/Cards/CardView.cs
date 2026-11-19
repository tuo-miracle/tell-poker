using System.Collections.Generic;
using Poker.Core;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 一張牌：主體＋靠近玩家那端的「掀角片」兩塊網格，掀角片用鉸鏈（平行 X 軸）往上翹，露出牌面下緣的角標。
    /// 本地座標：牌心在原點，寬 X、長 Z（+Z 是遠離玩家），預設背面朝上（+Y 是牌背）。
    /// 翻成正面時繞 Z 軸轉 180°，牌面上緣仍朝遠方，所以對玩家是正的。
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public const float Width = 0.063f, Length = 0.088f;
        /// <summary>掀角片的深度（沿 Z）。涵蓋下緣兩個角標。</summary>
        public const float FlapDepth = 0.034f;
        const float Half = 0.00025f, CornerRadius = 0.0045f;

        /// <summary>
        /// 咪牌最大掀起角度。spec 原本寫約 30°，但牌面朝下、坐著的眼睛在桌面上約 40 cm：
        /// 掀角片要超過約 40° 牌面才開始轉向眼睛，約 70° 角標才看得清楚（見 CardViewTests.FullPeek_*）。可調。
        /// </summary>
        public float MaxPeekDegrees = 70f;
        public Card Card { get; private set; }
        public bool FaceUp { get; private set; }
        public float PeekDegrees { get; private set; }
        public Transform Flap { get; private set; }
        /// <summary>水平朝向（度），例如 AI 的牌轉 180° 面向它。</summary>
        public float Yaw { get; private set; }

        static Material _backMat;
        static readonly Dictionary<int, Material> FaceMats = new Dictionary<int, Material>();
        static Mesh _bodyMesh, _flapMesh;

        Renderer[] _renderers;

        public static CardView Create(Transform parent, Card card, string name = null)
        {
            var go = new GameObject(name ?? "Card");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CardView>();
            view.Build();
            view.SetCard(card);
            return view;
        }

        void Build()
        {
            EnsureMeshes();
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = _bodyMesh;
            var br = body.AddComponent<MeshRenderer>();

            var flap = new GameObject("Flap");
            flap.transform.SetParent(transform, false);
            flap.transform.localPosition = new Vector3(0, 0, -Length * 0.5f + FlapDepth);
            flap.AddComponent<MeshFilter>().sharedMesh = _flapMesh;
            var fr = flap.AddComponent<MeshRenderer>();
            Flap = flap.transform;

            _renderers = new Renderer[] { br, fr };
            foreach (var r in _renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        public void SetCard(Card card)
        {
            Card = card;
            var mats = new[] { BackMaterial, card.IsValid ? FaceMaterial(card) : BackMaterial };
            foreach (var r in _renderers) r.sharedMaterials = mats;
        }

        public void SetYaw(float yaw)
        {
            Yaw = yaw;
            ApplyRotation(FaceUp ? 1f : 0f);
        }

        public void SetFaceUp(bool faceUp)
        {
            FaceUp = faceUp;
            if (faceUp) SetPeek(0f);
            ApplyRotation(faceUp ? 1f : 0f);
        }

        /// <summary>翻牌動畫用：0 = 背面朝上，1 = 正面朝上。</summary>
        public void SetFlipProgress(float t)
        {
            ApplyRotation(Mathf.Clamp01(t));
        }

        void ApplyRotation(float flip)
        {
            transform.localRotation = Quaternion.Euler(0f, Yaw, 0f) * Quaternion.AngleAxis(180f * flip, Vector3.forward);
        }

        /// <summary>咪牌：amount 0–1 線性對應到掀角角度（上限 MaxPeekDegrees）。翻開的牌不掀。</summary>
        public void SetPeek(float amount)
        {
            PeekDegrees = FaceUp ? 0f : Mathf.Clamp01(amount) * MaxPeekDegrees;
            Flap.localRotation = Quaternion.AngleAxis(PeekDegrees, Vector3.right);
        }

        /// <summary>掀角片最前端中央（牌的本地座標），測試用。</summary>
        public Vector3 FlapTipLocal => Flap.localPosition + Flap.localRotation * new Vector3(0, 0, -FlapDepth);

        public static Material BackMaterial
        {
            get
            {
                if (_backMat != null) return _backMat;
                _backMat = MakeMaterial("CardBack", CardFace.Back);
                return _backMat;
            }
        }

        public static Material FaceMaterial(Card c)
        {
            if (FaceMats.TryGetValue(c.Index, out var m) && m != null) return m;
            m = MakeMaterial("Card_" + c, CardFace.Get(c));
            FaceMats[c.Index] = m;
            return m;
        }

        static Material MakeMaterial(string name, Texture tex)
        {
            var m = new Material(Poker.Characters.MaterialCache.Unlit(Color.white)) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Cull", 2f); // 兩面各自一層，背面剔除才不會互相穿插
            return m;
        }

        // ---- 網格：兩個子網格（0 = 牌背，朝 +Y；1 = 牌面，朝 −Y） ----

        static void EnsureMeshes()
        {
            if (_bodyMesh != null) return;
            float zh = -Length * 0.5f + FlapDepth;
            _bodyMesh = Piece("CardBody", zh, Length * 0.5f, false, true, 0f);
            _flapMesh = Piece("CardFlap", -Length * 0.5f, zh, true, false, zh);
        }

        /// <summary>z0..z1 的一段牌（只在 roundNear／roundFar 那端做圓角），頂點減掉 pivotZ（鉸鏈在原點）。</summary>
        static Mesh Piece(string name, float z0, float z1, bool roundNear, bool roundFar, float pivotZ)
        {
            float hx = Width * 0.5f;
            var outline = new List<Vector2>(); // (x, z)，逆時針（從上往下看）
            void Corner(float cx, float cz, float a0)
            {
                for (int i = 0; i <= 5; i++)
                {
                    float a = (a0 + 90f * i / 5f) * Mathf.Deg2Rad;
                    outline.Add(new Vector2(cx + CornerRadius * Mathf.Cos(a), cz + CornerRadius * Mathf.Sin(a)));
                }
            }
            // 近端（z0）：右下 → 遠端（z1）：右上 → 左上 → 左下
            if (roundNear) Corner(hx - CornerRadius, z0 + CornerRadius, 270f); else outline.Add(new Vector2(hx, z0));
            if (roundFar) Corner(hx - CornerRadius, z1 - CornerRadius, 0f); else outline.Add(new Vector2(hx, z1));
            if (roundFar) Corner(-hx + CornerRadius, z1 - CornerRadius, 90f); else outline.Add(new Vector2(-hx, z1));
            if (roundNear) Corner(-hx + CornerRadius, z0 + CornerRadius, 180f); else outline.Add(new Vector2(-hx, z0));

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var top = new List<int>();
            var bottom = new List<int>();
            float cz = (z0 + z1) * 0.5f;
            foreach (bool isTop in new[] { true, false })
            {
                int start = v.Count;
                float y = isTop ? Half : -Half;
                var nor = isTop ? Vector3.up : Vector3.down;
                v.Add(new Vector3(0, y, cz - pivotZ)); n.Add(nor); uv.Add(UV(0, cz, isTop));
                foreach (var p in outline)
                {
                    v.Add(new Vector3(p.x, y, p.y - pivotZ)); n.Add(nor); uv.Add(UV(p.x, p.y, isTop));
                }
                var list = isTop ? top : bottom;
                int count = outline.Count;
                for (int i = 0; i < count; i++)
                {
                    int a = start + 1 + i, b = start + 1 + (i + 1) % count;
                    // Unity 正面是順時針；outline 從上往下看是逆時針
                    if (isTop) { list.Add(start); list.Add(b); list.Add(a); }
                    else { list.Add(start); list.Add(a); list.Add(b); }
                }
            }
            var m = new Mesh { name = name, subMeshCount = 2 };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(top, 0);
            m.SetTriangles(bottom, 1);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>牌背直接對應；牌面在底下，繞 Z 翻過來後 u 方向相反。</summary>
        static Vector2 UV(float x, float z, bool back) =>
            new Vector2(back ? 0.5f + x / Width : 0.5f - x / Width, 0.5f + z / Length);
    }
}
