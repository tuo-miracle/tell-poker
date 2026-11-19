using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>
    /// 玩家視角過濾器：給定觀看者座位，濾掉其他人的私密事件（目前只有 <see cref="HoleCardsDealt"/>）。
    /// 多人連線時，伺服器對每位玩家送出 <c>Filter(events, seat)</c> 的結果即可；旁觀者用座位 -1。
    /// </summary>
    public static class PlayerView
    {
        /// <summary>事件是否可讓 <paramref name="viewerSeat"/> 看到。</summary>
        public static bool IsVisibleTo(GameEvent e, int viewerSeat)
        {
            var only = e.VisibleToSeat;
            return only == null || only.Value == viewerSeat;
        }

        /// <summary>依序回傳 <paramref name="viewerSeat"/> 可見的事件。</summary>
        public static IEnumerable<GameEvent> Filter(IEnumerable<GameEvent> events, int viewerSeat)
        {
            foreach (var e in events)
                if (IsVisibleTo(e, viewerSeat)) yield return e;
        }
    }
}
