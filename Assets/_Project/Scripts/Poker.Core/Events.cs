using System;
using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>
    /// 引擎輸出的事件基底類別。<see cref="VisibleToSeat"/> 為 null 表示公開事件；
    /// 否則只有該座位能看到（例如底牌）。多人連線請用 <see cref="PlayerView"/> 過濾後再送出。
    /// </summary>
    public abstract class GameEvent
    {
        /// <summary>私密事件的可見座位；null＝公開。</summary>
        public virtual int? VisibleToSeat => null;

        /// <summary>包成唯讀集合，避免外部轉型回陣列竄改事件內容。</summary>
        internal static IReadOnlyList<T> RO<T>(T[] a) => Array.AsReadOnly(a);
    }

    /// <summary>街（下注輪）。</summary>
    public enum Street
    {
        /// <summary>翻牌前</summary>
        Preflop,
        /// <summary>翻牌</summary>
        Flop,
        /// <summary>轉牌</summary>
        Turn,
        /// <summary>河牌</summary>
        River,
    }

    /// <summary>盲注種類。</summary>
    public enum BlindType
    {
        /// <summary>小盲</summary>
        Small,
        /// <summary>大盲</summary>
        Big,
    }

    /// <summary>行動種類（事件用）。All-in 以 <see cref="PlayerActed.IsAllIn"/> 標示。</summary>
    public enum ActionType
    {
        /// <summary>棄牌</summary>
        Fold,
        /// <summary>過牌</summary>
        Check,
        /// <summary>跟注</summary>
        Call,
        /// <summary>下注（本街第一注）</summary>
        Bet,
        /// <summary>加注</summary>
        Raise,
    }

    /// <summary>新的一手開始。種子不公開（避免洩漏牌序）。</summary>
    public sealed class HandStarted : GameEvent
    {
        internal HandStarted(int handNumber, int button, int smallBlindSeat, int bigBlindSeat, int smallBlind, int bigBlind, int[] stacks, int[] seatsInHand)
        {
            HandNumber = handNumber; Button = button; SmallBlindSeat = smallBlindSeat; BigBlindSeat = bigBlindSeat;
            SmallBlind = smallBlind; BigBlind = bigBlind; Stacks = RO(stacks); SeatsInHand = RO(seatsInHand);
        }
        /// <summary>第幾手（從 1 起算）。</summary>
        public int HandNumber { get; }
        /// <summary>按鈕座位（dead button 時可能是沒有玩家的座位）。</summary>
        public int Button { get; }
        /// <summary>小盲座位；-1 表示本手沒有小盲（dead small blind）。</summary>
        public int SmallBlindSeat { get; }
        /// <summary>大盲座位。</summary>
        public int BigBlindSeat { get; }
        /// <summary>小盲金額。</summary>
        public int SmallBlind { get; }
        /// <summary>大盲金額。</summary>
        public int BigBlind { get; }
        /// <summary>開局時各座位籌碼（貼盲注前）。</summary>
        public IReadOnlyList<int> Stacks { get; }
        /// <summary>參與本手的座位（有籌碼者），依座位編號排序。</summary>
        public IReadOnlyList<int> SeatsInHand { get; }
    }

    /// <summary>貼盲注。</summary>
    public sealed class BlindPosted : GameEvent
    {
        internal BlindPosted(int seat, BlindType type, int amount, bool isAllIn) { Seat = seat; Type = type; Amount = amount; IsAllIn = isAllIn; }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>大盲或小盲。</summary>
        public BlindType Type { get; }
        /// <summary>實際貼出金額（籌碼不足時少於盲注）。</summary>
        public int Amount { get; }
        /// <summary>是否因貼盲注而 all-in。</summary>
        public bool IsAllIn { get; }
    }

    /// <summary>發底牌（私密：只有該座位可見）。</summary>
    public sealed class HoleCardsDealt : GameEvent
    {
        internal HoleCardsDealt(int seat, Card[] cards) { Seat = seat; Cards = GameEvent.RO(cards); }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>兩張底牌。</summary>
        public IReadOnlyList<Card> Cards { get; }
        /// <inheritdoc/>
        public override int? VisibleToSeat => Seat;
    }

    /// <summary>玩家行動完成。</summary>
    public sealed class PlayerActed : GameEvent
    {
        internal PlayerActed(int seat, ActionType action, int amountAdded, int streetBet, int stackAfter, bool isAllIn, bool isFullRaise)
        {
            Seat = seat; Action = action; AmountAdded = amountAdded; StreetBet = streetBet; StackAfter = stackAfter; IsAllIn = isAllIn; IsFullRaise = isFullRaise;
        }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>行動種類。</summary>
        public ActionType Action { get; }
        /// <summary>這次新放入的籌碼。</summary>
        public int AmountAdded { get; }
        /// <summary>行動後本街累計下注（＝「下注到」）。</summary>
        public int StreetBet { get; }
        /// <summary>行動後剩餘籌碼。</summary>
        public int StackAfter { get; }
        /// <summary>是否 all-in。</summary>
        public bool IsAllIn { get; }
        /// <summary>下注／加注是否達到完整加注（會重開下注）；短 all-in 為 false。</summary>
        public bool IsFullRaise { get; }
    }

    /// <summary>發出公共牌（flop 三張、turn／river 各一張）。</summary>
    public sealed class StreetDealt : GameEvent
    {
        internal StreetDealt(Street street, Card[] newCards, Card[] board) { Street = street; NewCards = RO(newCards); Board = RO(board); }
        /// <summary>進入的街。</summary>
        public Street Street { get; }
        /// <summary>這次新發的牌。</summary>
        public IReadOnlyList<Card> NewCards { get; }
        /// <summary>目前完整的公共牌。</summary>
        public IReadOnlyList<Card> Board { get; }
    }

    /// <summary>沒人跟注的下注部分退還給下注者。</summary>
    public sealed class UncalledBetReturned : GameEvent
    {
        internal UncalledBetReturned(int seat, int amount) { Seat = seat; Amount = amount; }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>退還金額。</summary>
        public int Amount { get; }
    }

    /// <summary>一個底池（主池或邊池）。</summary>
    public sealed class Pot
    {
        internal Pot(int amount, int[] eligibleSeats) { Amount = amount; EligibleSeats = GameEvent.RO(eligibleSeats); }
        /// <summary>金額。</summary>
        public int Amount { get; }
        /// <summary>有資格贏這個池的座位（未棄牌），依座位編號排序。</summary>
        public IReadOnlyList<int> EligibleSeats { get; }
    }

    /// <summary>每輪下注結束（收籌碼）時的底池狀態。索引 0 為主池，其後為邊池。</summary>
    public sealed class PotsUpdated : GameEvent
    {
        internal PotsUpdated(Pot[] pots) { Pots = RO(pots); }
        /// <summary>主池與各邊池。</summary>
        public IReadOnlyList<Pot> Pots { get; }
    }

    /// <summary>攤牌時亮出的一手牌。</summary>
    public sealed class ShownHand
    {
        internal ShownHand(int seat, Card[] cards) { Seat = seat; Cards = GameEvent.RO(cards); }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>兩張底牌。</summary>
        public IReadOnlyList<Card> Cards { get; }
    }

    /// <summary>
    /// 攤牌亮牌，<see cref="Hands"/> 依亮牌順序排列（TDA Rule 18-A）。
    /// 有人 all-in 且下注結束時，在發完剩餘公共牌「之前」就亮牌（TDA Rule 17 與其 Addendum）。
    /// </summary>
    public sealed class ShowdownRevealed : GameEvent
    {
        internal ShowdownRevealed(ShownHand[] hands, bool isAllInTableUp) { Hands = RO(hands); IsAllInTableUp = isAllInTableUp; }
        /// <summary>依亮牌順序的各手牌。</summary>
        public IReadOnlyList<ShownHand> Hands { get; }
        /// <summary>是否為 all-in 亮牌（TDA Rule 17：有人 all-in 且不可能再有下注時，全部亮牌；若公共牌未發完，之後才發完）。</summary>
        public bool IsAllInTableUp { get; }
    }

    /// <summary>某位玩家從某個池分到的金額。</summary>
    public sealed class PotShare
    {
        internal PotShare(int seat, int amount) { Seat = seat; Amount = amount; }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>分到的金額（含零頭）。</summary>
        public int Amount { get; }
    }

    /// <summary>一個池的分配結果。</summary>
    public sealed class PotAwarded : GameEvent
    {
        internal PotAwarded(int potIndex, int amount, PotShare[] shares, int[] oddChipSeats, HandValue? winningHand)
        {
            PotIndex = potIndex; Amount = amount; Shares = RO(shares); OddChipSeats = RO(oddChipSeats); WinningHand = winningHand;
        }
        /// <summary>池索引（0＝主池）。</summary>
        public int PotIndex { get; }
        /// <summary>池金額。</summary>
        public int Amount { get; }
        /// <summary>各贏家分到的金額，依「按鈕左邊起」的順序。</summary>
        public IReadOnlyList<PotShare> Shares { get; }
        /// <summary>拿到零頭（各多 1 枚）的座位；沒有零頭時為空。</summary>
        public IReadOnlyList<int> OddChipSeats { get; }
        /// <summary>贏的牌力；無攤牌（其他人都棄牌）時為 null。</summary>
        public HandValue? WinningHand { get; }
    }

    /// <summary>這一手結束。</summary>
    public sealed class HandEnded : GameEvent
    {
        internal HandEnded(int[] finalStacks) { FinalStacks = RO(finalStacks); }
        /// <summary>各座位最終籌碼。</summary>
        public IReadOnlyList<int> FinalStacks { get; }
    }
}
