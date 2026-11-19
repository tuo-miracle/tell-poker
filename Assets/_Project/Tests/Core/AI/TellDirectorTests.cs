using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    public class TellDirectorTests
    {
        static readonly Intent[] Intents = { Intent.Value, Intent.Bluff, Intent.SemiBluff, Intent.Marginal, Intent.GiveUp };

        static TellDirector Make(Personality p, ulong seed = 1) => new TellDirector(1, p, new Pcg32(seed));

        [TestCase(Archetype.Rookie)]
        [TestCase(Archetype.Loudmouth)]
        [TestCase(Archetype.Cold)]
        public void ConsistencyRate_MatchesCredibility(Archetype a)
        {
            var p = Personality.For(a);
            var d = Make(p, 7);
            int same = 0, reversed = 0, n = 20000;
            var rng = new Pcg32(99);
            for (int i = 0; i < n; i++)
            {
                var r = d.ForDecision(Intents[rng.NextBounded(5)], 0.5, i * 100.0, false);
                if (r.Shown == r.True) same++;
                if (r.Reversed) reversed++;
            }
            double rate = same / (double)n;
            Assert.AreEqual(p.TellCredibility, rate, 0.015, a + " 一致率");
            Assert.AreEqual((1 - p.TellCredibility) * p.ReverseActing, reversed / (double)n, 0.015, a + " 反向率");
            Assert.AreEqual(rate, d.Stats.ConsistencyRate, 1e-9);
        }

        [Test]
        public void TrueStates_FollowIntent()
        {
            Assert.AreEqual(InnerState.Strong, TellDirector.TrueStateOf(Intent.Value));
            Assert.AreEqual(InnerState.Bluffing, TellDirector.TrueStateOf(Intent.Bluff));
            Assert.AreEqual(InnerState.Bluffing, TellDirector.TrueStateOf(Intent.SemiBluff));
            Assert.AreEqual(InnerState.Unsure, TellDirector.TrueStateOf(Intent.Marginal));
            Assert.AreEqual(InnerState.Weak, TellDirector.TrueStateOf(Intent.GiveUp));
        }

        [Test]
        public void Rookie_TruthfulBluff_LooksNervous_WithHighIntensity()
        {
            var d = Make(Personality.Rookie, 3);
            int checkedCount = 0;
            for (int i = 0; i < 200; i++)
            {
                var r = d.ForDecision(Intent.Bluff, 0.2, i * 100.0, false);
                if (r.Shown != InnerState.Bluffing) continue;
                checkedCount++;
                Assert.AreEqual(Mood.Nervous, r.Cue.Mood);
                CollectionAssert.Contains(new[] { Tell.Swallow, Tell.Sweat, Tell.AvoidEyeContact, Tell.FidgetChips }, r.Cue.Tell);
                Assert.GreaterOrEqual(r.Cue.Intensity, 0.7f);
            }
            Assert.Greater(checkedCount, 150);
        }

        [Test]
        public void Reversal_ShowsTheOpposite()
        {
            var d = Make(Personality.Cold, 5);
            int seen = 0;
            for (int i = 0; i < 2000; i++)
            {
                var r = d.ForDecision(Intent.Value, 0.9, i * 100.0, false);
                if (!r.Reversed) continue;
                seen++;
                Assert.AreEqual(InnerState.Weak, r.Shown);
                var b = d.ForDecision(Intent.Bluff, 0.1, i * 100.0 + 50, false);
                if (b.Reversed) Assert.AreEqual(InnerState.Strong, b.Shown);
            }
            Assert.Greater(seen, 100);
        }

        [Test]
        public void BeingWatched_RaisesIntensity()
        {
            float Avg(bool gazed)
            {
                var d = Make(Personality.Loudmouth, 11);
                float s = 0;
                for (int i = 0; i < 500; i++) s += d.ForDecision(Intents[i % 5], 0.5, i * 100.0, gazed).Cue.Intensity;
                return s / 500;
            }
            Assert.Greater(Avg(true), Avg(false) + 0.1f);
            var dd = Make(Personality.Rookie, 2);
            var first = dd.ForDecision(Intent.Value, 0.9, 0, false).Cue;
            var g = dd.OnGazed(1.0);
            Assert.IsNotNull(g);
            Assert.GreaterOrEqual(g.Intensity, first.Intensity);
        }

        [Test]
        public void ReactsToPlayer_BigBetByPersonality_TankMakesRoccoLeanInAndTalk()
        {
            Assert.AreEqual(Mood.Nervous, Make(Personality.Rookie).OnPlayerNotice(PlayerNotice.BigBet, 0).Mood);
            Assert.AreEqual(Mood.Annoyed, Make(Personality.Loudmouth).OnPlayerNotice(PlayerNotice.BigBet, 0).Mood);
            var tank = Make(Personality.Loudmouth).OnPlayerNotice(PlayerNotice.LongHesitation, 0);
            Assert.AreEqual(Tell.LeanIn, tank.Tell);
            Assert.IsNotNull(tank.LineId);
            Assert.IsFalse(string.IsNullOrEmpty(tank.Subtitle));
            Assert.IsNull(Make(Personality.Rookie).OnPlayerNotice(PlayerNotice.None, 0));
        }

        [Test]
        public void HandEnd_HappyOrDejected()
        {
            Assert.AreEqual(Mood.Happy, Make(Personality.Rookie).OnHandEnd(true, 0).Mood);
            Assert.AreEqual(Mood.Dejected, Make(Personality.Rookie).OnHandEnd(false, 0).Mood);
        }

        [Test]
        public void StreetReaction_FollowsEquitySwing()
        {
            var d = Make(Personality.Rookie, 4);
            int good = 0, bad = 0;
            for (int i = 0; i < 100; i++)
            {
                var up = d.OnStreet(0.3, 0.8, i * 100.0, false);
                if (up != null && up.True == InnerState.Strong && up.Shown == InnerState.Strong) good++;
                var down = d.OnStreet(0.8, 0.3, i * 100.0 + 50, false);
                if (down != null && down.Shown == InnerState.Weak) bad++;
            }
            Assert.Greater(good, 80); Assert.Greater(bad, 80);
            Assert.IsNull(d.OnStreet(0.5, 0.52, 99999, false), "沒變化就不演");
        }

        [Test]
        public void SameSeed_SameCues()
        {
            var a = Make(Personality.Cold, 42); var b = Make(Personality.Cold, 42);
            for (int i = 0; i < 100; i++)
            {
                var x = a.ForDecision(Intents[i % 5], 0.5, i * 3.0, i % 2 == 0).Cue;
                var y = b.ForDecision(Intents[i % 5], 0.5, i * 3.0, i % 2 == 0).Cue;
                Assert.AreEqual((x.Mood, x.Tell, x.Look, x.Intensity, x.LineId), (y.Mood, y.Tell, y.Look, y.Intensity, y.LineId));
            }
        }
    }

    public class LineBookTests
    {
        static readonly string[] Characters = { "pip", "rocco", "vex", "silas", "dot" };

        [Test]
        public void EachCharacter_Has15To25OriginalShortEnglishLines()
        {
            var all = LineBook.All;
            Assert.AreEqual(all.Count, all.Select(l => l.Id).Distinct().Count(), "LineId 不可重複");
            foreach (var ch in Characters)
            {
                var mine = all.Where(l => l.Id.StartsWith(ch + ".")).ToList();
                Assert.That(mine.Count, Is.InRange(15, 25), ch);
                foreach (var l in mine)
                {
                    Assert.IsTrue(l.Text.All(c => c >= 32 && c < 127), l.Id + " 必須是 ASCII 英文");
                    Assert.That(l.Text.Length, Is.InRange(2, 60), l.Id);
                }
            }
        }

        [Test]
        public void EveryAiTrigger_HasLinesForEachAiCharacter()
        {
            foreach (var a in new[] { Archetype.Rookie, Archetype.Loudmouth, Archetype.Cold, Archetype.Veteran })
                foreach (LineTrigger t in Enum.GetValues(typeof(LineTrigger)))
                    if (!t.ToString().StartsWith("Dealer"))
                        Assert.IsNotEmpty(LineBook.For(a, t).ToList(), a + " " + t);
            foreach (LineTrigger t in Enum.GetValues(typeof(LineTrigger)))
                if (t.ToString().StartsWith("Dealer"))
                    Assert.IsNotEmpty(LineBook.ForDealer(t).ToList(), t.ToString());
        }

        [Test]
        public void Speaker_RespectsCooldown_AndAvoidsRepeats()
        {
            var sp = new LineSpeaker(Personality.Loudmouth, new Pcg32(1));
            var a = sp.TrySpeak(LineTrigger.PlayerTanks, 0, 1f);
            Assert.IsNotNull(a);
            Assert.IsNull(sp.TrySpeak(LineTrigger.PlayerTanks, 1, 1f), "冷卻中");
            var b = sp.TrySpeak(LineTrigger.PlayerTanks, 100, 1f);
            Assert.IsNotNull(b);
            Assert.AreNotEqual(a.Id, b.Id, "同一情境不連續講同一句");
        }

        [Test]
        public void Speaker_IsNotTooChatty()
        {
            // 每 3 秒一個決策點、連續 10 分鐘：台詞間隔至少為冷卻時間
            var sp = new LineSpeaker(Personality.Loudmouth, new Pcg32(5));
            int lines = 0;
            for (double t = 0; t < 600; t += 3) if (sp.TrySpeak(LineTrigger.ValueBet, t, 0.5f) != null) lines++;
            Assert.LessOrEqual(lines, 600 / sp.CooldownSeconds + 1);
            Assert.Greater(lines, 5);
        }
    }
}
