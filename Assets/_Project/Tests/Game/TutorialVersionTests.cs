using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 教學版本：存檔記「完成的是第幾版教學」。舊存檔只有 TutorialDone = true（舊的 4 步教學）算 v1；
    /// 完成 v1、v2、v3 的玩家都會再教一次 v4（2026-10-14 的短教學），開場由 Dot 說明；教完（或從選單跳過）寫入 TutorialVersion = 4，之後不再教。
    /// 統計（局數、勝場、讀心、對手紀錄）一律保留。
    /// （2026-10-13 實機：裝置上的 tell_progress.json 是舊版教學的 "TutorialDone": true，新教學從沒出現。）
    /// v3 沒有逐步逾時跳過了，原本用 Tutorial.Skip() 一步步跳完的測試改用選單的 SkipTutorial。
    /// </summary>
    public class TutorialVersionTests
    {
        GameTestRig _rig;
        string _path;

        [SetUp] public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "tell_tutver_" + System.Guid.NewGuid().ToString("N") + ".json");

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            foreach (var f in new[] { _path, _path + ".tmp", _path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
        }

        GameTestRig NewRig() => new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 40 }, g =>
        {
            g.ProgressPath = _path;
            g.TutorialEnabled = true;
        });

        /// <summary>舊版（v1）教學完成的存檔：和裝置上那份一樣，沒有 TutorialVersion 欄位。</summary>
        void WriteV1Save() => File.WriteAllText(_path,
            "{\n \"Version\": 1,\n \"TutorialDone\": true,\n \"GamesPlayed\": 3,\n \"GamesWon\": 1,\n \"HandsPlayed\": 41,\n \"HandsWon\": 12,\n" +
            " \"ReadsCorrect\": 5,\n \"ReadsTotal\": 8,\n \"BestPot\": 640,\n \"Opponents\": [ { \"Name\": \"Rocco\", \"GamesPlayed\": 3, \"HandsPlayed\": 41, \"PlayerWon\": 12, \"TheyWon\": 15, \"ReadsCorrect\": 2, \"ReadsTotal\": 4 } ],\n" +
            " \"Gestures\": [ { \"Gesture\": \"Knock\", \"Count\": 4 } ]\n}");

        static void AssertStatsKept(ProgressData d)
        {
            Assert.AreEqual(3, d.GamesPlayed);
            Assert.AreEqual(1, d.GamesWon);
            Assert.AreEqual(41, d.HandsPlayed);
            Assert.AreEqual(12, d.HandsWon);
            Assert.AreEqual(5, d.ReadsCorrect);
            Assert.AreEqual(8, d.ReadsTotal);
            Assert.AreEqual(640, d.BestPot);
            Assert.AreEqual(15, d.Opponent("Rocco").TheyWon);
            Assert.AreEqual(4, d.Successes(GhostGesture.Knock));
        }

        [Test]
        public void OldSave_LoadsAsVersion1()
        {
            WriteV1Save();
            var d = new ProgressStore(_path).Load();
            Assert.IsTrue(d.TutorialDone);
            Assert.AreEqual(1, d.TutorialVersion, "舊存檔沒有欄位：TutorialDone 代表完成了 v1");
            Assert.IsTrue(Tutorial.IsNeeded(d), "v1 < 目前版本：要教新版");
            AssertStatsKept(d);
            Assert.AreEqual(0, new ProgressData().TutorialVersion);
            Assert.IsTrue(Tutorial.IsNeeded(new ProgressData()));
            Assert.IsFalse(Tutorial.IsNeeded(new ProgressData { TutorialDone = true, TutorialVersion = Tutorial.CurrentVersion }));
            Assert.IsTrue(Tutorial.IsNeeded(new ProgressData { TutorialDone = true, TutorialVersion = 2 }), "v2 < v3：再教一次");
            Assert.AreEqual(4, Tutorial.CurrentVersion);
            Assert.IsTrue(Tutorial.IsNeeded(new ProgressData { TutorialDone = true, TutorialVersion = 3 }), "v3 < v4：再教一次（短的 v4）");
        }

        /// <summary>
        /// 重拍用（2026-11-04）：寫實預覽版 adb shell setprop debug.tell.firstrun 1 → 下次啟動當成第一次玩：
        /// 教學從頭、Dot 說「Hi, I'm Dot…」；存檔不清（完成紀錄、統計照留）。
        /// </summary>
        [Test]
        public void FirstRunReplay_DoneSave_GetsTheTutorialAndTheFirstHello_SaveKept()
        {
            File.WriteAllText(_path, "{\n \"Version\": 1,\n \"TutorialDone\": true,\n \"TutorialVersion\": 4,\n \"GamesPlayed\": 3,\n \"GamesWon\": 1,\n \"HandsPlayed\": 41,\n \"HandsWon\": 12,\n" +
                " \"ReadsCorrect\": 5,\n \"ReadsTotal\": 8,\n \"BestPot\": 640,\n \"Opponents\": [ { \"Name\": \"Rocco\", \"GamesPlayed\": 3, \"HandsPlayed\": 41, \"PlayerWon\": 12, \"TheyWon\": 15, \"ReadsCorrect\": 2, \"ReadsTotal\": 4 } ],\n" +
                " \"Gestures\": [ { \"Gesture\": \"Knock\", \"Count\": 4 } ]\n}");
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] first-run replay: version=4 begin \(saved v4 kept\)$"));
            _rig = new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 40 }, g =>
            {
                g.ProgressPath = _path;
                g.TutorialEnabled = true;
                g.ReplayFirstRun = true;
            });
            var game = _rig.Game;
            Assert.IsNotNull(game.Tutorial, "tutorial from the start");
            Assert.IsTrue(game.Tutorial.IsActive);
            Assert.IsTrue(_rig.IdleUntil(() => game.Session.HandsStarted == 1, 2));
            Assert.IsTrue(game.Voice.Said(DealerLines.FirstHello), string.Join(" | ", game.Voice.History));
            var disk = new ProgressStore(_path).Load();
            Assert.AreEqual(4, disk.TutorialVersion, "the save still says the tutorial was done");
            AssertStatsKept(disk);
        }

        [Test]
        public void WithoutFirstRunReplay_DoneSave_SkipsTheTutorial()
        {
            File.WriteAllText(_path, "{\n \"Version\": 1,\n \"TutorialDone\": true,\n \"TutorialVersion\": 4,\n \"GamesPlayed\": 3\n}");
            _rig = NewRig();
            Assert.IsTrue(_rig.Game.Tutorial == null || !_rig.Game.Tutorial.IsActive);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 2));
            Assert.IsFalse(_rig.Game.Voice.Said(DealerLines.FirstHello));
        }

        [Test]
        public void FreshSave_GetsTheTutorial_WithTheFirstTimeHello()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] version=4 begin \(saved v0\)$"));
            _rig = NewRig();
            Assert.IsNotNull(_rig.Game.Tutorial);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 2));
            Assert.IsTrue(_rig.Game.Voice.Said(DealerLines.FirstHello));
            Assert.IsFalse(_rig.Game.Voice.Said(DealerLines.NewTutorialIntro));
        }

        [Test]
        public void Version1Save_GetsTheNewTutorialOnce_WithAnIntroLine_AndKeepsStats()
        {
            WriteV1Save();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] version=4 begin \(saved v1\)$"));
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsNotNull(g.Tutorial, "v1 完成的玩家也要看到新教學");
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 1, 2));
            Assert.IsTrue(g.Voice.Said(DealerLines.NewTutorialIntro), "Dot 先說明有新的手勢要教");

            // 從選單跳過整個教學（v3 沒有逐步逾時跳過）
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] version=4 step=Peek start$"));
            Assert.IsTrue(_rig.IdleUntil(() => g.Props.TutorialGlowShown == TutorialGlow.Cards, 10));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] skipped by player, saved$"));
            g.SkipTutorial();
            Assert.IsFalse(g.Tutorial.IsActive);
            Assert.AreEqual(Tutorial.CurrentVersion, g.Progress.TutorialVersion);
            var disk = new ProgressStore(_path).Load();
            Assert.AreEqual(4, disk.TutorialVersion, "跳過也馬上寫入");
            Assert.IsTrue(disk.TutorialDone);
            AssertStatsKept(disk);
            StringAssert.Contains("\"TutorialVersion\": 4", File.ReadAllText(_path));

            // 下次開：不再教
            _rig.Dispose();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] skipped: already completed v4$"));
            _rig = NewRig();
            Assert.IsNull(_rig.Game.Tutorial);
            AssertStatsKept(_rig.Game.Progress);
        }

        [Test]
        public void Version2Save_GetsTheV4TutorialOnce()
        {
            new ProgressStore(_path).Save(new ProgressData { TutorialDone = true, TutorialVersion = 2, GamesPlayed = 2 });
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] version=4 begin \(saved v2\)$"));
            _rig = NewRig();
            Assert.IsNotNull(_rig.Game.Tutorial);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 2));
            Assert.IsTrue(_rig.Game.Voice.Said(DealerLines.NewTutorialIntro));
        }

        /// <summary>v3（兩手、九個步驟）教完的玩家：v4 短很多、重點換成讀心，再教一次。</summary>
        [Test]
        public void Version3Save_GetsTheV4TutorialOnce()
        {
            new ProgressStore(_path).Save(new ProgressData { TutorialDone = true, TutorialVersion = 3, GamesPlayed = 2 });
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] version=4 begin \(saved v3\)$"));
            _rig = NewRig();
            Assert.IsNotNull(_rig.Game.Tutorial);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 2));
            Assert.IsTrue(_rig.Game.Voice.Said(DealerLines.NewTutorialIntro));
            CollectionAssert.AreEqual(Poker.Core.Card.ParseMany(TutorialScript.Hand1PlayerHole), _rig.Game.Session.PlayerHole, "the v4 lesson hand");
        }

        [Test]
        public void Version4Save_SkipsTheTutorial()
        {
            new ProgressStore(_path).Save(new ProgressData { TutorialDone = true, TutorialVersion = 4, GamesPlayed = 2 });
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Tutorial\] skipped: already completed v4$"));
            _rig = NewRig();
            Assert.IsNull(_rig.Game.Tutorial);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 2));
            StringAssert.StartsWith("Welcome back", _rig.Game.Voice.LastText);
        }

        /// <summary>
        /// 2026-11-04（和結算看板一樣）：回訪開場白不講「tells read N%」——那個數字的定義（棄掉價值注）跟玩家想的「讀到對手」對不上。
        /// 改講已經有預錄片段的「Welcome back! 1 win in 3 so far.」：聲音和字幕同一句。
        /// </summary>
        [Test]
        public void Greeting_NoLongerQuotesTellsRead_AndIsStillVoiced()
        {
            var g = DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 4, GamesPlayed = 3, GamesWon = 1, ReadsCorrect = 5, ReadsTotal = 8 });
            Assert.AreEqual("Welcome back! 1 win in 3 so far.", g);
            Assert.AreEqual("Welcome back! 2 wins in 4 so far.", DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 4, GamesPlayed = 4, GamesWon = 2 }));
            Poker.Characters.SpeechBank.Reload();
            Assert.IsTrue(Poker.Characters.SpeechBank.CanSay("dot", g, out var why), "Dot has audio for the greeting: " + why);
        }

        [Test]
        public void Greeting_DependsOnTheTutorialVersion()
        {
            Assert.AreEqual(DealerLines.FirstHello, DealerLines.Greeting(new ProgressData()));
            Assert.AreEqual(DealerLines.NewTutorialIntro, DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 1, GamesPlayed = 3 }));
            Assert.AreEqual(DealerLines.NewTutorialIntro, DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 2, GamesPlayed = 3 }));
            Assert.AreEqual(DealerLines.NewTutorialIntro, DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 3, GamesPlayed = 3 }));
            StringAssert.StartsWith("Welcome back!", DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 4, GamesPlayed = 3, GamesWon = 1 }));
            Assert.AreNotEqual(DealerLines.NewTutorialIntro, DealerLines.Greeting(new ProgressData { TutorialDone = true, TutorialVersion = 4 }));
        }
    }
}
