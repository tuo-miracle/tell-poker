using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Poker.Game;
using Poker.Runtime;
using Poker.UX;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Poker.Tests.UX
{
    /// <summary>語音對話的同意畫面與 Settings 開關：沒按同意之前語音一律關閉。</summary>
    public class VoiceMenuTests
    {
        MenuModel Make(UxSettings s, out List<UxSettings> saved)
        {
            var m = new MenuModel(new FakeActions(), new PauseState(), s);
            var list = new List<UxSettings>();
            m.SettingsChanged += x => list.Add(x.Clone());
            saved = list;
            m.Open();
            m.Press(MenuItem.Settings);
            return m;
        }

        [Test] public void VoiceIsOffAndUnconsentedByDefault()
        {
            var s = new UxSettings();
            Assert.IsFalse(s.VoiceChat);
            Assert.IsFalse(s.VoiceConsent);
            Assert.IsFalse(s.VoiceActive);
        }

        [Test] public void SettingsPageHasVoiceToggle() =>
            CollectionAssert.Contains(new MenuModel(new FakeActions(), new PauseState(), new UxSettings()).Items(MenuPage.Settings), MenuItem.VoiceChat);

        [Test] public void FirstToggleShowsConsentInsteadOfTurningOn()
        {
            var m = Make(new UxSettings(), out var saved);
            m.Press(MenuItem.VoiceChat);
            Assert.AreEqual(MenuPage.VoiceConsent, m.Page);
            Assert.IsFalse(m.Settings.VoiceChat, "還沒同意，不開");
            Assert.IsEmpty(saved);
            CollectionAssert.AreEqual(new[] { MenuItem.VoiceAccept, MenuItem.VoiceDecline }, m.Items(MenuPage.VoiceConsent));
        }

        [Test] public void AcceptTurnsVoiceOnAndReturnsToSettings()
        {
            var m = Make(new UxSettings(), out var saved);
            m.Press(MenuItem.VoiceChat);
            m.Press(MenuItem.VoiceAccept);
            Assert.IsTrue(m.Settings.VoiceConsent);
            Assert.IsTrue(m.Settings.VoiceChat);
            Assert.IsTrue(m.Settings.VoiceActive);
            Assert.AreEqual(MenuPage.Settings, m.Page);
            Assert.AreEqual(1, saved.Count, "存檔一次");
        }

        [Test] public void DeclineKeepsVoiceOff()
        {
            var m = Make(new UxSettings(), out var saved);
            m.Press(MenuItem.VoiceChat);
            m.Press(MenuItem.VoiceDecline);
            Assert.IsFalse(m.Settings.VoiceChat);
            Assert.IsFalse(m.Settings.VoiceConsent);
            Assert.AreEqual(MenuPage.Settings, m.Page);
        }

        [Test] public void AfterConsentToggleIsDirect()
        {
            var m = Make(new UxSettings { VoiceConsent = true, VoiceChat = true }, out var saved);
            m.Press(MenuItem.VoiceChat);
            Assert.IsFalse(m.Settings.VoiceChat);
            Assert.AreEqual(MenuPage.Settings, m.Page);
            m.Press(MenuItem.VoiceChat);
            Assert.IsTrue(m.Settings.VoiceChat);
            Assert.AreEqual(2, saved.Count);
        }

        [Test] public void SanitizeTurnsOffVoiceWithoutConsent()
        {
            var s = new UxSettings { VoiceChat = true, VoiceConsent = false }.Sanitize();
            Assert.IsFalse(s.VoiceChat);
        }

        [Test] public void ConsentTextNamesTheServices()
        {
            StringAssert.Contains("Google Gemini", WristMenuView.VoiceConsentText);
            StringAssert.Contains("ElevenLabs", WristMenuView.VoiceConsentText);
            StringAssert.Contains("not stored", WristMenuView.VoiceConsentText);
        }
    }

    /// <summary>UxController 在牌局初始化後掛上語音控制器，並依設定開關。</summary>
    public class VoiceUxIntegrationTests
    {
        GameObject _root, _head, _gameGo;
        GameController _game;
        UxController _ux;
        string _dir;

        void Build(UxSettings saved)
        {
            _dir = Path.Combine(Path.GetTempPath(), "ux-voice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, "ux-settings.json");
            SettingsStore.Save(saved, path);
            _root = new GameObject("TestTable");
            var table = _root.AddComponent<TableFrame>();
            table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.EnvironmentRaycast);
            _root.AddComponent<TableView>();
            _head = new GameObject("Head");
            _head.AddComponent<Camera>();
            _head.transform.position = new Vector3(0f, 0.45f, -0.3f);
            _gameGo = new GameObject("Game");
            _gameGo.SetActive(false);
            _game = _gameGo.AddComponent<GameController>();
            _game.Table = table;
            _game.Head = _head.transform;
            _game.Config = new GameConfig { MonteCarloTrials = 40 };
            _gameGo.SetActive(true);
            _ux = UxController.Attach(_game, path, handTracking: new FakeHandTrackingMode());
        }

        [TearDown] public void TearDown()
        {
            foreach (var go in new[] { _gameGo, _root, _head }) if (go) Object.Destroy(go);
            if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true);
            AudioListener.pause = false;
        }

        IEnumerator UntilVoice()
        {
            for (int i = 0; i < 300 && _ux.Voice == null; i++) yield return null;
            Assert.IsNotNull(_ux.Voice, "牌局初始化後掛上 VoiceChatController");
        }

        [UnityTest] public IEnumerator SavedConsentAndToggleEnableVoice()
        {
            Build(new UxSettings { VoiceConsent = true, VoiceChat = true });
            yield return UntilVoice();
            Assert.IsTrue(_ux.Voice.Session.Enabled);
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            _ux.Menu.Press(MenuItem.VoiceChat);
            Assert.IsFalse(_ux.Voice.Session.Enabled, "從 Settings 關掉");
        }

        [UnityTest] public IEnumerator NoConsentKeepsVoiceDisabled()
        {
            Build(new UxSettings { VoiceChat = true });
            yield return UntilVoice();
            Assert.IsFalse(_ux.Voice.Session.Enabled);
            Assert.IsFalse(_ux.Voice.Session.Consented);
        }

        [UnityTest] public IEnumerator ConsentPageFitsInsidePanel()
        {
            Build(new UxSettings());
            yield return UntilVoice();
            _ux.Menu.Open();
            _ux.Menu.Press(MenuItem.Settings);
            _ux.Menu.Press(MenuItem.VoiceChat);
            yield return null;
            Assert.AreEqual(MenuPage.VoiceConsent, _ux.Menu.Page);
            var rows = _ux.View.Panel.Find("Rows");
            var half = MenuPlacement.PanelSize * 0.5f;
            var rects = new List<Rect>();
            foreach (var item in _ux.Menu.Items(MenuPage.VoiceConsent))
            {
                var plate = rows.Find(item.ToString());
                Assert.IsNotNull(plate, item + " 有按鈕");
                var c = plate.localPosition; var sz = plate.localScale;
                var r = new Rect(c.x - sz.x / 2f, c.y - sz.y / 2f, sz.x, sz.y);
                Assert.That(r.xMin >= -half.x && r.xMax <= half.x && r.yMin >= -half.y && r.yMax <= half.y, item + " 在面板內");
                foreach (var o in rects) Assert.IsFalse(o.Overlaps(r), item + " 不重疊");
                rects.Add(r);
            }
            var body = rows.Find("ConsentText");
            Assert.IsNotNull(body, "同意說明文字");
            StringAssert.Contains("Gemini", body.GetComponent<TextMesh>().text);
            _ux.Menu.Press(MenuItem.VoiceAccept);
            Assert.IsTrue(_ux.Voice.Session.Enabled, "同意後開啟");
        }
    }
}
