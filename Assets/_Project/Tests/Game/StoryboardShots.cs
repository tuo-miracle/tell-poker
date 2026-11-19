using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>
    /// Demo 影片的分鏡圖（2026-11-03）：真的牌局（寫實預覽 5 人桌、備案虛擬桌、主場景燈光），玩家視角 1280×720。
    /// 字幕泡泡、標籤、示範手、手印都是遊戲裡的元件與遊戲裡的台詞／文字常數，只是直接叫出來擺好（不跑完整劇情）。
    /// 手動跑：tools/run-unity-tests.sh PlayMode StoryboardShots（輸出 Logs/scratch/storyboard/）。
    /// </summary>
    public class StoryboardShots
    {
        const int W = 1280, H = 720;
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
        static string Dir => Path.Combine(Application.dataPath, "..", "Logs", "scratch", "storyboard");

        sealed class NoHands : IHandsFrameSource
        {
            public HandsFrame Current { get; } = new HandsFrame();
            public event Action<HandsFrame> FrameUpdated { add { } remove { } }
        }

        GameController _g;
        Camera _cam;
        Transform T => _rig.Table.transform;
        Vector3 Eye => T.TransformPoint(RealisticPreview.PlayerEye);

        CharacterPerformer Rocco => _g.Character(2);
        CharacterPerformer Silas => _g.Character(3);
        CharacterPerformer Jace => _g.Character(4);

        void Quiet()
        {
            foreach (var c in _g.AllCharacters()) { c.Speech.StopAll(); c.Subtitle.Hide(); }
            _g.ReadRevealLabel.Hide();
            _g.Props.HideHeadlines();
            _g.Props.HideSummary();
            _g.Ghost.Hide();
            _g.Props.ShowTutorialGlow(TutorialGlow.None);
        }

        void Say(CharacterPerformer c, string text, LookTarget look = LookTarget.Player, Mood mood = Mood.Neutral)
        {
            c.Play(new PerformanceCue { Mood = mood, Look = look, Intensity = 0.8f, Duration = 30f });
            // 走角色的 SpeechChannel（有預錄的台詞會配上聲音，泡泡由它顯示），和遊戲裡一樣
            c.Speech.Say(text, SpeechPriority.Reply);
        }

        void Settle(float secs = 1f)
        {
            for (float t = 0; t < secs; t += 1f / 72f)
            {
                foreach (var c in _g.AllCharacters()) c.Tick(1f / 72f);
                foreach (var b in Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None)) b.Sync(1f / 72f);
                _g.Ghost.Tick(1f / 72f, Eye);
            }
            _g.ReadRevealLabel.Face(Eye);
            _g.Props.FaceLabels(Eye);
        }

        void Shot(string name, Vector3 lookAtTable, float vfov = 60f)
        {
            _cam.transform.position = Eye;
            _cam.transform.LookAt(T.TransformPoint(lookAtTable), T.up);
            _cam.fieldOfView = vfov;
            _cam.aspect = (float)W / H;
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                if (r.enabled) { r.enabled = false; r.enabled = true; }
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else { _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (name != null) File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        Vector3 HeadOf(CharacterPerformer c) => T.InverseTransformPoint(c.Rig.Head.position);

        void Card(int seat, int i, string card, bool up)
        {
            var v = _g.Props.SeatCards[seat][i];
            v.gameObject.SetActive(true);
            v.SetCard(Poker.Core.Card.Parse(card));
            v.SetFaceUp(up);
            var p = _g.Spots.CardOf(seat, i);
            v.transform.localPosition = new Vector3(p.x, TableProps.CardY, p.z);
            v.SetYaw(_g.Spots.CardYaw(seat));
        }

        [UnityTest, Explicit("storyboard for the demo video")]
        public IEnumerator Storyboard()
        {
            Directory.CreateDirectory(Dir);
            _spawned.Add(MrLighting.CreateRig());
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 0.8f;
            floor.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Lit(new Color(0.42f, 0.40f, 0.38f), 0.1f);
            _spawned.Add(floor);

            _rig = new GameTestRig(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40, Opponents = RealisticPreview.Opponents });
            _g = _rig.Game;
            _rig.Table.Place(new Pose(new Vector3(0f, 0.74f, 0f), Quaternion.identity), TablePlacementSource.Virtual);
            _rig.Camera.transform.SetPositionAndRotation(Eye, Quaternion.LookRotation(T.TransformDirection(RealisticPreview.PlayerLook - RealisticPreview.PlayerEye)));
            var preview = new GameObject("RealisticPreview").AddComponent<RealisticPreview>();
            _spawned.Add(preview.gameObject);
            string Glb(string n) => $"Assets/_Project/Art/Characters/{n}/{n}.glb";
            preview.Cast = new[] { "Hank", "Marcus", "Jace", "Reginald", "Erik" }.Select(n => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Glb(n))).ToArray();
            preview.DealerModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Glb("Dealer"));
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.Idle(0.5);
            Assert.AreEqual(5, Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None).Length);
            foreach (var c in _g.AllCharacters()) c.SetViewer(_rig.Camera.transform);

            _cam = new GameObject("StoryCam").AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.33f, 0.31f, 0.29f);
            _cam.nearClipPlane = 0.02f;
            _spawned.Add(_cam.gameObject);
            Shot(null, RealisticPreview.PlayerLook);   // batchmode 第一張的材質會錯，丟掉

            var wide = new Vector3(0f, 0.22f, 0.6f);
            // 1-1 Rocco（Hank）半身＋即時讀人台詞
            Quiet();
            Say(Rocco, LiveLines.For("rocco", LiveCue.PullBack)[0].Text, mood: Mood.Confident);
            Settle(1.2f);
            Shot("1-1", HeadOf(Rocco) + new Vector3(0.05f, -0.12f, 0f), 34f);

            // 2-2 雙手校準提示的手印；2-3 全桌廣角
            Quiet();
            var cal = _rig.Table.gameObject.AddComponent<HandSurfaceCalibration>();
            cal.Init(_rig.Table, new NoHands());
            cal.Recalibrate();
            Settle(0.2f);
            Shot("2-2", new Vector3(0f, 0f, 0.25f), 55f);
            Object.DestroyImmediate(cal);
            foreach (Transform t in T) if (t.name == "HandPrints" || t.name == "RecalibrateButton") t.gameObject.SetActive(false);
            Settle(0.2f);
            Shot("2-3", wide, 70f);

            // 3-1 荷官特寫＋開場白
            Quiet();
            Say(_g.Dealer, DealerLines.FirstHello, mood: Mood.Happy);
            Settle(1.2f);
            Shot("3-1", HeadOf(_g.Dealer) + new Vector3(0.05f, -0.1f, 0f), 34f);

            // 3-2 底牌發光＋咪牌示範手
            Quiet();
            _g.Props.ShowTutorialGlow(TutorialGlow.Cards);
            _g.Ghost.Play(GhostGesture.Peek);
            Settle(0.9f);
            Shot("3-2", _g.Spots.PlayerCard(0) + new Vector3(0.04f, 0.02f, 0.02f), 48f);

            // 3-3 Rocco 加注（下注堆在他面前）＋即時嗆聲
            Quiet();
            _g.Props.Bet[2].SetAmount(30, _g.Config.Denomination);
            _g.Props.Bet[2].Position = _g.Spots.BetOf(2);
            Say(Rocco, LiveLines.For("rocco", LiveCue.Think7)[1].Text, mood: Mood.Confident);
            Settle(1.2f);
            Shot("3-3", HeadOf(Rocco) + new Vector3(0.1f, -0.25f, -0.1f), 45f);

            // 3-4 玩家的籌碼在下注區＋讀人揭曉卡
            Quiet();
            _g.Props.Bet[0].SetAmount(30, _g.Config.Denomination);
            _g.Props.Bet[0].Position = _g.Spots.PlayerBet;
            _g.ReadRevealLabel.Show(GameController.RevealText("Rocco", new LiveBelief { Belief = "weak" }, ReadOutcome.Fooled), _g.ReadRevealSpot);
            Settle(0.3f);
            Shot("3-4", new Vector3(0f, 0.12f, 0.5f), 55f);

            // 4-1 底牌 A♠A♥＋荷官教「等一下再下注」
            Quiet();
            _g.Props.Bet[0].SetAmount(0, _g.Config.Denomination);
            _g.Props.Bet[2].SetAmount(0, _g.Config.Denomination);
            Card(0, 0, "As", true);
            Card(0, 1, "Ah", true);
            Say(_g.Dealer, FoolRocco.DotCoach.Text);
            Settle(1.2f);
            Shot("4-1", new Vector3(0f, 0.2f, 0.45f), 62f);

            // 4-2 Rocco 讀成虛張、跟注
            Quiet();
            Say(Rocco, FoolRocco.RoccoCall.Text, mood: Mood.Confident);
            Settle(1.2f);
            Shot("4-2", HeadOf(Rocco) + new Vector3(0.08f, -0.15f, 0f), 40f);

            // 4-3 攤牌（Rocco 亮 K♣Q♣，牌面 9d 7c 2h Ks 4d）＋「You played me!」
            Quiet();
            var board = new[] { "9d", "7c", "2h", "Ks", "4d" };
            for (int i = 0; i < 5; i++)
            {
                var b = _g.Props.Board[i];
                b.gameObject.SetActive(true);
                b.SetCard(Poker.Core.Card.Parse(board[i]));
                b.SetFaceUp(true);
                var p = _g.Spots.Board(i);
                b.transform.localPosition = new Vector3(p.x, TableProps.CardY, p.z);
            }
            Card(2, 0, "Kc", true);
            Card(2, 1, "Qc", true);
            Say(Rocco, FoolRocco.RoccoPlayed.Text, mood: Mood.Annoyed);
            Settle(1.2f);
            Shot("4-3", new Vector3(-0.25f, 0.3f, 0.6f), 58f);   // 泡泡在玩家視線上方 14° 內（BubblePlacement.MaxPitch），鏡頭抬高一點才完整入鏡

            // 4-4 揭曉卡
            Quiet();
            _g.ReadRevealLabel.Show(FoolRocco.RevealFooled, _g.ReadRevealSpot);
            Settle(0.3f);
            Shot("4-4", new Vector3(0f, 0.15f, 0.55f), 50f);

            // 5-1 Jace（Vex 座）盯回來
            Quiet();
            Jace.SetGazedAt(true);
            Say(Jace, LineBook.For(Archetype.Cold, LineTrigger.StaredAt).First().Text);
            Settle(1.5f);
            Shot("5-1", HeadOf(Jace) + new Vector3(-0.1f, -0.12f, 0f), 40f);
            Jace.SetGazedAt(false);

            // 5-3 攤牌標題（AI 亮牌上方）
            Quiet();
            Card(4, 0, "7h", true);
            Card(4, 1, "2s", true);
            Card(3, 0, "Jd", true);
            Card(3, 1, "Jc", true);
            var at4 = (_g.Spots.CardOf(4, 0) + _g.Spots.CardOf(4, 1)) * 0.5f;
            var at3 = (_g.Spots.CardOf(3, 0) + _g.Spots.CardOf(3, 1)) * 0.5f;
            _g.Props.SeatHeadline[4].Show(HandReveal.HeadlineFor(Intent.Bluff), at4 + new Vector3(0f, 0.105f, 0f));
            _g.Props.SeatHeadline[3].Show(HandReveal.HeadlineFor(Intent.Value), at3 + new Vector3(0f, 0.105f, 0f));
            Settle(0.3f);
            Shot("5-3", new Vector3(0.55f, 0.12f, 0.6f), 50f);

            // 6-1 Silas（Reginald）回話
            Quiet();
            Say(Silas, LineBook.For(Archetype.Veteran, LineTrigger.StaredAt).Last().Text);
            Settle(1.2f);
            Shot("6-1", HeadOf(Silas) + new Vector3(-0.05f, -0.12f, 0f), 40f);

            // 7-1 結算看板
            Quiet();
            foreach (var s in new[] { 0, 2, 3, 4 }) for (int i = 0; i < 2; i++) _g.Props.SeatCards[s][i].gameObject.SetActive(false);
            for (int i = 0; i < 5; i++) _g.Props.Board[i].gameObject.SetActive(false);
            _g.Props.ShowSummary("You win!", new[] { "Hands won: 7 of 20", "Best hand: Full house" });
            Settle(0.3f);
            Shot("7-1", new Vector3(0f, 0.1f, 0.45f), 55f);
        }
#endif
    }
}
