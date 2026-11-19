using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>一手中某次決策當下的內心狀態。</summary>
    public sealed class RevealMoment
    {
        /// <summary>街。</summary>
        public Street Street;
        /// <summary>動作（例如 "Bet 40"、"Fold"）。</summary>
        public string Action;
        /// <summary>真實意圖。</summary>
        public Intent Intent;
        /// <summary>當時的勝率估計。</summary>
        public double Equity;
        /// <summary>真實狀態。</summary>
        public InnerState TrueState;
        /// <summary>當時演出的狀態。</summary>
        public InnerState ShownState;
        /// <summary>是否刻意反向演出。</summary>
        public bool Reversed;
    }

    /// <summary>每手結束後亮給玩家看的 AI 底牌與內心狀態，供玩家驗證自己的判讀。</summary>
    public sealed class HandReveal
    {
        /// <summary>手數。</summary>
        public int HandNumber;
        /// <summary>座位。</summary>
        public int Seat;
        /// <summary>角色名。</summary>
        public string Name;
        /// <summary>底牌。</summary>
        public IReadOnlyList<Card> HoleCards;
        /// <summary>每次決策的內心狀態（依時間順序）。</summary>
        public IReadOnlyList<RevealMoment> Moments;
        /// <summary>英文標題，例如 "Bluffing"。</summary>
        public string Headline;
        /// <summary>英文補充：最後一次演出是照實、反向還是撲克臉（2026-11-03 起不顯示，只留在資料裡）。</summary>
        public string Detail;

        /// <summary>意圖 → 英文標題：一行白話、一眼看懂（2026-11-03 使用者：舊的「Was semi-bluffing on a draw」太長難懂）。</summary>
        public static string HeadlineFor(Intent i)
        {
            switch (i)
            {
                case Intent.Value: return "Strong hand";
                case Intent.Bluff: return "Bluffing";
                case Intent.SemiBluff: return "On a draw";
                case Intent.Marginal: return "Unsure";
                default: return "Gave up";
            }
        }

        /// <summary>這一手沒輪到他行動（大家在他之前就棄了）。</summary>
        public const string NoAction = "No action";

        internal static HandReveal Build(int hand, int seat, string name, IReadOnlyList<Card> hole, List<RevealMoment> moments)
        {
            var r = new HandReveal { HandNumber = hand, Seat = seat, Name = name, HoleCards = hole, Moments = moments.ToArray() };
            if (moments.Count == 0) { r.Headline = NoAction; r.Detail = ""; return r; }
            var last = moments[moments.Count - 1];
            r.Headline = HeadlineFor(last.Intent);
            r.Detail = last.Reversed ? "Acted the opposite" : last.ShownState == InnerState.Masked ? "Kept a poker face" : "Showed it honestly";
            return r;
        }
    }
}
