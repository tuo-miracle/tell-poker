using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>
    /// 第 4 位對手（2026-11-02 使用者決定，坐荷官右側）：老手 Silas，緊凶、話少、乾冷幽默。
    /// 和 Pip／Rocco／Vex 同一套機制（Personality、LineBook、LiveLines），台詞量不超過現有最多的座位（Rocco）。
    /// </summary>
    public class VeteranTests
    {
        static readonly Archetype[] FiveSeat = { Archetype.Rookie, Archetype.Loudmouth, Archetype.Veteran, Archetype.Cold };

        [Test]
        public void Personality_IsATightAggressiveQuietVeteran()
        {
            var v = Personality.For(Archetype.Veteran);
            Assert.AreEqual("Silas", v.Name);
            Assert.AreEqual(Archetype.Veteran, v.Archetype);
            var rocco = Personality.Loudmouth; var vex = Personality.Cold; var pip = Personality.Rookie;
            Assert.Less(v.Vpip, rocco.Vpip, "tighter than Rocco");
            Assert.Greater(v.Pfr / v.Vpip, pip.Pfr / pip.Vpip, "raises most hands he plays (Pip limps)");
            Assert.GreaterOrEqual(v.Pfr / v.Vpip, 0.7f, "tight-aggressive: PFR close to VPIP");
            Assert.GreaterOrEqual(v.Aggression, 0.75f, "aggressive after the flop");
            Assert.Less(v.BluffFrequency, rocco.BluffFrequency, "bluffs less than Rocco");
            Assert.LessOrEqual(v.Talkativeness, vex.Talkativeness, "talks as little as Vex");
            Assert.Less(v.Expressiveness, pip.Expressiveness);
            Assert.That(v.TellCredibility, Is.InRange(vex.TellCredibility, rocco.TellCredibility), "more readable than Vex, less than Rocco");
            Assert.AreNotSame(Personality.For(Archetype.Veteran), Personality.For(Archetype.Veteran), "fresh copy each time");
        }

        [Test]
        public void LineBook_HasEveryAiTrigger_ShortAsciiLines_NoMoreThanTheChattiestSeat()
        {
            var mine = LineBook.All.Where(l => l.Id.StartsWith("silas.")).ToList();
            int most = new[] { "pip.", "rocco.", "vex." }.Max(p => LineBook.All.Count(l => l.Id.StartsWith(p)));
            Assert.That(mine.Count, Is.InRange(15, most), "same 15-25 range as the others, never more than the biggest seat");
            foreach (LineTrigger t in Enum.GetValues(typeof(LineTrigger)))
                if (!t.ToString().StartsWith("Dealer"))
                    Assert.IsNotEmpty(LineBook.For(Archetype.Veteran, t).ToList(), t.ToString());
            foreach (var l in mine)
            {
                Assert.IsTrue(l.Text.All(c => c >= 32 && c < 127), l.Id);
                Assert.That(l.Text.Length, Is.InRange(2, 60), l.Id);
                Assert.AreEqual("silas", LineBook.VoiceOf(l));
            }
            Assert.IsEmpty(LineBook.For(Archetype.Veteran, LineTrigger.ValueBet).Where(l => !l.Id.StartsWith("silas.")), "only his own lines");
        }

        [Test]
        public void LiveLines_EveryTurnCue_FewWordsLikeVex_TotalNotAboveTheChattiestSeat()
        {
            Assert.AreEqual("silas", LiveLines.VoiceOf(Archetype.Veteran));
            var cues = new[] { LiveCue.Think3, LiveCue.Think7, LiveCue.Think12, LiveCue.ReLook, LiveCue.Reach, LiveCue.PullBack, LiveCue.ChipsHeld,
                               LiveCue.PutBack, LiveCue.TowardCards, LiveCue.Gaze, LiveCue.ReadRight, LiveCue.ReadFooled };
            foreach (var c in cues) Assert.That(LiveLines.For("silas", c).Count, Is.InRange(1, 4), c.ToString());
            var mine = LiveLines.All().Where(l => l.Voice == "silas").ToList();
            foreach (var l in mine)
            {
                StringAssert.StartsWith("silas.live.", l.Id);
                Assert.That(l.Text.Length, Is.InRange(2, LiveLines.MaxChars), l.Id);
            }
            int Total(string who) => LineBook.All.Count(l => l.Id.StartsWith(who + ".")) + LiveLines.All().Count(l => l.Voice == who);
            Assert.LessOrEqual(Total("silas"), new[] { "pip", "rocco", "vex" }.Max(Total), "no more lines than the chattiest seat");
            Assert.Less(mine.Average(l => l.Text.Length), LiveLines.All().Where(l => l.Voice == "rocco").Average(l => l.Text.Length), "terse");
        }

        [Test]
        public void Quiet_LongCooldowns()
        {
            Assert.GreaterOrEqual(new LineSpeaker(Personality.For(Archetype.Veteran), new Pcg32(1)).CooldownSeconds, 14f, "at least Pip's gap");
            Assert.GreaterOrEqual(LiveTalkDirector.Cooldown("silas"), LiveTalkDirector.Cooldown("pip"));
        }

        /// <summary>
        /// 5 人桌（玩家＋Pip、Rocco、Silas、Vex）自我對弈：Silas 的 VPIP／PFR／AF 95% 區間整段在他的目標內，其他三位照舊在自己的目標內
        /// （他們的目標是 4 人桌量的；多一個人入池率會掉一點，所以只檢查 Silas 的完整目標，其他人只看 PFR 的相對順序）。
        /// 手數：Mono 20,000 手；Unity EditMode 8,000 手（MC 1000 次的成本同 SelfPlay_100kHands 的說明）。
        /// </summary>
        [Test, Category("Slow")]
        public void FiveSeatSelfPlay_VeteranStatsWithinHisTargets()
        {
#if UNITY_5_3_OR_NEWER
            const int perShard = 2000;
#else
            const int perShard = 5000;
#endif
            const int shards = 4;
            var sw = Stopwatch.StartNew();
            var parts = new SeatTally[shards][];
            Parallel.For(0, shards, i => parts[i] = SelfPlay.Run(perShard, (ulong)(2000 + i), new AiSettings { ReadSettings = new PlayerReadSettings() }, FiveSeat));
            var total = Enumerable.Range(0, 5).Select(_ => new SeatTally()).ToArray();
            foreach (var p in parts) for (int s = 0; s < 5; s++) total[s].Add(p[s]);
            Console.WriteLine(SelfPlay.Table(total, $"[5 人桌自我對弈 {shards * perShard} 手，{sw.Elapsed.TotalSeconds:0} 秒]", FiveSeat));
            var v = Personality.For(Archetype.Veteran); var g = v.Targets; var x = total[3];
            SelfPlayTests.AssertRateCi(x.Vpip, x.Hands, g.VpipMin, g.VpipMax, "Silas VPIP");
            SelfPlayTests.AssertRateCi(x.Pfr, x.Hands, g.PfrMin, g.PfrMax, "Silas PFR");
            SelfPlayTests.AssertRatioCi(x.PostAggr, x.PostCalls, g.AfMin, g.AfMax, "Silas AF");
            // 破綻一致率：只有 1 個座位的樣本（Unity 約 1 萬次、Mono 約 2.6 萬次），95% 區間半寬約 1 pp，容許 ±2.5 pp
            SelfPlayTests.AssertRateCi(x.TellsConsistent, x.Tells, v.TellCredibility - 0.025, v.TellCredibility + 0.025, "Silas 破綻一致率");
            var pip = total[1]; var rocco = total[2];
            Assert.Greater(SelfPlay.Z(x.Pfr, x.Hands, pip.Pfr, pip.Hands), 4, "PFR: Silas > Pip");
            Assert.Greater(SelfPlay.Z(rocco.Vpip, rocco.Hands, x.Vpip, x.Hands), 4, "VPIP: Rocco > Silas");
        }
    }
}
