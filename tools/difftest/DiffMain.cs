// 差異測試的 C# 端（不放進 Unity）：
//   gen N SEED      用 Poker.Core 產生 N 手隨機牌局（大量 all-in／短 all-in／最小加注），輸出 FORMAT.md 的 JSON Lines
//   replay FILE     重播 PokerKit 產生的牌局，比對每個動作是否合法、最終籌碼是否一致
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Poker.Core;

public static class DiffMain
{
    public static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "gen") return Gen(int.Parse(args[1]), ulong.Parse(args[2]));
        if (args.Length >= 2 && args[0] == "replay") return Replay(args[1]);
        Console.Error.WriteLine("usage: gen N SEED | replay FILE");
        return 2;
    }

    // ───────────── gen ─────────────

    static readonly int[][] Blinds = { new[] { 1, 2 }, new[] { 5, 10 }, new[] { 25, 50 }, new[] { 50, 100 }, new[] { 3, 7 }, new[] { 10, 25 } };  // 不含大小盲相等（PokerKit 的 opener 判定在此情況與 TDA 36-C 不同，見 DiffRegressionTests.Diff416）

    static int Gen(int n, ulong seed)
    {
        var rng = new Pcg32(seed, 99);
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
        int allInHands = 0, shortAllIns = 0;
        for (int id = 0; id < n; id++)
        {
            var bl = Blinds[rng.NextBounded((uint)Blinds.Length)];
            int sb = bl[0], bb = bl[1];
            int seats = 2 + (int)rng.NextBounded(8);
            var stacks = new int[seats];
            int live;
            do
            {
                live = 0;
                for (int i = 0; i < seats; i++)
                {
                    uint k = rng.NextBounded(10);
                    stacks[i] = k == 0 ? 0
                        : k <= 2 ? 1 + (int)rng.NextBounded((uint)bb)               // 不足 1 BB
                        : k <= 4 ? bb + (int)rng.NextBounded((uint)(2 * bb + 1))    // 1–3 BB
                        : 3 * bb + (int)rng.NextBounded((uint)(200 * bb));         // 深
                    if (stacks[i] > 0) live++;
                }
            } while (live < 2);
            var liveSeats = Enumerable.Range(0, seats).Where(i => stacks[i] > 0).ToList();
            int button = liveSeats[(int)rng.NextBounded((uint)liveSeats.Count)];

            var deck = Deck.Shuffled(rng.NextUInt() | ((ulong)rng.NextUInt() << 32));
            var holes = new Dictionary<int, Card[]>();
            int p = 0;
            foreach (int s in liveSeats) holes[s] = new[] { deck[p++], deck[p++] };
            var board = deck.Skip(p).Take(5).ToArray();

            var state = HandState.NewTable(stacks);
            var r = Engine.Apply(state, new StartHand((ulong)id, button, sb, bb, new CardPreset(holes, board)));
            if (!r.IsSuccess) throw new Exception("start failed: " + r.Error);
            state = r.State;
            var actions = new List<string>();
            var allEvents = new List<GameEvent>(r.Events);
            bool anyAllIn = false;
            while (state.Phase == HandPhase.Betting)
            {
                int seat = state.ToAct;
                var la = Engine.GetLegalActions(state, seat);
                var cmd = RandomCommand(rng, la, seat);
                var ar = Engine.Apply(state, cmd);
                if (!ar.IsSuccess) throw new Exception("own legal action rejected: " + ar.Error);
                state = ar.State;
                allEvents.AddRange(ar.Events);
                foreach (var e in ar.Events.OfType<PlayerActed>())
                {
                    if (e.IsAllIn) anyAllIn = true;
                    if (e.IsAllIn && (e.Action == ActionType.Bet || e.Action == ActionType.Raise) && !e.IsFullRaise) shortAllIns++;
                    actions.Add(ActionJson(e));
                }
            }
            if (anyAllIn) allInHands++;
            var sbj = new StringBuilder();
            sbj.Append("{\"id\":").Append(id)
               .Append(",\"seats\":[").Append(string.Join(",", stacks)).Append(']')
               .Append(",\"button\":").Append(r.State.Button)
               .Append(",\"sbSeat\":").Append(r.State.SmallBlindSeat)
               .Append(",\"bbSeat\":").Append(r.State.BigBlindSeat)
               .Append(",\"sb\":").Append(sb).Append(",\"bb\":").Append(bb)
               .Append(",\"hole\":{").Append(string.Join(",", holes.Select(kv => $"\"{kv.Key}\":\"{kv.Value[0]}{kv.Value[1]}\""))).Append('}')
               .Append(",\"board\":\"").Append(string.Concat(board.Select(c => c.ToString()))).Append('"')
               .Append(",\"actions\":[").Append(string.Join(",", actions)).Append(']')
               .Append(",\"final\":[").Append(string.Join(",", state.Stacks())).Append(']')
               .Append(",\"final_pk_oddchip\":[").Append(string.Join(",", PkStyleFinal(state.Stacks(), allEvents, holes, board, r.State.Button))).Append("]}");
            stdout.WriteLine(sbj.ToString());
        }
        stdout.Flush();
        Console.Error.WriteLine($"gen: hands={n} withAllIn={allInHands} shortAllInRaises={shortAllIns}");
        return 0;
    }

    static string ActionJson(PlayerActed e)
    {
        string type;
        switch (e.Action)
        {
            case ActionType.Fold: return $"{{\"seat\":{e.Seat},\"type\":\"fold\"}}";
            case ActionType.Check: return $"{{\"seat\":{e.Seat},\"type\":\"check\"}}";
            case ActionType.Call: return $"{{\"seat\":{e.Seat},\"type\":\"call\"}}";
            case ActionType.Bet: type = e.IsAllIn ? "allin" : "bet"; break;
            default: type = e.IsAllIn ? "allin" : "raise"; break;
        }
        return $"{{\"seat\":{e.Seat},\"type\":\"{type}\",\"to\":{e.StreetBet}}}";
    }

    static PlayerCommand RandomCommand(Pcg32 rng, LegalActions la, int seat)
    {
        bool canSize = la.CanBet || la.CanRaise;
        while (true)
        {
            uint roll = rng.NextBounded(100);
            if (roll < 12) { if (la.CanCall) return new Fold(seat); continue; }
            if (roll < 22) { if (la.CanCheck) return new Check(seat); continue; }
            if (roll < 47) { if (la.CanCall) return new Call(seat); if (la.CanCheck) return new Check(seat); continue; }
            if (roll < 67) { if (la.CanAllIn) return new AllIn(seat); continue; }
            if (!canSize) continue;
            int to;
            if (roll < 82) to = la.MinTo;
            else if (roll < 87) to = Math.Min(la.MinTo + 1, la.MaxTo);
            else if (roll < 92) to = Math.Max(la.MinTo, la.MaxTo - 1);                 // 差 1 枚就 all-in
            else to = la.MinTo + (int)rng.NextBounded((uint)(la.MaxTo - la.MinTo + 1));
            return la.CanBet ? (PlayerCommand)new Bet(seat, to) : new Raise(seat, to);
        }
    }

    // ───────────── replay ─────────────

    static int Replay(string path)
    {
        int total = 0, unexplained = 0;
        var classes = new SortedDictionary<string, int>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            total++;
            var rec = (Dictionary<string, object>)Json.Parse(line);
            var tags = new SortedSet<string>();
            string err = ReplayOne(rec, tags, out int[] final, out int[] alt);
            var expected = ((List<object>)rec["final"]).Select(o => Convert.ToInt32(o)).ToArray();
            if (err == null && !final.SequenceEqual(expected))
            {
                if (alt.SequenceEqual(expected)) tags.Add("oddchip_policy");
                else err = "final mismatch";
            }
            string cls = err != null && !err.StartsWith("KNOWN:") ? "UNEXPLAINED"
                : err != null ? err.Substring(6)
                : tags.Count == 0 ? "identical" : string.Join(",", tags);
            classes[cls] = classes.TryGetValue(cls, out int c) ? c + 1 : 1;
            if (cls == "UNEXPLAINED") unexplained++;
            if (cls != "identical")
                Console.WriteLine($"{{\"id\":{rec["id"]},\"class\":\"{cls}\",\"cs_final\":[{(final == null ? "" : string.Join(",", final))}],\"error\":\"{(err ?? "").Replace("\"", "'")}\"}}");
        }
        Console.Error.WriteLine($"cs_replay: total={total} unexplained={unexplained} classes: " + string.Join(", ", classes.Select(kv => kv.Key + "=" + kv.Value)));
        return unexplained == 0 ? 0 : 1;
    }

    static string ReplayOne(Dictionary<string, object> rec, SortedSet<string> tags, out int[] final, out int[] alt)
    {
        final = null; alt = null;
        var seats = ((List<object>)rec["seats"]).Select(o => Convert.ToInt32(o)).ToArray();
        int button = Convert.ToInt32(rec["button"]);
        int sb = Convert.ToInt32(rec["sb"]), bb = Convert.ToInt32(rec["bb"]);
        var holes = new Dictionary<int, Card[]>();
        foreach (var kv in (Dictionary<string, object>)rec["hole"]) holes[int.Parse(kv.Key)] = Card.ParseMany((string)kv.Value);
        var board = Card.ParseMany((string)rec["board"]);
        var state = HandState.NewTable(seats);
        var r = Engine.Apply(state, new StartHand(0, button, sb, bb, new CardPreset(holes, board)));
        if (!r.IsSuccess) return "start: " + r.Error;
        var events = new List<GameEvent>(r.Events);
        state = r.State;
        if (state.SmallBlindSeat != Convert.ToInt32(rec["sbSeat"]) || state.BigBlindSeat != Convert.ToInt32(rec["bbSeat"]))
            return $"blind seats differ: cs sb={state.SmallBlindSeat} bb={state.BigBlindSeat}";
        var actions = (List<object>)rec["actions"];
        for (int i = 0; i < actions.Count; i++)
        {
            var a = (Dictionary<string, object>)actions[i];
            int seat = Convert.ToInt32(a["seat"]);
            string type = (string)a["type"];
            if (state.Phase != HandPhase.Betting)
            {
                // PokerKit 會要求「沒有要跟、也沒人能回應」的玩家再 check 一次；本引擎直接亮牌（TDA 17）。
                bool allChecks = actions.Skip(i).All(o => (string)((Dictionary<string, object>)o)["type"] == "check");
                if (!allChecks) return $"action {i}: hand already over";
                tags.Add("pk_pointless_check");
                break;
            }
            if (state.ToAct != seat) return $"action {i}: cs expects seat {state.ToAct} to act, record has {seat}";
            var la = Engine.GetLegalActions(state, seat);
            PlayerCommand cmd;
            switch (type)
            {
                case "fold": cmd = new Fold(seat); break;
                case "check": cmd = new Check(seat); break;
                case "call": cmd = new Call(seat); break;
                default:
                    int to = Convert.ToInt32(a["to"]);
                    if (to <= state.CurrentBet) cmd = new Call(seat);          // all-in 跟注
                    else if (to == la.AllInTo) cmd = new AllIn(seat);
                    else if (state.CurrentBet == 0) cmd = new Bet(seat, to);
                    else cmd = new Raise(seat, to);
                    break;
            }
            var ar = Engine.Apply(state, cmd);
            if (!ar.IsSuccess)
            {
                var x = state.Seat(seat);
                bool closedBy49A = (cmd is Raise || cmd is AllIn || cmd is Bet) && x.HasActed && state.CurrentBet - x.ActedLevel < state.LastFullRaise;
                if (closedBy49A) return "KNOWN:pk_reopens_after_short_allin";
                return $"action {i} ({type} {(a.ContainsKey("to") ? a["to"] : "")}) rejected: {ar.Error}";
            }
            state = ar.State;
            events.AddRange(ar.Events);
        }
        if (state.Phase != HandPhase.Idle) return "actions exhausted but cs hand not over (to act: " + state.ToAct + ")";
        final = state.Stacks();
        alt = PkStyleFinal(final, events, holes, board, button);
        return null;
    }

    /// <summary>
    /// 以 PokerKit 的零頭政策重算最終籌碼（只用來「分類」差異，不是本引擎的規則）：
    /// 殺掉不可能贏任何池的手後，合併「剩餘玩家集合相同」的相鄰池，每池的零頭全部給按鈕左邊第一位贏家。
    /// </summary>
    static int[] PkStyleFinal(int[] final, List<GameEvent> events, Dictionary<int, Card[]> holes, Card[] board, int button)
    {
        var alt = (int[])final.Clone();
        if (!events.OfType<ShowdownRevealed>().Any()) return alt;
        var pots = events.OfType<PotsUpdated>().Last().Pots;
        var awards = events.OfType<PotAwarded>().ToList();
        var alive = new HashSet<int>(awards.SelectMany(aw => aw.Shares.Select(x => x.Seat)));
        foreach (var aw in awards) foreach (var sh in aw.Shares) alt[sh.Seat] -= sh.Amount;
        ulong boardMask = 0;
        foreach (var c in board) boardMask |= c.Mask;
        var groups = new List<KeyValuePair<List<int>, int>>();
        foreach (var pot in pots)
        {
            var set = pot.EligibleSeats.Where(alive.Contains).OrderBy(v => v).ToList();
            if (groups.Count > 0 && groups[groups.Count - 1].Key.SequenceEqual(set))
                groups[groups.Count - 1] = new KeyValuePair<List<int>, int>(set, groups[groups.Count - 1].Value + pot.Amount);
            else groups.Add(new KeyValuePair<List<int>, int>(set, pot.Amount));
        }
        int n = final.Length;
        foreach (var g in groups)
        {
            var vals = g.Key.ToDictionary(seat => seat, seat => HandEvaluator.EvaluateMask(boardMask | holes[seat][0].Mask | holes[seat][1].Mask).Value);
            int best = vals.Values.Max();
            var winners = Enumerable.Range(1, n).Select(k => (button + k) % n).Where(seat => vals.ContainsKey(seat) && vals[seat] == best).ToList();
            int share = g.Value / winners.Count, rem = g.Value % winners.Count;
            foreach (var w in winners) alt[w] += share;
            alt[winners[0]] += rem;
        }
        return alt;
    }
}

/// <summary>極簡 JSON 解析（只處理本工具需要的子集）。</summary>
static class Json
{
    public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

    static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object Value(string s, ref int i)
    {
        Ws(s, ref i);
        char c = s[i];
        if (c == '{')
        {
            var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i); var k = (string)Value(s, ref i); Ws(s, ref i); i++; // ':'
                d[k] = Value(s, ref i); Ws(s, ref i);
                if (s[i++] == '}') return d;
            }
        }
        if (c == '[')
        {
            var l = new List<object>(); i++; Ws(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i)); Ws(s, ref i);
                if (s[i++] == ']') return l;
            }
        }
        if (c == '"')
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"') { if (s[i] == '\\') i++; sb.Append(s[i++]); }
            i++; return sb.ToString();
        }
        if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
        if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
        if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
        int st = i;
        while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0) i++;
        return long.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }
}
