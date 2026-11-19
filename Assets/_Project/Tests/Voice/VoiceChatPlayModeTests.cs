using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Poker.Game;
using Poker.Tests.Game;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Voice
{
    /// <summary>
    /// 整合：真的牌桌＋角色，玩家盯著 Rocco 講話 → 請求的 addressee 是 rocco → 回話在 Rocco 身上播放，
    /// 頭上的字幕就是回話的字，和聲音同時出現、播完（再加閱讀時間）才收起來（聽障玩家；2026-10-13 起字幕改走 SpeechChannel）。
    /// </summary>
    public class VoiceChatPlayModeTests
    {
        sealed class FakeMic : IMicSource
        {
            public readonly Queue<float> Samples = new Queue<float>();
            public bool Running;
            public int SampleRate => Synth.Rate;
            public bool Start() { Running = true; return true; }
            public void Stop() => Running = false;
            public int Read(float[] buffer)
            {
                int n = 0;
                while (n < buffer.Length && Samples.Count > 0) buffer[n++] = Samples.Dequeue();
                return n;
            }
            public void Push(float[] a) { foreach (var x in a) Samples.Enqueue(x); }
        }

        GameTestRig _rig;
        FakeMic _mic;
        FakeTransport _http;
        VoiceChatController _voice;

        [SetUp] public void SetUp()
        {
            GazeTracker.DefaultSource = null;
            _rig = new GameTestRig();
            _mic = new FakeMic();
            _http = new FakeTransport();
            _voice = VoiceChatController.Attach(_rig.Game, _mic, _http, new VoiceConfig { TimeoutSeconds = 5f });
            _voice.AutoUpdate = false;
            // 2026-10-14：Dot 第一次看到你盯著 AI 會提一句（及時提示）；實機開了語音對話時不提（UX 接上 TutorialVoice），
            // 這裡沒有接 UX，就當作提示都教過了，只測語音對話本身（提示的規則見 JustInTimeTipsTests）
            foreach (Poker.Game.JitTip t in System.Enum.GetValues(typeof(Poker.Game.JitTip))) _rig.Game.Tips.TryUse(t);
        }

        [TearDown] public void TearDown() => _rig.Dispose();

        /// <summary>每一幀：牌桌 Step、麥克風送進這一幀的音訊、語音控制器 Tick。</summary>
        void Play(float[] audio)
        {
            int per = Mathf.RoundToInt(Synth.Rate * GameTestRig.Dt);
            for (int i = 0; i < audio.Length; i += per)
            {
                var chunk = new float[Mathf.Min(per, audio.Length - i)];
                System.Array.Copy(audio, i, chunk, 0, chunk.Length);
                _mic.Push(chunk);
                _rig.StepIdle();
                _voice.Tick(GameTestRig.Dt);
            }
        }

        [UnityTest] public IEnumerator GazeAtRoccoAndSpeakPlaysReplyOnRocco()
        {
            yield return null;
            _voice.SetEnabled(consented: true, userEnabled: true);
            Assert.IsTrue(_mic.Running, "同意＋開啟後才開麥克風");
            var rocco = _voice.CharacterOf(VoiceCharacter.Rocco);
            Assert.IsNotNull(rocco);
            _rig.LookAt(rocco.Rig.Head.position);

            Play(Synth.Silence(1.2));
            Assert.AreEqual(VoiceCharacter.Rocco, _voice.Gazed, "頭部射線盯著 Rocco");
            Play(Synth.Concat(Synth.Tone(1.0), Synth.Silence(0.8)));

            Assert.AreEqual(1, _http.Requests.Count, "送出一次");
            var json = _http.Requests[0].json;
            StringAssert.Contains("\"addressee\":\"rocco\"", json);
            StringAssert.Contains("\"directives\":{", json);
            foreach (var seat in _rig.Game.Session.AiSeats)
                foreach (var c in _rig.Game.Session.HoleOf(seat))
                    StringAssert.DoesNotContain("\"" + c + "\"", json, "AI 底牌不送");
            foreach (var c in _rig.Game.Session.PlayerHole) StringAssert.DoesNotContain("\"" + c + "\"", json, "玩家底牌不送");

            _http.Respond(200, Replies.Json("rocco", "Nice try, kid.", "banter", "are you bluffing", Replies.Beep(0.6)));
            var src = _voice.VoiceOf(VoiceCharacter.Rocco);
            Assert.IsNotNull(src.clip, "回話音檔在 Rocco 的 AudioSource 上");
            Assert.AreEqual(22050, src.clip.frequency);
            Assert.IsTrue(src.transform.IsChildOf(rocco.Rig.Head), "聲音從 Rocco 的頭發出");
            Assert.Greater(src.spatialBlend, 0.9f, "3D 聲音");
            // 以前有聲音就不顯示字；現在字和聲音走同一條 SpeechChannel，一定同步，所以字幕要在
            Assert.IsTrue(rocco.Subtitle.IsVisible, "回話的字幕和聲音一起出現");
            Assert.AreEqual("Nice try, kid.", rocco.Speech.ShownText);
            Assert.AreEqual("tts", rocco.Speech.Current.ClipId, "整段回話的音檔（/talk 的 TTS）");
            Assert.AreSame(src.clip, rocco.Speech.Current.Clip);
            Assert.IsTrue(rocco.IsTalking, "嘴巴跟著動");
            Assert.IsTrue(_voice.AnyCharacterSpeaking, "播放中算在講話（半雙工）");

            // 回話 0.6 s 播放中講話（0.4 s，在回話結束前講完）：不收音。
            // 以前這裡講 1.0 s 也不會收到，是因為批次模式沒有音訊裝置時 AudioSource.isPlaying 一直是 true；
            // 現在播放時間由 SpeechChannel 自己計時（和字幕同一個時鐘），所以講話要真的落在回話播放期間
            Play(Synth.Concat(Synth.Tone(0.4), Synth.Silence(0.8)));
            Assert.AreEqual(1, _http.Requests.Count, "角色講話時不收音");
            Assert.IsFalse(rocco.Speech.IsSpeaking, "回話播完");
        }

        [UnityTest] public IEnumerator StreamedReplyPlaysOnRoccoWhileItArrives()
        {
            yield return null;
            var ws = new FakeStreamTransport();
            _rig.Dispose();
            _rig = new GameTestRig();
            _voice = VoiceChatController.Attach(_rig.Game, _mic, _http, new VoiceConfig { TimeoutSeconds = 5f }, ws);
            _voice.AutoUpdate = false;
            _voice.SetEnabled(true, true);
            var rocco = _voice.CharacterOf(VoiceCharacter.Rocco);
            _rig.LookAt(rocco.Rig.Head.position);
            Play(Synth.Concat(Synth.Silence(1.2), Synth.Tone(1.0), Synth.Silence(0.8)));
            Assert.AreEqual(1, ws.Opened.Count);
            Assert.IsEmpty(_http.Requests);
            foreach (var seat in _rig.Game.Session.AiSeats)
                foreach (var c in _rig.Game.Session.HoleOf(seat))
                    StringAssert.DoesNotContain("\"" + c + "\"", ws.Last.Texts[0], "AI 底牌不送");

            // 等回話的這段時間 Rocco 說了一句自己的台詞（有預錄聲音）：字幕是那句。
            // 整桌一個發言權：先讓大家安靜（Rocco 被盯著時可能正在講自己的台詞），輪到他（最多等閒聊的 MaxWait）
            foreach (var c in _rig.Game.AllCharacters()) c.Speech.StopAll();
            rocco.Play(new Poker.AI.PerformanceCue { Seat = rocco.CurrentCue.Seat, LineId = "rocco.bigbet.02", Subtitle = "Cute bet. Real cute." });
            for (int i = 0; i < 60 && rocco.Speech.ShownText != "Cute bet. Real cute."; i++) Play(Synth.Silence(0.05));
            Assert.AreEqual("Cute bet. Real cute.", rocco.Speech.ShownText);

            ws.Last.ServerText(LiveReplies.Reply("rocco", "Nice try, kid.", "banter"));
            Play(Synth.Silence(0.05));
            Assert.AreEqual("Cute bet. Real cute.", rocco.Speech.ShownText, "回話的字還沒有聲音：先不出現");
            ws.Last.ServerAudio(LiveReplies.Pcm(0.3));
            Play(Synth.Silence(0.05));
            var src = _voice.VoiceOf(VoiceCharacter.Rocco);
            Assert.IsNotNull(src.clip);
            Assert.AreEqual(22050, src.clip.frequency);
            Assert.IsTrue(_voice.AnyCharacterSpeaking, "串流播放中算在講話（半雙工）");
            Assert.AreEqual("Nice try, kid.", rocco.Speech.ShownText, "第一塊音訊開始播：字幕換成回話（打斷剛才那句，字和聲音一起換）");
            Assert.AreEqual("stream", rocco.Speech.Current.ClipId);
            Assert.AreSame(src.clip, rocco.Speech.Current.Clip);
            Assert.IsTrue(rocco.Subtitle.IsVisible);
            ws.Last.ServerAudio(LiveReplies.Pcm(0.3));
            ws.Last.ServerText(LiveReplies.Done);
            ws.Last.ServerClose();
            Play(Synth.Silence(0.05));
            Assert.IsTrue(_voice.AnyCharacterSpeaking, "還沒播完");
            Assert.AreEqual("Nice try, kid.", rocco.Speech.ShownText, "播放中字幕一直在");
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"^\[Speech\] rocco clip=stream dur=\d+\.\d\d text=""Nice try, kid\.""$"));
            Play(Synth.Silence(2.0));
            Assert.IsFalse(_voice.AnyCharacterSpeaking, "播完（0.6 s）就恢復收音");
            Assert.IsFalse(rocco.Speech.IsSpeaking);
            Assert.IsFalse(rocco.Subtitle.IsVisible, "播完再過閱讀時間，字幕收起來");
            Assert.IsFalse(src.isPlaying);
        }

        [UnityTest] public IEnumerator OfflineFallbackSpeaksABakedLine_WithMatchingSubtitle()
        {
            yield return null;
            _voice.SetEnabled(true, true);
            var pip = _voice.CharacterOf(VoiceCharacter.Pip);
            _rig.LookAt(pip.Rig.Head.position);
            Play(Synth.Concat(Synth.Silence(1.2), Synth.Tone(1.0), Synth.Silence(0.8)));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Voice\] fallback"));
            _http.Fail("Cannot connect to destination host");
            // 以前離線備案只顯示字；現在備案的句子都有預錄聲音（數字用預錄片段拼），字幕就是那句
            Assert.IsTrue(pip.Subtitle.IsVisible);
            Assert.IsNotNull(pip.Speech.Current, "備案也有聲音");
            Assert.AreNotEqual("none", pip.Speech.Current.ClipId);
            Assert.IsNotNull(pip.Speech.Current.Clip);
            Assert.AreEqual(pip.Speech.Current.Text, pip.Speech.ShownText);
        }

        [UnityTest] public IEnumerator NoConsentKeepsMicOff()
        {
            yield return null;
            _voice.SetEnabled(consented: false, userEnabled: true);
            Assert.IsFalse(_mic.Running);
            var rocco = _voice.CharacterOf(VoiceCharacter.Rocco);
            _rig.LookAt(rocco.Rig.Head.position);
            Play(Synth.Concat(Synth.Silence(1.2), Synth.Tone(1.0), Synth.Silence(0.8)));
            Assert.IsEmpty(_http.Requests);
        }

        [UnityTest] public IEnumerator ActionLogFollowsTheGame()
        {
            yield return null;
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Actions.Count > 0, 30), _rig.Dump());
            var st = _voice.BuildState();
            Assert.That(st.Actions.Count, Is.GreaterThanOrEqualTo(3), "盲注＋至少一個行動");
            Assert.AreEqual(_rig.Game.Session.Pot, st.Pot);
            StringAssert.DoesNotContain("hole", st.ToJson().ToLowerInvariant());
        }

        [UnityTest] public IEnumerator DealerAnswersWhenGazingDot()
        {
            yield return null;
            _voice.SetEnabled(true, true);
            _rig.LookAt(_rig.Game.Dealer.Rig.Head.position);
            Play(Synth.Concat(Synth.Silence(1.2), Synth.Tone(1.0), Synth.Silence(0.8)));
            Assert.AreEqual(1, _http.Requests.Count);
            StringAssert.Contains("\"addressee\":\"dot\"", _http.Requests[0].json);
        }
    }
}
