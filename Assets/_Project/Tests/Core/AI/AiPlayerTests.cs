using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>AI 測試小工具。</summary>
    internal static class AiKit
    {
        public static AiSettings Fast(int trials = 150) => new AiSettings { MonteCarloTrials = trials, ReactionTrials = 100, ReadSettings = new PlayerReadSettings() };

        /// <summary>每位 AI 只看自己可見的事件。</summary>
        public static void Feed(IEnumerable<AiPlayer> ais, IReadOnlyList<GameEvent> events)
        {
            foreach (var ai in ais) ai.OnEvents(PlayerView.Filter(events, ai.Seat));
        }

        public static Personality P(int i) => Personality.For((Archetype)(i % 3));
    }

    public class AiPlayerTests
    {
        static Card[] C(string s) => Card.ParseMany(s);

        [Test]
        public void AlwaysReturnsLegalAction_RandomTables()
        {
            var rng = new Pcg32(2026);
            int decisions = 0;
            for (int game = 0; game < 120; game++)
            {
                int seats = 2 + (int)rng.NextBounded(5);
                var stacks = Enumerable.Range(0, seats).Select(_ => rng.NextBounded(4) == 0 ? 1 + (int)rng.NextBounded(30) : 50 + (int)rng.NextBounded(600)).ToArray();
                var ais = Enumerable.Range(0, seats).Select(s => new AiPlayer(AiKit.P(s + game), s, (ulong)(game * 10 + s), AiKit.Fast(60))).ToArray();
                var state = HandState.NewTable(stacks);
                for (int hand = 0; hand < 6; hand++)
                {
                    if (state.Stacks().Count(x => x > 0) < 2) break;
                    var r = Engine.Apply(state, new StartHand((ulong)(game * 100 + hand), null, 5, 10));
                    Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
                    state = r.State; AiKit.Feed(ais, r.Events);
                    while (state.Phase == HandPhase.Betting && state.ToAct >= 0)
                    {
                        var ai = ais[state.ToAct];
                        var d = ai.Decide(state, ai.Seat, ai.Observations);
                        var ar = Engine.Apply(state, d.Command);
                        Assert.IsTrue(ar.IsSuccess, $"不合法：{d.Command.GetType().Name} {ar.Error} 座位 {ai.Seat}");
                        decisions++;
                        state = ar.State; AiKit.Feed(ais, ar.Events);
                    }
                }
            }
            Assert.Greater(decisions, 1500);
        }

        static AiDecision DecideAt(Personality p, ulong seed, string holes, string board, Func<Table, Table> actions, int aiSeat, int[] stacks = null, AiSettings settings = null)
        {
            var t = Table.New(stacks ?? new[] { 1000, 1000, 1000, 1000 });
            var ai = new AiPlayer(p, aiSeat, seed, settings ?? AiKit.Fast(300));
            t.Start(0, 5, 10, holes, board);
            ai.OnEvents(PlayerView.Filter(t.Events, aiSeat));
            int before = t.Events.Count;
            actions(t);
            ai.OnEvents(PlayerView.Filter(t.Events.Skip(before).ToList(), aiSeat));
            Assert.AreEqual(aiSeat, t.S.ToAct, "測試情境錯：還沒輪到 AI");
            return ai.Decide(t.S, aiSeat, ai.Observations);
        }

        // 4 人桌按鈕 0：SB=1、BB=2，翻牌前 3 先行動；翻牌後 1 先行動
        static Table ToFlopAllCall(Table t) =>
            t.Do(new Call(3)).Do(new Call(0)).Do(new Call(1)).Do(new Check(2));

        [Test]
        public void NeverFoldsTheNuts_FacingAllIn()
        {
            foreach (var a in new[] { Archetype.Rookie, Archetype.Loudmouth, Archetype.Cold, Archetype.Veteran })
                for (ulong seed = 0; seed < 15; seed++)
                {
                    // 皇家同花順在手，翻牌後對手 all-in
                    var d = DecideAt(Personality.For(a), seed, "1:AsKs 2:7c7d 3:9d8d 0:2h3h", "QsJsTs4c5c",
                        t => ToFlopAllCall(t).Do(new Check(1)).Do(new AllIn(2)).Do(new Fold(3)).Do(new Fold(0)), 1);
                    Assert.IsNotInstanceOf<Fold>(d.Command, a + " seed " + seed);
                    Assert.AreEqual(1.0, d.RawEquity, 1e-9);
                    // 翻牌前 AA 面對 all-in
                    var p = DecideAt(Personality.For(a), seed, "2:AhAd 0:KcKd", null,
                        t => t.Do(new Fold(3)).Do(new AllIn(0)).Do(new Fold(1)), 2);
                    Assert.IsNotInstanceOf<Fold>(p.Command, a + " AA seed " + seed);
                }
        }

        [Test]
        public void DoesNotUseHiddenInformation_OtherHoleCardsSwapped()
        {
            string[] others = { "0:2h3h 2:7c7d 3:9d8d", "0:AhAd 2:KhKd 3:QcQd", "0:6c5c 2:Jh9h 3:4d4h" };
            foreach (var a in new[] { Archetype.Rookie, Archetype.Loudmouth, Archetype.Cold, Archetype.Veteran })
                for (ulong seed = 0; seed < 5; seed++)
                {
                    var results = others.Select(o => DecideAt(Personality.For(a), seed, "1:Ac9s " + o, "As9c4c2d7h",
                        t => ToFlopAllCall(t).Do(new Check(1)).Do(new Bet(2, 40)).Do(new Call(3)).Do(new Call(0)), 1)).ToList();
                    foreach (var r in results.Skip(1))
                    {
                        Assert.AreEqual(Describe(results[0].Command), Describe(r.Command));
                        Assert.AreEqual(results[0].RawEquity, r.RawEquity);
                        Assert.AreEqual(results[0].ThinkSeconds, r.ThinkSeconds);
                        Assert.AreEqual(results[0].Intent, r.Intent);
                        Assert.AreEqual((results[0].Cue.Mood, results[0].Cue.Tell, results[0].Cue.Intensity), (r.Cue.Mood, r.Cue.Tell, r.Cue.Intensity));
                    }
                }
        }

        [Test]
        public void Observations_RejectEventsTheSeatCannotSee()
        {
            var t = Table.New(1000, 1000).Start(0);
            var obs = new TableObservations(1);
            var other = t.Events.OfType<HoleCardsDealt>().First(e => e.Seat == 0);
            Assert.Throws<InvalidOperationException>(() => obs.Observe(other));
            Assert.DoesNotThrow(() => obs.Observe(t.Events.OfType<HoleCardsDealt>().First(e => e.Seat == 1)));
        }

        [Test]
        public void Decide_RejectsWrongSeat()
        {
            var t = Table.New(1000, 1000, 1000).Start(0);
            var ai = new AiPlayer(Personality.Cold, 1, 1, AiKit.Fast());
            Assert.AreNotEqual(1, t.S.ToAct);
            Assert.Throws<InvalidOperationException>(() => ai.Decide(t.S, 1, ai.Observations));
        }

        static string Describe(Command c) => c switch
        {
            Bet b => "Bet" + b.Amount,
            Raise r => "Raise" + r.ToAmount,
            _ => c.GetType().Name,
        };

        [Test]
        public void SameSeed_SameDecisions()
        {
            for (ulong seed = 0; seed < 10; seed++)
            {
                var a = DecideAt(Personality.Loudmouth, seed, "1:KdQd", "Qc8d3d", t => ToFlopAllCall(t), 1);
                var b = DecideAt(Personality.Loudmouth, seed, "1:KdQd", "Qc8d3d", t => ToFlopAllCall(t), 1);
                Assert.AreEqual(Describe(a.Command), Describe(b.Command));
                Assert.AreEqual(a.RawEquity, b.RawEquity);
                Assert.AreEqual(a.ThinkSeconds, b.ThinkSeconds);
            }
        }

        [Test]
        public void ThinkTime_InRange_AndLongerForHarderDecisions()
        {
            var easy = new List<float>(); var hard = new List<float>();
            var rng = new Pcg32(5);
            for (int i = 0; i < 400; i++)
            {
                var p = AiKit.P(i);
                var d = DecideAt(p, (ulong)i, null, null, t => ToFlopAllCall(t), 1, settings: AiKit.Fast(100));
                Assert.That(d.ThinkSeconds, Is.InRange(0.8f, 3.5f));
                Assert.AreEqual(d.ThinkSeconds, d.Cue.Duration, 1e-6, "表演持續到思考結束");
                if (d.Difficulty < 0.3) easy.Add(d.ThinkSeconds); else if (d.Difficulty > 0.7) hard.Add(d.ThinkSeconds);
            }
            Assert.IsNotEmpty(easy); Assert.IsNotEmpty(hard);
            Assert.Greater(hard.Average(), easy.Average() + 0.3);
        }

        [Test]
        public void PlayerRead_ShiftIsBounded_AppliedOnlyWhenEnabled_AndDecays()
        {
            var settings = AiKit.Fast(400);
            var t = Table.New(1000, 1000, 1000, 1000).Start(0, 5, 10, "1:Jc9c", "Jd7s2h");
            var ai = new AiPlayer(Personality.Loudmouth, 1, 3, settings) { HumanSeat = 2 };
            ai.OnEvents(PlayerView.Filter(t.Events, 1));
            for (int i = 0; i < 50; i++)
                ai.OnPlayerSignals(new PlayerSignals { HesitationSeconds = 15, PeekSeconds = 9, Delivery = ChipDelivery.Thrown, ThrowSpeed = 3, Time = 0 });
            int n = t.Events.Count;
            ToFlopAllCall(t);
            ai.OnEvents(PlayerView.Filter(t.Events.Skip(n).ToList(), 1));
            ai.Observations.Now = 0;
            var d = ai.Decide(t.S, 1, ai.Observations);
            Assert.LessOrEqual(Math.Abs(d.Equity - d.RawEquity), 0.08 + 1e-9);
            Assert.Greater(d.ReadShift, 0.05);
            Assert.AreEqual(d.ReadShift, d.Equity - d.RawEquity, 1e-9);
            ai.Observations.Now = 120;
            var later = ai.Decide(t.S, 1, ai.Observations);
            Assert.Less(later.ReadShift, d.ReadShift * 0.2);
            settings.ReadSettings.Enabled = false;
            ai.Observations.Now = 0;
            Assert.AreEqual(0, ai.Decide(t.S, 1, ai.Observations).ReadShift);
        }

        [Test]
        public void PlayerRead_HumanFolded_NoShift()
        {
            var t = Table.New(1000, 1000, 1000, 1000).Start(0, 5, 10, "1:Jc9c", "Jd7s2h");
            var ai = new AiPlayer(Personality.Loudmouth, 1, 3, AiKit.Fast()) { HumanSeat = 3 };
            ai.OnPlayerSignals(new PlayerSignals { HesitationSeconds = 15, Time = 0 });
            int n = t.Events.Count;
            t.Do(new Fold(3)).Do(new Call(0)).Do(new Call(1)).Do(new Check(2));
            ai.OnEvents(PlayerView.Filter(t.Events, 1));
            Assert.AreEqual(0, ai.Decide(t.S, 1, ai.Observations).ReadShift);
        }

        [Test]
        public void PlayerSignals_ProduceReactions()
        {
            var ai = new AiPlayer(Personality.Loudmouth, 1, 1, AiKit.Fast());
            var tank = ai.OnPlayerSignals(new PlayerSignals { HesitationSeconds = 10, Time = 30 });
            Assert.AreEqual(Tell.LeanIn, tank.Tell);
            var off = new AiSettings { ReadSettings = new PlayerReadSettings { ReactionsEnabled = false } };
            Assert.IsNull(new AiPlayer(Personality.Rookie, 1, 1, off).OnPlayerSignals(new PlayerSignals { HesitationSeconds = 10 }));
        }

        [Test]
        public void HandReveal_ShowsOwnCardsAndInnerState()
        {
            // AI 座位 1 用垃圾牌在河牌被迫選擇；不論動作為何，亮牌內容要對、內心狀態要等於最後一次決策
            for (ulong seed = 0; seed < 20; seed++)
            {
                var t = Table.New(1000, 1000).Start(0, 5, 10, "0:AhAd 1:7c2d", "KsQs9h5c3d", seed);
                var ai = new AiPlayer(Personality.Cold, 1, seed, AiKit.Fast());
                var human = 0;
                ai.OnEvents(PlayerView.Filter(t.Events, 1));
                AiDecision last = null;
                while (t.S.Phase == HandPhase.Betting)
                {
                    int n = t.Events.Count;
                    if (t.S.ToAct == human)
                    {
                        var la = Engine.GetLegalActions(t.S, human);
                        t.Do(la.CanCheck ? new Check(human) : (Command)new Call(human));
                    }
                    else { last = ai.Decide(t.S, 1, ai.Observations); t.Do(last.Command); }
                    ai.OnEvents(PlayerView.Filter(t.Events.Skip(n).ToList(), 1));
                }
                var rev = ai.LastReveal;
                Assert.IsNotNull(rev);
                Assert.AreEqual("7c 2d", string.Join(" ", rev.HoleCards));
                Assert.AreEqual(last.Intent, rev.Moments.Last().Intent);
                Assert.AreEqual(HandReveal.HeadlineFor(last.Intent), rev.Headline);
                Assert.IsTrue(rev.Moments.All(m => m.Equity >= 0 && m.Equity <= 1));
            }
            Assert.AreEqual("Bluffing", HandReveal.HeadlineFor(Intent.Bluff), "2026-11-03 一行白話");
        }

        [Test]
        public void StreetReactions_AndHandEndCues_ComeFromEvents()
        {
            int reactions = 0, ends = 0;
            for (ulong seed = 0; seed < 30; seed++)
            {
                var t = Table.New(1000, 1000).Start(0, 5, 10, "0:7c2d 1:AhKh", "AsKd2h5c3d", seed);
                var ai = new AiPlayer(Personality.Rookie, 1, seed, AiKit.Fast());
                ai.OnEvents(PlayerView.Filter(t.Events, 1));
                int n = t.Events.Count;
                t.Do(new Call(0)).Do(new Check(1));
                var cues = ai.OnEvents(PlayerView.Filter(t.Events.Skip(n).ToList(), 1));
                if (cues.Any(c => c.Mood == Mood.Happy || c.Mood == Mood.Confident)) reactions++;
                n = t.Events.Count;
                t.Do(new Check(1)).Do(new Bet(0, 20)).Do(new Fold(1));
                cues = ai.OnEvents(PlayerView.Filter(t.Events.Skip(n).ToList(), 1));
                if (cues.Any(c => c.Mood == Mood.Dejected)) ends++;
            }
            Assert.Greater(reactions, 20, "翻出兩對要有反應（新手幾乎都照實演）");
            Assert.AreEqual(30, ends, "棄牌輸掉要沮喪");
        }

        [Test]
        public void Performance_DecisionUnder10ms_WithDefaultTrials()
        {
            var settings = new AiSettings(); // 預設 1000 次
            Assert.AreEqual(1000, settings.MonteCarloTrials);
            var t = Table.New(1000, 1000, 1000, 1000).Start(0, 5, 10, "1:KdQd", "Qc8d3d");
            ToFlopAllCall(t);
            var ai = new AiPlayer(Personality.Cold, 1, 1, settings);
            ai.OnEvents(PlayerView.Filter(t.Events, 1));
            for (int i = 0; i < 20; i++) ai.Decide(t.S, 1, ai.Observations); // 暖機
            var sw = Stopwatch.StartNew();
            int n = 200;
            for (int i = 0; i < n; i++) ai.Decide(t.S, 1, ai.Observations);
            double ms = sw.Elapsed.TotalMilliseconds / n;
            Console.WriteLine($"[perf] 翻牌、3 對手、1000 次 MC：平均 {ms:0.000} ms／決策");
            Assert.Less(ms, 10);
        }
    }
}
