using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>
    /// 規則型決策（Loki 式門檻＋個性參數）。翻牌前查 169 類百分位；翻牌後用 Monte Carlo 勝率，
    /// 以「每位對手的等效勝率」e1 = eq^(1/n) 比門檻（多人底池自然收緊）。
    /// </summary>
    internal static class Policy
    {
        // 翻牌前範圍校準係數（自我對弈校準：面對加注會讓實際 VPIP 低於開局範圍）
        internal const float OpenScale = 1.0f;
        internal const float RaiseScale = 1.0f;
        /// <summary>勝率 ≥ 此值絕不棄牌。</summary>
        internal const double NeverFoldEquity = 0.9;
        /// <summary>夠跟時改成半虛張加注的機率＝攻擊度 × 此值。</summary>
        internal const double SemiRaiseScale = 0.5;

        internal struct Result
        {
            public PlayerCommand Command;
            public Intent Intent;
            public double RawEquity, Equity;
            public float Difficulty;
        }

        public static Result Decide(BotView v, TableObservations obs, Personality p, double readShift, Pcg32 rng, Pcg32 mc, int trials)
        {
            var opps = v.LiveOpponents();
            var ranges = new float[opps.Count];
            for (int i = 0; i < opps.Count; i++) ranges[i] = obs.RangeOf(opps[i]);
            double raw = opps.Count == 0 ? 1.0 : Poker.AI.Equity.Estimate(v.Hole1, v.Hole2, v.Board, opps.Count, trials, mc, ranges);
            double eq = Clamp01(raw + readShift);
            var r = v.Street == Street.Preflop ? Preflop(v, p, eq, readShift, rng) : Postflop(v, p, eq, opps.Count, rng);
            r.RawEquity = raw; r.Equity = eq;
            r.Command = Legalize(r.Command, v);
            // 保險：堅果牌與可免費過牌時絕不棄牌
            if (r.Command is Fold && (v.Legal.CanCheck || eq >= NeverFoldEquity))
                r.Command = v.Legal.CanCheck ? (PlayerCommand)new Check(v.Seat) : v.Legal.CanCall ? new Call(v.Seat) : new AllIn(v.Seat);
            return r;
        }

        // ───────── 翻牌前 ─────────

        static Result Preflop(BotView v, Personality p, double eq, double readShift, Pcg32 rng)
        {
            var la = v.Legal;
            float pct = PreflopTable.Percentile(v.Hole1, v.Hole2);
            int raises = Math.Max(0, v.BetCount - 1);
            int toCall = la.CanCall ? la.CallAmount : 0;
            int behind = v.PlayersBehind();
            float pos = behind == 0 ? 1.25f : behind == 1 ? 1.1f : behind >= 3 ? 0.9f : 1f;
            float jitter = 1f + 0.12f * ((float)Equity.Next01(rng) - 0.5f);
            float open = p.Vpip * OpenScale * pos * jitter;
            float raise = p.Pfr * RaiseScale * pos * jitter;
            double u = Equity.Next01(rng);
            int bb = Math.Max(1, v.BigBlind);
            var res = new Result();

            if (raises == 0)
            {
                int limpers = CountLimpers(v);
                if (pct < raise) { res.Command = BetOrRaise(v, bb * (3 + limpers)); res.Intent = Intent.Value; res.Difficulty = Diff(pct, raise); }
                else if (pct < open) { res.Command = toCall > 0 ? (PlayerCommand)new Call(v.Seat) : new Check(v.Seat); res.Intent = Intent.Marginal; res.Difficulty = Diff(pct, open); }
                else if (behind <= 1 && u < p.BluffFrequency * 0.6) { res.Command = BetOrRaise(v, bb * (3 + limpers)); res.Intent = Intent.Bluff; res.Difficulty = 0.6f; }
                else { res.Command = toCall > 0 ? (PlayerCommand)new Fold(v.Seat) : new Check(v.Seat); res.Intent = Intent.GiveUp; res.Difficulty = Diff(pct, open); }
                return res;
            }

            float size = v.CurrentBet / (float)bb;
            if (raises == 1)
            {
                float price = Math.Min(1f, 4f / Math.Max(1f, size));
                float threeBet = raise * 0.3f * price;
                float call = open * 0.6f * price * (1.3f - 0.6f * p.FoldToBet) * (float)(1 + 5 * Math.Max(0, readShift));
                if (pct < threeBet) { res.Command = BetOrRaise(v, v.CurrentBet * 3); res.Intent = Intent.Value; res.Difficulty = Diff(pct, threeBet); }
                else if (pct < call) { res.Command = new Call(v.Seat); res.Intent = pct < 0.08f ? Intent.Value : Intent.Marginal; res.Difficulty = Diff(pct, call); }
                else if (u < p.BluffFrequency * 0.15) { res.Command = BetOrRaise(v, v.CurrentBet * 3); res.Intent = Intent.Bluff; res.Difficulty = 0.7f; }
                else { res.Command = new Fold(v.Seat); res.Intent = Intent.GiveUp; res.Difficulty = Diff(pct, call); }
                return res;
            }

            float prem = 0.03f * (1.6f - p.FoldToBet);
            if (pct < prem * 0.6f) { res.Command = BetOrRaise(v, (int)(v.CurrentBet * 2.5f)); res.Intent = Intent.Value; res.Difficulty = 0.3f; }
            else if (pct < prem * 1.4f) { res.Command = new Call(v.Seat); res.Intent = Intent.Value; res.Difficulty = Diff(pct, prem * 1.4f); }
            else { res.Command = new Fold(v.Seat); res.Intent = Intent.GiveUp; res.Difficulty = Diff(pct, prem * 1.4f); }
            return res;
        }

        static int CountLimpers(BotView v)
        {
            int n = 0;
            for (int i = 0; i < v.Seats.Count; i++)
                if (i != v.Seat && v.Seats[i].StreetBet == v.CurrentBet && v.Seats[i].IsLive && v.Seats[i].StreetBet > 0) n++;
            return Math.Max(0, n - 1); // 扣掉大盲本人
        }

        static float Diff(float pct, float boundary) => 1f - Math.Min(1f, Math.Abs(pct - boundary) / Math.Max(0.05f, boundary));

        // ───────── 翻牌後 ─────────

        static Result Postflop(BotView v, Personality p, double eq, int nOpp, Pcg32 rng)
        {
            var la = v.Legal;
            var res = new Result();
            double e1 = nOpp <= 1 ? eq : Math.Pow(eq, 1.0 / nOpp);
            double jitter = 0.06 * (Equity.Next01(rng) - 0.5);
            double valueThr = 0.72 - 0.2 * p.Aggression + jitter;
            double raiseThr = 0.84 - 0.1 * p.Aggression + jitter;
            double u = Equity.Next01(rng);
            int toCall = la.CanCall ? la.CallAmount : 0;
            int pot = v.Pot;

            if (toCall == 0)
            {
                int betTo = v.Legal.StreetBet + (int)Math.Round(pot * p.BetSizePot * (0.9 + 0.2 * Equity.Next01(rng)));
                if (e1 >= valueThr)
                {
                    res.Intent = Intent.Value;
                    res.Command = u < 0.4 + 0.6 * p.Aggression ? BetOrRaise(v, betTo) : new Check(v.Seat); // 不下注＝慢打
                    res.Difficulty = Closeness(e1 - valueThr, 0.2);
                }
                else if (v.Street != Street.River && e1 >= 0.42 && u < p.BluffFrequency * 1.5)
                {
                    res.Intent = Intent.SemiBluff; res.Command = BetOrRaise(v, betTo); res.Difficulty = 0.6f;
                }
                else if (e1 < 0.42 && u < p.BluffFrequency * (nOpp == 1 ? 1.2 : 0.6))
                {
                    res.Intent = Intent.Bluff; res.Command = BetOrRaise(v, betTo); res.Difficulty = 0.7f;
                }
                else
                {
                    res.Intent = e1 >= 0.45 ? Intent.Marginal : Intent.GiveUp;
                    res.Command = new Check(v.Seat);
                    res.Difficulty = Closeness(e1 - valueThr, 0.2);
                }
                return res;
            }

            double potOdds = toCall / (double)(pot + toCall);
            double required = potOdds + (p.FoldToBet - 0.4) * 0.15;
            int raiseTo = la.CurrentBet + (int)Math.Round((pot + toCall) * p.BetSizePot * (0.9 + 0.2 * Equity.Next01(rng)));
            if (e1 >= raiseThr)
            {
                res.Intent = Intent.Value;
                res.Command = u < 0.3 + 0.6 * p.Aggression ? BetOrRaise(v, raiseTo) : new Call(v.Seat);
                res.Difficulty = Closeness(e1 - raiseThr, 0.15);
            }
            else if (eq >= required)
            {
                // 夠跟：兇的個性會把部分跟注改成半虛張加注（聽牌或中等牌施壓）
                if (v.Street != Street.River && e1 < valueThr && u < p.Aggression * SemiRaiseScale)
                {
                    res.Intent = Intent.SemiBluff; res.Command = BetOrRaise(v, raiseTo); res.Difficulty = 0.7f;
                }
                else
                {
                    res.Intent = e1 >= valueThr ? Intent.Value : Intent.Marginal;
                    res.Command = new Call(v.Seat);
                    res.Difficulty = Closeness(eq - required, 0.15);
                }
            }
            else if (u < p.BluffFrequency * 0.35)
            {
                res.Intent = Intent.Bluff; res.Command = BetOrRaise(v, raiseTo); res.Difficulty = 0.8f;
            }
            else
            {
                res.Intent = Intent.GiveUp; res.Command = new Fold(v.Seat);
                res.Difficulty = Closeness(eq - required, 0.15);
            }
            return res;
        }

        static float Closeness(double margin, double scale) => (float)(1 - Math.Min(1, Math.Abs(margin) / scale));

        // ───────── 合法化 ─────────

        /// <summary>下注／加注到 <paramref name="to"/>；夾在合法範圍內，接近全下就直接全下；不能加注時回傳 null。</summary>
        static PlayerCommand BetOrRaise(BotView v, int to)
        {
            var la = v.Legal;
            if (!la.CanBet && !la.CanRaise) return null;
            to = Math.Max(la.MinTo, Math.Min(la.MaxTo, to));
            if (to >= la.MaxTo || to >= la.StreetBet + la.Stack * 0.8) return la.CanAllIn ? new AllIn(v.Seat) : null;
            return la.CanBet ? (PlayerCommand)new Bet(v.Seat, to) : new Raise(v.Seat, to);
        }

        /// <summary>確保指令在 <see cref="LegalActions"/> 內；不行就退一步（加注→跟注→過牌→棄牌）。</summary>
        internal static PlayerCommand Legalize(PlayerCommand c, BotView v)
        {
            var la = v.Legal;
            int s = v.Seat;
            switch (c)
            {
                case Bet b when la.CanBet && b.Amount >= la.MinTo && b.Amount <= la.MaxTo: return b;
                case Raise r when la.CanRaise && r.ToAmount >= la.MinTo && r.ToAmount <= la.MaxTo: return r;
                case AllIn a when la.CanAllIn: return a;
                case Call cl when la.CanCall: return cl;
                case Check ck when la.CanCheck: return ck;
                case Fold f when la.CanFold && !la.CanCheck: return f;
            }
            // 退一步：想打得兇（或指令為 null）→ 能跟就跟、能過就過
            if (la.CanCheck) return new Check(s);
            if (c is Fold && la.CanFold) return new Fold(s);
            if (la.CanCall) return new Call(s);
            if (la.CanAllIn) return new AllIn(s);
            return new Fold(s);
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
