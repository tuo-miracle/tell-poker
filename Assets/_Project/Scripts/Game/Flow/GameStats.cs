using System;
using System.Collections.Generic;
using Poker.AI;
using Poker.Core;

namespace Poker.Game
{
    /// <summary>一次「讀心」：某個 AI 下注／加注後玩家怎麼回應，對照那次決策的真實意圖。</summary>
    public sealed class ReadRecord
    {
        public int Seat;
        public int HandNumber;
        public Intent AiIntent;
        public bool PlayerFolded;
        public bool Correct;
    }

    /// <summary>一手的結果（給結算與存檔）。</summary>
    public sealed class HandResult
    {
        public int HandNumber;
        /// <summary>這一局的第幾手（1 起算）。</summary>
        public int IndexInGame;
        /// <summary>玩家這手拿到的籌碼（所有底池的份額合計）。</summary>
        public int PotWon;
        public int PotTotal;
        public bool PlayerWon;
        public int[] SeatsDealt = Array.Empty<int>();
        public readonly List<int> Winners = new List<int>();
        public readonly List<ReadRecord> Reads = new List<ReadRecord>();
    }

    /// <summary>一局結束的結算。</summary>
    public sealed class GameSummary
    {
        public int HandsPlayed, HandsWon, ReadsCorrect, ReadsTotal, BiggestPotWon, BiggestPotHand, Place, FinalStack;
        public bool Won;
        public GameEndReason Reason;

        /// <summary>桌上的標題（一行）。</summary>
        public string Title => Won ? "You win!" : "You finish " + Ordinal(Place);

        /// <summary>
        /// 桌上的兩行短結算：贏幾手、最大的一手。
        /// 2026-11-04 拿掉「Tells read」：定義是「棄掉價值注、跟虛張」，不是讀到肢體訊號，實機幾乎都是 0 of N；ReadsCorrect／ReadsTotal 照算（存檔、log 用）。
        /// </summary>
        public List<string> Lines() => new List<string>
        {
            "Hands won: " + HandsWon + " of " + HandsPlayed,
            BiggestPotWon > 0 ? "Biggest pot: " + BiggestPotWon + " (hand " + BiggestPotHand + ")" : "Biggest pot: next time!",
        };

        public static string Ordinal(int n) => n == 1 ? "1st" : n == 2 ? "2nd" : n == 3 ? "3rd" : n + "th";
    }

    /// <summary>讀心判定：價值注 → 棄牌才對；虛張／半虛張 → 跟或加才對；拿不定的注不算。</summary>
    public static class ReadJudge
    {
        public static bool? Judge(Intent aiIntent, bool playerFolded)
        {
            switch (aiIntent)
            {
                case Intent.Value: return playerFolded;
                case Intent.Bluff:
                case Intent.SemiBluff: return !playerFolded;
                default: return null;
            }
        }
    }

    /// <summary>
    /// 一局的戰績：吃 Core 的完整事件（遊戲層，不是 AI），記每手輸贏、最大底池與讀心。
    /// 讀心要等一手結束、AI 亮出 HandReveal 才能判定（revealOf 由 GameSession 提供）。
    /// </summary>
    public sealed class GameStats
    {
        readonly int _human;
        readonly Func<int, HandReveal> _revealOf;
        readonly List<HandResult> _hands = new List<HandResult>();
        readonly List<(int seat, int moment, bool folded)> _pending = new List<(int, int, bool)>();
        HandResult _hand;
        int[] _decisions = Array.Empty<int>();
        int _aggressor = -1, _aggressorMoment = -1;

        public GameStats(int humanSeat, Func<int, HandReveal> revealOf)
        {
            _human = humanSeat;
            _revealOf = revealOf;
        }

        public HandResult LastHand { get; private set; }
        public IReadOnlyList<HandResult> Hands => _hands;

        public void Reset()
        {
            _hands.Clear();
            _pending.Clear();
            _hand = null;
            LastHand = null;
        }

        public void Observe(GameEvent e)
        {
            switch (e)
            {
                case HandStarted hs:
                    _hand = new HandResult { HandNumber = hs.HandNumber, IndexInGame = _hands.Count + 1, SeatsDealt = new List<int>(hs.SeatsInHand).ToArray() };
                    _decisions = new int[hs.Stacks.Count];
                    _pending.Clear();
                    _aggressor = -1;
                    break;
                case StreetDealt _:
                    _aggressor = -1;
                    break;
                case PlayerActed a when _hand != null:
                    OnActed(a);
                    break;
                case PotAwarded pa when _hand != null:
                    _hand.PotTotal += pa.Amount;
                    foreach (var sh in pa.Shares)
                    {
                        if (sh.Amount <= 0) continue;
                        if (!_hand.Winners.Contains(sh.Seat)) _hand.Winners.Add(sh.Seat);
                        if (sh.Seat == _human) _hand.PotWon += sh.Amount;
                    }
                    break;
                case HandEnded _ when _hand != null:
                    Finish();
                    break;
            }
        }

        void OnActed(PlayerActed a)
        {
            bool aggressive = a.Action == ActionType.Bet || a.Action == ActionType.Raise;
            if (a.Seat != _human)
            {
                int moment = a.Seat < _decisions.Length ? _decisions[a.Seat]++ : 0;
                if (aggressive) { _aggressor = a.Seat; _aggressorMoment = moment; }
                return;
            }
            // 玩家回應了一個 AI 的注（同一個注只算一次）
            if (_aggressor >= 0 && a.Action != ActionType.Check)
                _pending.Add((_aggressor, _aggressorMoment, a.Action == ActionType.Fold));
            _aggressor = -1;
        }

        void Finish()
        {
            var h = _hand;
            _hand = null;
            h.PlayerWon = h.Winners.Contains(_human);
            foreach (var p in _pending)
            {
                var rev = _revealOf?.Invoke(p.seat);
                if (rev == null || rev.HandNumber != h.HandNumber || rev.Moments == null || p.moment >= rev.Moments.Count) continue;
                var intent = rev.Moments[p.moment].Intent;
                var ok = ReadJudge.Judge(intent, p.folded);
                if (ok == null) continue;
                h.Reads.Add(new ReadRecord { Seat = p.seat, HandNumber = h.HandNumber, AiIntent = intent, PlayerFolded = p.folded, Correct = ok.Value });
            }
            _pending.Clear();
            _hands.Add(h);
            LastHand = h;
        }

        public GameSummary Summarize(IReadOnlyList<int> finalStacks, GameEndReason reason)
        {
            var s = new GameSummary { Reason = reason, HandsPlayed = _hands.Count };
            foreach (var h in _hands)
            {
                if (h.PlayerWon) s.HandsWon++;
                foreach (var r in h.Reads) { s.ReadsTotal++; if (r.Correct) s.ReadsCorrect++; }
                if (h.PotWon > s.BiggestPotWon) { s.BiggestPotWon = h.PotWon; s.BiggestPotHand = h.IndexInGame; }
            }
            s.Place = MatchSchedule.PlaceOf(finalStacks, _human);
            s.FinalStack = finalStacks[_human];
            s.Won = s.Place == 1 && reason != GameEndReason.PlayerBusted;
            return s;
        }
    }
}
