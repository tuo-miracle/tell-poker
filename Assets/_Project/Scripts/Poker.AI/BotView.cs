using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>某座位看得到的公開資訊（不含底牌）。</summary>
    public readonly struct PublicSeat
    {
        internal PublicSeat(SeatState s) { Stack = s.Stack; StreetBet = s.StreetBet; Committed = s.Committed; Status = s.Status; }
        /// <summary>剩餘籌碼。</summary>
        public int Stack { get; }
        /// <summary>本街已下注。</summary>
        public int StreetBet { get; }
        /// <summary>本手累計投入。</summary>
        public int Committed { get; }
        /// <summary>座位狀態。</summary>
        public SeatStatus Status { get; }
        /// <summary>是否仍在爭池。</summary>
        public bool IsLive => Status == SeatStatus.Active || Status == SeatStatus.AllIn;
    }

    /// <summary>
    /// AI 的「隱藏資訊防線」：從權威 <see cref="HandState"/> 只複製公開欄位與自己的底牌。
    /// 決策程式只看這個物件，看不到別人的底牌與牌堆。
    /// </summary>
    public sealed class BotView
    {
        /// <summary>自己的座位。</summary>
        public int Seat { get; private set; }
        /// <summary>自己的底牌。</summary>
        public Card Hole1 { get; private set; }
        /// <summary>自己的第二張底牌。</summary>
        public Card Hole2 { get; private set; }
        /// <summary>公共牌。</summary>
        public IReadOnlyList<Card> Board { get; private set; }
        /// <summary>目前的街。</summary>
        public Street Street { get; private set; }
        /// <summary>按鈕座位。</summary>
        public int Button { get; private set; }
        /// <summary>大盲金額。</summary>
        public int BigBlind { get; private set; }
        /// <summary>本街跟注水位。</summary>
        public int CurrentBet { get; private set; }
        /// <summary>本街下注／加注次數（翻牌前大盲算 1）。</summary>
        public int BetCount { get; private set; }
        /// <summary>各座位公開資訊。</summary>
        public IReadOnlyList<PublicSeat> Seats { get; private set; }
        /// <summary>自己的合法行動。</summary>
        public LegalActions Legal { get; private set; }
        /// <summary>底池總額（含本街下注）。</summary>
        public int Pot { get; private set; }

        BotView() { }

        /// <summary>建立視角。<paramref name="seat"/> 必須是輪到行動的座位。</summary>
        public static BotView From(HandState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var legal = Engine.GetLegalActions(state, seat);
            if (!legal.IsTurn) throw new InvalidOperationException("不是座位 " + seat + " 的回合");
            var seats = new PublicSeat[state.SeatCount];
            int pot = 0;
            for (int i = 0; i < seats.Length; i++) { seats[i] = new PublicSeat(state.Seat(i)); pot += seats[i].Committed; }
            var me = state.Seat(seat);   // 只讀自己的底牌
            var board = new Card[state.Board.Count];
            for (int i = 0; i < board.Length; i++) board[i] = state.Board[i];
            return new BotView
            {
                Seat = seat, Hole1 = me.Hole1, Hole2 = me.Hole2, Board = board, Street = state.Street, Button = state.Button,
                BigBlind = state.BigBlind, CurrentBet = state.CurrentBet, BetCount = state.BetCount, Seats = seats, Legal = legal, Pot = pot,
            };
        }

        /// <summary>還在爭池的對手座位。</summary>
        public List<int> LiveOpponents()
        {
            var r = new List<int>();
            for (int i = 0; i < Seats.Count; i++) if (i != Seat && Seats[i].IsLive) r.Add(i);
            return r;
        }

        /// <summary>在自己之後還要行動（尚未棄牌、有籌碼）的人數，粗估位置用：0 = 最後行動。</summary>
        public int PlayersBehind()
        {
            int n = 0, count = Seats.Count;
            for (int k = 1; k < count; k++)
            {
                int i = (Seat + k) % count;
                if (i == (Button + 1) % count && Street != Street.Preflop) break;
                if (Seats[i].Status == SeatStatus.Active) n++;
                if (i == Button) break;
            }
            return n;
        }
    }
}
