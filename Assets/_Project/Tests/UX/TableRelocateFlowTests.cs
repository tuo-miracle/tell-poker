using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using Poker.UX;
using UnityEngine;
using UnityEngine.TestTools;
using Hand = Poker.Gestures.Hand;
using Object = UnityEngine.Object;
using NVec = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;

namespace Poker.Tests.UX
{
    /// <summary>
    /// 端到端（2026-10-15 實機：校準過的桌子轉向／換房間後改不了）：牌局進行中、桌子是錨點放回的（鎖定），
    /// 玩家到另一個地方把雙手平放 2 s → 桌子搬過去、播確認音、牌局狀態不變（牌與籌碼跟著牌桌）。
    /// </summary>
    public class TableRelocateFlowTests
    {
        GameObject _root, _head, _gameGo;
        TableFrame _table;
        HandSurfaceCalibration _cal;
        GameController _game;
        QueuedHands _hands;
        UxController _ux;
        string _dir;
        readonly List<int> _actions = new List<int>();

        void Build()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ux-reloc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _root = new GameObject("TestTable");
            _table = _root.AddComponent<TableFrame>();
            _table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.Virtual);
            _root.AddComponent<TableView>();
            _hands = new GameObject("Hands").AddComponent<QueuedHands>();
            _cal = _root.AddComponent<HandSurfaceCalibration>();
            _cal.Init(_table, _hands);
            _table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.Restored, TablePlacementSource.HandCalibrated);

            _head = new GameObject("Head");
            var cam = _head.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            _head.transform.position = new Vector3(0f, 0.45f, -0.3f);
            _head.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.1f, 0.5f) - _head.transform.position);

            _cal.Head = _head.transform;   // 不靠 Camera.main（別的測試可能留下場景相機）

            _gameGo = new GameObject("Game");
            _gameGo.SetActive(false);
            _game = _gameGo.AddComponent<GameController>();
            _game.Table = _table;
            _game.Head = _head.transform;
            _game.HandsSource = _hands;
            _game.Config = new GameConfig { MonteCarloTrials = 80 };
            _game.AiThinkScale = 0.25f;
            _game.TutorialEnabled = false;
            _game.ProgressPath = Path.Combine(_dir, "progress.json");
            _gameGo.SetActive(true);
            _ux = UxController.Attach(_game, Path.Combine(_dir, "ux-settings.json"), handTracking: new FakeHandTrackingMode());
            _game.ActionApplied += (s, c) => _actions.Add(s);
        }

        [TearDown] public void TearDown()
        {
            foreach (var go in new[] { _gameGo, _root, _head, _hands != null ? _hands.gameObject : null })
                if (go) Object.Destroy(go);
            if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true);
            _actions.Clear();
            AudioListener.pause = false;
        }

        /// <summary>雙手平放靜止 seconds 秒，在目前牌桌座標的 center（手牌兩側各 15 cm），整組轉 yaw 度。</summary>
        static IEnumerable<HandsFrame> FlatAt(Vector3 center, float yaw, double seconds)
        {
            var s = new SyntheticHands();
            var area = new NVec(0f, 0.025f, HandSurfaceCalibration.HandsToEdge);
            s.Set(Hand.Left, area + new NVec(-0.15f, 0f, 0f)).Set(Hand.Right, area + new NVec(0.15f, 0f, 0f));
            s.Wait(seconds);
            var q = NQuat.CreateFromAxisAngle(NVec.UnitY, yaw * Mathf.Deg2Rad);
            var to = new NVec(center.x, center.y + 0.025f, center.z);
            foreach (var f in s.Frames)
                foreach (var h in new[] { f.Left, f.Right })
                    for (int j = 0; j < HandPose.JointCount; j++)
                    {
                        h.Positions[j] = to + NVec.Transform(h.Positions[j] - area, q);
                        h.Rotations[j] = NQuat.Concatenate(h.Rotations[j], q);
                    }
            return s.Frames;
        }

        [UnityTest] public IEnumerator Relocating_mid_hand_moves_the_table_plays_a_chime_and_keeps_the_hand()
        {
            Build();
            for (int i = 0; i < 300 && !_game.IsInitialized; i++) yield return null;
            Assert.IsTrue(_game.IsInitialized);
            float end = Time.realtimeSinceStartup + 40f;
            while (_game.Flow != GameFlow.PlayerTurn && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(GameFlow.PlayerTurn, _game.Flow, "前置：輪到玩家");
            for (int i = 0; i < 60; i++) yield return null;   // 發牌動畫停下

            var stacks = _game.Session.State.Stacks();
            var street = _game.Session.State.Street;
            int actions = _actions.Count;
            var card = _game.PlayerCard(0).transform;
            var cardLocal = _table.WorldToTable(card.position);
            var tableBefore = _table.WorldPose;

            // 往左 1 m、往前 0.3 m、低 8 cm 的另一張桌子，人向左轉 90°
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"^\[Table\] recalibrated at new spot \(dist=\d\.\d\dm yaw=-9\d°\)"));
            _hands.Enqueue(FlatAt(new Vector3(-1.0f, -0.08f, 0.3f + HandSurfaceCalibration.HandsToEdge), -90f, 3.0));
            for (int i = 0; i < 5000 && _hands.Pending > 0; i++) yield return null;
            for (int i = 0; i < 30; i++) yield return null;

            Assert.That(_table.Source, Is.EqualTo(TablePlacementSource.HandCalibrated));
            Assert.That(Vector3.Distance(_table.WorldPose.position, tableBefore.position), Is.GreaterThan(0.8f));
            Assert.That(Mathf.DeltaAngle(_table.transform.eulerAngles.y, -90f), Is.EqualTo(0f).Within(2f));
            Assert.That(_table.transform.position.y, Is.EqualTo(-0.08f + 0.025f - SurfaceCalibrator.PalmThickness).Within(0.003f), "palms 2.5 cm above the new desk");
            Assert.That(_ux.RelocateChimes, Is.EqualTo(1), "short confirmation sound");
            Assert.That(_cal.PromptVisible, Is.False);

            Assert.AreEqual(GameFlow.PlayerTurn, _game.Flow, "the hand keeps going");
            Assert.That(_actions.Count, Is.EqualTo(actions), "relocating is not a poker action");
            Assert.That(_game.Session.State.Stacks(), Is.EqualTo(stacks));
            Assert.That(_game.Session.State.Street, Is.EqualTo(street));
            Assert.That(Vector3.Distance(_table.WorldToTable(card.position), cardLocal), Is.LessThan(0.005f), "cards follow the table");
        }
    }
}
