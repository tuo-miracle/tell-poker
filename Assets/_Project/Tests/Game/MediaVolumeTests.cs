using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 頭盔沒聲音（2026-10-13 實機）：遊戲音訊在播（AAudio started），但 Android 媒體音量（STREAM_MUSIC）是 0，系統把它靜音了。
    /// 開始時與之後每 5 秒讀一次媒體音量；0 時在牌桌旁顯示一行字（聲音被系統關掉，只能用字），大於 0 就收起來；音量改變時記 log。
    /// </summary>
    public class MediaVolumeTests
    {
        sealed class FakeVolume : IMediaVolumeSource
        {
            public int Volume = 0, Max = 15, Reads;
            public bool Known = true;
            public bool TryRead(out int volume, out int max)
            {
                Reads++;
                volume = Volume; max = Max;
                return Known;
            }
        }

        static void Tick(MediaVolumeGuard g, double seconds)
        {
            for (double t = 0; t < seconds - 1e-6; t += 0.1) g.Tick(0.1f);
        }

        [Test]
        public void Reads_On_Start_Then_Every_Five_Seconds()
        {
            var src = new FakeVolume { Volume = 8 };
            var g = new MediaVolumeGuard(src, _ => { });
            g.Tick(0f);
            Assert.AreEqual(1, src.Reads, "read on start");
            Tick(g, 4.8);
            Assert.AreEqual(1, src.Reads, "not every frame");
            Tick(g, 0.3);
            Assert.AreEqual(2, src.Reads, "again after ~5 s");
            Tick(g, 5.0);
            Assert.AreEqual(3, src.Reads);
        }

        [Test]
        public void Zero_Volume_Is_Muted_And_Logged_Once_Then_Change_Is_Logged()
        {
            var src = new FakeVolume { Volume = 0, Max = 15 };
            var log = new List<string>();
            var g = new MediaVolumeGuard(src, log.Add);
            g.Tick(0f);
            Assert.IsTrue(g.Muted);
            CollectionAssert.AreEqual(new[] { "[Audio] media volume 0/15 — speech muted" }, log);
            Tick(g, 5.1);
            Assert.AreEqual(1, log.Count, "unchanged volume is not logged again");
            src.Volume = 6;
            Tick(g, 5.1);
            Assert.IsFalse(g.Muted);
            Assert.AreEqual("[Audio] media volume 6/15", log[^1]);
            Assert.AreEqual(2, log.Count);
            src.Volume = 0;
            Tick(g, 5.1);
            Assert.IsTrue(g.Muted);
            Assert.AreEqual("[Audio] media volume 0/15 — speech muted", log[^1]);
        }

        [Test]
        public void Unknown_Volume_Is_Not_Muted_And_Logged_Once()
        {
            var src = new FakeVolume { Known = false };
            var log = new List<string>();
            var g = new MediaVolumeGuard(src, log.Add);
            g.Tick(0f);
            Tick(g, 11.0);
            Assert.IsFalse(g.Muted);
            Assert.IsFalse(g.Known);
            CollectionAssert.AreEqual(new[] { "[Audio] media volume unknown" }, log);
        }

        [Test]
        public void Android_Reader_In_Editor_Or_Batchmode_Says_Unknown()
        {
            Assert.IsFalse(new AndroidMediaVolumeSource().TryRead(out _, out _));
        }

        [Test]
        public void Game_Shows_A_Notice_By_The_Table_While_The_Media_Volume_Is_Zero()
        {
            var src = new FakeVolume { Volume = 0, Max = 15 };
            LogAssert.Expect(LogType.Log, new Regex("^" + Regex.Escape("[Audio] media volume 0/15 — speech muted") + "$"));
            LogAssert.Expect(LogType.Log, new Regex("^" + Regex.Escape("[Audio] media volume 4/15") + "$"));
            using var rig = new GameTestRig(null, g => g.MediaVolume = src);
            rig.Idle(0.2);
            var notice = rig.Game.VolumeNotice;
            Assert.IsTrue(notice.Go.activeSelf, "notice shown");
            Assert.AreEqual(MediaVolumeGuard.Notice, notice.Text);
            StringAssert.Contains("volume +", notice.Text);
            var p = notice.Go.transform.localPosition;
            var felt = rig.Game.Spots.Layout.FeltSize;
            Assert.That(p.x, Is.InRange(-felt.X * 0.5f, felt.X * 0.5f), "over the table " + p);
            Assert.That(p.z, Is.InRange(0f, felt.Y), "over the table " + p);
            Assert.That(p.y, Is.InRange(0.02f, 0.35f), "just above the felt " + p);
            var toHead = rig.Camera.transform.position - notice.Go.transform.position;
            Assert.Greater(Vector3.Dot(notice.Go.transform.forward, -toHead.normalized), 0.9f, "readable: faces the player");

            src.Volume = 4;
            rig.Idle(MediaVolumeGuard.PollSeconds + 0.2);
            Assert.IsFalse(notice.Go.activeSelf, "hidden once there is sound");
        }
    }
}
