using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poker.Editor.Characters
{
    /// <summary>
    /// 寫實角色貼圖壓縮：glTFast 把 GLB 裡的 JPEG 匯入成未壓縮 RGB24（每張 2048² 約 16 MB，實機 GPU 約 1.2 GB），而且匯入器沒有壓縮設定。
    /// 這裡從匯入後的 GLB 抽出貼圖存成 PNG（Android 用 ASTC）、複製材質指向這些 PNG、存成 Baked/&lt;名字&gt;.prefab（網格、骨頭仍是 GLB 的）。
    /// 遊戲用預製物，GLB 的未壓縮貼圖就不會被打包。GLB 仍是唯一來源：換了 GLB 就再跑一次（Baked/source.txt 記著 GLB 的 sha1，測試會檢查）。
    /// 用法：選單 Poker/Realistic/Bake Characters，或 Unity -batchmode -executeMethod Poker.Editor.Characters.RealisticBake.BakeAllBatch
    /// </summary>
    public static class RealisticBake
    {
        public static readonly string[] Names = { "Hank", "Dealer", "Marcus", "Jace", "Reginald", "Erik" };
        const TextureImporterFormat ColorFormat = TextureImporterFormat.ASTC_6x6, DataFormat = TextureImporterFormat.ASTC_5x5;

        public static string Dir(string name) => $"Assets/_Project/Art/Characters/{name}/Baked";
        public static string PrefabPath(string name) => $"{Dir(name)}/{name}.prefab";

        [MenuItem("Poker/Realistic/Bake Characters")]
        public static void BakeAll()
        {
            foreach (var n in Names) Bake(n);
            AssetDatabase.SaveAssets();
        }

        public static void BakeAllBatch()
        {
            int code = 0;
            try { BakeAll(); Debug.Log("[RealisticBake] DONE"); }
            catch (Exception e) { Debug.LogError("[RealisticBake] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void Bake(string name)
        {
            var glbPath = $"Assets/_Project/Art/Characters/{name}/{name}.glb";
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (glb == null) throw new FileNotFoundException(glbPath);
            var dir = Dir(name);
            Directory.CreateDirectory(dir);

            // 1. 貼圖：每張存成 PNG（顏色 sRGB、法線等資料 linear），設好 Android ASTC
            var renderers = glb.GetComponentsInChildren<Renderer>(true);
            var srcMats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            var texMap = new Dictionary<Texture, Texture2D>();
            foreach (var m in srcMats)
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    if (!(m.GetTexture(prop) is Texture2D t) || texMap.ContainsKey(t)) continue;
                    bool linear = IsData(prop);
                    var png = $"{dir}/{Safe(t.name)}.png";
                    File.WriteAllBytes(png, ReadBack(t, linear).EncodeToPNG());
                    AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceSynchronousImport);
                    var imp = (TextureImporter)AssetImporter.GetAtPath(png);
                    imp.textureType = TextureImporterType.Default;   // glTFast 的 shader 照原樣取樣（法線本來就是 linear RGB），不改成 NormalMap 免得換編碼
                    imp.sRGBTexture = !linear;
                    imp.mipmapEnabled = true;
                    imp.maxTextureSize = 2048;
                    imp.wrapMode = t.wrapMode;
                    imp.filterMode = t.filterMode;
                    imp.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    {
                        name = "Android", overridden = true, maxTextureSize = 2048,
                        format = linear ? DataFormat : ColorFormat, compressionQuality = 50,
                    });
                    imp.SaveAndReimport();
                    texMap[t] = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
                }

            // 2. 材質：複製一份，貼圖換成 PNG
            var matMap = new Dictionary<Material, Material>();
            foreach (var m in srcMats)
            {
                var copy = new Material(m) { name = m.name };
                foreach (var prop in m.GetTexturePropertyNames())
                    if (m.GetTexture(prop) is Texture2D t && texMap.TryGetValue(t, out var png)) copy.SetTexture(prop, png);
                var path = $"{dir}/{Safe(m.name)}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) { EditorUtility.CopySerialized(copy, existing); copy = existing; }
                else AssetDatabase.CreateAsset(copy, path);
                matMap[m] = copy;
            }

            // 3. 預製物：GLB 的階層（網格、骨頭照舊），材質換成上面的
            var go = (GameObject)UnityEngine.Object.Instantiate(glb);
            go.name = name;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(m => m != null && matMap.TryGetValue(m, out var c) ? c : m).ToArray();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(name));
            UnityEngine.Object.DestroyImmediate(go);

            File.WriteAllText($"{dir}/source.txt", $"{glbPath} sha1 {Sha1(glbPath)}\n");
            AssetDatabase.ImportAsset($"{dir}/source.txt");
            Debug.Log($"[RealisticBake] {name}: {texMap.Count} textures, {matMap.Count} materials -> {PrefabPath(name)}");
        }

        /// <summary>不是顏色的貼圖（法線、金屬粗糙度、遮蔽）：linear。</summary>
        public static bool IsData(string prop) => prop.Contains("normal") || prop.Contains("metallicRoughness") || prop.Contains("occlusion");

        static Texture2D ReadBack(Texture2D t, bool linear)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGB24, false, linear);
            copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));

        static string Sha1(string path)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2")));
        }
    }
}
