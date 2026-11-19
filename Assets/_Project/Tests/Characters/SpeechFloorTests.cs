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
    /// 整桌一個「發言權」（SpeechFloor，2026-10-13 實機：四個角色同一瞬間開口）：
    /// - 同時只有一個角色在講；別人講完再停 SpeechFloor.Gap 秒才換下一個人。
    /// - 等發言權時：重要的先講（Reply ＞ Info ＞ Chatter），同等級先來先講；閒聊等太久（MaxWait）就丟掉，Filler 不等。
    /// - 不打斷別人講到一半：只有回話（Reply，玩家在等的答案）會切掉別人的閒聊；別人在講 Info／Reply 時，整段回話排第一個等，
    ///   串流回話（音訊已經在來，不能等）照播並記 overlaps。
    /// 每個角色自己的規則（字和聲音同進同出、同角色的排隊與打斷）不變。
    /// </summary>
    public class SpeechFloorTests
    {
        const float Dt = 1f / 60f;
        readonly List<Object> _spawned = new List<Object>();
        SpeechFloor _floor;
        SpeechChannel _dot, _pip, _rocco;
        CharacterPerformer[] _ps;
        readonly List<(string who, float t)> _starts = new List<(string, float)>();
        float _time;

        [SetUp]
        public void SetUp()
        {
            _floor = new SpeechFloor();
            _ps = new[]
            {
                CharacterFactory.Create(CharacterKind.Dealer, null, Vector3.zero, Quaternion.identity, 3),
                CharacterFactory.Create(CharacterKind.Rookie, null, Vector3.right, Quaternion.identity, 4),
                CharacterFactory.Create(CharacterKind.Loudmouth, null, Vector3.left, Quaternion.identity, 5),
            };
            foreach (var p in _ps)
            {
                p.AutoUpdate = false;
                _spawned.Add(p.gameObject);
                _floor.Join(p.Speech);
                var who = p.Speech.Who;
                p.Speech.Started += u => _starts.Add((who, _time));
            }
            _dot = _ps[0].Speech; _pip = _ps[1].Speech; _rocco = _ps[2].Speech;
            _starts.Clear();
            _time = 0f;
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

        int SpeakingCount()
        {
            int n = 0;
            foreach (var p in _ps) if (p.Speech.IsSpeaking) n++;
            return n;
        }

        /// <summary>推進；每一幀檢查：最多一個人在講，每個人的字都是自己正在播的那句。</summary>
        void Run(float seconds, bool allowOverlap = false)
        {
            for (float t = 0; t < seconds; t += Dt)
            {
                _time += Dt;
                foreach (var p in _ps) p.Tick(Dt);
                if (!allowOverlap) Assert.LessOrEqual(SpeakingCount(), 1, "同時只有一個角色在講");
                foreach (var p in _ps)
                    if (p.Speech.IsSpeaking) Assert.AreEqual(p.Speech.Current.Text, p.Speech.ShownText, p.Speech.Who + " 的字就是他正在播的那句");
            }
        }

        [Test]
        public void OnlyOneSpeaksAtATime_TheNextWaitsForTheGap()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] floor: pip waits for dot$"));
            var a = _dot.Say("Check.", Clip(1f, "a"), "t.check", SpeechPriority.Chatter);
            var b = _pip.Say("Why is it so warm in here?", Clip(0.8f, "b"), "t.warm", SpeechPriority.Chatter);
            Assert.AreEqual(UtteranceState.Playing, a.State);
            Assert.AreEqual(UtteranceState.Queued, b.State, "別人在講：排隊等");
            Assert.IsFalse(_pip.IsShowingText, "等的時候沒有字也沒有聲音");
            Assert.IsNull(_pip.Source.clip, "等的時候沒有聲音");
            Run(3f);
            Assert.AreEqual(UtteranceState.Done, b.State, "輪到 Pip 講完");
            Assert.AreEqual(2, _starts.Count);
            float gap = _starts[1].t - _starts[0].t - 1f;
            Assert.GreaterOrEqual(gap, SpeechFloor.Gap - Dt * 1.5f, "停一下才換人");
            Assert.LessOrEqual(gap, SpeechFloor.Gap + 0.1f, "不會停太久");
            Assert.That(SpeechFloor.Gap, Is.InRange(0.2f, 0.4f));
        }

        [Test]
        public void ChatterWaitingTooLong_IsDropped()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] floor: rocco waits for dot$"));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] floor: rocco dropped \(floor busy\) clip=t\.tank text=""Clock's ticking, champ\.""$"));
            _dot.Say("Palm over your cards. Lift to peek.", Clip(SpeechChannel.MaxWait(SpeechPriority.Chatter) + 1.5f), "t.peek", SpeechPriority.Info);
            var chat = _rocco.Say("Clock's ticking, champ.", Clip(1f), "t.tank", SpeechPriority.Chatter);
            Run(SpeechChannel.MaxWait(SpeechPriority.Chatter) + 0.1f);
            Assert.AreEqual(UtteranceState.Dropped, chat.State, "過時的閒聊丟掉");
            Run(2.5f);
            Assert.AreEqual(1, _starts.Count, "Rocco 從頭到尾沒開口");
        }

        [Test]
        public void Filler_IsDroppedWhenSomeoneElseHoldsTheFloor()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] floor: pip dropped \(floor busy\) clip=t\.hm text=""Hm\?""$"));
            _dot.Say("The turn.", Clip(1f), "t.turn", SpeechPriority.Chatter);
            var f = _pip.Say("Hm?", Clip(0.3f), "t.hm", SpeechPriority.Filler);
            Assert.AreEqual(UtteranceState.Dropped, f.State);
        }

        [Test]
        public void DealerInfo_GoesBeforeQueuedChatter_ButDoesNotCutTheSpeaker()
        {
            var pip = _pip.Say("Take your time! No pressure. Ha.", Clip(1f, "p"), "t.tank1", SpeechPriority.Chatter);
            var rocco = _rocco.Say("Thinking hard, huh? That's a tell.", Clip(1f, "r"), "t.tank2", SpeechPriority.Chatter);
            Run(0.2f);
            var info = _dot.Say("I'll call for you.", Clip(1f, "d"), "t.call", SpeechPriority.Info);
            Assert.AreEqual(UtteranceState.Playing, pip.State, "Info 不會切掉別人講到一半的話");
            Assert.AreEqual(UtteranceState.Queued, info.State);
            Run(0.8f + SpeechFloor.Gap + 0.1f);
            Assert.AreEqual(UtteranceState.Playing, info.State, "Pip 講完，荷官的提示插到 Rocco 前面");
            Assert.AreEqual(UtteranceState.Queued, rocco.State);
            Run(1f + SpeechFloor.Gap + 0.1f);
            Assert.AreEqual(UtteranceState.Playing, rocco.State, "還沒等太久的閒聊接著講");
            CollectionAssert.AreEqual(new[] { "pip", "dot", "rocco" }, _starts.ConvertAll(s => s.who));
        }

        [Test]
        public void SamePriority_FirstComeFirstServed()
        {
            _dot.Say("Check.", Clip(0.5f), "t.check", SpeechPriority.Chatter);
            _rocco.Say("Clock's ticking, champ.", Clip(0.5f), "t.r", SpeechPriority.Chatter);
            _pip.Say("Take your time! No pressure. Ha.", Clip(0.5f), "t.p", SpeechPriority.Chatter);
            Run(2.5f);
            CollectionAssert.AreEqual(new[] { "dot", "rocco", "pip" }, _starts.ConvertAll(s => s.who));
        }

        [Test]
        public void Reply_CutsSomeoneElsesChatter()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] floor: rocco cuts pip \(reply\)$"));
            var chat = _pip.Say("Why is it so warm in here?", Clip(2f), "t.warm", SpeechPriority.Chatter);
            Run(0.5f);
            var reply = _rocco.Say("Nice try, kid.", Clip(1f), "tts", SpeechPriority.Reply);
            Assert.AreEqual(UtteranceState.Interrupted, chat.State, "玩家在等的回答不排在閒聊後面");
            Assert.IsNull(_pip.Source.clip, "被切掉的聲音停了");
            Assert.AreEqual(UtteranceState.Playing, reply.State);
            Run(1.5f);
        }

        [Test]
        public void ReplyClip_WaitsForSomeoneElsesInfo_FirstInLine()
        {
            var info = _dot.Say("Knock twice to check.", Clip(1f), "t.knock", SpeechPriority.Info);
            var chat = _pip.Say("Hmm. Hmm hmm hmm.", Clip(0.5f), "t.hmm", SpeechPriority.Chatter);
            var reply = _rocco.Say("Nice try, kid.", Clip(1f), "tts", SpeechPriority.Reply);
            Assert.AreEqual(UtteranceState.Playing, info.State, "教學提示不被切掉");
            Assert.AreEqual(UtteranceState.Queued, reply.State);
            Run(1f + SpeechFloor.Gap + 0.1f);
            Assert.AreEqual(UtteranceState.Playing, reply.State, "回話排在閒聊前面");
            Assert.AreEqual(UtteranceState.Queued, chat.State);
        }

        [Test]
        public void StreamReply_CutsChatter_OverlapsInfo_AndLogsIt()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] floor: rocco overlaps dot \(stream reply\)$"));
            var info = _dot.Say("Knock twice to check.", Clip(1f), "t.knock", SpeechPriority.Info);
            var u = _rocco.SayStream("Nice try, kid.", Clip(60f, "voice_live"));
            Assert.AreEqual(UtteranceState.Playing, u.State, "串流的聲音已經在來：馬上播");
            Assert.AreEqual(UtteranceState.Playing, info.State);
            Run(1.2f, allowOverlap: true);
            _rocco.EndStream(u);

            Run(1f);
            var chat = _pip.Say("Hmm. Hmm hmm hmm.", Clip(2f), "t.hmm", SpeechPriority.Chatter);
            var u2 = _rocco.SayStream("Again, kid.", Clip(60f, "voice_live2"));
            Assert.AreEqual(UtteranceState.Interrupted, chat.State);
            _rocco.EndStream(u2);
        }

        [Test]
        public void HeldChatter_DoesNotBlockOthers()
        {
            // 玩家講話時（HoldBelow = Info）閒聊先不講；排著的閒聊不能擋住別人的 Info
            foreach (var p in _ps) p.Speech.HoldBelow = SpeechPriority.Info;
            var chat = _pip.Say("Hmm. Hmm hmm hmm.", Clip(0.5f), "t.hmm", SpeechPriority.Chatter);
            Run(0.5f);
            var info = _dot.Say("Knock twice to check.", Clip(1f), "t.knock", SpeechPriority.Info);
            Assert.AreEqual(UtteranceState.Playing, info.State);
            Assert.AreEqual(UtteranceState.Queued, chat.State);
        }

        [Test]
        public void ChannelWithoutAFloor_BehavesAsBefore()
        {
            var lone = CharacterFactory.Create(CharacterKind.Cold, null, Vector3.forward, Quaternion.identity, 6);
            lone.AutoUpdate = false;
            _spawned.Add(lone.gameObject);
            _dot.Say("Check.", Clip(1f), "t.check", SpeechPriority.Chatter);
            var u = lone.Speech.Say("Bet.", Clip(0.5f), "t.bet", SpeechPriority.Chatter);
            Assert.AreEqual(UtteranceState.Playing, u.State, "沒加入發言權的角色（單獨測試）照舊");
        }

        [Test]
        public void LeavingTheFloor_ReleasesIt()
        {
            _dot.Say("Check.", Clip(5f), "t.check", SpeechPriority.Chatter);
            Object.DestroyImmediate(_ps[0].gameObject);
            var u = _pip.Say("Hmm. Hmm hmm hmm.", Clip(0.5f), "t.hmm", SpeechPriority.Chatter);
            Assert.AreEqual(UtteranceState.Playing, u.State, "消失的角色不佔著發言權");
        }
    }
}
