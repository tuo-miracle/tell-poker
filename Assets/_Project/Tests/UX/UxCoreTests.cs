using System.IO;
using NUnit.Framework;
using Poker.Gestures;
using Poker.UX;
using UnityEngine;
using UnityEngine.TestTools;
using Hand = Poker.Gestures.Hand;

namespace Poker.Tests.UX
{
    public class SettingsStoreTests
    {
        string _dir;
        string PathOf(string name) => System.IO.Path.Combine(_dir, name);

        [SetUp] public void SetUp()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ux-settings-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        [Test] public void DevMenuProp_OpensOnAValueChange_ZeroCloses()
        {
            // 實機截選單（頭盔沒人戴、沒有手叫不出選單）：寫實預覽版 adb shell setprop debug.tell.menu 1／2／3… 打開、0 關上
            Assert.AreEqual(UxController.DevMenuAction.Open, UxController.DevMenu("", "1"));
            Assert.AreEqual(UxController.DevMenuAction.Open, UxController.DevMenu("1", "2"));
            Assert.AreEqual(UxController.DevMenuAction.None, UxController.DevMenu("2", "2"), "same value: nothing");
            Assert.AreEqual(UxController.DevMenuAction.Close, UxController.DevMenu("2", "0"));
            Assert.AreEqual(UxController.DevMenuAction.None, UxController.DevMenu("", ""), "never set");
            Assert.AreEqual(UxController.DevMenuAction.Page, UxController.DevMenu("1", "settings"), "a page name opens that page");
        }

        [Test] public void OldSaveWithTheRemovedAssistButtonsField_StillLoads_AndTheFieldIsDropped()
        {
            // 2026-11-03 手勢備援按鈕移除：舊存檔還有 "AssistButtons"，照樣讀得進其他欄位；再存一次就不見了
            var p = PathOf("old.json");
            File.WriteAllText(p, "{\"Handedness\":1,\"HighContrast\":true,\"SubtitleScale\":1.25,\"AssistButtons\":false,\"Hints\":true}");
            var r = SettingsStore.Load(p);
            Assert.AreEqual(Handedness.Left, r.Handedness);
            Assert.IsTrue(r.HighContrast);
            Assert.AreEqual(1.25f, r.SubtitleScale, 1e-5f);
            SettingsStore.Save(r, p);
            StringAssert.DoesNotContain("AssistButtons", File.ReadAllText(p));
        }

        [Test] public void RoundTripKeepsAllFields()
        {
            var s = new UxSettings { Handedness = Handedness.Left, HighContrast = true, SubtitleScale = 1.5f, FastMotion = true, PalmMenuHintSeen = true };
            var p = PathOf("s.json");
            SettingsStore.Save(s, p);
            var r = SettingsStore.Load(p);
            Assert.AreEqual(Handedness.Left, r.Handedness);
            Assert.IsTrue(r.HighContrast);
            Assert.AreEqual(1.5f, r.SubtitleScale, 1e-5f);
            Assert.IsTrue(r.FastMotion, "fast motion keeps the saved value (no menu item since 2026-10-15)");
            Assert.IsTrue(r.PalmMenuHintSeen);
        }

        /// <summary>
        /// 存檔遷移（2026-10-15 選單瘦身）：
        /// 1. 靈敏度不在玩家選單了 → 讀檔一律回到 1.0（舊的開發值不會悄悄留著、又沒地方改回來）。
        /// 2. 提示合併成一個開關 → 兩個都開才是 On；舊存檔只要關了其中一個，兩個都關（尊重玩家關掉的那個）。
        /// 3. Fast motion 照存檔值（預設關）。
        /// </summary>
        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(false, false, false)]
        public void Migration_MergesHints_OffWins(bool hints, bool handHints, bool expected)
        {
            var p = PathOf("s.json");
            File.WriteAllText(p, $"{{\"Hints\":{(hints ? "true" : "false")},\"HandHints\":{(handHints ? "true" : "false")}}}");
            var r = SettingsStore.Load(p);
            Assert.AreEqual(expected, r.Hints);
            Assert.AreEqual(expected, r.HandHints);
        }

        [Test] public void Migration_ResetsSensitivity_KeepsFastMotion()
        {
            var p = PathOf("s.json");
            File.WriteAllText(p, "{\"GestureSensitivity\":1.3,\"FastMotion\":true}");
            var r = SettingsStore.Load(p);
            Assert.AreEqual(1f, r.GestureSensitivity, "not exposed any more: back to the default");
            Assert.IsTrue(r.FastMotion);
        }

        [Test] public void Migration_OldFileWithoutHintFields_HasHintsOn()
        {
            var p = PathOf("s.json");
            File.WriteAllText(p, "{ \"Handedness\": 2, \"GestureSensitivity\": 1.2, \"HighContrast\": true }");
            var r = SettingsStore.Load(p);
            Assert.IsTrue(r.Hints && r.HandHints);
            Assert.IsTrue(r.HighContrast);
            Assert.AreEqual(Handedness.Right, r.Handedness, "a saved Right stays (shown as Left-handed: Off)");
        }

        [Test] public void MissingFileGivesDefaults()
        {
            var r = SettingsStore.Load(PathOf("none.json"));
            Assert.AreEqual(Handedness.Auto, r.Handedness);
            Assert.AreEqual(1f, r.GestureSensitivity);
            Assert.IsFalse(r.HighContrast);
            Assert.AreEqual(1f, r.SubtitleScale);
            Assert.IsFalse(r.FastMotion, "Fast Motion 預設關");
        }

        [TestCase("{{{ not json")]
        [TestCase("{\"Handedness\":2,\"GestureSensitiv")]
        [TestCase("")]
        [TestCase("null")]
        public void CorruptFileGivesDefaultsAndIsSetAside(string content)
        {
            var p = PathOf("s.json");
            File.WriteAllText(p, content);
            var r = SettingsStore.Load(p);
            Assert.AreEqual(Handedness.Auto, r.Handedness);
            Assert.AreEqual(1f, r.GestureSensitivity);
            Assert.IsFalse(File.Exists(p), "壞檔要移開，避免下次再讀");
            Assert.IsTrue(File.Exists(p + ".corrupt"));
            // 移開後存檔仍可用
            SettingsStore.Save(new UxSettings { HighContrast = true }, p);
            Assert.IsTrue(SettingsStore.Load(p).HighContrast);
        }

        [Test] public void OutOfRangeValuesAreClamped()
        {
            var p = PathOf("s.json");
            File.WriteAllText(p, "{\"Handedness\":7,\"GestureSensitivity\":9.0,\"HighContrast\":true,\"SubtitleScale\":-3}");
            var r = SettingsStore.Load(p);
            Assert.AreEqual(Handedness.Auto, r.Handedness);
            Assert.AreEqual(1f, r.GestureSensitivity, "reset by the migration");
            Assert.AreEqual(UxSettings.MinSubtitleScale, r.SubtitleScale);
            Assert.IsTrue(r.HighContrast);
            // Sanitize 本身照舊夾住範圍（開發用的 TuningScaler 路徑還在）
            Assert.AreEqual(UxSettings.MaxSensitivity, new UxSettings { GestureSensitivity = 9f }.Sanitize().GestureSensitivity);
        }

        [Test] public void SaveCreatesMissingDirectory()
        {
            var p = System.IO.Path.Combine(_dir, "sub", "s.json");
            SettingsStore.Save(new UxSettings(), p);
            Assert.IsTrue(File.Exists(p));
        }
    }

    public class OvrHandTrackingModeTests
    {
        [Test] public void WithoutXrDoesNotCallOvrPluginAndLogs()
        {
            var m = new OvrHandTrackingMode();
            LogAssert.Expect(LogType.Log, "[UX] FastMotion requested=true result=skipped(no XR)");
            m.SetFastMotion(true);
            Assert.IsFalse(m.IsFastMotion, "沒有 XR 就沒有真的切換");
        }

        [Test] public void RequestSkippedBeforeXrIsSentOnceXrBecomesActive()
        {
            bool xr = false;
            var sent = new System.Collections.Generic.List<bool>();
            var m = new OvrHandTrackingMode(() => xr, on => { sent.Add(on); return 0; });
            m.SetFastMotion(false);   // 實機上 UxController.Setup 比 XR 初始化早約 0.1 s
            m.Tick();
            Assert.IsEmpty(sent, "XR 還沒好就不送");
            xr = true;
            m.Tick();
            CollectionAssert.AreEqual(new[] { false }, sent, "XR 好了補送啟動時的設定");
            m.Tick();
            CollectionAssert.AreEqual(new[] { false }, sent, "只補送一次");
            m.SetFastMotion(true);
            CollectionAssert.AreEqual(new[] { false, true }, sent);
            Assert.IsTrue(m.IsFastMotion);
        }
    }

    public class PauseStateTests
    {
        [Test] public void MenuKeepsPausedWhenPlatformResumes()
        {
            var p = new PauseState();
            p.Set(PauseSource.Menu, true);
            p.Set(PauseSource.FocusLost, true);
            p.Set(PauseSource.FocusLost, false);
            Assert.IsTrue(p.IsPaused, "平台恢復不能解除選單的暫停");
            p.Set(PauseSource.Menu, false);
            Assert.IsFalse(p.IsPaused);
        }

        [Test] public void HmdAndFocusAreIndependent()
        {
            var p = new PauseState();
            p.Set(PauseSource.HmdUnmounted, true);
            p.Set(PauseSource.FocusLost, true);
            p.Set(PauseSource.HmdUnmounted, false);
            Assert.IsTrue(p.IsPaused);
            p.Set(PauseSource.FocusLost, false);
            Assert.IsFalse(p.IsPaused);
        }

        [Test] public void ChangedFiresOnlyOnTransitions()
        {
            var p = new PauseState();
            int n = 0; bool last = false;
            p.Changed += v => { n++; last = v; };
            p.Set(PauseSource.Menu, true);
            p.Set(PauseSource.Menu, true);
            p.Set(PauseSource.FocusLost, true);
            Assert.AreEqual(1, n); Assert.IsTrue(last);
            p.Set(PauseSource.Menu, false);
            Assert.AreEqual(1, n);
            p.Set(PauseSource.FocusLost, false);
            Assert.AreEqual(2, n); Assert.IsFalse(last);
        }
    }

    public class MenuPlacementTests
    {
        static readonly Vector3 Head = new Vector3(0.2f, 1.2f, -0.3f);

        static void AssertPanelInFov(Quaternion headRot)
        {
            var pose = MenuPlacement.InFront(Head, headRot);
            var half = MenuPlacement.PanelSize * 0.5f;
            foreach (var c in new[] { new Vector3(-half.x, -half.y), new Vector3(half.x, -half.y), new Vector3(-half.x, half.y), new Vector3(half.x, half.y), Vector3.zero })
            {
                var world = pose.position + pose.rotation * c;
                var a = MenuPlacement.AnglesFromHead(Head, headRot, world);
                Assert.That(Mathf.Abs(a.x), Is.LessThanOrEqualTo(MenuPlacement.MaxYaw), $"yaw {a} corner {c} head {headRot.eulerAngles}");
                Assert.That(Mathf.Abs(a.y), Is.LessThanOrEqualTo(MenuPlacement.MaxPitch), $"pitch {a} corner {c} head {headRot.eulerAngles}");
            }
            // 面板面向玩家
            Assert.Greater(Vector3.Dot(pose.rotation * Vector3.forward, (pose.position - Head).normalized), 0.9f);
        }

        [Test] public void LimitsMatchSpec()
        {
            Assert.AreEqual(25f, MenuPlacement.MaxYaw);
            Assert.AreEqual(20f, MenuPlacement.MaxPitch);
        }

        [Test] public void LevelHead() => AssertPanelInFov(Quaternion.identity);
        [Test] public void LookingDownAtTable() => AssertPanelInFov(Quaternion.Euler(40f, 0f, 0f));
        [Test] public void TurnedAndTilted() => AssertPanelInFov(Quaternion.Euler(-15f, 120f, 12f));

        [Test] public void AnglesAreSigned()
        {
            var right = MenuPlacement.AnglesFromHead(Vector3.zero, Quaternion.identity, new Vector3(1f, 0f, 1f));
            Assert.AreEqual(45f, right.x, 0.01f);
            var up = MenuPlacement.AnglesFromHead(Vector3.zero, Quaternion.identity, new Vector3(0f, 1f, 1f));
            Assert.AreEqual(45f, up.y, 0.01f);
        }
    }

    public class TuningScalerTests
    {
        [Test] public void NeutralSensitivityCopiesWithoutSharing()
        {
            var src = new GestureTuning();
            var t = TuningScaler.Apply(src, 1f);
            Assert.AreNotSame(src, t);
            foreach (var f in typeof(GestureTuning).GetFields())
                Assert.AreEqual(f.GetValue(src), f.GetValue(t), f.Name);
        }

        [Test] public void HigherSensitivityLowersMainThresholds()
        {
            var src = new GestureTuning { KnockPeakSpeed = 0.5f, PinchStart = 0.6f };
            var t = TuningScaler.Apply(src, 1.25f);
            Assert.AreEqual(0.4f, t.KnockPeakSpeed, 1e-5f);
            Assert.AreEqual(src.FoldPushMinDistance / 1.25f, t.FoldPushMinDistance, 1e-5f);
            Assert.AreEqual(src.AllInStartDistance / 1.25f, t.AllInStartDistance, 1e-5f);
            Assert.AreEqual(0.6f, t.PinchStart, "非主要門檻不變");
            Assert.AreEqual(src.RetractWindow, t.RetractWindow);
            Assert.AreEqual(0.5f, src.KnockPeakSpeed, "來源不被改動");
        }

        [Test] public void LowerSensitivityRaisesThresholds()
        {
            var src = new GestureTuning();
            var t = TuningScaler.Apply(src, 0.8f);
            Assert.AreEqual(src.KnockPeakSpeed / 0.8f, t.KnockPeakSpeed, 1e-5f);
            Assert.AreEqual(src.FoldPushMinDistance / 0.8f, t.FoldPushMinDistance, 1e-5f);
        }

        [Test] public void SensitivityIsClamped()
        {
            var src = new GestureTuning();
            var t = TuningScaler.Apply(src, 99f);
            Assert.AreEqual(src.KnockPeakSpeed / UxSettings.MaxSensitivity, t.KnockPeakSpeed, 1e-5f);
        }

        [Test] public void KeepsOrderingInvariants()
        {
            foreach (var s in new[] { UxSettings.MinSensitivity, 1f, UxSettings.MaxSensitivity })
            {
                var t = TuningScaler.Apply(new GestureTuning(), s);
                Assert.Less(t.KnockStrokeEndSpeed, t.KnockPeakSpeed);
                Assert.Less(t.FoldPushClaimDistance, t.FoldPushMinDistance);
            }
        }
    }

    public class HandednessTests
    {
        [Test] public void ExplicitSettingWins()
        {
            Assert.AreEqual(Hand.Left, HandednessResolver.Dominant(Handedness.Left, Hand.Right));
            Assert.AreEqual(Hand.Right, HandednessResolver.Dominant(Handedness.Right, Hand.Left));
        }

        [Test] public void AutoUsesSystemThenRight()
        {
            Assert.AreEqual(Hand.Left, HandednessResolver.Dominant(Handedness.Auto, Hand.Left));
            Assert.AreEqual(Hand.Right, HandednessResolver.Dominant(Handedness.Auto, null));
        }

        [Test] public void WristButtonGoesOnOtherHand()
        {
            Assert.AreEqual(Hand.Right, HandednessResolver.WristButtonHand(Handedness.Left, null));
            Assert.AreEqual(Hand.Left, HandednessResolver.WristButtonHand(Handedness.Auto, null));
        }
    }
}
