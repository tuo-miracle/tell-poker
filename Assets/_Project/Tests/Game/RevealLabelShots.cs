using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 一手結束的兩種揭曉（2026-11-03 使用者：「美術太難看了，而且文字很長，難懂」）的前後對照截圖：
    /// A＝每個 AI 的攤牌標題（HandReveal.HeadlineFor），B＝讀人揭曉卡（RevealText／教學／Fool Rocco 的常數）。
    /// 字都從程式拿（改字之前跑＝舊樣子、之後跑＝新樣子）。寫實預覽 5 人桌、玩家視角 1280×720。
    /// 手動跑：tools/run-unity-tests.sh PlayMode RevealLabelShots（REVEAL_SHOTS_DIR＝輸出資料夾名，預設 after）。
    /// </summary>
    public class RevealLabelShots
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
        GameController _g;
        Camera _cam;
        Transform T => _rig.Table.transform;
        Vector3 Eye => T.TransformPoint(RealisticPreview.PlayerEye);
        static string Dir => Path.Combine(Application.dataPath, "..", "Logs", "scratch", "round6", System.Environment.GetEnvironmentVariable("REVEAL_SHOTS_DIR") ?? "after");

        void Shot(string name, Vector3 lookAt, float vfov)
        {
            _cam.transform.position = Eye;
            _cam.transform.LookAt(T.TransformPoint(lookAt), T.up);
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

        void Card(int seat, int i, string card)
        {
            var v = _g.Props.SeatCards[seat][i];
            v.gameObject.SetActive(true);
            v.SetCard(Poker.Core.Card.Parse(card));
            v.SetFaceUp(true);
            var p = _g.Spots.CardOf(seat, i);
            v.transform.localPosition = new Vector3(p.x, TableProps.CardY, p.z);
            v.SetYaw(_g.Spots.CardYaw(seat));
        }

        void Reveal(string text)
        {
            _g.ReadRevealLabel.Show(text, _g.ReadRevealSpot);
            _g.ReadRevealLabel.Face(Eye);
        }

        void Quiet()
        {
            foreach (var c in _g.AllCharacters()) { c.Speech.StopAll(); c.Subtitle.Hide(); }
            _g.ReadRevealLabel.Hide();
            _g.Props.HideHeadlines();
        }

        [UnityTest, Explicit("before/after screenshots for review")]
        public IEnumerator EndOfHandReveals()
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
            _cam = new GameObject("RevealCam").AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.33f, 0.31f, 0.29f);
            _cam.nearClipPlane = 0.02f;
            _spawned.Add(_cam.gameObject);
            Shot(null, RealisticPreview.PlayerLook, 60f);

            // A：一般牌局一手結束，四個 AI 的攤牌標題（遊戲的 ShowHeadlines 擺法）
            Quiet();
            var intents = new[] { Intent.Bluff, Intent.Value, Intent.SemiBluff, Intent.Marginal };
            var hole = new[] { ("7h", "2s"), ("Kc", "Qc"), ("Jd", "Td"), ("9c", "9s") };
            for (int seat = 1; seat <= 4; seat++)
            {
                Card(seat, 0, hole[seat - 1].Item1);
                Card(seat, 1, hole[seat - 1].Item2);
                var rev = new HandReveal { Seat = seat, Name = _g.Session.NameOf(seat), Moments = new[] { new RevealMoment { Intent = intents[seat - 1] } } };
                rev.Headline = HandReveal.HeadlineFor(intents[seat - 1]);
                rev.Detail = seat == 1 ? "Acted the opposite" : seat == 2 ? "Kept a poker face" : "Showed it honestly";   // 舊版的第二行（新版不顯示）
                _g.ShowHeadline(seat, rev);
            }
            _g.Props.FaceLabels(Eye);
            Shot("A_showdown_headlines", new Vector3(0f, 0.15f, 0.6f), 75f);

            // B：一般牌局的讀人揭曉卡（讀對）
            Quiet();
            var belief = new LiveBelief { Belief = "weak", Cues = new List<ReadCue> { ReadCue.LongThink, ReadCue.ReLook } };
            Reveal(GameController.RevealText("Rocco", belief, ReadOutcome.Right));
            Shot("B_read_reveal_game", new Vector3(0f, 0.15f, 0.55f), 55f);

            // 教學第 1 手：玩家什麼都沒露出來
            Quiet();
            Reveal(TutorialScript.RevealNothing);
            Shot("B_read_reveal_tutorial", new Vector3(0f, 0.15f, 0.55f), 55f);

            // Fool Rocco（教學第 2 手也用這張）：騙到 Rocco
            Quiet();
            Reveal(FoolRocco.RevealFooled);
            Shot("B_read_reveal_foolrocco", new Vector3(0f, 0.15f, 0.55f), 55f);
        }
#endif
    }
}
