using Poker.Core;

namespace Poker.Game
{
    /// <summary>
    /// 玩家丟進下注區的籌碼代表什麼行動。大多數情況直接用 Core 的 InterpretChipCommit；
    /// 另外補上 Core 只看金額而做不到的「單一大面額籌碼」規則（面對下注只放一枚、沒宣告加注 → 跟注，找零退回）。
    /// </summary>
    public readonly struct ChipCommit
    {
        public readonly ChipCommitKind Kind;
        public readonly PlayerCommand Command;
        public readonly int Pushed;
        public readonly int Committed;
        public readonly string Rule;

        public ChipCommit(ChipCommitKind kind, PlayerCommand command, int pushed, int committed, string rule)
        {
            Kind = kind; Command = command; Pushed = pushed; Committed = committed; Rule = rule;
        }

        public bool IsValid => Kind != ChipCommitKind.Invalid && Command != null;
        /// <summary>正 = 還要補籌碼，負 = 退回多出的籌碼（金額）。</summary>
        public int Adjustment => Committed - Pushed;

        public const string OversizedChipRule = "單一大面額籌碼：面對下注只放一枚且未宣告加注 → 視為跟注";

        public static ChipCommit From(ChipCommitInterpretation c) =>
            new ChipCommit(c.Kind, c.Command, c.ChipsPushed, c.ChipsCommitted, c.Rule);

        public static ChipCommit Interpret(HandState state, int seat, int chipCount, int denomination)
        {
            int amount = chipCount * denomination;
            var core = Engine.InterpretChipCommit(state, seat, amount);
            if (chipCount != 1 || !core.Kind.Equals(ChipCommitKind.Raise) && !core.Kind.Equals(ChipCommitKind.Bet))
                return From(core);
            var la = Engine.GetLegalActions(state, seat);
            int toCall = la.CurrentBet - la.StreetBet;
            if (toCall <= 0 || !la.CanCall || amount <= toCall) return From(core);
            return new ChipCommit(ChipCommitKind.Call, new Call(seat), amount, la.CallAmount, OversizedChipRule);
        }
    }
}
