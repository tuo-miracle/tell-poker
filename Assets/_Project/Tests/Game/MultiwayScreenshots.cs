using System.Collections;
using System.IO;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>
    /// M2 截圖（Logs/scratch/m2_*.png）：4 人入座的玩家視角、教學四步的發光提示、攤牌亮牌、結算看板、俯視配置。
    /// 背景用暗暖灰模擬 passthrough。檔案要人眼看過（測試只確認檔案存在、畫面不是空的）。
    /// </summary>
    public class MultiwayScreenshots
    {
        const int W = 1280, H = 960;
        static readonly Color Room = new Color(0.33f, 0.31f, 0.29f);
        GameTestRig _rig;
        GameObject _lights;
        string _dir, _save;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.dataPath, "..", "Logs", "scratch");
            Directory.CreateDirectory(_dir);
            _save = Path.Combine(Path.GetTempPath(), "tell_shots_" + System.Guid.NewGuid().ToString("N") + ".json");
            _lights = MrLighting.CreateRig();
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            if (_lights != null) Object.DestroyImmediate(_lights);
            if (File.Exists(_save)) File.Delete(_save);
        }

        void Setup(GameConfig cfg, System.Action<GameController> configure = null)
        {
            _rig = new GameTestRig(cfg, configure);
            var cam = _rig.Camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Room;
            cam.fieldOfView = 68f;
            cam.transform.position = new Vector3(0f, 0.42f, -0.22f);
        }

        [UnityTest]
        public IEnumerator Seated_Tutorial_Showdown_Summary()
        {
            // ---- 教學（第一次玩）：4 人入座＋看牌、下注的發光 ----
            Setup(new GameConfig { Seed = 31, MonteCarloTrials = 60 }, g =>
            {
                g.ProgressPath = _save;
                g.TutorialEnabled = true;
                g.AiThinkScale = 0.6f;
            });
            yield return null;
            var g0 = _rig.Game;

            Assert.IsTrue(_rig.IdleUntil(() => g0.Props.TutorialGlowShown == TutorialGlow.Cards, 10));
            _rig.Idle(0.3);
            Aim(new Vector3(0f, 0.12f, 0.45f));
            Save("m2_01_seated_player_view");
            // 接近 Quest 3 的視野（垂直約 90°）、以及轉頭看 Pip 時的樣子（檢查有沒有貼臉）
            _rig.Camera.fieldOfView = 90f;
            Aim(new Vector3(0f, 0.12f, 0.45f));
            Save("m2_01b_quest_fov");
            _rig.Camera.fieldOfView = 68f;
            _rig.Camera.transform.rotation = Quaternion.LookRotation(g0.Character(1).Rig.Head.position - _rig.Camera.transform.position);
            Save("m2_01c_look_at_pip");
            _rig.Camera.transform.rotation = Quaternion.LookRotation(g0.Character(3).Rig.Head.position - _rig.Camera.transform.position);
            Save("m2_01d_look_at_vex");
            Aim(new Vector3(0f, 0.02f, 0.25f));
            Save("m2_02_tutorial_peek_glow");

            // 教學 v4（2026-10-14）：發完牌先等玩家看牌（AI 還不動）→ 牌力提示 → Rocco 加注、跟或棄 → 揭曉 Rocco 讀到什麼
            _rig.Play(GestureScripts.Peek(new SyntheticHands(), Hand.Right, hold: 1.0));
            Assert.IsNotNull(g0.HandHintText, "hand-strength label");
            Aim(new Vector3(0f, 0.02f, 0.2f));
            Save("m2_02b_hand_hint_label");
            Assert.IsTrue(_rig.IdleUntil(() => g0.Flow == GameFlow.PlayerTurn && g0.Props.TutorialGlowShown == TutorialGlow.ChipsAndBetZone, 15));
            _rig.Idle(0.4);
            Aim(new Vector3(0.05f, 0.02f, 0.3f));
            Save("m2_03_tutorial_bet_glow");

            Toss(2);   // 跟 Rocco 的 30
            Assert.IsTrue(_rig.IdleUntil(() => g0.Flow == GameFlow.HandOver && g0.ReadReveal != null, 40));
            _rig.Idle(0.4);
            Aim(new Vector3(0f, 0.15f, 0.45f));
            Save("m2_04_tutorial_read_reveal");
            Assert.AreEqual(TutorialStep.Bet, g0.Tutorial.Step);
            _rig.Dispose();
            _rig = null;

            // ---- 正式牌局：打到攤牌（所有 AI 只跟／過，玩家也跟／過）----
            Setup(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 60 }, g =>
            {
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
                g.AiThinkScale = 0.3f;
            });
            yield return null;
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 120), "showdown");
            _rig.Idle(0.2);
            Aim(new Vector3(0f, 0.08f, 0.5f));
            Save("m2_06_showdown_reveal");

            // 俯視：檢查座位與桌上物件的配置
            var cam = _rig.Camera;
            cam.transform.position = new Vector3(0f, 1.25f, 0.35f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.fieldOfView = 70f;
            Save("m2_07_topdown_layout");
            _rig.Dispose();
            _rig = null;

            // ---- 一局結束：結算看板 ----
            var cfg = new GameConfig { Seed = 7, MonteCarloTrials = 60, NextHandSeconds = 0.3f, SummarySeconds = 60f };
            cfg.Schedule = new MatchSchedule { MaxHands = 2 };
            Setup(cfg, g =>
            {
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
                g.AiThinkScale = 0.2f;
            });
            yield return null;
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.GameOver, 200), "game over");
            _rig.Idle(0.5);
            Aim(new Vector3(0f, 0.1f, 0.35f));
            Save("m2_08_summary");

            foreach (var f in new[] { "m2_01_seated_player_view", "m2_01b_quest_fov", "m2_01c_look_at_pip", "m2_01d_look_at_vex", "m2_02_tutorial_peek_glow", "m2_02b_hand_hint_label", "m2_03_tutorial_bet_glow",
                         "m2_04_tutorial_read_reveal", "m2_06_showdown_reveal", "m2_07_topdown_layout", "m2_08_summary" })
                Assert.IsTrue(File.Exists(Path.Combine(_dir, f + ".png")), f);
        }

        void Toss(int n)
        {
            var bet = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: n <= 1 ? 0.005f : 0.025f * (n - 1) + 0.012f);
            GestureScripts.PlaceAt(bet, Hand.Right, new NV3(0.0f, 0.02f, 0.40f));
            bet.Wait(2.0);
            _rig.Play(bet);
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
            int differ = 0;
            Color32 room = Room;
            for (int i = 0; i < px.Length; i += 97)
                if (Mathf.Abs(px[i].r - room.r) + Mathf.Abs(px[i].g - room.g) + Mathf.Abs(px[i].b - room.b) > 30) differ++;
            Assert.Greater(differ, px.Length / 97 / 10, name + " is mostly empty");
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
