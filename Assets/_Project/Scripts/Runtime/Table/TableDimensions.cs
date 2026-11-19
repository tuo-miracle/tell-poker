using Poker.Gestures;

namespace Poker.Runtime
{
    /// <summary>
    /// 牌桌尺寸（公尺，牌桌座標）。區域定義只有一份：<see cref="Layout"/>（Poker.Gestures 的 TableLayout），
    /// 手勢辨識、牌桌標線、遊戲物件都用同一個實例。
    /// 絨布範圍：X ∈ [−Width/2, Width/2]，Z ∈ [0, Depth]，原點在玩家側桌緣中央。
    /// </summary>
    public static class TableDimensions
    {
        /// <summary>共用的牌桌區域定義。</summary>
        public static readonly TableLayout Layout = new TableLayout();

        public static float Width => Layout.FeltSize.X;
        public static float Depth => Layout.FeltSize.Y;
        /// <summary>絨布貼在桌面上方的高度，避免與 passthrough 下的真實桌面 z-fighting。</summary>
        public const float FeltLift = 0.002f;
        /// <summary>虛擬桌（備案）的桌面厚度與桌腳。</summary>
        public const float VirtualTopThickness = 0.03f;
        public const float VirtualLegSize = 0.04f;
        /// <summary>扶手寬：家用 84×42 吋撲克桌的軟墊扶手約 4.5–5 吋（docs/research/2026-11-02-real-poker-table-seating.md）；同 TableSeats.RailWidth。</summary>
        public const float VirtualBorder = 0.12f;
    }
}
