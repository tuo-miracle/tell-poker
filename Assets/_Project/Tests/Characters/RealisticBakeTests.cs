using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Poker.Tests
{
    /// <summary>
    /// 寫實角色貼圖壓縮（2026-11-02）：glTFast 把 GLB 裡的 JPEG 匯入成未壓縮 RGB24（實機 GPU 約 1.2 GB）。
    /// Editor 步驟（Poker/Realistic/Bake Characters）從 GLB 抽出貼圖存成 PNG（Android ASTC）、複製材質指向它們、存一份預製物；
    /// 遊戲用預製物，GLB 的未壓縮貼圖就不會被打包。GLB 仍是唯一來源：換 GLB 後重跑一次就好（source.txt 記著 GLB 的 sha1）。
    /// </summary>
    public class RealisticBakeTests
    {
        static readonly string[] Names = { "Hank", "Dealer", "Marcus", "Jace", "Reginald", "Erik" };

#if UNITY_EDITOR
        static string Dir(string n) => $"Assets/_Project/Art/Characters/{n}/Baked";

        static string Sha1(string path)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2")));
        }

        [TestCaseSource(nameof(Names))]
        public void BakedPrefab_IsFromTheCurrentGlb(string name)
        {
            var src = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>($"{Dir(name)}/source.txt");
            Assert.IsNotNull(src, "baked: run Poker/Realistic/Bake Characters");
            StringAssert.Contains(Sha1($"Assets/_Project/Art/Characters/{name}/{name}.glb"), src.text, "GLB changed since the bake: re-run it");
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir(name)}/{name}.prefab");
            Assert.IsNotNull(prefab);
            Assert.AreEqual(name, prefab.name, "RealisticSpec.ByName uses the model name");
        }

        [TestCaseSource(nameof(Names))]
        public void BakedPrefab_UsesOnlyAstcCompressedTextures_OnAndroid_FaceKeepsItsNormalMap(string name)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir(name)}/{name}.prefab");
            Assert.IsNotNull(prefab);
            var mats = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            Assert.IsNotEmpty(mats);
            int textures = 0;
            foreach (var m in mats)
            {
                StringAssert.StartsWith(Dir(name), UnityEditor.AssetDatabase.GetAssetPath(m), m.name + ": baked material, not the GLB's");
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(prop);
                    if (t == null) continue;
                    textures++;
                    var path = UnityEditor.AssetDatabase.GetAssetPath(t);
                    StringAssert.EndsWith(".png", path, $"{m.name}.{prop}: an extracted texture, not the GLB's uncompressed one");
                    var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
                    var android = imp.GetPlatformTextureSettings("Android");
                    Assert.IsTrue(android.overridden, path + " Android override");
                    Assert.That(android.format.ToString(), Does.StartWith("ASTC_"), path + " ASTC on Android");
                    Assert.IsTrue(imp.mipmapEnabled, path + " mipmaps");
                    bool linear = prop.Contains("normal") || prop.Contains("metallicRoughness") || prop.Contains("occlusion");
                    Assert.AreEqual(!linear, imp.sRGBTexture, path + (linear ? " linear data" : " colour is sRGB"));
                }
            }
            Assert.Greater(textures, 0);
            var face = mats.Single(m => m.name.EndsWith("_face") || m.name == "hank_face" || m.name == "dealer_face");
            if (name != "Hank" && name != "Dealer")
                Assert.IsNotNull(face.GetTexture("normalTexture"), "face normal map still on the face material");
        }

        [Test]
        public void BakedPrefabs_KeepTheRigTheGameNeeds()
        {
            foreach (var n in Names)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir(n)}/{n}.prefab");
                var glb = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Characters/{n}/{n}.glb");
                var a = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => (r.name, r.sharedMesh, r.bones.Length)).ToList();
                var b = glb.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => (r.name, r.sharedMesh, r.bones.Length)).ToList();
                CollectionAssert.AreEqual(b, a, n + ": same meshes and bones as the GLB");
            }
        }
#endif
    }
}
