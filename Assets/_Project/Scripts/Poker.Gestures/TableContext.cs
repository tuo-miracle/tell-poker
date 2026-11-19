namespace Poker.Gestures
{
    /// <summary>牌局階段（Gestures 不依賴 Poker.Core，自帶一份）。</summary>
    public enum TablePhase { Preflop, Flop, Turn, River, Showdown }

    /// <summary>遊戲層每幀提供的牌桌狀態，手勢辨識用它當閘門。</summary>
    public struct TableContext
    {
        /// <summary>是否輪到玩家。攤牌時輪到玩家亮牌，遊戲層也要設為 true。</summary>
        public bool IsPlayerTurn;
        /// <summary>現在可以過牌嗎（沒有人下注）。</summary>
        public bool CanCheck;
        public TablePhase Phase;
        /// <summary>玩家剩餘籌碼（金額）。含手上拿著、下注區待確認的部分；遊戲層在 BetCommitted 後才扣。</summary>
        public int PlayerChips;
        /// <summary>每枚籌碼的面額。手上一階＝1 枚＝這個金額。</summary>
        public int ChipDenomination;
        /// <summary>遊戲層認定玩家手上拿著的籌碼數（例如物理籌碼黏在手上）。大於 0 時不判斷過牌、棄牌、All-in。</summary>
        public int ChipsInHand;
        /// <summary>玩家面前沒有可以拿起來看的底牌（還沒發、已棄牌、已翻開）。為 true 時不能拿牌，拿著的牌會放回（CardsReturned）。</summary>
        public bool HoleCardsGone;
        /// <summary>還沒輪到玩家，但玩家還在這一手、之後會輪到（沒棄牌、沒 All-in、不是攤牌）。為 true 時：
        /// 棄牌手勢照樣辨識（FoldPushed，遊戲層記下、輪到時自動棄牌）；籌碼可以拿起、拿著、放回；
        /// 丟進下注區的籌碼不算下注（ChipsReleased 不在下注區＋ActionTooEarly），敲桌發 ActionTooEarly。</summary>
        public bool CanActEarly;

        /// <summary>剩餘籌碼換算成枚數（上限）。</summary>
        public int StackChipCount => ChipDenomination > 0 ? PlayerChips / ChipDenomination : 0;

        public static TableContext Default => new TableContext
        {
            IsPlayerTurn = true,
            CanCheck = true,
            Phase = TablePhase.Flop,
            PlayerChips = 1000,
            ChipDenomination = 50,
        };
    }
}
