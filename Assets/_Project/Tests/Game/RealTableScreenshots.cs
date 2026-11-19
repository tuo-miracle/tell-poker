using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 真實尺寸長圓桌的截圖（2026-11-02 第 4 輪）：真的牌局（GameController＋寫實預覽，5 人局）放在備案虛擬桌上，燈同主場景。
    /// 玩家視角正面、俯視全桌、斜後方全景；另外一張 3 位卡通對手（正式版）的玩家視角。
    /// 手動跑：tools/run-unity-tests.sh PlayMode RealTableScreenshots（輸出 Logs/scratch/realistic-cast/round4/，要人眼看過）。
    /// </summary>
    public class RealTableScreenshots
    {
        const int Size = 1200;
        GameTestRig _rig;
        readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

#if UNITY_EDITOR
        static string Dir => Path.Combine(Application.dataPath, "..", "Logs", "scratch", "realistic-cast", "round4");

        Camera Stage()
        {
            _spawned.Add(MrLighting.CreateRig());
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 0.8f;
            floor.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Lit(new Color(0.42f, 0.40f, 0.38f), 0.1f);
            _spawned.Add(floor);
            var cam = new GameObject("ShotCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.33f, 0.31f, 0.29f);
            cam.nearClipPlane = 0.02f;
            _spawned.Add(cam.gameObject);
            return cam;
        }

        [UnityTest, Explicit("screenshots for review")]
        public IEnumerator FiveHanded_RealisticCast_OnTheVirtualTable()
        {
            Directory.CreateDirectory(Dir);
            var cam = Stage();
            var cfg = new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40, Opponents = RealisticPreview.Opponents };
            _rig = new GameTestRig(cfg);
            _rig.Table.Place(new Pose(new Vector3(0f, 0.74f, 0f), Quaternion.identity), TablePlacementSource.Virtual);
            var preview = new GameObject("RealisticPreview").AddComponent<RealisticPreview>();
            _spawned.Add(preview.gameObject);
            string GlbOf(string n) => $"Assets/_Project/Art/Characters/{n}/{n}.glb";
            preview.Cast = new[] { "Hank", "Marcus", "Jace", "Reginald", "Erik" }.Select(n => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GlbOf(n))).ToArray();
            preview.DealerModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GlbOf("Dealer"));
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            // 測試台預設的頭在桌心上方低處：字幕泡泡會被壓到桌面下（俯視圖上的白線），換成玩家座位的眼睛
            _rig.Camera.transform.SetPositionAndRotation(_rig.Table.transform.TransformPoint(RealisticPreview.PlayerEye),
                Quaternion.LookRotation(_rig.Table.transform.TransformDirection(RealisticPreview.PlayerLook - RealisticPreview.PlayerEye)));
            _rig.Idle(1.0);
            yield return null;
            Assert.AreEqual(5, Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None).Length, "4 opponents + dealer realistic");

            var t = _rig.Table.transform;
            // batchmode 第一次畫的材質會錯：先畫一張丟掉
            Shot(cam, t, RealisticPreview.PlayerEye, RealisticPreview.PlayerLook, 90f, "warmup", keep: false);
            Shot(cam, t, RealisticPreview.PlayerEye, new Vector3(0f, 0.25f, 0.6f), 90f, "five_player_view");
            Shot(cam, t, new Vector3(0f, 2.9f, 0.42f), new Vector3(0f, 0f, 0.43f), 55f, "five_top_down", up: Vector3.forward);
            Shot(cam, t, new Vector3(-1.3f, 1.1f, -1.0f), new Vector3(0f, 0.1f, 0.5f), 55f, "five_three_quarter");
            Shot(cam, t, new Vector3(0f, 0.45f, 1.9f), new Vector3(0f, 0.15f, 0.2f), 70f, "five_behind_dealer");
            // 同時說話：荷官＋老手座（Reginald）、Pip 座＋Rocco 座、老手座＋Vex 座
            var g = _rig.Game;
            foreach (var (name, a, b) in new[] { ("talk_dealer_reginald", g.Dealer, g.Character(3)), ("talk_pip_rocco", g.Character(1), g.Character(2)), ("talk_silas_vex", g.Character(3), g.Character(4)) })
            {
                foreach (var c in g.AllCharacters()) c.Subtitle.Hide();
                a.Subtitle.Show("Action is on you.", 10f);
                b.Subtitle.Show("Bet. Don't take it personally.", 10f);
                // 不跑牌局（整桌一個發言權會把第二個泡泡收掉），只讓這兩個角色更新泡泡位置
                for (int k = 0; k < 40; k++) { a.Tick(1f / 72f); b.Tick(1f / 72f); }
                Shot(cam, t, RealisticPreview.PlayerEye, new Vector3(0f, 0.25f, 0.6f), 90f, "five_" + name);
            }
        }

        [UnityTest, Explicit("screenshots for review")]
        public IEnumerator Cartoon_FourHanded_OnTheVirtualTable()
        {
            Directory.CreateDirectory(Dir);
            var cam = Stage();
            _rig = new GameTestRig(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40 });
            _rig.Table.Place(new Pose(new Vector3(0f, 0.74f, 0f), Quaternion.identity), TablePlacementSource.Virtual);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            // 測試台預設的頭在桌心上方低處：字幕泡泡會被壓到桌面下（俯視圖上的白線），換成玩家座位的眼睛
            _rig.Camera.transform.SetPositionAndRotation(_rig.Table.transform.TransformPoint(RealisticPreview.PlayerEye),
                Quaternion.LookRotation(_rig.Table.transform.TransformDirection(RealisticPreview.PlayerLook - RealisticPreview.PlayerEye)));
            _rig.Idle(1.0);
            yield return null;
            var t = _rig.Table.transform;
            Shot(cam, t, RealisticPreview.PlayerEye, RealisticPreview.PlayerLook, 90f, "warmup", keep: false);
            Shot(cam, t, RealisticPreview.PlayerEye, new Vector3(0f, 0.25f, 0.6f), 90f, "cartoon4_player_view");
            Shot(cam, t, new Vector3(0f, 2.9f, 0.42f), new Vector3(0f, 0f, 0.43f), 55f, "cartoon4_top_down", up: Vector3.forward);
        }

        static void Shot(Camera cam, Transform table, Vector3 from, Vector3 at, float fov, string name, bool keep = true, Vector3? up = null)
        {
            cam.transform.position = table.TransformPoint(from);
            cam.transform.LookAt(table.TransformPoint(at), up.HasValue ? table.TransformDirection(up.Value) : Vector3.up);
            cam.fieldOfView = fov;
            // batchmode 沒有逐幀更新：blendshape 要關掉再打開才會重新上傳
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                if (r.enabled) { r.enabled = false; r.enabled = true; }
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var path = Path.Combine(Dir, name + ".png");
            if (keep) File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            if (keep) Assert.IsTrue(File.Exists(path), path);
        }
#endif
    }
}
