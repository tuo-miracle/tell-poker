using System.Collections;
using System.IO;
using NUnit.Framework;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 備案虛擬桌（放置來源 Virtual）的截圖，燈光同主場景（MrLighting）：玩家視角、斜上方全景、扶手轉角特寫。
    /// 手動跑：tools/run-unity-tests.sh PlayMode VirtualTableScreenshots（輸出 Logs/scratch/realistic-round1/table_virtual_*.png，要人眼看過）。
    /// </summary>
    public class VirtualTableScreenshots
    {
        const int Size = 1200;
        GameObject _table, _lights, _floor, _cam;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _table, _lights, _floor, _cam }) if (go != null) Object.DestroyImmediate(go);
        }

        [UnityTest, Explicit("screenshots for review")]
        public IEnumerator VirtualTable_Views()
        {
            var dir = Path.Combine(Application.dataPath, "..", "Logs", "scratch", "realistic-round1");
            Directory.CreateDirectory(dir);
            _lights = MrLighting.CreateRig();
            _floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floor.transform.localScale = Vector3.one * 0.6f;
            _floor.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Lit(new Color(0.42f, 0.40f, 0.38f), 0.1f);
            _table = new GameObject("Table");
            var frame = _table.AddComponent<TableFrame>();
            frame.Place(new Pose(new Vector3(0, 0.74f, 0), Quaternion.identity), TablePlacementSource.Virtual);
            _table.AddComponent<TableView>();
            _cam = new GameObject("Cam");
            var cam = _cam.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.33f, 0.31f, 0.29f);
            cam.nearClipPlane = 0.02f;
            yield return null;

            // batchmode 第一次畫的材質會錯（全部變成同一個顏色）：先畫一張丟掉
            Shot(cam, frame, new Vector3(0, 0.45f, -0.25f), new Vector3(0, 0, 0.42f), 80f, Path.Combine(dir, "table_virtual_warmup.png"));
            File.Delete(Path.Combine(dir, "table_virtual_warmup.png"));
            // 玩家坐著的眼睛（牌桌座標 (0, 0.45, −0.25)）、斜上方全景、扶手轉角特寫
            Shot(cam, frame, new Vector3(0, 0.45f, -0.25f), new Vector3(0, 0, 0.42f), 80f, Path.Combine(dir, "table_virtual_player_view.png"));
            Shot(cam, frame, new Vector3(-1.0f, 0.75f, -0.75f), new Vector3(0, -0.12f, 0.35f), 50f, Path.Combine(dir, "table_virtual_three_quarter.png"));
            Shot(cam, frame, new Vector3(0.75f, 0.12f, -0.22f), new Vector3(0.45f, 0f, 0.05f), 35f, Path.Combine(dir, "table_virtual_rail_corner.png"));
        }

        static void Shot(Camera cam, TableFrame frame, Vector3 from, Vector3 at, float fov, string path)
        {
            cam.transform.position = frame.transform.TransformPoint(from);
            cam.transform.LookAt(frame.transform.TransformPoint(at));
            cam.fieldOfView = fov;
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
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Assert.IsTrue(File.Exists(path), path);
        }
    }
}
