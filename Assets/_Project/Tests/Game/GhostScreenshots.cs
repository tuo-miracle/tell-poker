using System.Collections;
using System.IO;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 示範手截圖（Logs/scratch/ghost_*.png）：教學第一步的咪牌示範（坐姿與實機的低眼位）、牌局中發呆時的敲桌提示、下注與 All-in 示範。
    /// 背景用暗暖灰模擬 passthrough。檔案要人眼看過（測試只確認檔案存在、畫面不是空的、示範手真的有畫出來）。
    /// </summary>
    public class GhostScreenshots
    {
        const int W = 1280, H = 960;
        static readonly Color Room = new Color(0.33f, 0.31f, 0.29f);
        GameTestRig _rig;
        GameObject _lights;
        GhostHands _extra;
        string _dir, _save;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.dataPath, "..", "Logs", "scratch");
            Directory.CreateDirectory(_dir);
            _save = Path.Combine(Path.GetTempPath(), "tell_ghostshots_" + System.Guid.NewGuid().ToString("N") + ".json");
            _lights = MrLighting.CreateRig();
        }

        [TearDown]
        public void TearDown()
        {
            _extra?.Dispose();
            _extra = null;
            _rig?.Dispose();
            _rig = null;
            if (_lights != null) Object.DestroyImmediate(_lights);
            if (File.Exists(_save)) File.Delete(_save);
        }

        void Setup(GameConfig cfg, System.Action<GameController> configure)
        {
            _rig = new GameTestRig(cfg, configure);
            var cam = _rig.Camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Room;
            cam.fieldOfView = 68f;
            cam.transform.position = new Vector3(0f, 0.42f, -0.22f);
        }

        [UnityTest]
        public IEnumerator GhostHands_TutorialAndHints()
        {
            // ---- 教學第一步：牌上的咪牌示範 ----
            Setup(new GameConfig { Seed = 31, MonteCarloTrials = 60 }, g =>
            {
                g.ProgressPath = _save;
                g.TutorialEnabled = true;
            });
            yield return null;
            var g0 = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g0.GhostShown == GhostGesture.Peek, 10));
            Assert.IsTrue(_rig.IdleUntil(() => g0.Ghost.PlaybackTime > 1.0 && g0.Ghost.Alpha > 0.3f, 6), "mid-peek");
            Aim(new Vector3(0f, 0.05f, 0.2f));
            Save("ghost_01_tutorial_peek");
            // 實機那一張（2026-10-14）：頭戴裝置放在桌上，眼睛只比桌面高 0.2 m、看向牌桌中段；Quest 3 的垂直視角約 96°
            _rig.Camera.transform.position = new Vector3(0f, 0.20f, -0.30f);
            _rig.Camera.fieldOfView = 96f;
            Aim(new Vector3(0f, 0.04f, 0.35f));
            _rig.Idle(0.05);   // 字幕、泡泡面向新的視點
            Save("ghost_01b_tutorial_peek_low_eye");
            _rig.Dispose();
            _rig = null;

            // ---- 正式牌局：輪到玩家發呆 5 秒，敲桌提示 ----
            Setup(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 60 }, g =>
            {
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
            });
            yield return null;
            var g1 = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(10));
            Assert.IsTrue(_rig.IdleUntil(() => g1.GhostShown == GhostGesture.Knock && g1.Ghost.PlaybackTime > 0.9, 12), "knock hint");
            Aim(new Vector3(0.15f, 0.05f, 0.25f));
            Save("ghost_02_hint_knock");

            // ---- 下注、All-in：另外放一組示範手，播到關鍵那一刻 ----
            _extra = new GhostHands(g1.Props.Root, "ShotGhost");
            g1.MenuOpen = true; // 牌局自己的提示先收起來
            Shoot(GhostGesture.Bet, 2.6, new Vector3(0.1f, 0.05f, 0.3f), "ghost_03_bet");
            Shoot(GhostGesture.AllIn, 1.7, new Vector3(0.1f, 0.03f, 0.25f), "ghost_04_allin");
            Shoot(GhostGesture.LookAtCards, 1.6, new Vector3(0f, 0.15f, 0.15f), "ghost_05_look");

            foreach (var f in new[] { "ghost_01_tutorial_peek", "ghost_01b_tutorial_peek_low_eye", "ghost_02_hint_knock", "ghost_03_bet", "ghost_04_allin", "ghost_05_look" })
                Assert.IsTrue(File.Exists(Path.Combine(_dir, f + ".png")), f);
        }

        void Shoot(GhostGesture g, double at, Vector3 aim, string name)
        {
            _extra.Hide();
            _extra.Play(g);
            for (double t = 0; t < at; t += 1.0 / 72) _extra.Tick(1f / 72f, _rig.Camera.transform.position);
            Assert.Greater(_extra.Alpha, 0.3f, name + " visible");
            Aim(aim);
            Save(name);
        }

        void Aim(Vector3 tableTarget)
        {
            _rig.Camera.transform.rotation = Quaternion.LookRotation(tableTarget - _rig.Camera.transform.position);
        }

        void Save(string name)
        {
            var cam = _rig.Camera;
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
            var px = tex.GetPixels32();
            int differ = 0, cyan = 0;
            Color32 room = Room;
            for (int i = 0; i < px.Length; i += 97)
            {
                if (Mathf.Abs(px[i].r - room.r) + Mathf.Abs(px[i].g - room.g) + Mathf.Abs(px[i].b - room.b) > 30) differ++;
                // 示範手的青色：藍、綠明顯高於紅
                if (px[i].b > px[i].r + 25 && px[i].g > px[i].r + 15) cyan++;
            }
            Assert.Greater(differ, px.Length / 97 / 10, name + " is mostly empty");
            Assert.Greater(cyan, 15, name + ": no ghost-hand pixels");
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
