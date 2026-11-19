using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 真的牌桌：荷官與 AI 說的每一句都有預錄聲音，頭上的字幕就是那句、在聲音開始時出現（聽障玩家也看得到每一句）；
    /// 別的角色或同一個角色的下一句不會蓋掉正在講的字（2026-10-13 實機：荷官有字沒聲音、字被別句蓋掉）。
    /// </summary>
    public class SpeechPlayModeTests
    {
        GameTestRig _rig;
        string _path;

        [SetUp] public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "tell_speech_" + System.Guid.NewGuid().ToString("N") + ".json");

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            foreach (var f in new[] { _path, _path + ".tmp", _path + ".corrupt" }) if (File.Exists(f)) File.Delete(f);
        }

        static string Unwrap(SubtitleBubble b) => b.Text.Replace("\n", " ");

        [Test]
        public void TutorialFirstLine_PlaysItsAudio_AndTheBubbleShowsTheSameWords()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 40 }, g => { g.ProgressPath = _path; g.TutorialEnabled = true; });
            var dealer = _rig.Game.Dealer;
            string prompt = Tutorial.Prompt(TutorialStep.Peek);
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] dot clip=dot\.hello\.first dur=\d+\.\d\d text=""Hi, I'm Dot\. Let's play one together\.""$"));
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Speech\] dot clip=dot\.tutorial\.peek dur=\d+\.\d\d text=""Let's peek at your cards — cup your hand over them and lift your wrist\.""$"));
            Assert.IsTrue(_rig.IdleUntil(() => dealer.Speech.Current != null && dealer.Speech.Current.Text == prompt, 15), "Dot 講到第一步提示");
            var u = dealer.Speech.Current;
            Assert.AreEqual("dot.tutorial.peek", u.ClipId);
            Assert.IsNotNull(u.Clip, "有預錄聲音");
            Assert.AreSame(u.Clip, dealer.Speech.Source.clip, "荷官頭上的 AudioSource 播的就是這句");
            Assert.IsTrue(dealer.Subtitle.IsVisible, "字幕同時出現");
            Assert.AreEqual(prompt, Unwrap(dealer.Subtitle), "字幕就是講的那句");
            Assert.Greater(u.Duration, 1f);
            float dur = u.Duration;
            _rig.Idle(dur + SpeechChannel.ReadTail + 0.2f);
            Assert.IsFalse(dealer.Speech.Current == u, "播完了");
        }

        [Test]
        public void AiLineWhileTheDealerSpeaks_DoesNotOverwriteTheDealersBubble()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 5, MonteCarloTrials = 40 });
            var g = _rig.Game;
            var dealer = g.Dealer;
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 1, 2));
            // 等荷官真的講完（開場白、輪到誰）：配音長度會隨重錄改變，不能寫死秒數（2026-10-14 換模型後 4 s 不夠）
            double quietSince = -1;
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                bool quiet = dealer.Speech.LastEnded != null && !dealer.Speech.IsSpeaking && dealer.Speech.QueuedCount == 0;
                if (!quiet) { quietSince = -1; return false; }
                if (quietSince < 0) quietSince = g.Now;
                return g.Now - quietSince >= 1.5;   // 講過話，而且連續安靜 1.5 s（開場白、輪到誰都講完了）
            }, 20), "dealer quiet");
            Assert.IsTrue(g.Voice.Say(DealerLines.Invalid(InvalidReason.NotYourTurn), g.Now, "invalid", force: true));
            string dealerText = DealerLines.Invalid(InvalidReason.NotYourTurn);
            Assert.AreEqual(dealerText, dealer.Speech.ShownText);

            // 同時 Pip 說了一句：整桌一個發言權（SpeechFloor）→ Pip 先等荷官講完，不出字、不出聲；荷官的字不變
            var pip = g.Pip;
            pip.Speech.StopAll();   // Pip 這時可能剛好在講自己的台詞
            var line = "Why is it so warm in here?";
            pip.Play(new PerformanceCue { Seat = pip.CurrentCue.Seat, LineId = "pip.nervous.02", Subtitle = line });
            var pipU = pip.Speech.LastSubmitted;
            Assert.IsNotNull(pipU);
            Assert.AreEqual(UtteranceState.Queued, pipU.State, "荷官在講：Pip 等");
            Assert.IsNull(pip.Speech.ShownText, "等的時候不出字");
            Assert.AreEqual(dealerText, dealer.Speech.ShownText, "別人講話不會蓋掉荷官的字");
            Assert.AreEqual(dealerText, Unwrap(dealer.Subtitle));

            // 荷官自己的下一句排隊：講完這句才換字和聲音
            Assert.IsTrue(g.Voice.Say(DealerLines.Check, g.Now, "check", cooldown: 0f, force: true));
            Assert.AreEqual(dealerText, dealer.Speech.ShownText, "同一個角色的下一句也不會蓋掉");
            var cur = dealer.Speech.Current;
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                Assert.IsFalse(pip.Speech.IsSpeaking && dealer.Speech.IsSpeaking, "不會兩個人同時講");
                return dealer.Speech.Current != cur && dealer.Speech.Current != null;
            }, 6), "輪到下一句");
            Assert.AreNotEqual(dealerText, dealer.Speech.ShownText);
            Assert.AreEqual(dealer.Speech.Current.Text, dealer.Speech.ShownText, "字和正在播的聲音一致");
            Assert.AreEqual(UtteranceState.Queued, pipU.State, "荷官的 Info 排在 Pip 的閒聊前面");
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                Assert.IsFalse(pip.Speech.IsSpeaking && dealer.Speech.IsSpeaking, "不會兩個人同時講");
                if (pip.Speech.IsSpeaking) Assert.AreEqual(line, pip.Speech.ShownText);
                return pipU.State == UtteranceState.Done || pipU.State == UtteranceState.Dropped;
            }, 8), "Pip 講完（或等太久丟掉）");
        }

        static IEnumerable<CharacterPerformer> All(GameController g) => g.AllCharacters();

        [Test]
        public void IdlePlayerDuringTutorial_OneSpeakerAtATime_AndNobodyActsForThePlayer()
        {
            // 2026-10-13 實機：教學自動跳過（玩家沒動）時 Pip、Rocco、Vex 的長考台詞和荷官的 "I'll call for you." 同一瞬間開口。
            // 教學 v3 不再自動跳過、不替玩家出手（長考台詞也就不會在教學中出現；整桌冷卻改由 SessionLongHesitation 測）；
            // 這裡留下「同時只有一個角色在講、不同角色開口至少隔 0.2 s」的檢查，並確認提示升級的台詞也遵守。
            _rig = new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 40 }, g => { g.ProgressPath = _path; g.TutorialEnabled = true; });
            var game = _rig.Game;
            foreach (var c in All(game)) Assert.AreSame(game.Floor, c.Speech.Floor, c.Speech.Who + " 加入整桌的發言權");
            var starts = new List<(string who, string clip, double t)>();
            foreach (var c in All(game))
            {
                var who = c.Speech.Who;
                c.Speech.Started += u => starts.Add((who, u.ClipId, game.Now));
            }
            int maxSpeaking = 0;
            _rig.IdleUntil(() =>
            {
                int n = 0;
                foreach (var c in All(game)) if (c.Speech.IsSpeaking) n++;
                maxSpeaking = Mathf.Max(maxSpeaking, n);
                return false;
            }, 110);
            string dump = string.Join("\n", starts.ConvertAll(s => $"{s.t:0.000} {s.who} {s.clip}"));
            TestContext.WriteLine(dump);
            Assert.LessOrEqual(maxSpeaking, 1, "同時只有一個角色在講\n" + dump);
            Assert.IsTrue(starts.Exists(s => s.clip != null && s.clip.StartsWith("dot.tip.")), "卡住 25 s 有具體建議（有預錄聲音）\n" + dump);
            Assert.AreEqual(TutorialStep.Peek, game.Tutorial.Step, "沒有自動跳過");
            // 教學 v4 玩家是大盲（桌上只有自己的盲注）；沒有任何替玩家做的動作
            Assert.AreEqual(0, _rig.Actions.FindAll(a => a.seat == 0).Count, "沒人替玩家出手");
            Assert.AreNotEqual(Poker.Core.SeatStatus.Folded, game.Session.State.Seat(0).Status);
            foreach (var t in game.Voice.History) StringAssert.DoesNotContain("for you", t);
            for (int i = 1; i < starts.Count; i++)
                if (starts[i].who != starts[i - 1].who)
                    Assert.GreaterOrEqual(starts[i].t - starts[i - 1].t, 0.2, "不同角色開口至少隔 0.2 s\n" + dump);
        }

        [Test]
        public void SessionLongHesitation_OnlyOneAiGetsALine_AndTheNextTimeSomeoneElse()
        {
            var session = new GameSession(new GameConfig { Seed = 7, MonteCarloTrials = 40 });
            session.StartHand(0);
            var cues = session.SignalPlayer(new PlayerSignals { HesitationSeconds = 10f, Time = 100 });
            Assert.AreEqual(3, cues.Count, "三個 AI 都有表情");
            var first = cues.Where(c => c.Subtitle != null).ToList();
            Assert.AreEqual(1, first.Count, "只有一個開口");
            cues = session.SignalPlayer(new PlayerSignals { HesitationSeconds = 10f, Time = 104 });
            Assert.AreEqual(0, cues.Count(c => c.Subtitle != null), "整桌冷卻內沒人再催");
            cues = session.SignalPlayer(new PlayerSignals { HesitationSeconds = 10f, Time = 200 });
            var second = cues.Where(c => c.Subtitle != null).ToList();
            Assert.AreEqual(1, second.Count);
            Assert.AreNotEqual(first[0].Seat, second[0].Seat, "輪流：換一個人");
        }
    }
}
