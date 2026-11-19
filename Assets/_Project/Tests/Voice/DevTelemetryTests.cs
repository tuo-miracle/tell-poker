using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Poker.Voice;
using UnityEngine;

namespace Poker.Tests.Voice
{
    /// <summary>開發版遙測：過濾（白名單 tag）、去識別、緩衝、批次上傳與重試（純邏輯，不連網）。</summary>
    public class DevTelemetryFilterTests
    {
        [Test] public void AcceptsOnlyAllowlistedTags()
        {
            foreach (var tag in new[] { "[Playtest]", "[Gesture]", "[GestureEvent]", "[LiveTalk]", "[Read]", "[Tutorial]", "[Table]", "[Speech]", "[Assist]", "[Audio]", "[Voice]", "[FoolRocco]", "[UX]" })   // [UX]：選單叫不出來的 near-miss（2026-10-14）
                Assert.IsTrue(TelemetryFilter.Accept(tag + " something", LogType.Log), tag);
            Assert.IsTrue(TelemetryFilter.Accept("[Voice] stream lost", LogType.Warning), "warnings with a tag too");
            Assert.IsFalse(TelemetryFilter.Accept("plain log line", LogType.Log));
            Assert.IsFalse(TelemetryFilter.Accept("[Telemetry] on", LogType.Log), "own lines are not uploaded");
            Assert.IsFalse(TelemetryFilter.Accept("[GestureX] nope", LogType.Log), "exact tag only");
            Assert.IsFalse(TelemetryFilter.Accept("[MRUK] room loaded", LogType.Log));
            Assert.IsFalse(TelemetryFilter.Accept("text [Voice] in the middle", LogType.Log));
            Assert.IsFalse(TelemetryFilter.Accept(null, LogType.Log));
        }

        [Test] public void ErrorsAndExceptionsAreAlwaysAccepted()
        {
            Assert.IsTrue(TelemetryFilter.Accept("NullReferenceException: boom", LogType.Exception));
            Assert.IsTrue(TelemetryFilter.Accept("Shader error", LogType.Error));
            Assert.IsTrue(TelemetryFilter.Accept("Assertion failed", LogType.Assert));
        }

        [Test] public void ScrubsIdsAndPlayerSpeech()
        {
            var s = TelemetryFilter.Scrub("[Voice] id 0f8fad5b-d9cb-469f-a165-70867728950e mac 00:1A:2b:3C:4d:5E transcript=\"Rocco how much\" ok");
            StringAssert.DoesNotContain("0f8fad5b", s);
            StringAssert.DoesNotContain("00:1A", s);
            StringAssert.DoesNotContain("Rocco how much", s);
            StringAssert.Contains("transcript=<scrubbed>", s);
            StringAssert.EndsWith(" ok", s);
            StringAssert.DoesNotContain("hello there", TelemetryFilter.Scrub("[Voice] heard \"hello there\""));
            StringAssert.DoesNotContain("fold now", TelemetryFilter.Scrub("[Voice] transcript: fold now"));
            Assert.AreEqual("[Speech] dot clip=dot.x dur=1.2 text=\"Your turn.\"", TelemetryFilter.Scrub("[Speech] dot clip=dot.x dur=1.2 text=\"Your turn.\""), "game lines stay");
        }

        [Test] public void FormatsOneJsonObjectPerLineWithinTheRelayLimit()
        {
            var t = new DateTime(2026, 10, 14, 12, 0, 1, 250, DateTimeKind.Utc);
            var line = TelemetryFilter.Format(t, 7, 12.5, LogType.Warning, "[Voice] say \"hi\"\nnext", null);
            Assert.IsFalse(line.Contains("\n"), "one line");
            StringAssert.StartsWith("{", line);
            StringAssert.Contains("\"t\":\"2026-10-14T12:00:01.250Z\"", line);
            StringAssert.Contains("\"s\":7", line);
            StringAssert.Contains("\"l\":\"warn\"", line);
            StringAssert.Contains("\\\"hi\\\"", line);
            var huge = TelemetryFilter.Format(t, 1, 0, LogType.Exception, "E: " + new string('\u0001', 5000), new string('x', 9000));
            Assert.LessOrEqual(huge.Length, TelemetryFilter.MaxLineChars);
            StringAssert.Contains("\"l\":\"exception\"", huge);
        }

        [Test] public void NulCharactersAreRemovedSoTheRelayDoesNotRefuseTheBatch()
        {
            var line = TelemetryFilter.Format(DateTime.UtcNow, 1, 0, LogType.Error, "bad\0name", "at x\0y");
            StringAssert.DoesNotContain("\\u0000", line);
            StringAssert.Contains("badname", line);
        }

        [Test] public void ConfigReadsTheTelemetryFlagDefaultOff()
        {
            Assert.IsTrue(VoiceConfig.FromClientJson("{\"url\":\"https://x\",\"token\":\"t\",\"telemetry\":true}").Telemetry);
            Assert.IsFalse(VoiceConfig.FromClientJson("{\"url\":\"https://x\",\"token\":\"t\",\"telemetry\":false}").Telemetry);
            Assert.IsFalse(VoiceConfig.FromClientJson("{\"url\":\"https://x\",\"token\":\"t\"}").Telemetry);
            Assert.IsFalse(VoiceConfig.FromClientJson(null).Telemetry);
        }

        [Test] public void RunsOnlyOnDeviceWithFlagAndToken()
        {
            var on = VoiceConfig.FromClientJson("{\"token\":\"t\",\"telemetry\":true}");
            Assert.IsTrue(DevTelemetry.ShouldRun(on, isEditor: false));
            Assert.IsFalse(DevTelemetry.ShouldRun(on, isEditor: true));
            Assert.IsFalse(DevTelemetry.ShouldRun(VoiceConfig.FromClientJson("{\"token\":\"t\"}"), false));
            Assert.IsFalse(DevTelemetry.ShouldRun(VoiceConfig.FromClientJson("{\"telemetry\":true}"), false), "no token = relay would refuse");
        }

        [Test] public void SessionIdAndBuildAreSafeHeaderValues()
        {
            var a = DevTelemetry.NewSessionId();
            Assert.That(a, Does.Match("^[0-9a-f]{8}$"));
            Assert.AreNotEqual(a, DevTelemetry.NewSessionId());
            Assert.AreEqual("0.3.1_dev_x", DevTelemetry.SafeBuild("0.3.1 dev/x"));
            Assert.AreEqual("unknown", DevTelemetry.SafeBuild(""));
            Assert.AreEqual(40, DevTelemetry.SafeBuild(new string('a', 80)).Length);
        }
    }

    public class TelemetryBufferTests
    {
        [Test] public void DropsOldestWhenFullAndReportsHowMany()
        {
            var b = new TelemetryBuffer(maxLines: 3);
            for (int i = 0; i < 5; i++) b.Add("{\"m\":\"" + i + "\"}");
            Assert.AreEqual(3, b.Count);
            Assert.AreEqual(2, b.Dropped);
            var batch = b.TakeBatch(10_000);
            Assert.AreEqual(4, batch.Count, "a dropped-count line plus the three newest");
            StringAssert.Contains("dropped 2", batch[0]);
            Assert.AreEqual("{\"m\":\"2\"}", batch[1]);
            Assert.AreEqual("{\"m\":\"4\"}", batch[3]);
            Assert.AreEqual(0, b.Dropped);
        }

        [Test] public void BatchesRespectTheByteLimitAndRequeueKeepsOrder()
        {
            var b = new TelemetryBuffer(maxLines: 100);
            for (int i = 0; i < 10; i++) b.Add("{\"m\":\"" + new string('x', 90) + i + "\"}");
            var first = b.TakeBatch(350);
            Assert.AreEqual(3, first.Count);
            Assert.AreEqual(7, b.Count);
            b.Requeue(first);
            Assert.AreEqual(10, b.Count);
            var all = b.TakeBatch(100_000);
            Assert.AreEqual(10, all.Count);
            StringAssert.EndsWith("0\"}", all[0]);
            StringAssert.EndsWith("9\"}", all[9]);
        }

        [Test] public void RequeueAlsoRespectsTheBound()
        {
            var b = new TelemetryBuffer(maxLines: 3);
            b.Add("{\"m\":\"a\"}"); b.Add("{\"m\":\"b\"}");
            var batch = b.TakeBatch(1000);
            b.Add("{\"m\":\"c\"}"); b.Add("{\"m\":\"d\"}");
            b.Requeue(batch);
            Assert.AreEqual(3, b.Count);
            Assert.AreEqual(1, b.Dropped, "the oldest (a) goes");
        }

        [Test] public void AddIsThreadSafe()
        {
            var b = new TelemetryBuffer(maxLines: 100_000);
            Parallel.For(0, 8, _ => { for (int i = 0; i < 1000; i++) b.Add("{}"); });
            Assert.AreEqual(8000, b.Count);
        }
    }

    public class TelemetryUploaderTests
    {
        sealed class FakeTelemetryTransport : ITelemetryTransport
        {
            public readonly List<(string url, Dictionary<string, string> headers, byte[] body, Action<int, string> done)> Posts = new List<(string, Dictionary<string, string>, byte[], Action<int, string>)>();
            public void Post(string url, IList<KeyValuePair<string, string>> headers, byte[] body, Action<int, string> done) =>
                Posts.Add((url, headers.ToDictionary(h => h.Key, h => h.Value), body, done));
            public void Reply(int status, string body = "{}") { var p = Posts[Posts.Count - 1]; p.done(status, status == 0 ? "offline" : body); }
        }

        double _now;
        FakeTelemetryTransport _t;
        TelemetryBuffer _buf;
        TelemetryUploader _up;

        [SetUp] public void SetUp()
        {
            _now = 0;
            _t = new FakeTelemetryTransport();
            _buf = new TelemetryBuffer(1000);
            _up = new TelemetryUploader(_t, _buf, "https://relay.example/", "tok", "0f8fad5b-d9cb-469f-a165-70867728950e", "a1b2c3d4", "0.3.1", () => _now);
        }

        static string Gunzip(byte[] b)
        {
            using (var gz = new GZipStream(new MemoryStream(b), CompressionMode.Decompress))
            using (var r = new StreamReader(gz, Encoding.UTF8)) return r.ReadToEnd();
        }

        string BodyText(int i = -1)
        {
            var p = _t.Posts[i < 0 ? _t.Posts.Count - 1 : i];
            return p.headers.TryGetValue("content-encoding", out var enc) && enc == "gzip" ? Gunzip(p.body) : Encoding.UTF8.GetString(p.body);
        }

        [Test] public void SendsABatchEveryIntervalWithTokenDeviceSessionAndBuild()
        {
            _buf.Add("{\"m\":\"[Playtest] a\"}");
            _up.Tick();
            Assert.AreEqual(0, _t.Posts.Count, "waits for the first interval");
            _now = 10.1;
            _up.Tick();
            Assert.AreEqual(1, _t.Posts.Count);
            var p = _t.Posts[0];
            Assert.AreEqual("https://relay.example/telemetry", p.url);
            Assert.AreEqual("tok", p.headers["x-voice-token"]);
            Assert.AreEqual("0f8fad5b-d9cb-469f-a165-70867728950e", p.headers["x-voice-device"]);
            Assert.AreEqual("a1b2c3d4", p.headers["x-tell-session"]);
            Assert.AreEqual("0.3.1", p.headers["x-tell-build"]);
            Assert.AreEqual("application/x-ndjson", p.headers["content-type"]);
            Assert.AreEqual("{\"m\":\"[Playtest] a\"}\n", BodyText());
            _buf.Add("{\"m\":\"b\"}");
            _now = 11; _up.Tick();
            Assert.AreEqual(1, _t.Posts.Count, "one request in flight at a time");
            _t.Reply(200);
            Assert.AreEqual(1, _buf.Count, "sent lines are gone, the new one waits");
            _now = 15; _up.Tick();
            Assert.AreEqual(1, _t.Posts.Count, "next batch after the interval");
            _now = 21.2; _up.Tick();
            Assert.AreEqual(2, _t.Posts.Count);
        }

        [Test] public void NothingToSendSendsNothing()
        {
            _now = 100; _up.Tick(); _up.Flush();
            Assert.AreEqual(0, _t.Posts.Count);
        }

        [Test] public void OfflineKeepsTheLinesAndBacksOffExponentially()
        {
            _buf.Add("{\"m\":\"a\"}");
            _now = 10; _up.Tick();
            _t.Reply(0);
            Assert.AreEqual(1, _buf.Count, "kept for retry");
            _now = 25; _up.Tick();
            Assert.AreEqual(1, _t.Posts.Count, "backoff 20 s after the first failure");
            _now = 30.1; _up.Tick();
            Assert.AreEqual(2, _t.Posts.Count);
            _t.Reply(503);
            _now = 60; _up.Tick();
            Assert.AreEqual(2, _t.Posts.Count, "40 s after the second failure");
            _now = 70.2; _up.Tick();
            Assert.AreEqual(3, _t.Posts.Count);
            _t.Reply(200);
            Assert.AreEqual(0, _buf.Count);
            for (int i = 0; i < 20; i++) { _buf.Add("{}"); _now += 1000; _up.Tick(); _t.Reply(0); }
            Assert.LessOrEqual(_up.BackoffSeconds, TelemetryUploader.MaxBackoffSeconds);
        }

        [Test] public void FlushSendsRightAwayButNotMoreThanEveryTwoSeconds()
        {
            _buf.Add("{\"m\":\"a\"}");
            _now = 1; _up.Flush();
            Assert.AreEqual(1, _t.Posts.Count);
            _t.Reply(200);
            _buf.Add("{\"m\":\"b\"}");
            _now = 2; _up.Flush();
            Assert.AreEqual(1, _t.Posts.Count);
            _now = 3.1; _up.Flush();
            Assert.AreEqual(2, _t.Posts.Count);
        }

        [Test] public void SessionFullOrBadTokenStopsUploading()
        {
            _buf.Add("{}");
            _now = 10; _up.Tick();
            _t.Reply(413, "{\"error\":\"session log full\",\"code\":\"session_full\"}");
            Assert.IsTrue(_up.Stopped);
            _buf.Add("{}");
            _now = 1000; _up.Tick(); _up.Flush();
            Assert.AreEqual(1, _t.Posts.Count);

            SetUp();
            _buf.Add("{}");
            _now = 10; _up.Tick();
            _t.Reply(401, "{\"error\":\"bad token\"}");
            Assert.IsTrue(_up.Stopped);
        }

        [Test] public void RejectedGzipIsRetriedAsPlainText()
        {
            _buf.Add("{\"m\":\"a\"}");
            _now = 10; _up.Tick();
            Assert.AreEqual("gzip", _t.Posts[0].headers["content-encoding"]);
            _t.Reply(400, "{\"code\":\"bad_lines\"}");
            Assert.AreEqual(1, _buf.Count, "kept");
            _now = 20.1; _up.Tick();
            Assert.IsFalse(_t.Posts[1].headers.ContainsKey("content-encoding"));
            Assert.AreEqual("{\"m\":\"a\"}\n", BodyText(1));
            _t.Reply(400, "{\"code\":\"bad_lines\"}");
            Assert.AreEqual(0, _buf.Count, "plain text refused too: drop the batch, don't loop");
        }

        [Test] public void BatchesStayUnderTheRelayLimitAndDrainFasterWhenBehind()
        {
            for (int i = 0; i < 400; i++) _buf.Add("{\"m\":\"" + new string('x', 400) + "\"}");
            _now = 10; _up.Tick();
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(BodyText()), TelemetryUploader.MaxBatchBytes);
            _t.Reply(200);
            Assert.Greater(_buf.Count, 0);
            _now = 15.1; _up.Tick();
            Assert.AreEqual(2, _t.Posts.Count, "backlog: next batch after the 5 s minimum gap");
        }
    }
}
