using System.Collections.Generic;
using NUnit.Framework;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Voice
{
    /// <summary>VoiceSession：同意、半雙工、收音觸發、逾時與離線備案（不需要場景）。</summary>
    public class VoiceSessionTests
    {
        sealed class FakeTable : IVoiceTable
        {
            public VoiceCharacter Gazed { get; set; } = VoiceCharacter.None;
            public float GazeDwell { get; set; }
            public bool AnyCharacterSpeaking { get; set; }
            public PublicTableState State = States.Flop();
            public readonly List<VoiceCharacter> Listening = new List<VoiceCharacter>(), Fillers = new List<VoiceCharacter>();
            public readonly List<(VoiceCharacter who, string text, float[] samples, int rate)> Spoken = new List<(VoiceCharacter, string, float[], int)>();
            public PublicTableState BuildState() => State;
            public string DirectivesJson() => "{\"rocco\":{\"mood\":\"Bluffing\",\"tell\":\"None\",\"intensity\":0.50}}";
            public string Voices = "{}";
            public string VoicesJson() => Voices;
            public void OnListening(VoiceCharacter who) => Listening.Add(who);
            public void OnFiller(VoiceCharacter who) => Fillers.Add(who);
            public void Speak(VoiceCharacter who, string text, float[] samples, int rate) => Spoken.Add((who, text, samples, rate));
            // 串流播放
            public readonly List<(VoiceCharacter who, string text, int rate)> Began = new List<(VoiceCharacter, string, int)>();
            public int FedBytes, Ended;
            public void BeginVoice(VoiceCharacter who, string text, int rate) => Began.Add((who, text, rate));
            public void FeedVoice(byte[] pcm, int count) => FedBytes += count;
            public void EndVoice() => Ended++;
        }

        const float Dt = 0.02f;
        FakeTable _table;
        FakeTransport _http;
        VoiceSession _s;

        [SetUp] public void SetUp()
        {
            _table = new FakeTable();
            _http = new FakeTransport();
            var cfg = new VoiceConfig { TimeoutSeconds = 5f, ListenDwell = 0.8f };
            _s = new VoiceSession(_table, new VoiceProxyClient(_http, cfg), cfg, Synth.Rate) { Consented = true, UserEnabled = true };
        }

        /// <summary>以 Dt 為一格把音訊餵給 session。</summary>
        void Run(float[] audio)
        {
            int chunk = (int)(Synth.Rate * Dt);
            var buf = new float[chunk];
            for (int i = 0; i < audio.Length; i += chunk)
            {
                int n = Mathf.Min(chunk, audio.Length - i);
                System.Array.Copy(audio, i, buf, 0, n);
                if (_table.Gazed != VoiceCharacter.None) _table.GazeDwell += Dt;
                _s.Tick(Dt, buf, n);
            }
        }

        static float[] Utterance() => Synth.Concat(Synth.Tone(1.0), Synth.Silence(0.8));

        // ================= 串流（/live） =================

        FakeStreamTransport _ws;

        void UseStreaming()
        {
            _ws = new FakeStreamTransport();
            var cfg = new VoiceConfig { TimeoutSeconds = 5f, ListenDwell = 0.8f, ProxyUrl = "http://127.0.0.1:8787", Token = "tell-dev" };
            _s = new VoiceSession(_table, new VoiceProxyClient(_http, cfg), cfg, Synth.Rate, _ws) { Consented = true, UserEnabled = true };
        }

        [Test] public void StreamingOpensAtSpeechStartAndSendsAudioWhileTalking()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Synth.Tone(0.6));
            Assert.AreEqual(1, _ws.Opened.Count, "一開口就連");
            var c = _ws.Last;
            Assert.AreEqual("ws://127.0.0.1:8787/live", c.Url);
            Assert.AreEqual("tell-dev", c.Token);
            StringAssert.Contains("\"type\":\"start\"", c.Texts[0]);
            StringAssert.Contains("\"addressee\":\"rocco\"", c.Texts[0]);
            StringAssert.Contains("\"pot\":60", c.Texts[0]);
            StringAssert.Contains("Bluffing", c.Texts[0]);
            StringAssert.Contains("\"rate\":16000", c.Texts[0]);
            Assert.Greater(c.AudioBytes, 16000, "講話的同時就在送（不等講完）");
            Assert.AreEqual(VoiceState.Capturing, _s.State);
            Run(Synth.Concat(Synth.Tone(0.4), Synth.Silence(0.8)));
            Assert.AreEqual("{\"type\":\"end\"}", c.Texts[c.Texts.Count - 1]);
            Assert.AreEqual(VoiceState.Waiting, _s.State);
            Assert.IsEmpty(_http.Requests, "串流成功時不走 /talk");
            CollectionAssert.AreEqual(new[] { VoiceCharacter.Rocco }, _table.Fillers);
        }

        [Test] public void StreamedReplyPlaysChunksAsTheyArrive()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            var c = _ws.Last;
            c.ServerText(LiveReplies.Reply("rocco", "I have 870.", transcript: "Rocco, how much do you have?"));
            _s.Tick(Dt, null, 0);
            Assert.IsEmpty(_table.Began, "第一塊音訊到了才開始播");
            c.ServerAudio(LiveReplies.Pcm(0.2));
            _s.Tick(Dt, null, 0);
            Assert.AreEqual(1, _table.Began.Count);
            Assert.AreEqual((VoiceCharacter.Rocco, "I have 870.", 22050), _table.Began[0]);
            c.ServerAudio(LiveReplies.Pcm(0.3));
            c.ServerText(LiveReplies.Done);
            c.ServerClose();
            _s.Tick(Dt, null, 0);
            Assert.AreEqual((int)(0.5 * 22050) * 2, _table.FedBytes, 4);
            Assert.AreEqual(1, _table.Ended);
            Assert.IsEmpty(_table.Spoken, "有聲音就不顯示字");
            Assert.AreEqual(VoiceState.Idle, _s.State);
            Assert.AreEqual(VoiceCharacter.Rocco, _s.LastSpeaker);
            Assert.AreEqual("I have 870.", _s.LastReply.Text);
            Assert.Greater(_s.LastFirstAudioMs, 0f);
        }

        [Test] public void StreamFailureBeforeTheReplyFallsBackToTalk()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            _ws.Last.ServerClose("connection reset");
            _s.Tick(Dt, null, 0);
            Assert.AreEqual(1, _http.Requests.Count, "改走整段 /talk");
            StringAssert.Contains("\"audio\":\"UklGR", _http.Requests[0].json);
            _http.Respond(200, Replies.Json("dot", "The pot is 60.", wav: Replies.Beep(0.4)));
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.IsNotNull(_table.Spoken[0].samples);
        }

        [Test] public void StreamThatNeverConnectsUsesTalkAtSpeechEnd()
        {
            UseStreaming();
            _ws.FailImmediately = true;
            _table.Gazed = VoiceCharacter.Vex;
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count);
            StringAssert.Contains("\"addressee\":\"vex\"", _http.Requests[0].json);
        }

        [Test] public void StreamTimeoutFallsBackOfflineAndIgnoresLateAudio()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Voice\] fallback.*timeout"));
            Run(Synth.Silence(5.2));
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.IsNull(_table.Spoken[0].samples, "離線備案：只有字");
            Assert.IsTrue(_ws.Last.Closed);
            _ws.Last.ServerText(LiveReplies.Reply("dot", "The pot is 60."));
            _ws.Last.ServerAudio(LiveReplies.Pcm(0.2));
            _s.Tick(Dt, null, 0);
            Assert.IsEmpty(_table.Began);
        }

        [Test] public void StreamedIgnoreSaysNothing()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            _ws.Last.ServerText(LiveReplies.Reply("pip", "", "ignore"));
            _ws.Last.ServerText(LiveReplies.Done);
            _s.Tick(Dt, null, 0);
            Assert.IsEmpty(_table.Began);
            Assert.IsEmpty(_table.Spoken);
            Assert.AreEqual(VoiceState.Idle, _s.State);
        }

        [Test] public void StreamedReplyWithoutAudioShowsTheTextOnly()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            _ws.Last.ServerText(LiveReplies.Reply("pip", "Um, 20 to call."));
            _ws.Last.ServerText("{\"type\":\"done\",\"timings\":{},\"error\":\"tts: elevenlabs http 401\"}");
            _s.Tick(Dt, null, 0);
            Assert.IsEmpty(_table.Began);
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.AreEqual("Um, 20 to call.", _table.Spoken[0].text);
            Assert.IsNull(_table.Spoken[0].samples);
        }

        [Test] public void SpeechCutByACharacterTalkingCancelsTheStream()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Vex;
            Run(Synth.Tone(0.6));
            _table.AnyCharacterSpeaking = true;
            Run(Synth.Tone(0.2));
            Assert.IsTrue(_ws.Last.Closed);
            StringAssert.Contains("cancel", _ws.Last.Texts[_ws.Last.Texts.Count - 1]);
            _table.AnyCharacterSpeaking = false;
            Run(Synth.Silence(1.0));
            Assert.IsEmpty(_http.Requests);
        }

        [Test] public void StreamingMicDuringReplyIsIgnored()
        {
            UseStreaming();
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            Run(Utterance());
            Assert.AreEqual(1, _ws.Opened.Count, "等回覆時不開第二條");
        }

        [Test] public void NoConsentMeansNothingIsSent()
        {
            _s.Consented = false;
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Utterance());
            Assert.IsEmpty(_http.Requests);
            Assert.AreEqual(VoiceState.Off, _s.State);
            Assert.IsFalse(_s.Enabled);
        }

        [Test] public void SwitchedOffMeansNothingIsSent()
        {
            _s.UserEnabled = false;
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Utterance());
            Assert.IsEmpty(_http.Requests);
        }

        [Test] public void StreamingStartTellsTheProxyWhoseVoiceEachSeatHas()
        {
            UseStreaming();
            _table.Voices = "{\"rocco\":\"erik\"}";
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Synth.Tone(0.6));
            StringAssert.Contains("\"voices\":{\"rocco\":\"erik\"}", _ws.Last.Texts[0]);
        }

        [Test] public void TalkRequestTellsTheProxyWhoseVoiceEachSeatHas()
        {
            _table.Voices = "{\"pip\":\"marcus\"}";
            _table.Gazed = VoiceCharacter.Pip;
            Run(Synth.Silence(1.0));
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count);
            StringAssert.Contains("\"voices\":{\"pip\":\"marcus\"}", _http.Requests[0].json);
        }

        [Test] public void GazingAndSpeakingSendsToTheGazedCharacter()
        {
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Synth.Silence(1.0));
            CollectionAssert.AreEqual(new[] { VoiceCharacter.Rocco }, _table.Listening, "盯滿 0.8 秒就做出在聽的反應");
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count);
            StringAssert.Contains("\"addressee\":\"rocco\"", _http.Requests[0].json);
            StringAssert.Contains("\"pot\":60", _http.Requests[0].json);
            StringAssert.Contains("Bluffing", _http.Requests[0].json);
            Assert.AreEqual(VoiceState.Waiting, _s.State);
            CollectionAssert.AreEqual(new[] { VoiceCharacter.Rocco }, _table.Fillers, "等待時先「嗯？」");
        }

        [Test] public void SpeechStartingWhileGazingCountsEvenBeforeDwell()
        {
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count);
            StringAssert.Contains("\"addressee\":\"pip\"", _http.Requests[0].json);
        }

        [Test] public void NotGazingAnyoneDoesNotCapture()
        {
            Run(Utterance());
            Assert.IsEmpty(_http.Requests);
        }

        [Test] public void HalfDuplexIgnoresMicWhileACharacterSpeaks()
        {
            _table.Gazed = VoiceCharacter.Vex;
            _table.AnyCharacterSpeaking = true;
            Run(Utterance());
            Assert.IsEmpty(_http.Requests, "角色講話時不收音");
            _table.AnyCharacterSpeaking = false;
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count);
        }

        [Test] public void SpeechCutByCharacterTalkingIsDropped()
        {
            _table.Gazed = VoiceCharacter.Vex;
            Run(Synth.Tone(0.6));
            _table.AnyCharacterSpeaking = true;
            Run(Synth.Tone(0.4));
            _table.AnyCharacterSpeaking = false;
            Run(Synth.Silence(1.0));
            Assert.IsEmpty(_http.Requests);
        }

        [Test] public void ReplyIsSpokenByTheResolvedSpeakerWithAudio()
        {
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            _http.Respond(200, Replies.Json("rocco", "I have 870.", transcript: "Rocco, how much do you have?", wav: Replies.Beep(0.5)));
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.AreEqual(VoiceCharacter.Rocco, _table.Spoken[0].who);
            Assert.AreEqual("I have 870.", _table.Spoken[0].text);
            Assert.AreEqual(22050, _table.Spoken[0].rate);
            Assert.AreEqual(11025, _table.Spoken[0].samples.Length);
            Assert.AreEqual(VoiceState.Idle, _s.State);
            Assert.AreEqual(VoiceCharacter.Rocco, _s.LastSpeaker);
        }

        [Test] public void IgnoreIntentSaysNothing()
        {
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            _http.Respond(200, Replies.Json("pip", "", "ignore"));
            Assert.IsEmpty(_table.Spoken);
            Assert.AreEqual(VoiceState.Idle, _s.State);
        }

        [Test] public void NetworkErrorFallsBackToSubtitleOnlyCannedLine()
        {
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Utterance());
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Voice\] fallback"));
            _http.Fail("Cannot connect to destination host");
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.AreEqual(VoiceCharacter.Rocco, _table.Spoken[0].who);
            Assert.IsNull(_table.Spoken[0].samples, "只有字幕");
            Assert.IsFalse(string.IsNullOrEmpty(_table.Spoken[0].text));
        }

        [Test] public void TimeoutFallsBackAndLateReplyIsIgnored()
        {
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            Assert.AreEqual(VoiceState.Waiting, _s.State);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Voice\] fallback.*timeout"));
            Run(Synth.Silence(5.2));
            Assert.AreEqual(1, _table.Spoken.Count, "逾時備案");
            Assert.IsNull(_table.Spoken[0].samples);
            _http.Respond(200, Replies.Json("dot", "The pot is 60.", wav: Replies.Beep()));
            Assert.AreEqual(1, _table.Spoken.Count, "逾時後才到的回覆不播");
        }

        [Test] public void EmptyTextWithTranscriptUsesLocalTemplate()
        {
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            _http.Respond(200, Replies.Json("dot", "", "on_topic", transcript: "how much is in the pot"));
            Assert.AreEqual("The pot is 60.", _table.Spoken[0].text);
        }

        [Test] public void MicCapturedDuringWaitIsDiscarded()
        {
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count, "等回覆時不送第二段");
        }
        // ================= 中繼限流（每台裝置） =================

        const string Dev = "0f8fad5b-d9cb-469f-a165-70867728950e";
        System.DateTime _utc = new System.DateTime(2026, 10, 20, 22, 0, 0, System.DateTimeKind.Utc);

        void UseStreamingWithDevice()
        {
            _ws = new FakeStreamTransport();
            var cfg = new VoiceConfig { TimeoutSeconds = 5f, ListenDwell = 0.8f, ProxyUrl = "https://vr-poker-api.elebymax.com", Token = "tok", DeviceId = Dev };
            _s = new VoiceSession(_table, new VoiceProxyClient(_http, cfg), cfg, Synth.Rate, _ws) { Consented = true, UserEnabled = true, UtcNow = () => _utc };
        }

        static string LiveError(string code) => "{\"type\":\"error\",\"error\":\"limited\",\"code\":\"" + code + "\"}";

        [Test] public void StreamCarriesTheDeviceIdInTheUrl()
        {
            UseStreamingWithDevice();
            _table.Gazed = VoiceCharacter.Rocco;
            Run(Synth.Tone(0.6));
            Assert.AreEqual("wss://vr-poker-api.elebymax.com/live?device=" + Dev, _ws.Last.Url);
            Assert.AreEqual("tok", _ws.Last.Token);
        }

        [Test] public void TalkFallbackCarriesTheDeviceId()
        {
            UseStreamingWithDevice();
            _ws.FailImmediately = true;
            _table.Gazed = VoiceCharacter.Vex;
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count);
            Assert.AreEqual(Dev, _http.Requests[0].device);
        }

        [Test] public void DeviceDailyCapOnLiveGoesOfflineAndStopsCallingTheRelayUntilUtcMidnight()
        {
            UseStreamingWithDevice();
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            _ws.Last.ServerText(LiveError("device_daily_cap"));
            _ws.Last.ServerClose();
            _s.Tick(Dt, null, 0);
            Assert.IsEmpty(_http.Requests, "限流時不用 /talk 重送");
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.IsNull(_table.Spoken[0].samples, "離線備案：只有字");
            StringAssert.Contains("device_daily_cap", _s.LastFallbackReason);
            Assert.IsTrue(_s.RelayBlocked);

            Run(Utterance());
            Assert.AreEqual(1, _ws.Opened.Count, "今天不再連中繼");
            Assert.IsEmpty(_http.Requests);
            Assert.AreEqual(2, _table.Spoken.Count, "照樣用離線備案回話");
            Assert.IsNull(_table.Spoken[1].samples);

            _utc = new System.DateTime(2026, 10, 21, 0, 0, 5, System.DateTimeKind.Utc);
            Assert.IsFalse(_s.RelayBlocked, "UTC 換日就恢復");
            Run(Utterance());
            Assert.AreEqual(2, _ws.Opened.Count);
        }

        [Test] public void DeviceDailyCapOnTalkAlsoBlocksForTheDay()
        {
            _table.Gazed = VoiceCharacter.Dot;
            var cfg = new VoiceConfig { TimeoutSeconds = 5f, ListenDwell = 0.8f, DeviceId = Dev };
            _s = new VoiceSession(_table, new VoiceProxyClient(_http, cfg), cfg, Synth.Rate) { Consented = true, UserEnabled = true, UtcNow = () => _utc };
            Run(Utterance());
            _http.Respond(429, "{\"error\":\"device daily cap reached\",\"code\":\"device_daily_cap\"}");
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.IsTrue(_s.RelayBlocked);
            Run(Utterance());
            Assert.AreEqual(1, _http.Requests.Count, "不再送");
            Assert.AreEqual(2, _table.Spoken.Count);
        }

        [Test] public void RateLimitedBacksOffBrieflyWithoutRetrying()
        {
            UseStreamingWithDevice();
            _table.Gazed = VoiceCharacter.Pip;
            Run(Utterance());
            _ws.Last.ServerText(LiveError("rate_limited"));
            _s.Tick(Dt, null, 0);
            Assert.IsEmpty(_http.Requests, "不用 /talk 重送");
            Assert.AreEqual(1, _table.Spoken.Count);
            Assert.IsTrue(_s.RelayBlocked);
            Run(Utterance());
            Assert.AreEqual(1, _ws.Opened.Count, "冷卻中不連");
            _utc = _utc.AddSeconds(new VoiceConfig().RateLimitBackoffSeconds + 1);
            Assert.IsFalse(_s.RelayBlocked);
            Run(Utterance());
            Assert.AreEqual(2, _ws.Opened.Count, "冷卻過了再試");
        }

        [Test] public void OtherProxyErrorsStillRetryWithTalk()
        {
            UseStreamingWithDevice();
            _table.Gazed = VoiceCharacter.Dot;
            Run(Utterance());
            _ws.Last.ServerText("{\"type\":\"error\",\"error\":\"gemini: boom\"}");
            _s.Tick(Dt, null, 0);
            Assert.AreEqual(1, _http.Requests.Count);
            Assert.IsFalse(_s.RelayBlocked);
        }

        [Test] public void LimitCodeIsReadFromBothTransports()
        {
            Assert.IsTrue(VoiceProtocol.TryParseLive(LiveError("device_daily_cap"), out var m));
            Assert.AreEqual("device_daily_cap", m.Code);
            Assert.AreEqual("device_daily_cap", VoiceProtocol.LimitCode("http 429 device_daily_cap: {...}"));
            Assert.AreEqual("daily_cap", VoiceProtocol.LimitCode("http 429 daily_cap: {...}"));
            Assert.AreEqual("rate_limited", VoiceProtocol.LimitCode("http 429 rate_limited: {...}"));
            Assert.IsNull(VoiceProtocol.LimitCode("http 502: gemini"));
            Assert.IsNull(VoiceProtocol.LimitCode(null));
        }
    }
}
