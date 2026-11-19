using System.Collections.Generic;
using Poker.Characters;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 遊戲物件在牌桌上的位置（牌桌座標，Unity 型別）。全部由 TableLayout 推出：
    /// 玩家這側用 Layout 的區域；每個 AI 座位有自己的「座位框架」（角色面前的桌緣、面向牌桌），
    /// 牌、籌碼堆、下注、棄牌都放在框架裡的固定位置。
    /// 一對一：Pip 正對面、Dot 坐他右手邊（老手座，M1 的配置搬到真實尺寸的桌子）。
    /// 多人：用 TableSeats（長圓桌：左端 Pip、荷官左邊 Rocco、遠端中央 Dot、荷官右邊老手、右端 Vex）。
    /// </summary>
    public sealed class TableSpots
    {
        public readonly TableLayout Layout;
        readonly CharacterKind[] _kinds;
        readonly Pose[] _seats;
        readonly Pose[] _defaultSeats;
        readonly bool _multi;
        Pose _dealer, _defaultDealer;

        /// <param name="seatKinds">每個座位的角色（0 號是玩家，忽略）。null = 一對一（玩家＋Pip）。</param>
        public TableSpots(TableLayout layout, IReadOnlyList<CharacterKind> seatKinds = null)
        {
            Layout = layout;
            _kinds = seatKinds != null ? new List<CharacterKind>(seatKinds).ToArray() : new[] { CharacterKind.Rookie, CharacterKind.Rookie };
            _multi = _kinds.Length > 2;
            _seats = new Pose[_kinds.Length];
            // TableSeats 的桌子大小以實際的絨布為準
            TableSeats.TableSize = new Vector2(layout.FeltSize.X, layout.FeltSize.Y);
            for (int s = 1; s < _kinds.Length; s++)
                _seats[s] = _multi ? TableSeats.For(_kinds[s]) : new Pose(new Vector3(0f, 0f, Far), Quaternion.LookRotation(Vector3.back));
            _defaultSeats = (Pose[])_seats.Clone();
            _dealer = _defaultDealer = TableSeats.For(_multi ? CharacterKind.Dealer : CharacterKind.Veteran);
        }

        /// <summary>
        /// 換座位（真實房間有牆或家具擋住時，GameController.Seating 依 SeatingPlanner 的結果呼叫）。
        /// seats 以座位編號索引（0 號是玩家，忽略）；牌、籌碼、下注、牌堆的位置都跟著新座位算。
        /// </summary>
        public void SetSeats(IReadOnlyList<Pose> seats, Pose dealer)
        {
            for (int s = 1; s < _seats.Length && s < seats.Count; s++) _seats[s] = seats[s];
            _dealer = dealer;
        }

        /// <summary>沒有房間資料時的預設座位（TableSeats）。</summary>
        public Pose DefaultSeatPose(int seat) => _defaultSeats[seat];
        public Pose DefaultDealerSeat => _defaultDealer;

        float Far => Layout.FeltSize.Y;
        public int SeatCount => _kinds.Length;
        public bool IsMultiway => _multi;

        public static Vector3 U(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        // ---- 角色座位（角色根節點在桌緣，身體在桌緣後方） ----
        /// <summary>AI 座位的角色姿勢（根節點在角色面前的桌緣，臉朝牌桌）。</summary>
        public Pose SeatPose(int seat) => _seats[seat];
        public CharacterKind KindOf(int seat) => _kinds[seat];
        /// <summary>第一個 AI（一對一的 Pip）。</summary>
        public Pose OpponentSeat => _seats[1];

        /// <summary>荷官（Dot）：多人時在遠端長邊中央（扶手缺口）；一對一時坐 Pip 右手邊（TableSeats）。</summary>
        public Pose DealerSeat => _dealer;

        // ---- 座位框架 ----
        Vector3 Fwd(int seat) => _seats[seat].rotation * Vector3.forward;
        Vector3 Right(int seat) => _seats[seat].rotation * Vector3.right;
        Vector3 Origin(int seat) => _seats[seat].position;

        /// <summary>
        /// 籌碼堆、下注放在角色的哪一側：離底池遠的那側（免得擠在桌子中間）；
        /// 但那一側的籌碼堆會掉出絨布、跑到下注線前（玩家的區域）或壓到旁邊座位的牌時（換座位後才會發生），改放另一側。
        /// </summary>
        float Side(int seat)
        {
            var o = Origin(seat); var r = Right(seat);
            float away = Vector3.Distance(o + r * 0.15f, PotPile) + 1e-4f >= Vector3.Distance(o - r * 0.15f, PotPile) ? 1f : -1f;
            if (StackFits(seat, away) || !StackFits(seat, -away)) return away;
            return -away;
        }

        bool StackFits(int seat, float side)
        {
            var p = Origin(seat) + Fwd(seat) * StackFwd(seat) + Right(seat) * (StackSide * side);
            var q = new System.Numerics.Vector3(p.x, 0f, p.z);
            if (!Layout.InFelt(q, 0.02f) || Layout.PlayerZone.Contains(q, 0.02f)) return false;
            for (int other = 1; other < _seats.Length; other++)
            {
                if (other == seat) continue;
                var cards = Origin(other) + Fwd(other) * 0.065f;
                if (Vector3.Distance(p, cards) < 0.08f) return false;
                if (NearHands(p, _seats[other])) return false;
            }
            return !NearHands(p, _dealer);
        }

        /// <summary>
        /// 籌碼堆壓到別人放在桌上的手（2026-10-14：側邊的 Pip 往後坐之後，左手正好放在 Rocco 的籌碼堆上）：
        /// 手大約在座位前方 0.10 m、左右各 0.12 m（角色骨架的休息姿勢），籌碼堆中心離它們要 ≥ StackHandClearance。
        /// </summary>
        bool NearHands(Vector3 p, Pose seat)
        {
            var f = seat.rotation * Vector3.forward;
            var r = seat.rotation * Vector3.right;
            for (int k = -1; k <= 1; k += 2)
            {
                var hand = seat.position + f * 0.10f + r * (0.12f * k);
                if (new Vector2(p.x - hand.x, p.z - hand.z).magnitude < StackHandClearance) return true;
            }
            return false;
        }

        /// <summary>籌碼堆中心離別人的手至少多遠（公尺）。</summary>
        public const float StackHandClearance = 0.12f;

        // 一對一沿用 M1 的距離；多人桌比較擠：牌往籌碼那側偏一點，側邊座位的下注偏向玩家，免得撞到對面的人
        // 側邊座位（往後坐了，2026-10-14）的籌碼堆往桌內多放 3 cm：不然靠玩家那側會掉出絨布邊
        float StackFwd(int seat) => !_multi ? 0.08f : IsSideSeat(seat) ? 0.12f : 0.09f;
        float StackSide => _multi ? 0.15f : 0.2f;
        float BetFwd => _multi ? 0.17f : 0.15f;
        float BetSide(int seat) => !_multi ? 0.1f : IsSideSeat(seat) ? 0.06f : 0f;
        float CardShift => _multi ? 0.025f : 0f;
        /// <summary>坐在牌桌左右兩側（不是對面）。</summary>
        bool IsSideSeat(int seat) => Mathf.Abs(Fwd(seat).x) > 0.5f;

        // ---- 玩家 ----
        public Vector3 PlayerCard(int i) => U(i == 0 ? Layout.CardLeftCenter : Layout.CardRightCenter);
        public Vector3 PlayerStack => U(Layout.ChipStackCenter);
        /// <summary>玩家確認後的下注（本街）擺放處：下注線後方一點。</summary>
        public Vector3 PlayerBet => new Vector3(0.07f, 0f, Layout.BetLineZ + 0.07f);
        /// <summary>教學提示的敲桌點（右手自然落下的地方）。</summary>
        public Vector3 KnockSpot => new Vector3(0.3f, 0f, 0.24f);

        // ---- 公共區 ----
        public Vector3 Board(int i) => new Vector3((i - 2) * 0.072f, 0f, Layout.BetLineZ + 0.14f);
        public Vector3 PotPile => U(Layout.PotCenter) + new Vector3(0f, 0f, 0.02f);
        /// <summary>
        /// 牌堆：荷官面前，放在荷官靠牌桌中間那一側
        /// （2026-10-14：放在另一側時離 Vex 伸上桌的手只有 7 cm）。
        /// </summary>
        public Vector3 Deck
        {
            get
            {
                var d = DealerSeat;
                var inward = Vector3.Dot(d.rotation * Vector3.right, PotPile - d.position) >= 0f ? 1f : -1f;
                return d.position + d.rotation * new Vector3(0.07f * inward, 0f, 0.1f);
            }
        }
        /// <summary>玩家棄的牌滑到這裡（荷官前方，避開公共牌那一排）。</summary>
        public Vector3 Muck => new Vector3(0.25f, 0f, Layout.BetLineZ + 0.06f);

        // ---- 各座位 ----
        public Vector3 CardOf(int seat, int i)
        {
            if (seat == 0) return PlayerCard(i);
            return Origin(seat) + Fwd(seat) * 0.065f + Right(seat) * ((i == 0 ? 0.036f : -0.036f) + CardShift * Side(seat));
        }

        public Vector3 StackOf(int seat) => seat == 0 ? PlayerStack : Origin(seat) + Fwd(seat) * StackFwd(seat) + Right(seat) * (StackSide * Side(seat));
        public Vector3 BetOf(int seat) => seat == 0 ? PlayerBet : Origin(seat) + Fwd(seat) * BetFwd + Right(seat) * (BetSide(seat) * Side(seat));
        /// <summary>AI 棄牌：牌往前推一點、偏離籌碼那側。</summary>
        public Vector3 MuckOf(int seat, int i) => CardOf(seat, i) - Right(seat) * (0.05f * Side(seat)) + Fwd(seat) * 0.06f;
        /// <summary>AI 的牌朝向角色（Y 軸角度）。</summary>
        public float CardYaw(int seat) => seat == 0 ? 0f : _seats[seat].rotation.eulerAngles.y;

        // ---- 一對一相容（第一個 AI） ----
        public Vector3 AiCard(int i) => CardOf(1, i);
        public Vector3 AiStack => StackOf(1);
        public Vector3 AiBet => BetOf(1);
        public Vector3 AiMuck(int i) => MuckOf(1, i);

        /// <summary>籌碼落點夾在下注區內（避免掉到桌外或玩家自己的牌上）。與辨識器的撈回判斷共用 TableLayout.BetDisplayPoint。</summary>
        public Vector3 ClampToBetZone(Vector3 p)
        {
            var q = Layout.BetDisplayPoint(new System.Numerics.Vector3(p.x, 0f, p.z));
            return new Vector3(q.X, 0f, q.Z);
        }
    }
}
