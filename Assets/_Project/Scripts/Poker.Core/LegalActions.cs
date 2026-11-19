namespace Poker.Core
{
    /// <summary>
    /// 某座位目前可做的合法行動（<see cref="Engine.GetLegalActions"/> 的結果）。
    /// 金額一律為整數；Bet/Raise 的範圍以「下注到（to）」表示。
    /// </summary>
    public sealed class LegalActions
    {
        internal static readonly LegalActions NotYourTurn = new LegalActions();

        /// <summary>是否輪到此座位。為 false 時其他欄位皆無意義（全為 false／0）。</summary>
        public bool IsTurn { get; internal set; }
        /// <summary>可否棄牌。</summary>
        public bool CanFold { get; internal set; }
        /// <summary>可否過牌。</summary>
        public bool CanCheck { get; internal set; }
        /// <summary>可否跟注。</summary>
        public bool CanCall { get; internal set; }
        /// <summary>跟注需新放入的籌碼（籌碼不足時＝全部籌碼，即 all-in 跟注）。</summary>
        public int CallAmount { get; internal set; }
        /// <summary>可否下注（本街尚無下注）。</summary>
        public bool CanBet { get; internal set; }
        /// <summary>可否加注（本街已有下注，且加注權未被短 all-in 關閉）。</summary>
        public bool CanRaise { get; internal set; }
        /// <summary>最小下注到／加注到（若籌碼不足最小加注，則等於 <see cref="MaxTo"/>，只能 all-in）。</summary>
        public int MinTo { get; internal set; }
        /// <summary>最大下注到／加注到（＝本街已下注＋剩餘籌碼）。</summary>
        public int MaxTo { get; internal set; }
        /// <summary>可否 all-in（加注權關閉時，只有籌碼不超過跟注額才可 all-in 跟注）。</summary>
        public bool CanAllIn { get; internal set; }
        /// <summary>all-in 時本街下注到的總額。</summary>
        public int AllInTo { get; internal set; }
        /// <summary>目前剩餘籌碼。</summary>
        public int Stack { get; internal set; }
        /// <summary>本街已下注。</summary>
        public int StreetBet { get; internal set; }
        /// <summary>本街目前的跟注水位。</summary>
        public int CurrentBet { get; internal set; }
    }

    /// <summary><see cref="Engine.InterpretChipCommit"/> 的判定結果種類。</summary>
    public enum ChipCommitKind
    {
        /// <summary>無效（需請玩家重新操作）</summary>
        Invalid,
        /// <summary>過牌</summary>
        Check,
        /// <summary>跟注</summary>
        Call,
        /// <summary>下注</summary>
        Bet,
        /// <summary>加注</summary>
        Raise,
        /// <summary>全下</summary>
        AllIn,
    }

    /// <summary>
    /// 「玩家丟進 N 枚籌碼」的判定結果。<see cref="Command"/> 可直接送進 <see cref="Engine.Apply"/>。
    /// </summary>
    public sealed class ChipCommitInterpretation
    {
        internal ChipCommitInterpretation(ChipCommitKind kind, PlayerCommand command, int pushed, int committed, string rule)
        {
            Kind = kind; Command = command; ChipsPushed = pushed; ChipsCommitted = committed; Rule = rule;
        }
        /// <summary>判定種類。</summary>
        public ChipCommitKind Kind { get; }
        /// <summary>對應的指令；<see cref="ChipCommitKind.Invalid"/> 時為 null。</summary>
        public PlayerCommand Command { get; }
        /// <summary>玩家實際推出的籌碼。</summary>
        public int ChipsPushed { get; }
        /// <summary>執行指令後實際投入的籌碼（Invalid 時為 0）。</summary>
        public int ChipsCommitted { get; }
        /// <summary>需補的籌碼（正）或退回的籌碼（負）＝ <see cref="ChipsCommitted"/> − <see cref="ChipsPushed"/>。</summary>
        public int Adjustment => ChipsCommitted - ChipsPushed;
        /// <summary>判定依據（TDA 條號與簡述），供 UI／荷官教學顯示。</summary>
        public string Rule { get; }
    }

    /// <summary>錯誤代碼。</summary>
    public enum EngineErrorCode
    {
        /// <summary>牌局進行中，不能開新的一手。</summary>
        HandInProgress,
        /// <summary>目前沒有進行中的牌局。</summary>
        NoHandInProgress,
        /// <summary>座位編號不合法。</summary>
        InvalidSeat,
        /// <summary>不是輪到此座位。</summary>
        NotYourTurn,
        /// <summary>此時不允許這個行動。</summary>
        IllegalAction,
        /// <summary>金額不合法。</summary>
        InvalidAmount,
        /// <summary>有籌碼的玩家不足 2 人。</summary>
        NotEnoughPlayers,
        /// <summary>盲注設定不合法。</summary>
        InvalidBlinds,
        /// <summary>按鈕座位不合法。</summary>
        InvalidButton,
        /// <summary>指定牌不合法（重複、數量錯、座位錯）。</summary>
        InvalidPreset,
        /// <summary>未知的指令類型。</summary>
        UnknownCommand,
    }

    /// <summary>指令被拒絕的原因。</summary>
    public sealed class EngineError
    {
        internal EngineError(EngineErrorCode code, string message) { Code = code; Message = message; }
        /// <summary>錯誤代碼。</summary>
        public EngineErrorCode Code { get; }
        /// <summary>說明（臺灣中文）。</summary>
        public string Message { get; }
        /// <inheritdoc/>
        public override string ToString() => Code + ": " + Message;
    }

    /// <summary>
    /// <see cref="Engine.Apply"/> 的結果。成功時含新狀態與事件；失敗時 <see cref="State"/> 為原本的狀態（完全不變），事件為空。
    /// </summary>
    public sealed class ApplyResult
    {
        internal ApplyResult(HandState state, System.Collections.Generic.IReadOnlyList<GameEvent> events, EngineError error)
        {
            State = state; Events = events; Error = error;
        }
        /// <summary>是否成功。</summary>
        public bool IsSuccess => Error == null;
        /// <summary>新狀態（失敗時為原狀態）。</summary>
        public HandState State { get; }
        /// <summary>這個指令產生的事件（依發生順序）。</summary>
        public System.Collections.Generic.IReadOnlyList<GameEvent> Events { get; }
        /// <summary>錯誤；成功時為 null。</summary>
        public EngineError Error { get; }
    }
}
