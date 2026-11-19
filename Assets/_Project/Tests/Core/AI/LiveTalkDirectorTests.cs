using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>
    /// 即時牌桌對話的導演（2026-10-14 live talk）：玩家還在做決定時，每個訊號最多一個人講一句——
    /// 一次一個人、整桌間隔 ≥ 3 s、每個角色有冷卻、每個觸發每個角色用洗牌袋（不重複）、不蓋過玩家講話與別人的話、教學不講；
    /// 誰講、講不講依個性加權；而且「講不講」本身就是破綻：依 AI 演出來的內心狀態（TellDirector 依可信度濾過）決定。
    /// </summary>
    public class LiveTalkDirectorTests
    {
        static LiveSpeaker Ai(Archetype a, int seat, InnerState stance = InnerState.Unsure) => new LiveSpeaker { Seat = seat, Archetype = a, Stance = stance };
        static LiveSpeaker Dot() => new LiveSpeaker { Seat = -1, Archetype = null, Stance = InnerState.Masked };

        static LiveTalkContext Ctx(double now, params LiveSpeaker[] who)
        {
            var c = new LiveTalkContext { Now = now };
            c.Speakers.AddRange(who);
            return c;
        }

        static LiveTalkContext Table(double now, InnerState stance = InnerState.Unsure) =>
            Ctx(now, Ai(Archetype.Rookie, 1, stance), Ai(Archetype.Loudmouth, 2, stance), Ai(Archetype.Cold, 3, stance), Dot());

        static readonly LiveCue[] TurnCues =
            { LiveCue.Think3, LiveCue.Think7, LiveCue.Think12, LiveCue.ReLook, LiveCue.Reach, LiveCue.PullBack, LiveCue.ChipsHeld, LiveCue.PutBack, LiveCue.TowardCards, LiveCue.Gaze };

        static LiveSignal Sig(LiveCue c, double t, int count = 0, int seat = -1)
        {
            if (count == 0)
                count = c == LiveCue.Think3 ? 3 : c == LiveCue.Think7 ? 7 : c == LiveCue.Think12 ? 12 : c == LiveCue.ReLook ? 2 : c == LiveCue.ChipsHeld ? 3 : 0;
            return new LiveSignal(c, t, count, seat);
        }

        // ───────────── 台詞 ─────────────

        [Test]
        public void Lines_AreShortPgEnglish_UniqueIds_AndEveryAiHas2To4VariantsPerTrigger()
        {
            var all = LiveLines.All().ToList();
            Assert.AreEqual(all.Count, all.Select(l => l.Id).Distinct().Count(), "ids unique");
            foreach (var l in all)
            {
                Assert.IsTrue(l.Text.All(ch => ch >= 32 && ch < 127), l.Id + " ASCII English");
                Assert.That(l.Text.Length, Is.InRange(2, LiveLines.MaxChars), l.Id);
                StringAssert.StartsWith(l.Voice + ".live.", l.Id);
            }
            foreach (var who in new[] { "pip", "rocco", "vex" })
                foreach (var c in TurnCues.Concat(new[] { LiveCue.ReadRight, LiveCue.ReadFooled }))
                    Assert.That(LiveLines.For(who, c).Count, Is.InRange(1, 4), who + " " + c);
            foreach (var who in new[] { "pip", "rocco", "vex" })
                Assert.GreaterOrEqual(TurnCues.Count(c => LiveLines.For(who, c).Count >= 2), 8, who + ": listenable variety (2+ variants on most triggers)");
            Assert.That(LiveLines.For("dot", LiveCue.Think12).Count, Is.InRange(2, 4), "the dealer only has a few neutral prompts");
            Assert.AreEqual(0, LiveLines.For("dot", LiveCue.Reach).Count, "the dealer never needles");
            Assert.That(all.Count, Is.InRange(60, 130));
            Console.WriteLine($"[LiveTalk] {all.Count} lines, {all.Sum(l => l.Text.Length)} characters");
        }

        [Test]
        public void RoccoHasTheSignatureNeedles_PipAndVexToo()
        {
            var rocco = LiveLines.All().Where(l => l.Voice == "rocco").Select(l => l.Text).ToList();
            CollectionAssert.Contains(rocco, "Ooh, going in?");
            CollectionAssert.Contains(rocco, "Changed your mind? I saw that.");
            CollectionAssert.Contains(rocco, "Clock's ticking, champ.");
            CollectionAssert.Contains(LiveLines.All().Where(l => l.Voice == "pip").Select(l => l.Text).ToList(), "H-how much are you thinking?");
            CollectionAssert.Contains(LiveLines.All().Where(l => l.Voice == "vex").Select(l => l.Text).ToList(), "Checking again? Hm.");
        }

        // ───────────── 規則 ─────────────

        [Test]
        public void AtMostOneLinePerSignal_AndNeverWhileSomeoneIsTalking()
        {
            var d = new LiveTalkDirector(1);
            var ctx = Table(10);
            ctx.FloorBusy = true;
            var r = d.React(Sig(LiveCue.PullBack, 10), ctx);
            Assert.IsFalse(r.Spoke);
            Assert.AreEqual("floor busy", r.Skip);
            ctx.FloorBusy = false;
            ctx.SinceSpeech = 0.2f;
            Assert.AreEqual("just spoke", d.React(Sig(LiveCue.PullBack, 10), ctx).Skip, "a breath after someone else's line");
            int spoke = 0;
            for (int i = 0; i < 200; i++)
            {
                var dd = new LiveTalkDirector((ulong)i);
                var x = dd.React(Sig(LiveCue.PullBack, 10), Table(10, InnerState.Strong));
                if (x.Spoke) { spoke++; Assert.IsNotNull(x.Who); Assert.IsNull(x.Skip); }
                else Assert.IsNotNull(x.Skip);
            }
            Assert.Greater(spoke, 100, "a pull-back is usually worth a needle");
        }

        [Test]
        public void TableGap_AtLeast3SecondsBetweenLiveLines_AcrossCharacters()
        {
            Assert.GreaterOrEqual(LiveTalkDirector.TableGap, 3f);
            var d = new LiveTalkDirector(5) { };
            var times = new List<double>();
            for (double t = 0; t < 60; t += 0.5)
            {
                var r = d.React(Sig(TurnCues[(int)(t * 2) % TurnCues.Length], t, seat: 2), Table(t, InnerState.Strong));
                if (r.Spoke) times.Add(t);
            }
            Assert.Greater(times.Count, 6);
            for (int i = 1; i < times.Count; i++) Assert.GreaterOrEqual(times[i] - times[i - 1], LiveTalkDirector.TableGap - 1e-9, string.Join(",", times));
            var skip = new LiveTalkDirector(5);
            Assert.IsTrue(Enumerable.Range(0, 50).Select(i => skip.React(Sig(LiveCue.PullBack, 100 + i * 0.01), Table(100 + i * 0.01, InnerState.Strong))).Any(x => x.Skip == "table gap"));
        }

        [Test]
        public void PerCharacterCooldown_RoccoAloneCannotTalkEveryGap()
        {
            float cd = LiveTalkDirector.Cooldown("rocco");
            Assert.Greater(cd, LiveTalkDirector.TableGap);
            Assert.Greater(LiveTalkDirector.Cooldown("vex"), LiveTalkDirector.Cooldown("pip"));
            Assert.Greater(LiveTalkDirector.Cooldown("pip"), LiveTalkDirector.Cooldown("rocco"));
            var d = new LiveTalkDirector(2);
            var said = new List<double>();
            string lastSkip = null;
            for (double t = 0; t < 120; t += 0.25)
            {
                var r = d.React(Sig(LiveCue.PullBack, t), Ctx(t, Ai(Archetype.Loudmouth, 2, InnerState.Strong)));
                if (r.Spoke) said.Add(t); else if (r.Skip.StartsWith("cooldown")) lastSkip = r.Skip;
            }
            for (int i = 1; i < said.Count; i++) Assert.GreaterOrEqual(said[i] - said[i - 1], cd - 1e-9);
            Assert.AreEqual("cooldown rocco", lastSkip);
        }

        [Test]
        public void ShuffleBag_PerTriggerPerCharacter_NoRepeatsUntilAllVariantsWereSaid()
        {
            var d = new LiveTalkDirector(11);
            int n = LiveLines.For("rocco", LiveCue.PullBack).Count;
            Assert.GreaterOrEqual(n, 2);
            var said = new List<string>();
            for (double t = 0; said.Count < n * 5 && t < 5000; t += 20)
            {
                var r = d.React(Sig(LiveCue.PullBack, t), Ctx(t, Ai(Archetype.Loudmouth, 2, InnerState.Strong)));
                if (r.Spoke) said.Add(r.Line.Id);
            }
            Assert.AreEqual(n * 5, said.Count);
            for (int b = 0; b < 5; b++) Assert.AreEqual(n, said.Skip(b * n).Take(n).Distinct().Count(), string.Join(",", said));
            for (int i = 1; i < said.Count; i++) Assert.AreNotEqual(said[i - 1], said[i], "no back-to-back repeat across refills");
        }

        [Test]
        public void NeverOverThePlayersVoice_AndSilentInTheTutorial()
        {
            var d = new LiveTalkDirector(3);
            var c = Table(5, InnerState.Strong);
            c.PlayerSpeaking = true;
            Assert.AreEqual("player speaking", d.React(Sig(LiveCue.PullBack, 5), c).Skip);
            var spoke = d.React(Sig(LiveCue.PlayerSpoke, 6), Table(6, InnerState.Strong));
            Assert.IsFalse(spoke.Spoke, "the player started talking: listen");
            var t = Table(20, InnerState.Strong);
            t.Tutorial = true;
            for (int i = 0; i < 30; i++) Assert.AreEqual("tutorial", d.React(Sig(TurnCues[i % TurnCues.Length], 20 + i * 5), t).Skip);
        }

        /// <summary>
        /// 教學 v4 第 1 手（2026-10-14）：一定要讓玩家聽到 Rocco 讀他。Force＝跳過「這次不講」的骰子（再沒牌的演出狀態、再低的機率都講），
        /// 但一次一人、不蓋過別人、不蓋過玩家講話照舊（遊戲層等發言權空出來再送一次）。
        /// </summary>
        [Test]
        public void Force_AlwaysSpeaks_ButNeverOverSomeone()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var d = new LiveTalkDirector(seed);
                var c = Ctx(10, Ai(Archetype.Loudmouth, 2, InnerState.Weak));
                c.Force = true;
                var r = d.React(Sig(LiveCue.Think3, 10), c);
                Assert.IsTrue(r.Spoke, "seed " + seed + ": " + r.Skip);
                Assert.AreEqual("rocco", r.Who);
            }
            var busy = Ctx(10, Ai(Archetype.Loudmouth, 2, InnerState.Weak));
            busy.Force = true;
            busy.FloorBusy = true;
            Assert.AreEqual("floor busy", new LiveTalkDirector(1).React(Sig(LiveCue.Reach, 10), busy).Skip);
            var talking = Ctx(10, Ai(Archetype.Loudmouth, 2, InnerState.Weak));
            talking.Force = true;
            talking.PlayerSpeaking = true;
            Assert.AreEqual("player speaking", new LiveTalkDirector(1).React(Sig(LiveCue.Reach, 10), talking).Skip);
        }

        [Test]
        public void Gaze_OnlyTheAiBeingStaredAtAnswers()
        {
            var who = new HashSet<string>();
            for (int i = 0; i < 300; i++)
            {
                var r = new LiveTalkDirector((ulong)i).React(Sig(LiveCue.Gaze, 10, seat: 3), Table(10, InnerState.Strong));
                if (r.Spoke) who.Add(r.Who);
            }
            CollectionAssert.AreEquivalent(new[] { "vex" }, who);
        }

        [Test]
        public void TheDealer_OnlyGivesANeutralPromptOnAVeryLongThink()
        {
            var whoByCue = new Dictionary<LiveCue, HashSet<string>>();
            foreach (var c in TurnCues)
            {
                whoByCue[c] = new HashSet<string>();
                for (int i = 0; i < 300; i++)
                {
                    var r = new LiveTalkDirector((ulong)(i * 7 + 1)).React(Sig(c, 30), Ctx(30, Dot()));
                    if (r.Spoke) whoByCue[c].Add(r.Who);
                }
            }
            CollectionAssert.AreEquivalent(new[] { "dot" }, whoByCue[LiveCue.Think12]);
            foreach (var c in TurnCues.Where(x => x != LiveCue.Think12)) CollectionAssert.IsEmpty(whoByCue[c], c.ToString());
        }

        [Test]
        public void PersonalityWeights_RoccoNeedlesMost_VexIsRare()
        {
            var count = new Dictionary<string, int> { { "pip", 0 }, { "rocco", 0 }, { "vex", 0 }, { "dot", 0 } };
            for (int i = 0; i < 400; i++)
            {
                var d = new LiveTalkDirector((ulong)i);
                foreach (var c in TurnCues)
                {
                    var r = d.React(Sig(c, i * 1000 + (int)c * 50, seat: 1 + (int)c % 3), Table(i * 1000 + (int)c * 50));
                    if (r.Spoke) count[r.Who]++;
                }
            }
            Console.WriteLine("[LiveTalk] lines by character: " + string.Join(", ", count.Select(kv => kv.Key + " " + kv.Value)));
            Assert.Greater(count["rocco"], count["pip"] * 1.3);
            Assert.Greater(count["pip"], count["vex"] * 1.5);
            int total = count.Values.Sum();
            Assert.Less(count["vex"] / (double)total, 0.15, "Vex is rare");
            Assert.Greater(count["vex"], 0, "but not mute");
        }

        /// <summary>
        /// 講不講是破綻：每回合 AI 的真實狀態（強／弱）先經過 TellDirector 的可信度（Pip 92%、Rocco 65%、Vex 35% 且會反向演）變成「演出來的狀態」，
        /// 導演依演出來的狀態決定開不開口。所以 Pip 開口幾乎代表有牌；Rocco 有點參考價值；Vex 開口反而常是沒牌。
        /// </summary>
        [Test]
        public void TalkingIsATell_ConsistentWithEachCharactersReliability()
        {
            // 「他開口了 → 他有牌的機率」（真實狀態強弱各半）
            var post = new Dictionary<Archetype, double>();
            foreach (var a in new[] { Archetype.Rookie, Archetype.Loudmouth, Archetype.Cold })
            {
                var p = Personality.For(a);
                var tells = new TellDirector(1, p, new Pcg32(9, 1), new Pcg32(9, 2));
                int[] talk = new int[2], n = new int[2];
                for (int i = 0; i < 8000; i++)
                {
                    bool strong = i % 2 == 0;
                    var stance = tells.LiveStance(strong ? InnerState.Strong : InnerState.Weak);
                    var d = new LiveTalkDirector((ulong)i * 3 + 1);
                    var r = d.React(Sig(LiveCue.PullBack, 50), Ctx(50, Ai(a, 1, stance)));
                    int k = strong ? 0 : 1;
                    n[k]++;
                    if (r.Spoke) talk[k]++;
                }
                double ps = talk[0] / (double)n[0], pw = talk[1] / (double)n[1];
                post[a] = ps / (ps + pw);
                Console.WriteLine($"[LiveTalk] {p.Name}: talks {ps:P0} when strong, {pw:P0} when weak -> P(strong | talked) = {post[a]:P0}");
            }
            Assert.Greater(post[Archetype.Rookie], 0.75, "Pip talking means a hand");
            Assert.Greater(post[Archetype.Rookie], post[Archetype.Loudmouth] + 0.08, "Pip is the most honest");
            Assert.Greater(post[Archetype.Loudmouth], 0.55, "Rocco: some signal");
            Assert.Less(post[Archetype.Cold], post[Archetype.Loudmouth] - 0.1);
            Assert.Less(post[Archetype.Cold], 0.52, "Vex reverse-acts: talking is no sign of strength");
        }

        [Test]
        public void TheLiveStance_IsStableWithinATurn_AndDoesNotTouchTheDecisionRng()
        {
            var p = Personality.Loudmouth;
            var a = new TellDirector(2, p, new Pcg32(4, 13), new Pcg32(4, 15));
            var b = new TellDirector(2, p, new Pcg32(4, 13), new Pcg32(4, 15));
            for (int i = 0; i < 20; i++) a.LiveStance(InnerState.Strong);
            var ra = a.ForDecision(Intent.Value, 0.8, 0, false);
            var rb = b.ForDecision(Intent.Value, 0.8, 0, false);
            Assert.AreEqual(rb.Shown, ra.Shown, "live stance draws come from their own stream");
            Assert.AreEqual(rb.Cue.Tell, ra.Cue.Tell);
        }

        [Test]
        public void SkippedFirstLook_OnlyLookingAgainCounts()
        {
            var d = new LiveTalkDirector(1);
            Assert.AreEqual("first look", d.React(Sig(LiveCue.ReLook, 10, count: 1), Table(10, InnerState.Strong)).Skip);
        }

        [Test]
        public void Chattiness_RaisesTheChance_ForTheFoolRoccoScenario()
        {
            int Count(float k)
            {
                int n = 0;
                for (int i = 0; i < 500; i++)
                {
                    var c = Ctx(10, Ai(Archetype.Loudmouth, 2, InnerState.Unsure));
                    c.Chattiness = k;
                    if (new LiveTalkDirector((ulong)i).React(Sig(LiveCue.Think3, 10), c).Spoke) n++;
                }
                return n;
            }
            Assert.Greater(Count(2f), Count(1f) * 1.4);
        }
    }
}
