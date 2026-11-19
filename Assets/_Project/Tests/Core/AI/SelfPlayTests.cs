using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>單一座位的自我對弈統計。</summary>
    internal sealed class SeatTally
    {
        public int Hands, Vpip, Pfr, PostAggr, PostCalls, Tells, TellsConsistent, Decisions;
        public long ChipsWon;
        public double VpipRate => Vpip / (double)Hands;
        public double PfrRate => Pfr / (double)Hands;
        public double Af => PostCalls == 0 ? double.PositiveInfinity : PostAggr / (double)PostCalls;
        public double Consistency => TellsConsistent / (double)Tells;
        public void Add(SeatTally o)
        {
            Hands += o.Hands; Vpip += o.Vpip; Pfr += o.Pfr; PostAggr += o.PostAggr; PostCalls += o.PostCalls;
            Tells += o.Tells; TellsConsistent += o.TellsConsistent; Decisions += o.Decisions; ChipsWon += o.ChipsWon;
        }
    }

    /// <summary>無畫面自我對弈：座位 0 是簡單規則玩家（代替真人，會送隨機肢體訊號），1–3 是 Pip／Rocco／Vex（或指定的 AI 陣容）。</summary>
    internal static class SelfPlay
    {
        public static readonly Archetype[] AiSeats = { Archetype.Rookie, Archetype.Loudmouth, Archetype.Cold };
        const int Big = 10, Stack = 2000;

        public static SeatTally[] Run(int hands, ulong seed, AiSettings settings, Archetype[] aiSeats = null)
        {
            aiSeats ??= AiSeats;
            int n = aiSeats.Length + 1;
            var tally = Enumerable.Range(0, n).Select(_ => new SeatTally()).ToArray();
            var ais = new AiPlayer[n];
            for (int s = 1; s < n; s++) ais[s] = new AiPlayer(Personality.For(aiSeats[s - 1]), s, seed * 31 + (ulong)s, settings) { HumanSeat = 0 };
            var live = ais.Skip(1).ToArray();
            var rng = new Pcg32(seed, 99);
            double clock = 0;
            for (int h = 0; h < hands; h++)
            {
                var state = HandState.NewTable(Enumerable.Repeat(Stack, n).ToArray());
                var r = Engine.Apply(state, new StartHand(seed * 1000003 + (ulong)h, h % n, Big / 2, Big));
                state = r.State;
                var handEvents = new List<GameEvent>(r.Events);
                AiKit.Feed(live, r.Events);
                while (state.Phase == HandPhase.Betting && state.ToAct >= 0)
                {
                    int seat = state.ToAct;
                    PlayerCommand cmd;
                    if (seat == 0)
                    {
                        cmd = RulePlayer(state, rng);
                        var sig = RandomSignals(rng, clock, n);
                        foreach (var ai in live) ai.OnPlayerSignals(sig);
                        clock += 2;
                    }
                    else
                    {
                        foreach (var ai in live) ai.Observations.Now = clock;
                        var d = ais[seat].Decide(state, seat, ais[seat].Observations);
                        cmd = d.Command;
                        tally[seat].Decisions++;
                        tally[seat].Tells++;
                        if (d.Tell.Shown == d.Tell.True) tally[seat].TellsConsistent++;
                        clock += d.ThinkSeconds;
                    }
                    var ar = Engine.Apply(state, cmd);
                    if (!ar.IsSuccess) throw new InvalidOperationException("不合法：" + ar.Error);
                    state = ar.State;
                    handEvents.AddRange(ar.Events);
                    AiKit.Feed(live, ar.Events);
                }
                Count(handEvents, tally);
                var fin = state.Stacks();
                for (int s = 0; s < n; s++) tally[s].ChipsWon += fin[s] - Stack;
                clock += 5;
            }
            return tally;
        }

        static void Count(List<GameEvent> events, SeatTally[] t)
        {
            var street = Street.Preflop;
            var vp = new bool[t.Length]; var pf = new bool[t.Length];
            foreach (var e in events)
            {
                switch (e)
                {
                    case HandStarted hs: foreach (var s in hs.SeatsInHand) t[s].Hands++; break;
                    case StreetDealt sd: street = sd.Street; break;
                    case PlayerActed pa:
                        bool aggr = pa.Action == ActionType.Bet || pa.Action == ActionType.Raise;
                        if (street == Street.Preflop)
                        {
                            if (aggr || pa.Action == ActionType.Call) vp[pa.Seat] = true;
                            if (aggr) pf[pa.Seat] = true;
                        }
                        else if (aggr) t[pa.Seat].PostAggr++;
                        else if (pa.Action == ActionType.Call) t[pa.Seat].PostCalls++;
                        break;
                }
            }
            for (int s = 0; s < t.Length; s++) { if (vp[s]) t[s].Vpip++; if (pf[s]) t[s].Pfr++; }
        }

        /// <summary>簡單規則玩家：翻前前 30% 入池；翻牌後有對子以上跟、兩對以上下注。</summary>
        internal static PlayerCommand RulePlayer(HandState s, Pcg32 rng)
        {
            var la = Engine.GetLegalActions(s, 0);
            var me = s.Seat(0);
            bool canAggro = la.CanBet || la.CanRaise;
            int to = Math.Max(la.MinTo, Math.Min(la.MaxTo, la.CurrentBet * 3 + Big));
            PlayerCommand Aggro() => !canAggro ? (la.CanCheck ? (PlayerCommand)new Check(0) : new Call(0))
                : to >= la.MaxTo ? new AllIn(0) : la.CanBet ? new Bet(0, to) : new Raise(0, to);
            PlayerCommand Passive() => la.CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
            PlayerCommand Give() => la.CanCheck ? (PlayerCommand)new Check(0) : new Fold(0);
            if (s.Street == Street.Preflop)
            {
                float p = PreflopTable.Percentile(me.Hole1, me.Hole2);
                if (p < 0.06f) return Aggro();
                if (p < (s.BetCount > 1 ? 0.15f : 0.3f)) return Passive();
                return Give();
            }
            var cat = HandEvaluator.Evaluate(new[] { me.Hole1, me.Hole2 }.Concat(s.Board).ToArray()).Category;
            if (cat >= HandCategory.TwoPair) return Aggro();
            if (cat >= HandCategory.Pair) return la.CallAmount > s.Stacks()[0] / 4 && rng.NextBounded(2) == 0 ? Give() : Passive();
            return rng.NextBounded(10) == 0 && canAggro ? Aggro() : Give();
        }

        static PlayerSignals RandomSignals(Pcg32 rng, double t, int seats) => new PlayerSignals
        {
            HesitationSeconds = 0.3f + rng.NextBounded(100) / 10f,
            PeekSeconds = rng.NextBounded(60) / 10f,
            Delivery = (ChipDelivery)rng.NextBounded(3),
            ThrowSpeed = rng.NextBounded(30) / 10f,
            GazeSeat = (int)rng.NextBounded((uint)seats),
            GazeSeconds = rng.NextBounded(30) / 10f,
            Time = t,
        };

        public static string Table(SeatTally[] t, string title, Archetype[] aiSeats = null)
        {
            aiSeats ??= AiSeats;
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine("| 座位 | 角色 | 手數 | VPIP | 目標 | PFR | 目標 | AF | 目標 | 破綻一致率 | 設定 | 決策數 | 盈虧(BB/100) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            for (int s = 0; s < t.Length; s++)
            {
                var x = t[s];
                string bb100 = (x.ChipsWon / (double)Big / x.Hands * 100).ToString("0.0");
                if (s == 0) { sb.AppendLine($"| 0 | 規則玩家 | {x.Hands} | {x.VpipRate:P1} | – | {x.PfrRate:P1} | – | {x.Af:0.00} | – | – | – | – | {bb100} |"); continue; }
                var p = Personality.For(aiSeats[s - 1]); var g = p.Targets;
                sb.AppendLine($"| {s} | {p.Name} ({p.Archetype}) | {x.Hands} | {x.VpipRate:P1} | {g.VpipMin:P0}–{g.VpipMax:P0} | {x.PfrRate:P1} | {g.PfrMin:P0}–{g.PfrMax:P0} | {x.Af:0.00} | {g.AfMin:0.0}–{g.AfMax:0.0} | {x.Consistency:P1} | {p.TellCredibility:P0} | {x.Decisions} | {bb100} |");
            }
            return sb.ToString();
        }

        /// <summary>兩比例 z 檢定。</summary>
        public static double Z(int x1, int n1, int x2, int n2)
        {
            double p1 = x1 / (double)n1, p2 = x2 / (double)n2, p = (x1 + x2) / (double)(n1 + n2);
            return (p1 - p2) / Math.Sqrt(p * (1 - p) * (1.0 / n1 + 1.0 / n2));
        }
    }

    public class SelfPlayTests
    {
        [Test]
        public void PersonalityDifferences_AreStatisticallySignificant()
        {
            var t = SelfPlay.Run(1500, 7, AiKit.Fast(100));
            Console.WriteLine(SelfPlay.Table(t, "[快速自我對弈 1500 手]"));
            var rookie = t[1]; var loud = t[2]; var cold = t[3];
            Assert.Greater(SelfPlay.Z(loud.Vpip, loud.Hands, rookie.Vpip, rookie.Hands), 4, "VPIP：Rocco > Pip");
            Assert.Greater(SelfPlay.Z(loud.Pfr, loud.Hands, rookie.Pfr, rookie.Hands), 4, "PFR：Rocco > Pip");
            Assert.Greater(SelfPlay.Z(cold.Pfr, cold.Hands, rookie.Pfr, rookie.Hands), 4, "PFR：Vex > Pip");
            Assert.Greater(SelfPlay.Z(loud.PostAggr, loud.PostAggr + loud.PostCalls, rookie.PostAggr, rookie.PostAggr + rookie.PostCalls), 3, "攻擊比例：Rocco > Pip");
            Assert.Greater(SelfPlay.Z(rookie.TellsConsistent, rookie.Tells, cold.TellsConsistent, cold.Tells), 4, "破綻一致率：Pip > Vex");
        }

        [Test, Explicit("工具：校準個性參數用，20,000 手、MC 200 次")]
        public void Tool_Calibrate()
        {
            var parts = new SeatTally[4][];
            Parallel.For(0, 4, i => parts[i] = SelfPlay.Run(5000, (ulong)(50 + i), AiKit.Fast(200)));
            var total = Enumerable.Range(0, 4).Select(_ => new SeatTally()).ToArray();
            foreach (var p in parts) for (int s = 0; s < 4; s++) total[s].Add(p[s]);
            Console.WriteLine(SelfPlay.Table(total, "[校準 20000 手]"));
        }

        /// <summary>
        /// 大樣本自我對弈：VPIP／PFR／AF 的 95% 信賴區間整段都要落在個性目標內，破綻一致率的 95% 區間要在設定值 ±0.01 內。
        /// 手數：Mono 核心測試（tools/run-core-tests.sh，約 1 分鐘）跑 100,000 手；Unity EditMode 跑 25,000 手（同樣 4 個種子、各取前 1/4）。
        /// 理由（2026-10-14 量測）：成本幾乎全在每個決策 1000 次的 Monte Carlo（即時讀人每手增加的成本量不出來：1.48 vs 1.50–1.64 ms／手），
        /// Unity 的 Mono 跑同樣 10 萬手要 120–200 s，機器忙時超過 Test Framework 預設的 180 s 逾時。
        /// 斷言用信賴區間而不是點估計，所以兩邊手數不同、說的是同一件事（樣本小時區間變寬、要求自動變嚴）；
        /// 25,000 手時最寬的區間：破綻一致率 ±0.5 pp（容許 ±1 pp）、VPIP ±0.5 pp（離目標邊界最近 2.2 pp）、AF 約 ±7%。
        /// </summary>
        [Test, Category("Slow")]
        public void SelfPlay_100kHands_StatsWithinTargets()
        {
#if UNITY_5_3_OR_NEWER
            const int perShard = 6250;
#else
            const int perShard = 25000;
#endif
            const int shards = 4;
            var sw = Stopwatch.StartNew();
            var parts = new SeatTally[shards][];
            Parallel.For(0, shards, i => parts[i] = SelfPlay.Run(perShard, (ulong)(1000 + i), new AiSettings { ReadSettings = new PlayerReadSettings() }));
            var total = Enumerable.Range(0, 4).Select(_ => new SeatTally()).ToArray();
            foreach (var p in parts) for (int s = 0; s < 4; s++) total[s].Add(p[s]);
            Console.WriteLine(SelfPlay.Table(total, $"[自我對弈 {shards * perShard} 手，MC 1000 次，{sw.Elapsed.TotalSeconds:0} 秒（{shards} 執行緒）]"));
            for (int s = 1; s < 4; s++)
            {
                var p = Personality.For(SelfPlay.AiSeats[s - 1]); var g = p.Targets; var x = total[s];
                AssertRateCi(x.Vpip, x.Hands, g.VpipMin, g.VpipMax, p.Name + " VPIP");
                AssertRateCi(x.Pfr, x.Hands, g.PfrMin, g.PfrMax, p.Name + " PFR");
                AssertRatioCi(x.PostAggr, x.PostCalls, g.AfMin, g.AfMax, p.Name + " AF");
                AssertRateCi(x.TellsConsistent, x.Tells, p.TellCredibility - 0.01, p.TellCredibility + 0.01, p.Name + " 破綻一致率");
            }
        }

        const double Z95 = 1.96;

        /// <summary>比例 x/n 的 95% 常態近似區間整段在 [min, max] 內。</summary>
        internal static void AssertRateCi(int x, int n, double min, double max, string what)
        {
            double p = x / (double)n, half = Z95 * Math.Sqrt(p * (1 - p) / n);
            Assert.That(p - half >= min && p + half <= max, Is.True, $"{what}: {p:P2} ± {half:P2} 不在 {min:P1}–{max:P1} 內（n={n}）");
        }

        /// <summary>比值 a/c（AF）的 95% 區間（log 尺度、Poisson 計數的 delta method：Var(log a/c) ≈ 1/a + 1/c）整段在 [min, max] 內。</summary>
        internal static void AssertRatioCi(int a, int c, double min, double max, string what)
        {
            Assert.That(a > 0 && c > 0, Is.True, $"{what}: 計數為 0（a={a}, c={c}）");
            double r = a / (double)c, k = Math.Exp(Z95 * Math.Sqrt(1.0 / a + 1.0 / c));
            Assert.That(r / k >= min && r * k <= max, Is.True, $"{what}: {r:0.00}（95% {r / k:0.00}–{r * k:0.00}）不在 {min:0.0}–{max:0.0} 內");
        }
    }
}
