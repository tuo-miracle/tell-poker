using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Voice
{
    /// <summary>每次安裝隨機產生一次的裝置 id（限流用）：產生、保存、送出格式。</summary>
    public class VoiceDeviceIdTests
    {
        static readonly Regex Guid36 = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        string _dir;

        [SetUp] public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "voice-device-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            VoiceDeviceId.ResetCacheForTests();
        }

        [TearDown] public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { }
            VoiceDeviceId.ResetCacheForTests();
        }

        [Test] public void GeneratedOnceAndPersisted()
        {
            var a = VoiceDeviceId.LoadOrCreate(_dir);
            Assert.That(a, Does.Match(Guid36.ToString()));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, VoiceDeviceId.FileName)));
            VoiceDeviceId.ResetCacheForTests(); // 模擬 App 重開
            Assert.AreEqual(a, VoiceDeviceId.LoadOrCreate(_dir), "重開 App 還是同一個 id");
            Assert.AreEqual(a, File.ReadAllText(Path.Combine(_dir, VoiceDeviceId.FileName)).Trim());
        }

        [Test] public void DifferentInstallsGetDifferentIds()
        {
            var other = _dir + "-b";
            Directory.CreateDirectory(other);
            try { Assert.AreNotEqual(VoiceDeviceId.LoadOrCreate(_dir), VoiceDeviceId.LoadOrCreate(other)); }
            finally { Directory.Delete(other, true); }
        }

        [Test] public void CorruptFileIsReplacedWithAFreshId()
        {
            File.WriteAllText(Path.Combine(_dir, VoiceDeviceId.FileName), "not a guid; serial=R3CT1234");
            var id = VoiceDeviceId.LoadOrCreate(_dir);
            Assert.That(id, Does.Match(Guid36.ToString()));
            Assert.AreEqual(id, File.ReadAllText(Path.Combine(_dir, VoiceDeviceId.FileName)).Trim());
        }

        [Test] public void UnwritableFolderStillGivesAnIdWithoutThrowing()
        {
            var notADir = Path.Combine(_dir, "file");
            File.WriteAllText(notADir, "x");
            string id = null;
            Assert.DoesNotThrow(() => id = VoiceDeviceId.LoadOrCreate(Path.Combine(notADir, "sub")));
            Assert.That(id, Does.Match(Guid36.ToString()));
        }

        [Test] public void IsRandomNotHardware()
        {
            // 不用 SystemInfo.deviceUniqueIdentifier 之類的硬體 id：兩個資料夾（兩次安裝）一定不同，且是 v4 隨機 GUID
            var id = VoiceDeviceId.LoadOrCreate(_dir);
            Assert.AreEqual('4', id[14], "GUID v4");
            Assert.AreNotEqual(SystemInfo.deviceUniqueIdentifier.ToLowerInvariant(), id);
        }

        [Test] public void LoadLogsOnceWithOnlyAShortPrefix()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Voice\] proxy "));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Voice\] device id set \([0-9a-f]{4}…\)$"));
            var c = VoiceConfig.Load(_dir);
            Assert.That(c.DeviceId, Does.Match(Guid36.ToString()));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Voice\] proxy "));
            var again = VoiceConfig.Load(_dir);
            Assert.AreEqual(c.DeviceId, again.DeviceId);
            LogAssert.NoUnexpectedReceived(); // 第二次不再印 device id
        }

        [Test] public void LiveUrlCarriesTheDeviceAsAQueryParameter()
        {
            const string id = "0f8fad5b-d9cb-469f-a165-70867728950e";
            Assert.AreEqual("wss://vr-poker-api.elebymax.com/live?device=" + id, VoiceProtocol.LiveUrl("https://vr-poker-api.elebymax.com/", id));
            Assert.AreEqual("ws://127.0.0.1:8787/live", VoiceProtocol.LiveUrl("http://127.0.0.1:8787", ""));
            Assert.AreEqual("ws://127.0.0.1:8787/live", VoiceProtocol.LiveUrl("http://127.0.0.1:8787"));
            Assert.AreEqual("ws://h/live?device=a%26b", VoiceProtocol.LiveUrl("http://h", "a&b"), "escaped");
        }

        [Test] public void TalkHeadersIncludeTheDevice()
        {
            var h = VoiceHttp.Headers("tok", "0f8fad5b-d9cb-469f-a165-70867728950e").ToList();
            CollectionAssert.Contains(h, ("x-voice-token", "tok"));
            CollectionAssert.Contains(h, ("x-voice-device", "0f8fad5b-d9cb-469f-a165-70867728950e"));
            Assert.IsFalse(VoiceHttp.Headers("tok", "").Any(x => x.Item1 == "x-voice-device"), "沒有 id 就不送這個 header");
        }

        [Test] public void TalkSendsTheDeviceId()
        {
            var t = new FakeTransport { Auto = _ => new VoiceHttpResult { Ok = true, Status = 200, Body = Replies.Json("pip", "Hi.") } };
            var cfg = new VoiceConfig { ProxyUrl = "http://127.0.0.1:8787", Token = "tok", DeviceId = "0f8fad5b-d9cb-469f-a165-70867728950e" };
            new VoiceProxyClient(t, cfg).Talk(new byte[4], "pip", "{}", "{}", (r, e) => { });
            Assert.AreEqual("0f8fad5b-d9cb-469f-a165-70867728950e", t.Requests[0].device);
            Assert.AreEqual("tok", t.Requests[0].token);
        }
    }
}
