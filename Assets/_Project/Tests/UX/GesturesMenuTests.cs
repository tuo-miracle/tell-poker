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
using Object = UnityEngine.Object;

namespace Poker.Tests.UX
{
    /// <summary>手腕選單的「How to play」頁（2026-10-15 由 Gestures 頁瘦身）：7 個示範、Replay tutorial、Back。</summary>
    public class HowToPlayMenuModelTests
    {
        MenuModel Make(out FakeActions acts, UxSettings s = null)
        {
            acts = new FakeActions();
            return new MenuModel(acts, new PauseState(), s ?? new UxSettings());
        }

        [Test] public void MainPage_HasAHowToPlayEntry()
        {
            var m = Make(out _);
            CollectionAssert.Contains(m.Items(MenuPage.Main), MenuItem.HowToPlay);
            m.Open();
            m.Press(MenuItem.HowToPlay);
            Assert.AreEqual(MenuPage.HowToPlay, m.Page);
            Assert.IsTrue(m.IsOpen);
        }

        /// <summary>
        /// 每個示範對到既有的示範手：Check＝敲桌、Show cards＝攤牌翻牌、Bet＝下注片段（片段開頭 2.6 s 就是捏起籌碼，所以捏＋丟都看得到）。
        /// Roll peek、Pick up、Chips 不在選單（示範手片段留給教學與即時提示用）。
        /// </summary>
        [Test] public void Demos_MapToTheExistingGhosts_InCatalogOrder()
        {
            var m = Make(out _);
            var ghosts = m.Items(MenuPage.HowToPlay).Select(MenuModel.GhostOf).Where(g => g.HasValue).Select(g => g.Value).ToArray();
            CollectionAssert.AreEqual(new[] { GhostGesture.Peek, GhostGesture.Bet, GhostGesture.Knock, GhostGesture.Fold, GhostGesture.AllIn, GhostGesture.Flip, GhostGesture.Talk }, ghosts);
            CollectionAssert.IsSubsetOf(ghosts, GhostCatalog.All);
            CollectionAssert.AreEqual(GhostCatalog.All.Where(ghosts.Contains).ToArray(), ghosts, "same order as the catalog");
            foreach (var gone in new[] { GhostGesture.RollPeek, GhostGesture.LookAtCards, GhostGesture.PickChips })
            {
                CollectionAssert.DoesNotContain(ghosts, gone);
                Assert.IsNotNull(GhostCatalog.ClipName(gone), gone + " clip stays for the tutorial and tips");
            }
            Assert.IsNull(MenuModel.GhostOf(MenuItem.Resume));
        }

        [Test] public void SelectingAGesture_StartsItsDemo_AndKeepsTheMenuOpen()
        {
            var m = Make(out _);
            int changed = 0;
            m.Changed += () => changed++;
            m.Open();
            m.Press(MenuItem.HowToPlay);
            Assert.IsNull(m.Demo, "nothing selected yet");
            m.Press(MenuItem.GhostKnock);
            Assert.AreEqual(GhostGesture.Knock, m.Demo);
            Assert.IsTrue(m.IsOpen);
            m.Press(MenuItem.GhostAllIn);
            Assert.AreEqual(GhostGesture.AllIn, m.Demo);
            Assert.AreEqual(4, changed);
            m.Press(MenuItem.Back);
            Assert.IsNull(m.Demo, "leaving the page stops the demo");
            m.Press(MenuItem.HowToPlay);
            m.Press(MenuItem.GhostFold);
            m.Close();
            Assert.IsNull(m.Demo, "closing the menu stops the demo");
        }

        [Test] public void ReplayTutorial_IsOnHowToPlay_CallsTheGame_AndClosesTheMenu()
        {
            var m = Make(out var acts);
            CollectionAssert.DoesNotContain(m.Items(MenuPage.Settings), MenuItem.ReplayTutorial);
            m.Open();
            m.Press(MenuItem.HowToPlay);
            m.Press(MenuItem.ReplayTutorial);
            CollectionAssert.AreEqual(new[] { "tutorial" }, acts.Calls);
            Assert.IsFalse(m.IsOpen);
        }

        [Test] public void DemoPose_PutsTheGhostJustBelowTheMenu_FacingTheTableDirection()
        {
            var head = new Vector3(0.1f, 1.2f, -0.3f);
            var headRot = Quaternion.Euler(10f, 20f, 0f);
            var tableRot = Quaternion.Euler(0f, 15f, 0f);
            var clipCenter = new Vector3(0.05f, 0.04f, 0.2f);
            var pose = MenuPlacement.GhostDemo(head, headRot, tableRot, clipCenter);
            Assert.AreEqual(tableRot, pose.rotation, "moves keep the table's forward (a push still goes away from you)");
            var center = pose.position + pose.rotation * clipCenter;
            var a = MenuPlacement.AnglesFromHead(head, headRot, center);
            Assert.That(Mathf.Abs(a.x), Is.LessThan(MenuPlacement.MaxYaw), "in view horizontally");
            Assert.That(a.y, Is.InRange(-MenuPlacement.MaxPitch - 15f, -MenuPlacement.MaxPitch + 2f), "just below the panel " + a);
            Assert.That((center - head).magnitude, Is.InRange(0.3f, 0.6f), "within reach-ish distance");
        }
    }

    /// <summary>接到遊戲：選單開著時收起提示、How to play 頁的示範手在暫停中照樣播、Hints 開關、Replay tutorial、字幕大小、各頁版面。</summary>
    public class GesturesMenuIntegrationTests
    {
        GameObject _root, _head, _gameGo;
        GameController _game;
        QueuedHands _hands;
        UxController _ux;
        string _dir;

        void Build(UxSettings saved = null)
        {
            _dir = Path.Combine(Path.GetTempPath(), "ux-ghost-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var settings = Path.Combine(_dir, "ux-settings.json");
            if (saved != null) SettingsStore.Save(saved, settings);
            _root = new GameObject("TestTable");
            var table = _root.AddComponent<TableFrame>();
            table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.EnvironmentRaycast);
            _root.AddComponent<TableView>();
            _head = new GameObject("Head");
            var cam = _head.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            _head.transform.position = new Vector3(0f, 0.45f, -0.3f);
            _head.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.1f, 0.5f) - _head.transform.position);
            _hands = new GameObject("Hands").AddComponent<QueuedHands>();
            _gameGo = new GameObject("Game");
            _gameGo.SetActive(false);
            _game = _gameGo.AddComponent<GameController>();
            _game.Table = table;
            _game.Head = _head.transform;
            _game.HandsSource = _hands;
            _game.Config = new GameConfig { MonteCarloTrials = 80 };
            _game.AiThinkScale = 0.25f;
            _game.ProgressPath = Path.Combine(_dir, "progress.json");
            _game.TutorialEnabled = false;
            _gameGo.SetActive(true);
            _ux = UxController.Attach(_game, settings, handTracking: new FakeHandTrackingMode());
        }

        [TearDown] public void TearDown()
        {
            foreach (var go in new[] { _gameGo, _root, _head, _hands != null ? _hands.gameObject : null, _dev != null ? _dev.gameObject : null })
                if (go) Object.Destroy(go);
            if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true);
            AudioListener.pause = false;
        }

        IEnumerator UntilInitialized()
        {
            for (int i = 0; i < 300 && !_game.IsInitialized; i++) yield return null;
            Assert.IsTrue(_game.IsInitialized);
            yield return null;
        }

        [UnityTest] public IEnumerator OpeningTheMenu_HidesGameHints()
        {
            Build();
            yield return UntilInitialized();
            Assert.IsFalse(_game.MenuOpen);
            _ux.Menu.Open();
            Assert.IsTrue(_game.MenuOpen);
            _ux.Menu.Close();
            Assert.IsFalse(_game.MenuOpen);
            Assert.IsNotNull(_game.TutorialVoice, "voice state is wired for the tutorial and hints");
            Assert.IsFalse(_game.TutorialVoice.Available, "voice chat is off by default");
        }

        [UnityTest] public IEnumerator GestureDemo_PlaysWhilePaused_InFrontOfThePlayer()
        {
            Build();
            yield return UntilInitialized();
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.HowToPlay);
            _ux.Menu.Press(MenuItem.GhostFold);
            Assert.IsFalse(_game.AutoStep, "game is paused");
            float end = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < end) yield return null;
            var demo = _ux.DemoGhost;
            Assert.AreEqual(GhostGesture.Fold, demo.Current);
            Assert.IsTrue(demo.IsVisible);
            Assert.Greater(demo.PlaybackTime, 0.05, "ticks with unscaled time while paused");
            Assert.AreEqual(GhostCatalog.Caption(GhostGesture.Fold), demo.CaptionText);
            var center = demo.Root.TransformPoint(demo.ClipCenter);
            var a = MenuPlacement.AnglesFromHead(_head.transform.position, _head.transform.rotation, center);
            Assert.Less(Mathf.Abs(a.x), MenuPlacement.MaxYaw, "in view");
            Assert.Less(a.y, -MenuPlacement.MaxPitch + 2f, "below the panel, not behind it");
            StringAssert.Contains("fold", _ux.View.Title.ToLowerInvariant(), "the panel title shows the caption");
            _ux.Menu.Close();
            Assert.IsFalse(demo.IsVisible, "closing the menu hides the demo");
        }

        /// <summary>Hints（Settings 頁，2026-10-15）：一個開關同時管手勢提示（HintsEnabled）與牌力提示（HandHintsEnabled）。</summary>
        [UnityTest] public IEnumerator HintsToggle_ReachesBothHintSystems_AndTextSizeReachesCaptions()
        {
            Build(new UxSettings { Hints = false, HandHints = false, SubtitleScale = 1.5f });
            yield return UntilInitialized();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsFalse(_game.HintsEnabled, "saved Off is applied at start");
            Assert.IsFalse(_game.HandHintsEnabled, "saved Off is applied at start");
            Assert.AreEqual(1.5f, _game.Ghost.CaptionScale, 1e-4f, "ghost captions follow Text size");
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            yield return null;
            Assert.AreEqual("Hints: Off", Flat(_ux.View.LabelText(MenuItem.Hints)));
            Assert.AreEqual("Text size: 1.5x", Flat(_ux.View.LabelText(MenuItem.TextSize)));
            _ux.Menu.Press(MenuItem.Hints);
            Assert.IsTrue(_game.HintsEnabled);
            Assert.IsTrue(_game.HandHintsEnabled);
            var saved = SettingsStore.Load(_ux.SettingsPath);
            Assert.IsTrue(saved.Hints && saved.HandHints, "saved On (both)");
            Assert.AreEqual("Hints: On", Flat(_ux.View.LabelText(MenuItem.Hints)));
            _ux.Menu.Press(MenuItem.Hints);
            Assert.IsFalse(_game.HintsEnabled);
            Assert.IsFalse(_game.HandHintsEnabled);
            _ux.Menu.Press(MenuItem.Back);
            _ux.Menu.Press(MenuItem.HowToPlay);
            _ux.Menu.Press(MenuItem.GhostKnock);
            Assert.AreEqual(1.5f, _ux.DemoGhost.CaptionScale, 1e-4f, "the menu demo caption too");
        }

        /// <summary>舊存檔只關了其中一種提示：兩種都關（尊重玩家關掉的那個），選單顯示 Off。</summary>
        [UnityTest] public IEnumerator SavedHandHintsOff_TurnsBothHintsOff()
        {
            Build(new UxSettings { Hints = true, HandHints = false });
            yield return UntilInitialized();
            Assert.IsFalse(_game.HintsEnabled);
            Assert.IsFalse(_game.HandHintsEnabled);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            yield return null;
            Assert.AreEqual("Hints: Off", Flat(_ux.View.LabelText(MenuItem.Hints)));
        }

        /// <summary>Text size 按鈕：每按一次換下一個大小，字幕、籌碼標籤、示範手字幕馬上跟著變，按鈕顯示目前的值。</summary>
        [UnityTest] public IEnumerator TextSize_CyclesAndShowsTheValue()
        {
            Build();
            yield return UntilInitialized();
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            yield return null;
            Assert.AreEqual("Text size: 1.0x", Flat(_ux.View.LabelText(MenuItem.TextSize)));
            var want = new[] { ("1.25x", 1.25f), ("1.5x", 1.5f), ("0.85x", 0.85f), ("1.0x", 1f) };
            foreach (var (label, scale) in want)
            {
                _ux.Menu.Press(MenuItem.TextSize);
                Assert.AreEqual("Text size: " + label, Flat(_ux.View.LabelText(MenuItem.TextSize)));
                Assert.AreEqual(scale, _game.BetLabel.Scale, 1e-4f);
                Assert.AreEqual(scale, _game.Ghost.CaptionScale, 1e-4f);
                Assert.AreEqual(scale, SettingsStore.Load(_ux.SettingsPath).SubtitleScale, 1e-4f, "saved");
            }
        }

        /// <summary>Left-handed：On → 選單按鈕換到右手（非慣用手）；Off 回到自動（系統慣用手，Editor 沒有就右手 → 按鈕在左手）。</summary>
        [UnityTest] public IEnumerator LeftHanded_MovesTheMenuButtonToTheRightHand()
        {
            Build();
            yield return UntilInitialized();
            Assert.AreEqual(Poker.Gestures.Hand.Left, _ux.WristHand);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            yield return null;
            Assert.AreEqual("Left-handed: Off", Flat(_ux.View.LabelText(MenuItem.LeftHanded)));
            _ux.Menu.Press(MenuItem.LeftHanded);
            Assert.AreEqual("Left-handed: On", Flat(_ux.View.LabelText(MenuItem.LeftHanded)));
            Assert.AreEqual(Poker.Gestures.Hand.Right, _ux.WristHand);
            Assert.AreEqual(Handedness.Left, SettingsStore.Load(_ux.SettingsPath).Handedness);
            _ux.Menu.Press(MenuItem.LeftHanded);
            Assert.AreEqual(Poker.Gestures.Hand.Left, _ux.WristHand);
            Assert.AreEqual(Handedness.Auto, SettingsStore.Load(_ux.SettingsPath).Handedness);
        }

        [UnityTest] public IEnumerator ReplayTutorial_FromTheMenu()
        {
            Build();
            yield return UntilInitialized();
            Assert.IsNull(_game.Tutorial);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.HowToPlay);
            _ux.Menu.Press(MenuItem.ReplayTutorial);
            Assert.IsNotNull(_game.Tutorial);
            Assert.AreEqual(TutorialStep.Peek, _game.Tutorial.Step);
            Assert.IsFalse(_ux.Menu.IsOpen);
        }

        [UnityTest] public IEnumerator SkipTutorial_FromTheMainPage_EndsTheLesson()
        {
            Build();
            yield return UntilInitialized();
            _game.ReplayTutorial();
            Assert.IsTrue(_game.Tutorial.IsActive);
            _ux.Menu.Open();
            CollectionAssert.Contains(_ux.Menu.Items(MenuPage.Main), MenuItem.SkipTutorial);
            yield return null;
            var skip = _ux.View.ButtonCenterLocal(MenuItem.SkipTutorial);   // 畫面上真的有這顆按鈕
            var resume = _ux.View.ButtonCenterLocal(MenuItem.Resume);
            Assert.AreEqual(resume.y, skip.y, 1e-4f, "same first row as Resume");
            _ux.Menu.Press(MenuItem.SkipTutorial);
            Assert.IsFalse(_game.Tutorial.IsActive);
            Assert.IsTrue(_game.Tutorial.SkippedByPlayer);
            Assert.IsFalse(_ux.Menu.IsOpen);
            _ux.Menu.Open();
            yield return null;
            CollectionAssert.DoesNotContain(_ux.Menu.Items(MenuPage.Main), MenuItem.SkipTutorial);
            Assert.Throws<KeyNotFoundException>(() => _ux.View.ButtonCenterLocal(MenuItem.SkipTutorial), "the button is gone too");
            _ux.Menu.Close();
        }

        /// <summary>
        /// Fool Rocco 原型（2026-10-15）：玩家版（場景裡沒有開發工具）主頁沒有這顆按鈕；
        /// 開發工具場景（TELL_DEV_TOOLS=1，有 RecorderPanel）才有，按下去照舊開始情境（拍宣傳片用）。
        /// </summary>
        [UnityTest] public IEnumerator FoolRocco_OnlyInDevToolsScenes()
        {
            Build();
            yield return UntilInitialized();
            Assert.IsTrue(_game.CanStartFoolRocco, "precondition: the scenario itself could start");
            _ux.Menu.Open();
            yield return null;
            CollectionAssert.DoesNotContain(_ux.Menu.Items(MenuPage.Main), MenuItem.FoolRocco, "player build: no Fool Rocco");
            _ux.Menu.Close();
            Object.Destroy(_ux);
            yield return null;
            _dev = new GameObject("RecorderPanel").AddComponent<RecorderPanel>();   // 開發工具場景的標記
            _ux = UxController.Attach(_game, Path.Combine(_dir, "ux-settings.json"), handTracking: new FakeHandTrackingMode());
            _ux.Menu.Open();
            yield return null;
            CollectionAssert.Contains(_ux.Menu.Items(MenuPage.Main), MenuItem.FoolRocco, "dev tools scene: Fool Rocco is there");
            _ux.View.ButtonCenterLocal(MenuItem.FoolRocco);
            _ux.Menu.Press(MenuItem.FoolRocco);
            Assert.IsFalse(_ux.Menu.IsOpen);
            Assert.IsTrue(_game.FoolRoccoActive);
        }

        /// <summary>
        /// 每一頁（含教學中、開發工具場景的主頁）：按鈕都在面板內、不重疊、不壓到標題；按鈕至少 4 × 3 cm（好戳）；
        /// 字在按鈕裡，而且在選單距離（0.45 m）至少 WristMenuView.MinLabelDegrees（1.3°）高。
        /// </summary>
        /// <summary>
        /// 2026-11-03 使用者：「選單也幫我美化」——私人高額牌室：黃銅細框、深色皮革底、墨綠絨布面、象牙色按鈕、襯線字；
        /// 有停留（hover）與按下的狀態色；只用不透明的 Unlit（不加即時燈光、陰影、透明疊層，72 fps）。
        /// </summary>
        [UnityTest] public IEnumerator Menu_LooksLikeAPrivateCardRoom_WithHoverAndPressedStates_Cheaply()
        {
            Build();
            yield return UntilInitialized();
            _ux.Menu.Open();
            yield return null;
            var panel = _ux.View.Panel;
            foreach (var n in new[] { "Frame", "Bg", "Felt", "TitlePlate" }) Assert.IsNotNull(panel.Find(n), n);
            Assert.Greater(panel.Find("Frame").localPosition.z, panel.Find("Bg").localPosition.z, "brass frame behind the leather");
            Assert.Greater(panel.Find("Bg").localPosition.z, panel.Find("Felt").localPosition.z, "felt on top of the leather");
            Assert.Greater(panel.Find("Frame").localScale.x, panel.Find("Bg").localScale.x, "the frame shows as a thin rim");
            Assert.Less(panel.Find("Frame").localScale.x - panel.Find("Bg").localScale.x, 0.012f, "thin");
            Color C(string n) => panel.Find(n).GetComponent<Renderer>().sharedMaterial.color;
            Assert.Greater(C("Frame").r, C("Frame").b + 0.25f, "brass/gold rim");
            Assert.Less(C("Bg").grayscale, 0.25f, "dark leather");
            Assert.Greater(C("Felt").g, C("Felt").r, "green felt");
            Assert.Less(C("Felt").grayscale, 0.3f, "deep green");
            var title = panel.Find("Title").GetComponent<TextMesh>();
            StringAssert.Contains("Baskerville", title.font.name, "serif title");
            var label = panel.Find("Rows/" + MenuItem.Resume + "Label").GetComponent<TextMesh>();
            StringAssert.Contains("Baskerville", label.font.name, "serif labels");
            // 狀態色：平常、停留、按下、正在示範，四種都看得出來
            var rest = WristMenuView.StateColor(WristMenuView.ButtonState.Rest);
            var colors = new[] { rest, WristMenuView.StateColor(WristMenuView.ButtonState.Hover), WristMenuView.StateColor(WristMenuView.ButtonState.Pressed), WristMenuView.StateColor(WristMenuView.ButtonState.Selected) };
            for (int i = 0; i < colors.Length; i++)
                for (int j = i + 1; j < colors.Length; j++)
                    Assert.Greater(Mathf.Abs(colors[i].r - colors[j].r) + Mathf.Abs(colors[i].g - colors[j].g) + Mathf.Abs(colors[i].b - colors[j].b), 0.15f, $"state {i} vs {j}");
            float Diff(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
            Assert.Less(Diff(rest, _ux.View.ButtonColorOf(MenuItem.Resume)), 0.01f);
            _ux.View.NotePressed(MenuItem.Settings);
            Assert.Less(Diff(WristMenuView.StateColor(WristMenuView.ButtonState.Pressed), _ux.View.ButtonColorOf(MenuItem.Settings)), 0.01f, "pressed flash");
            for (float t = 0; t < 0.5f; t += 0.05f) yield return new WaitForSecondsRealtime(0.05f);
            _ux.View.Refresh();
            Assert.Less(Diff(rest, _ux.View.ButtonColorOf(MenuItem.Settings)), 0.01f, "the flash fades");
            // 便宜：不透明 Unlit、沒有陰影、沒有燈
            foreach (var r in panel.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name);
                if (r.TryGetComponent<TextMesh>(out _)) continue;   // 字本身要透明混色（字型貼圖），底板都不透明
                foreach (var m in r.sharedMaterials) Assert.Less(m.renderQueue, 3000, r.name + " is not a transparent overlay");
            }
            Assert.IsEmpty(panel.GetComponentsInChildren<Light>(true));
            Assert.LessOrEqual(panel.GetComponentsInChildren<Renderer>(true).Length, 4 + 2 * _ux.Menu.Items(_ux.Menu.Page).Count + 1, "frame, leather, felt, title plate + plate and label per button + title");
        }

        [UnityTest] public IEnumerator EveryPage_FitsThePanel_WithBigButtonsAndLegibleLabels()
        {
            Build();
            yield return UntilInitialized();
            _ux.Menu.Open();
            yield return null;
            AssertLayout("main");
            MenuShots.Save(_head.GetComponent<Camera>(), "ux-menu-main");
            _ux.Menu.Press(MenuItem.HowToPlay);
            yield return null;
            AssertLayout("how to play");
            MenuShots.Save(_head.GetComponent<Camera>(), "ux-menu-howtoplay-test");
            _ux.Menu.Press(MenuItem.GhostFlip);
            yield return null;
            AssertLayout("how to play with a demo");
            _ux.Menu.Press(MenuItem.Back);
            _ux.Menu.Press(MenuItem.Settings);
            yield return null;
            AssertLayout("settings");
            MenuShots.Save(_head.GetComponent<Camera>(), "ux-menu-settings-test");
            _ux.Menu.Press(MenuItem.TextSize);
            _ux.Menu.Press(MenuItem.LeftHanded);
            yield return null;
            AssertLayout("settings after edits");
            _ux.Menu.Close();
            _game.ReplayTutorial();
            _ux.Menu.Open();
            yield return null;
            CollectionAssert.Contains(_ux.Menu.Items(MenuPage.Main), MenuItem.SkipTutorial);
            AssertLayout("main during the tutorial");
            MenuShots.Save(_head.GetComponent<Camera>(), "ux-menu-main-tutorial-test");
            _ux.Menu.Close();
        }

        RecorderPanel _dev;

        static string Flat(string label) => label.Replace("\n", " ");

        void AssertLayout(string context)
        {
            // 2026-11-03 選單美化：字至少約 1.3°（使用者要清楚好按；同 GhostCaptionPlacement.MinLineDegrees 再多 0.1°）
            const float MinAngle = WristMenuView.MinLabelDegrees;
            var panel = _ux.View.Panel;
            var rows = panel.Find("Rows");
            var half = MenuPlacement.PanelSize * 0.5f;
            var title = panel.Find("TitlePlate");
            var rects = new List<Rect> { new Rect(title.localPosition.x - title.localScale.x / 2f, title.localPosition.y - title.localScale.y / 2f, title.localScale.x, title.localScale.y) };
            foreach (var item in _ux.Menu.Items(_ux.Menu.Page))
            {
                var what = context + ": " + item;
                var plate = rows.Find(item.ToString());
                Assert.IsNotNull(plate, what + " has a button");
                var c = plate.localPosition; var sz = plate.localScale;
                var r = new Rect(c.x - sz.x / 2f, c.y - sz.y / 2f, sz.x, sz.y);
                Assert.That(r.xMin >= -half.x && r.xMax <= half.x && r.yMin >= -half.y && r.yMax <= half.y, what + " inside the panel " + r);
                foreach (var o in rects) Assert.IsFalse(o.Overlaps(r), what + " overlaps " + o);
                rects.Add(r);
                Assert.GreaterOrEqual(sz.x, 0.04f - 1e-4f, what + " at least 4 cm wide");
                Assert.GreaterOrEqual(sz.y, 0.03f - 1e-4f, what + " at least 3 cm tall");

                var label = rows.Find(item + "Label").GetComponent<TextMesh>();
                var b = label.GetComponent<MeshRenderer>().localBounds;
                Assert.Greater(b.size.y, 0f, what + " label mesh exists");
                var lc = (Vector2)label.transform.localPosition + (Vector2)b.center;
                var lr = new Rect(lc.x - b.size.x / 2f, lc.y - b.size.y / 2f, b.size.x, b.size.y);
                Assert.That(lr.xMin >= r.xMin && lr.xMax <= r.xMax && lr.yMin >= r.yMin && lr.yMax <= r.yMax, $"{what} label \"{label.text}\" {lr} inside its button {r}");
                // 字高的量法同 FovAuditTests（characterSize × fontSize × 0.1 ＝ 一個字的高度；FitText 縮小過就跟著變小）
                float lineHeight = label.characterSize * label.fontSize * 0.1f * label.transform.lossyScale.y;
                float deg = Mathf.Atan2(lineHeight, MenuPlacement.Distance) * Mathf.Rad2Deg;
                Assert.GreaterOrEqual(deg, MinAngle, $"{what} label \"{label.text}\" is {deg:F2} deg tall");
                TestContext.Out.WriteLine($"[layout] {what} button {sz.x * 100f:F1}x{sz.y * 100f:F1} cm label \"{label.text.Replace("\n", "|")}\" {deg:F2} deg");
            }
        }
    }
}
