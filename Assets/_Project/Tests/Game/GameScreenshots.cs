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
    /// 從玩家視角輸出截圖（Logs/screenshots/game_*.png）：開局、咪牌、拿籌碼、下注後＋AI 思考、盯著 Pip（破綻放大）、攤牌亮牌。
    /// 背景用暗暖灰模擬 passthrough 的房間。檔案要人眼看過（測試只確認檔案存在、畫面不是空的）。
    /// </summary>
    public class GameScreenshots
    {
        const int W = 1280, H = 960;
        static readonly Color Room = new Color(0.33f, 0.31f, 0.29f);
        GameTestRig _rig;
        GameObject _lights;
        string _dir;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            if (_lights != null) Object.DestroyImmediate(_lights);
        }

        [UnityTest]
        public IEnumerator PlayerView_KeyMoments()
        {
            _dir = Path.Combine(Application.dataPath, "..", "Logs", "screenshots");
            Directory.CreateDirectory(_dir);
            _lights = MrLighting.CreateRig();
            // 第一手玩家先行動；Pip 之後都跟注／過牌，讓這手打到攤牌
            _rig = new GameTestRig(new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 12, FirstButton = 0, MonteCarloTrials = 200 }, g => g.AiThinkScale = 2.5f);
            var cam = _rig.Camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Room;
            cam.fieldOfView = 68f;
            cam.transform.position = new Vector3(0f, 0.42f, -0.22f);
            // 角色建模時借用內建 primitive 的網格，用完延後到這一幀結束才刪：先讓一幀過去
            yield return null;

            // 1. 開局：發完牌、輪到玩家（下注區與籌碼堆發光提示）
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Idle(0.4);
            Aim(new Vector3(0f, 0.1f, 0.42f));
            Save("game_01_deal");

            // 2. 咪牌：手掌在牌上方抬起手腕
            Aim(new Vector3(0f, 0f, 0.2f));
            var peek = GestureScripts.Peek(new SyntheticHands(), Hand.Right, hold: 1.2);
            float best = 0f;
            bool shot = false;
            PlayUntil(peek, () =>
            {
                float d = _rig.Game.PlayerCard(0).PeekDegrees;
                bool peak = !shot && d >= 0.97f * _rig.Game.PlayerCard(0).MaxPeekDegrees; // 掀到最高
                best = Mathf.Max(best, d);
                return peak;
            }, () =>
            {
                Save("game_02_peek");
                cam.fieldOfView = 26f;
                Aim(new Vector3(0f, 0.01f, 0.13f));
                Save("game_02b_peek_closeup");
                cam.fieldOfView = 68f;
                Aim(new Vector3(0f, 0f, 0.2f));
                shot = true;
            });
            Debug.Log($"[Shots] peak peek {best:0.0} deg");
            Assert.IsTrue(shot, "peek reached " + best);
            _rig.Idle(0.5);

            // 3. 從籌碼堆捏起 3 枚，手上方顯示金額
            Aim(new Vector3(0.12f, 0.02f, 0.25f));
            var bet = GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: 0.085f);
            GestureScripts.PlaceAt(bet, Hand.Right, new NV3(0.05f, 0.02f, 0.42f));
            bet.Wait(1.8);
            shot = false;
            PlayUntil(bet, () => !shot && _rig.Game.Recognizer.HeldChips(Hand.Right) >= 3, () => { Save("game_03_holding"); shot = true; });
            Assert.IsTrue(shot, "holding chips");

            // 4. 下注後：籌碼在下注區，Pip 正在想
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.AiThinking, 5), "Pip thinking");
            // 之後的決策固定成跟／過（這一次的決策已經在想了，照原本的走）
            _rig.Game.AiOverride = st => Engine.GetLegalActions(st, 1).CanCheck ? (PlayerCommand)new Check(1) : new Call(1);
            _rig.Idle(0.6);
            Aim(new Vector3(0f, 0.12f, 0.5f));
            Save("game_04_after_bet_ai_thinking");

            // 5. 盯著 Pip 超過 1 秒：破綻放大
            _rig.LookAt(_rig.Game.Pip.Rig.Head.position + Vector3.down * 0.05f);
            _rig.Idle(1.3);
            Assert.IsTrue(_rig.Game.Pip.IsGazedAt);
            Save("game_05_gaze_tell");

            // 6. 打到攤牌：之後 Pip 只跟／過、玩家自動過牌／跟注，直到有一手打到攤牌（兩邊都亮牌）
            _rig.Game.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
            _rig.Game.AiThinkScale = 0.3f;
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver && _rig.Game.PlayerCard(0).FaceUp, 120), "a showdown");
            _rig.Idle(0.2);
            _rig.LookFromSeat(new Vector3(0f, 0.06f, 0.45f));
            Save("game_06_showdown_reveal");

            // 7. 牌面特寫（檢查點數、花色清不清楚）：視角拉窄，像低頭看公共牌
            cam.fieldOfView = 30f;
            _rig.LookFromSeat(new Vector3(0f, 0f, 0.44f));
            Save("game_07_board_closeup");
            cam.fieldOfView = 68f;

            foreach (var f in new[] { "game_01_deal", "game_02_peek", "game_02b_peek_closeup", "game_03_holding", "game_04_after_bet_ai_thinking", "game_05_gaze_tell", "game_06_showdown_reveal", "game_07_board_closeup" })
                Assert.IsTrue(File.Exists(Path.Combine(_dir, f + ".png")), f);
        }

        void Aim(Vector3 tableTarget)
        {
            _rig.Camera.transform.rotation = Quaternion.LookRotation(tableTarget - _rig.Camera.transform.position);
        }

        void PlayUntil(SyntheticHands s, System.Func<bool> when, System.Action action)
        {
            // 拆成單幀播放，條件成立時拍照
            var frames = s.Frames;
            var one = new SyntheticHands();
            foreach (var f in frames)
            {
                one.Frames.Clear();
                one.Frames.Add(f);
                _rig.Play(one);
                if (when()) action();
            }
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
            // 畫面不是一片背景色
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
