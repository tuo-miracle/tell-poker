using System.Numerics;

namespace Poker.Gestures
{
    public enum GestureKind
    {
        PeekChanged,
        ChipsPicked,
        ChipCountChanged,
        ChipsReleased,
        BetCommitted,
        BetRetracted,
        KnockCheck,
        FoldPushed,
        AllInPushStarted,
        AllInConfirmed,
        AllInCancelled,
        ShowdownFlip,
        CardsLifted,
        CardsReturned,
        /// <summary>還沒輪到玩家（TableContext.CanActEarly）就做完的動作被擋下：籌碼丟進下注區（Count＝枚數，籌碼退回）或敲桌（Count＝0）。不是行動。</summary>
        ActionTooEarly,
    }

    /// <summary>
    /// 手勢事件。欄位依 Kind 使用：
    /// PeekChanged → Hand、Amount（0–1）；
    /// ChipsPicked／ChipCountChanged → Hand、Count；
    /// ChipsReleased → Hand、Count、Position（估算落點）、InBetZone；
    /// BetCommitted／BetRetracted → Count（BetRetracted 另有 Hand）；
    /// FoldPushed／ShowdownFlip／KnockCheck → Hand；
    /// ShowdownFlip 由推、拖、拿著牌往前放開而來時另帶 Position（牌翻開的位置，TableLayout.RevealDisplayPoint，z 一定 &gt; 0）；
    /// 原本的翻牌手勢 Position 為 default（在原位翻開）。
    /// </summary>
    public readonly struct GestureEvent
    {
        public readonly GestureKind Kind;
        public readonly double Time;
        public readonly Hand Hand;
        public readonly int Count;
        public readonly float Amount;
        public readonly Vector3 Position;
        public readonly bool InBetZone;
        public readonly Quaternion Rotation;

        public GestureEvent(GestureKind kind, double time, Hand hand = Hand.Right, int count = 0, float amount = 0f,
            Vector3 position = default, bool inBetZone = false, Quaternion rotation = default)
        {
            Kind = kind; Time = time; Hand = hand; Count = count; Amount = amount;
            Position = position; InBetZone = inBetZone; Rotation = rotation;
        }

        /// <summary>會改變牌局的「行動」事件（防誤觸統計用）。咪牌與單純拿放籌碼不算。</summary>
        public bool IsAction =>
            Kind == GestureKind.BetCommitted || Kind == GestureKind.KnockCheck || Kind == GestureKind.FoldPushed ||
            Kind == GestureKind.AllInPushStarted || Kind == GestureKind.AllInConfirmed ||
            Kind == GestureKind.ShowdownFlip;

        public override string ToString() =>
            $"{Time:0.000} {Kind} {Hand} n={Count} a={Amount:0.00} pos=({Position.X:0.00},{Position.Y:0.00},{Position.Z:0.00}) zone={InBetZone}";
    }
}
