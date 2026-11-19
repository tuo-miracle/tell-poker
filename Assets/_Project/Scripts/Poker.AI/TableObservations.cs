using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>
    /// 某個 AI 座位自己記下的牌局歷史，只接受「該座位看得到」的事件（PlayerView 過濾後）。
    /// 收到別人的私密事件（例如別人的底牌）會丟例外，防止遊戲層誤傳。
    /// </summary>
    public sealed class TableObservations
    {
        /// <summary>單一對手的統計。</summary>
        public sealed class SeatStats
        {
            /// <summary>看過的手數。</summary>
            public int Hands;
            /// <summary>主動入池的手數。</summary>
            public int VpipHands;
            /// <summary>翻前加注的手數。</summary>
            public int PfrHands;
            /// <summary>本手是否主動入池。</summary>
            public bool VpipThisHand;
            /// <summary>本手是否翻前加注。</summary>
            public bool RaisedPreflop;
            /// <summary>本手翻牌後下注／加注次數。</summary>
            public int PostflopAggression;
            /// <summary>是否已棄牌。</summary>
            public bool Folded;
        }

        readonly Dictionary<int, SeatStats> _stats = new Dictionary<int, SeatStats>();
        readonly List<GameEvent> _handEvents = new List<GameEvent>();
        Street _street;

        /// <summary>建立。</summary>
        public TableObservations(int seat) { Seat = seat; }

        /// <summary>觀察者座位。</summary>
        public int Seat { get; }
        /// <summary>遊戲時鐘（秒），由遊戲層更新；讀玩家的衰減用。</summary>
        public double Now { get; set; }
        /// <summary>目前手數。</summary>
        public int HandNumber { get; private set; }
        /// <summary>自己的底牌（本手；還沒發為 null）。</summary>
        public IReadOnlyList<Card> MyHole { get; private set; }
        /// <summary>目前公共牌。</summary>
        public IReadOnlyList<Card> Board { get; private set; } = Array.Empty<Card>();
        /// <summary>本手參與的座位。</summary>
        public IReadOnlyList<int> SeatsInHand { get; private set; } = Array.Empty<int>();
        /// <summary>本手是否已結束。</summary>
        public bool HandOver { get; private set; } = true;
        /// <summary>本手到目前的事件（自己可見的）。</summary>
        public IReadOnlyList<GameEvent> HandEvents => _handEvents;

        /// <summary>某座位的統計（沒看過就建立）。</summary>
        public SeatStats Stats(int seat)
        {
            if (!_stats.TryGetValue(seat, out var s)) _stats[seat] = s = new SeatStats();
            return s;
        }

        /// <summary>吃一串事件。</summary>
        public void Observe(IEnumerable<GameEvent> events) { foreach (var e in events) Observe(e); }

        /// <summary>吃一個事件。看不到的事件會丟 <see cref="InvalidOperationException"/>。</summary>
        public void Observe(GameEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (!PlayerView.IsVisibleTo(e, Seat))
                throw new InvalidOperationException("座位 " + Seat + " 不應看到這個事件（" + e.GetType().Name + "）；請先用 PlayerView.Filter 過濾");
            switch (e)
            {
                case HandStarted hs:
                    _handEvents.Clear();
                    HandNumber = hs.HandNumber; SeatsInHand = hs.SeatsInHand; Board = Array.Empty<Card>(); MyHole = null; HandOver = false;
                    _street = Street.Preflop;
                    foreach (var s in hs.SeatsInHand)
                    {
                        var st = Stats(s);
                        st.Hands++; st.VpipThisHand = false; st.RaisedPreflop = false; st.PostflopAggression = 0; st.Folded = false;
                    }
                    break;
                case HoleCardsDealt hc:
                    if (hc.Seat == Seat) MyHole = hc.Cards;
                    break;
                case PlayerActed pa:
                {
                    var st = Stats(pa.Seat);
                    bool aggressive = pa.Action == ActionType.Bet || pa.Action == ActionType.Raise;
                    if (pa.Action == ActionType.Fold) st.Folded = true;
                    if (_street == Street.Preflop)
                    {
                        if (pa.Action == ActionType.Call || aggressive)
                        {
                            if (!st.VpipThisHand) { st.VpipThisHand = true; st.VpipHands++; }
                        }
                        if (aggressive && !st.RaisedPreflop) { st.RaisedPreflop = true; st.PfrHands++; }
                    }
                    else if (aggressive) st.PostflopAggression++;
                    break;
                }
                case StreetDealt sd:
                    _street = sd.Street; Board = sd.Board;
                    break;
                case HandEnded _:
                    HandOver = true;
                    break;
            }
            _handEvents.Add(e);
        }

        /// <summary>
        /// 依對手公開動作推估其範圍（只玩前 X% 起手牌）：輕量版 Loki 再加權。
        /// 沒主動入池（例如大盲過牌）＝任何牌；入池＝其長期入池率；翻前加注再收窄；翻牌後每次下注／加注再收窄。
        /// </summary>
        public float RangeOf(int seat)
        {
            var st = Stats(seat);
            if (!st.VpipThisHand && st.PostflopAggression == 0) return 1f;
            // 先驗：35% 入池，權重 10 手
            float vpip = (st.VpipHands + 3.5f) / (st.Hands + 10f);
            float top = st.VpipThisHand ? vpip : 0.6f;
            if (st.RaisedPreflop) top *= 0.6f;
            for (int i = 0; i < st.PostflopAggression; i++) top *= 0.75f;
            return Math.Max(0.05f, Math.Min(1f, top));
        }
    }
}
