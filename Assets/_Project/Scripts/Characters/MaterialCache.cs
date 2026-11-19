using System.Collections.Generic;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 執行期建立並快取 URP 材質（同色同參數共用一份，SRP Batcher 友善）。
    /// 基底材質放在 Resources/CharacterMaterials，確保 Quest 建置時 shader 與 keyword 不被剝掉；找不到才退回 Shader.Find。
    /// </summary>
    public static class MaterialCache
    {
        static readonly Dictionary<(int kind, Color32 color, int smooth), Material> Cache = new Dictionary<(int, Color32, int), Material>();
        static Material _litBase, _unlitBase, _textBase;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        public const string ResourceFolder = "CharacterMaterials";

        public static Material Lit(Color c, float smoothness = 0.3f) => Get(0, c, smoothness);
        public static Material Unlit(Color c) => Get(1, c, 0);

        /// <summary>字幕用：URP Unlit＋alpha clip，貼字型貼圖。</summary>
        public static Material Text(Texture fontTexture, Color c)
        {
            var key = (2, (Color32)c, fontTexture != null ? fontTexture.GetInstanceID() : 0);
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(TextBase()) { name = "CharText" };
            m.SetColor(BaseColor, c);
            m.SetTexture(BaseMap, fontTexture);
            Cache[key] = m;
            return m;
        }

        static Material Get(int kind, Color c, float smoothness)
        {
            var key = (kind, (Color32)c, Mathf.RoundToInt(smoothness * 100));
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(kind == 0 ? LitBase() : UnlitBase()) { name = (kind == 0 ? "CharLit_" : "CharUnlit_") + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor(BaseColor, c);
            if (kind == 0) m.SetFloat(Smoothness, smoothness);
            m.enableInstancing = false;
            Cache[key] = m;
            return m;
        }

        static Material LitBase() => _litBase != null ? _litBase : (_litBase = Load(LitAsset));
        static Material UnlitBase() => _unlitBase != null ? _unlitBase : (_unlitBase = Load(UnlitAsset));
        static Material TextBase() => _textBase != null ? _textBase : (_textBase = Load(TextAsset));

        public const string LitAsset = "CharacterLit", UnlitAsset = "CharacterUnlit", TextAsset = "CharacterText";

        static Material Load(string asset)
        {
            var m = Resources.Load<Material>(ResourceFolder + "/" + asset);
            return m != null ? m : CreateBase(asset);
        }

        /// <summary>建立基底材質（Editor 產生 Resources 資產時也用這個，確保兩邊設定一致）。</summary>
        public static Material CreateBase(string asset)
        {
            var shader = Shader.Find(asset == LitAsset ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            var m = new Material(shader) { name = asset };
            if (asset == LitAsset) return m;
            m.SetFloat("_Cull", 0); // 程式網格不必在意繞序
            if (asset == TextAsset) ConfigureTransparent(m);
            return m;
        }

        /// <summary>字幕文字：半透明混合（字型貼圖的 alpha 當覆蓋率，邊緣平滑，也避開 MSAA alpha-to-coverage）。</summary>
        public static void ConfigureTransparent(Material m)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetFloat("_AlphaClip", 0);
            m.SetFloat("_AlphaToMask", 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

    }
}
