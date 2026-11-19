using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    /// <summary>規則測試用的小工具：包住 HandState，逐步送指令並累積事件。</summary>
    internal sealed class Table
    {
        public HandState S;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public IReadOnlyList<GameEvent> Last = new GameEvent[0];

        public static Table New(params int[] stacks) => new Table { S = HandState.NewTable(stacks) };

        /// <param name="holes">例如 "0:AsKs 1:7c7d"</param>
        public Table Start(int? button, int sb = 50, int bb = 100, string holes = null, string board = null, ulong seed = 1)
        {
            CardPreset preset = null;
            if (holes != null || board != null)
            {
                var d = new Dictionary<int, string>();
                if (holes != null)
                    foreach (var part in holes.Split(' '))
                    {
                        var kv = part.Split(':');
                        d[int.Parse(kv[0])] = kv[1];
                    }
                preset = CardPreset.Parse(d, board ?? "");
            }
            return Do(new StartHand(seed, button, sb, bb, preset));
        }

        public Table Do(Command c)
        {
            var r = Engine.Apply(S, c);
            Assert.IsTrue(r.IsSuccess, "指令被拒絕：" + r.Error);
            S = r.State;
            Events.AddRange(r.Events);
            Last = r.Events;
            return this;
        }

        /// <summary>送出應被拒絕的指令，並確認狀態完全不變。</summary>
        public EngineError Reject(Command c)
        {
            var before = S;
            string snap = Snapshot(S);
            var r = Engine.Apply(S, c);
            Assert.IsFalse(r.IsSuccess, "應被拒絕但成功了：" + c.GetType().Name);
            Assert.AreSame(before, r.State);
            Assert.AreEqual(snap, Snapshot(S));
            Assert.AreEqual(0, r.Events.Count);
            Assert.IsNotNull(r.Error);
            return r.Error;
        }

        public Table Fold(int s) => Do(new Fold(s));
        public Table Check(int s) => Do(new Check(s));
        public Table Call(int s) => Do(new Call(s));
        public Table Bet(int s, int amount) => Do(new Bet(s, amount));
        public Table Raise(int s, int to) => Do(new Raise(s, to));
        public Table AllIn(int s) => Do(new AllIn(s));

        /// <summary>目前街上所有人都過牌直到下一街或攤牌（依 ToAct 順序）。</summary>
        public Table CheckAround()
        {
            var street = S.Street;
            int guard = 0;
            while (S.Phase == HandPhase.Betting && S.Street == street && guard++ < 50) Check(S.ToAct);
            return this;
        }

        /// <summary>剩下的每一街都過牌到底。</summary>
        public Table CheckDown()
        {
            int guard = 0;
            while (S.Phase == HandPhase.Betting && guard++ < 200) Check(S.ToAct);
            return this;
        }

        public LegalActions Legal(int s) => Engine.GetLegalActions(S, s);
        public IEnumerable<T> Of<T>() where T : GameEvent => Events.OfType<T>();
        public int[] Stacks => S.Stacks();

        public static string Snapshot(HandState s)
        {
            var sb = new StringBuilder();
            sb.Append($"{s.Phase}|{s.Street}|{s.HandNumber}|{s.Button}|{s.SmallBlindSeat}|{s.BigBlindSeat}|{s.ToAct}|{s.CurrentBet}|{s.LastFullRaise}|{s.LastAggressor}|{s.BetCount}|");
            sb.Append(string.Join("", s.Board.Select(c => c.ToString()))).Append('|');
            for (int i = 0; i < s.SeatCount; i++)
            {
                var x = s.Seat(i);
                sb.Append($"[{x.Stack},{x.StreetBet},{x.Committed},{x.Status},{x.HasActed},{x.Hole1}{x.Hole2}]");
            }
            return sb.ToString();
        }

        /// <summary>把事件的所有公開屬性轉成字串（重播比對用）。</summary>
        public static string Describe(object e)
        {
            if (e == null) return "null";
            if (e is string str) return str;
            if (e is IEnumerable en)
                return "[" + string.Join(",", en.Cast<object>().Select(Describe)) + "]";
            var t = e.GetType();
            if (t.IsPrimitive || t.IsEnum || e is Card || e is HandValue) return e.ToString();
            return t.Name + "{" + string.Join(";", t.GetProperties().OrderBy(p => p.Name).Select(p => p.Name + "=" + Describe(p.GetValue(e)))) + "}";
        }
    }
}
