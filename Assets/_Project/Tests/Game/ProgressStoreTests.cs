using System.IO;
using NUnit.Framework;
using Poker.Game;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace Poker.Tests.Game
{
    /// <summary>進度存檔：JSON 讀寫、檔案不存在、損毀容錯、對戰紀錄、開局問候。</summary>
    public class ProgressStoreTests
    {
        string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "tell_progress_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        string P => Path.Combine(_dir, "progress.json");

        [Test]
        public void Missing_GivesFreshProgress()
        {
            var store = new ProgressStore(P);
            var d = store.Load();
            Assert.IsFalse(d.TutorialDone);
            Assert.AreEqual(0, d.GamesPlayed);
            Assert.IsFalse(store.LastLoadRecovered);
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new ProgressStore(P);
            var d = new ProgressData { TutorialDone = true, GamesPlayed = 3, GamesWon = 1, HandsPlayed = 30, HandsWon = 9, ReadsCorrect = 5, ReadsTotal = 8, BestPot = 640 };
            var rocco = d.Opponent("Rocco");
            rocco.HandsPlayed = 30; rocco.TheyWon = 7; rocco.PlayerWon = 9; rocco.ReadsCorrect = 2; rocco.ReadsTotal = 3;
            Assert.IsTrue(store.Save(d));
            var back = new ProgressStore(P).Load();
            Assert.IsTrue(back.TutorialDone);
            Assert.AreEqual(3, back.GamesPlayed);
            Assert.AreEqual(1, back.GamesWon);
            Assert.AreEqual(640, back.BestPot);
            Assert.AreEqual(5f / 8f, back.ReadAccuracy, 1e-5f);
            Assert.AreEqual(7, back.Opponent("Rocco").TheyWon);
            Assert.AreEqual(3, back.Opponent("Rocco").ReadsTotal);
            Assert.AreEqual(1, back.Opponents.Count, "Opponent() finds the existing record");
            StringAssert.Contains("\"TutorialDone\": true", File.ReadAllText(P), "human-readable JSON");
        }

        [Test]
        public void Corrupt_IsBackedUp_AndFreshProgressReturned()
        {
            File.WriteAllText(P, "{ \"TutorialDone\": tr");
            var store = new ProgressStore(P);
            var d = store.Load();
            Assert.IsNotNull(d);
            Assert.IsFalse(d.TutorialDone);
            Assert.IsTrue(store.LastLoadRecovered);
            Assert.IsTrue(File.Exists(P + ".corrupt"), "bad file kept for debugging");
            Assert.IsTrue(store.Save(d), "can save over it afterwards");
            Assert.IsFalse(new ProgressStore(P).Load() == null);
        }

        [Test]
        public void Garbage_AndNegativeNumbers_AreSanitized()
        {
            File.WriteAllText(P, "{\"Version\":1,\"GamesPlayed\":-4,\"GamesWon\":9,\"ReadsCorrect\":7,\"ReadsTotal\":2,\"Opponents\":[{\"Name\":\"\"},null]}");
            var d = new ProgressStore(P).Load();
            Assert.AreEqual(0, d.GamesPlayed);
            Assert.LessOrEqual(d.GamesWon, d.GamesPlayed);
            Assert.LessOrEqual(d.ReadsCorrect, d.ReadsTotal);
            Assert.IsTrue(d.Opponents.TrueForAll(o => o != null && !string.IsNullOrEmpty(o.Name)));
        }

        [Test]
        public void CrashBetweenWriteAndRename_LoadsTheTempFile()
        {
            var store = new ProgressStore(P);
            Assert.IsTrue(store.Save(new ProgressData { TutorialDone = true, GamesPlayed = 2 }));
            // 模擬：新內容寫進 .tmp、舊檔已刪、還沒換名就當機
            File.Move(P, P + ".tmp");
            var d = new ProgressStore(P).Load();
            Assert.IsTrue(d.TutorialDone);
            Assert.AreEqual(2, d.GamesPlayed);
        }

        [Test]
        public void Save_OverAnExistingFile_KeepsNoTempBehind()
        {
            var store = new ProgressStore(P);
            store.Save(new ProgressData { GamesPlayed = 1 });
            store.Save(new ProgressData { GamesPlayed = 2 });
            Assert.AreEqual(2, new ProgressStore(P).Load().GamesPlayed);
            Assert.IsFalse(File.Exists(P + ".tmp"));
        }

        [Test]
        public void EmptyFile_IsTreatedAsCorrupt()
        {
            File.WriteAllText(P, "");
            var store = new ProgressStore(P);
            Assert.IsFalse(store.Load().TutorialDone);
            Assert.IsTrue(store.LastLoadRecovered);
        }

        [Test]
        public void Save_ToAnUnwritablePath_ReturnsFalse_NoThrow()
        {
            // 父目錄其實是一個檔案 → 寫不進去
            var blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "x");
            var store = new ProgressStore(Path.Combine(blocker, "p.json"));
            Assert.IsFalse(store.Save(new ProgressData()));
        }

        [Test]
        public void Greeting_FirstTime_vs_Returning()
        {
            var fresh = new ProgressData();
            Assert.IsFalse(string.IsNullOrEmpty(DealerLines.Greeting(fresh)));
            // 教學有版本了：只有 TutorialDone（舊版教學）會改成「再教新版」的開場，所以這裡標成完成目前版本
            var back = new ProgressData { TutorialDone = true, TutorialVersion = Tutorial.CurrentVersion, GamesPlayed = 4, GamesWon = 2, ReadsCorrect = 3, ReadsTotal = 4 };
            var line = DealerLines.Greeting(back);
            StringAssert.Contains("2", line);
            StringAssert.DoesNotContain("%", line, "2026-11-04 no tells-read percentage in the greeting");
            Assert.Less(line.Length, 70, "one short line");
        }

        [Test]
        public void RecordHand_And_RecordGame_Accumulate()
        {
            var d = new ProgressData();
            var hand = new HandResult { PlayerWon = true, PotWon = 300, SeatsDealt = new[] { 0, 1, 2, 3 } };
            hand.Winners.Add(0);
            hand.Reads.Add(new ReadRecord { Seat = 2, Correct = true });
            hand.Reads.Add(new ReadRecord { Seat = 3, Correct = false });
            d.RecordHand(hand, s => s == 1 ? "Pip" : s == 2 ? "Rocco" : "Vex");
            Assert.AreEqual(1, d.HandsPlayed);
            Assert.AreEqual(1, d.HandsWon);
            Assert.AreEqual(1, d.ReadsCorrect);
            Assert.AreEqual(2, d.ReadsTotal);
            Assert.AreEqual(300, d.BestPot);
            Assert.AreEqual(1, d.Opponent("Pip").HandsPlayed);
            Assert.AreEqual(1, d.Opponent("Pip").PlayerWon);
            Assert.AreEqual(1, d.Opponent("Rocco").ReadsCorrect);
            Assert.AreEqual(1, d.Opponent("Vex").ReadsTotal);

            var lost = new HandResult { SeatsDealt = new[] { 0, 1 } };
            lost.Winners.Add(1);
            d.RecordHand(lost, s => "Pip");
            Assert.AreEqual(1, d.Opponent("Pip").TheyWon);

            d.RecordGame(new GameSummary { Won = true, Place = 1 }, new[] { "Pip", "Rocco", "Vex" });
            Assert.AreEqual(1, d.GamesPlayed);
            Assert.AreEqual(1, d.GamesWon);
            Assert.AreEqual(1, d.Opponent("Vex").GamesPlayed);
        }

        [Test]
        public void GestureSuccesses_RoundTrip_OldSavesHaveNone_AndLookupDoesNotAllocate()
        {
            File.WriteAllText(P, "{ \"Version\": 1, \"TutorialDone\": true }");
            var old = new ProgressStore(P).Load();
            Assert.AreEqual(0, old.Successes(GhostGesture.Knock), "old save: nothing learned yet");
            Assert.AreEqual(1, old.RecordSuccess(GhostGesture.Knock));
            Assert.AreEqual(2, old.RecordSuccess(GhostGesture.Knock));
            old.RecordSuccess(GhostGesture.Fold);
            new ProgressStore(P).Save(old);
            var back = new ProgressStore(P).Load();
            Assert.AreEqual(2, back.Successes(GhostGesture.Knock));
            Assert.AreEqual(1, back.Successes(GhostGesture.Fold));
            Assert.AreEqual(0, back.Successes(GhostGesture.AllIn));
            // 提示排程每幀都會查：不能配置記憶體
            Assert.That(() => { for (int i = 0; i < 100; i++) back.Successes((GhostGesture)(i % 10)); }, Is.Not.AllocatingGCMemory());
        }
    }
}
