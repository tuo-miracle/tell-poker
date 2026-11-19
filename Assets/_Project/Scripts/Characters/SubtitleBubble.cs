using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 頭頂的英文字幕泡泡，永遠面向玩家。字用內建字型的貼圖自己拼四邊形，材質是 URP Unlit（避免舊 TextMesh shader 在 multiview 只畫一眼）。
    /// </summary>
    public sealed class SubtitleBubble
    {
        const int FontSize = 48;
        // 公尺（2026-10-14：0.024 在 1.2 m 外的荷官只有 1.16°，提高到 0.027 約 1.3°；
        // 2026-11-02 真實尺寸的長圓桌：Pip、Rocco 的泡泡離玩家約 1.4 m，0.027 只剩 1.11° → 0.031 約 1.27°）
        public const float LineHeight = 0.031f;
        const int WrapChars = 22;
        const float Pad = 0.014f;

        readonly Transform _root;
        readonly MeshFilter _bg, _text;
        readonly Renderer _textRenderer;
        readonly Font _font;
        float _remaining, _age;
        Mesh _bgMesh, _textMesh;

        public bool IsVisible => _root.gameObject.activeSelf;
        public string Text { get; private set; } = "";
        public Transform Transform => _root;
        /// <summary>泡泡本體大小（公尺，不含尾巴、未乘字幕倍率）。</summary>
        public Vector2 Size { get; private set; }
        /// <summary>字幕倍率（UX 的 Text size 縮放子物件）。</summary>
        public float ContentScale => _text.transform.localScale.x;


        public SubtitleBubble(Transform parent, Vector3 localPos)
        {
            _root = CharacterBuilder.Node("Bubble", parent, localPos);
            var bg = new GameObject("BubbleBg");
            bg.transform.SetParent(_root, false);
            _bg = bg.AddComponent<MeshFilter>();
            var bgr = bg.AddComponent<MeshRenderer>();
            bgr.sharedMaterials = new[] { MaterialCache.Unlit(CharacterBuilder.Ink), MaterialCache.Unlit(Color.white) };
            CharacterBuilder.SetupRenderer(bgr);

            var tx = new GameObject("BubbleText");
            tx.transform.SetParent(_root, false);
            tx.transform.localPosition = new Vector3(0, 0, -0.003f);
            _text = tx.AddComponent<MeshFilter>();
            _textRenderer = tx.AddComponent<MeshRenderer>();
            CharacterBuilder.SetupRenderer(_textRenderer);

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Font.textureRebuilt += OnFontRebuilt;
            _root.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            Util.SafeDestroy(_bgMesh);
            Util.SafeDestroy(_textMesh);
        }

        void OnFontRebuilt(Font f)
        {
            // Editor 模式下 OnDestroy 不一定會被呼叫，物件不在了就自己退訂
            if (_root == null) { Font.textureRebuilt -= OnFontRebuilt; return; }
            if (f == _font && IsVisible) BuildText();
        }

        public void Show(string text, float seconds)
        {
            Text = Wrap(text, WrapChars);
            _remaining = seconds;
            _age = 0;
            _root.gameObject.SetActive(true);
            var size = BuildText();
            Size = new Vector2(size.x + Pad * 2, size.y + Pad * 2);
            BuildBackground(Size.x, Size.y);
        }

        public void Hide()
        {
            _remaining = 0;
            _root.gameObject.SetActive(false);
        }

        /// <summary>計時、彈出動畫與面向玩家。</summary>
        public void Tick(float dt, Vector3 viewerPos)
        {
            if (!IsVisible) return;
            _age += dt;
            _remaining -= dt;
            if (_remaining <= 0) { Hide(); return; }
            float pop = _age < 0.18f ? Mathf.Sin(_age / 0.18f * Mathf.PI * 0.75f) / 0.707f : 1f;
            float shrink = Mathf.Clamp01(_remaining / 0.15f);
            _root.localScale = Vector3.one * Mathf.Max(0.01f, pop * shrink);
            var away = _root.position - viewerPos;
            if (away.sqrMagnitude > 1e-6f) _root.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        static string Wrap(string s, int width)
        {
            var sb = new StringBuilder();
            int lineLen = 0;
            foreach (var word in s.Split(' '))
            {
                if (lineLen > 0 && lineLen + 1 + word.Length > width) { sb.Append('\n'); lineLen = 0; }
                else if (lineLen > 0) { sb.Append(' '); lineLen++; }
                sb.Append(word);
                lineLen += word.Length;
            }
            return sb.ToString();
        }

        Vector2 BuildText()
        {
            _font.RequestCharactersInTexture(Text, FontSize, FontStyle.Bold);
            float k = LineHeight / (FontSize * 1.15f);
            var lines = Text.Split('\n');
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float maxW = 0;
            var widths = new float[lines.Length];
            for (int l = 0; l < lines.Length; l++)
            {
                float w = 0;
                foreach (var ch in lines[l])
                    if (_font.GetCharacterInfo(ch, out var ci, FontSize, FontStyle.Bold)) w += ci.advance;
                widths[l] = w * k;
                maxW = Mathf.Max(maxW, widths[l]);
            }
            float totalH = lines.Length * LineHeight;
            for (int l = 0; l < lines.Length; l++)
            {
                float x = -widths[l] * 0.5f;
                float baseY = totalH * 0.5f - (l + 1) * LineHeight + LineHeight * 0.25f;
                foreach (var ch in lines[l])
                {
                    if (!_font.GetCharacterInfo(ch, out var ci, FontSize, FontStyle.Bold)) continue;
                    int i0 = verts.Count;
                    verts.Add(new Vector3(x + ci.minX * k, baseY + ci.minY * k, 0));
                    verts.Add(new Vector3(x + ci.maxX * k, baseY + ci.minY * k, 0));
                    verts.Add(new Vector3(x + ci.maxX * k, baseY + ci.maxY * k, 0));
                    verts.Add(new Vector3(x + ci.minX * k, baseY + ci.maxY * k, 0));
                    uvs.Add(ci.uvBottomLeft); uvs.Add(ci.uvBottomRight); uvs.Add(ci.uvTopRight); uvs.Add(ci.uvTopLeft);
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                    tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
                    x += ci.advance * k;
                }
            }
            if (_textMesh == null) _textMesh = new Mesh { name = "SubtitleText" };
            _textMesh.Clear();
            _textMesh.SetVertices(verts);
            _textMesh.SetUVs(0, uvs);
            _textMesh.SetTriangles(tris, 0);
            _textMesh.RecalculateBounds();
            _text.sharedMesh = _textMesh;
            _textRenderer.sharedMaterial = MaterialCache.Text(_font.material.mainTexture, CharacterBuilder.Ink);
            return new Vector2(maxW, totalH);
        }

        void BuildBackground(float w, float h)
        {
            var outline = RoundedWithTail(w, h, 0.012f);
            var outer = RoundedWithTail(w + 0.006f, h + 0.006f, 0.015f);
            var c = new MeshLibrary.Composer()
                .Add(MeshLibrary.Slab(outer, 0.002f), Matrix4x4.Translate(new Vector3(0, 0, 0.002f)), MaterialCache.Unlit(CharacterBuilder.Ink))
                .Add(MeshLibrary.Slab(outline, 0.002f), Matrix4x4.identity, MaterialCache.Unlit(Color.white));
            Util.SafeDestroy(_bgMesh);
            _bgMesh = c.Build(out var mats);
            _bg.sharedMesh = _bgMesh;
            _bg.GetComponent<MeshRenderer>().sharedMaterials = mats;
        }

        /// <summary>圓角矩形＋往下指向角色的尾巴。</summary>
        static List<Vector2> RoundedWithTail(float w, float h, float r)
        {
            var pts = new List<Vector2>();
            float hw = w * 0.5f, hh = h * 0.5f, tailH = 0.022f + (w - 0.1f) * 0.02f;
            void Corner(float cx, float cy, float a0)
            {
                for (int i = 0; i <= 4; i++)
                {
                    float a = (a0 + i * 22.5f) * Mathf.Deg2Rad;
                    pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r));
                }
            }
            Corner(hw - r, hh - r, 0);
            Corner(-hw + r, hh - r, 90);
            Corner(-hw + r, -hh + r, 180);
            float tx = -Mathf.Min(0.02f, hw * 0.3f);
            pts.Add(new Vector2(tx - 0.012f, -hh));
            pts.Add(new Vector2(tx - 0.004f, -hh - tailH));
            pts.Add(new Vector2(tx + 0.014f, -hh));
            Corner(hw - r, -hh + r, 270);
            return pts;
        }
    }
}
