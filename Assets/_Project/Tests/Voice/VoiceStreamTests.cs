using System.Collections.Generic;
using NUnit.Framework;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Voice
{
    /// <summary>串流播放緩衝與 /live 協定（純邏輯）。</summary>
    public class PcmStreamTests
    {
        [Test] public void DecodesPcm16EvenWhenSamplesAreSplitAcrossWrites()
        {
            var s = new PcmStream(22050);
            s.Write(new byte[] { 0x00, 0x40, 0x00 }, 0, 3);
            s.Write(new byte[] { 0xC0 }, 0, 1);
            Assert.AreEqual(2, s.Buffered);
            var dst = new float[2];
            Assert.AreEqual(2, s.Read(dst, 0, 2));
            Assert.AreEqual(0.5f, dst[0], 1e-4f);
            Assert.AreEqual(-0.5f, dst[1], 1e-4f);
        }

        [Test] public void UnderrunFillsSilenceAndCountsOnlyRealSamples()
        {
            var s = new PcmStream(100);
            s.Write(new byte[] { 0xFF, 0x7F }, 0, 2);
            var dst = new float[4] { 9, 9, 9, 9 };
            Assert.AreEqual(1, s.Read(dst, 0, 4));
            Assert.AreEqual(1f, dst[0], 1e-3f);
            Assert.AreEqual(0f, dst[1]);
            Assert.AreEqual(0f, dst[3]);
            Assert.AreEqual(1, s.Played);
        }

        [Test] public void DrainedOnlyAfterCompleteAndEverythingPlayed()
        {
            var s = new PcmStream(100);
            s.Write(new byte[8], 0, 8);
            s.Complete();
            Assert.IsFalse(s.Drained);
            s.Read(new float[10], 0, 10);
            Assert.IsTrue(s.Drained);
            Assert.AreEqual(4, s.Written);
            Assert.AreEqual(0.04f, s.DurationSeconds, 1e-6f);
        }

        [Test] public void OverflowDropsTheNewestAndCountsIt()
        {
            var s = new PcmStream(100, capacitySeconds: 0.1f);
            s.Write(new byte[30], 0, 30);
            Assert.AreEqual(10, s.Buffered);
            Assert.AreEqual(5, s.Dropped);
        }
    }

    public class LiveProtocolTests
    {
        [Test] public void LiveUrlFromProxyUrl()
        {
            Assert.AreEqual("ws://127.0.0.1:8787/live", VoiceProtocol.LiveUrl("http://127.0.0.1:8787/"));
            Assert.AreEqual("wss://voice.example.com/live", VoiceProtocol.LiveUrl("https://voice.example.com"));
        }

        [Test] public void StartMessageCarriesAddresseeStateDirectivesAndRate()
        {
            var j = VoiceProtocol.BuildLiveStart("rocco", States.Flop().ToJson(), "{\"rocco\":{}}", 48000);
            StringAssert.Contains("\"type\":\"start\"", j);
            StringAssert.Contains("\"addressee\":\"rocco\"", j);
            StringAssert.Contains("\"pot\":60", j);
            StringAssert.Contains("\"directives\":{\"rocco\":{}}", j);
            StringAssert.Contains("\"rate\":48000", j);
        }

        [Test] public void ParsesReplyDoneAndError()
        {
            Assert.IsTrue(VoiceProtocol.TryParseLive(LiveReplies.Reply("rocco", "I have 870.", transcript: "Rocco, how much?"), out var r));
            Assert.AreEqual(LiveMessageKind.Reply, r.Kind);
            Assert.AreEqual("rocco", r.Reply.Speaker);
            Assert.AreEqual("I have 870.", r.Reply.Text);
            Assert.AreEqual("Rocco, how much?", r.Reply.Transcript);
            Assert.AreEqual(22050, r.AudioRate);
            Assert.AreEqual("live", r.Path);

            Assert.IsTrue(VoiceProtocol.TryParseLive(LiveReplies.Done, out var d));
            Assert.AreEqual(LiveMessageKind.Done, d.Kind);
            Assert.AreEqual(700f, d.Timings.firstAudio);
            Assert.AreEqual(540f, d.Timings.gemini);

            Assert.IsTrue(VoiceProtocol.TryParseLive("{\"type\":\"error\",\"error\":\"bad token\"}", out var e));
            Assert.AreEqual(LiveMessageKind.Error, e.Kind);
            Assert.AreEqual("bad token", e.Error);

            Assert.IsFalse(VoiceProtocol.TryParseLive("<html>", out _));
            Assert.IsFalse(VoiceProtocol.TryParseLive("{\"type\":\"weird\"}", out _));
        }

        [Test] public void Pcm16FromFloatsIsLittleEndianAndClamped()
        {
            var b = new byte[6];
            int n = WavCodec.ToPcm16(new[] { 0.5f, -2f, 1f }, 3, b);
            Assert.AreEqual(6, n);
            Assert.AreEqual(16383, (short)(b[0] | b[1] << 8), 1);
            Assert.AreEqual(-32767, (short)(b[2] | b[3] << 8));
            Assert.AreEqual(32767, (short)(b[4] | b[5] << 8));
        }
    }
}
