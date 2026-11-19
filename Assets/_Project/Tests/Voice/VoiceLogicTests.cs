using System;
using System.Collections.Generic;
using NUnit.Framework;
using Poker.AI;
using Poker.Voice;

namespace Poker.Tests.Voice
{
    public class WavCodecTests
    {
        [Test] public void EncodeThenDecodeKeepsSamplesAndRate()
        {
            var tone = Synth.Tone(0.25, 0.5f);
            var wav = WavCodec.EncodePcm16(tone, 0, tone.Length, 16000);
            Assert.AreEqual(44 + tone.Length * 2, wav.Length);
            Assert.IsTrue(WavCodec.TryDecode(wav, out var back, out int rate, out int ch));
            Assert.AreEqual(16000, rate);
            Assert.AreEqual(1, ch);
            Assert.AreEqual(tone.Length, back.Length);
            for (int i = 0; i < tone.Length; i += 97) Assert.AreEqual(tone[i], back[i], 1e-4f);
        }

        [Test] public void DecodeRejectsGarbage()
        {
            Assert.IsFalse(WavCodec.TryDecode(new byte[10], out _, out _, out _));
            Assert.IsFalse(WavCodec.TryDecode(null, out _, out _, out _));
        }

        [Test] public void ResampleChangesLength()
        {
            var a = Synth.Tone(1.0, 0.2f, 48000);
            var b = WavCodec.Resample(a, a.Length, 48000, 16000);
            Assert.AreEqual(16000, b.Length, 1);
        }
    }

    public class EnergyVadTests
    {
        sealed class Log
        {
            public int Started;
            public readonly List<float[]> Segments = new List<float[]>();
            public Log(EnergyVad v) { v.SpeechStarted += () => Started++; v.SegmentEnded += s => Segments.Add(s); }
        }

        [Test] public void SilenceProducesNothing()
        {
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Silence(3));
            Assert.AreEqual(0, log.Started);
            Assert.IsEmpty(log.Segments);
        }

        [Test] public void SpeechThenSilenceGivesOneSegmentWithPreRoll()
        {
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Silence(0.5), Synth.Tone(1.0), Synth.Silence(0.8)));
            Assert.AreEqual(1, log.Started);
            Assert.AreEqual(1, log.Segments.Count);
            float secs = log.Segments[0].Length / 16000f;
            Assert.That(secs, Is.InRange(1.1f, 1.8f), "前置 0.3 s＋語音 1 s＋尾巴一點");
        }

        [Test] public void EndsAfterAboutPointFourSecondsOfSilence()
        {
            // 2026-10-13：0.6 → 0.4 s（講完到角色開口的等待少 0.2 s）
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Tone(1.0), Synth.Silence(0.3)));
            Assert.IsEmpty(log.Segments, "0.3 s 靜音還沒結束");
            Assert.IsTrue(v.IsSpeaking);
            Synth.FeedChunks(v, Synth.Silence(0.15));
            Assert.AreEqual(1, log.Segments.Count, "0.4 s 後結束");
        }

        [Test] public void NaturalPausesInsideASentenceDoNotSplit()
        {
            // 句中停頓（逗號、換氣）約 0.2–0.35 s
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Tone(0.6), Synth.Silence(0.3), Synth.Tone(0.5), Synth.Silence(0.35), Synth.Tone(0.4), Synth.Silence(0.8)));
            Assert.AreEqual(1, log.Started);
            Assert.AreEqual(1, log.Segments.Count);
        }

        [Test] public void HesitationAfterAShortOpenerDoesNotSplit()
        {
            // 「Rocco…（想一下）…are you bluffing?」：開頭很短時結尾靜音放寬到 0.7 s
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Tone(0.4), Synth.Silence(0.6), Synth.Tone(0.9), Synth.Silence(0.8)));
            Assert.AreEqual(1, log.Started);
            Assert.AreEqual(1, log.Segments.Count);
            Assert.That(log.Segments[0].Length / 16000f, Is.GreaterThan(1.8f), "兩段都在同一個片段裡");
        }

        [Test] public void LongPauseAfterAFullSentenceEndsTheTurn()
        {
            // 取捨：講完一整句後停 0.5 s 就當作講完（之後再講是新的一段）
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Tone(1.0), Synth.Silence(0.5), Synth.Tone(0.6), Synth.Silence(0.8)));
            Assert.AreEqual(2, log.Segments.Count);
        }

        [Test] public void SpeechAudioStreamsPreRollAndEveryFrameWhileSpeaking()
        {
            var v = new EnergyVad(16000); var log = new Log(v);
            int streamed = 0, startedAt = -1;
            v.SpeechAudio += (buf, n) => { if (startedAt < 0) startedAt = log.Started; streamed += n; };
            Synth.FeedChunks(v, Synth.Concat(Synth.Silence(0.5), Synth.Tone(1.0), Synth.Silence(0.8)));
            Assert.AreEqual(1, startedAt, "開口偵測之後才開始送");
            Assert.AreEqual(1, log.Segments.Count);
            Assert.That(streamed, Is.GreaterThanOrEqualTo(log.Segments[0].Length), "串流送出的至少包含整個片段（含前置）");
            Assert.That(streamed / 16000f, Is.InRange(1.6f, 1.9f), "前置 0.3＋開口 0.1＋語音＋結尾靜音 0.4");
        }

        [Test] public void CapsAtEightSeconds()
        {
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Tone(10));
            Assert.AreEqual(1, log.Segments.Count);
            Assert.That(log.Segments[0].Length / 16000f, Is.LessThanOrEqualTo(8.001f));
        }

        [Test] public void ClicksDoNotStartSpeech()
        {
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Silence(0.3), Synth.Tone(0.04, 0.6f), Synth.Silence(1)));
            Assert.AreEqual(0, log.Started);
        }

        [Test] public void CannotStartWhenNotAllowed()
        {
            var v = new EnergyVad(16000) { CanStart = false }; var log = new Log(v);
            Synth.FeedChunks(v, Synth.Concat(Synth.Tone(1), Synth.Silence(1)));
            Assert.AreEqual(0, log.Started);
        }

        [Test] public void ResetDropsSpeechInProgress()
        {
            var v = new EnergyVad(16000); var log = new Log(v);
            Synth.FeedChunks(v, Synth.Tone(0.8));
            Assert.IsTrue(v.IsSpeaking);
            v.Reset();
            Synth.FeedChunks(v, Synth.Silence(1));
            Assert.IsEmpty(log.Segments);
            Assert.IsFalse(v.IsSpeaking);
        }
    }

    public class VoiceCastTests
    {
        [Test] public void ProxySpeakerWins() => Assert.AreEqual(VoiceCharacter.Rocco, VoiceCast.Resolve("rocco", "Pip, hey", VoiceCharacter.Pip));
        [Test] public void NameInTranscriptBeatsGaze() => Assert.AreEqual(VoiceCharacter.Vex, VoiceCast.Resolve(null, "Vex, are you bluffing?", VoiceCharacter.Pip));
        [Test] public void GazeWhenNoName() => Assert.AreEqual(VoiceCharacter.Pip, VoiceCast.Resolve("", "are you bluffing?", VoiceCharacter.Pip));
        [Test] public void DealerWhenNothing() => Assert.AreEqual(VoiceCharacter.Dot, VoiceCast.Resolve("bogus", "hello", VoiceCharacter.None));
        [Test] public void DealerWord() => Assert.AreEqual(VoiceCharacter.Dot, VoiceCast.NameIn("dealer, what's the pot?"));
        [Test] public void WholeWordsOnly() => Assert.AreEqual(VoiceCharacter.None, VoiceCast.NameIn("pipeline vexing"));
        [Test] public void Ids()
        {
            Assert.AreEqual("rocco", VoiceCast.Id(VoiceCharacter.Rocco));
            Assert.AreEqual("auto", VoiceCast.Id(VoiceCharacter.None));
            Assert.AreEqual(VoiceCharacter.Pip, VoiceCast.Parse("Pip"));
        }
    }

    public class VoiceFallbackTests
    {
        [TestCase("Dot, how much is in the pot?", LocalIntent.Pot)]
        [TestCase("how many chips do you have", LocalIntent.Stack)]
        [TestCase("what's Vex's stack", LocalIntent.Stack)]
        [TestCase("how much do I need to call", LocalIntent.ToCall)]
        [TestCase("what are the blinds", LocalIntent.Blinds)]
        [TestCase("whose turn is it", LocalIntent.Turn)]
        [TestCase("nice weather today", LocalIntent.None)]
        [TestCase(null, LocalIntent.None)]
        public void Classifies(string text, LocalIntent want) => Assert.AreEqual(want, VoiceFallback.Classify(text));

        [Test] public void TemplatesUseRealNumbers()
        {
            var s = States.Flop();
            StringAssert.Contains("60", VoiceFallback.Answer(LocalIntent.Pot, s, VoiceCharacter.Dot, "pot?"));
            StringAssert.Contains("1000", VoiceFallback.Answer(LocalIntent.Stack, s, VoiceCharacter.Dot, "how many chips does Vex have"));
            Assert.AreEqual("I have 870.", VoiceFallback.Answer(LocalIntent.Stack, s, VoiceCharacter.Rocco, "how many chips do you have"));
            Assert.AreEqual("You have 950.", VoiceFallback.Answer(LocalIntent.Stack, s, VoiceCharacter.Dot, "how many chips do I have"));
            Assert.AreEqual("You need 20 to call.", VoiceFallback.Answer(LocalIntent.ToCall, s, VoiceCharacter.Dot, "call?"));
            Assert.AreEqual("Blinds are 5 and 10.", VoiceFallback.Answer(LocalIntent.Blinds, s, VoiceCharacter.Dot, "blinds?"));
            Assert.AreEqual("It's your turn.", VoiceFallback.Answer(LocalIntent.Turn, s, VoiceCharacter.Dot, "whose turn"));
            s.YouToCall = 0;
            Assert.AreEqual("Nothing to call. You can check.", VoiceFallback.Answer(LocalIntent.ToCall, s, VoiceCharacter.Dot, "call?"));
        }

        [Test] public void NoTranscriptGivesCannedInCharacterLine()
        {
            var a = VoiceFallback.Reply(null, States.Flop(), VoiceCharacter.Rocco, 0);
            Assert.IsFalse(string.IsNullOrEmpty(a));
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(a, "[0-9]"), "罐頭台詞不報數字");
            Assert.AreNotEqual(a, VoiceFallback.Reply(null, States.Flop(), VoiceCharacter.Pip, 0), "各角色台詞不同");
        }

        [Test] public void KnownTranscriptGetsTemplate() =>
            Assert.AreEqual("The pot is 60.", VoiceFallback.Reply("what's the pot", States.Flop(), VoiceCharacter.Dot, 0));
    }

    public class VoiceProtocolTests
    {
        [Test] public void RequestCarriesAudioAddresseeStateAndDirectives()
        {
            var json = VoiceProtocol.BuildRequest(new byte[] { 1, 2, 3 }, "rocco", "{\"pot\":60}", "{\"rocco\":{\"mood\":\"Bluffing\"}}");
            StringAssert.Contains("\"audio\":\"AQID\"", json);
            StringAssert.Contains("\"addressee\":\"rocco\"", json);
            StringAssert.Contains("\"state\":{\"pot\":60}", json);
            StringAssert.Contains("\"directives\":{\"rocco\":{\"mood\":\"Bluffing\"}}", json);
        }

        [Test] public void ParsesReplyWithAudio()
        {
            var wav = Replies.Beep(0.2);
            Assert.IsTrue(VoiceProtocol.TryParseReply(Replies.Json("rocco", "I have 870.", wav: wav), out var r));
            Assert.AreEqual("rocco", r.Speaker);
            Assert.AreEqual("I have 870.", r.Text);
            Assert.AreEqual("on_topic", r.Intent);
            Assert.AreEqual(wav, r.Audio);
            Assert.AreEqual(2700.5f, r.Timings.gemini, 0.01f);
        }

        [Test] public void NullAudioIsNull()
        {
            Assert.IsTrue(VoiceProtocol.TryParseReply(Replies.Json("dot", "", "ignore"), out var r));
            Assert.IsNull(r.Audio);
            Assert.AreEqual("ignore", r.Intent);
        }

        [Test] public void GarbageFails() => Assert.IsFalse(VoiceProtocol.TryParseReply("<html>", out _));

        [Test] public void DirectivesJson()
        {
            var json = VoiceDirectives.ToJson(new List<(VoiceCharacter, PerformanceCue)>
            {
                (VoiceCharacter.Rocco, new PerformanceCue { Mood = Mood.Bluffing, Tell = Tell.FidgetChips, Intensity = 0.7f }),
                (VoiceCharacter.Vex, null),
            });
            Assert.AreEqual("{\"rocco\":{\"mood\":\"Bluffing\",\"tell\":\"FidgetChips\",\"intensity\":0.70}}", json);
        }
    }

    public class VoiceProxyClientTests
    {
        static VoiceProxyClient Client(FakeTransport t) => new VoiceProxyClient(t, new VoiceConfig { ProxyUrl = "http://127.0.0.1:8787/", Token = "tok", TimeoutSeconds = 5 });

        [Test] public void PostsToTalkWithToken()
        {
            var t = new FakeTransport { Auto = _ => new VoiceHttpResult { Ok = true, Status = 200, Body = Replies.Json("pip", "Um, hi.") } };
            VoiceReply got = null; string err = "unset";
            Client(t).Talk(new byte[4], "pip", "{}", "{}", (r, e) => { got = r; err = e; });
            Assert.AreEqual("http://127.0.0.1:8787/talk", t.Requests[0].url);
            Assert.AreEqual("tok", t.Requests[0].token);
            Assert.IsNull(err);
            Assert.AreEqual("Um, hi.", got.Text);
        }

        [Test] public void HttpErrorIsReported()
        {
            var t = new FakeTransport { Auto = _ => new VoiceHttpResult { Ok = false, Status = 502, Body = "{\"error\":\"gemini: boom\"}" } };
            string err = null;
            Client(t).Talk(new byte[4], "pip", "{}", "{}", (r, e) => err = e);
            StringAssert.Contains("502", err);
        }

        [Test] public void LimitCodeFromTheRelayIsKeptInTheError()
        {
            var t = new FakeTransport { Auto = _ => new VoiceHttpResult { Ok = false, Status = 429, Body = "{\"error\":\"device daily cap reached\",\"code\":\"device_daily_cap\"}" } };
            string err = null;
            Client(t).Talk(new byte[4], "pip", "{}", "{}", (r, e) => err = e);
            StringAssert.StartsWith("http 429 device_daily_cap", err);
            Assert.AreEqual("device_daily_cap", VoiceProtocol.LimitCode(err));
        }

        [Test] public void NetworkErrorAndGarbageAreReported()
        {
            var t = new FakeTransport { Auto = _ => new VoiceHttpResult { Ok = false, Status = 0, Error = "Cannot connect" } };
            string err = null;
            Client(t).Talk(new byte[4], "pip", "{}", "{}", (r, e) => err = e);
            StringAssert.Contains("Cannot connect", err);
            t.Auto = _ => new VoiceHttpResult { Ok = true, Status = 200, Body = "nope" };
            Client(t).Talk(new byte[4], "pip", "{}", "{}", (r, e) => err = e);
            StringAssert.Contains("parse", err);
        }
    }
}

namespace Poker.Tests.Voice
{
    /// <summary>2026-10-14：語音中繼上線（https://vr-poker-api.elebymax.com）；通行碼建置時寫進 Resources/VoiceClient.json（不進 git）。</summary>
    public class VoiceClientConfigTests
    {
        [NUnit.Framework.Test] public void DefaultsPointAtThePublicHttpsProxyWithoutAToken()
        {
            var c = new Poker.Voice.VoiceConfig();
            NUnit.Framework.Assert.AreEqual("https://vr-poker-api.elebymax.com", c.ProxyUrl);
            NUnit.Framework.Assert.AreEqual("", c.Token, "no shared dev token baked into source");
            NUnit.Framework.Assert.AreEqual("wss://vr-poker-api.elebymax.com/live", Poker.Voice.VoiceProtocol.LiveUrl(c.ProxyUrl));
        }

        [NUnit.Framework.Test] public void ClientFileOverridesUrlAndToken()
        {
            var c = Poker.Voice.VoiceConfig.FromClientJson("{\"url\":\"http://127.0.0.1:8787/\",\"token\":\"abc123\"}");
            NUnit.Framework.Assert.AreEqual("http://127.0.0.1:8787", c.ProxyUrl);
            NUnit.Framework.Assert.AreEqual("abc123", c.Token);
        }

        [NUnit.Framework.Test] public void BrokenOrEmptyClientFileKeepsDefaults()
        {
            foreach (var j in new[] { null, "", "not json", "{\"url\":\"\"}" })
            {
                var c = Poker.Voice.VoiceConfig.FromClientJson(j);
                NUnit.Framework.Assert.AreEqual("https://vr-poker-api.elebymax.com", c.ProxyUrl, j ?? "null");
            }
        }
    }
}
