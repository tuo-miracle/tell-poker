using System.Collections.Generic;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 依真實房間換座位：TablePlacer 把 MRUK 的牆與家具交給 TableFrame.SetRoom（牌桌座標），
    /// 這裡用 SeatingPlanner 算出不會卡進牆、而且都在玩家視線 ±60° 內的座位，等沒有動畫的時候搬過去
    /// （角色、AI 已經發出去的牌；籌碼堆與下注每幀依 Spots 擺，自然跟著走）。
    /// 沒有房間資料就完全照舊（TableSeats）；一對一的配置不動。
    /// log：[Table] seating &lt;default|shifted|compressed&gt; (blocked: …) seats=…
    /// </summary>
    public sealed partial class GameController
    {
        bool _seatingDirty;
        /// <summary>目前套用的座位規劃（還沒有房間資料時是 null）。</summary>
        public SeatingPlan Seating { get; private set; }

        void InitSeating()
        {
            Table.RoomChanged += OnRoomChanged;
            _seatingDirty = Table.Room != null;
        }

        void OnRoomChanged(TableFrame f) => _seatingDirty = true;

        /// <summary>換座位不能打斷發牌、收籌碼的動畫，也不在亮牌（攤牌、一手結束）的時候搬。</summary>
        bool SeatingSafe =>
            _seq.IsIdle && _free.Count == 0 && _preMuckT < 0f &&
            (Flow == GameFlow.NotStarted || Flow == GameFlow.PlayerTurn || Flow == GameFlow.AiThinking || Flow == GameFlow.GameOver);

        void TickSeating()
        {
            if (!_seatingDirty || Table.Room == null || !SeatingSafe) return;
            _seatingDirty = false;
            if (!Spots.IsMultiway) return;

            // 座位順序：AI 座位依編號，最後是荷官
            var seats = new List<int>(Session.AiSeats);
            var defaults = new List<Pose>();
            foreach (var s in seats) defaults.Add(Spots.DefaultSeatPose(s));
            defaults.Add(Spots.DefaultDealerSeat);

            var felt = new Vector2(Spots.Layout.FeltSize.X, Spots.Layout.FeltSize.Y);
            var head = Head != null ? Table.WorldToTable(Head.position) : SeatingPlanner.DefaultHead;
            var plan = SeatingPlanner.Plan(felt, defaults, Table.Room, head);
            if (Seating != null && plan.SameSeatsAs(Seating)) return;
            Seating = plan;
            Debug.Log("[Table] seating " + plan.Describe());
            ApplySeats(seats, plan.Seats);
        }

        void ApplySeats(List<int> seats, Pose[] planned)
        {
            int n = Session.SeatCount;
            // 已經在桌上的 AI 牌：記住它在原本的「牌位」還是「棄牌處」，搬到新座位的同一個地方
            var mucked = new bool[n, 2];
            foreach (var s in seats)
                for (int i = 0; i < 2; i++)
                {
                    var p = Flat(Props.SeatCards[s][i].transform.localPosition);
                    mucked[s, i] = (p - Flat(Spots.MuckOf(s, i))).sqrMagnitude < (p - Flat(Spots.CardOf(s, i))).sqrMagnitude;
                }

            var poses = new Pose[n];
            for (int k = 0; k < seats.Count; k++) poses[seats[k]] = planned[k];
            Spots.SetSeats(poses, planned[seats.Count]);

            foreach (var s in seats)
            {
                var c = Character(s);
                if (c != null) c.transform.SetLocalPositionAndRotation(Spots.SeatPose(s).position, Spots.SeatPose(s).rotation);
                for (int i = 0; i < 2; i++)
                {
                    var card = Props.SeatCards[s][i];
                    if (!card.gameObject.activeSelf) continue;
                    var target = mucked[s, i] ? Spots.MuckOf(s, i) : Spots.CardOf(s, i);
                    card.transform.localPosition = new Vector3(target.x, card.transform.localPosition.y, target.z);
                    card.SetYaw(Spots.CardYaw(s));
                }
            }
            if (Dealer != null) Dealer.transform.SetLocalPositionAndRotation(Spots.DealerSeat.position, Spots.DealerSeat.rotation);
            Props.RefreshSeatGlows();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
