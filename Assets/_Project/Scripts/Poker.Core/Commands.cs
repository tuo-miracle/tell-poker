using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Poker.Core
{
    /// <summary>送進 <see cref="Engine.Apply"/> 的指令基底類別。所有指令皆不可變。</summary>
    public abstract class Command { }

    /// <summary>
    /// 開始新的一手。
    /// <para><see cref="Button"/> 為 null 時自動輪轉（TDA dead button：大盲每手前進到下一位有籌碼者）；
    /// 第一手為 null 時按鈕給座位編號最小的有籌碼玩家。</para>
    /// </summary>
    public sealed class StartHand : Command
    {
        /// <summary>建立開局指令。</summary>
        /// <param name="seed">洗牌種子（相同種子＝相同牌序，可重播）。</param>
        /// <param name="button">指定按鈕座位（必須有籌碼）；null 表示自動輪轉。</param>
        /// <param name="smallBlind">小盲（&gt;0，≤ 大盲）。</param>
        /// <param name="bigBlind">大盲（&gt;0）。</param>
        /// <param name="preset">指定部分底牌／公共牌（教學、測試、重播用）；null 表示完全依種子洗牌。</param>
        public StartHand(ulong seed, int? button, int smallBlind, int bigBlind, CardPreset preset = null)
        {
            Seed = seed; Button = button; SmallBlind = smallBlind; BigBlind = bigBlind; Preset = preset;
        }

        /// <summary>洗牌種子。</summary>
        public ulong Seed { get; }
        /// <summary>指定按鈕座位；null＝自動輪轉。</summary>
        public int? Button { get; }
        /// <summary>小盲金額。</summary>
        public int SmallBlind { get; }
        /// <summary>大盲金額。</summary>
        public int BigBlind { get; }
        /// <summary>指定牌（可為 null）。</summary>
        public CardPreset Preset { get; }
    }

    /// <summary>
    /// 指定牌：某些座位的底牌、以及前幾張公共牌（依 flop→turn→river 順序）。
    /// 其餘的牌由種子洗牌後的剩餘牌補上。不得重複。
    /// </summary>
    public sealed class CardPreset
    {
        /// <summary>建立指定牌。輸入會被複製，之後修改原陣列不影響此物件。</summary>
        public CardPreset(IReadOnlyDictionary<int, Card[]> holeCards, IReadOnlyList<Card> board)
        {
            var copy = new Dictionary<int, IReadOnlyList<Card>>();
            if (holeCards != null)
                foreach (var kv in holeCards)
                    copy[kv.Key] = Array.AsReadOnly(kv.Value == null ? Array.Empty<Card>() : (Card[])kv.Value.Clone());
            HoleCards = new ReadOnlyDictionary<int, IReadOnlyList<Card>>(copy);
            Board = Array.AsReadOnly(board == null ? Array.Empty<Card>() : new List<Card>(board).ToArray());
        }

        /// <summary>以字串建立，例如 <c>CardPreset.Parse(new Dictionary&lt;int,string&gt;{{0,"AsKs"}}, "2c3d4h5s6c")</c>。</summary>
        public static CardPreset Parse(IReadOnlyDictionary<int, string> holeCards, string board)
        {
            var d = new Dictionary<int, Card[]>();
            if (holeCards != null) foreach (var kv in holeCards) d[kv.Key] = Card.ParseMany(kv.Value);
            return new CardPreset(d, Card.ParseMany(board ?? ""));
        }

        /// <summary>座位 → 兩張底牌。</summary>
        public IReadOnlyDictionary<int, IReadOnlyList<Card>> HoleCards { get; }
        /// <summary>前 0–5 張公共牌。</summary>
        public IReadOnlyList<Card> Board { get; }
    }

    /// <summary>玩家行動指令的基底類別（帶座位，用來驗證輪到誰）。</summary>
    public abstract class PlayerCommand : Command
    {
        /// <summary>建立玩家指令。</summary>
        protected PlayerCommand(int seat) { Seat = seat; }
        /// <summary>行動的座位。</summary>
        public int Seat { get; }
    }

    /// <summary>棄牌。</summary>
    public sealed class Fold : PlayerCommand { /// <summary>建立棄牌指令。</summary>
        public Fold(int seat) : base(seat) { } }

    /// <summary>過牌（不需跟注時）。</summary>
    public sealed class Check : PlayerCommand { /// <summary>建立過牌指令。</summary>
        public Check(int seat) : base(seat) { } }

    /// <summary>跟注（籌碼不足時自動變成 all-in 跟注）。</summary>
    public sealed class Call : PlayerCommand { /// <summary>建立跟注指令。</summary>
        public Call(int seat) : base(seat) { } }

    /// <summary>本街第一個下注。<see cref="Amount"/> 是下注總額（＝下注到多少）。</summary>
    public sealed class Bet : PlayerCommand
    {
        /// <summary>建立下注指令。</summary>
        public Bet(int seat, int amount) : base(seat) { Amount = amount; }
        /// <summary>下注總額。</summary>
        public int Amount { get; }
    }

    /// <summary>加注。<see cref="ToAmount"/> 是本街「加注到」的總額（不是加多少）。</summary>
    public sealed class Raise : PlayerCommand
    {
        /// <summary>建立加注指令。</summary>
        public Raise(int seat, int toAmount) : base(seat) { ToAmount = toAmount; }
        /// <summary>本街加注到的總額。</summary>
        public int ToAmount { get; }
    }

    /// <summary>推出全部籌碼。依金額自動視為 all-in 跟注、下注或加注（短 all-in 亦可）。</summary>
    public sealed class AllIn : PlayerCommand { /// <summary>建立 all-in 指令。</summary>
        public AllIn(int seat) : base(seat) { } }
}
