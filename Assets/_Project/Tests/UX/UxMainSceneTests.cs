using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.Game;
using Poker.UX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Poker.Tests.UX
{
    /// <summary>Main 場景：UxBootstrap 自動掛上、選單截圖（玩家視角）、重新開一局後重新掛上。</summary>
    public class UxMainSceneTests
    {
        string _dir;

        [SetUp] public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ux-main-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            UxController.SettingsPathOverride = Path.Combine(_dir, "ux-settings.json");
        }

        [TearDown] public void TearDown()
        {
            UxController.SettingsPathOverride = null;
            AudioListener.pause = false;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static IEnumerator LoadMain()
        {
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 90; i++) yield return null;
        }

        static UxController FindUx(out GameController gc)
        {
            gc = Object.FindAnyObjectByType<GameController>();
            Assert.IsNotNull(gc, "Main 有 GameController");
            Assert.IsTrue(gc.TryGetComponent<UxController>(out var ux), "UxBootstrap 在執行期掛上 UxController");
            return ux;
        }

        [UnityTest] public IEnumerator BootstrapAttachesAndMenuScreenshots()
        {
            yield return LoadMain();
            var ux = FindUx(out var gc);
            Assert.IsTrue(gc.IsInitialized);
            Assert.IsFalse(ux.Menu.IsOpen);

            ux.Menu.Open();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsFalse(gc.AutoStep, "選單打開 → 暫停");
            var cam = gc.Head.GetComponent<Camera>();
            // 每一頁各一張（2026-10-15 選單瘦身）：主頁（教學中多 Skip tutorial）、How to play（選了一個示範）、Settings
            Save(cam, ux.Menu.Items(MenuPage.Main).Contains(MenuItem.SkipTutorial) ? "ux-menu-main-tutorial" : "ux-menu-main");
            ux.Menu.Press(MenuItem.HowToPlay);
            for (int i = 0; i < 3; i++) yield return null;
            Save(cam, "ux-menu-howtoplay");
            ux.Menu.Press(MenuItem.GhostBet);
            for (int i = 0; i < 20; i++) yield return null;
            Save(cam, "ux-menu-howtoplay-bet");
            ux.Menu.Press(MenuItem.Back);
            ux.Menu.Press(MenuItem.Settings);
            for (int i = 0; i < 3; i++) yield return null;
            Save(cam, "ux-menu-settings");
            ux.Menu.Press(MenuItem.HighContrast);
            ux.Menu.Press(MenuItem.Back);
            ux.Menu.Press(MenuItem.Resume);
            Assert.IsTrue(gc.AutoStep);
            for (int i = 0; i < 240 && gc.Props.PlayerCards[0].transform.position.y < -100f; i++) yield return null;
            for (int i = 0; i < 120; i++) yield return null;   // 等發牌與下盲注
            var head = gc.Head;
            var keep = head.rotation;
            head.rotation = Quaternion.LookRotation(gc.Table.TableToWorld(new Vector3(0f, 0f, 0.25f)) - head.position);
            Save(cam, "ux-high-contrast");
            head.rotation = keep;
            // 教學中的主頁（多 Skip tutorial）
            gc.ReplayTutorial();
            ux.Menu.Open();
            for (int i = 0; i < 5; i++) yield return null;
            if (ux.Menu.Items(MenuPage.Main).Contains(MenuItem.SkipTutorial)) Save(cam, "ux-menu-main-tutorial");
            ux.Menu.Close();
        }

        [UnityTest] public IEnumerator NewGameReloadsAndReattaches()
        {
            yield return LoadMain();
            var ux = FindUx(out var gc);
            ux.Menu.Open();
            ux.Menu.Press(MenuItem.NewGame);
            for (int i = 0; i < 120; i++) yield return null;
            var ux2 = FindUx(out var gc2);
            Assert.AreNotSame(gc, gc2, "新的一局（場景重新載入）");
            Assert.IsFalse(ux2.Menu.IsOpen);
            Assert.IsTrue(gc2.AutoStep);
            Assert.IsFalse(AudioListener.pause);
        }

        static void Save(Camera cam, string name) => MenuShots.Save(cam, name);
    }

    /// <summary>從玩家視角截圖存到 Logs/scratch/（選單各頁的證據）。</summary>
    static class MenuShots
    {
        public static void Save(Camera cam, string name)
        {
            const int W = 1280, H = 960;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "scratch"));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
