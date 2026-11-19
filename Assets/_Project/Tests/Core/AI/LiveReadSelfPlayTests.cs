using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>自我對弈裡「真人」怎麼演：不演（中性）、誠實（弱就猶豫、強就秒下）、演假破綻（強就裝猶豫、弱就秒下）。</summary>
    internal enum PlayerStyle { Neutral, Honest, Faker }

    /// <summary>讀人的設定：關掉、開（含攤牌後防騙）、開但不防騙（不學）。</summary>
    internal enum ReadMode { Off, Live, LiveNoLearning }

    internal sealed class LiveTally
    {
        public int Hands;
        public long PlayerChips;
        /// <summary>AI 面對玩家的下注／加注時的決策數與棄牌數；[0]＝玩家演得弱（猶豫等），[1]＝演得強（秒下）。</summary>
        public readonly int[] FacingBet = new int[2], FoldsFacingBet = new int[2];
        /// <summary>同上，只算後半段的手（看 AI 學到之後）。</summary>
        public readonly int[] LateFacingBet = new int[2], LateFolds = new int[2];
        public int Fooled, Right;
        public double SumAbsShift; public int Shifted, Decisions;
        public void Add(LiveTally o)
        {
            Hands += o.Hands; PlayerChips += o.PlayerChips; Fooled += o.Fooled; Right += o.Right;
            SumAbsShift += o.SumAbsShift; Shifted += o.Shifted; Decisions += o.Decisions;
            for (int i = 0; i < 2; i++)
            {
                FacingBet[i] += o.FacingBet[i]; FoldsFacingBet[i] += o.FoldsFacingBet[i];
                LateFacingBet[i] += o.LateFacingBet[i]; LateFolds[i] += o.LateFolds[i];
            }
        }
        public double PlayerBb100(int bb) => PlayerChips / (double)bb / Math.Max(1, Hands) * 100;
        public double FoldRate(int i) => FoldsFacingBet[i] / (double)Math.Max(1, FacingBet[i]);
        public double LateFoldRate(int i) => LateFolds[i] / (double)Math.Max(1, LateFacingBet[i]);
    }

    /// <summary>
    /// 自我對弈（同 SelfPlay：座位 0 是規則玩家，1–3 是 Pip／Rocco／Vex），玩家每次行動前依自己的牌力與「演法」送出即時訊號。
    /// 每個設定用同一組牌（同樣的種子），差別只來自讀人改變了 AI 的決策（配對比較，雜訊小很多）。
    /// </summary>
    internal static class LiveSelfPlay
    {
        public const int Big = 10, Stack = 2000;

        static PlayerSignals Weak() => new PlayerSignals { Live = true, ThinkSeconds = 9f, HesitationSeconds = 9f, Looks = 2, PullBacks = 1 };
        static PlayerSignals Strong() => new PlayerSignals { Live = true, ThinkSeconds = 1f, HesitationSeconds = 1f, Delivery = ChipDelivery.Placed };
        static PlayerSignals Plain() => new PlayerSignals { Live = true, ThinkSeconds = 4f, HesitationSeconds = 4f };

        /// <summary>規則玩家自己覺得牌強嗎（翻前前 10%、翻後兩對以上）。</summary>
        static bool PlayerStrong(HandState s)
        {
            var me = s.Seat(0);
            if (s.Street == Street.Preflop) return PreflopTable.Percentile(me.Hole1, me.Hole2) < 0.10f;
            return HandEvaluator.Evaluate(new[] { me.Hole1, me.Hole2 }.Concat(s.Board).ToArray()).Category >= HandCategory.TwoPair;
        }

        public static LiveTally Run(int hands, ulong seed, PlayerStyle style, ReadMode mode, int trials = 100, Action<PlayerReadSettings> tweak = null)
        {
            var t = new LiveTally();
            var rs = new PlayerReadSettings { Enabled = mode != ReadMode.Off, LearnFromShowdown = mode == ReadMode.Live };
            tweak?.Invoke(rs);
            var settings = new AiSettings { MonteCarloTrials = trials, ReactionTrials = 60, ReadSettings = rs };
            var ais = new AiPlayer[4];
            for (int s = 1; s < 4; s++) ais[s] = new AiPlayer(Personality.For(SelfPlay.AiSeats[s - 1]), s, seed * 31 + (ulong)s, settings) { HumanSeat = 0 };
            var live = ais.Skip(1).ToArray();
            var rng = new Pcg32(seed, 99);
            double clock = 0;
            for (int h = 0; h < hands; h++)
            {
                var state = HandState.NewTable(new[] { Stack, Stack, Stack, Stack });
                var r = Engine.Apply(state, new StartHand(seed * 1000003 + (ulong)h, h % 4, Big / 2, Big));
                state = r.State;
                AiKit.Feed(live, r.Events);
                int shownAs = -1;   // 玩家最近一次演的：0 弱、1 強
                bool playerAggressor = false;
                while (state.Phase == HandPhase.Betting && state.ToAct >= 0)
                {
                    int seat = state.ToAct;
                    PlayerCommand cmd;
                    if (seat == 0)
                    {
                        cmd = SelfPlay.RulePlayer(state, rng);
                        bool strong = PlayerStrong(state);
                        PlayerSignals sig;
                        switch (style)
                        {
                            case PlayerStyle.Honest: sig = strong ? Strong() : Weak(); shownAs = strong ? 1 : 0; break;
                            case PlayerStyle.Faker: sig = strong ? Weak() : Strong(); shownAs = strong ? 0 : 1; break;
                            default: sig = Plain(); shownAs = -1; break;
                        }
                        sig.Aggressive = cmd is Bet || cmd is Raise || cmd is AllIn;
                        sig.Folded = cmd is Fold;
                        sig.Time = clock;
                        playerAggressor = sig.Aggressive;
                        foreach (var ai in live) ai.OnPlayerSignals(sig);
                        clock += 2;
                    }
                    else
                    {
                        foreach (var ai in live) ai.Observations.Now = clock;
                        var d = ais[seat].Decide(state, seat, ais[seat].Observations);
                        cmd = d.Command;
                        t.Decisions++;
                        if (d.ReadShift != 0) { t.Shifted++; t.SumAbsShift += Math.Abs(d.ReadShift); }
                        var la = Engine.GetLegalActions(state, seat);
                        if (playerAggressor && la.CanCall && state.LastAggressor == 0 && shownAs >= 0)
                        {
                            t.FacingBet[shownAs]++;
                            if (cmd is Fold) t.FoldsFacingBet[shownAs]++;
                            if (h >= hands / 2) { t.LateFacingBet[shownAs]++; if (cmd is Fold) t.LateFolds[shownAs]++; }
                        }
                        clock += d.ThinkSeconds;
                    }
                    var ar = Engine.Apply(state, cmd);
                    if (!ar.IsSuccess) throw new InvalidOperationException("不合法：" + ar.Error);
                    state = ar.State;
                    AiKit.Feed(live, ar.Events);
                }
                foreach (var ai in live)
                {
                    if (ai.LastReadOutcome == ReadOutcome.Fooled) t.Fooled++;
                    else if (ai.LastReadOutcome == ReadOutcome.Right) t.Right++;
                }
                t.Hands++;
                t.PlayerChips += state.Stacks()[0] - Stack;
                clock += 5;
            }
            return t;
        }

        public static LiveTally RunParallel(int hands, ulong seed, PlayerStyle style, ReadMode mode, int shards = 4, int trials = 100, Action<PlayerReadSettings> tweak = null)
        {
            var parts = new LiveTally[shards];
            Parallel.For(0, shards, i => parts[i] = Run(hands / shards, seed + (ulong)i * 7919, style, mode, trials, tweak));
            var total = new LiveTally();
            foreach (var p in parts) total.Add(p);
            return total;
        }

        public static string Row(string name, LiveTally t) =>
            $"| {name} | {t.Hands} | {t.PlayerBb100(Big):+0.0;-0.0} | {t.FoldRate(0):P0} ({t.FacingBet[0]}) | {t.FoldRate(1):P0} ({t.FacingBet[1]}) | {t.Fooled} | {t.Right} | {(t.Shifted == 0 ? 0 : t.SumAbsShift / t.Shifted):0.000} |";

        public const string Header = "| 玩家／讀人 | 手數 | 玩家 BB/100 | AI 面對下注棄牌率：演弱之後 | 演強之後 | 被騙 | 讀對 | 平均偏移 |\n|---|---|---|---|---|---|---|---|";
    }

    /// <summary>
    /// 讀人的效果要「有感但不能被輕易利用」（2026-10-14 live talk）。看機制（AI 面對下注的棄牌率），不看 BB/100（雜訊太大，見 Tool_Evidence）：
    /// - 誠實的玩家（弱就猶豫、強就秒下）被讀到：AI 面對他猶豫之後的下注明顯更常跟、面對秒下的下注不會更常跟。
    /// - 演假破綻的玩家（強的時候裝猶豫、弱的時候秒下）：沒有防騙時 AI 一直上當；有防騙時，後半段的棄牌率回到讀人關掉的水準。
    /// </summary>
    public class LiveReadSelfPlayTests
    {
        const int Hands = 12000;

        [Test, Category("Slow")]
        public void Reads_Matter_ForHonestPlayers_AndFakersCannotFarmThem()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var off = LiveSelfPlay.RunParallel(Hands, 101, PlayerStyle.Honest, ReadMode.Off, shards: 8);
            var honest = LiveSelfPlay.RunParallel(Hands, 101, PlayerStyle.Honest, ReadMode.LiveNoLearning, shards: 8);
            var fakeOff = LiveSelfPlay.RunParallel(Hands, 101, PlayerStyle.Faker, ReadMode.Off, shards: 8);
            var fakeNoLearn = LiveSelfPlay.RunParallel(Hands, 101, PlayerStyle.Faker, ReadMode.LiveNoLearning, shards: 8);
            var fake = LiveSelfPlay.RunParallel(Hands, 101, PlayerStyle.Faker, ReadMode.Live, shards: 8);
            var neutral = LiveSelfPlay.RunParallel(4000, 101, PlayerStyle.Neutral, ReadMode.Live);
            var sb = new StringBuilder();
            sb.AppendLine($"[Read] self-play {Hands} hands per row, same deals per row ({sw.Elapsed.TotalSeconds:0} s)");
            sb.AppendLine(LiveSelfPlay.Header);
            sb.AppendLine(LiveSelfPlay.Row("honest / off", off));
            sb.AppendLine(LiveSelfPlay.Row("honest / live, no learning", honest));
            sb.AppendLine(LiveSelfPlay.Row("faker / off", fakeOff));
            sb.AppendLine(LiveSelfPlay.Row("faker / live, no learning", fakeNoLearn));
            sb.AppendLine(LiveSelfPlay.Row("faker / live + anti-exploit", fake));
            sb.AppendLine($"late half fold vs hesitant bets: faker off {fakeOff.LateFoldRate(0):P1}, no learning {fakeNoLearn.LateFoldRate(0):P1}, anti-exploit {fake.LateFoldRate(0):P1}");
            sb.AppendLine($"late half fold vs snap bets: faker off {fakeOff.LateFoldRate(1):P1}, no learning {fakeNoLearn.LateFoldRate(1):P1}, anti-exploit {fake.LateFoldRate(1):P1}");
            Console.WriteLine(sb.ToString());

            // 有感：猶豫之後的下注，AI 少棄很多（讀成弱）；秒下的下注不會少棄
            Assert.Less(honest.FoldRate(0), off.FoldRate(0) - 0.05, "AI calls hesitant bets more");
            Assert.GreaterOrEqual(honest.FoldRate(1), off.FoldRate(1) - 0.01, "and respects snap bets");
            // 沒防騙：一直上當
            Assert.Less(fakeNoLearn.LateFoldRate(0), fakeOff.LateFoldRate(0) - 0.05, "without learning the slow-reach trick keeps working");
            Assert.Greater(fakeNoLearn.LateFoldRate(1), fakeOff.LateFoldRate(1) + 0.03, "and snap-bluffs keep working");
            // 防騙：後半段回到讀人關掉的水準
            Assert.Greater(fake.LateFoldRate(0), fakeNoLearn.LateFoldRate(0) + 0.05, "learned to distrust the slow reach");
            Assert.Greater(fake.LateFoldRate(0), fakeOff.LateFoldRate(0) - 0.04);
            Assert.Less(fake.LateFoldRate(1), fakeOff.LateFoldRate(1) + 0.04, "and to distrust snap bets from a known actor");
            Assert.Greater(fake.Fooled, 0);
            Assert.AreEqual(0, neutral.Shifted, "no tells, no reads");
        }

        /// <summary>每個設定跑 seeds 組種子（同一組種子各設定用同樣的牌），回傳每組相對於「讀人關掉」的 BB/100 差。</summary>
        static (double mean, double se, LiveTally total) Diff(PlayerStyle style, ReadMode mode, ulong[] seeds, int hands, Action<PlayerReadSettings> tweak = null)
        {
            var d = new List<double>();
            var total = new LiveTally();
            foreach (var seed in seeds)
            {
                var off = LiveSelfPlay.RunParallel(hands, seed, style, ReadMode.Off, shards: 8);
                var on = LiveSelfPlay.RunParallel(hands, seed, style, mode, shards: 8, tweak: tweak);
                d.Add(on.PlayerBb100(LiveSelfPlay.Big) - off.PlayerBb100(LiveSelfPlay.Big));
                total.Add(on);
            }
            double m = d.Average();
            double sd = Math.Sqrt(d.Sum(x => (x - m) * (x - m)) / Math.Max(1, d.Count - 1));
            return (m, sd / Math.Sqrt(d.Count), total);
        }

        [Test, Explicit("工具：讀人效果的證據（每格 6 組種子 × 20,000 手，約 4 分鐘）")]
        public void Tool_Evidence()
        {
            var seeds = new ulong[] { 202, 303, 404, 505, 606, 707 };
            const int n = 20000;
            var sb = new StringBuilder();
            sb.AppendLine($"[Read] evidence: {seeds.Length} seeds x {n} hands per cell; player BB/100 minus the same deals with reads off (mean ± s.e.)");
            sb.AppendLine("| player | reads | player BB/100 vs off | AI fold vs hesitant bets (late half) | AI fold vs snap bets (late half) | fooled | right |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var style in new[] { PlayerStyle.Honest, PlayerStyle.Faker })
            {
                var offT = new LiveTally();
                foreach (var seed in seeds) offT.Add(LiveSelfPlay.RunParallel(n, seed, style, ReadMode.Off, shards: 8));
                sb.AppendLine($"| {style} | off | 0 (abs {offT.PlayerBb100(10):0.0}) | {offT.LateFoldRate(0):P0} | {offT.LateFoldRate(1):P0} | - | - |");
                foreach (var mode in new[] { ReadMode.LiveNoLearning, ReadMode.Live })
                {
                    var (m, se, t) = Diff(style, mode, seeds, n);
                    sb.AppendLine($"| {style} | {mode} | {m:+0.0;-0.0} ± {se:0.0} | {t.LateFoldRate(0):P0} | {t.LateFoldRate(1):P0} | {t.Fooled} | {t.Right} |");
                }
            }
            Console.WriteLine(sb.ToString());
        }
    }
}
