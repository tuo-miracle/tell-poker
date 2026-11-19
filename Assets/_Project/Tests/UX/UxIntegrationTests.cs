using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

namespace Poker.Tests.UX
{
    /// <summary>測試用手部來源：每個 Unity 幀送出佇列中的下一幀（時間重新編成單調遞增）；佇列空了就送最後一幀。</summary>
    public sealed class QueuedHands : MonoBehaviour, IHandsFrameSource
    {
        readonly Queue<HandsFrame> _queue = new Queue<HandsFrame>();
        HandsFrame _current = new HandsFrame();
        double _t;
        public HandsFrame Current => _current;
        public event Action<HandsFrame> FrameUpdated;
        public int Pending => _queue.Count;

        public void Enqueue(IEnumerable<HandsFrame> frames) { foreach (var f in frames) _queue.Enqueue(f); }

        void Update()
        {
            if (_queue.Count > 0) _current = _queue.Dequeue();
            _t += 1.0 / 72.0;
            _current.Time = _t;
            FrameUpdated?.Invoke(_current);
        }
    }

    /// <summary>測試用：記錄每次 SetFastMotion 的參數。</summary>
    public sealed class FakeHandTrackingMode : IHandTrackingMode
    {
        public void Tick() { }
        public readonly List<bool> Calls = new List<bool>();
        public bool IsFastMotion { get; private set; }
        public void SetFastMotion(bool on) { Calls.Add(on); IsFastMotion = on; }
    }

    public class UxIntegrationTests
    {
        GameObject _root, _head, _gameGo;
        TableFrame _table;
        GameController _game;
        QueuedHands _hands;
        UxController _ux;
        string _dir;
        readonly List<GestureEvent> _gestures = new List<GestureEvent>();
        readonly List<int> _actions = new List<int>();

        string SettingsPath => Path.Combine(_dir, "ux-settings.json");

        FakeHandTrackingMode _mode;

        void Build(UxSettings saved = null)
        {
            _dir = Path.Combine(Path.GetTempPath(), "ux-int-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            if (saved != null) SettingsStore.Save(saved, SettingsPath);

            _root = new GameObject("TestTable");
            _table = _root.AddComponent<TableFrame>();
            _table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.EnvironmentRaycast);
            _root.AddComponent<TableView>();

            _head = new GameObject("Head");
            var cam = _head.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            _head.transform.position = new Vector3(0f, 0.45f, -0.3f);
            _head.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.1f, 0.5f) - _head.transform.position);

            _hands = new GameObject("Hands").AddComponent<QueuedHands>();

            _gameGo = new GameObject("Game");
            _gameGo.SetActive(false);   // 先設好欄位再啟用，等同場景載入（Awake → UX 掛上 → Start）
            _game = _gameGo.AddComponent<GameController>();
            _game.Table = _table;
            _game.Head = _head.transform;
            _game.HandsSource = _hands;
            _game.Config = new GameConfig { MonteCarloTrials = 80 };
            _game.AiThinkScale = 0.25f;
            // 這裡測的是選單與暫停，不是教學：教學 v3 會等玩家咪牌才讓 AI 行動（2026-10-13），而且存檔不要寫到真的 persistentDataPath
            _game.TutorialEnabled = false;
            _game.ProgressPath = Path.Combine(_dir, "progress.json");
            _gameGo.SetActive(true);
            _mode = new FakeHandTrackingMode();
            _ux = UxController.Attach(_game, SettingsPath, handTracking: _mode);
            _game.GestureSeen += _gestures.Add;
            _game.ActionApplied += (s, c) => _actions.Add(s);
        }

        [TearDown] public void TearDown()
        {
            foreach (var go in new[] { _gameGo, _root, _head, _hands != null ? _hands.gameObject : null })
                if (go) Object.Destroy(go);
            if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true);
            _gestures.Clear(); _actions.Clear();
            AudioListener.pause = false;
        }

        IEnumerator UntilInitialized()
        {
            for (int i = 0; i < 300 && !_game.IsInitialized; i++) yield return null;
            Assert.IsTrue(_game.IsInitialized, "GameController 初始化");
        }

        IEnumerator Drain()
        {
            for (int i = 0; i < 5000 && _hands.Pending > 0; i++) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
        }

        static IEnumerable<HandsFrame> Knock() =>
            GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)).Wait(0.3).Frames;

        IEnumerator UntilPlayerTurn()
        {
            float end = Time.realtimeSinceStartup + 40f;
            while (_game.Flow != GameFlow.PlayerTurn && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(GameFlow.PlayerTurn, _game.Flow, "前置：輪到玩家");
        }

        [UnityTest] public IEnumerator MenuPauseBlocksGestureEventsAndClock()
        {
            Build();
            yield return UntilInitialized();
            yield return UntilPlayerTurn();
            int actionsBefore = _actions.Count;
            _gestures.Clear();
            _ux.Menu.Open();
            Assert.IsFalse(_game.AutoStep, "暫停時 GameController 不 Step");
            double now = _game.Now;
            _hands.Enqueue(Knock());
            yield return Drain();
            Assert.IsEmpty(_gestures, "暫停期間輪到玩家敲桌也沒有手勢事件");
            Assert.AreEqual(actionsBefore, _actions.Count, "暫停期間沒有行動（AI 與玩家）");
            Assert.AreEqual(GameFlow.PlayerTurn, _game.Flow);
            Assert.AreEqual(now, _game.Now, "遊戲時鐘停住（AI 計時不走）");

            _ux.Menu.Press(MenuItem.Resume);
            Assert.IsTrue(_game.AutoStep);
            _hands.Enqueue(Knock());
            yield return Drain();
            Assert.That(_gestures.Exists(e => e.Kind == GestureKind.KnockCheck), "對照組：恢復後同一段敲桌會被辨識");
            Assert.Greater(_game.Now, now);
        }

        [UnityTest] public IEnumerator PlatformResumeDoesNotEndMenuPause()
        {
            Build();
            yield return UntilInitialized();
            _ux.Platform.OnInputFocusLost();
            Assert.IsFalse(_game.AutoStep);
            Assert.IsTrue(AudioListener.pause);
            _ux.Menu.Open();
            _ux.Platform.OnInputFocusAcquired();
            Assert.IsFalse(_game.AutoStep, "選單開著，取回焦點不解除暫停");
            _ux.Platform.OnHmdUnmounted();
            _ux.Menu.Close();
            Assert.IsFalse(_game.AutoStep, "頭盔還沒戴回");
            _ux.Platform.OnHmdMounted();
            Assert.IsTrue(_game.AutoStep);
            Assert.IsFalse(AudioListener.pause);
        }

        /// <summary>
        /// 靈敏度（2026-10-15 起不在玩家選單）：存檔的舊值讀進來回到 1.0，辨識器用預設門檻；
        /// 開發用的注入路徑（UxSettings.GestureSensitivity → TuningScaler，改設定即時套用、不累乘）照舊。
        /// </summary>
        [UnityTest] public IEnumerator Sensitivity_DefaultsToOne_AndTheInjectionPathStillWorks()
        {
            Build(new UxSettings { GestureSensitivity = 1.25f });
            var defaults = new GestureTuning();
            yield return UntilInitialized();
            var t = _game.Recognizer.Tuning;
            Assert.AreEqual(1f, _ux.Settings.GestureSensitivity, "saved dev value is reset");
            Assert.AreEqual(defaults.KnockPeakSpeed, t.KnockPeakSpeed, 1e-5f, "default thresholds");

            _ux.Settings.GestureSensitivity = 1.35f;   // 只有程式（開發）會改
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            _ux.Menu.Press(MenuItem.HighContrast);   // 任何設定改動都會重新套用
            Assert.AreSame(t, _game.Recognizer.Tuning);
            Assert.AreEqual(defaults.KnockPeakSpeed / 1.35f, _game.Recognizer.Tuning.KnockPeakSpeed, 1e-5f, "applied live (not compounded)");
        }

        [UnityTest] public IEnumerator WristPokeOpensMenuInFrontOfPlayer()
        {
            Build();
            yield return UntilInitialized();
            // 左手腕在桌上（非慣用手，預設右手慣用），右手食指從上方點；2026-10-14 起是停留 0.2 s 就按（不用上膛），所以多停幾幀
            var heights = new[] { 0.10f, 0.07f, 0.05f, 0.03f, 0.015f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f, 0.006f };
            var frames = new List<HandsFrame>();
            foreach (var h in heights) frames.Add(Hands.Frame(Hands.Above(h)));
            LogAssert.Expect(LogType.Log, "[UX] menu opened via wrist");
            _hands.Enqueue(frames);
            yield return Drain();
            Assert.IsTrue(_ux.Menu.IsOpen, "手腕按鈕打開選單");
            Assert.IsFalse(_game.AutoStep);

            var panel = _ux.View.Panel;
            Assert.IsTrue(panel.gameObject.activeInHierarchy);
            var head = _head.transform;
            var half = MenuPlacement.PanelSize * 0.5f;
            foreach (var c in new[] { new Vector3(-half.x, -half.y), new Vector3(half.x, half.y), new Vector3(-half.x, half.y), new Vector3(half.x, -half.y) })
            {
                var a = MenuPlacement.AnglesFromHead(head.position, head.rotation, panel.TransformPoint(c));
                Assert.That(Mathf.Abs(a.x), Is.LessThanOrEqualTo(25f));
                Assert.That(Mathf.Abs(a.y), Is.LessThanOrEqualTo(20f));
            }
        }

        /// <summary>掌心朝上叫選單（2026-10-14）：左手（非慣用手）掌心朝上停 0.4 s → 手掌上方出現按鈕（第一次附提示字）→ 右手食指點 → 選單打開，log 記來源。</summary>
        [UnityTest] public IEnumerator PalmUpThenTouch_OpensMenu_WithFirstTimeHint()
        {
            Build();
            yield return UntilInitialized();
            var palm = new System.Numerics.Vector3(-0.22f, 0.18f, 0.02f);
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, palm, hold: 0.6);
            _hands.Enqueue(s.Frames);
            yield return Drain();
            Assert.IsTrue(_ux.View.PalmButtonVisible, "掌心朝上 → 按鈕出現");
            Assert.IsFalse(_ux.Menu.IsOpen);
            StringAssert.Contains("Menu", _ux.View.PalmButtonText);
            StringAssert.Contains("other hand", _ux.View.PalmButtonText, "第一次附提示");
            var world = _ux.View.PalmButtonWorld;
            var center = _table.WorldToTable(world);
            Assert.Less(Vector3.Distance(center, new Vector3(palm.X, palm.Y + PalmMenu.ButtonLift, palm.Z)), 0.02f);

            var c = new System.Numerics.Vector3(center.x, center.y, center.z);
            var n = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0f, 0.45f, -0.3f) - c);
            var s2 = new SyntheticHands();
            GestureScripts.PalmUp(s2, Hand.Left, palm, hold: 0.0, raise: 0.01);
            GestureScripts.IndexTo(s2, Hand.Right, c + n * 0.01f, approach: 0.3, hold: 0.4, from: c + n * 0.12f);
            LogAssert.Expect(LogType.Log, "[UX] menu opened via palm");
            _hands.Enqueue(s2.Frames);
            yield return Drain();
            Assert.IsTrue(_ux.Menu.IsOpen, "點掌上按鈕打開選單");
            Assert.IsTrue(_ux.Settings.PalmMenuHintSeen, "提示只出現第一次（存檔）");
            Assert.IsTrue(SettingsStore.Load(SettingsPath).PalmMenuHintSeen);
            StringAssert.DoesNotContain("other hand", _ux.View.PalmButtonText);
        }

        [UnityTest] public IEnumerator PokingPanelButtonPressesItem()
        {
            Build();
            yield return UntilInitialized();
            _ux.Menu.Open();
            yield return null;
            var panel = _ux.View.Panel;
            var center = _ux.View.ButtonCenterLocal(MenuItem.Resume);
            var frames = new List<HandsFrame>();
            // 食指從面板前方（玩家側＝面板 -Z）往面板推
            foreach (var d in new[] { 0.08f, 0.05f, 0.03f, 0.02f, 0.01f, 0.004f, 0.0f })
            {
                var world = panel.TransformPoint(new Vector3(center.x, center.y, -d));
                var tip = _table.WorldToTable(world);
                var f = new HandsFrame();
                f.Right.Tracked = true;
                for (int i = 0; i < HandPose.JointCount; i++) f.Right.Rotations[i] = System.Numerics.Quaternion.Identity;
                f.Right.Positions[(int)HandJoint.IndexTip] = new NVec(tip.x, tip.y, tip.z);
                frames.Add(f);
            }
            _hands.Enqueue(frames);
            yield return Drain();
            Assert.IsFalse(_ux.Menu.IsOpen, "點「繼續」關閉選單");
            Assert.IsTrue(_game.AutoStep);
        }

        [UnityTest] public IEnumerator HighContrastAddsOutlinesAndRestores()
        {
            Build(new UxSettings { HighContrast = true });
            yield return UntilInitialized();
            yield return null;
            var betZone = UxController.FindDeep(_table.transform, "BetZone").GetComponent<Renderer>();
            Assert.AreEqual((Color32)HighContrast.MarkColor, (Color32)HighContrast.ColorOf(betZone.sharedMaterial), "下注區改成高對比色");
            Assert.IsNotNull(UxController.FindDeep(_game.Props.Stack[0].Go.transform, HighContrast.OutlineName), "籌碼外框");
            Assert.IsNotNull(UxController.FindDeep(_game.Props.PlayerCards[0].transform, HighContrast.OutlineName), "牌外框");

            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            _ux.Menu.Press(MenuItem.HighContrast);
            Assert.AreNotEqual((Color32)HighContrast.MarkColor, (Color32)HighContrast.ColorOf(betZone.sharedMaterial), "關掉後還原");
            var outline = UxController.FindDeep(_game.Props.Stack[0].Go.transform, HighContrast.OutlineName);
            Assert.IsTrue(outline == null || !outline.gameObject.activeSelf);
        }

        [UnityTest] public IEnumerator FastMotionDefaultsOffAndIsAppliedAtStartup()
        {
            Build();
            yield return null;
            CollectionAssert.AreEqual(new[] { false }, _mode.Calls, "啟動時明確送一次「關」（manifest 允許 HIGH，執行期預設要關）");
        }

        [UnityTest] public IEnumerator SavedFastMotionIsAppliedAtStartup()
        {
            Build(new UxSettings { FastMotion = true });
            yield return null;
            CollectionAssert.AreEqual(new[] { true }, _mode.Calls);
        }

        /// <summary>Fast motion（實機 A/B 用）2026-10-15 起不在玩家選單：沒有按鈕，改別的設定也不重送。</summary>
        [UnityTest] public IEnumerator FastMotion_HasNoMenuButton_AndOtherEditsDoNotResendIt()
        {
            Build();
            yield return UntilInitialized();
            _mode.Calls.Clear();
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            yield return null;
            Assert.IsNull(_ux.View.Panel.Find("Rows/FastMotionLabel"), "no Fast motion button");
            _ux.Menu.Press(MenuItem.HighContrast);
            _ux.Menu.Press(MenuItem.TextSize);
            Assert.IsEmpty(_mode.Calls, "editing other settings does not resend Fast Motion");
        }

        [UnityTest] public IEnumerator FastMotionIsReappliedOnAppResume()
        {
            Build(new UxSettings { FastMotion = true });
            yield return null;
            _mode.Calls.Clear();
            _gameGo.SendMessage("OnApplicationPause", true);
            Assert.IsEmpty(_mode.Calls, "進入暫停不送");
            _gameGo.SendMessage("OnApplicationPause", false);
            CollectionAssert.AreEqual(new[] { true }, _mode.Calls, "回到前景重送目前設定");
        }

        [UnityTest] public IEnumerator IsdkGazeTargetsUseGameControllerIds()
        {
            Build(new UxSettings());
            yield return UntilInitialized();
            yield return null;
            var ids = new HashSet<int>();
            foreach (var t in _game.GazeTargets()) ids.Add(t.Id);
            Assert.AreEqual(4, ids.Count, "3 AI＋荷官");
            var tg = _gameGo.AddComponent<TransformGaze>();
            tg.Source = _head.transform;
            var rig = IsdkGazeRig.Build(_gameGo.transform, tg, _head.transform);
            _ux.RegisterGazeTargets(rig);
            CollectionAssert.AreEquivalent(ids, rig.TargetIds, "ISDK 注視目標的編號與 GameController 一致（AI 用座位號、荷官用 DealerGazeId）");
            Assert.IsTrue(ids.Contains(GameController.DealerGazeId));
        }

        [UnityTest] public IEnumerator SubtitleScaleCoversEveryCharacter()
        {
            Build(new UxSettings { SubtitleScale = 1.5f });
            yield return UntilInitialized();
            yield return null;
            foreach (var t in _game.GazeTargets())
                Assert.AreEqual(1.5f, t.Who.Subtitle.Transform.Find("BubbleText").localScale.x, 1e-4f, t.Who.name);
        }

        [UnityTest] public IEnumerator SubtitleScaleAppliesToCharacterBubbles()
        {
            Build(new UxSettings { SubtitleScale = 1.5f });
            yield return UntilInitialized();
            yield return null;
            var bubble = _game.Pip.Subtitle.Transform;
            Assert.AreEqual(1.5f, bubble.Find("BubbleText").localScale.x, 1e-4f);
            Assert.AreEqual(1.5f, bubble.Find("BubbleBg").localScale.x, 1e-4f);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            _ux.Menu.Press(MenuItem.TextSize);   // 1.5 → 0.85（循環的下一個）
            Assert.AreEqual(0.85f, _game.Dealer.Subtitle.Transform.Find("BubbleText").localScale.x, 1e-4f);
        }

        [UnityTest] public IEnumerator SubtitleScaleAndHighContrastApplyToTheBetLabel()
        {
            Build(new UxSettings { SubtitleScale = 1.5f, HighContrast = true });
            yield return UntilInitialized();
            yield return null;
            Assert.AreEqual(1.5f, _game.BetLabel.Scale, 1e-4f);
            Assert.IsTrue(_game.BetLabel.HighContrast);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            _ux.Menu.Press(MenuItem.TextSize);   // 1.5 → 0.85
            Assert.AreEqual(0.85f, _game.BetLabel.Scale, 1e-4f);
            _ux.Menu.Press(MenuItem.HighContrast);
            Assert.IsFalse(_game.BetLabel.HighContrast);
        }

        /// <summary>沒有 TablePlacer 的場景（只有手印校準）：Move table here 退回只顯示雙手手印（舊的 Recalibrate table 行為）。</summary>
        [UnityTest] public IEnumerator MoveTableHere_WithoutAPlacer_ShowsTheTwoHandsPrompt()
        {
            Build();
            var cal = _root.AddComponent<HandSurfaceCalibration>();
            cal.Init(_table, _hands);
            yield return UntilInitialized();
            _hands.Enqueue(new SyntheticHands().Wait(3.0).Frames);   // 雙手平放不動 → 完成校準
            yield return Drain();
            Assert.IsTrue(cal.IsCalibrated, "precondition: calibrated once");
            Assert.IsFalse(cal.PromptVisible);

            _ux.Menu.Open();
            yield return null;
            Assert.AreEqual("Move table here", _ux.View.LabelText(MenuItem.MoveTable).Replace("\n", " "));
            _ux.Menu.Press(MenuItem.MoveTable);
            Assert.IsTrue(cal.PromptVisible, "shows the two-hands prompt (HandSurfaceCalibration.Recalibrate)");
            Assert.IsFalse(cal.IsCalibrated);
            Assert.IsFalse(_ux.Menu.IsOpen);
        }

        /// <summary>
        /// Move table here（2026-10-15）：和系統重新置中同一套（RecenterPlan）——桌子先搬到玩家面前（校準過的也搬），
        /// 面前有真實桌面就對齊，再顯示雙手手印。
        /// </summary>
        [UnityTest] public IEnumerator MoveTableHere_BringsTheTableInFront_ThenPrompts()
        {
            Build();
            var cal = _root.AddComponent<HandSurfaceCalibration>();
            cal.Init(_table, _hands);
            var placer = _root.AddComponent<TablePlacer>();
            yield return UntilInitialized();
            _hands.Enqueue(new SyntheticHands().Wait(3.0).Frames);
            yield return Drain();
            Assert.IsTrue(cal.IsCalibrated, "precondition: a calibrated (locked) table");
            placer.Head = _head.transform;

            // 玩家轉身走開：頭到了別處、面向別的方向
            _head.transform.SetPositionAndRotation(new Vector3(1.5f, 1.2f, 0.8f), Quaternion.Euler(20f, 90f, 0f));
            var expected = TablePlacement.Fallback(_head.transform.position, _head.transform.forward);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.MoveTable);
            Assert.IsFalse(_ux.Menu.IsOpen);
            Assert.Less(Vector3.Distance(expected.position, _table.WorldPose.position), 0.01f, "the table is in front of the player now");
            Assert.Less(Quaternion.Angle(expected.rotation, _table.WorldPose.rotation), 1f);
            Assert.AreEqual(TablePlacementSource.Virtual, _table.Source);
            Assert.IsTrue(cal.PromptVisible, "then the two-hands prompt");
            Assert.IsFalse(cal.IsCalibrated);
        }

        [UnityTest] public IEnumerator DestroyingUxWhilePausedReleasesGame()
        {
            Build();
            yield return UntilInitialized();
            _ux.Menu.Open();
            Assert.IsFalse(_game.AutoStep);
            Object.Destroy(_ux);
            yield return null;
            Assert.IsTrue(_game.AutoStep, "UX 被移除時不能讓遊戲卡在暫停");
            Assert.IsFalse(AudioListener.pause);
        }

        [UnityTest] public IEnumerator DisabledUxIgnoresWristPoke()
        {
            Build();
            yield return UntilInitialized();
            _ux.enabled = false;
            var frames = new List<HandsFrame>();
            foreach (var h in new[] { 0.10f, 0.07f, 0.05f, 0.03f, 0.015f, 0.006f }) frames.Add(Hands.Frame(Hands.Above(h)));
            for (int i = 0; i < 20; i++) frames.Add(Hands.Frame(Hands.Above(0.006f)));   // 停留夠久（停留就按，2026-10-14）
            _hands.Enqueue(frames);
            yield return Drain();
            Assert.IsFalse(_ux.Menu.IsOpen);
            Assert.IsTrue(_game.AutoStep);
        }

        [UnityTest] public IEnumerator BatchModeWithoutOverrideDoesNotTouchUserSettings()
        {
            Assume.That(Application.isBatchMode);
            Build();
            yield return UntilInitialized();
            var other = new GameObject("Game2");
            other.SetActive(false);
            var gc2 = other.AddComponent<GameController>();
            gc2.Table = _table; gc2.Head = _head.transform; gc2.HandsSource = _hands;
            other.SetActive(true);
            try
            {
                var ux2 = UxController.Attach(gc2);   // 沒有 override：批次模式（測試）不讀寫使用者的設定檔
                Assert.IsNull(ux2.SettingsPath);
                Assert.AreEqual(1f, ux2.Settings.GestureSensitivity);
                ux2.Menu.Open();
                ux2.Menu.Press(MenuItem.Settings);
                ux2.Menu.Press(MenuItem.HighContrast);   // 不丟例外、不寫檔
                ux2.Menu.Close();
            }
            finally { Object.Destroy(other); }
            yield return null;
        }
    }
}
