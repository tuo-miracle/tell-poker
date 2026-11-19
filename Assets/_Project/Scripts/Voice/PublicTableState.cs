using System;
using System.Collections.Generic;
using System.Text;
using Poker.Core;

namespace Poker.Voice
{
    public sealed class PublicSeat
    {
        public string Name;
        public int Stack, Bet;
        public string Status;
    }

    public sealed class PublicAction
    {
        public string Street, Who, Action;
        public int Amount;
    }

    /// <summary>
    /// 送給語言模型（中繼服務）的牌局狀態：只有公開資訊——盲注、底池、各家籌碼與本街下注、公牌、行動紀錄、誰棄牌、輪到誰、要跟多少。
    /// <b>任何人的底牌都不在這裡</b>（HandState 的 Hole1／Hole2 從不讀取），所以模型不可能說溜嘴。
    /// 格式與 tools/voice-proxy/src/facts.ts 的 PublicState 一致。
    /// </summary>
    public sealed class PublicTableState
    {
        public string Phase = "between_hands";
        public int Hand;
        public string Street = "preflop";
        public List<string> Board = new List<string>();
        public int SmallBlind, BigBlind, Pot;
        public string ToAct, Button;
        public int YouToCall;
        public List<PublicSeat> Seats = new List<PublicSeat>();
        public List<PublicAction> Actions = new List<PublicAction>();

        public PublicSeat Seat(string name)
        {
            foreach (var s in Seats) if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <param name="names">各座位的名字（小寫；玩家是 "you"）。</param>
        public static PublicTableState From(HandState s, IReadOnlyList<string> names, int humanSeat, IReadOnlyList<PublicAction> actions)
        {
            bool betting = s.Phase == HandPhase.Betting;
            string NameOf(int seat) => seat >= 0 && seat < names.Count ? names[seat] : null;
            var st = new PublicTableState
            {
                Phase = betting ? "betting" : "between_hands",
                Hand = s.HandNumber,
                Street = s.Street.ToString().ToLowerInvariant(),
                SmallBlind = s.SmallBlind,
                BigBlind = s.BigBlind,
                ToAct = betting ? NameOf(s.ToAct) : null,
                Button = NameOf(s.Button),
            };
            foreach (var c in s.Board) st.Board.Add(c.ToString());
            int pot = 0;
            for (int i = 0; i < s.SeatCount; i++)
            {
                var seat = s.Seat(i);
                if (betting) pot += seat.Committed;
                st.Seats.Add(new PublicSeat
                {
                    Name = NameOf(i), Stack = seat.Stack, Bet = betting ? seat.StreetBet : 0,
                    Status = seat.Status == SeatStatus.AllIn ? "allin" : seat.Status.ToString().ToLowerInvariant(),
                });
            }
            st.Pot = pot;
            if (betting && humanSeat >= 0 && humanSeat < s.SeatCount)
            {
                var me = s.Seat(humanSeat);
                st.YouToCall = me.IsLive ? Math.Min(me.Stack, Math.Max(0, s.CurrentBet - me.StreetBet)) : 0;
            }
            if (actions != null) st.Actions.AddRange(actions);
            return st;
        }

        public string ToJson()
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"phase\":"); JsonText.Str(sb, Phase);
            sb.Append(",\"hand\":").Append(Hand);
            sb.Append(",\"street\":"); JsonText.Str(sb, Street);
            sb.Append(",\"board\":[");
            for (int i = 0; i < Board.Count; i++) { if (i > 0) sb.Append(','); JsonText.Str(sb, Board[i]); }
            sb.Append("],\"blinds\":{\"small\":").Append(SmallBlind).Append(",\"big\":").Append(BigBlind).Append('}');
            sb.Append(",\"pot\":").Append(Pot);
            sb.Append(",\"toAct\":"); JsonText.Str(sb, ToAct);
            sb.Append(",\"button\":"); JsonText.Str(sb, Button);
            sb.Append(",\"youToCall\":").Append(YouToCall);
            sb.Append(",\"seats\":[");
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":"); JsonText.Str(sb, s.Name);
                sb.Append(",\"stack\":").Append(s.Stack).Append(",\"bet\":").Append(s.Bet);
                sb.Append(",\"status\":"); JsonText.Str(sb, s.Status);
                sb.Append('}');
            }
            sb.Append("],\"actions\":[");
            for (int i = 0; i < Actions.Count; i++)
            {
                var a = Actions[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"street\":"); JsonText.Str(sb, a.Street);
                sb.Append(",\"who\":"); JsonText.Str(sb, a.Who);
                sb.Append(",\"action\":"); JsonText.Str(sb, a.Action);
                sb.Append(",\"amount\":").Append(a.Amount).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 這一手的公開行動紀錄（盲注、過牌、跟注、下注、加注、棄牌、贏池）。只看旁觀者看得到的事件（PlayerView.Filter 座位 -1），
    /// 攤牌亮牌事件不記（裡面有牌）。新的一手開始時清空。
    /// </summary>
    public sealed class VoiceActionLog
    {
        public const int MaxActions = 40;
        readonly List<PublicAction> _actions = new List<PublicAction>();
        string _street = "preflop";

        public IReadOnlyList<PublicAction> Actions => _actions;

        public void Observe(IEnumerable<GameEvent> events, IReadOnlyList<string> names)
        {
            if (events == null) return;
            string Who(int seat) => seat >= 0 && seat < names.Count ? names[seat] : "?";
            foreach (var e in PlayerView.Filter(events, -1))
            {
                switch (e)
                {
                    case HandStarted _:
                        _actions.Clear();
                        _street = "preflop";
                        break;
                    case BlindPosted b:
                        Add(Who(b.Seat), b.Type == BlindType.Small ? "small_blind" : "big_blind", b.Amount);
                        break;
                    case PlayerActed a:
                        string act = a.IsAllIn ? "all_in" : a.Action.ToString().ToLowerInvariant();
                        int amount = a.Action == ActionType.Check || a.Action == ActionType.Fold ? 0 : a.StreetBet;
                        Add(Who(a.Seat), act, amount);
                        break;
                    case StreetDealt sd:
                        _street = sd.Street.ToString().ToLowerInvariant();
                        break;
                    case PotAwarded pa:
                        foreach (var share in pa.Shares) Add(Who(share.Seat), "wins", share.Amount);
                        break;
                }
            }
        }

        void Add(string who, string action, int amount)
        {
            _actions.Add(new PublicAction { Street = _street, Who = who, Action = action, Amount = amount });
            if (_actions.Count > MaxActions) _actions.RemoveAt(0);
        }
    }
}
