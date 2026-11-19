using System.Collections.Generic;
using NUnit.Framework;
using Poker.Gestures;
using Poker.UX;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NVec = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;

namespace Poker.Tests.UX
{
    /// <summary>以牌桌座標造雙手：左手手背朝上平放，右手食指從上方點左手腕。</summary>
    static class Hands
    {
        public static readonly NVec LeftWrist = new NVec(-0.15f, 0.08f, 0.15f);

        public static HandsFrame Frame(NVec? rightTip, bool leftTracked = true, double time = 0)
        {
            var f = new HandsFrame { Time = time };
            f.Left.Tracked = leftTracked;
            for (int i = 0; i < HandPose.JointCount; i++) f.Left.Rotations[i] = NQuat.Identity; // 手背 +Y 朝上、手指朝 +Z（ISDK：Distal=+Z、Dorsal=+Y）
            f.Left.Positions[(int)HandJoint.Wrist] = LeftWrist;
            f.Left.Positions[(int)HandJoint.Palm] = LeftWrist + new NVec(0, 0, -0.05f);
            // 其他關節不能都留在原點：判定範圍放大後（2026-10-14），「左手食指尖＝原點」會剛好落在「右手腕＝原點」的按鈕裡
            f.Left.Positions[(int)HandJoint.IndexTip] = LeftWrist + new NVec(0.02f, 0, 0.17f);
            f.Right.Positions[(int)HandJoint.Wrist] = new NVec(0.25f, 0.08f, 0.0f);
            f.Right.Tracked = rightTip.HasValue;
            for (int i = 0; i < HandPose.JointCount; i++) f.Right.Rotations[i] = NQuat.Identity;
            if (rightTip.HasValue) f.Right.Positions[(int)HandJoint.IndexTip] = rightTip.Value;
            return f;
        }

        public static NVec Above(float h) => WristMenuButton.ButtonCenter(LeftPose()) + new NVec(0, h, 0);

        static HandPose LeftPose() => Frame(null).Left;
    }

    /// <summary>
    /// 手腕按鈕（2026-10-14 改成次要路徑）：實機回報「用手叫選單很不靈敏」→ 目標放大（顯示 4 cm、判定半寬 3 cm＋邊界 1.5 cm），
    /// 改成「停留就按」：不用先從 3 cm 以上懸停上膛，指尖在按鈕範圍內停 0.2 s 就按下；按鈕那手短暫掉追蹤 0.5 s 內沿用最後的姿勢。
    /// </summary>
    public class WristMenuButtonTests
    {
        const double Dt = 0.02;

        static int Presses(WristMenuButton b, IEnumerable<float> heights, NVec? offset = null, System.Func<int, bool> leftTracked = null, Hand hand = Hand.Left)
        {
            int n = 0, i = 0;
            double t = 0;
            foreach (var h in heights)
            {
                var tip = Hands.Above(h) + (offset ?? NVec.Zero);
                if (b.Step(Hands.Frame(tip, leftTracked == null || leftTracked(i), time: t), hand, t)) n++;
                t += Dt; i++;
            }
            return n;
        }

        static IEnumerable<float> Repeat(float h, int frames) { for (int i = 0; i < frames; i++) yield return h; }
        static IEnumerable<float> Concat(params IEnumerable<float>[] parts) { foreach (var p in parts) foreach (var h in p) yield return h; }

        [Test] public void ButtonSitsOnBackOfWristTowardElbow()
        {
            var c = WristMenuButton.ButtonCenter(Hands.Frame(null).Left);
            Assert.Greater(c.Y, Hands.LeftWrist.Y, "在手背那一側（+Y）");
            Assert.Less(c.Z, Hands.LeftWrist.Z, "往手肘（-Z，手指是 +Z），不是往指節");
        }

        [Test] public void PokeFromAboveAndRest_Opens()
        {
            Assert.AreEqual(1, Presses(new WristMenuButton(), Concat(new[] { 0.10f, 0.06f, 0.04f, 0.02f }, Repeat(0.008f, 12))));
        }

        /// <summary>舊版要求先從 3 cm 以上上膛，手指直接落在手腕上永遠不算（實機很難按）；2026-10-14 起停留就按。</summary>
        [Test] public void FingerAlreadyResting_FiresAfterDwell_NoArmingNeeded()
        {
            Assert.AreEqual(1, Presses(new WristMenuButton(), Repeat(0.008f, 15)));
            Assert.AreEqual(0.2, WristMenuButton.Dwell, 1e-9);
        }

        [Test] public void BriefTouch_DoesNotFire()
        {
            Assert.AreEqual(0, Presses(new WristMenuButton(), Concat(Repeat(0.008f, 7), Repeat(0.12f, 5))), "0.14 s < 0.2 s");
        }

        [Test] public void HoldingDownFiresOnce()
        {
            Assert.AreEqual(1, Presses(new WristMenuButton(), Concat(new[] { 0.08f, 0.04f }, Repeat(0.005f, 60))));
        }

        [Test] public void LargerTarget_OffCenterTouchCounts()
        {
            // 偏 4 cm：舊版（半寬 2.5 cm＋邊界 1 cm）按不到
            Assert.AreEqual(1, Presses(new WristMenuButton(), Repeat(0.01f, 15), new NVec(0.04f, 0f, 0f)));
            Assert.AreEqual(1, Presses(new WristMenuButton(), Repeat(0.01f, 15), new NVec(0f, 0f, -0.04f)));
            Assert.AreEqual(0, Presses(new WristMenuButton(), Repeat(0.01f, 15), new NVec(0.06f, 0f, 0f)), "偏 6 cm 不算");
            Assert.AreEqual(0, Presses(new WristMenuButton(), Repeat(0.06f, 15)), "手背上方 6 cm 不算（不在按鈕上）");
        }

        [Test] public void ButtonHandBrieflyUntracked_StillFires()
        {
            // 食指碰到手腕時，按鈕那手常掉追蹤（手疊手遮擋）
            Assert.AreEqual(1, Presses(new WristMenuButton(), Repeat(0.008f, 15), leftTracked: i => i < 3 || i > 10));
        }

        [Test] public void WrongHandDoesNotFire()
        {
            // 按鈕在右手（慣用左手），左手腕被點不算
            Assert.AreEqual(0, Presses(new WristMenuButton(), Repeat(0.008f, 20), hand: Hand.Right));
        }

        [Test] public void UntrackedButtonHandDoesNotFire()
        {
            Assert.AreEqual(0, Presses(new WristMenuButton(), Repeat(0.008f, 20), leftTracked: _ => false));
        }

        [Test] public void Blocked_DoesNotFire_AndLogsNearMiss()
        {
            var lines = new List<string>();
            var b = new WristMenuButton { Log = new MenuSummonLog { Sink = lines.Add } };
            double t = 0;
            int n = 0;
            foreach (var h in Repeat(0.008f, 20)) { if (b.Step(Hands.Frame(Hands.Above(h), time: t), Hand.Left, t, blocked: true)) n++; t += Dt; }
            Assert.AreEqual(0, n, "拿著牌或籌碼時不開選單");
            Assert.That(lines, Has.Some.StartsWith("[UX] menu near-miss: blocked-holding via=wrist"));
        }

        [Test] public void ApproachWithoutPress_LogsWristMissed()
        {
            var lines = new List<string>();
            var b = new WristMenuButton { Log = new MenuSummonLog { Sink = lines.Add } };
            Assert.AreEqual(0, Presses(b, Concat(new[] { 0.12f, 0.07f }, Repeat(0.03f, 4), new[] { 0.08f, 0.15f, 0.2f })));
            Assert.That(lines, Has.Some.StartsWith("[UX] menu near-miss: wrist-missed via=wrist closest="), string.Join("\n", lines));
        }
    }

    sealed class FakeActions : IMenuActions
    {
        public readonly List<string> Calls = new List<string>();
        public void NewGame() => Calls.Add("new");
        public void MoveTableHere() => Calls.Add("move");
        public void Quit() => Calls.Add("quit");
        public void ReplayTutorial() => Calls.Add("tutorial");
        public bool TutorialActive { get; set; }
        public void SkipTutorial() { Calls.Add("skip"); TutorialActive = false; }
        public bool FoolRoccoAvailable { get; set; }
        public void StartFoolRocco() { Calls.Add("fool"); FoolRoccoAvailable = false; }
    }

    /// <summary>
    /// 選單瘦身（2026-10-15）：評審大約只玩 3 分鐘，舊選單約 30 項（很多是開發留下的）。
    /// 主頁 6 項（教學中多 Skip tutorial）、How to play 9 項、Settings 7 項；Fool Rocco、靈敏度、Fast motion 不在玩家選單。
    /// </summary>
    public class MenuModelTests
    {
        MenuModel Make(out FakeActions acts, out PauseState pause, UxSettings s = null)
        {
            acts = new FakeActions();
            pause = new PauseState();
            return new MenuModel(acts, pause, s ?? new UxSettings());
        }

        [Test] public void MainPage_HasTheSixItems_InOrder()
        {
            var m = Make(out _, out _);
            CollectionAssert.AreEqual(
                new[] { MenuItem.Resume, MenuItem.HowToPlay, MenuItem.MoveTable, MenuItem.Settings, MenuItem.NewGame, MenuItem.Quit },
                m.Items(MenuPage.Main));
        }

        [Test] public void HowToPlayPage_HasTheSevenDemos_ThenReplayTutorialAndBack()
        {
            var m = Make(out _, out _);
            CollectionAssert.AreEqual(
                new[] { MenuItem.GhostPeek, MenuItem.GhostBet, MenuItem.GhostKnock, MenuItem.GhostFold, MenuItem.GhostAllIn, MenuItem.GhostFlip, MenuItem.GhostTalk, MenuItem.ReplayTutorial, MenuItem.Back },
                m.Items(MenuPage.HowToPlay));
        }

        /// <summary>2026-11-03 手勢備援按鈕移除，「Assist buttons」這一項跟著拿掉（其他順序不變）。</summary>
        [Test] public void SettingsPage_HasTheSixItems_InOrder()
        {
            var m = Make(out _, out _);
            CollectionAssert.AreEqual(
                new[] { MenuItem.VoiceChat, MenuItem.Hints, MenuItem.TextSize, MenuItem.HighContrast, MenuItem.LeftHanded, MenuItem.Back },
                m.Items(MenuPage.Settings));
        }

        /// <summary>舊選單的開發項目不在任何一頁（設定值本身保留，見 SettingsStoreTests）。</summary>
        [Test] public void DevLeftovers_AreOnNoPage()
        {
            var m = Make(out var acts, out _);
            var names = new List<string>();
            foreach (MenuPage p in System.Enum.GetValues(typeof(MenuPage)))
                foreach (var it in m.Items(p)) names.Add(it.ToString());
            foreach (var gone in new[] { "FoolRocco", "SensitivityDown", "SensitivityUp", "FastMotion", "GhostRollPeek", "GhostLook", "GhostChips", "HandHints", "SubtitleDown", "SubtitleUp" })
                CollectionAssert.DoesNotContain(names, gone);
            Assert.IsFalse(System.Enum.IsDefined(typeof(MenuItem), "FastMotion"), "no menu item for fast motion");
            Assert.IsFalse(System.Enum.IsDefined(typeof(MenuItem), "SensitivityUp"), "no menu item for sensitivity");
        }

        /// <summary>教學進行中，主頁第一列多一個「Skip tutorial」（和 Resume 同一列）；教學以外不出現（2026-10-13 教學 v3，行為不變）。</summary>
        [Test] public void SkipTutorial_OnlyOnTheMainPage_WhileTheTutorialRuns()
        {
            var m = Make(out var acts, out var pause);
            CollectionAssert.DoesNotContain(m.Items(MenuPage.Main), MenuItem.SkipTutorial);
            acts.TutorialActive = true;
            CollectionAssert.AreEqual(
                new[] { MenuItem.Resume, MenuItem.SkipTutorial, MenuItem.HowToPlay, MenuItem.MoveTable, MenuItem.Settings, MenuItem.NewGame, MenuItem.Quit },
                m.Items(MenuPage.Main));
            m.Open();
            m.Press(MenuItem.SkipTutorial);
            CollectionAssert.AreEqual(new[] { "skip" }, acts.Calls);
            Assert.IsFalse(m.IsOpen, "closes the menu");
            Assert.IsFalse(pause.IsPaused);
            m.Open();
            m.Press(MenuItem.SkipTutorial);
            CollectionAssert.AreEqual(new[] { "skip" }, acts.Calls, "gone once the tutorial is over");
        }

        /// <summary>
        /// Fool Rocco 原型：玩家選單沒有（2026-10-15）；只有開發工具場景（TELL_DEV_TOOLS=1，GameMenuActions 判斷）回報可用時才出現，
        /// 放在 New game 後面。拍片要用，所以程式與入口保留。
        /// </summary>
        [Test] public void FoolRocco_OnlyWhenTheActionsSayAvailable()
        {
            var m = Make(out var acts, out var pause);
            CollectionAssert.DoesNotContain(m.Items(MenuPage.Main), MenuItem.FoolRocco);
            acts.FoolRoccoAvailable = true;
            CollectionAssert.AreEqual(
                new[] { MenuItem.Resume, MenuItem.HowToPlay, MenuItem.MoveTable, MenuItem.Settings, MenuItem.NewGame, MenuItem.FoolRocco, MenuItem.Quit },
                m.Items(MenuPage.Main));
            m.Open();
            m.Press(MenuItem.FoolRocco);
            CollectionAssert.AreEqual(new[] { "fool" }, acts.Calls);
            Assert.IsFalse(m.IsOpen);
            Assert.IsFalse(pause.IsPaused);
        }

        [Test] public void OpenPausesAndResumeUnpauses()
        {
            var m = Make(out _, out var pause);
            m.Open();
            Assert.IsTrue(m.IsOpen);
            Assert.IsTrue(pause.IsPaused);
            m.Press(MenuItem.Resume);
            Assert.IsFalse(m.IsOpen);
            Assert.IsFalse(pause.IsPaused);
        }

        [Test] public void MenuPauseSurvivesPlatformResume()
        {
            var m = Make(out _, out var pause);
            pause.Set(PauseSource.HmdUnmounted, true);
            m.Open();
            pause.Set(PauseSource.HmdUnmounted, false);
            Assert.IsTrue(pause.IsPaused);
        }

        /// <summary>「Move table here」＝舊的 Recalibrate table（改名；動作是把桌子叫到面前再顯示雙手手印，見 UxIntegrationTests）。</summary>
        [Test] public void NewGameMoveTableQuit_CallActionsAndClose()
        {
            foreach (var (item, call) in new[] { (MenuItem.NewGame, "new"), (MenuItem.MoveTable, "move"), (MenuItem.Quit, "quit") })
            {
                var m = Make(out var acts, out var pause);
                m.Open();
                m.Press(item);
                CollectionAssert.AreEqual(new[] { call }, acts.Calls, item.ToString());
                Assert.IsFalse(m.IsOpen, item + " closes the menu");
                Assert.IsFalse(pause.IsPaused);
            }
        }

        [Test] public void SettingsPage_EditsAndSaves_EveryPress()
        {
            var s = new UxSettings();
            var m = Make(out _, out var pause, s);
            int saved = 0;
            m.SettingsChanged += _ => saved++;
            m.Open();
            m.Press(MenuItem.Settings);
            Assert.AreEqual(MenuPage.Settings, m.Page);
            Assert.IsTrue(pause.IsPaused, "still paused on the settings page");
            m.Press(MenuItem.HighContrast);
            Assert.IsTrue(s.HighContrast);
            m.Press(MenuItem.LeftHanded);
            m.Press(MenuItem.TextSize);
            m.Press(MenuItem.Hints);
            Assert.AreEqual(4, saved, "every change notifies (save + apply)");
            m.Press(MenuItem.Back);
            Assert.AreEqual(MenuPage.Main, m.Page);
            Assert.IsTrue(m.IsOpen);
        }

        /// <summary>Hints：一個開關同時管牌力提示（HandHints）與手勢提示（Hints）。</summary>
        [Test] public void Hints_OneToggle_ControlsBothHintSystems()
        {
            var s = new UxSettings();
            Assert.IsTrue(s.Hints && s.HandHints, "both on by default");
            var m = Make(out _, out _, s);
            var seen = new List<(bool, bool)>();
            m.SettingsChanged += x => seen.Add((x.Hints, x.HandHints));
            m.Open();
            m.Press(MenuItem.Hints);
            Assert.IsTrue(s.Hints, "not on the main page");
            m.Press(MenuItem.Settings);
            m.Press(MenuItem.Hints);
            Assert.IsFalse(s.Hints);
            Assert.IsFalse(s.HandHints);
            m.Press(MenuItem.Hints);
            Assert.IsTrue(s.Hints);
            Assert.IsTrue(s.HandHints);
            CollectionAssert.AreEqual(new[] { (false, false), (true, true) }, seen);
        }

        /// <summary>Text size 循環 1.0 → 1.25 → 1.5 → 0.85 → 1.0；不在循環裡的舊存檔值（例如 1.75）回到 1.0。</summary>
        [Test] public void TextSize_Cycles()
        {
            var s = new UxSettings();
            var m = Make(out _, out _, s);
            m.Open();
            m.Press(MenuItem.Settings);
            var seen = new List<float>();
            for (int i = 0; i < 5; i++) { m.Press(MenuItem.TextSize); seen.Add(s.SubtitleScale); }
            CollectionAssert.AreEqual(new[] { 1.25f, 1.5f, 0.85f, 1f, 1.25f }, seen);
            s.SubtitleScale = 1.75f;
            m.Press(MenuItem.TextSize);
            Assert.AreEqual(1f, s.SubtitleScale, 1e-5f, "off-cycle value returns to the default");
            Assert.AreEqual(1.5f, UxSettings.NextTextSize(1.2501f), 1e-5f, "float noise still matches");
        }

        /// <summary>Left-handed：Off＝Auto（系統慣用手，沒有就右手）、On＝Left；舊存檔的 Right 顯示 Off，按一下變 On。</summary>
        [Test] public void LeftHanded_TogglesBetweenAutoAndLeft()
        {
            var s = new UxSettings();
            var m = Make(out _, out _, s);
            m.Open();
            m.Press(MenuItem.Settings);
            Assert.IsFalse(s.LeftHanded);
            m.Press(MenuItem.LeftHanded);
            Assert.AreEqual(Handedness.Left, s.Handedness);
            Assert.IsTrue(s.LeftHanded);
            m.Press(MenuItem.LeftHanded);
            Assert.AreEqual(Handedness.Auto, s.Handedness);
            s.Handedness = Handedness.Right;
            Assert.IsFalse(s.LeftHanded);
            m.Press(MenuItem.LeftHanded);
            Assert.AreEqual(Handedness.Left, s.Handedness);
        }

        [Test] public void HowToPlay_FromMain_AndBack()
        {
            var m = Make(out _, out _);
            m.Open();
            m.Press(MenuItem.HowToPlay);
            Assert.AreEqual(MenuPage.HowToPlay, m.Page);
            m.Press(MenuItem.Back);
            Assert.AreEqual(MenuPage.Main, m.Page);
        }

        [Test] public void ItemsOfOtherPageAreIgnored()
        {
            var m = Make(out var acts, out _);
            m.Open();
            m.Press(MenuItem.HighContrast);   // 主頁沒有這一項
            Assert.IsEmpty(acts.Calls);
            Assert.AreEqual(MenuPage.Main, m.Page);
            Assert.IsFalse(m.Settings.HighContrast);
        }

        [Test] public void ClosedMenuIgnoresPresses()
        {
            var m = Make(out var acts, out _);
            m.Press(MenuItem.Quit);
            Assert.IsEmpty(acts.Calls);
        }

        [Test] public void ToggleOpensAndCloses()
        {
            var m = Make(out _, out var pause);
            m.Toggle();
            Assert.IsTrue(m.IsOpen);
            m.Toggle();
            Assert.IsFalse(m.IsOpen);
            Assert.IsFalse(pause.IsPaused);
        }
    }
}
