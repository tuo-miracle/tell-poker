using System;
using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>牌局階段。</summary>
    public enum HandPhase
    {
        /// <summary>尚未開始任何一手，或上一手已結束：可以送 <see cref="StartHand"/>。</summary>
        Idle,
        /// <summary>下注中：等待 <see cref="HandState.ToAct"/> 行動。</summary>
        Betting,
    }

    /// <summary>座位狀態。</summary>
    public enum SeatStatus
    {
        /// <summary>沒有參與本手（空位或籌碼為 0）。</summary>
        Out,
        /// <summary>仍在局且還有籌碼可行動。</summary>
        Active,
        /// <summary>已棄牌。</summary>
        Folded,
        /// <summary>已 all-in。</summary>
        AllIn,
    }

    /// <summary>單一座位在某時刻的快照（不可變；外部唯讀）。</summary>
    public struct SeatState
    {
        /// <summary>剩餘籌碼（不含已下注）。</summary>
        public int Stack { get; internal set; }
        /// <summary>本街已下注。</summary>
        public int StreetBet { get; internal set; }
        /// <summary>本手累計投入（含各街）。</summary>
        public int Committed { get; internal set; }
        /// <summary>座位狀態。</summary>
        public SeatStatus Status { get; internal set; }
        /// <summary>本街是否已主動行動過（盲注不算）。</summary>
        public bool HasActed { get; internal set; }
        /// <summary>第一張底牌（未參與時為 default）。</summary>
        public Card Hole1 { get; internal set; }
        /// <summary>第二張底牌。</summary>
        public Card Hole2 { get; internal set; }

        /// <summary>上次行動後所面對的本街下注水位（判斷短 all-in 是否重開下注用）。</summary>
        internal int ActedLevel { get; set; }

        /// <summary>是否仍有機會贏池（Active 或 AllIn）。</summary>
        public bool IsLive => Status == SeatStatus.Active || Status == SeatStatus.AllIn;
    }

    /// <summary>
    /// 牌桌與當前牌局的完整狀態。<b>不可變</b>：所有屬性對引擎外部唯讀，只有 <see cref="Engine"/> 會產生新的 <see cref="HandState"/>。
    /// 注意：此物件含牌序與所有底牌，屬於伺服器端權威資料；送給玩家請用事件＋<see cref="PlayerView"/>。
    /// </summary>
    public sealed class HandState
    {
        internal SeatState[] SeatArray;
        internal Card[] DeckCards = Array.Empty<Card>();
        internal int DeckPosition;
        internal List<Card> BoardList = new List<Card>();

        private HandState() { }

        /// <summary>最多座位數。</summary>
        public const int MaxSeats = 10;

        /// <summary>建立新牌桌。<paramref name="stacks"/> 為各座位籌碼（0 表示空位），座位依順時針編號。</summary>
        public static HandState NewTable(IReadOnlyList<int> stacks)
        {
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));
            if (stacks.Count < 2 || stacks.Count > MaxSeats) throw new ArgumentException("座位數必須介於 2 到 " + MaxSeats, nameof(stacks));
            var s = new HandState { SeatArray = new SeatState[stacks.Count], Phase = HandPhase.Idle, ToAct = -1, Button = -1, SmallBlindSeat = -1, BigBlindSeat = -1, LastAggressor = -1 };
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i] < 0) throw new ArgumentException("籌碼不可為負", nameof(stacks));
                s.SeatArray[i].Stack = stacks[i];
            }
            long total = 0;
            foreach (var v in stacks) total += v;
            if (total > int.MaxValue) throw new ArgumentException("總籌碼超過 int 上限（" + int.MaxValue + "）", nameof(stacks));
            return s;
        }

        internal HandState Clone()
        {
            var c = (HandState)MemberwiseClone();
            c.SeatArray = (SeatState[])SeatArray.Clone();
            c.BoardList = new List<Card>(BoardList);
            return c; // DeckCards 一旦建立就不再修改，可共用
        }

        /// <summary>座位數。</summary>
        public int SeatCount => SeatArray.Length;
        /// <summary>座位快照。</summary>
        public SeatState Seat(int seat) => SeatArray[seat];
        /// <summary>所有座位的籌碼（不含本手已投入）。</summary>
        public int[] Stacks() { var r = new int[SeatArray.Length]; for (int i = 0; i < r.Length; i++) r[i] = SeatArray[i].Stack; return r; }
        /// <summary>公共牌。</summary>
        public IReadOnlyList<Card> Board => BoardList.AsReadOnly();

        /// <summary>階段。</summary>
        public HandPhase Phase { get; internal set; }
        /// <summary>目前的街（只在 <see cref="HandPhase.Betting"/> 時有意義）。</summary>
        public Street Street { get; internal set; }
        /// <summary>已開始的手數（第一手開始後為 1）。</summary>
        public int HandNumber { get; internal set; }
        /// <summary>本手的種子。</summary>
        public ulong Seed { get; internal set; }
        /// <summary>按鈕座位（dead button 時可能是空位）。</summary>
        public int Button { get; internal set; }
        /// <summary>小盲座位；-1 表示本手沒有小盲。</summary>
        public int SmallBlindSeat { get; internal set; }
        /// <summary>大盲座位。</summary>
        public int BigBlindSeat { get; internal set; }
        /// <summary>小盲金額。</summary>
        public int SmallBlind { get; internal set; }
        /// <summary>大盲金額。</summary>
        public int BigBlind { get; internal set; }
        /// <summary>輪到的座位；-1 表示沒有人需要行動。</summary>
        public int ToAct { get; internal set; }
        /// <summary>本街目前最高的下注水位（＝要跟到的金額）。大盲籌碼不足時為實際貼出的最高盲注（TDA 53-B：貼出的大盲即翻牌前開局下注）。</summary>
        public int CurrentBet { get; internal set; }
        /// <summary>本街最近一次「完整」下注／加注的增量（最小加注單位，TDA Rule 45-A）。每街初始為大盲。</summary>
        public int LastFullRaise { get; internal set; }
        /// <summary>本街最後一位下注／加注者；-1 表示本街沒有人下注。</summary>
        public int LastAggressor { get; internal set; }
        /// <summary>本街下注／加注的次數（翻牌前大盲算第 1 次）；用於判斷「面對開局下注」（TDA Rule 53-B）。</summary>
        public int BetCount { get; internal set; }

        internal int SmallBlindPosition { get; set; } = -1;   // 小盲「位置」（可能無人貼，dead button 輪轉用）

        /// <summary>總籌碼（籌碼＋本手已投入），用於守恆檢查。</summary>
        public int TotalChips()
        {
            int t = 0;
            foreach (var s in SeatArray) t += s.Stack + s.Committed;
            return t;
        }
    }
}
