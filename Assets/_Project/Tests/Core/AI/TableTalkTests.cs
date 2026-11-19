using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>
    /// 台詞挑選（2026-10-13 實機：玩家長考時三個 AI 同時講話、Pip 一分鐘內講了三次同一句）：
    /// - LineSpeaker：同一情境用「洗牌袋」輪流講完所有變化才重來；同一句至少隔 RepeatSeconds；情境各自有冷卻（長考 20 s）。
    /// - TableTalk：「大家都有反應」的時候（長考、攤牌輸贏、翻牌）只挑一個 AI 開口（依多話程度加權、不連續挑同一個），其他人只演表情。
    /// </summary>
    public class TableTalkTests
    {
        static LineSpeaker Speaker(Personality p, ulong seed = 1) => new LineSpeaker(p, new Pcg32(seed)) { CooldownSeconds = 0f };

        [Test]
        public void ShuffleBag_UsesEveryVariantBeforeRepeating()
        {
            var sp = Speaker(Personality.Loudmouth, 3);
            int n = LineBook.For(Archetype.Loudmouth, LineTrigger.Win).Count();
            Assert.GreaterOrEqual(n, 2);
            var said = new List<string>();
            for (int i = 0; i < n * 6; i++)
            {
                var l = sp.TrySpeak(LineTrigger.Win, i * 100.0, 1f);
                Assert.IsNotNull(l, "第 " + i + " 次");
                said.Add(l.Id);
            }
            for (int b = 0; b < 6; b++)
                CollectionAssert.AreEquivalent(LineBook.For(Archetype.Loudmouth, LineTrigger.Win).Select(l => l.Id), said.Skip(b * n).Take(n), "每一輪都講過每一句、沒有重複：" + string.Join(",", said));
            for (int i = 1; i < said.Count; i++) Assert.AreNotEqual(said[i - 1], said[i], "換輪時也不連續講同一句");
        }

        [Test]
        public void ShuffleBag_ThreeTankLines_NoRepeatWithinThree()
        {
            var sp = Speaker(Personality.Loudmouth, 9);
            var said = new List<string>();
            for (int i = 0; i < 12; i++) said.Add(sp.TrySpeak(LineTrigger.PlayerTanks, i * 100.0, 1f).Id);
            for (int i = 0; i + 3 <= said.Count; i += 3) Assert.AreEqual(3, said.Skip(i).Take(3).Distinct().Count(), string.Join(",", said));
        }

        [Test]
        public void TankCooldown_PerCharacter_IsAtLeast20Seconds()
        {
            Assert.GreaterOrEqual(LineSpeaker.TriggerCooldown(LineTrigger.PlayerTanks), 20f);
            var sp = Speaker(Personality.Loudmouth);
            Assert.IsNotNull(sp.TrySpeak(LineTrigger.PlayerTanks, 0, 1f));
            Assert.IsNull(sp.TrySpeak(LineTrigger.PlayerTanks, 19.5, 1f), "長考台詞 20 s 內不再講");
            Assert.IsNotNull(sp.TrySpeak(LineTrigger.ValueBet, 19.5, 1f), "別的情境不受影響");
            Assert.IsNotNull(sp.TrySpeak(LineTrigger.PlayerTanks, 20.5, 1f));
        }

        [Test]
        public void SameLine_NotRepeatedWithinRepeatSeconds_EvenWithOneVariant()
        {
            // Pip 的長考台詞只有一句：實機一分鐘講了三次
            Assert.AreEqual(1, LineBook.For(Archetype.Rookie, LineTrigger.PlayerTanks).Count());
            var sp = Speaker(Personality.Rookie);
            Assert.AreEqual("pip.tank.01", sp.TrySpeak(LineTrigger.PlayerTanks, 0, 1f).Id);
            Assert.IsNull(sp.TrySpeak(LineTrigger.PlayerTanks, 30, 1f));
            Assert.IsNull(sp.TrySpeak(LineTrigger.PlayerTanks, LineSpeaker.RepeatSeconds - 1, 1f));
            Assert.IsNotNull(sp.TrySpeak(LineTrigger.PlayerTanks, LineSpeaker.RepeatSeconds + 1, 1f));
        }

        [Test]
        public void Propose_DoesNotConsumeTheLine_UntilCommitted()
        {
            var sp = Speaker(Personality.Rookie);
            var a = sp.Propose(LineTrigger.PlayerTanks, 0, 1f);
            Assert.IsNotNull(a);
            Assert.IsNotNull(sp.Propose(LineTrigger.PlayerTanks, 0.1, 1f), "只是提議：沒講出來就不算講過");
            sp.Commit(a, 0.1);
            Assert.IsNull(sp.Propose(LineTrigger.PlayerTanks, 1, 1f), "講過了才進冷卻");
        }

        static (TableTalk talk, TellDirector[] ds) Table(ulong seed = 1)
        {
            var talk = new TableTalk(seed);
            var ds = new[] { Personality.Rookie, Personality.Loudmouth, Personality.Cold }
                .Select((p, i) => new TellDirector(i + 1, p, new Pcg32(seed + (ulong)i * 17)) { Table = talk }).ToArray();
            foreach (var d in ds) d.Speaker.CooldownSeconds = 0f;
            return (talk, ds);
        }

        [Test]
        public void EveryoneReacts_OnlyOneAiSpeaks_OthersJustAnimate()
        {
            var (talk, ds) = Table();
            var cues = ds.Select(d => d.OnPlayerNotice(PlayerNotice.LongHesitation, 0)).ToList();
            Assert.IsTrue(cues.All(c => c != null), "三個都有表情");
            Assert.AreEqual(0, cues.Count(c => c.Subtitle != null), "還沒決定誰講");
            var line = talk.Resolve();
            Assert.IsNotNull(line);
            Assert.AreEqual(1, cues.Count(c => c.Subtitle != null), "只有一個開口");
            var speaking = cues.Single(c => c.Subtitle != null);
            Assert.AreEqual(line.Text, speaking.Subtitle);
            Assert.AreEqual(line.Id, speaking.LineId);
            Assert.IsTrue(cues.Where(c => c != speaking).All(c => c.LineId == null), "其他人沒有台詞（不出聲、不出字）");
            Assert.AreEqual(0, talk.Pending);
        }

        [Test]
        public void EveryoneReacts_WeightedByPersonality_AndRotates()
        {
            var (talk, ds) = Table(5);
            var who = new List<string>();
            for (int i = 0; i < 600; i++)
            {
                double now = i * 100.0;
                foreach (var d in ds) d.OnHandEnd(i % 2 == 0, now);
                var l = talk.Resolve();
                if (l != null) who.Add(LineBook.VoiceOf(l));
            }
            int pip = who.Count(w => w == "pip"), rocco = who.Count(w => w == "rocco"), vex = who.Count(w => w == "vex");
            TestContext.WriteLine($"pip={pip} rocco={rocco} vex={vex}");
            Assert.Greater(rocco, pip, "大嘴巴最常開口");
            Assert.Greater(pip, vex, "冷面最少");
            Assert.Greater(vex, 0, "冷面偶爾也會講");
            for (int i = 1; i < who.Count; i++) Assert.AreNotEqual(who[i - 1], who[i], "不連續挑同一個人（第 " + i + " 次）");
        }

        [Test]
        public void HeadsUp_TheOnlyAiStillTalksEveryTime()
        {
            // 一對一只有 Pip：輪流規則不能讓他每隔一次就閉嘴
            var talk = new TableTalk(1);
            var d = new TellDirector(1, Personality.Rookie, new Pcg32(3)) { Table = talk };
            d.Speaker.CooldownSeconds = 0f;
            int spoke = 0, offered = 0;
            for (int i = 0; i < 40; i++)
            {
                d.OnHandEnd(i % 2 == 0, i * 100.0);
                if (talk.Pending > 0) offered++;
                if (talk.Resolve() != null) spoke++;
            }
            Assert.Greater(offered, 10);
            Assert.AreEqual(offered, spoke, "有提議就講");
        }

        [Test]
        public void TankChatter_TableWideCooldown()
        {
            Assert.GreaterOrEqual(TableTalk.TableCooldown(LineTrigger.PlayerTanks), 8f);
            var (talk, ds) = Table(2);
            foreach (var d in ds) d.OnPlayerNotice(PlayerNotice.LongHesitation, 0);
            Assert.IsNotNull(talk.Resolve());
            var cues = ds.Select(d => d.OnPlayerNotice(PlayerNotice.LongHesitation, 5)).ToList();
            Assert.IsNull(talk.Resolve(), "8 s 內整桌不再有人講長考台詞");
            Assert.IsTrue(cues.All(c => c.Subtitle == null));
            foreach (var d in ds) d.OnPlayerNotice(PlayerNotice.LongHesitation, 9);
            Assert.IsNotNull(talk.Resolve(), "換另一個人（自己的 20 s 冷卻沒到的人）");
        }

        /// <summary>
        /// 即時對話收尾（2026-10-14）：這一回合已經用即時台詞講過話的角色，玩家行動後不再補一句舊的「長考」台詞（Veto 回傳原因）；
        /// 其他角色照常可以講，被擋的人的那一句不算講過（不進冷卻、洗牌袋不動）。
        /// </summary>
        [Test]
        public void Veto_SuppressesTankLine_ForAVoiceThatAlreadySpokeLive()
        {
            var (talk, ds) = Table(2);
            var traces = new List<string>();
            talk.Trace = traces.Add;
            talk.Veto = l => l.Trigger == LineTrigger.PlayerTanks && LineBook.VoiceOf(l) != "pip" ? "spoke live this turn" : null;
            var cues = ds.Select(d => d.OnPlayerNotice(PlayerNotice.LongHesitation, 0)).ToList();
            var line = talk.Resolve();
            Assert.IsNotNull(line, string.Join(" | ", traces));
            Assert.AreEqual("pip", LineBook.VoiceOf(line), "只剩 Pip 可以講");
            Assert.AreEqual(1, cues.Count(c => c.Subtitle != null));

            // 全部都被擋：沒有人講，trace 寫出原因；被擋的人台詞沒被用掉
            talk.Veto = l => "spoke live this turn";
            foreach (var d in ds) d.OnPlayerNotice(PlayerNotice.LongHesitation, 100);
            Assert.IsNull(talk.Resolve());
            StringAssert.Contains("none (spoke live this turn)", traces.Last());
            talk.Veto = null;
            foreach (var d in ds) d.OnPlayerNotice(PlayerNotice.LongHesitation, 101);
            var after = talk.Resolve();
            Assert.IsNotNull(after, "veto 拿掉後照常講（被擋那次沒有進冷卻）");
        }

        [Test]
        public void Veto_DoesNotTouchOtherTriggers()
        {
            var (talk, ds) = Table(3);
            talk.Veto = l => l.Trigger == LineTrigger.PlayerTanks ? "spoke live this turn" : null;
            Line l = null;
            for (int i = 0; i < 20 && l == null; i++)
            {
                foreach (var d in ds) d.OnHandEnd(true, i * 100.0);
                l = talk.Resolve();
            }
            Assert.IsNotNull(l, "輸贏的台詞不受影響");
        }

        [Test]
        public void DecisionLines_AreNotPartOfTheChorus()
        {
            // 輪到 AI 時的台詞只有一個人（而且在背景執行緒決策），照舊直接決定
            var (talk, ds) = Table(4);
            bool any = false;
            for (int i = 0; i < 50 && !any; i++)
            {
                var r = ds[1].ForDecision(Intent.Value, 0.8, i * 100.0, false);
                any = r.Cue.Subtitle != null;
            }
            Assert.IsTrue(any);
            Assert.AreEqual(0, talk.Pending);
        }
    }
}
