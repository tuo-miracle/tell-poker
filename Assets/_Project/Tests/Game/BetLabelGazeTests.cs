using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 看著對手丟進來的籌碼（本街下注堆）約 0.3 秒 → 上方出現「Rocco bet 120 / To call: 115」。
    /// 牌局：按鈕在 3 號，玩家小盲 5、Pip 大盲 10；Rocco 加注到 120、Vex 跟，輪到玩家。
    /// </summary>
    public class BetLabelGazeTests
    {
        GameTestRig _rig;
        readonly List<string> _logs = new List<string>();

        void OnLog(string msg, string stack, LogType t) { if (msg.StartsWith("[UX] bet label")) _logs.Add(msg); }

        [SetUp] public void SetUp() { _logs.Clear(); Application.logMessageReceived += OnLog; }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig?.Dispose();
            _rig = null;
        }

        static PlayerCommand RoccoRaisesOthersCall(HandState st)
        {
            int seat = st.ToAct;
            var la = Engine.GetLegalActions(st, seat);
            if (seat == 2 && st.Street == Street.Preflop && st.CurrentBet < 120) return new Raise(2, 120);
            return la.CanCheck ? (PlayerCommand)new Check(seat) : new Call(seat);
        }

        GameController StartFacingRoccosRaise()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 5, FirstButton = 3, MonteCarloTrials = 40 }, g => g.AiOverride = RoccoRaisesOthersCall);
            _rig.LookFromSeat(new Vector3(0f, 0f, 0.1f));
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.Idle(0.2);
            var s = _rig.Game.Session.State;
            Assert.AreEqual(120, s.Seat(2).StreetBet, "Rocco raised to 120");
            Assert.AreEqual(120, s.Seat(3).StreetBet, "Vex called");
            return _rig.Game;
        }

        Vector3 PileWorld(int seat) => _rig.Table.transform.TransformPoint(_rig.Game.Spots.BetOf(seat) + Vector3.up * 0.01f);
        Vector3 Away => _rig.Table.transform.TransformPoint(new Vector3(0f, 0f, 0.1f));

        [Test]
        public void LookingAtRoccosBet_ShowsAmountAndToCall_LooksAwayHides()
        {
            var g = StartFacingRoccosRaise();
            _rig.LookAt(PileWorld(2));
            _rig.Idle(0.15);
            Assert.IsFalse(g.BetLabel.IsVisible, "not before ~0.3 s");
            _rig.Idle(0.25);
            Assert.IsTrue(g.BetLabel.IsVisible, "after ~0.4 s");
            Assert.AreEqual(2, g.BetLabel.Target);
            Assert.AreEqual("Rocco bet 120", g.BetLabel.Line1);
            Assert.AreEqual("To call: 115", g.BetLabel.Line2);
            // 標籤在那堆籌碼上方
            var labelPos = g.BetLabel.Main.Go.transform.position;
            var pile = PileWorld(2);
            Assert.Greater(labelPos.y, pile.y + 0.03f, "above the pile");
            Assert.Less(new Vector2(labelPos.x - pile.x, labelPos.z - pile.z).magnitude, 0.05f, "right over the pile");
            // 面向玩家
            var toLabel = (labelPos - _rig.Camera.transform.position).normalized;
            Assert.Greater(Vector3.Dot(g.BetLabel.Main.Go.transform.forward, toLabel), 0.95f, "faces the player");
            _rig.Idle(1.0);
            CollectionAssert.AreEqual(new[] { "[UX] bet label seat=2 amount=120 toCall=115" }, _logs, "logged once per show");

            _rig.LookAt(Away);
            _rig.Idle(0.3);
            Assert.IsTrue(g.BetLabel.IsVisible, "lingers briefly");
            _rig.Idle(0.35);
            Assert.IsFalse(g.BetLabel.IsVisible, "hidden ~0.5 s after looking away");
        }

        [Test]
        public void GazingAtThePile_IsNotGazingAtRocco()
        {
            var g = StartFacingRoccosRaise();
            int signalsBefore = g.GazeSignalsSent;
            _rig.LookAt(PileWorld(2));
            _rig.Idle(2.0);
            Assert.IsTrue(g.BetLabel.IsVisible);
            Assert.AreEqual(signalsBefore, g.GazeSignalsSent, "no PlayerSignals gaze");
            Assert.IsFalse(_rig.Signals.Any(x => x.s.GazeSeat == 2), "no gaze signal for Rocco");
            Assert.IsFalse(g.Character(2).IsGazedAt, "no tell amplification");
            Assert.IsFalse(g.Session.AiAt(2).IsGazed);
            Assert.AreEqual(-1, g.GazedSeat);
        }

        [Test]
        public void LookingAtPipsHead_IsCharacterGaze_NotTheLabel()
        {
            var g = StartFacingRoccosRaise();
            _rig.LookAt(g.Pip.Rig.Head.position);
            _rig.Idle(1.3);
            Assert.IsTrue(g.Pip.IsGazedAt);
            Assert.AreEqual(1, g.GazedSeat);
            Assert.AreEqual(1, g.GazeSignalsSent);
            Assert.IsFalse(g.BetLabel.IsVisible, "head gaze does not show a bet label");
        }

        [Test]
        public void BetChanges_LabelUpdatesLive()
        {
            var g = StartFacingRoccosRaise();
            _rig.LookAt(PileWorld(3));
            _rig.Idle(0.5);
            Assert.AreEqual("Vex bet 120", g.BetLabel.Line1);
            Assert.AreEqual("To call: 115", g.BetLabel.Line2);
            // 玩家跟注：還輪到 Pip，Vex 那堆不變，但玩家已經跟平了 → 第二行消失
            g.AutoPlayer = st => new Call(0);
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.State.ToAct == 1 || g.Flow == GameFlow.AiThinking, 5));
            g.AutoPlayer = null;
            _rig.StepIdle();
            Assert.IsTrue(g.BetLabel.IsVisible, "still looking at Vex's pile");
            Assert.AreEqual("Vex bet 120", g.BetLabel.Line1);
            Assert.IsNull(g.BetLabel.Line2, "matched: nothing to call");
            Assert.AreEqual(1, _logs.Count, "a live update is not a new show");
        }

        [Test]
        public void OwnPile_And_EmptyPiles_ShowNothing_PotShowsTotal()
        {
            var g = StartFacingRoccosRaise();
            // 自己的下注（小盲）不標
            _rig.LookAt(_rig.Table.transform.TransformPoint(g.Spots.PlayerBet));
            _rig.Idle(0.6);
            Assert.IsFalse(g.BetLabel.IsVisible, "own bet zone");
            // 翻牌前底池那堆是空的（盲注還在各自面前）
            _rig.LookAt(_rig.Table.transform.TransformPoint(g.Spots.PotPile));
            _rig.Idle(0.6);
            Assert.IsFalse(g.BetLabel.IsVisible, "empty pot pile");

            // 打到翻牌：所有注收進底池（4 × 120 = 480），各家下注堆變空
            g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.State.Street == Street.Flop, 10), _rig.Dump());
            g.AutoPlayer = null;
            Assert.IsTrue(_rig.WaitPlayerTurn(10), _rig.Dump());
            _rig.LookAt(PileWorld(2));
            _rig.Idle(0.6);
            Assert.IsFalse(g.BetLabel.IsVisible, "Rocco's pile is empty on the flop");
            _rig.LookAt(_rig.Table.transform.TransformPoint(g.Spots.PotPile + Vector3.up * 0.01f));
            _rig.Idle(0.6);
            Assert.IsTrue(g.BetLabel.IsVisible, "pot");
            Assert.AreEqual(BetLabel.PotTarget, g.BetLabel.Target);
            Assert.AreEqual("Pot 480", g.BetLabel.Line1);
            Assert.IsNull(g.BetLabel.Line2);
            Assert.IsTrue(_logs.Contains("[UX] bet label seat=pot amount=480 toCall=0"), string.Join("|", _logs));
            Assert.AreEqual(0, g.GazeSignalsSent);
        }

        [Test]
        public void Labels_StayClearOfThePlayersCardsAndBetZone()
        {
            var g = StartFacingRoccosRaise();
            var head = _rig.Camera.transform.position;
            var mine = new[] { g.Spots.PlayerCard(0), g.Spots.PlayerCard(1), g.Spots.PlayerBet }
                .Select(p => _rig.Table.transform.TransformPoint(p)).ToArray();
            foreach (int seat in new[] { 1, 2, 3 })
            {
                _rig.LookAt(Away);
                _rig.Idle(0.7);
                _rig.LookAt(PileWorld(seat));
                _rig.Idle(0.5);
                Assert.IsTrue(g.BetLabel.IsVisible, "seat " + seat);
                foreach (var label in new[] { g.BetLabel.Main, g.BetLabel.Detail })
                {
                    if (!label.Go.activeSelf) continue;
                    var bounds = label.Plate.GetComponent<Renderer>().bounds;
                    foreach (var p in mine)
                    {
                        // 標籤比玩家的牌／下注區更遠 → 擋不到；也不能剛好落在視線上
                        Assert.Greater(Vector3.Distance(head, bounds.center), Vector3.Distance(head, p), "seat " + seat + " label is beyond " + p);
                        Assert.IsFalse(bounds.IntersectRay(new Ray(head, (p - head).normalized), out float d) && d < Vector3.Distance(head, p), "seat " + seat + " occludes " + p);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator Screenshot_RoccoBetLabel()
        {
            var lights = MrLighting.CreateRig();
            try
            {
                var g = StartFacingRoccosRaise();
                yield return null;
                var cam = _rig.Camera;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.33f, 0.31f, 0.29f);
                cam.fieldOfView = 68f;
                _rig.LookAt(PileWorld(2));
                _rig.Idle(0.5);
                Assert.IsTrue(g.BetLabel.IsVisible);
                var dir = Path.Combine(Application.dataPath, "..", "Logs", "scratch");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "bet_label_rocco.png");
                Save(cam, path);
                Debug.Log("[Shots] " + Path.GetFullPath(path));
                Assert.IsTrue(File.Exists(path));
            }
            finally { Object.DestroyImmediate(lights); }
        }

        static void Save(Camera cam, string path)
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
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
