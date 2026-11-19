using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests
{
    /// <summary>
    /// 每個角色一條 SpeechChannel：頭上的字和聲音同進同出（字只在那句聲音開始時出現、播完再留一小段閱讀時間）；
    /// 同時來的話依重要程度排隊或打斷（打斷時字和聲音一起換），絕不讓別句蓋掉正在講的字。
    /// </summary>
    public class SpeechChannelTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> _spawned = new List<Object>();
        CharacterPerformer _p;
        SpeechChannel _ch;
        readonly List<string> _started = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _p = CharacterFactory.Create(CharacterKind.Dealer, null, Vector3.zero, Quaternion.identity, 3);
            _p.AutoUpdate = false;
            _spawned.Add(_p.gameObject);
            _ch = _p.Speech;
            _started.Clear();
            _ch.Started += u => _started.Add(u.Text);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
        }

        AudioClip Clip(float seconds, string name = "test")
        {
            var c = AudioClip.Create(name, Mathf.RoundToInt(seconds * 22050), 1, 22050, false);
            _spawned.Add(c);
            return c;
        }

        /// <summary>推進並在每一幀檢查同步：字只在聲音播放中（或剛播完的閱讀時間內）出現，而且就是正在播的那句。</summary>
        void Run(float seconds)
        {
            for (float t = 0; t < seconds; t += Dt)
            {
                _p.Tick(Dt);
                AssertLockstep();
            }
        }

        void AssertLockstep()
        {
            if (_ch.IsSpeaking)
            {
                Assert.IsTrue(_ch.IsShowingText, "聲音在播，字一定在");
                Assert.AreEqual(_ch.Current.Text, _ch.ShownText, "字就是正在播的那句");
                Assert.AreSame(_ch.Current.Clip, _ch.Source.clip, "播的就是那句的音檔");
            }
            else if (_ch.IsShowingText)
                Assert.LessOrEqual(_ch.SinceEnd, SpeechChannel.ReadTail + Dt * 1.5f, "聲音停了，字只多留閱讀時間");
        }

        static string Shown(SpeechChannel ch) => ch.ShownText;

        [Test]
        public void Say_ShowsTheTextExactlyWhileItsAudioPlays_ThenAShortReadTime()
        {
            var clip = Clip(1f);
            Assert.IsFalse(_ch.IsShowingText);
            _ch.Say("Hello there.", clip, "t.hello", SpeechPriority.Info);
            Assert.IsTrue(_ch.IsSpeaking, "聲音開始的那一刻");
            Assert.IsTrue(_ch.IsShowingText, "字同時出現");
            Assert.AreEqual("Hello there.", Shown(_ch));
            Assert.AreSame(clip, _ch.Source.clip);
            Assert.IsTrue(_p.IsTalking, "嘴巴跟著動");
            Run(0.9f);
            Assert.IsTrue(_ch.IsSpeaking);
            Run(0.2f);
            Assert.IsFalse(_ch.IsSpeaking, "1 s 的音檔播完");
            Assert.IsTrue(_ch.IsShowingText, "閱讀時間");
            Run(SpeechChannel.ReadTail + 0.05f);
            Assert.IsFalse(_ch.IsShowingText, "播完再過閱讀時間就收起來");
            CollectionAssert.AreEqual(new[] { "Hello there." }, _started);
        }

        [Test]
        public void SamePriority_Queues_AndNeverOverwritesTheLineBeingSpoken()
        {
            var a = Clip(1f, "a");
            var b = Clip(0.5f, "b");
            _ch.Say("First line.", a, "t.a", SpeechPriority.Chatter);
            _ch.Say("Second line.", b, "t.b", SpeechPriority.Chatter);
            Assert.AreEqual("First line.", Shown(_ch), "第二句不能蓋掉第一句");
            Assert.AreSame(a, _ch.Source.clip);
            Assert.AreEqual(1, _ch.QueuedCount);
            Run(0.95f);
            Assert.AreEqual("First line.", Shown(_ch));
            Run(0.05f + SpeechChannel.Gap + 0.05f);
            Assert.AreEqual("Second line.", Shown(_ch), "第一句講完才輪到第二句");
            Assert.AreSame(b, _ch.Source.clip, "字和聲音一起換");
            Run(0.6f + SpeechChannel.ReadTail);
            Assert.IsFalse(_ch.IsShowingText);
            CollectionAssert.AreEqual(new[] { "First line.", "Second line." }, _started);
        }

        [Test]
        public void HigherPriority_Interrupts_SwitchingTextAndAudioTogether()
        {
            var a = Clip(2f, "a");
            var b = Clip(0.6f, "b");
            var first = _ch.Say("Here comes the flop.", a, "t.flop", SpeechPriority.Chatter);
            Run(0.5f);
            var second = _ch.Say("Not your turn yet.", b, "t.invalid", SpeechPriority.Info);
            Assert.AreEqual(UtteranceState.Interrupted, first.State);
            Assert.AreSame(second, _ch.Current);
            Assert.AreEqual("Not your turn yet.", Shown(_ch), "字立刻換成打斷的那句");
            Assert.AreSame(b, _ch.Source.clip, "聲音也立刻換");
            Run(0.7f + SpeechChannel.ReadTail + 0.1f);
            Assert.IsFalse(_ch.IsShowingText);
            Assert.IsFalse(_ch.IsSpeaking, "被打斷的那句不會回來");
            CollectionAssert.AreEqual(new[] { "Here comes the flop.", "Not your turn yet." }, _started);
        }

        [Test]
        public void LowerPriority_WaitsBehindTheImportantLine()
        {
            _ch.Say("Palm over your cards.", Clip(1f), "t.peek", SpeechPriority.Info);
            var chat = _ch.Say("Check.", Clip(0.4f), "t.check", SpeechPriority.Chatter);
            Assert.AreEqual(UtteranceState.Queued, chat.State);
            Assert.AreEqual("Palm over your cards.", Shown(_ch));
            Run(1.0f + SpeechChannel.Gap + 0.05f);
            Assert.AreEqual("Check.", Shown(_ch));
        }

        [Test]
        public void StaleChatter_IsDropped_AndNeverShown()
        {
            _ch.Say("Long important line.", Clip(SpeechChannel.MaxWait(SpeechPriority.Chatter) + 1f), "t.long", SpeechPriority.Info);
            var chat = _ch.Say("The turn.", Clip(0.5f), "t.turn", SpeechPriority.Chatter);
            Run(SpeechChannel.MaxWait(SpeechPriority.Chatter) + 0.1f);
            Assert.AreEqual(UtteranceState.Dropped, chat.State, "等太久的閒聊直接丟掉（過時了）");
            Run(2f);
            CollectionAssert.DoesNotContain(_started, "The turn.");
        }

        /// <summary>即時台詞（2026-10-14）：每句可以有自己的排隊上限（比閒聊預設的 3 s 短），過了就丟、回報原因；不會晚講。</summary>
        [Test]
        public void PerUtteranceMaxWait_DropsEarlier_AndReportsWhy()
        {
            _ch.Say("Long important line.", Clip(4f), "t.long", SpeechPriority.Info);
            var live = _ch.Say("Thinking hard, huh?", Clip(0.5f), "t.live", SpeechPriority.Chatter);
            string why = null;
            live.MaxWait = 1.5f;
            live.OnDropped = r => why = r;
            Run(1.4f);
            Assert.AreEqual(UtteranceState.Queued, live.State, "1.5 s 內還在排");
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] dot dropped \(stale\) clip=t\.live"));
            Run(0.2f);
            Assert.AreEqual(UtteranceState.Dropped, live.State, "排超過 1.5 s 就丟（不是閒聊預設的 3 s）");
            Assert.AreEqual("stale", why);
            Run(4f);
            CollectionAssert.DoesNotContain(_started, "Thinking hard, huh?");
        }

        /// <summary>觸發的情境不在了（玩家已經行動）：排隊中的符合條件的話全部丟掉；正在講的不動。</summary>
        [Test]
        public void DropQueued_RemovesMatchingQueuedLines_Only()
        {
            var cur = _ch.Say("Long important line.", Clip(2f), "t.long", SpeechPriority.Info);
            var a = _ch.Say("Clock's ticking.", Clip(0.5f), "t.a", SpeechPriority.Chatter);
            var b = _ch.Say("Pot is right.", Clip(0.5f), "t.b", SpeechPriority.Chatter);
            a.Tag = "live";
            string why = null;
            a.OnDropped = r => why = r;
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] dot dropped \(player acted\) clip=t\.a"));
            Assert.AreEqual(1, _ch.DropQueued(u => u.Tag == "live", "player acted"));
            Assert.AreEqual(UtteranceState.Dropped, a.State);
            Assert.AreEqual("player acted", why);
            Assert.AreEqual(UtteranceState.Queued, b.State, "沒標記的照排");
            Assert.AreEqual(UtteranceState.Playing, cur.State, "正在講的不受影響");
            Assert.AreEqual(0, _ch.DropQueued(u => u == cur, "player acted"), "只動排隊中的");
            Run(3f);
            CollectionAssert.AreEqual(new[] { "Long important line.", "Pot is right." }, _started);
        }

        [Test]
        public void Filler_IsSkippedWhenBusy()
        {
            _ch.Say("Pot goes to Rocco!", Clip(1f), "t.pot", SpeechPriority.Info);
            var f = _ch.Say("Yes?", Clip(0.3f), "t.filler", SpeechPriority.Filler);
            Assert.AreEqual(UtteranceState.Dropped, f.State);
            Assert.AreEqual("Pot goes to Rocco!", Shown(_ch));
        }

        [Test]
        public void HoldBelow_DefersChatterUntilReleased()
        {
            _ch.HoldBelow = SpeechPriority.Info;   // 玩家正在講話：閒聊先別開口
            var chat = _ch.Say("Your call, friend.", Clip(0.5f), "t.yourcall", SpeechPriority.Chatter);
            Assert.AreEqual(UtteranceState.Queued, chat.State);
            Assert.IsFalse(_ch.IsShowingText, "排隊中的話不顯示字");
            Run(0.5f);
            _ch.HoldBelow = SpeechPriority.Filler;
            Run(SpeechChannel.Gap + 0.05f);
            Assert.AreEqual(UtteranceState.Playing, chat.State);
            Assert.AreEqual("Your call, friend.", Shown(_ch));
        }

        [Test]
        public void Stream_ShowsTheReplyFromTheFirstAudio_UntilPlaybackEnds()
        {
            var info = _ch.Say("Knock twice to check.", Clip(2f), "t.knock", SpeechPriority.Info);
            var stream = Clip(60f, "voice_live");
            var u = _ch.SayStream("Nice try, kid.", stream);
            Assert.AreEqual(UtteranceState.Interrupted, info.State, "回話打斷提示");
            Assert.AreEqual("Nice try, kid.", Shown(_ch));
            Assert.AreSame(stream, _ch.Source.clip);
            Run(3f);
            Assert.IsTrue(_ch.IsSpeaking, "串流還沒結束：字一直在");
            Assert.AreEqual("Nice try, kid.", Shown(_ch));
            _ch.EndStream(u);
            Assert.IsFalse(_ch.IsSpeaking);
            Assert.IsTrue(_ch.IsShowingText, "閱讀時間");
            Run(SpeechChannel.ReadTail + 0.05f);
            Assert.IsFalse(_ch.IsShowingText);
        }

        [Test]
        public void EveryUtterance_IsLogged_WithClipDurationAndText()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] dot clip=t\.hello dur=1\.00 text=""Hello there\.""$"));
            _ch.Say("Hello there.", Clip(1f), "t.hello", SpeechPriority.Info);
            var u = _ch.SayStream("Nice try, kid.", Clip(60f, "voice_live"));
            Run(1.5f);
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] dot clip=stream dur=1\.5\d text=""Nice try, kid\.""$"));
            _ch.EndStream(u);
        }

        [Test]
        public void ResolvesBakedClipsByVoiceAndText()
        {
            // 荷官的台詞從預錄音檔庫找（Resources/Speech；tools/voice-proxy/bake 產生）
            var u = _ch.Say("Action is on you.", SpeechPriority.Chatter);
            Assert.AreEqual("dot.yourturn.01", u.ClipId);
            Assert.IsNotNull(u.Clip, "有聲音");
            Assert.Greater(u.Duration, 0.3f);
            Assert.AreSame(u.Clip, _ch.Source.clip);
        }

        [Test]
        public void TextWithoutAnyAudio_IsLoudlyLoggedAsNone()
        {
            // 預錄庫裡沒有的句子（程式錯誤，SpeechInventoryTests 會先抓到）：照樣顯示給聽障玩家，但記 clip=none 警告
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Speech\] dot clip=none dur=\d+\.\d\d text=""Zzz unknown line\.""$"));
            var u = _ch.Say("Zzz unknown line.", SpeechPriority.Chatter);
            Assert.AreEqual("none", u.ClipId);
            Assert.IsTrue(_ch.IsShowingText);
        }
    }
}
